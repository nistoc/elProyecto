"""The free assembling step: stored answers and lists -> `package.json` (CONTRACT.md). Asks nothing.

It also makes the list of the words found only by code (`bycode_todo.json`) when that is not stored yet.
"""
import collections
import datetime
import json
import os
import re

from . import VERSION, bycode, first_form, grammar_words
from .ask import dollars, read_answer, write_json
from .context import load_json
from .earlier import seen_before
from .lists import candidates_of
from .textutil import clock, latin_words, letters, phrase, plain, slug, verdict

WHO = {'П': 'teacher', 'У': 'student'}
TRANSLIT = re.compile('k|w|sh|zh|ts|ch[^aeiouáéíóú]|[^aeiou]y[^aeiou]')  # letters a Spanish word hardly has
CLITIC = ('la', 'lo', 'le', 'se', 'me', 'te', 'los', 'las', 'les', 'nos')
KIND_ORDER = {'spanish': 0, 'marked': 1, 'russian': 2, 'maybe_russian': 3, 'other': 4, 'broken': 5, '': 6}
FILTER_MODEL = grammar_words.FILTER_MODEL


def key_of(es):
    return plain(re.sub(r'\([^)]*\)', '', es)).strip()


def dedupe(items, taken):
    out = []
    for w in items:
        k = key_of(w['es'])
        if k in taken:
            taken[k]['again'] = taken[k].get('again', 0) + 1
            continue
        taken[k] = w
        out.append(w)
    return out


def forms_of(es):
    """An entry of one word: the word, its singular, the verb without the pronoun stuck to it."""
    wds = latin_words(re.sub(r'\([^)]*\)', ' ', es))
    if len(wds) != 1:
        return set()
    w = plain(wds[0])
    out = {w}
    if w.endswith('s') and len(w) > 4:
        out.add(w[:-1])
        if w.endswith('es'):
            out.add(w[:-2])
    for c in CLITIC:
        if w.endswith(c) and w[:-len(c)].endswith(('ar', 'er', 'ir')):
            out.add(w[:-len(c)])
    return out


class Lines:
    """The lines of every period, for the places of the items."""

    def __init__(self, periods):
        self.by_period = {p['n']: p['lines'] for p in periods}

    def find(self, period, at, who, piece):
        """The line of this time and voice that has the piece; any line of this time otherwise."""
        if not at:
            return None
        same = [l for l in self.by_period.get(period, []) if clock(l['at']) == at]
        low = (piece or '').lower()
        for want_who, want_piece in ((True, True), (False, True), (True, False), (False, False)):
            for l in same:
                if want_who and who and l['who'] != who:
                    continue
                if want_piece and low and low not in l['text'].lower():
                    continue
                return l
        return None

    def by_n(self, period, n):
        for l in self.by_period.get(period, []):
            if l['n'] == n:
                return l
        return None

    def last(self, period):
        ls = self.by_period.get(period, [])
        return ls[-1] if ls else None


def place(item, lines, piece=None):
    period = item.get('period') or 0
    at = item.get('at') or None
    line = lines.find(period, at, item.get('who'), piece if piece is not None else (item.get('said') or item.get('quote')))
    return {'period': period, 'at': at, 'sec': round(float(line['at']), 2) if line else None,
            'who': WHO.get(item.get('who') or '', ''), 'line': item.get('line'), 'quote': item.get('quote'),
            'said': (line['text'] if line else None) or item.get('said'), 'check': item.get('check')}


def export_grammar(items, lines):
    out = []
    for g in items:
        out.append({'topic': g.get('topic'), 'theme': g.get('theme'), 'rule': g.get('rule'), 'kind': g.get('kind'),
                    'period': g.get('period') or 0, 'points': g.get('points') or 1,
                    'examples': [place(x, lines) for x in g.get('examples') or []],
                    'parts': [{'topic': p.get('topic'), 'rule': p.get('rule'), 'examples': [place(x, lines) for x in p.get('examples') or []]}
                              for p in (g.get('parts') or []) if (g.get('points') or 1) > 1]})
    return out


