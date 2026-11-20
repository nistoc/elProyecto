"""Passes 1-4: the lists of a recording (1, 2), the words the code puts in doubt (3), the second pass for grammar (4)."""
import os
import re

from . import prompts, schemas
from .ask import content, read_answer
from .context import in_est, load_json, out_est, save_json
from .earlier import candidates
from .textutil import LATIN, letters, window

PER_REQUEST = 12  # words to a request in pass 3


def _lists_req(L, n, name, task, schema, max_tokens, out_tokens, pass_key):
    text = prompts.load(task)
    est = in_est(len(L.dialogue(n)) + len(text)) + out_est(out_tokens)
    return L.request(L.pdir(n), name, pass_key, 'gpt-4.1', max_tokens, prompts.system(),
                     ['Строки диалога:\n' + L.dialogue(n), text], schema, est)


def stage_pass1(L):
    done = True
    for p in L.periods:
        if L.ask(_lists_req(L, p['n'], 'task1', 'task1', schemas.PASS1, 7000, 3000, 'pass1')) is None:
            done = False
    return done


def stage_pass2(L):
    done = True
    for p in L.periods:
        if L.ask(_lists_req(L, p['n'], 'task2', 'task2', schemas.PASS2, 12000, 6000, 'pass2')) is None:
            done = False
    return done


def shown(lines, c):
    """What a model sees of a candidate: the line cut around the word and the ends of the lines before and after."""
    l = lines[c['n'] - 1]
    prev = lines[c['n'] - 2] if c['n'] >= 2 else None
    nxt = lines[c['n']] if c['n'] < len(lines) else None
    parts = []
    if prev:
        parts.append('   до [%d] %s: …%s' % (prev['n'], prev['who'], prev['text'][-110:]))
    parts.append('   строка [%d] %s: %s' % (l['n'], l['who'], window(l['text'], c['word'])))
    if nxt:
        parts.append('   после [%d] %s: %s…' % (nxt['n'], nxt['who'], nxt['text'][:130]))
    return '\n'.join(parts)


def candidates_of(L, n):
    """The candidates of period n: stored in `candidates.json`, else computed from the lists of pass 2 and the earlier
    lessons (and then stored)."""
    path = os.path.join(L.pdir(n), 'candidates.json')
    stored = load_json(path)
    if stored is not None:
        return stored
    t2 = L.stored(L.pdir(n), 'task2')
    held = [x for w in t2['new_words'] for x in re.findall(LATIN, w['es'] + ' ' + w.get('heard', ''))]
    cs = candidates(L.lines(n), held, L.earlier, min_cyr=2)
    save_json(path, cs)
    return cs


def cq_requests(L, n):
    cs = candidates_of(L, n)
    lines = L.lines(n)
    text_of = prompts.load('doubt')
    reqs = []
    for b in range(0, len(cs), PER_REQUEST):
        batch = cs[b:b + PER_REQUEST]
        text = '\n'.join('#%d слово «%s»\n%s' % (i + 1, c['word'], shown(lines, c)) for i, c in enumerate(batch))
        est = in_est(len(text), 1050) + len(batch) * 30 * 8.0 / 1e6
        reqs.append(L.request(L.pdir(n), 'cq_%02d' % (b // PER_REQUEST), 'pass3', 'gpt-4.1', 2500, prompts.system(),
                              [text_of % text], schemas.PASS3, est))
    return reqs


def stage_pass3(L):
    done = True
    for p in L.periods:
        for req in cq_requests(L, p['n']):
            if L.ask(req) is None:
                done = False
    return done


def read_cq(L, n):
    """The answers about the candidates of period n: (candidates, [(candidate, word answer)], usage sums).
    Each taught answer carries `evidence_ok`: its evidence stands in the lines around the candidate."""
    cs = candidates_of(L, n)
    lines = L.lines(n)
    tin = tout = reqs = 0
    found = []
    for b in range(0, (len(cs) + PER_REQUEST - 1) // PER_REQUEST):
        path = os.path.join(L.pdir(n), 'cq_%02d.json' % b)
        if not os.path.exists(path):
            continue
        a = read_answer(path)
        tin += a['usage']['prompt_tokens']
        tout += a['usage']['completion_tokens']
        reqs += 1
        batch = cs[b * PER_REQUEST:(b + 1) * PER_REQUEST]
        for w in content(a)['words']:
            if w['taught'] and 1 <= w['i'] <= len(batch):
                c = batch[w['i'] - 1]
                near = ' '.join(lines[k]['text'] for k in range(max(0, c['n'] - 2), min(len(lines), c['n'] + 1)))
                ev = w.get('evidence')
                w['evidence_ok'] = None if ev is None else bool(letters(ev)) and letters(ev) in letters(near)
                found.append((c, w))
    return cs, found, {'prompt_tokens': tin, 'completion_tokens': tout}, reqs


def listed_grammar(grammar):
    return '\n'.join('%d. [%s] %s — %s' % (i + 1, g['theme'], g['topic'], g['rule']) for i, g in enumerate(grammar)) or '(пусто)'


def stage_pass4(L):
    done = True
    task = prompts.load('grammar2')
    for p in L.periods:
        n = p['n']
        listed = listed_grammar(L.stored(L.pdir(n), 'task1')['grammar'])
        text = task % listed
        est = in_est(len(L.dialogue(n)) + len(listed) + 0, 900) + out_est(900)
        req = L.request(L.pdir(n), 'task1b', 'pass4', 'gpt-4.1', 4000, prompts.system(),
                        ['Строки диалога:\n' + L.dialogue(n), text], schemas.PASS4, est)
        if L.ask(req) is None:
            done = False
    return done
