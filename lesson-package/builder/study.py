"""The old notes of a lesson rebuilt into small study items, by code only (free, made anew on every build).

The notes of the owner's vault are long documents of an earlier parse: a summary with sections of grammar, words,
speech models, corrections and practice; homework; card lists; dialogues; topics; logs. A page that shows them as
they are shows huge blocks. Here every note is cut at its headings into small sections, each section is told by its
heading what it holds, and the lines of known shapes are taken out as items:

  words     `- **es** — ru`, `es;ru` (card lists), a table row with a Spanish and a Russian cell
  phrases   `ES: … ; RU: …`, `ES: … — RU: …`, and a word line of more than four words
  mistakes  `wrong → right` in a section of corrections or typical mistakes
  cards     a section of grammar, a topic, a dialogue part or another text: title, a short text, its examples
  tasks     a homework or practice section: its text, the numbered items to answer, the keys when the note has them

Sections that only tell how the notes were made (coverage checks, "moved to canvas …", file lists) are dropped. The
notes themselves stay on the page under "sources", folded.
"""
import re

LATIN = re.compile(r'[A-Za-zÁÉÍÓÚÑÜáéíóúñü]')
CYR = re.compile(r'[А-Яа-яЁё]')
HEAD = re.compile(r'^(#{1,6})\s+(.*?)\s*#*\s*$')
ES_RU = re.compile(r'^\s*(?:[-*+]|\d+[.)])?\s*ES:\s*(.+?)\s*(?:;|—|–|\s-\s)\s*RU:\s*(.+)$', re.I)
PAIR = re.compile(r'^\s*(?:[-*+]|\d+[.)]|[a-zа-я]\))?\s*(.+?)\s+(?:—|–|-|=)\s+(.+)$')
SEMI = re.compile(r'^\s*(?:[-*+]\s+)?([^;]{1,90});([^;]{1,140})$')
ARROW = re.compile(r'^\s*(?:[-*+]|\d+[.)])?\s*(.+?)\s*(?:→|->|⇒)\s*(.+)$')
ITEM = re.compile(r'^\s*(?:\d+[.)]|[a-zа-я]\))\s+(.+)$')
BLANK = re.compile(r'_{3,}|(?:…|\.{3})\s*$|\((?:[a-záéíóúñ]+(?:\s[a-záéíóúñ]+)?)\)')
KEYS_LINE = re.compile(r'^\s*(?:[-*+>]\s*)?[*_]*\s*(?:ключи|ключ|ответы|answers|soluciones)\s*[*_]*\s*:', re.I)
BULLET = re.compile(r'^\s*[-*+]\s+(?!\[[ xX]\])(.+)$')
GAP_MARK = 'QQGAPQQ'  # a gap of a line to answer kept through clean(), whose emphasis rule would eat underscores

SKIP = ('coverage', 'aux canvas', 'перенесено', 'отражено', 'транскрипты (каждый', 'источник', 'нормализаци',
        'журнал', 'лог ', 'метаданн', 'служебн', 'чеклист', 'чек-лист')
KINDS = (  # first match wins; the heading of the section, then its parents
    ('keys', ('ключ', 'ответы', 'answers', 'soluciones')),
    ('mistakes', ('исправлен', 'ошибк', 'примечани', 'errores')),
    ('practice', ('практик', 'упражнен', 'самопровер', 'переведите', 'переведи', 'pon el verbo', 'заполни', 'проспряга',
                  'ejercicio', 'quiz', 'тест', 'проверь себя', 'проверить себя', 'мини-диалог')),
    ('phrases', ('модели речи', 'реплик', 'фраз', 'шаблон', 'выражени', 'frases', 'модель')),
    ('words', ('лексик', 'лексикон', 'словар', 'слова', 'существительн', 'глагол', 'прилагательн', 'наречи', 'vocabul',
               'palabras', 'anki', 'карточ')),
    ('grammar', ('граммат', 'правил', 'образован', 'когда используется', 'особенност', 'таблиц', 'спряжени', 'gramática',
                 'subjuntivo', 'indicativo', 'imperfecto', 'indefinido', 'perfecto', 'pretérito', 'condicional',
                 'imperativo', 'степен', 'артикл', 'предлог', 'местоимени', 'клитик')),
    ('homework', ('домашн', 'дз', 'tarea', 'задани', 'сдать', 'к следующему уроку')),
)
NOTE_DEFAULT = {'homework': 'homework', 'cards': 'words', 'dialogues': 'dialogue', 'topics': 'topic',
                'summary': 'reading', 'other': 'reading', 'log': None, 'transcript': None}
CARD_LINES = 8  # a card shows this many lines; the rest is folded
HOMEWORK_TITLE = re.compile(r'(?<!\w)(?:tareas?|домашн\w*|дз)(?!\w)', re.I)  # homework named in any note


