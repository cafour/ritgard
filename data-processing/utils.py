from datetime import datetime, UTC
from markdown import markdown
from bs4 import BeautifulSoup
import regex

URL_REGEX = regex.compile(r'https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)')

def get_now_string():
    return datetime.now(UTC).strftime("%Y-%m-%d_%H-%M-%S")

def get_plain_text(md: str) -> str:
    html = markdown(md, extensions=['extra', 'sane_lists', 'tables', 'toc'])
    soup = BeautifulSoup(html, "html.parser")
    for tag in soup.find_all(['a', 'img', 'pre']):
        if tag.name == 'a':
            tag.replace_with(tag.get_text())
        elif tag.name == 'img':
            alt = tag.get('alt', '')
            tag.replace_with(alt)
        elif tag.name == 'pre':
            tag.replace_with("")
    plain = soup.get_text()
    return regex.sub(URL_REGEX, '', plain)

def get_prompt(repo_topics: list[str]):
    return f"""You will extract a short topic label from keywords and several titles of representative developer conversations of a software project.
Here are two examples of topics you created before:

# Example 1
Representative developer conversation titles from this topic:
- [testing] Flaky UI tests
- UI Tests for ko.options.deferUpdates
- [testing] [bug] Updated selenium helpers and fixed unstable UI tests
- Run UI tests in both Development and Production env

Project topics: aspnet, aspnetcore, c-sharp, dotnet, dotnet-core, dotnet-template, framework, mvvm, owin

Keywords: tests, selenium, ui, browser, output, failure, success

topic: UI tests

# Example 2
Representative developer conversation from this topic:
- Make C# keywords as reserved in bindings
- [enhancement] [framework] Improvements to BindingParser and PropertyDeclarationDirectiveCompiler
- Add support for lambdas in command bindings
- [documentation] [framework] [testing] Knockout if binding

Project topics: aspnet, aspnetcore, c-sharp, dotnet, dotnet-core, dotnet-template, framework, mvvm, owin

Keywords: binding, expression, js, knockout, command, compilation

topic: Compilation of binding expressions

# Your task
Representative developer conversation from this topic:
[DOCUMENTS]

Project topics: {", ".join(repo_topics)}

Keywords: [KEYWORDS]

Based on the information above, extract a short topic label in the following format:

topic: <topic_label>
"""

class LineTokenizer:
    def encode(self, doc: str):
        return doc.splitlines()
    def decode(self, tokens: list[str]):
        return " ".join(tokens)