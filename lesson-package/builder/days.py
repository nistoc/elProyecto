"""`days`: a group of lesson days from recording to package, resumable, stopping before every paid step.

One call asks agent05 what each lesson still misses to have its text (`POST /api/sessions/batch`, `dryRun: true`),
starts the transcription only when `--transcribe-max-usd` is given, builds the lessons whose text is whole (the same
code as `build`) and, with `--publish`, copies the finished packages to the packages folder and makes the page
of each day there (`page.py`).
"""
import argparse
import json
import os
import urllib.error
import urllib.request

from .cli import say, cmd_build, cmd_publish, EXIT_ERROR, EXIT_WAITING, EXIT_BUDGET

EXIT_PAID, EXIT_QUEUED = 5, 6  # a paid or manual step is for the person to decide / transcription is queued or going
# The exit code of a call is the most urgent of all lessons: error, over the limit, a step for the person
# (paid start or failed track), answers to be written, transcription going, done.
URGENCY = (EXIT_ERROR, EXIT_BUDGET, EXIT_PAID, EXIT_WAITING, EXIT_QUEUED, 0)
KINDS = ('run', 'dialogue', 'wait', 'failed', 'duplicate', 'done', 'unconverted')


def worst(codes):
    codes = set(codes)
    for c in URGENCY:
        if c in codes:
            return c
    return 0


def http_post_json(url, body):
    """POST json; returns (status, parsed json or None). An HTTP error status is returned, not raised."""
    data = json.dumps(body).encode('utf-8')
    req = urllib.request.Request(url, data=data, method='POST',
                                 headers={'Content-Type': 'application/json', 'Accept': 'application/json'})
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            status, raw = r.status, r.read()
    except urllib.error.HTTPError as e:
        status, raw = e.code, e.read()
    try:
        return status, json.loads(raw.decode('utf-8')) if raw else None
    except ValueError:
        return status, None


def counts(steps):
    out = {}
    for s in steps:
        out[s.get('kind')] = out.get(s.get('kind'), 0) + 1
    return out


def describe(lesson):
    steps = lesson.get('steps') or []
    c = counts(steps)
    todo = [s for s in steps if s.get('kind') in ('run', 'failed')]
    minutes = sum(s.get('minutes') or 0 for s in todo)
    usd = sum(s.get('estimateUsd') or 0 for s in todo)
    kinds = ', '.join('%s %d' % (k, c[k]) for k in sorted(c, key=lambda k: KINDS.index(k) if k in KINDS else 99)) or 'no steps'
    return '%s: %s; %.1f min to transcribe, estimate $%.2f' % (lesson.get('date'), kinds, minutes, usd)


def classify(lesson, retry_failed):
    """'failed' | 'start' | 'wait' | 'ready' | 'nothing' for what the lesson needs before it can be built."""
    c = counts(lesson.get('steps') or [])
    if c.get('failed') and not retry_failed:
        return 'failed'
    if c.get('run') or c.get('dialogue') or (retry_failed and c.get('failed')):
        return 'start'
    if c.get('wait'):
        return 'wait'
    return 'ready' if c.get('done') else 'nothing'


