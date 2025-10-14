from typing import Mapping
from sklearn.manifold import MDS
from sklearn.metrics import pairwise_distances
from sklearn.feature_extraction.text import CountVectorizer
import numpy as np
from bertopic import BERTopic
from bertopic.representation import KeyBERTInspired
from bertopic.representation import PartOfSpeech
from bertopic.representation import MaximalMarginalRelevance
from bertopic.representation import OpenAI
from bertopic.vectorizers import ClassTfidfTransformer
from hdbscan import HDBSCAN
from scipy.cluster import hierarchy as sch
import argparse
from pathlib import Path
import torch
import logging
from sklearn.feature_extraction.text import ENGLISH_STOP_WORDS as SKLEARN_STOP_WORDS
import nltk

from utils import get_now_string

nltk.download("stopwords")
from nltk.corpus import stopwords as nltk_stopwords
import openai
from dotenv import load_dotenv
import os
import datatypes as dt

load_dotenv()

STOP_WORDS = list(set(nltk_stopwords.words("english")).union(set(SKLEARN_STOP_WORDS)))

log = logging.getLogger("ritgard.model-topics")


def get_documents_from_items(items: dict[str, dt.DocumentItem], embed_labels: bool, embed_bodies: bool):
    ids = []
    docs = []
    if items is None or len(items) == 0:
        return ids, docs

    for item in items.values():
        doc = ""
        if embed_labels and item.labels is not None:
            for label in item.labels:
                doc += f"[{label}] "

        doc += item.title

        if embed_bodies and (item.plain_text is not None or item.body is not None):
            doc += "\n\n"
            doc += item.plain_text or item.body

        ids.append(item.id)
        docs.append(doc)

    return ids, docs

def get_documents(data: dt.MiningResult, embed_labels: bool, embed_bodies: bool) -> tuple[list[str], list[str]]:
    log.info(
        f"Preparing documents for '{data.repository.owner}/{data.repository.name}'"
    )
    ids = []
    docs = []

    if data.issues is not None:
        issue_ids, issue_docs = get_documents_from_items(data.issues, embed_labels, embed_bodies)
        ids.extend(issue_ids)
        docs.extend(issue_docs)
    if data.pull_requests is not None:
        pr_ids, pr_docs = get_documents_from_items(data.pull_requests, embed_labels, embed_bodies)
        ids.extend(pr_ids)
        docs.extend(pr_docs)
    if data.discussions is not None:
        discussion_ids, discussion_docs = get_documents_from_items(data.discussions, embed_labels, embed_bodies)
        ids.extend(discussion_ids)
        docs.extend(discussion_docs)

    return ids, docs

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


def use_bertopic(docs: list[str], project_name, use_metacentrum: bool):
    from sentence_transformers import SentenceTransformer
    import umap.umap_ as umap

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
        stop_words=STOP_WORDS, ngram_range=(1, 1)
    )
    ctfidf_model = ClassTfidfTransformer(reduce_frequent_words=True)
    keybert_model = KeyBERTInspired()

    representation_model = {
        "KeyBERT": keybert_model,
        "POS": PartOfSpeech("en_core_web_sm"),
        "MMS": MaximalMarginalRelevance(diversity=0.5)
    }
    if use_metacentrum:
        api_key = os.getenv("METACENTRUM_API_KEY")
        if api_key is None:
            raise RuntimeError("The METACENTRUM_API_KEY environment variable is not set.");
        llm_client = openai.OpenAI(api_key=api_key, base_url='https://chat.ai.e-infra.cz/api', timeout=60)
        # noinspection PyTypeChecker
        # NB: "tetřev hlušec" is a dummy value that should (hopefully) never occur in real output.
        #     I'm not sure why I can't use `null or an empty string...
        representation_model["LLM"] = OpenAI(client=llm_client, model="qwen3-coder",
                                             generator_kwargs={"stop": "tetřev hlušec"})
    topic_model = BERTopic(
        embedding_model=embedding_model,
        umap_model=umap_model,
        hdbscan_model=hdbscan_model,
        vectorizer_model=vectorizer_model,
        ctfidf_model=ctfidf_model,
        representation_model=representation_model,  # type: ignore
        top_n_words=10,
        verbose=True,
        calculate_probabilities=True,
    )
    topic_model.fit_transform(docs, embeddings=embeddings)
    reduction_umap = umap.UMAP(
        n_neighbors=10, n_components=2, min_dist=0.0, metric="cosine", random_state=42
    )
    positions: np.ndarray[tuple[int, int], np.dtype[np.float32]] = (
        reduction_umap.fit_transform(embeddings)
    )

    if "LLM" in topic_model.topic_aspects_:
        llm_labels = {
            topic: values[0][0].strip()
            for topic, values in topic_model.topic_aspects_["LLM"].items()
        }
        topic_model.set_topic_labels(llm_labels)

    linkage_function = lambda x: sch.linkage(x, "single", optimal_ordering=True)
    hierarchical_topics = topic_model.hierarchical_topics(
        docs, linkage_function=linkage_function
    )
    fig_hierarchy = topic_model.visualize_hierarchy(
        hierarchical_topics=hierarchical_topics
    )
    formatted_datetime = get_now_string()
    fig_hierarchy.write_html(
        f"./out/hierarchy_{project_name}_{formatted_datetime}.html"
    )

    fig = topic_model.visualize_documents(
        docs,
        embeddings=embeddings,
        reduced_embeddings=positions,  # type: ignore
        custom_labels=True,
        hide_annotations=True,
        title=project_name,
    )
    fig.write_html(f"./out/issues_{project_name}_{formatted_datetime}.html")
    return topic_model, positions


