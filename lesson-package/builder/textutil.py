"""Small text helpers shared by the stages."""
import re
import unicodedata

LATIN = r"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+"


def plain(s):
    """Lower case, no accents: the form words are compared in."""
    return ''.join(c for c in unicodedata.normalize('NFD', s.lower()) if not unicodedata.combining(c))


def letters(s):
    """Only the letters and digits, lower case: a quote and a line are compared by these."""
    return re.sub(r'[^\w]', '', s, flags=re.U).lower()


def squeeze(s):
    return re.sub(r'\s+', ' ', s).strip().lower()


def latin_words(text):
    return [plain(w) for w in re.findall(LATIN, text)]


def clock(at):
    """Seconds -> m:ss."""
    s = float(at)
    return '%d:%02d' % (int(s) // 60, int(s) % 60)


def slug(word):
    """Text in lower case without accents, runs of other characters a dash, at most 120 characters."""
    return (re.sub(r'[^a-z0-9]+', '-', plain(word)).strip('-') or 'x')[:120]


def window(text, word, before=110, after=150):
    """The piece of the line around the word, cut at spaces. Verbatim, so it serves as a quote."""
    i = text.find(word)
    if i < 0 or len(text) <= before + after:
        return text
    a = max(0, i - before)
    b = min(len(text), i + len(word) + after)
    if a > 0:
        a = text.find(' ', a) + 1
    if b < len(text):
        b = text.rfind(' ', 0, b)
    return text[a:b]


def phrase(text, piece):
    """The line as the screen shows it: whole when short, else the part around the piece, cut at spaces."""
    if len(text) <= 300:
        return text
    i = text.find(piece) if piece else -1
    if i < 0 and piece:
        i = text.lower().find(piece.lower())
    if i < 0:
        i = 0
    a, b = max(0, i - 110), min(len(text), i + len(piece or '') + 170)
    if a > 0:
        a = text.find(' ', a) + 1
    if b < len(text):
        b = text.rfind(' ', 0, b)
    return ('… ' if a > 0 else '') + text[a:b] + (' …' if b < len(text) else '')


def verdict(lines, n, quote):
    """How a quote is found: (check, line number). exact: in the named line as it is; close: there by its letters
    alone; other: by its letters in the nearest other line; none."""
    q, ql = squeeze(quote), letters(quote)
    if not ql:
        return 'none', None
    if 1 <= n <= len(lines):
        t = lines[n - 1]['text']
        if q in squeeze(t):
            return 'exact', n
        if ql in letters(t):
            return 'close', n
    for k in sorted(range(1, len(lines) + 1), key=lambda k: abs(k - n)):
        if ql in letters(lines[k - 1]['text']):
            return 'other', k
    return 'none', None
