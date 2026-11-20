"""Quote check, candidates, prompts, input mapping, budget. Made-up words only."""
import os
import tempfile
import unittest

from builder import prompts, source
from builder.ask import Budget, BudgetExceeded
from builder.earlier import Earlier, candidates, seen_before
from builder.textutil import verdict


def lines_of(*texts):
    return [{'n': i + 1, 'at': i * 10.0, 'who': 'П', 'text': t} for i, t in enumerate(texts)]


class QuoteCheck(unittest.TestCase):
    L = lines_of('La lámpara está aquí.', 'Ahora, el gato; negro.', 'Otra línea sin nada.')

    def test_exact(self):
        self.assertEqual(verdict(self.L, 1, 'la lámpara está'), ('exact', 1))

    def test_close_when_only_the_signs_differ(self):
        self.assertEqual(verdict(self.L, 2, 'el gato negro'), ('close', 2))

    def test_other_line_nearest_to_the_named_one(self):
        self.assertEqual(verdict(self.L, 3, 'el gato'), ('other', 2))

    def test_none(self):
        self.assertEqual(verdict(self.L, 1, 'una jirafa'), ('none', None))
        self.assertEqual(verdict(self.L, 1, '...'), ('none', None))


class Candidates(unittest.TestCase):
    EARLIER = {'lessons': [{'date': '2026-01-05', 'forms': ['casa', 'hablamos', 'gato']},
                           {'date': '2026-01-12', 'forms': ['casa', 'ventana']},
                           {'date': '2026-03-01', 'forms': ['pelota']}]}

    def test_only_lessons_strictly_before_count(self):
        e = Earlier.from_json(self.EARLIER, '2026-01-12')
        self.assertEqual(sorted(e.slots), ['2026-01-05'])

    def test_quoted_and_unheard(self):
        e = Earlier.from_json(self.EARLIER, '2026-02-01')
        lines = lines_of(
            'Это слово lámpara значит свет.',          # quoted: among Russian words
            'Hablamos del barco ahora.',               # barco: unheard; hablamos: heard
            'Un gato y una Mesa.',                     # gato heard; Mesa has a capital: not unheard
            'Это слово lámpara опять.')                # the same word again: one candidate
        got = candidates(lines, held_words=[], earlier=e)
        self.assertEqual([(c['word'], c['n'], c['kind']) for c in got], [('lámpara', 1, 'quoted'), ('barco', 2, 'unheard')])

    def test_held_words_and_small_words_are_not_candidates(self):
        e = Earlier.from_json(self.EARLIER, '2026-02-01')
        lines = lines_of('Una lámpara pero también barco.')
        got = candidates(lines, held_words=['lámpara'], earlier=e)
        self.assertEqual([c['word'] for c in got], ['barco'])

    def test_same_form_of_a_word_counts_as_heard(self):
        e = Earlier.from_json(self.EARLIER, '2026-02-01')
        self.assertEqual(e.seen_in('hablando'), 0)
        self.assertEqual(e.seen_in('hablaba'), 1)  # same beginning but for the last two letters
        self.assertEqual(candidates(lines_of('Ellos hablaba mucho.'), [], e), [])

    def test_seen_before_phrase_needs_text_or_all_words(self):
        e = Earlier.from_json({'lessons': [{'date': '2026-01-05', 'forms': ['casa', 'grande', 'ventana']}]}, '2026-02-01')
        self.assertEqual(seen_before(e, 'casa grande', ''), ['2026-01-05'])  # no text: every word is there
        e2 = Earlier.from_json({'lessons': [{'date': '2026-01-05', 'forms': ['casa', 'grande'], 'text': 'grande casa'}]}, '2026-02-01')
        self.assertEqual(seen_before(e2, 'casa grande', ''), [])  # text: the phrase must stand as it is
        self.assertIsNone(seen_before(e, 'algo', ''))