def assemble(L):
    """Returns the package as a dict. Needs every answer of passes 1-6 and the first form stored; pass 7 may be partly
    missing (its words are then listed as not told apart)."""
    periods = []
    for p in L.periods:
        n = p['n']
        d = L.pdir(n)
        t1a, t2a, t1ba = (L.stored_answer(d, x) for x in ('task1', 'task2', 'task1b'))
        _, found, usage, cq_reqs = grammar_words.read_cq(L, n)
        cs = candidates_of(L, n)
        firsts = [read_answer(os.path.join(first_form.folder(L, n), f + '.json')) for f in ('task1', 'task2', 'task3')
                  if os.path.exists(os.path.join(first_form.folder(L, n), f + '.json'))]
        periods.append({'n': n, 'start': p['start'], 'job': p['job'], 'lines': p['lines'],
                        't1': L.stored(d, 'task1'), 't2': L.stored(d, 'task2'), 'usages': [a['usage'] for a in (t1a, t2a, t1ba)],
                        'cq': {'asked': len(cs), 'marked': len(found), 'requests': cq_reqs, 'usage': usage,
                               'no_evidence': sum(1 for c, w in found if not w['evidence_ok'])},
                        'first': [a['usage'] for a in firsts]})
    by_n = {p['n']: p for p in periods}
    stats = collections.Counter()

    def ref(p, item, section):
        v, at_line = verdict(p['lines'], item['line'], item['quote'])
        stats[(section, v)] += 1
        stats[('all', v)] += 1
        item.update(check=v, period=p['n'])
        if at_line:
            l = p['lines'][at_line - 1]
            item.update(at=clock(l['at']), who=l['who'], said=phrase(l['text'], item['quote']))
        return item

    # the words that go through the filter
    ws = grammar_words.filter_words(L)
    vs, f_in, f_out, f_reqs = grammar_words.verdicts(L, len(ws))
    passed, dropped = [], []
    for w, v in zip(ws, vs):
        (passed if grammar_words.keep(v, w) else dropped).append(dict(w, verdict=v))

    topics, grammar, examples, new_words, vocabulary = [], [], [], [], []
    for p in periods:
        t1, t2 = p['t1'], p['t2']
        tt = []
        for t in t1['topics']:
            if 1 <= t['from_line'] <= t['to_line'] <= len(p['lines']):
                t.update(from_at=clock(p['lines'][t['from_line'] - 1]['at']), to_at=clock(p['lines'][t['to_line'] - 1]['at']))
                tt.append(t)
        topics.append(tt)
        examples += [ref(p, e, 'examples') for e in t1['examples']]
        for w in t2['new_words']:
            w['pas'] = 1
            new_words.append(ref(p, w, 'new_words'))
        vocabulary += [ref(p, w, 'vocabulary') for w in t2['vocabulary']]
    asked_refs = 0
    for w in passed:
        w['pas'] = 2 if w['src'] == 'q' else 3
        asked_refs += w['pas'] == 2  # the quote of such a word is cut by code, not written by a model
        new_words.append(ref(by_n[w['period']], w, 'new_words'))

    for n, pas, g in grammar_words.grammar_points(L):
        p = by_n[n]
        g['examples'] = [ref(p, x, 'grammar') for x in g['examples']]
        g.update(period=p['n'], pas=pas)
        grammar.append(g)
    grammar_points = len(grammar)
    not_grammar, merge_usage = [], []
    merge_path = os.path.join(L.dir, 'merge3.json')
    if os.path.isfile(merge_path):
        ma = read_answer(merge_path)
        mg = json.loads(ma['choices'][0]['message']['content'])
        merge_usage = [ma['usage']]
        used, merged = set(), []
        for gr in mg['groups']:
            ids = [i for i in gr['ids'] if 1 <= i <= len(grammar) and i not in used]
            used.update(ids)
            if not ids:
                continue
            items = [grammar[i - 1] for i in ids]
            seen_x, xs = set(), []
            for it in items:
                for x in it['examples']:
                    k = (x['period'], x.get('at'), letters(x['quote']))
                    if k not in seen_x:
                        seen_x.add(k)
                        xs.append(x)
            m = {'topic': gr['title'], 'theme': gr['theme'], 'rule': gr['rule'], 'kind': items[0]['kind'],
                 'period': min(it['period'] for it in items), 'pas': min(it['pas'] for it in items),
                 'passes': sorted(set(it['pas'] for it in items)), 'examples': xs, 'points': len(items),
                 'is_grammar': gr['is_grammar'], 'was': [it['topic'] for it in items],
                 'parts': [{'topic': it['topic'], 'rule': it['rule'], 'examples': it['examples'][:1]} for it in items]}
            (merged if gr['is_grammar'] else not_grammar).append(m)
        left = [dict(g, points=1, is_grammar=True, was=[g['topic']], passes=[g['pas']]) for i, g in enumerate(grammar, 1) if i not in used]
        grammar = merged + left
    else:
        grammar = [dict(g, points=1, is_grammar=True, was=[g['topic']], passes=[g['pas']]) for g in grammar]

    # old spelling marks: a word with `right` is written so, one without is left out
    marks = L.marks

    def with_marks(items):
        out = []
        for w in items:
            m = marks.get(slug(w['es']))
            if not m:
                out.append(w)
            elif (m.get('right') or '').strip():
                w.update(recorded=w['es'], es=m['right'].strip())
                out.append(w)
        return out

    new_words, vocabulary = with_marks(new_words), with_marks(vocabulary)
    taken = {}
    new_words = dedupe(new_words, taken)
    same_twice = []
    have = [forms_of(w['es']) for w in new_words if w['pas'] == 1]
    kept = []
    for w in new_words:
        f = forms_of(w['es'])
        if w['pas'] > 1:
            if f and any(f & h for h in have):
                same_twice.append(w['es'])
                taken.pop(key_of(w['es']), None)
                continue
            have.append(f)
        kept.append(w)
    new_words = kept
    vocabulary = dedupe(vocabulary, taken)
    for w in new_words + vocabulary:
        w['seen'] = seen_before(L.earlier, w['es'], w['quote'])

    # the Spanish of the lesson that no earlier lesson has and no list holds
    todo = load_json(bycode.todo_path(L))
    if todo is None:
        todo = bycode.compute_todo(L, [t for w in new_words + vocabulary for t in (w['es'], w['quote'])])
        write_json(bycode.todo_path(L), todo)
    count, first, _ = bycode.word_index(L)
    store = load_json(bycode.store_path(L), {})
    bc_rows = []
    for t in todo:
        f = t['word']
        if f not in first:
            continue
        a = store.get(f) or {'kind': '', 'lemma': '', 'ru': '', 'sure': False}
        row = dict(first[f], word=f, said=count[f], kind=a['kind'], lemma=a['lemma'], ru=a['ru'], sure=a['sure'])
        m = marks.get(slug(row['raw']))
        if m and not (m.get('right') or '').strip():
            continue
        if m:
            row.update(recorded=row['raw'], raw=m['right'].strip(), was=row['kind'], kind='marked', lemma='', ru='')
        if row['kind'] == 'broken' and TRANSLIT.search(f):
            row['kind'] = 'maybe_russian'  # no model placed it; the letters say it is not Spanish
        if row['kind'] == 'name':
            continue
        bc_rows.append(row)
    bc_rows.sort(key=lambda r: (KIND_ORDER[r['kind']], -r['said'], r['period'], r['sec']))
    bc_kind = collections.Counter(r['kind'] for r in bc_rows)
    bc_russian = bc_kind['russian'] + sum(1 for r in bc_rows if r['kind'] == 'marked' and r.get('was') == 'russian')

    # what it cost
    bc_a, bc_b = bycode.usages(L)
    first_usages = [u for p in periods for u in p['first']]
    spend = collections.OrderedDict()
    spend['lists'] = [2 * len(periods), dollars([u for p in periods for u in p['usages'][:2]], 'gpt-4.1')]
    spend['question'] = [sum(p['cq']['requests'] for p in periods), dollars([p['cq']['usage'] for p in periods], 'gpt-4.1')]
    spend['grammar2'] = [len(periods), dollars([p['usages'][2] for p in periods], 'gpt-4.1')]
    spend['filter'] = [f_reqs, dollars([{'prompt_tokens': f_in, 'completion_tokens': f_out}], FILTER_MODEL)]
    spend['merge'] = [len(merge_usage), dollars(merge_usage, 'gpt-4.1')]
    spend['bycode'] = [len(bc_a) + len(bc_b), dollars(bc_a, 'gpt-4.1-mini') + dollars(bc_b, 'gpt-4.1')]
    spend['first'] = [len(first_usages), dollars(first_usages, 'gpt-4.1')]
    total_requests, total_cost = sum(v[0] for v in spend.values()), sum(v[1] for v in spend.values())

    n_refs = sum(v for (s, _), v in stats.items() if s == 'all')
    facts = {
        'lines': sum(len(p['lines']) for p in periods), 'refs': n_refs,
        'inplace': stats[('all', 'exact')] + stats[('all', 'close')], 'other': stats[('all', 'other')], 'none': stats[('all', 'none')],
        'topics': sum(len(t) for t in topics), 'grammar': len(grammar), 'grammar_points': grammar_points,
        'examples': len(examples), 'new': len(new_words), 'new2': sum(1 for w in new_words if w['pas'] == 2),
        'new3': sum(1 for w in new_words if w['pas'] == 3), 'vocab': len(vocabulary),
        'never': sum(1 for w in new_words + vocabulary if w['seen'] == []),
        'never_new': sum(1 for w in new_words if w['seen'] == []), 'never_vocab': sum(1 for w in vocabulary if w['seen'] == []),
        'bycode': len(bc_rows), 'bycode_es': bc_kind['spanish'], 'bycode_ru': bc_russian,
        'bycode_other': len(bc_rows) - bc_kind['spanish'] - bc_russian,
        'earlier': len(L.earlier), 'refs_lists': n_refs - asked_refs,
        'inplace_lists': stats[('all', 'exact')] + stats[('all', 'close')] - asked_refs,
        'asked': sum(p['cq']['asked'] for p in periods), 'marked': sum(p['cq']['marked'] for p in periods),
        'no_evidence': sum(p['cq']['no_evidence'] for p in periods), 'same_twice': len(same_twice),
        'through_filter': len(ws), 'filter_dropped': len(dropped), 'not_grammar': len(not_grammar),
        'requests': total_requests,
    }

    # the package
    lines = Lines(periods)
    out_periods = []
    for p in periods:
        last = lines.last(p['n'])
        out_periods.append({'n': p['n'], 'job': p['job'], 'start': p['start'], 'lines': len(p['lines']),
                            'end_at': clock(last['at']) if last else None})
    out_topics = []
    for i, of_period in enumerate(topics):
        for t in of_period:
            a, b = lines.by_n(i + 1, t.get('from_line')), lines.by_n(i + 1, t.get('to_line'))
            out_topics.append({'period': i + 1, 'title': t.get('title'), 'summary': t.get('summary'),
                               'from_line': t.get('from_line'), 'to_line': t.get('to_line'),
                               'from_at': t.get('from_at'), 'to_at': t.get('to_at'),
                               'from_sec': round(float(a['at']), 2) if a else None,
                               'to_sec': round(float(b['at']), 2) if b else None})
    out_examples = [dict(place(x, lines), ru=x.get('ru'), shows=x.get('shows')) for x in examples]
    out_new = [dict(place(w, lines), es=w.get('recorded') or w.get('es'), ru=w.get('ru'), how=w.get('how'),
                    heard=w.get('heard') or None, note=w.get('note') or None, source=w.get('pas')) for w in new_words]
    out_vocab = [dict(place(w, lines), es=w.get('recorded') or w.get('es'), ru=w.get('ru'), pos=w.get('pos')) for w in vocabulary]
    other = []
    for r in bc_rows:
        raw = r.get('recorded') or r.get('raw') or r.get('word')
        pl = place(r, lines, piece=raw)
        kind = r.get('was') if r.get('kind') == 'marked' else r.get('kind')
        other.append({'word': r.get('word'), 'raw': raw, 'count': r.get('said') or 0, 'period': pl['period'], 'at': pl['at'],
                      'sec': r.get('sec') if r.get('sec') is not None else pl['sec'], 'who': pl['who'], 'said': pl['said'],
                      'kind': kind or '', 'lemma': r.get('lemma') or None, 'ru': r.get('ru') or None, 'sure': bool(r.get('sure'))})
    return {
        'schema_version': 1,
        'lesson': L.date,
        'built_at': datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'builder': 'lesson-package builder %s' % VERSION,
        'requests': total_requests,
        'cost_usd': float('%.2f' % total_cost),
        'periods': out_periods,
        'topics': out_topics,
        'grammar': export_grammar(grammar, lines),
        'word_notes': export_grammar(not_grammar, lines),
        'examples': out_examples,
        'new_words': out_new,
        'vocabulary': out_vocab,
        'other_words': other,
        'stats': facts,
    }
