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