from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_pascal

import logging

log = logging.getLogger("ritgard.datatypes")


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
    probabilities: dict[int, float]


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