class Prompts(unittest.TestCase):
    def test_every_prompt_loads(self):
        for key, name in prompts.FILES.items():
            with open(os.path.join(prompts.PROMPTS_DIR, name), encoding='utf-8') as f:
                blocks = prompts.parse(f.read())
            self.assertTrue(blocks and all(b.strip() for b in blocks.values()), name)

    def test_named_blocks_and_placeholders(self):
        self.assertIn('%s', prompts.load('doubt'))
        self.assertIn('%s', prompts.load('merge'))
        self.assertNotEqual(prompts.load('filter', 'System'), prompts.load('filter', 'Text'))
        self.assertEqual(prompts.load('first3').count('%s'), 2)
        with self.assertRaises(KeyError):
            prompts.load('filter', 'Nothing')

    def test_first_form_system_lacks_one_clause(self):
        self.assertIn(prompts.FIRST_FORM_CLAUSE, prompts.system())
        self.assertNotIn(prompts.FIRST_FORM_CLAUSE, prompts.system(first_form=True))

    def test_parse_longer_fence(self):
        text = '# t\n\n## Text\n\n````text\nline with ```\nsecond\n````\n'
        self.assertEqual(prompts.parse(text), {'Text': 'line with ```\nsecond'})


class Input(unittest.TestCase):
    DOC = {'date': '2026-02-01', 'recordings': [
        {'id': 'a', 'startedAt': '2026-02-01T19:05:33', 'jobIds': ['job1', 'job2'], 'lines': [
            {'start': 1.5, 'speaker': 'x', 'role': 'teacher', 'text': 'Hola.'},
            {'start': 4.0, 'speaker': 'y', 'role': 'student', 'text': 'Buenas.'},
            {'start': 6.0, 'speaker': 'z', 'role': 'other', 'text': 'Ruido.'},
            {'start': 7.0, 'speaker': None, 'role': None, 'text': '  '},
            {'start': 8.0, 'speaker': None, 'role': None, 'text': 'Nadie.'}]},
        {'id': 'b', 'startedAt': None, 'jobIds': [], 'lines': [{'start': 3725.0, 'role': 'teacher', 'text': 'Otra.'}]}]}

    def test_speaker_letters(self):
        self.assertEqual([source.speaker_letter(r) for r in ('teacher', 'student', 'other', None, 'x')], ['П', 'У', '?', '?', '?'])

    def test_periods(self):
        ps = source.periods_from_text(self.DOC)
        self.assertEqual([p['n'] for p in ps], [1, 2])
        self.assertEqual((ps[0]['job'], ps[0]['start']), ('job1', '19:05'))
        self.assertEqual((ps[1]['job'], ps[1]['start']), ('', '1:02'))  # no startedAt: the clock of the first line
        self.assertEqual([l['who'] for l in ps[0]['lines']], ['П', 'У', '?', '?'])  # the blank line is skipped
        self.assertEqual([l['n'] for l in ps[0]['lines']], [1, 2, 3, 4])

    def test_input_is_read_once_and_stored(self):
        calls = []

        def get(url):
            calls.append(url)
            return self.DOC if url.endswith('/text') else {'before': '2026-02-01', 'lessons': []}

        with tempfile.TemporaryDirectory() as d:
            source.read_input('2026-02-01', d, 'http://x', False, get)
            self.assertEqual(len(calls), 2)
            self.assertTrue(calls[1].endswith('/api/sessions/words?before=2026-02-01'))
            source.read_input('2026-02-01', d, 'http://x', False, get)
            self.assertEqual(len(calls), 2)  # the stored copy is used
            source.read_input('2026-02-01', d, 'http://x', True, get)
            self.assertEqual(len(calls), 4)  # --refresh-input


class BudgetTest(unittest.TestCase):
    def test_stops_before_going_over(self):
        b = Budget(1.0, {'a': 0.4, 'b': 0.4})
        b.before('a', 0.4)
        b.after('a', 0.35)
        b.before('b', 0.4)
        with self.assertRaises(BudgetExceeded):
            b.before('c', 0.5)  # 0.35 spent + 0.4 still known + 0.5 > 1.0


if __name__ == '__main__':
    unittest.main()