def clean(text):
    """A line of markdown as plain text: emphasis, highlights, links and leading list marks taken away."""
    t = re.sub(r'!\[\[[^\]]*\]\]', '', text)
    t = re.sub(r'\[\[([^\]|#]+)(?:#[^\]|]*)?(?:\|([^\]]*))?\]\]', lambda m: m.group(2) or m.group(1), t)
    t = re.sub(r'\[([^\]]+)\]\([^)]*\)', r'\1', t)
    t = re.sub(r'(\*\*|__|==|~~|`)', '', t)
    t = re.sub(r'(?<![\w])[*_](?=\S)(.+?)(?<=\S)[*_](?![\w])', r'\1', t)
    t = re.sub(r'^\s*(?:[-*+]|\d+[.)])\s+', '', t)
    t = re.sub(r'\[(?:добавлено|added|доп\.?)\]\s*', '', t, flags=re.I)
    t = re.sub(r'^[^\w¿¡«"(\[]+', '', t)  # emoji, marks and stray emphasis at the start
    return re.sub(r'\s+', ' ', t).strip(' \t:;,.*_')


def heading_text(title):
    """A heading without its emoji, numbering and marks, for reading and for telling its kind."""
    t = clean(title)
    t = re.sub(r'^[^\wА-Яа-яЁё¿¡]+', '', t)
    t = re.sub(r'^(?:\d+(?:\.\d+)*|[A-ZА-Я])[).:]\s*', '', t)
    return t.strip()


def spanish(text):
    lat, cyr = len(LATIN.findall(text)), len(CYR.findall(text))
    return lat >= 2 and lat > 2 * cyr


def russian(text):
    lat, cyr = len(LATIN.findall(text)), len(CYR.findall(text))
    return cyr >= 2 and cyr >= lat


def title_kind(title, note_kind):
    low = title.lower()
    for kind, keys in KINDS:
        if any(k in low for k in keys):
            if kind == 'homework' and note_kind != 'homework' and not HOMEWORK_TITLE.search(low):
                continue
            return kind
    return None


def kind_of(titles, note_kind):
    """What a section holds; None = dropped. A heading that only tells how the notes were made drops the section;
    keys are told by the section's own heading; a practice heading anywhere above makes the section practice (its
    "A) Grammar" is an exercise, not a rule); else the nearest heading that tells a kind, else the kind of the note.
    In a homework note every section is a task (the words in it are still taken)."""
    if any(any(s in t.lower() for s in SKIP) for t in titles):
        return None
    kinds = [title_kind(t, note_kind) for t in titles]
    if kinds and kinds[-1] == 'keys':
        return 'keys'
    if 'practice' in kinds:
        return 'practice'
    nearest = next((k for k in reversed(kinds) if k and k != 'keys'), None) or NOTE_DEFAULT.get(note_kind, 'reading')
    if note_kind == 'homework' and nearest not in ('practice', 'mistakes'):
        return 'homework'
    return nearest


def sections(markdown):
    """The note cut at its headings: [{titles: [h1 … this], lines: [...]}] — every heading starts a section."""
    out, path = [], []
    cur = {'titles': [], 'lines': []}
    fence = False
    for line in markdown.split('\n'):
        if line.strip().startswith('```'):
            fence = not fence
        m = None if fence else HEAD.match(line)
        if m:
            if cur['lines'] and any(l.strip() for l in cur['lines']):
                out.append(cur)
            level = len(m.group(1))
            path = [p for p in path if p[0] < level] + [(level, heading_text(m.group(2)) or m.group(2).strip())]
            cur = {'titles': [p[1] for p in path], 'lines': []}
        else:
            cur['lines'].append(line)
    if any(l.strip() for l in cur['lines']):
        out.append(cur)
    return out


def table_rows(lines):
    """Rows of the markdown tables of the lines: [[cell, …], …] without the header line and the rule."""
    rows, header = [], None
    for l in lines:
        s = l.strip()
        if not s.startswith('|'):
            header = None
            continue
        cells = [clean(c) for c in s.strip('|').split('|')]
        if all(re.fullmatch(r':?-{2,}:?', c.replace(' ', '')) or not c for c in cells):
            continue
        if header is None:
            header = cells
            continue
        rows.append(cells)
    return rows


def word_pair(line):
    """(es, ru) of a line that pairs a Spanish word or phrase with its Russian meaning, else None."""
    if line.lstrip().startswith('|'):
        return None  # a table row is read by its cells
    m = ES_RU.match(line)
    if m:
        es, ru = clean(m.group(1)), clean(m.group(2))
        return (es, ru) if es and ru else None
    m = SEMI.match(line.strip())
    if m and spanish(m.group(1)) and russian(m.group(2)):
        return clean(m.group(1)), clean(m.group(2))
    m = PAIR.match(line)
    if m:
        es, ru = clean(m.group(1)), clean(m.group(2))
        if es and ru and spanish(es) and russian(ru) and len(es) <= 120:
            return es, ru
    return None


def is_phrase(es):
    return len(es.split()) > 4 or bool(re.search(r'[.?!¿¡]', es))


