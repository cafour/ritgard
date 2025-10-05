from typing import Mapping
from sklearn.manifold import MDS
from sklearn.metrics import pairwise_distances
from sklearn.feature_extraction.text import CountVectorizer
from sklearn.neighbors import NearestNeighbors
import numpy as np
from datetime import datetime
from bertopic import BERTopic
from bertopic.representation import KeyBERTInspired
from bertopic.representation import PartOfSpeech
from bertopic.representation import MaximalMarginalRelevance
from bertopic.representation import TextGeneration
from bertopic.vectorizers import ClassTfidfTransformer
from hdbscan import HDBSCAN
from scipy.cluster import hierarchy as sch
import argparse
from pathlib import Path
import torch
import logging
from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_pascal
from sklearn.feature_extraction.text import ENGLISH_STOP_WORDS as SKLEARN_STOP_WORDS
import nltk
nltk.download("stopwords")
from nltk.corpus import stopwords as nltk_stopwords

STOP_WORDS = list(set(nltk_stopwords.words("english")).union(set(SKLEARN_STOP_WORDS)))

log = logging.getLogger("data-processing")


class DocumentItemComment(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    body: str | None = None
    plain_text: str | None = None


class DocumentItem(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    id: int
    title: str
    labels: list[str] | None = None
    body: str | None = None
    plain_text: str | None = None
    comments: list[DocumentItemComment] | None = None


class Repository(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    id: int
    owner: str
    name: str


class MiningResult(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    issues: dict[int, DocumentItem] | None = None
    pull_requests: dict[int, DocumentItem] | None = None
    repository: Repository


class Topic(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    representations: dict[str, list[tuple[str, float]]]


class TopicItem(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    id: int
    x: float
    y: float
    topic_id: int


class TopicModellingResult(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    topics: dict[int, Topic]
    items: dict[int, TopicItem]


def read_mining_result(filename: str) -> MiningResult:
    log.info(f"Reading data from '{filename}'")
    with open(filename, "r", encoding="utf8") as json_file:
        json = json_file.read()
        return MiningResult.model_validate_json(json)


def get_documents(data: MiningResult) -> tuple[list[int], list[str]]:
    log.info(
        f"Preparing documents for '{data.repository.owner}/{data.repository.name}'"
    )
    ids = []
    docs = []
    if data.issues is not None:
        for issue in data.issues.values():
            ids.append(issue.id)
            doc = ""
            if issue.labels != None:
                for label in issue.labels:
                    doc += f"[{label}] "
            doc += issue.title
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


def prepare_llm():
    import transformers

    model_id = "meta-llama/Llama-2-7b-chat-hf"
    bnb_config = transformers.BitsAndBytesConfig(
        load_in_4bit=True,  # 4-bit quantization
        bnb_4bit_quant_type="nf4",  # Normalized float 4
        bnb_4bit_use_double_quant=True,  # Second quantization after the first
        bnb_4bit_compute_dtype=torch.bfloat16,  # Computation type
    )
    tokenizer = transformers.AutoTokenizer.from_pretrained(model_id)
    model = transformers.AutoModelForCausalLM.from_pretrained(
        model_id,
        trust_remote_code=True,
        quantization_config=bnb_config,
        device_map="auto",
    )
    model.eval()
    generator = transformers.pipeline(
        model=model,
        tokenizer=tokenizer,
        task="text-generation",
        temperature=0.1,
        max_new_tokens=500,
        repetition_penalty=1.1,
    )

    system_prompt = """
    <s>[INST] <<SYS>>
    You are a helpful, respectful and honest assistant for labeling topics of GitHub issues.
    <</SYS>>
    """

    example_prompt = """
    I have a topic that contains the following documents:
    - Traditional diets in most cultures were primarily plant-based with a little meat on top, but with the rise of industrial style meat production and factory farming, meat has become a staple food.
    - Meat, but especially beef, is the word food in terms of emissions.
    - Eating meat doesn't make you a bad person, not eating meat doesn't make you a good one.

    The topic is described by the following keywords: 'meat, beef, eat, eating, emissions, steak, food, health, processed, chicken'.

    Based on the information about the topic above, please create a short label of this topic. Make sure you to only return the label and nothing more. Don't use the word 'issue' as it's redundant.

    [/INST] Environmental impacts of eating meat
    """

    main_prompt = """
    [INST]
    I have a topic that contains the following documents:
    [DOCUMENTS]

    The topic is described by the following keywords: '[KEYWORDS]'.

    Based on the information about the topic above, please create a short label of this topic. Make sure you to only return the label and nothing more. Don't use the word 'issue' as it's redundant.
    [/INST]
    """

    prompt = system_prompt + example_prompt + main_prompt
    return generator, prompt


def use_bertopic(docs: list[str], project_name):
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

    # llm, prompt = prepare_llm()

    representation_model = {
        "KeyBERT": keybert_model,
        "POS": PartOfSpeech("en_core_web_sm"),
        "MMS": MaximalMarginalRelevance(diversity=0.5),
        # "LLM": TextGeneration(llm, prompt=prompt),
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
    return topic_model, reduced_embeddings


def write_topics(
        ids: list[int],
        positions: np.ndarray[tuple[int, int], np.dtype[np.float64]],
        topic_model: BERTopic,
        neighbors: np.ndarray[tuple[int], np.dtype[np.int32]],
        neighbor_distances: np.ndarray[tuple[int], np.dtype[np.float32]],
        project_name: str,
):
    topics = {}
    for topic_id in topic_model.topic_representations_.keys():
        # noinspection PyTypeChecker
        full_info: Mapping[str, list[tuple[str, float]]] = topic_model.get_topic(topic_id, full=True)
        representations = {method: [candidate for candidate in candidates if candidate[0] != ""] for
                           (method, candidates) in full_info.items()}
        topics[topic_id] = Topic(representations=representations)

    topic_items = {}
    for doc_id, pos, topic, nn, nn_dist in zip(ids, positions, topic_model.topics_, neighbors, neighbor_distances):
        topic_items[doc_id] = TopicItem(id=doc_id, x=pos[0].item(), y=pos[1].item(), topic_id=topic)

    result = TopicModellingResult(
        topics=topics,
        items=topic_items
    )
    json = result.model_dump_json()

    formatted_datetime = datetime.now().strftime("%d_%b_%Y_%H_%M_%S")
    out_path = f"./out/topics_{project_name}_{formatted_datetime}.json"
    log.info(f"Writing data processing result to '{out_path}'")
    with open(out_path, "w") as json_file:
        json_file.write(json)


def get_nearest_neighbor_distances(
        positions: np.ndarray[tuple[int, int], np.dtype[np.float32]],
):
    neighbors = NearestNeighbors(n_neighbors=2).fit(positions)
    distances, indices = neighbors.kneighbors(positions, n_neighbors=2)
    return distances[0:, 1], indices[0:, 1]


def main():
    logging.basicConfig(level=logging.INFO)

    args_parser = argparse.ArgumentParser(prog="ritgard")
    args_parser.add_argument("data_path")
    args = args_parser.parse_args()

    Path("./out").mkdir(exist_ok=True)

    data = read_mining_result(args.data_path)
    project_name = data.repository.name
    ids, docs = get_documents(data)

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

    topic_model, positions = use_bertopic(docs, project_name)
    distances, indices = get_nearest_neighbor_distances(positions)
    write_topics(ids, positions, topic_model, indices, distances, project_name)

    min_distance = np.min(distances)
    max_distance = np.max(distances)
    avg_distance = np.mean(distances)
    med_distance = np.median(distances)

    log.info(f"Min nearest neighbor distance: {min_distance}")
    log.info(f"Max nearest neighbor distance: {max_distance}")
    log.info(f"Average nearest neighbor distance: {avg_distance}")
    log.info(f"Median nearest neighbor distance: {med_distance}")


if __name__ == "__main__":
    main()
