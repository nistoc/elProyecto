"""Command line: `python -m builder build <date> ... --work <folder> --mode ...` and `publish`."""
import argparse
import concurrent.futures
import os
import shutil
import sys
import threading

from .ask import MODES, Budget, BudgetExceeded, ReplayMissing, BadAnswer
from .pipeline import STAGES, run_lesson
from .source import DEFAULT_URL

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_ENV = os.path.join(REPO, 'agent06-improver-dot-net', 'API', '.env')
PASSES = ('first', 'pass1', 'pass2', 'pass3', 'pass4', 'pass5', 'pass6', 'pass7')
EXIT_ERROR, EXIT_WAITING, EXIT_BUDGET = 2, 3, 4
_print_lock = threading.Lock()


def say(*parts):
    with _print_lock:
        print(*parts, flush=True)


def parse_answer_modes(items):
    modes = {}
    for item in items or []:
        key, _, mode = item.partition('=')
        if key not in PASSES or mode not in MODES:
            raise SystemExit('--answer %s: expected <%s>=<%s>' % (item, '|'.join(PASSES), '|'.join(MODES)))
        modes[key] = mode
    return modes


def report(res):
    lines = ['%s: %s' % (res.date, res.status)]
    lines.append('   stages: ' + ', '.join('%s %s' % (n, s) for n, s in res.stages.items()))
    for key, r in res.ledger.rows.items():
        lines.append('   %-6s stored %3d, asked %3d, request files %3d, missing %3d, paid $%.4f, to ask about $%.4f' % (
            key, r['stored'], r['asked'], r['written'], r['missing'], r['cost'], r['est']))
    if res.status == 'done':
        lines.append('   package: %d requests, $%.2f by the usage of the answers' % (res.requests, res.cost))
    lines.append('   paid $%.4f%s' % (res.ledger.cost, '' if res.status == 'done' else '; still to ask about $%.3f' % sum(
        r['est'] for r in res.ledger.rows.values() if r['written'] or r['missing'])))
    say('\n'.join(lines))


def cmd_build(a):
    modes = parse_answer_modes(a.answer)
    used = set(modes.values()) | {a.mode}
    budget = None
    run = lambda date, **kw: run_lesson(date, a.work, a.mode, modes, a.agent05, a.refresh_input, a.first_form, a.env, **kw)
    if 'openai' in used:
        if a.max_usd is None:
            raise SystemExit('--max-usd is required when a pass is answered by openai')
        if not os.path.isfile(a.env):
            raise SystemExit('no key file: %s' % a.env)
        known = {}
        for date in a.dates:  # pre-flight: what the stored state still has to ask
            run(date, collector=known, dry_run=True)
        total = sum(known.values())
        say('estimate of what is known to be still to ask: $%.3f (limit $%.2f)' % (total, a.max_usd))
        if total > a.max_usd:
            say('stopped before asking anything: the estimate is over the limit')
            return EXIT_BUDGET
        budget = Budget(a.max_usd, known)
    results, code = [], 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, a.parallel)) as pool:
        futures = {pool.submit(run, d, budget=budget): d for d in a.dates}
        for f in concurrent.futures.as_completed(futures):
            date = futures[f]
            try:
                res = f.result()
            except BudgetExceeded as e:
                say('%s: stopped by the budget: %s' % (date, e))
                code = max(code, EXIT_BUDGET)
                continue
            except (ReplayMissing, BadAnswer) as e:
                say('%s: error: %s' % (date, e))
                code = max(code, EXIT_ERROR)
                continue
            results.append(res)
            report(res)
    total = sum(r.ledger.cost for r in results)
    say('total paid in this call: $%.4f in %d lesson(s)' % (total, len(results)))
    if code:
        return EXIT_BUDGET if code == EXIT_BUDGET else EXIT_ERROR
    if any(r.status not in ('done', 'ready') for r in results):
        if any(r.ledger.rows.get(k, {}).get('written') for r in results for k in r.ledger.rows):
            say('answers are to be written as files next to the .request.json files; then run the same command again')
            return EXIT_WAITING
        if a.mode == 'dry':
            return 0
        return EXIT_WAITING
    return 0


def cmd_publish(a):
    src = os.path.join(a.work, a.date, 'package.json')
    if not os.path.isfile(src):
        raise SystemExit('no %s' % src)
    target_dir = os.path.join(a.packages, a.date)
    target = os.path.join(target_dir, 'package.json')
    if os.path.exists(target) and not a.replace:
        raise SystemExit('%s exists; give --replace to overwrite it (marks.json is never touched)' % target)
    os.makedirs(target_dir, exist_ok=True)
    tmp = target + '.tmp'
    shutil.copyfile(src, tmp)
    os.replace(tmp, target)
    say('published %s' % target)
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(prog='python -m builder', description='Builder of the lesson package.')
    sub = ap.add_subparsers(dest='cmd', required=True)
    b = sub.add_parser('build', help='build the packages of one or more lessons')
    b.add_argument('dates', nargs='+', metavar='date', help='YYYY-MM-DD')
    b.add_argument('--work', required=True, help='work folder: <work>/<date>/...')
    b.add_argument('--mode', required=True, choices=MODES, help='how a missing answer is got')
    b.add_argument('--answer', action='append', metavar='PASS=MODE', help='mode of one pass (%s)' % ', '.join(PASSES))
    b.add_argument('--max-usd', type=float, help='limit of what an openai call may spend (required with openai)')
    b.add_argument('--parallel', type=int, default=3, help='lessons built at the same time')
    b.add_argument('--first-form', default='all', choices=('all', 'a', 'none'), help='requests of the first form to ask')
    b.add_argument('--agent05', default=DEFAULT_URL)
    b.add_argument('--refresh-input', action='store_true', help='read the input from agent05 again')
    b.add_argument('--env', default=DEFAULT_ENV, help='env file with the line of the OpenAI key (never printed)')
    p = sub.add_parser('publish', help='copy a built package.json to the packages folder')
    p.add_argument('date')
    p.add_argument('--work', required=True)
    p.add_argument('--packages', required=True)
    p.add_argument('--replace', action='store_true')
    a = ap.parse_args(argv)
    try:
        return cmd_build(a) if a.cmd == 'build' else cmd_publish(a)
    except SystemExit:
        raise
    except Exception as e:  # a readable message instead of a trace
        say('error: %s: %s' % (type(e).__name__, e))
        return EXIT_ERROR