def cmd_days(a, post=None):
    post = post or http_post_json
    url = a.agent05.rstrip('/') + '/api/sessions/batch'
    body = {'dates': list(a.dates), 'dryRun': True}
    if a.retry_failed:
        body['retryFailed'] = True
    status, doc = post(url, body)
    if status != 200 or not doc:
        say('error: agent05 answered %s to the plan of the days' % status)
        return EXIT_ERROR
    lessons = {l.get('date'): l for l in (doc.get('plan') or {}).get('lessons') or []}
    codes, ready, start, left = [], [], [], []
    for date in a.dates:
        lesson = lessons.get(date)
        if lesson is None:
            say('%s: no lesson of this date in the recordings folder' % date)
            codes.append(EXIT_ERROR)
            left.append(date)
            continue
        say(describe(lesson))
        for s in lesson.get('steps') or []:
            if s.get('kind') in ('failed', 'duplicate', 'unconverted'):
                say('   %s: %s%s' % (s['kind'], s.get('track') or s.get('period'), (' - ' + s['reason']) if s.get('reason') else ''))
        kind = classify(lesson, a.retry_failed)
        if kind == 'ready':
            ready.append(date)
            continue
        left.append(date)
        if kind == 'start':
            start.append(date)
        elif kind == 'wait':
            say('   a run is queued or going; not built yet')
            codes.append(EXIT_QUEUED)
        elif kind == 'failed':
            say('   a track ended without a transcript; --retry-failed (paid) starts it again')
            codes.append(EXIT_PAID)
        else:
            say('   no transcript to build from and nothing to start')
            codes.append(EXIT_PAID)
    if start:
        codes.append(start_transcription(a, post, url, start, lessons))
    if ready:
        say('building %d lesson(s) with text: %s' % (len(ready), ', '.join(ready)))
        codes.append(cmd_build(argparse.Namespace(**{**vars(a), 'dates': ready})))
        if a.publish:
            publish_all(a, ready)
    if left:
        say('not built in this call: %s' % ', '.join(left))
    return worst(codes)


def start_transcription(a, post, url, dates, lessons):
    usd = sum(s.get('estimateUsd') or 0 for d in dates for s in lessons[d].get('steps') or [])
    if a.transcribe_max_usd is None:
        say('transcription of %s is paid: estimate up to $%.2f; nothing was sent. Give --transcribe-max-usd <limit> to start it'
            % (', '.join(dates), usd))
        return EXIT_PAID
    body = {'dates': dates, 'dryRun': False, 'maxUsd': a.transcribe_max_usd}
    if a.retry_failed:
        body['retryFailed'] = True
    status, doc = post(url, body)
    if status == 409:
        say('not started: %s' % ((doc or {}).get('detail') or 'the estimate is above --transcribe-max-usd'))
        return EXIT_BUDGET
    if status not in (200, 202) or not doc:
        say('error: agent05 answered %s to the start of the transcription' % status)
        return EXIT_ERROR
    started, merged = doc.get('startedJobs') or [], doc.get('dialogues') or []
    say('queued: %d run(s), %d dialogue(s) merged; run the same command again later' % (len(started), len(merged)))
    bad = [s for l in (doc.get('plan') or {}).get('lessons') or [] for s in l.get('steps') or [] if s.get('kind') == 'failed']
    for s in bad:
        say('   failed to start: %s - %s' % (s.get('track') or s.get('period'), s.get('reason')))
    return EXIT_PAID if bad else EXIT_QUEUED


def publish_all(a, dates):
    """Publishes the finished packages of `dates`; one already in the packages folder is left and reported."""
    for date in dates:
        if not os.path.isfile(os.path.join(a.work, date, 'package.json')):
            say('%s: no package to publish yet' % date)
        elif os.path.exists(os.path.join(a.packages, date, 'package.json')):
            say('%s: already published, left as it is' % date)
        else:
            cmd_publish(argparse.Namespace(date=date, work=a.work, packages=a.packages, replace=False, commit=False))
    write_day_pages(a, dates)
    from .history import commit_days, message_of
    commit_days(a.packages, dates, message_of('days', dates))


def write_day_pages(a, dates):
    """The page of every day made anew from what agent05 gives now (free, GET only); a day it cannot be made for is
    reported and the call goes on."""
    from .page import Agent05, day_page, write_page
    api = Agent05(a.agent05)
    for date in dates:
        try:
            res = write_page(api, date, day_page(a.packages, date))
        except Exception as e:  # the page is not worth stopping the call for
            say('%s: page not made: %s: %s' % (date, type(e).__name__, e))
            continue
        say('%s: page %s' % (date, res[0]) if res else '%s: no package and no note - no page' % date)
