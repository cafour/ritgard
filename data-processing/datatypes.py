from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_pascal

import logging

log = logging.getLogger("ritgard.datatypes")


class DocumentItemComment(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    body: str | None = None
    plain_text: str | None = None
    created_at: str | None = None


class DocumentItem(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    id: str
    title: str
    labels: list[str] | None = None
    body: str | None = None
    plain_text: str | None = None
    comments: list[DocumentItemComment] | None = None
    created_at: str


class ClocHeader(BaseModel):
    model_config = ConfigDict(validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)
    file_count: int = Field(alias="n_files", default=0)
    line_count: int = Field(alias="n_lines", default=0)


class ClocEntry(BaseModel):
    model_config = ConfigDict(validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)
    file_count: int = Field(alias="nFiles", default=0)
    code_count: int = Field(alias="code", default=0)


DEFAULT_EXCLUDED_FILE_TYPES = ["Markdown", "CSV", "Text", "TOML", "JSON", "YAML", "INI"]
DEFAULT_EXCLUDED_EXTENSIONS = [".json", ".csv", ".yaml", ".yml", ".toml", ".ini", ".txt", ".md", ".xml", ".lock"]


class ClocInfo(BaseModel):
    model_config = ConfigDict(validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True, extra='allow')
    header: ClocHeader = Field(alias="header")
    entries: dict[str, ClocEntry] | None = None

    @model_validator(mode='after')
    def parse_extras(self):
        if self.model_extra is not None:
            self.entries = {}
            for file_type, entry in self.model_extra.items():
                self.entries[file_type] = ClocEntry(**entry)
        return self

    def get_file_count(self, excluded_file_types: list[str] = None):
        if excluded_file_types is None:
            excluded_file_types = DEFAULT_EXCLUDED_FILE_TYPES
        total = 0
        for file_type, entry in self.entries.items():
            if file_type == "SUM" or file_type in excluded_file_types:
                continue
            total = total + entry.file_count
        return total

    def get_code_lines(self, excluded_file_types: list[str] = None):
        if excluded_file_types is None:
            excluded_file_types = DEFAULT_EXCLUDED_FILE_TYPES
        total = 0
        for file_type, entry in self.entries.items():
            if file_type == "SUM" or file_type in excluded_file_types:
                continue
            total = total + entry.code_count
        return total


class GitLocEntry(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)
    added_line_count: int = 0
    deleted_line_count: int = 0


class GitLocInfo(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)
    added_line_count: int = 0
    deleted_line_count: int = 0
    entries: dict[str, GitLocEntry]

    def get_line_count(self, excluded_extensions: list[str] = None):
        if excluded_extensions is None:
            excluded_extensions = DEFAULT_EXCLUDED_EXTENSIONS
        total = 0
        for file_type, entry in self.entries.items():
            if file_type in excluded_extensions:
                continue
            total = total + entry.added_line_count + entry.deleted_line_count
        return total


class Repository(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    id: int
    owner: str
    name: str
    topics: list[str]
    cloc: ClocInfo | None
    git_loc: GitLocInfo | None
    size: int
    created_at: str | None


class MiningResult(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    issues: dict[str, DocumentItem] | None = None
    pull_requests: dict[str, DocumentItem] | None = None
    discussions: dict[str, DocumentItem] | None = None
    repository: Repository


class Topic(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    id: int
    representations: dict[str, list[tuple[str, float]]]


class TopicItem(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)

    id: str
    x: float
    y: float
    topic_id: int
    probabilities: dict[int, float]


class TopicModellingResult(BaseModel):
    model_config = ConfigDict(alias_generator=to_pascal, validate_by_alias=True, validate_by_name=True,
                              serialize_by_alias=True)
    name: str
    owner: str
    topics: dict[int, Topic]
    items: dict[str, TopicItem]


def read_mining_result(filename: str) -> MiningResult:
    log.info(f"Reading data from '{filename}'")
    with open(filename, "r", encoding="utf8") as json_file:
        json = json_file.read()
        return MiningResult.model_validate_json(json)
