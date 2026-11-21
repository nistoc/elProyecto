"""The package page of a lesson: one self-contained HTML file for a claude.ai Artifact, read only.

It is made of what agent05 already gives (GET only, nothing is asked of a model):
  GET /api/sessions                       the lesson: its title, recordings, stage
  GET /api/sessions/{date}/package        the package as the app shows it (owner's edits on, "said before", "?")
  GET /api/sessions/{date}/notes          the notes of the owner's vault copied into the day folder
  GET /api/sessions/{date}/notes/text     one note's markdown
  GET /api/sessions/{date}/notes/file     a picture or a sound a note embeds

A lesson without a package gets a page of its notes alone; a lesson with neither gets no page. Pictures are made
smaller (Pillow when it is installed, else kept as they are while they fit) and put into the page as data URIs; a
sound is put in only while the page stays under its limit. The page is never the place of an edit: edits are made
in XtractManager and the page is made again.
"""
import base64
import io
import json
import os
import re
import urllib.error
import urllib.parse
import urllib.request

from .source import DEFAULT_URL

TEMPLATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'page_template.html')
PAGE_LIMIT = 14 * 1024 * 1024          # the Artifact limit is 16 MB; room is left for the page itself
PICTURE_WIDTH = 1400
PICTURE_QUALITY = 80
SOUND_LIMIT = 3 * 1024 * 1024          # one sound larger than this is not put in
PICTURE_TYPES = {'.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.gif': 'image/gif',
                 '.webp': 'image/webp', '.svg': 'image/svg+xml'}
SOUND_TYPES = {'.mp3': 'audio/mpeg', '.m4a': 'audio/mp4', '.wav': 'audio/wav', '.ogg': 'audio/ogg'}
KIND_ORDER = ['homework', 'summary', 'cards', 'dialogues', 'topics', 'transcript', 'log', 'other']
EMBED = re.compile(r'!\[\[([^\]|#]+)(?:#[^\]|]*)?(?:\|([^\]]*))?\]\]')
WIKI = re.compile(r'(?<!!)\[\[([^\]|#]+)(?:#([^\]|]*))?(?:\|([^\]]*))?\]\]')
COMMENT = re.compile(r'%%.*?%%', re.S)


def http_get(url, accept='application/json'):
    """(status, bytes) of a GET; a 404 is an answer, not an error."""
    req = urllib.request.Request(url, method='GET', headers={'Accept': accept})
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            return r.status, r.read()
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return 404, b''
        raise


class Agent05:
    """The GETs the page needs; `get` is replaced in the tests."""

    def __init__(self, base=DEFAULT_URL, get=http_get):
        self.base = base.rstrip('/')
        self.get = get
        self._sessions = None

    def _json(self, path):
        status, body = self.get(self.base + path)
        return None if status == 404 else json.loads(body.decode('utf-8'))

    def lessons(self):
        if self._sessions is None:
            self._sessions = (self._json('/api/sessions') or {}).get('sessions') or []
        return self._sessions

    def package(self, date):
        return self._json('/api/sessions/%s/package' % date)

    def notes(self, date):
        doc = self._json('/api/sessions/%s/notes' % date)
        return (doc or {}).get('notes') or []

    def note_text(self, date, path):
        status, body = self.get(self.base + '/api/sessions/%s/notes/text?path=%s' % (date, urllib.parse.quote(path)),
                                'text/markdown')
        return None if status == 404 else body.decode('utf-8', errors='replace')

    def note_file(self, date, name):
        status, body = self.get(self.base + '/api/sessions/%s/notes/file?name=%s' % (date, urllib.parse.quote(name)),
                                '*/*')
        return None if status == 404 else body


def lesson_of(lessons, date):
    """The lesson rows of the date (a lesson may have two day folders) merged into one description."""
    rows = [l for l in lessons if l.get('date') == date]
    if not rows:
        return None
    recordings, titles, stages = [], [], []
    for l in rows:
        if l.get('title'):
            titles.append(l['title'])
        stages.append(l.get('stage'))
        for p in l.get('periods') or []:
            tracks = p.get('tracks') or []
            if not tracks:
                continue
            minutes = max((t.get('durationSec') or 0) for t in tracks) / 60.0
            started = p.get('startedAt') or ''
            recordings.append({'start': started.split('T', 1)[1][:5] if 'T' in started else '',
                               'minutes': round(minutes, 1),
                               'participantTracks': sum(1 for t in tracks if t.get('kind') == 'participant')})
    return {'date': date, 'titles': titles, 'stages': stages, 'recordings': recordings}


def smaller_picture(data, ext):
    """A picture made at most PICTURE_WIDTH wide (JPEG, or PNG when it has see-through parts); as it was without Pillow."""
    try:
        from PIL import Image  # optional
    except ImportError:
        return data, PICTURE_TYPES.get(ext, 'application/octet-stream')
    if ext in ('.svg', '.gif'):
        return data, PICTURE_TYPES[ext]
    try:
        im = Image.open(io.BytesIO(data))
        im.load()
    except Exception:
        return data, PICTURE_TYPES.get(ext, 'application/octet-stream')
    if im.width > PICTURE_WIDTH:
        im = im.resize((PICTURE_WIDTH, max(1, round(im.height * PICTURE_WIDTH / im.width))))
    out = io.BytesIO()
    see_through = im.mode in ('RGBA', 'LA') or (im.mode == 'P' and 'transparency' in im.info)
    if see_through:
        im.save(out, 'PNG', optimize=True)
        kind = 'image/png'
    else:
        im.convert('RGB').save(out, 'JPEG', quality=PICTURE_QUALITY, optimize=True)
        kind = 'image/jpeg'
    small = out.getvalue()
    return (small, kind) if len(small) < len(data) else (data, PICTURE_TYPES.get(ext, kind))


