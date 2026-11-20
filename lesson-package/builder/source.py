"""The input of a lesson: read from agent05 once (GET only), stored under `<work>/<date>/input/`, then read from there.

  input/recordings.json  {"date", "periods": [{"n", "job", "start", "lines": [{"n", "at", "who", "text"}]}]}
  input/earlier.json     the answer of `GET /api/sessions/words?before=<date>`
  input/spelling_marks.json  optional: {slug: {"word", "right"}} - old spelling marks (a word with `right` is
                         written so, one without is left out of the words found by code)
"""
import json
import os
import urllib.parse
import urllib.request

from .ask import write_json

ROLE_LETTER = {'teacher': 'П', 'student': 'У'}
DEFAULT_URL = 'http://localhost:5010'


def speaker_letter(role):
    """Role of agent05 -> the letter a line goes to a model with: П teacher, У student, ? anything else."""
    return ROLE_LETTER.get(role, '?')


def http_get_json(url):
    req = urllib.request.Request(url, method='GET', headers={'Accept': 'application/json'})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read().decode('utf-8'))


def recording_clock(started_at, first_start):
    """HH:mm of the start of a recording: of `startedAt` (YYYY-MM-DDTHH:mm:ss), else of its first line."""
    if started_at and 'T' in started_at:
        return started_at.split('T', 1)[1][:5]
    s = int(float(first_start or 0))
    return '%d:%02d' % (s // 3600, (s // 60) % 60)


def periods_from_text(doc):
    """The answer of `GET /api/sessions/{date}/text` -> the periods of the builder (a recording is one period)."""
    periods = []
    for rec in doc.get('recordings', []):
        lines = []
        for l in rec.get('lines', []):
            text = (l.get('text') or '').strip()
            if not text:
                continue
            lines.append({'n': len(lines) + 1, 'at': l['start'], 'who': speaker_letter(l.get('role')), 'text': text})
        if not lines:
            continue
        job_ids = rec.get('jobIds') or []
        periods.append({'n': len(periods) + 1, 'job': job_ids[0] if job_ids else '',
                        'start': recording_clock(rec.get('startedAt'), lines[0]['at']), 'lines': lines})
    return periods


def read_input(date, folder, agent05=DEFAULT_URL, refresh=False, get=http_get_json):
    """Makes sure `folder/input/` holds the lesson's input; returns (periods, earlier json, marks)."""
    inp = os.path.join(folder, 'input')
    rec_path, earl_path = os.path.join(inp, 'recordings.json'), os.path.join(inp, 'earlier.json')
    base = agent05.rstrip('/')
    if refresh or not os.path.exists(rec_path):
        doc = get('%s/api/sessions/%s/text' % (base, date))
        periods = periods_from_text(doc)
        if not periods:
            raise SystemExit('%s: agent05 gave no lines for this lesson' % date)
        write_json(rec_path, {'date': date, 'periods': periods})
    if refresh or not os.path.exists(earl_path):
        write_json(earl_path, get('%s/api/sessions/words?before=%s' % (base, urllib.parse.quote(date))))
    with open(rec_path, encoding='utf-8') as f:
        periods = json.load(f)['periods']
    with open(earl_path, encoding='utf-8') as f:
        earlier = json.load(f)
    marks = {}
    marks_path = os.path.join(inp, 'spelling_marks.json')
    if os.path.exists(marks_path):
        with open(marks_path, encoding='utf-8') as f:
            marks = json.load(f)
    return periods, earlier, marks
