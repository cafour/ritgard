from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.decomposition import TruncatedSVD
from sklearn.manifold import MDS
from sklearn.metrics import pairwise_distances
import umap.umap_ as umap
import matplotlib.pyplot as plt
import csv
import hdbscan
import numpy as np

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


# Read issue titles
issue_titles = []
with open('issues.csv', 'r') as csv_file:
    reader = csv.DictReader(csv_file)
    for issue in reader:
        issue_titles.append(issue['Title'])

vectorizer = TfidfVectorizer(stop_words="english")
tfidf_matrix = vectorizer.fit_transform(issue_titles)
feature_names = np.array(vectorizer.get_feature_names_out())

# Latent Semantic Indexing (LSI) via TruncatedSVD
n_components = 100  # latent dimensions (tune as needed)
svd = TruncatedSVD(n_components=n_components, random_state=42)
embeddings = svd.fit_transform(tfidf_matrix)

# Reduce dimensions to 2D
cosine_dist = pairwise_distances(embeddings, metric="cosine")
mds = MDS(n_components=2, random_state=42, n_init=4, max_iter=300, dissimilarity="precomputed")
embeddings_2d = mds.fit_transform(cosine_dist)

# Cluster with HDBSCAN
clusterer = hdbscan.HDBSCAN(min_cluster_size=2, gen_min_span_tree=True)
labels = clusterer.fit_predict(embeddings)
keywords_per_cluster = get_top_keywords(tfidf_matrix, labels, feature_names, top_n=5)

# Plot
plt.figure(figsize=(10, 7))

# Use a colormap, but assign gray to noise points (-1)
palette = plt.cm.get_cmap('tab10', len(set(labels)))
colors = [palette(l) if l != -1 else (0.7, 0.7, 0.7, 0.5) for l in labels]

plt.scatter(embeddings_2d[:, 0], embeddings_2d[:, 1], c=colors, s=80, alpha=0.8, edgecolors='k')

plt.title("Issue Titles (LSI + HDBSCAN + MDS)")
plt.show()

# Print cluster assignments with keywords
for cluster_id, keywords in keywords_per_cluster.items():
    print(f"Cluster {cluster_id}: {', '.join(keywords)}")
    for name, label in zip(issue_titles, labels):
        if label == cluster_id:
            print(f"   - {name}")
