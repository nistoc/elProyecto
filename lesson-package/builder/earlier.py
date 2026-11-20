"""The earlier lessons as the builder knows them: per lesson the plain Latin forms said in it.

The forms come from agent05 (`GET /api/sessions/words?before=YYYY-MM-DD`), stored in `input/earlier.json`.
A lesson entry may also carry `text` (the plain Latin words of its lines, line after line, joined by newlines),
which agent05 does not send; when present, a phrase is looked up in it exactly.
"""
import json
import re

from .textutil import LATIN, latin_words, plain

# Small and frequent words that are never put in doubt.
SMALL = set('''de la el un una unos unas a en con por para que se lo los las del al y o e u no si su mi tu me te le nos es
    pero como muy mas más bien bueno vale claro esto eso esta este estos estas esa ese hay ser estar tener hacer
    soy eres somos son era fue está están estoy estaba tengo tiene tienen puede puedo quiero también cuando donde porque
    ahora aquí allí entonces bueno pues algo nada todo toda todos todas mucho mucha muchos muchas poco otro otra otros otras
    sobre entre hasta desde hacia sin según cada qué cómo cuál quién dónde cuándo ella ellos ellas nosotros vosotros usted
    yes not and the for with that this what have from'''.split())
SMALL_P = set(plain(w) for w in SMALL)
SMALL_SEEN = set('de la el un una unos unas a en con por para que se lo los las del al y o e u no si su mi tu me te le nos es'.split())
PLACE = set('gerundio infinitivo participio algo alguien sustantivo adjetivo verbo tiempo'.split())


def stem(f):
    return f[:max(4, len(f) - 2)]


class Slot:
    def __init__(self, forms, text=None):
        self.forms = set(plain(f) for f in forms)
        self.prefixes = set(f[:k] for f in self.forms for k in range(3, len(f) + 1))
        self.text = text

    def known(self, form):
        """Was this form, or a form of the same word, said in the lesson? The same word: the beginning is the same
        but for the last two letters, and never less than four letters of it."""
        return form in self.forms or (len(form) >= 5 and stem(form) in self.prefixes)


class Earlier:
    def __init__(self, slots):
        self.slots = slots  # date -> Slot

    def __len__(self):
        return len(self.slots)

    def __bool__(self):
        return bool(self.slots)

    def seen_in(self, form):
        return sum(1 for s in self.slots.values() if s.known(form))

    def unseen(self, form):
        return not any(s.known(form) for s in self.slots.values())

    @classmethod
    def from_json(cls, data, lesson):
        """Only lessons strictly before `lesson` (YYYY-MM-DD, compared as whole dates)."""
        slots = {}
        for item in data.get('lessons', []):
            if item['date'] < lesson:
                slots[item['date']] = Slot(item.get('forms', []), item.get('text'))
        return cls(slots)

    @classmethod
    def load(cls, path, lesson):
        with open(path, encoding='utf-8') as f:
            return cls.from_json(json.load(f), lesson)


def candidates(lines, held_words, earlier, min_cyr=2, max_lessons=None):
    """The words put in doubt: a Latin-letter word that the lists do not hold and that (a) stands in a line among
    Russian words ('quoted'), or (b) no earlier lesson has ('unheard'). One candidate per word: its first line of
    kind (a), else (b). Returns [{word, n, kind, lessons}] in the order of the lines."""
    held = set(stem(plain(w)) for w in held_words if len(w) >= 4)
    out, taken = [], {}
    for l in lines:
        words = re.findall(LATIN, l['text'])
        cyr = len(re.findall(r'[А-Яа-яЁё]+', l['text']))
        for w in words:
            f = plain(w)
            if len(f) < 4 or f in SMALL_P or stem(f) in held:
                continue
            lessons = earlier.seen_in(f)
            kind = None
            if cyr >= min_cyr and (max_lessons is None or lessons <= max_lessons):
                kind = 'quoted'
            elif lessons == 0 and earlier and not w[0].isupper():
                kind = 'unheard'
            if not kind:
                continue
            k = stem(f)
            if k in taken:
                # a word quoted in Russian speech wins over the same word found only as "unheard"
                if kind == 'quoted' and taken[k]['kind'] == 'unheard':
                    taken[k].update(word=w, n=l['n'], kind=kind)
                continue
            c = {'word': w, 'n': l['n'], 'kind': kind, 'lessons': lessons}
            taken[k] = c
            out.append(c)
    return sorted(out, key=lambda c: c['n'])


def seen_before(earlier, es, quote):
    """The dates of the earlier lessons that have the word. None when the entry is a pattern, not a word."""
    words = latin_words(re.sub(r'\([^)]*\)', ' ', es))
    content = [w for w in words if w not in SMALL_SEEN and w not in PLACE and len(w) >= 3]
    if not content:
        return None
    dates = []
    for date, slot in earlier.slots.items():
        if len(content) == 1:
            w = content[0]
            said = [q for q in latin_words(quote) if len(q) >= 4 and q[:4] == w[:4]]
            stem_ = re.sub(r'(se)?$', '', w)
            stem_ = stem_[:-2] if re.search(r'(ar|er|ir)$', stem_) and len(stem_) >= 6 else None
            if any(slot.known(f) for f in [w] + said) or (stem_ and stem_ in slot.prefixes):
                dates.append(date)
        else:
            phrase_ = ' '.join(w for w in words if w not in PLACE)
            if '+' in es:  # a pattern (llevar + gerundio): the first word of it
                if slot.known(content[0]):
                    dates.append(date)
            elif slot.text is not None:
                if re.search(r'(^|\s)%s(\s|$)' % re.escape(phrase_), slot.text):
                    dates.append(date)
            elif all(slot.known(w) for w in content):  # no text of the lesson: every word of the phrase is there
                dates.append(date)
    return sorted(dates)
