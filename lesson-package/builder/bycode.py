"""The words found only by code, and pass 7 (what each of them is)."""
import collections
import hashlib
import os
import re

from . import prompts, schemas
from .ask import content, read_answer
from .context import in_est, load_json, save_json
from .textutil import LATIN, clock, phrase, plain

PER_REQUEST = 25
# Words of another language that the transcript often holds; they are not put to a model.
ENGLISH = set('continuous past present perfect simple have been waiting discover with from this that what when where which there their about would could should start ages time'.split())


def word_index(L):
    """Per plain form of 4+ letters: how many times it was said, the line of its first occurrence, whether it was only
    ever written with a capital."""
    count, first, caps_only = collections.Counter(), {}, collections.defaultdict(lambda: True)
    for p in L.periods:
        for l in p['lines']:
            for r in re.findall(LATIN, l['text']):
                f = plain(r)
                if len(f) < 4:
                    continue
                count[f] += 1
                first.setdefault(f, {'period': p['n'], 'at': clock(l['at']), 'who': l['who'], 'raw': r, 'text': l['text'],
                                     'sec': float(l['at'])})
                if not r[0].isupper():
                    caps_only[f] = False
    return count, first, caps_only


def compute_todo(L, held_texts):
    """Latin-letter words of the lesson that no list holds and no earlier lesson has, most said first.
    held_texts: the es and quote of every word of the lists."""
    held = set()
    for t in held_texts:
        for x in re.findall(LATIN, t):
            x = plain(x)
            if len(x) >= 4:
                held.add(x[:max(4, len(x) - 2)])
    count, first, caps_only = word_index(L)
    unseen = [f for f in count if L.earlier.unseen(f)]
    by_code = sorted((f for f in unseen if f[:max(4, len(f) - 2)] not in held and not caps_only[f] and f not in ENGLISH),
                     key=lambda f: (-count[f], first[f]['period'], first[f]['sec']))
    return [{'word': f, 'raw': first[f]['raw'], 'phrase': phrase(first[f]['text'], first[f]['raw'])} for f in by_code]


def todo_path(L):
    return os.path.join(L.dir, 'bycode_todo.json')


def store_path(L):
    return os.path.join(L.dir, 'bycode_words.json')


def _batch_name(prefix, part):
    return prefix + hashlib.sha1(' '.join(t['word'] for t in part).encode('utf-8')).hexdigest()[:10]


def _request(L, prefix, model, pass_key, part):
    text = '\n'.join('#%d «%s»\n   фраза: «%s»' % (i + 1, t['raw'], re.sub(r'\s+', ' ', t['phrase'])[:260]) for i, t in enumerate(part))
    price_in, price_out = (2.0, 8.0) if model == 'gpt-4.1' else (0.4, 1.6)
    est = (len(text) / 2.6 + 600) * price_in / 1e6 + len(part) * 30 * price_out / 1e6
    return L.request(L.dir, _batch_name(prefix, part), pass_key, model, 3500, prompts.load('bycode', 'System'),
                     [prompts.load('bycode', 'Text') % text], schemas.PASS7, est)


def stage_pass7(L):
    """7a: every word of the todo list to the cheap model, 25 to a request. 7b: the words it calls Russian or broken, or
    is not sure of, once more to gpt-4.1. The answers are kept in `bycode_words.json` as they are applied."""
    todo_list = load_json(todo_path(L), [])
    todo = {t['word']: t for t in todo_list}
    store = load_json(store_path(L), {})
    changed, done = False, True
    missing = [t for t in todo_list if t['word'] not in store]
    for b in range(0, len(missing), PER_REQUEST):
        part = missing[b:b + PER_REQUEST]
        a = L.ask(_request(L, 'bycode_', 'gpt-4.1-mini', 'pass7', part))
        if a is None:
            done = False
            continue
        for it in content(a)['items']:
            if 1 <= it['i'] <= len(part):
                store[part[it['i'] - 1]['word']] = {'kind': it['kind'], 'lemma': it['lemma'], 'ru': it['ru'], 'sure': it['sure'],
                                                    'model': 'gpt-4.1-mini'}
                changed = True
    if done:
        again = [todo[w] for w, a in store.items() if w in todo and a.get('model') != 'gpt-4.1' and (a['kind'] in ('russian', 'broken') or not a['sure'])]
        for b in range(0, len(again), PER_REQUEST):
            part = again[b:b + PER_REQUEST]
            a = L.ask(_request(L, 'bycode2_', 'gpt-4.1', 'pass7', part))
            if a is None:
                done = False
                continue
            for it in content(a)['items']:
                if 1 <= it['i'] <= len(part):
                    w = part[it['i'] - 1]['word']
                    was = store[w]
                    store[w] = {'kind': it['kind'], 'lemma': it['lemma'], 'ru': it['ru'], 'sure': it['sure'], 'model': 'gpt-4.1',
                                'first': {k: was[k] for k in ('kind', 'lemma', 'ru', 'sure')}}
                    changed = True
    if changed:
        save_json(store_path(L), store)
    return done


def usages(L):
    """The usages of the answers of pass 7a and 7b (the files `bycode_<hash>.json`, `bycode2_<hash>.json`)."""
    a, b = [], []
    for f in sorted(os.listdir(L.dir)):
        if re.fullmatch(r'bycode_[0-9a-f]{10}\.json', f):
            a.append(read_answer(os.path.join(L.dir, f))['usage'])
        elif re.fullmatch(r'bycode2_[0-9a-f]{10}\.json', f):
            b.append(read_answer(os.path.join(L.dir, f))['usage'])
    return a, b
