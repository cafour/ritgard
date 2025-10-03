from sklearn.manifold import MDS
from sklearn.metrics import pairwise_distances
from sklearn.feature_extraction.text import CountVectorizer
from sklearn.neighbors import NearestNeighbors
from sentence_transformers import SentenceTransformer
import umap.umap_ as umap
import csv
import json
import numpy as np
from datetime import datetime
from bertopic import BERTopic
from bertopic.representation import KeyBERTInspired
from bertopic.representation import PartOfSpeech
from bertopic.representation import MaximalMarginalRelevance
from bertopic.vectorizers import ClassTfidfTransformer
from hdbscan import HDBSCAN
from scipy.cluster import hierarchy as sch
import argparse
from pathlib import Path
import torch


def read_issues(filename: str) -> tuple[list[str], list[str]]:
    print("Reading " + filename)
    ids = []
    docs = []
    with open(filename, "r", encoding="utf8") as json_file:
        data = json.load(json_file)
        for issue_id in data["Issues"]:
            issue = data["Issues"][issue_id]
            ids.append(issue["Id"])
            doc = ""
            if "Labels" in issue and issue["Labels"] != None:
                for label in issue["Labels"]:
                    doc += f"[{label}] "
            doc += issue["Title"]
            # doc = doc.lower().replace(project_name, "")
            docs.append(doc)
    return (ids, docs)


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


def use_bertopic(docs: list[str], project_name):
    embedding_model = SentenceTransformer("Qwen/Qwen3-Embedding-0.6B")
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
    ctfidf_model = ClassTfidfTransformer(reduce_frequent_words=True)
    keybert_model = KeyBERTInspired()

    representation_model = {
        "KeyBERT": keybert_model,
        "POS": PartOfSpeech("en_core_web_sm"),
        "MMS": MaximalMarginalRelevance(diversity=0.5),
    }
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
    _topics, probs = topic_model.fit_transform(docs, embeddings=embeddings)
    reduction_umap = umap.UMAP(
        n_neighbors=10, n_components=2, min_dist=0.0, metric="cosine", random_state=42
    )
    reduced_embeddings: np.ndarray[tuple[int, int], np.dtype[np.float32]] = (
        reduction_umap.fit_transform(embeddings)
    )  # type: ignore
    # reduced_embeddings = reduce_mds(topic_model.umap_model.transform(embeddings))

    keybert_labels = {
        topic: "; ".join(list(zip(*values))[0][:3])
        for topic, values in topic_model.topic_aspects_["KeyBERT"].items()
    }
    pos_labels = {
        topic: " ".join(list(zip(*values))[0][:3])
        for topic, values in topic_model.topic_aspects_["POS"].items()
    }
    mms_labels = {
        topic: " ".join(list(zip(*values))[0][:3])
        for topic, values in topic_model.topic_aspects_["MMS"].items()
    }
    combined_labels = {
        topic: keybert_labels[topic]
        + " | "
        + pos_labels[topic]
        + " | "
        + mms_labels[topic]
        for topic in keybert_labels.keys()
    }
    topic_model.set_topic_labels(pos_labels)

    formatted_datetime = datetime.now().strftime("%d_%b_%Y_%H_%M_%S")

    linkage_function = lambda x: sch.linkage(x, "single", optimal_ordering=True)
    hierarchical_topics = topic_model.hierarchical_topics(
        docs, linkage_function=linkage_function
    )
    fig_hierarchy = topic_model.visualize_hierarchy(
        hierarchical_topics=hierarchical_topics
    )
    fig_hierarchy.write_html(
        f"./out/hierarchy_{project_name}_{formatted_datetime}.html"
    )

    fig = topic_model.visualize_documents(
        docs,
        embeddings=embeddings,
        reduced_embeddings=reduced_embeddings,  # type: ignore
        custom_labels=True,
        hide_annotations=True,
        title=project_name,
    )
    fig.write_html(f"./out/issues_{project_name}_{formatted_datetime}.html")
    topics = [combined_labels[topic] if topic != -1 else "" for topic in topic_model.topics_]  # type: ignore
    return (reduced_embeddings, topics)


def write_topics(
    ids: list[str],
    positions: np.ndarray[tuple[int, int], np.dtype[np.float32]],
    topics: list[str],
    neighbors: np.ndarray[tuple[int], np.dtype[np.int32]],
    neighbor_distances: np.ndarray[tuple[int], np.dtype[np.float32]],
    project_name: str,
):
    formatted_datetime = datetime.now().strftime("%d_%b_%Y_%H_%M_%S")
    with open(
        f"./out/topics_{project_name}_{formatted_datetime}.csv",
        "w",
        encoding="utf8",
        newline="",
    ) as file:
        writer = csv.DictWriter(
            file,
            fieldnames=[
                "Id",
                "X",
                "Y",
                "Topic",
                "NearestNeighbor",
                "NearestNeighborDistance",
            ],
        )
        writer.writeheader()
        for id, pos, topic, nn, nn_dist in zip(
            ids, positions, topics, neighbors, neighbor_distances
        ):
            writer.writerow(
                {
                    "Id": id,
                    "X": pos[0],
                    "Y": pos[1],
                    "Topic": topic,
                    "NearestNeighbor": nn,
                    "NearestNeighborDistance": nn_dist,
                }
            )


def get_nearest_neighbor_distances(
    positions: np.ndarray[tuple[int, int], np.dtype[np.float32]],
):
    neighbors = NearestNeighbors(n_neighbors=2).fit(positions)
    distances, indices = neighbors.kneighbors(positions, n_neighbors=2)
    return (distances[0:, 1], indices[0:, 1])


def main():
    args_parser = argparse.ArgumentParser(prog="ritgard")
    args_parser.add_argument("project_name")
    args_parser.add_argument("data_path")
    args = args_parser.parse_args()

    Path("./out").mkdir(exist_ok=True)

    project_name: str = args.project_name
    data_path: str = args.data_path
    ids, docs = read_issues(data_path)

    current_gpu = -1
    current_gpu_free_memory = 0
    for i in range(0, torch.cuda.device_count()):
        memory = torch.cuda.mem_get_info(i)
        print(
            f"[GPU {i}] {torch.cuda.get_device_name(i)}: {memory[0]} free / {memory[1]} total"
        )
        free_memory = memory[0] / memory[1]
        if free_memory > current_gpu_free_memory:
            current_gpu = i
            current_gpu_free_memory = free_memory
    if current_gpu == -1:
        raise RuntimeError("No CUDA device detected")
    torch.cuda.set_device(current_gpu)
    torch.cuda.empty_cache()
    print(f"Selected GPU {current_gpu}")

    positions, topics = use_bertopic(docs, project_name)
    distances, indices = get_nearest_neighbor_distances(positions)
    write_topics(ids, positions, topics, indices, distances, project_name)

    min_distance = np.min(distances)
    max_distance = np.max(distances)
    avg_distance = np.mean(distances)
    med_distance = np.median(distances)

    print(f"Min nearest neighbor distance: {min_distance}")
    print(f"Max nearest neighbor distance: {max_distance}")
    print(f"Average nearest neighbor distance: {avg_distance}")
    print(f"Median nearest neighbor distance: {med_distance}")


if __name__ == "__main__":
    main()
