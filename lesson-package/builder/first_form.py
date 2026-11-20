"""The first form: three requests per recording (A topics/grammar/examples, B words, C what is missing).

Answers lie in `p<n>/first/task1.json` (A), `task2.json` (B), `task3.json` (C). `--first-form a` asks A alone,
`none` asks nothing; the lists the other passes read are then simply empty.
"""
import os

from . import prompts, schemas
from .context import in_est, out_est

# Rough sizes of the answers, tuned to the price of the first form measured in BUILDER.md.
OUT_TOKENS = {'task1': 2600, 'task2': 5100, 'task3': 1100}


def folder(L, n):
    return os.path.join(L.pdir(n), 'first')


def load(L, n, name):
    """Content of a first-form answer, {} when there is none."""
    return L.stored(folder(L, n), name) or {}


def _req(L, n, name, task, schema, max_tokens, task_args=None):
    text = prompts.load(task)
    if task_args is not None:
        text = text % task_args
    msgs = ['Строки диалога:\n' + L.dialogue(n), text]
    est = in_est(len(L.dialogue(n)) + len(text)) + out_est(OUT_TOKENS[name])
    return L.request(folder(L, n), name, 'first', 'gpt-4.1', max_tokens, prompts.system(first_form=True), msgs, schema, est)


def stage_ab(L):
    """Requests A and B for every period. True when all answers are there."""
    if L.first_form == 'none':
        return True
    done = True
    for p in L.periods:
        n = p['n']
        names = [('task1', 'first1', schemas.FIRST_A, 7000)]
        if L.first_form == 'all':
            names.append(('task2', 'first2', schemas.FIRST_B, 12000))
        for name, task, schema, mx in names:
            if L.ask(_req(L, n, name, task, schema, mx)) is None:
                done = False
    return done


def stage_c(L):
    """Request C: what A and B missed, asked with their lists."""
    if L.first_form != 'all':
        return True
    done = True
    for p in L.periods:
        n = p['n']
        t1, t2 = load(L, n, 'task1'), load(L, n, 'task2')
        found_words = '; '.join(w['es'] for w in t2['new_words'] + t2['vocabulary'])
        found_grammar = '; '.join(g['topic'] for g in t1['grammar'])
        if L.ask(_req(L, n, 'task3', 'first3', schemas.FIRST_C, 6000, task_args=(found_words, found_grammar))) is None:
            done = False
    return done
