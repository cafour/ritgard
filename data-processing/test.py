# Install dependencies if needed:
# pip install sentence-transformers umap-learn matplotlib

from sentence_transformers import SentenceTransformer
import umap.umap_ as umap
import matplotlib.pyplot as plt
import csv

# Example article names
article_names = [
    "Deep Learning for Natural Language Processing",
    "Advances in Cancer Research",
    "Neural Networks for Image Recognition",
    "Quantum Computing Explained",
    "New Therapies for Diabetes",
    "Machine Learning in Finance",
    "The Future of Renewable Energy",
    "Climate Change and Its Impact",
    "AI for Drug Discovery",
    "Solar Power Technology Trends"
]

# 1. Load a pretrained SBERT model
model = SentenceTransformer('all-MiniLM-L6-v2')

issue_names = []

with open('issues.csv', 'r') as csv_file:
    reader = csv.DictReader(csv_file)
    for issue in reader:
        issue_names.append(issue['Title'])

# 2. Encode article titles into embeddings
embeddings = model.encode(issue_names)

# 3. Reduce dimensions to 2D using UMAP
reducer = umap.UMAP(n_neighbors=5, min_dist=0.3, random_state=42)
embeddings_2d = reducer.fit_transform(embeddings)

# 4. Plot the 2D projection
plt.figure(figsize=(10, 7))
plt.scatter(embeddings_2d[:, 0], embeddings_2d[:, 1], c='skyblue', s=70)

# Add article names as labels
for i, name in enumerate(issue_names):
    plt.text(embeddings_2d[i, 0] + 0.02, embeddings_2d[i, 1] + 0.02, name, fontsize=9)

plt.title("Issue Titles Clustered by Topic (SBERT + UMAP)")
plt.show()
