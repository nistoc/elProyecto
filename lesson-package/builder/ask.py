"""Asking the models: answer modes, the store of answers, the budget.

Modes (per pass, see cli.py):
  dry     asks nothing; the request is only counted with its estimate.
  openai  paid: the chat completions API; the answer is stored as it came.
  files   a missing answer is written as `<name>.request.json` and not asked; the answer is the file `<name>.json`.
  replay  stored answers only; a missing one is an error.
A stored answer is never asked again, in any mode.
"""
import json
import os
import re
import threading
import time
import urllib.error
import urllib.request

PRICES = {'gpt-4.1': (2.0, 0.5, 8.0), 'gpt-4.1-mini': (0.4, 0.1, 1.6)}  # $ per 1M tokens: in, in cached, out
MODES = ('dry', 'openai', 'files', 'replay')
API_URL = 'https://api.openai.com/v1/chat/completions'


class ReplayMissing(Exception):
    pass


class BudgetExceeded(Exception):
    pass


class BadAnswer(Exception):
    pass


class Request:
    """One request: where its answer lies, what is sent, and a rough price (for dry mode and the budget)."""

    def __init__(self, folder, name, pass_key, model, max_tokens, system, messages, schema, est):
        self.folder, self.name, self.pass_key = folder, name, pass_key
        self.model, self.max_tokens = model, max_tokens
        self.system, self.messages, self.schema, self.est = system, messages, schema, est

    @property
    def path(self):
        return os.path.join(self.folder, self.name + '.json')

    @property
    def request_path(self):
        return os.path.join(self.folder, self.name + '.request.json')

    def file_form(self):
        return {'model': self.model, 'temperature': 0, 'max_tokens': self.max_tokens,
                'messages': [{'role': 'system', 'content': self.system}] +
                            [{'role': 'user', 'content': m} for m in self.messages],
                'schema': self.schema}

    def api_body(self):
        f = self.file_form()
        schema = f.pop('schema')
        f['response_format'] = {'type': 'json_schema',
                                'json_schema': {'name': re.sub(r'\W', '_', self.name), 'strict': True, 'schema': schema}}
        return f


def content(answer):
    return json.loads(answer['choices'][0]['message']['content'])


def read_answer(path):
    try:
        with open(path, encoding='utf-8') as f:
            answer = json.load(f)
        content(answer)
        answer['usage']['prompt_tokens'], answer['usage']['completion_tokens']
    except (ValueError, KeyError, IndexError, TypeError) as e:
        raise BadAnswer('%s is not an answer in the chat-completions shape (%s: %s)' % (path, type(e).__name__, e))
    return answer


def write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


def cost(answer, model):
    u = answer['usage']
    cached = (u.get('prompt_tokens_details') or {}).get('cached_tokens', 0)
    p = PRICES[model]
    return ((u['prompt_tokens'] - cached) * p[0] + cached * p[1] + u['completion_tokens'] * p[2]) / 1e6


def dollars(usages, model):
    """Price of a list of usages; the cached share is counted where the usage has it."""
    p = PRICES[model]
    cached = sum((u.get('prompt_tokens_details') or {}).get('cached_tokens', 0) for u in usages)
    tin, tout = sum(u['prompt_tokens'] for u in usages), sum(u['completion_tokens'] for u in usages)
    return ((tin - cached) * p[0] + cached * p[1] + tout * p[2]) / 1e6


KEY_NAME = 'OPENAI_API_KEY'


def api_key(env_path):
    """The value of the line `<KEY_NAME>=…` of the env file; never printed."""
    with open(env_path, encoding='utf-8-sig') as f:
        for line in f:
            name, sep, value = line.partition('=')
            if sep and name.strip() == KEY_NAME:
                return value.strip().strip('"')
    raise SystemExit('no %s in %s' % (KEY_NAME, env_path))


class Budget:
    """--max-usd: before each paid request, what was paid plus the estimate of this request plus the estimate of
    everything else known to be still to ask must stay within the limit."""

    def __init__(self, max_usd, known):
        self.max_usd, self.known, self.spent = max_usd, dict(known), 0.0
        self.lock = threading.Lock()
        self.stopped = False

    def before(self, key, est):
        with self.lock:
            if self.stopped:
                raise BudgetExceeded('stopped')
            rest = sum(v for k, v in self.known.items() if k != key)
            if self.spent + est + rest > self.max_usd:
                self.stopped = True
                raise BudgetExceeded('paid $%.4f, this request about $%.4f, the rest about $%.4f: over $%.2f'
                                     % (self.spent, est, rest, self.max_usd))

    def after(self, key, actual):
        with self.lock:
            self.known.pop(key, None)
            self.spent += actual


class Ledger:
    """What one lesson did, per pass: answers stored, asked, written as request files, missing; money."""

    def __init__(self):
        self.rows = {}

    def row(self, key):
        return self.rows.setdefault(key, {'stored': 0, 'asked': 0, 'written': 0, 'missing': 0, 'cost': 0.0, 'est': 0.0})

    @property
    def cost(self):
        return sum(r['cost'] for r in self.rows.values())

    @property
    def est(self):
        return sum(r['est'] for r in self.rows.values())


class Asker:
    def __init__(self, lesson, modes, default_mode, ledger, budget=None, env_path=None, collector=None, dry_run=False):
        self.lesson, self.modes, self.default = lesson, modes, default_mode
        self.ledger, self.budget, self.env_path = ledger, budget, env_path
        self.collector = collector  # dict filled with {key: estimate} of the paid requests still to ask (pre-flight)
        self.dry_run = dry_run  # pre-flight: behave as dry whatever the mode, but collect the paid ones

    def mode(self, pass_key):
        return self.modes.get(pass_key, self.default)

    def ask(self, req):
        """The answer, or None when it is not there (dry, files). Never asks what is stored."""
        row = self.ledger.row(req.pass_key)
        if os.path.exists(req.path):
            row['stored'] += 1
            return read_answer(req.path)
        mode = self.mode(req.pass_key)
        key = '%s|%s' % (self.lesson, req.path)
        if mode == 'replay':
            raise ReplayMissing('%s: no stored answer (replay mode)' % req.path)
        row['est'] += req.est
        if self.collector is not None and mode == 'openai':
            self.collector[key] = req.est
        if mode == 'dry' or self.dry_run:
            row['missing'] += 1
            return None
        if mode == 'files':
            write_json(req.request_path, req.file_form())
            row['written'] += 1
            return None
        # openai
        if self.budget:
            self.budget.before(key, req.est)
        answer = self._call(req)
        write_json(req.path, answer)
        c = cost(answer, req.model)
        row['asked'] += 1
        row['cost'] += c
        if self.budget:
            self.budget.after(key, c)
        return answer

    def _call(self, req):
        http = urllib.request.Request(API_URL, data=json.dumps(req.api_body()).encode('utf-8'),
                                      headers={'Content-Type': 'application/json',
                                               'Authorization': 'Bearer ' + api_key(self.env_path)})
        started = time.time()
        try:
            with urllib.request.urlopen(http, timeout=400) as r:
                answer = json.loads(r.read().decode('utf-8'))
        except urllib.error.HTTPError as e:
            raise SystemExit('%s: HTTP %s %s' % (req.name, e.code, e.read().decode('utf-8', 'replace')[:600]))
        answer['_seconds'] = round(time.time() - started, 1)
        answer['_answered_by'] = 'openai:' + req.model
        return answer
