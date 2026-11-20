"""Reads the prompts from `lesson-package/prompts/*.md`: the text of each lies in a fenced block under a `## ` heading."""
import os
import re

PROMPTS_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'prompts')

# The first form was written before this clause was added to the system message (see prompts/08).
FIRST_FORM_CLAUSE = ', а ученик ошибается в словах'

FILES = {
    'system': '00-system.md',
    'task1': '01-topics-grammar-examples.md',
    'task2': '02-words.md',
    'doubt': '03-word-in-doubt.md',
    'grammar2': '04-grammar-second-pass.md',
    'merge': '05-grammar-merge.md',
    'filter': '06-word-filter.md',
    'bycode': '07-words-by-code.md',
    'first1': '08-first-form-topics-grammar-examples.md',
    'first2': '09-first-form-words.md',
    'first3': '10-first-form-what-is-missing.md',
}


def parse(text):
    """{heading: text} of every fenced block that follows a `## heading`."""
    lines = text.split('\n')
    out, i = {}, 0
    while i < len(lines):
        m = re.match(r'^## (.+?)\s*$', lines[i])
        if not m:
            i += 1
            continue
        heading = m.group(1)
        i += 1
        while i < len(lines) and not lines[i].strip():
            i += 1
        f = re.match(r'^(`{3,})text\s*$', lines[i]) if i < len(lines) else None
        if not f:
            raise ValueError('no text block under "## %s"' % heading)
        fence = f.group(1)
        i += 1
        body = []
        while i < len(lines) and lines[i].rstrip() != fence:
            body.append(lines[i])
            i += 1
        if i >= len(lines):
            raise ValueError('the block under "## %s" is not closed' % heading)
        out[heading] = '\n'.join(body)
        i += 1
    return out


_cache = {}


def load(key, heading='Text', folder=None):
    """The text of prompt `key` (a key of FILES) under `heading`."""
    folder = folder or PROMPTS_DIR
    path = os.path.join(folder, FILES[key])
    if path not in _cache:
        with open(path, encoding='utf-8') as f:
            _cache[path] = parse(f.read())
    blocks = _cache[path]
    if heading not in blocks:
        raise KeyError('%s has no "## %s" (it has %s)' % (FILES[key], heading, ', '.join(blocks)))
    return blocks[heading]


def system(first_form=False):
    text = load('system')
    if first_form:
        if FIRST_FORM_CLAUSE not in text:
            raise ValueError('the system message has no clause to leave out for the first form')
        text = text.replace(FIRST_FORM_CLAUSE, '')
    return text
