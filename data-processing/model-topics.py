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

from utils import get_now_string, get_plain_text, get_prompt, LineTokenizer

nltk.download("stopwords")
from nltk.corpus import stopwords as nltk_stopwords
import openai
from dotenv import load_dotenv
import os
import datatypes as dt

load_dotenv()

STOP_WORDS = list(set(nltk_stopwords.words("english")).union(set(SKLEARN_STOP_WORDS)))

log = logging.getLogger("ritgard.model-topics")


def get_documents_from_items(items: dict[str, dt.DocumentItem], embed_labels: bool, embed_bodies: bool,
                             embed_comments: bool):
    ids = []
    docs = []
    if items is None or len(items) == 0:
        return ids, docs

    for item in items.values():
        doc = ""
        if embed_labels and item.labels is not None:
            for label in item.labels:
                doc += f"[{label}] "

        doc += get_plain_text(item.title)

        if embed_bodies and item.body is not None:
            doc += "\n\n"
            doc += get_plain_text(item.body)

        if embed_comments and item.comments is not None:
            for comment in item.comments:
                if comment.body is not None and len(comment.body) > 0:
                    doc += "\n\n"
                    doc += get_plain_text(comment.body)

        ids.append(item.id)
        docs.append(doc)

    return ids, docs


def get_documents(data: dt.MiningResult, embed_labels: bool, embed_bodies: bool, embed_comments: bool) -> tuple[
    list[str], list[str]]:
    log.info(
        f"Preparing documents for '{data.repository.owner}/{data.repository.name}'"
    )
    ids = []
    docs = []

    if data.issues is not None:
        issue_ids, issue_docs = get_documents_from_items(data.issues, embed_labels, embed_bodies, embed_comments)
        ids.extend(issue_ids)
        docs.extend(issue_docs)
    if data.pull_requests is not None:
        pr_ids, pr_docs = get_documents_from_items(data.pull_requests, embed_labels, embed_bodies, embed_comments)
        ids.extend(pr_ids)
        docs.extend(pr_docs)
    if data.discussions is not None:
        discussion_ids, discussion_docs = get_documents_from_items(data.discussions, embed_labels, embed_bodies,
                                                                   embed_comments)
        ids.extend(discussion_ids)
        docs.extend(discussion_docs)

    log.info(f"Pre-processed {len(ids)} documents, with maximum length of {max([len(doc) for doc in docs])} characters.")

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


def use_bertopic(
        docs: list[str],
        repository: dt.Repository,
        options: dt.TopicModelingOptions
):
    from sentence_transformers import SentenceTransformer
    import umap.umap_ as umap

    embedding_model_kwargs = {
        "device_map": "auto"
    }
    if options.flash_attention:
        log.info("Using Flash Attention 2")
        embedding_model_kwargs["dtype"] = torch.bfloat16
        embedding_model_kwargs["attn_implementation"] = "flash_attention_2"
    embedding_model = SentenceTransformer(
        options.embed_model,
        model_kwargs=embedding_model_kwargs,
        tokenizer_kwargs={"padding_side": "left"},
    )
    embeddings = embedding_model.encode(docs, show_progress_bar=True, batch_size=options.embed_batch_size)
    umap_model = umap.UMAP(
        n_neighbors=15, n_components=5, min_dist=0.0, metric="cosine", random_state=42
    )
    hdbscan_model = HDBSCAN(
        min_cluster_size=options.min_cluster_size,
        min_samples=options.min_samples,
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
        # "KeyBERT": keybert_model,
        # "POS": PartOfSpeech("en_core_web_sm"),
        # "MMS": MaximalMarginalRelevance(diversity=0.5)
    }
    if options.llm:
        api_key = os.getenv("LLM_API_KEY")
        base_url = os.getenv("LLM_BASE_URL")
        if api_key is None:
            raise RuntimeError("The LLM_API_KEY environment variable is not set.");
        llm_client = openai.OpenAI(api_key=api_key, base_url=base_url, timeout=60)
        # noinspection PyTypeChecker
        # NB: "tetřev hlušec" is a dummy value that should (hopefully) never occur in real output.
        #     I'm not sure why I can't use `null or an empty string...
        representation_model["LLM"] = OpenAI(
            client=llm_client,
            model=options.llm_model,
            system_prompt="You are an assistant that extracts high-level topics from software engineering conversations.",
            prompt=get_prompt(repository.topics),
            generator_kwargs={"stop": "tetřev hlušec"},
            tokenizer=LineTokenizer(),
            doc_length=1
        )
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
    topics, probs = topic_model.fit_transform(docs, embeddings=embeddings)
    # topics = topic_model.reduce_outliers(docs, topics)
    # topics = topic_model.reduce_outliers(docs, topics, probabilities=probs, strategy="probabilities")

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
        f"./out/hierarchy_{repository.name}_{formatted_datetime}.html"
    )

    fig = topic_model.visualize_documents(
        docs,
        topics=topics,
        embeddings=embeddings,
        reduced_embeddings=positions,  # type: ignore
        custom_labels=True,
        hide_annotations=True,
        title=repository.name,
    )
    fig.write_html(f"./out/issues_{repository.name}_{formatted_datetime}.html")
    return topic_model, topics, positions


