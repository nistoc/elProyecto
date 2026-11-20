"""Pass 5 (grammar merged into themes) and pass 6 (filter of the words), with the lists that decide what they ask."""
import os
import re

from . import first_form, prompts, schemas
from .ask import content, read_answer
from .context import in_est, load_json, out_est, save_json
from .lists import read_cq
from .textutil import LATIN, plain, window

SMALL = set('de la el un una los las del al se a en con por para que lo y o no'.split())
HOW = ('explained', 'asked', 'corrected', 'prompted', 'introduced')
PER_REQUEST = 20  # words to a request in pass 6
FILTER_MODEL = 'gpt-4.1-mini'


def keys(es):
    """The stems of the words of an entry that carry it."""
    ws = [plain(w) for w in re.findall(LATIN, re.sub(r'\([^)]*\)', ' ', es))]
    ws = [w for w in ws if w not in SMALL and len(w) >= 3]
    return frozenset(w[:max(4, len(w) - 2)] for w in ws)


def has(es, pool_keys):
    k = keys(es)
    return bool(k) and any(k & x for x in pool_keys)


def filter_words(L):
    """The words that go through the filter, in one fixed order (the answers are matched by position):
    src 'q' - a word the question about a candidate marked as taught, with its evidence found in the lines;
    src 'v1' - a new word of the first form that no list of the later passes has.
    Stored in `filter_words.json`; computed (and stored) when absent."""
    path = os.path.join(L.dir, 'filter_words.json')
    stored = load_json(path)
    if stored is not None:
        return stored
    out, v2_keys = [], []
    for p in L.periods:
        n = p['n']
        s2 = L.stored(L.pdir(n), 'task2') or {}
        for w in s2.get('new_words', []):
            v2_keys.append(keys(w['es']))
            if w.get('heard'):
                v2_keys.append(keys(w['heard']))
        v2_keys += [keys(w['es']) for w in s2.get('vocabulary', [])]
        lines = L.lines(n)
        cs, found, _, _ = read_cq(L, n)
        for c, w in found:
            if w['evidence_ok']:
                out.append({'src': 'q', 'period': n, 'es': w['es'], 'ru': w['ru'], 'how': w['how'] if w['how'] in HOW else 'explained',
                            'line': c['n'], 'quote': window(lines[c['n'] - 1]['text'], c['word'], 70, 110), 'note': w['evidence'],
                            'heard': c['word'], 'found': c['kind']})
    q_keys = [keys(w['es']) for w in out]
    taken = []
    for p in L.periods:
        n = p['n']
        for name in ('task2', 'task3'):
            for w in first_form.load(L, n, name).get('new_words', []):
                if has(w['es'], v2_keys + q_keys + taken):
                    continue
                taken.append(keys(w['es']))
                out.append({'src': 'v1', 'period': n, 'es': w['es'], 'ru': w['ru'], 'how': w['how'] if w['how'] in HOW else 'explained',
                            'line': w['line'], 'quote': w['quote'], 'note': w.get('note', ''), 'heard': '', 'found': 'first form'})
    save_json(path, out)
    return out


def filter_requests(L):
    ws = filter_words(L)
    task = prompts.load('filter', 'Text')
    system = prompts.load('filter', 'System')
    reqs = []
    for b in range(0, len(ws), PER_REQUEST):
        part = ws[b:b + PER_REQUEST]
        text = '\n'.join('#%d es: «%s» | ru: «%s»\n   место: «%s»%s' % (
            i + 1, w['es'], w['ru'], re.sub(r'\s+', ' ', w['quote'])[:240],
            ('\n   в этом месте сказано: «%s»' % w['note'][:120]) if w['src'] == 'q' and w['note'] else '')
            for i, w in enumerate(part))
        est = in_est(len(text), 700) * 0.4 / 2.0 + len(part) * 32 * 1.6 / 1e6
        reqs.append(L.request(L.dir, 'filter_%s_%02d' % (FILTER_MODEL, b // PER_REQUEST), 'pass6', FILTER_MODEL, 2500,
                              system, [task % text], schemas.PASS6, est))
    return reqs


def stage_pass6(L):
    done = True
    for req in filter_requests(L):
        if L.ask(req) is None:
            done = False
    return done


def verdicts(L, n_words):
    """The answers of the filter by position (None where a word got no answer), the tokens and the requests."""
    out = [None] * n_words
    tin = tout = reqs = 0
    for b in range((n_words + PER_REQUEST - 1) // PER_REQUEST):
        p = os.path.join(L.dir, 'filter_%s_%02d.json' % (FILTER_MODEL, b))
        if not os.path.isfile(p):
            continue
        a = read_answer(p)
        tin += a['usage']['prompt_tokens']
        tout += a['usage']['completion_tokens']
        reqs += 1
        for it in content(a)['items']:
            k = b * PER_REQUEST + it['i'] - 1
            if 1 <= it['i'] <= PER_REQUEST and k < n_words:
                out[k] = it
    return out, tin, tout, reqs


def keep(v, w):
    """The rule of the filter: a Spanish word that is not a form of a verb; a word the model itself names as the
    dictionary form is one, whatever else it answered. A word without an answer is kept."""
    if v is None:
        return True
    same = bool(v['lemma']) and plain(v['lemma']) == plain(w['es'])
    return (v['spanish'] or same) and not (v['form'] == 'verb_form' and not same)


def grammar_points(L):
    """The grammar points of the lesson in one fixed order (the merge is asked in this order and the build reads it
    so). Per period: the list of pass 1, pass 4, then the first form's A and C. Each: (period, pass, point);
    pass 1 - the list, 2 - the second pass, 3 - the first form."""
    out = []
    for p in L.periods:
        n = p['n']
        sources = [(L.stored(L.pdir(n), 'task1'), 1), (L.stored(L.pdir(n), 'task1b'), 2),
                   (first_form.load(L, n, 'task1'), 3), (first_form.load(L, n, 'task3'), 3)]
        for doc, pas in sources:
            for g in (doc or {}).get('grammar', []):
                out.append((n, pas, g))
    return out


def merge_text(points):
    return '\n'.join('%d. [%s] %s — %s' % (i + 1, g.get('theme') or '—', g['topic'], g['rule']) for i, (_, _, g) in enumerate(points))


def stage_pass5(L):
    text = merge_text(grammar_points(L))
    est = in_est(len(text), 600) + out_est(len(text) / 2.6)
    req = L.request(L.dir, 'merge3', 'pass5', 'gpt-4.1', 8000, prompts.system(), [prompts.load('merge') % text], schemas.PASS5, est)
    return L.ask(req) is not None
