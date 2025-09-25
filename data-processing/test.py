from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.decomposition import TruncatedSVD
from sklearn.manifold import MDS
from sklearn.metrics import pairwise_distances
from sklearn.preprocessing import normalize
from sentence_transformers import SentenceTransformer, SparseEncoder
import umap.umap_ as umap
import matplotlib.pyplot as plt
import csv
import hdbscan
import numpy as np
import datetime

def get_top_keywords(tfidf_matrix, labels, feature_names, top_n=5):
    clusters = {}
    for cluster_id in set(labels):
        if cluster_id == -1:
            continue  # skip noise
        cluster_docs = tfidf_matrix[labels == cluster_id]
        mean_tfidf = cluster_docs.mean(axis=0).A1
        top_indices = mean_tfidf.argsort()[::-1][:top_n]
        clusters[cluster_id] = feature_names[top_indices]
    return clusters

def read_issues(filename: str) -> list[str]:
    print('Reading ' + filename)
    issue_titles = []
    with open(filename, 'r', encoding="utf8") as csv_file:
        reader = csv.DictReader(csv_file)
        for issue in reader:
            issue_titles.append(issue['Title'])
    return issue_titles

def embed_sbert(strings: list[str]):
    model = SentenceTransformer('all-mpnet-base-v2')
    embeddings = model.encode(strings)
    return embeddings

def embed_splade(strings: list[str]):
    model = SparseEncoder("naver/splade-cocondenser-ensembledistil")
    embeddings = model.encode(strings)
    dense = embeddings.to_dense().numpy()
    svd = TruncatedSVD(n_components=300, random_state=42)
    return svd.fit_transform(dense)

def embed_lsi(strings: list[str]):
    vectorizer = TfidfVectorizer(stop_words="english")
    tfidf_matrix = vectorizer.fit_transform(strings)
    # feature_names = np.array(vectorizer.get_feature_names_out())
    n_components = 100  # latent dimensions (tune as needed)
    svd = TruncatedSVD(n_components=n_components, random_state=42)
    embeddings = svd.fit_transform(tfidf_matrix)
    return embeddings

def reduce_mds(embeddings):
    cosine_dist = pairwise_distances(embeddings, metric="cosine")
    mds = MDS(n_components=2, random_state=42, n_init=4, max_iter=300, dissimilarity="precomputed")
    embeddings_2d = mds.fit_transform(cosine_dist)
    return embeddings_2d

def reduce_umap(embeddings):
    reducer = umap.UMAP(n_neighbors=5, min_dist=0.3, random_state=42)
    embeddings_2d = reducer.fit_transform(embeddings)
    return embeddings_2d

def cluster_hdbscan(embeddings):
    clusterer = hdbscan.HDBSCAN(min_cluster_size=2, gen_min_span_tree=True)
    labels = clusterer.fit_predict(embeddings)
    # keywords_per_cluster = get_top_keywords(tfidf_matrix, labels, feature_names, top_n=5)
    return labels

def show_plot(titles: list[str], labels, positions, title):
    fig, ax = plt.subplots()

    # Use a colormap, but assign gray to noise points (-1)
    palette = plt.cm.get_cmap('tab10', len(set(labels)))
    colors = [palette(l) if l != -1 else (0.7, 0.7, 0.7, 0.5) for l in labels]

    scatter = plt.scatter(positions[:, 0], positions[:, 1], c=colors, s=80, alpha=0.8, edgecolors='k')

    annot = ax.annotate(
        "",
        xy=(0,0),
        xytext=(20,20),
        textcoords="offset points",
        arrowprops=dict(arrowstyle="->")
    )
    annot.set_visible(False)

    plt.title(title)

    def hover(event):
        vis = annot.get_visible()
        if event.inaxes == ax:
            contains, index = scatter.contains(event)
            if contains:
                pos = scatter.get_offsets()[index["ind"][0]]
                annot.xy = pos
                annot.set_text(titles[index["ind"][0]])
                annot.set_visible(True)
                fig.canvas.draw_idle()
            else:
                if vis:
                    annot.set_visible(False)
                    fig.canvas.draw_idle()

    fig.canvas.mpl_connect("motion_notify_event", hover)
    formatted_datetime = datetime.now().strftime("%d_%b_%Y_%H_%M_%S")
    plt.savefig(
        f"./issues_{formatted_datetime}.png",
        dpi=300
    )
    plt.show()

# Print cluster assignments with keywords
# for cluster_id, keywords in keywords_per_cluster.items():
#     print(f"Cluster {cluster_id}: {', '.join(keywords)}")
#     for name, label in zip(issue_titles, labels):
#         if label == cluster_id:
#             print(f"   - {name}")

issues_filename = 'dotvvm.csv'
issue_titles = read_issues(issues_filename)
embeddings = embed_sbert(issue_titles)
positions = reduce_mds(embeddings)
labels = cluster_hdbscan(embeddings)
show_plot(issue_titles, labels, positions, "DotVVM Issues (SPLADE + MDS + HDBSCAN)")
