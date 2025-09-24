from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.decomposition import TruncatedSVD
import umap.umap_ as umap
import matplotlib.pyplot as plt
import csv
import hdbscan

# Read issue titles
issue_titles = []
with open('issues.csv', 'r') as csv_file:
    reader = csv.DictReader(csv_file)
    for issue in reader:
        issue_titles.append(issue['Title'])

vectorizer = TfidfVectorizer(stop_words="english")
tfidf_matrix = vectorizer.fit_transform(issue_titles)

# Latent Semantic Indexing (LSI) via TruncatedSVD
n_components = 100  # latent dimensions (tune as needed)
svd = TruncatedSVD(n_components=n_components, random_state=42)
embeddings = svd.fit_transform(tfidf_matrix)

# Cluster with HDBSCAN
clusterer = hdbscan.HDBSCAN(min_cluster_size=2, gen_min_span_tree=True)
labels = clusterer.fit_predict(embeddings)  # cluster in high-dimensional space (better!)

# Reduce dimensions to 2D using UMAP
reducer = umap.UMAP(n_neighbors=5, min_dist=0.3, random_state=42)
embeddings_2d = reducer.fit_transform(embeddings)

# Plot
plt.figure(figsize=(10, 7))

# Use a colormap, but assign gray to noise points (-1)
palette = plt.cm.get_cmap('tab10', len(set(labels)))
colors = [palette(l) if l != -1 else (0.7, 0.7, 0.7, 0.5) for l in labels]

plt.scatter(embeddings_2d[:, 0], embeddings_2d[:, 1], c=colors, s=80, alpha=0.8, edgecolors='k')

# Add labels
# for i, name in enumerate(issue_titles):
#     plt.text(embeddings_2d[i, 0] + 0.02, embeddings_2d[i, 1] + 0.02, name, fontsize=9)

plt.title("Issue Titles (LSI + HDBSCAN + UMAP)")
plt.show()

# # Print cluster assignments
# for name, label in zip(issue_titles, labels):
#     print(f"{name:50} -> Cluster {label}")