def write_topics(
        ids: list[str],
        positions: np.ndarray[tuple[int, int], np.dtype[np.float64]],
        topic_model: BERTopic,
        project_name: str,
):
    topics = {}
    for topic_id in topic_model.topic_representations_.keys():
        # noinspection PyTypeChecker
        full_info: Mapping[str, list[tuple[str, float]]] = topic_model.get_topic(topic_id, full=True)
        representations = {method: [candidate for candidate in candidates if candidate[0] != ""] for
                           (method, candidates) in full_info.items()}
        topics[topic_id] = dt.Topic(id=topic_id, representations=representations)

    topic_items = {}
    for doc_id, pos, topic, probs in zip(ids, positions, topic_model.topics_, topic_model.probabilities_):
        doc_probs = {index: probability for (index, probability) in enumerate(probs) if not np.isclose(probability, 0)}
        topic_items[doc_id] = dt.TopicItem(id=doc_id, x=pos[0].item(), y=pos[1].item(), topic_id=topic,
                                           probabilities=doc_probs)

    result = dt.TopicModellingResult(
        topics=topics,
        items=topic_items
    )
    json = result.model_dump_json()

    formatted_datetime = get_now_string()
    out_path = f"./out/topics_{project_name}_{formatted_datetime}.json"
    log.info(f"Writing data processing result to '{out_path}'")
    with open(out_path, "w", encoding="utf8") as json_file:
        json_file.write(json)


def main():
    logging.basicConfig(level=logging.INFO)

    args_parser = argparse.ArgumentParser(prog="ritgard")
    args_parser.add_argument("data_path")
    args_parser.add_argument("--llm", action="store_true")
    args_parser.add_argument("--embed-labels", action="store_true")
    args_parser.add_argument("--embed-bodies", action="store_true")
    args = args_parser.parse_args()

    Path("./out").mkdir(exist_ok=True)

    data = dt.read_mining_result(args.data_path)
    project_name = data.repository.name
    ids, docs = get_documents(data, args.embed_labels, args.embed_bodies)

    current_gpu = -1
    current_gpu_free_memory = 0
    for i in range(0, torch.cuda.device_count()):
        memory = torch.cuda.mem_get_info(i)
        log.info(
            f"[GPU {i}] {torch.cuda.get_device_name(i)}: {memory[0]} free / {memory[1]} total"
        )
        free_memory = memory[0] / memory[1]
        if free_memory > current_gpu_free_memory:
            current_gpu = i
            current_gpu_free_memory = free_memory
    if current_gpu == -1:
        log.warning("No CUDA device detected")
    else:
        torch.cuda.set_device(current_gpu)
        torch.cuda.empty_cache()
        log.info(f"Selected GPU {current_gpu}")

    topic_model, positions = use_bertopic(docs, project_name, use_metacentrum=args.llm)
    write_topics(ids, positions, topic_model, project_name)


if __name__ == "__main__":
    main()