class Study:
    """Collects the items of the notes of one lesson; `result()` is what the page gets."""

    def __init__(self):
        self.words, self.phrases, self.mistakes, self.cards, self.tasks = [], [], [], [], []
        self._seen_w, self._seen_p, self._seen_m = set(), set(), set()

    def add_pair(self, es, ru, group, phrase=None):
        if phrase is None:
            phrase = is_phrase(es)
        key = re.sub(r'\W+', ' ', es.lower()).strip()
        if not key:
            return
        seen, target = (self._seen_p, self.phrases) if phrase else (self._seen_w, self.words)
        if key in seen:
            return
        seen.add(key)
        target.append({'es': es, 'ru': ru, 'group': group})

    def add_mistake(self, wrong, right, note, group):
        key = (wrong.lower(), right.lower())
        if key in self._seen_m or not right:
            return
        self._seen_m.add(key)
        self.mistakes.append({'wrong': wrong, 'right': right, 'note': note, 'group': group})

    def note(self, note_id, title, kind, markdown):
        last_task = None
        for n, sec in enumerate(sections(markdown)):
            titles = sec['titles'] or [title]
            what = kind_of(titles, kind)
            if what is None:
                continue
            group = titles[-1]
            lines = [l for l in sec['lines'] if l.strip() and l.strip() != '---']
            if not lines:
                continue
            ident = '%s-%d' % (note_id, n)
            if what == 'keys':
                if last_task is not None:
                    last_task['keys'] = (last_task.get('keys') or '') + '\n'.join(lines) + '\n'
                else:
                    self.cards.append(self.card(ident, 'reading', titles, lines, note_id))
                continue
            if what == 'mistakes':
                for l in lines:
                    m = ARROW.match(l)
                    if m and not l.strip().startswith('|'):
                        self.add_mistake(clean(m.group(1)), clean(m.group(2)), '', group)
                        continue
                    p = ES_RU.match(l)
                    if p:
                        self.add_mistake('', clean(p.group(1)), clean(p.group(2)), group)
                continue
            if what != 'dialogue':  # the pairs of every section are taken; a card keeps its ES/RU lines as examples
                for l in lines:
                    pair = word_pair(l)
                    if pair:
                        self.add_pair(pair[0], pair[1], group, True if ES_RU.match(l) else None)
                if what != 'grammar':
                    for row in table_rows(lines):
                        self.row_pair(row, group)
            if what in ('words', 'phrases'):
                continue
            if what in ('practice', 'homework'):
                task = self.task(ident, what, titles, lines, note_id)
                self.tasks.append(task)
                last_task = task
                continue
            self.cards.append(self.card(ident, what, titles, lines, note_id))

    def row_pair(self, row, group):
        es = next((c for c in row if spanish(c) and len(c) <= 120), None)
        ru = next((c for c in row if russian(c)), None)
        if es and ru:
            self.add_pair(es, ru, group)

    @staticmethod
    def card(ident, kind, titles, lines, note_id):
        examples, body = [], []
        for l in lines:
            m = ES_RU.match(l)
            if m and kind != 'dialogue':
                examples.append({'es': clean(m.group(1)), 'ru': clean(m.group(2))})
            else:
                body.append(l)
        return {'id': ident, 'kind': kind, 'title': titles[-1], 'path': titles[:-1], 'note': note_id,
                'md': '\n'.join(body).strip(), 'examples': examples}

    @staticmethod
    def task(ident, kind, titles, lines, note_id):
        items, body, keys, translate = [], [], [], 'перев' in ' '.join(titles).lower()
        for l in lines:
            if KEYS_LINE.match(l):
                keys.append(l)  # "Keys: 1) …; 2) …" in the task's own text would give the answers away
                continue
            m = ITEM.match(l) or (BULLET.match(l) if re.search(r'_{3,}', l) else None)
            if m and (BLANK.search(m.group(1)) or (translate and russian(m.group(1)))):
                line = clean(re.sub(r'_{3,}|(?:…|\.{3})\s*$', ' %s ' % GAP_MARK, m.group(1)))
                items.append(re.sub(r'\s*%s\s*' % GAP_MARK, ' ____ ', line).strip())  # a line to answer, not in the text
            else:
                body.append(l)
        return {'id': ident, 'kind': kind, 'title': titles[-1], 'path': titles[:-1], 'note': note_id,
                'md': '\n'.join(body).strip(), 'items': items, 'keys': '\n'.join(keys) + '\n' if keys else None}

    def result(self):
        return {'words': self.words, 'phrases': self.phrases, 'mistakes': self.mistakes, 'cards': self.cards,
                'tasks': self.tasks}


def study_of(notes):
    """`notes`: [{id, title, kind, markdown}] (the markdown as the page shows it) -> the study items of the lesson."""
    s = Study()
    for n in notes:
        if NOTE_DEFAULT.get(n.get('kind'), 'reading') is None:
            continue
        s.note(n['id'], n.get('title') or '', n.get('kind') or 'other', n.get('markdown') or '')
    return s.result()