def data_uri(data, kind):
    return 'data:%s;base64,%s' % (kind, base64.b64encode(data).decode('ascii'))


class Embeds:
    """Pictures and sounds of the notes of one lesson, each read once, within the size budget of the page."""

    def __init__(self, api, date, budget=PAGE_LIMIT):
        self.api, self.date, self.budget = api, date, budget
        self.used = 0
        self.cache = {}
        self.left_out = []

    def uri(self, name):
        if name in self.cache:
            return self.cache[name]
        ext = os.path.splitext(name)[1].lower()
        uri = None
        if ext in PICTURE_TYPES or ext in SOUND_TYPES:
            data = self.api.note_file(self.date, name)
            if data is not None:
                if ext in PICTURE_TYPES:
                    data, kind = smaller_picture(data, ext)
                else:
                    kind = SOUND_TYPES[ext]
                size = len(data) * 4 // 3 + 64
                if (ext in SOUND_TYPES and len(data) > SOUND_LIMIT) or self.used + size > self.budget:
                    self.left_out.append(name)
                else:
                    self.used += size
                    uri = data_uri(data, kind)
            else:
                self.left_out.append(name)
        self.cache[name] = uri
        return uri


def note_markdown(text, titles, embeds):
    """Obsidian markdown -> the markdown the page renders: comments dropped, embeds put in, wiki links made links."""
    text = COMMENT.sub('', text)

    def embed(m):
        name = m.group(1).strip()
        ext = os.path.splitext(name)[1].lower()
        uri = embeds.uri(name) if embeds else None
        if ext in SOUND_TYPES:
            return ('<audio controls preload="none" src="%s"></audio>' % uri) if uri else \
                '*🔊 звук «%s» — в XtractManager, кнопка «ДЗ»*' % name
        if ext in PICTURE_TYPES:
            return ('![%s](%s)' % (os.path.splitext(name)[0], uri)) if uri else '*🖼 картинка «%s» не вошла в страницу*' % name
        target = name if name in titles else None
        return '[%s](#note-%s)' % (name, titles[target]) if target else '*%s*' % name

    text = EMBED.sub(embed, text)

    def wiki(m):
        name, alias = m.group(1).strip(), (m.group(3) or '').strip()
        shown = alias or name
        return '[%s](#note-%s)' % (shown, titles[name]) if name in titles else shown

    return WIKI.sub(wiki, text)


def kept(items):
    return [i for i in items or [] if not i.get('leftOut')]


def page_data(api, date):
    """Everything the page shows, or None when the lesson has neither a package nor a note."""
    lesson = lesson_of(api.lessons(), date) or {'date': date, 'titles': [], 'stages': [], 'recordings': []}
    view = api.package(date)
    notes = sorted(api.notes(date), key=lambda n: (KIND_ORDER.index(n.get('kind')) if n.get('kind') in KIND_ORDER
                                                   else len(KIND_ORDER), n.get('title') or ''))
    if view is None and not notes:
        return None
    titles = {n['title']: str(i) for i, n in enumerate(notes) if n.get('title')}
    embeds = Embeds(api, date)
    out_notes = []
    for i, n in enumerate(notes):
        text = api.note_text(date, n['path'])
        if text is None:
            continue
        out_notes.append({'id': str(i), 'title': n.get('title') or n['path'], 'kind': n.get('kind') or 'other',
                          'markdown': note_markdown(text, titles, embeds)})
    data = {'date': date, 'lesson': lesson, 'notes': out_notes, 'leftOutMedia': embeds.left_out, 'package': None}
    if view is not None:
        p = view['package']
        data['package'] = {
            'builtAt': p.get('builtAt'),
            'periods': p.get('periods') or [],
            'topics': kept(p.get('topics')),
            'grammar': kept(p.get('grammar')),
            'wordNotes': kept(p.get('wordNotes')),
            'examples': kept(p.get('examples')),
            'newWords': kept(p.get('newWords')),
            'vocabulary': kept(p.get('vocabulary')),
            'otherWords': kept(p.get('otherWords')),
            'compared': view.get('compared'),
            'counts': view.get('counts'),
        }
    return data


def human_date(date):
    y, m, d = date.split('-')
    return '%s.%s.%s' % (d, m, y)


def render(data, template=None):
    """The page: the template with the title and the data put in (no `<` of the data can end or open a script)."""
    if template is None:
        with io.open(TEMPLATE, encoding='utf-8') as f:
            template = f.read()
    blob = json.dumps(data, ensure_ascii=False, separators=(',', ':')).replace('<', '\\u003c')
    title = 'Пакет урока %s' % human_date(data['date']) if data.get('package') else 'Заметки урока %s' % human_date(data['date'])
    return template.replace('{{title}}', title).replace('{{data}}', blob)


def write_page(api, date, out_dir):
    """Writes `<out_dir>/<date>.html`; returns (path, size, media left out) or None when there is nothing to show."""
    data = page_data(api, date)
    if data is None:
        return None
    html = render(data)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, '%s.html' % date)
    tmp = path + '.tmp'
    with io.open(tmp, 'w', encoding='utf-8', newline='') as f:
        f.write(html)
    os.replace(tmp, path)
    return path, len(html.encode('utf-8')), data['leftOutMedia']
