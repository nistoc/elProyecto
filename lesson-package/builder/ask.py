"""Asking the models: answer modes, the store of answers, the budget.

Modes (per pass, see cli.py):
  dry     asks nothing; the request is only counted with its estimate.
  openai  paid: the chat completions API; the answer is stored as it came.
  files   a missing answer is written as `<name>.request.json` and not asked; the answer is the file `<name>.json`.
  replay  stored answers only; a missing one is an error.
A stored answer is never asked again, in any mode.

An answer cut at its max_tokens (finish_reason "length") has a content that is not whole JSON. It is never asked
again either: its whole items are the answer (`salvage`), exact repeats of an item dropped, and the cut answer is kept
next to it as `<name>.cut.json`. Asked at temperature 0, a model that looped loops again; and a list that was only
longer than the room is reported (the ledger counts it), so a person can raise the room and ask that one again.
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

    @property
    def cut_path(self):
        return os.path.join(self.folder, self.name + '.cut.json')

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


def is_cut(answer):
    """The answer stopped at its max_tokens: its content is the start of a JSON text, not a whole one."""
    try:
        return answer['choices'][0].get('finish_reason') == 'length'
    except (KeyError, IndexError, TypeError, AttributeError):
        return False


def complete_prefix(text):
    """The longest start of a cut JSON text that ends right after a whole object or list inside another one, with
    the brackets still open closed; None when there is no such place."""
    stack, in_str, esc, best = [], False, False, None
    for i, ch in enumerate(text):
        if in_str:
            if esc:
                esc = False
            elif ch == '\\':
                esc = True
            elif ch == '"':
                in_str = False
            continue
        if ch == '"':
            in_str = True
        elif ch in '{[':
            stack.append(ch)
        elif ch in '}]':
            if not stack:
                return None
            stack.pop()
            if not stack:
                return text[:i + 1]
            best = text[:i + 1] + ''.join('}' if c == '{' else ']' for c in reversed(stack))
    return best


def _empty(schema):
    kind = schema.get('type')
    kind = kind[0] if isinstance(kind, list) else kind
    if kind == 'object':
        return _fill({}, schema)
    return {'array': [], 'string': '', 'integer': 0, 'number': 0, 'boolean': False}.get(kind)


def _fill(value, schema):
    """The fields the schema has and a cut value lacks (those after the cut), each empty."""
    if not isinstance(schema, dict):
        return value
    if isinstance(value, dict):
        for k, s in (schema.get('properties') or {}).items():
            value[k] = _fill(value[k], s) if k in value else _empty(s)
    elif isinstance(value, list) and isinstance(schema.get('items'), dict):
        value[:] = [_fill(v, schema['items']) for v in value]
    return value


def drop_repeats(value):
    """Exact repeats of an item of a list are dropped (a looping answer writes the same items again); their count."""
    dropped = 0
    if isinstance(value, dict):
        for v in value.values():
            dropped += drop_repeats(v)
    elif isinstance(value, list):
        seen, kept = set(), []
        for v in value:
            key = json.dumps(v, sort_keys=True, ensure_ascii=False)
            if key in seen:
                dropped += 1
                continue
            seen.add(key)
            kept.append(v)
            dropped += drop_repeats(v)
        value[:] = kept
    return dropped


def salvage(answer, schema):
    """A cut answer made whole: its whole items, exact repeats dropped, the fields after the cut empty. The usage
    stays (it is what was paid); `_cut` tells what was done. None when not one whole item came."""
    text = answer['choices'][0]['message'].get('content') or ''
    prefix = complete_prefix(text)
    if prefix is None:
        return None
    try:
        value = json.loads(prefix)
    except ValueError:
        return None
    value = _fill(value, schema)
    dropped = drop_repeats(value)
    mended = json.loads(json.dumps(answer))
    mended['choices'][0]['message']['content'] = json.dumps(value, ensure_ascii=False)
    mended['_cut'] = {'content_chars': len(text), 'kept_chars': len(prefix), 'repeats_dropped': dropped}
    return mended


def _load_raw(path):
    try:
        with open(path, encoding='utf-8') as f:
            return json.load(f)
    except ValueError:
        return None


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
        return self.rows.setdefault(key, {'stored': 0, 'asked': 0, 'written': 0, 'missing': 0, 'cost': 0.0, 'est': 0.0,
                                          'cut': 0, 'repeats': 0})

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
            stored = _load_raw(req.path)
            if stored is not None and is_cut(stored) and '_cut' not in stored:
                return self._mend(req, row, stored)  # stored by a builder that kept cut answers as they came
            answer = read_answer(req.path)
            if '_cut' in answer:
                row['cut'] += 1
                row['repeats'] += answer['_cut'].get('repeats_dropped', 0)
            return answer
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
        c = cost(answer, req.model)
        row['asked'] += 1
        row['cost'] += c
        if self.budget:
            self.budget.after(key, c)
        if is_cut(answer):
            return self._mend(req, row, answer)
        write_json(req.path, answer)
        return answer

    def _mend(self, req, row, cut):
        """A cut answer: kept as `<name>.cut.json`, its salvaged form stored as the answer. Done in every mode, also
        by a pre-flight: it costs nothing, gives the same each time, and the later stages read the stored file."""
        mended = salvage(cut, req.schema)
        write_json(req.cut_path, cut)
        if mended is None:
            if os.path.exists(req.path):
                os.remove(req.path)  # the cut answer is in .cut.json; the place of the answer stays free
            raise BadAnswer('%s: the answer was cut at max_tokens %s before its first whole item (kept as %s)'
                            % (req.path, req.max_tokens, req.cut_path))
        row['cut'] += 1
        row['repeats'] += mended['_cut']['repeats_dropped']
        write_json(req.path, mended)
        return mended

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
