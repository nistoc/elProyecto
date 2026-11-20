"""The order of a build (BUILDER.md, "The order of a build") and the run of one lesson."""
import os

from . import assemble, bycode, first_form, grammar_words, lists
from .ask import Asker, Ledger, write_json
from .context import Lesson
from .source import read_input

# (stage, the pass it belongs to for the answer mode, the stages it needs, function)
STAGES = [
    ('first_ab', 'first', [], first_form.stage_ab),
    ('first_c', 'first', ['first_ab'], first_form.stage_c),
    ('pass1', 'pass1', [], lists.stage_pass1),
    ('pass2', 'pass2', [], lists.stage_pass2),
    ('pass3', 'pass3', ['pass2'], lists.stage_pass3),
    ('pass4', 'pass4', ['pass1'], lists.stage_pass4),
    ('pass5', 'pass5', ['first_c', 'pass1', 'pass4'], grammar_words.stage_pass5),
    ('pass6', 'pass6', ['first_c', 'pass2', 'pass3'], grammar_words.stage_pass6),
    ('todo', None, ['first_c', 'pass1', 'pass2', 'pass3', 'pass4', 'pass5', 'pass6'], None),
    ('pass7', 'pass7', ['todo'], bycode.stage_pass7),
    ('package', None, ['pass7'], None),
]


class Result:
    def __init__(self, date):
        self.date, self.status, self.stages, self.ledger = date, 'new', {}, Ledger()


def run_lesson(date, work, mode, modes, agent05, refresh, first_form_opt, env_path, budget=None, collector=None, dry_run=False,
               get=None):
    """Runs the stages of one lesson. Status: done (package.json written), waiting (answers missing: dry, files),
    ready: all answers are there, a dry call writes nothing."""
    res = Result(date)
    folder = os.path.join(work, date)
    kw = {'get': get} if get else {}
    periods, earlier_json, marks = read_input(date, folder, agent05, refresh, **kw)
    asker = Asker(date, modes, mode, res.ledger, budget, env_path, collector, dry_run)
    L = Lesson(date, folder, periods, earlier_json, marks, asker, first_form_opt)
    writes = not dry_run and mode != 'dry'  # a dry call writes no package
    for name, pass_key, needs, fn in STAGES:
        if any(res.stages.get(n) != 'done' for n in needs):
            res.stages[name] = 'blocked'
            continue
        if name == 'todo':
            if writes:
                ok = _assemble(L, res, write=False)
            else:  # a dry call computes nothing: the list is there or it is still to make
                ok = os.path.exists(bycode.todo_path(L))
        elif name == 'package':
            if writes:
                ok = _assemble(L, res, write=True)
            else:
                res.stages[name] = 'ready'
                continue
        else:
            ok = fn(L)
        res.stages[name] = 'done' if ok else 'waiting'
    res.status = 'done' if res.stages['package'] == 'done' else 'ready' if res.stages['package'] == 'ready' else 'waiting'
    return res


def _assemble(L, res, write):
    pkg = assemble.assemble(L)
    if write:
        write_json(os.path.join(L.dir, 'package.json'), pkg)
        res.cost, res.requests = pkg['cost_usd'], pkg['requests']
    return True
