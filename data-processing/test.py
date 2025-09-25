from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.decomposition import TruncatedSVD
from sklearn.manifold import MDS
from sklearn.metrics import pairwise_distances
from sklearn.preprocessing import normalize
from sklearn.feature_extraction.text import CountVectorizer
# from sklearn.cluster import HDBSCAN
from sentence_transformers import SentenceTransformer, SparseEncoder
import umap.umap_ as umap
import matplotlib.pyplot as plt
import csv
import numpy as np
from datetime import datetime
from bertopic import BERTopic
from bertopic.representation import KeyBERTInspired
from bertopic.vectorizers import ClassTfidfTransformer
from hdbscan import HDBSCAN


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


def read_issues(filename: str, project_name: str) -> list[str]:
    print("Reading " + filename)
    docs = []
    with open(filename, "r", encoding="utf8") as csv_file:
        reader = csv.DictReader(csv_file)
        for issue in reader:
            doc = ""
            if issue["Labels"] != None and issue["Labels"] != "":
                labels = sorted(issue["Labels"].lower().split(";")) or []
                for label in labels:
                    doc += f"[{label}] "
            doc += issue["Title"]
            if issue["Body"] != None and issue["Body"] != "":
                doc += " " + issue["Body"]
            doc = doc.lower().replace(project_name, "")
            docs.append(doc)
    return docs


def embed_sbert(strings: list[str]):
    model = SentenceTransformer("all-mpnet-base-v2")
    embeddings = model.encode(strings)
    return embeddings


def embed_jina(strings: list[str]):
    model = SentenceTransformer("jinaai/jina-embeddings-v4", trust_remote_code=True)
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
    mds = MDS(
        n_components=2,
        random_state=42,
        n_init=4,
        max_iter=300,
        dissimilarity="precomputed",
    )
    embeddings_2d = mds.fit_transform(cosine_dist)
    return embeddings_2d


def reduce_umap(embeddings):
    reducer = umap.UMAP(n_neighbors=5, min_dist=0.3, random_state=42)
    embeddings_2d = reducer.fit_transform(embeddings)
    return embeddings_2d


def cluster_hdbscan(embeddings):
    clusterer = HDBSCAN(
        min_cluster_size=3,
        metric="cosine",
    )
    labels = clusterer.fit_predict(embeddings)
    # keywords_per_cluster = get_top_keywords(tfidf_matrix, labels, feature_names, top_n=5)
    return labels


def show_plot(titles: list[str], labels, positions, title):
    fig, ax = plt.subplots()

    # Use a colormap, but assign gray to noise points (-1)
    palette = plt.cm.get_cmap("tab10", len(set(labels)))
    colors = [palette(l) if l != -1 else (0.7, 0.7, 0.7, 0.5) for l in labels]

    scatter = plt.scatter(
        positions[:, 0], positions[:, 1], c=colors, s=80, alpha=0.8, edgecolors="k"
    )

    annot = ax.annotate(
        "",
        xy=(0, 0),
        xytext=(20, 20),
        textcoords="offset points",
        arrowprops=dict(arrowstyle="->"),
    )
    annot.set_visible(False)

    plt.title(title)

    def hover(event):
        vis = annot.get_visible()
        if event.inaxes == ax:
            contains, index = scatter.contains(event)
            if contains:
                pos = scatter.get_offsets()[index["ind"][0]] # type: ignore
                annot.xy = pos # type: ignore
                annot.set_text(titles[index["ind"][0]])
                annot.set_visible(True)
                fig.canvas.draw_idle()
            else:
                if vis:
                    annot.set_visible(False)
                    fig.canvas.draw_idle()

    fig.canvas.mpl_connect("motion_notify_event", hover)
    formatted_datetime = datetime.now().strftime("%d_%b_%Y_%H_%M_%S")
    plt.savefig(f"./issues_{formatted_datetime}.png", dpi=300)
    plt.show()


def use_bertopic(docs: list[str], project_name):
    embedding_model = SentenceTransformer("all-MiniLM-L6-v2")
    embeddings = embedding_model.encode(docs, show_progress_bar=True)
    umap_model = umap.UMAP(
        n_neighbors=15, n_components=5, min_dist=0.0, metric="cosine", random_state=42
    )
    hdbscan_model = HDBSCAN(
        min_cluster_size=3,
        metric="euclidean",
        cluster_selection_method="eom",
        prediction_data=True,
    )
    vectorizer_model = CountVectorizer(
        stop_words="english", min_df=2, ngram_range=(1, 2)
    )
    ctfidf_model = ClassTfidfTransformer()
    keybert_model = KeyBERTInspired()
    representation_model = {"KeyBERT": keybert_model}
    topic_model = BERTopic(
        embedding_model=embedding_model,
        umap_model=umap_model,
        hdbscan_model=hdbscan_model,
        vectorizer_model=vectorizer_model,
        ctfidf_model=ctfidf_model,
        representation_model=representation_model,  # type: ignore
        top_n_words=10,
        verbose=True,
        # calculate_probabilities=True
    )
    topic_model.fit(docs, embeddings=embeddings)
    reduction_umap = umap.UMAP(
        n_neighbors=10, n_components=2, min_dist=0.0, metric="cosine", random_state=42
    )
    reduced_embeddings = reduction_umap.fit_transform(embeddings)

    keybert_topic_labels = {
        topic: list(zip(*values))[0][0]
        for topic, values in topic_model.topic_aspects_["KeyBERT"].items()
    }
    topic_model.set_topic_labels(keybert_topic_labels)

    # new_topics = topic_model.reduce_outliers(
    #     titles, topics, embeddings=embeddings, strategy="embeddings"
    # )
    # topic_model.update_topics(titles, topics=new_topics)

    # title_topics, probs = topic_model.transform(titles, embeddings=title_embeddings)
    fig = topic_model.visualize_documents(
        docs,
        embeddings=embeddings,
        reduced_embeddings=reduced_embeddings,  # type: ignore
        custom_labels=True,
        hide_annotations=True,
        title=project_name,
    )
    formatted_datetime = datetime.now().strftime("%d_%b_%Y_%H_%M_%S")
    fig.write_html(f"./issues_{project_name}_{formatted_datetime}.html")


# Print cluster assignments with keywords
# for cluster_id, keywords in keywords_per_cluster.items():
#     print(f"Cluster {cluster_id}: {', '.join(keywords)}")
#     for name, label in zip(issue_titles, labels):
#         if label == cluster_id:
#             print(f"   - {name}")

project_name = "lume"
issues_filename = "lume.csv"
docs = read_issues(issues_filename, project_name)
# embeddings = embed_sbert(issue_titles)
# positions = reduce_umap(embeddings)
# labels = cluster_hdbscan(embeddings)
# show_plot(issue_titles, labels, positions, "DotVVM Issues (SBERT + UMAP + HDBSCAN)")
use_bertopic(docs, project_name)
