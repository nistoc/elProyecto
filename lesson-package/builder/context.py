"""The state of one lesson during a build: its input, its folder, its asker."""
import json
import os

from .ask import Request, content, read_answer, write_json
from .earlier import Earlier

IN_PRICE = 2.0
OUT_PRICE = 8.0


class Lesson:
    def __init__(self, date, folder, periods, earlier_json, marks, asker, first_form='all'):
        self.date, self.dir = date, folder
        self.periods, self.marks, self.asker = periods, marks, asker
        self.earlier_json = earlier_json
        self.earlier = Earlier.from_json(earlier_json, date)
        self.first_form = first_form  # all | a | none

    def pdir(self, n):
        return os.path.join(self.dir, 'p%d' % n)

    def lines(self, n):
        return self.periods[n - 1]['lines']

    def dialogue(self, n):
        return '\n'.join('[%d] %s: %s' % (l['n'], l['who'], l['text']) for l in self.lines(n))

    def request(self, folder, name, pass_key, model, max_tokens, system, messages, schema, est):
        return Request(folder, name, pass_key, model, max_tokens, system, messages, schema, est)

    def ask(self, req):
        return self.asker.ask(req)

    def stored(self, folder, name):
        """Content of a stored answer, or None."""
        path = os.path.join(folder, name + '.json')
        return content(read_answer(path)) if os.path.exists(path) else None

    def stored_answer(self, folder, name):
        path = os.path.join(folder, name + '.json')
        return read_answer(path) if os.path.exists(path) else None


def in_est(chars, extra=900):
    """Rough price of the input side: characters / 2.6 per token, plus the fixed part of the prompt."""
    return (chars / 2.6 + extra) * IN_PRICE / 1e6


def out_est(tokens):
    return tokens * OUT_PRICE / 1e6


def load_json(path, default=None):
    if not os.path.exists(path):
        return default
    with open(path, encoding='utf-8') as f:
        return json.load(f)


def save_json(path, data):
    write_json(path, data)