def write_topics(
        ids: list[str],
        positions: np.ndarray[tuple[int, int], np.dtype[np.float32]],
        topic_model: BERTopic,
        topics: list[int],
        repository: dt.Repository,
        options: dt.TopicModelingOptions
):
    topic_models = {}
    for topic_id in topic_model.topic_representations_.keys():
        # noinspection PyTypeChecker
        full_info: Mapping[str, list[tuple[str, float]]] = topic_model.get_topic(topic_id, full=True)
        representations = {method: [candidate for candidate in candidates if candidate[0] != ""] for
                           (method, candidates) in full_info.items()}
        topic_models[topic_id] = dt.Topic(id=topic_id, representations=representations)

    topic_items = {}
    for doc_id, pos, topic, probs in zip(ids, positions, topics, topic_model.probabilities_):
        doc_probs = {index: probability for (index, probability) in enumerate(probs) if not np.isclose(probability, 0)}
        topic_items[doc_id] = dt.TopicItem(id=doc_id, x=pos[0].item(), y=pos[1].item(), topic_id=topic,
                                           probabilities=doc_probs)

    result = dt.TopicModellingResult(
        name=repository.name,
        owner=repository.owner,
        topics=topic_models,
        items=topic_items
    )

    if options.output is None:
        options.output = f"./out/topics_{repository.name}_{get_now_string()}.json"
        log.info(f"Automatically set output path to '{options.output}'")
    output = Path(options.output)
    result.topic_modelling_options = options
    json = result.model_dump_json()

    if output.parent is not None:
        output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf8") as json_file:
        log.info(f"Writing data processing result to '{output}'")
        json_file.write(json)


def main():
    logging.basicConfig(level=logging.INFO)

    args_parser = argparse.ArgumentParser(prog="ritgard")
    args_parser.add_argument("data_path")
    args_parser.add_argument("--llm", action="store_true")
    args_parser.add_argument("--embed-labels", action="store_true")
    args_parser.add_argument("--embed-bodies", action="store_true")
    args_parser.add_argument("--embed-comments", action="store_true")
    args_parser.add_argument("--embed-batch-size", default=16, type=int)
    args_parser.add_argument("--flash-attention", action="store_true")
    args_parser.add_argument("--llm-model", default="gpt-oss-120b")
    args_parser.add_argument("--embed-model", default="Qwen/Qwen3-Embedding-0.6B")
    args_parser.add_argument("--min-cluster-size", default=5, type=int)
    args_parser.add_argument("--min-samples", default=3, type=int)
    args_parser.add_argument("--output", default=None)
    options = dt.TopicModelingOptions(**vars(args_parser.parse_args()))

    Path("./out").mkdir(exist_ok=True)

    data = dt.read_mining_result(options.data_path)
    ids, docs = get_documents(data, options.embed_labels, options.embed_bodies, options.embed_comments)

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

    topic_model, topics, positions = use_bertopic(
        docs=docs,
        repository=data.repository,
        options=options
    )
    write_topics(
        ids=ids,
        positions=positions,
        topic_model=topic_model,
        topics=topics,
        repository=data.repository,
        options=options
    )


if __name__ == "__main__":
    main()
