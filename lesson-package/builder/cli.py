"""Command line: `python -m builder build <date> ... --work <folder> --mode ...`, `days`, `page` and `publish`."""
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
        if r.get('cut'):
            lines.append('   %-6s %d answer(s) cut at max_tokens: whole items kept, %d repeat(s) dropped%s' % (
                key, r['cut'], r['repeats'], ' (the model looped)' if r['repeats'] else
                ' - the list was longer than the room: items after the cut are missing (see <name>.cut.json)'))
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
    reported = sum(r.ledger.cost for r in results)
    total = max(budget.spent, reported) if budget else reported  # the budget saw every paid request
    lost = total - reported
    say('total paid in this call: $%.4f in %d lesson(s)%s' % (total, len(results), (
        '; $%.4f of it in lessons that stopped (their answers are stored)' % lost) if lost >= 0.00005 else ''))
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
    if getattr(a, 'commit', True):  # `days` commits its packages and pages at once
        from .history import commit_days, message_of
        commit_days(a.packages, [a.date], message_of('package', [a.date]))
    return 0


def cmd_page(a):
    from .page import Agent05, day_page, write_page
    api = Agent05(a.agent05)
    made = []
    for date in a.dates:
        path = day_page(a.packages, date) if a.packages else os.path.join(a.out, '%s.html' % date)
        res = write_page(api, date, path)
        if res is None:
            say('%s: no package and no note - no page' % date)
            continue
        path, size, left_out = res
        made.append(date)
        say('%s: %s, %.1f MB%s' % (date, path, size / 1048576.0,
                                    ', %d media file(s) left out' % len(left_out) if left_out else ''))
    if made and a.packages:
        from .history import commit_days, message_of
        commit_days(a.packages, made, message_of('page', made))
    return 0 if made else EXIT_ERROR


def main(argv=None):
    ap = argparse.ArgumentParser(prog='python -m builder', description='Builder of the lesson package.')
    sub = ap.add_subparsers(dest='cmd', required=True)
    def build_options(b):
        b.add_argument('--work', required=True, help='work folder: <work>/<date>/...')
        b.add_argument('--mode', required=True, choices=MODES, help='how a missing answer is got')
        b.add_argument('--answer', action='append', metavar='PASS=MODE', help='mode of one pass (%s)' % ', '.join(PASSES))
        b.add_argument('--max-usd', type=float, help='limit of what an openai call may spend (required with openai)')
        b.add_argument('--parallel', type=int, default=3, help='lessons built at the same time')
        b.add_argument('--first-form', default='a', choices=('a', 'all', 'none'),
                       help='requests of the first form to ask (default a: request A alone; all: A, B and C)')
        b.add_argument('--agent05', default=DEFAULT_URL)
        b.add_argument('--refresh-input', action='store_true', help='read the input from agent05 again')
        b.add_argument('--env', default=DEFAULT_ENV, help='env file with the line of the OpenAI key (never printed)')

    b = sub.add_parser('build', help='build the packages of one or more lessons')
    b.add_argument('dates', nargs='+', metavar='date', help='YYYY-MM-DD')
    build_options(b)
    d = sub.add_parser('days', help='a group of days from recording to package; paid transcription only on request')
    d.add_argument('dates', nargs='+', metavar='date', help='YYYY-MM-DD')
    build_options(d)
    d.add_argument('--transcribe-max-usd', type=float, help='start the missing transcriptions (paid) when their estimate is within this')
    d.add_argument('--retry-failed', action='store_true', help='start a track again whose runs ended without a transcript (paid)')
    d.add_argument('--publish', action='store_true',
                   help='publish the packages that are done (never replaces one) and make the page of each day')
    d.add_argument('--packages', help='packages folder (required with --publish)')
    g = sub.add_parser('page', help='the page of a lesson day: one HTML file made from its data (read only); free, GET only')
    g.add_argument('dates', nargs='+', metavar='date', help='YYYY-MM-DD')
    where = g.add_mutually_exclusive_group(required=True)
    where.add_argument('--packages', help='packages folder: the page of a day is <packages>/<date>/lesson.html')
    where.add_argument('--out', help='any folder: <out>/<date>.html (to look at a page before it is put in place)')
    g.add_argument('--agent05', default=DEFAULT_URL)
    p = sub.add_parser('publish', help='copy a built package.json to the packages folder')
    p.add_argument('date')
    p.add_argument('--work', required=True)
    p.add_argument('--packages', required=True)
    p.add_argument('--replace', action='store_true')
    a = ap.parse_args(argv)
    if a.cmd == 'days' and a.publish and not a.packages:
        ap.error('--publish needs --packages')
    try:
        if a.cmd == 'days':
            from .days import cmd_days
            return cmd_days(a)
        if a.cmd == 'page':
            return cmd_page(a)
        return cmd_build(a) if a.cmd == 'build' else cmd_publish(a)
    except SystemExit:
        raise
    except Exception as e:  # a readable message instead of a trace
        say('error: %s: %s' % (type(e).__name__, e))
        return EXIT_ERROR
