"""The package page with agent05 replaced by a dictionary: made-up lessons, nothing is sent anywhere."""
import io
import json
import os
import tempfile
import unittest
import urllib.parse

from builder import page

BASE = 'http://agent05.test'
DATE = '2026-01-07'


class FakeGet:
    """Answers GETs from a dictionary of path -> body (dict = JSON, str = text, bytes = file); else 404."""

    def __init__(self, answers):
        self.answers, self.asked = answers, []

    def __call__(self, url, accept='application/json'):
        path = url[len(BASE):]
        self.asked.append(path)
        if path not in self.answers:
            return 404, b''
        body = self.answers[path]
        if isinstance(body, dict):
            return 200, json.dumps(body).encode('utf-8')
        if isinstance(body, str):
            return 200, body.encode('utf-8')
        return 200, body


def notes_path(date, kind, name):
    return '/api/sessions/%s/notes/%s?%s=%s' % (date, kind, 'path' if kind == 'text' else 'name', urllib.parse.quote(name))


def lessons(*rows):
    return {'sessions': list(rows)}


def lesson_row(date=DATE, title='', stage='package', periods=()):
    return {'date': date, 'title': title, 'stage': stage, 'periods': list(periods)}


def period(started, *tracks):
    return {'startedAt': started, 'tracks': list(tracks)}


def track(kind, seconds):
    return {'kind': kind, 'durationSec': seconds}


def package_view(**lists):
    pkg = {'builtAt': '2026-01-08T10:00:00Z', 'periods': [{'n': 1}]}
    for name in ('topics', 'grammar', 'wordNotes', 'examples', 'newWords', 'vocabulary', 'otherWords'):
        pkg[name] = lists.get(name, [])
    return {'date': DATE, 'package': pkg, 'compared': {'lessons': 2}, 'counts': {'newWords': 1}}


def api_of(answers):
    return page.Agent05(BASE, get=FakeGet(answers))


class LessonOf(unittest.TestCase):
    def test_two_day_folders_of_one_date_are_one_lesson(self):
        rows = [lesson_row(title='a', periods=[period('2026-01-07T18:00:00', track('mixed', 600),
                                                      track('participant', 590), track('participant', 580))]),
                lesson_row(title='b', periods=[period('2026-01-07T19:05:00', track('mixed', 300)), period('x')]),
                lesson_row(date='2026-01-14')]
        got = page.lesson_of(rows, DATE)
        self.assertEqual(got['titles'], ['a', 'b'])
        self.assertEqual([r['start'] for r in got['recordings']], ['18:00', '19:05'])
        self.assertEqual([r['minutes'] for r in got['recordings']], [10.0, 5.0])
        self.assertEqual([r['participantTracks'] for r in got['recordings']], [2, 0])

    def test_unknown_date(self):
        self.assertIsNone(page.lesson_of([lesson_row()], '2026-02-02'))


class NoteMarkdown(unittest.TestCase):
    def embeds(self, files):
        return page.Embeds(api_of({notes_path(DATE, 'file', n): b for n, b in files.items()}), DATE)

    def test_comments_are_dropped_and_wiki_links_point_at_notes_of_the_page(self):
        md = page.note_markdown('uno %%hidden%% [[Deberes]] [[Deberes|tarea]] [[Elsewhere]] [[Deberes#part]]',
                                {'Deberes': '3'}, self.embeds({}))
        self.assertNotIn('hidden', md)
        self.assertIn('[Deberes](#note-3)', md)
        self.assertIn('[tarea](#note-3)', md)
        self.assertIn(' Elsewhere ', md)
        self.assertNotIn('[[', md)

    def test_picture_is_put_in_and_a_missing_one_is_named(self):
        embeds = self.embeds({'casa.png': b'not really a picture'})
        md = page.note_markdown('![[casa.png]] ![[gato.png|200]]', {}, embeds)
        self.assertIn('![casa](#media-0)', md)
        self.assertTrue(embeds.media[0].startswith('data:image/png;base64,'))
        self.assertIn('gato.png', md)
        self.assertEqual(embeds.left_out, ['gato.png'])

    def test_large_sound_is_left_out_and_a_small_one_plays(self):
        embeds = self.embeds({'a.mp3': b'x' * 10, 'b.mp3': b'x' * (page.SOUND_LIMIT + 1)})
        md = page.note_markdown('![[a.mp3]]\n![[b.mp3]]', {}, embeds)
        self.assertIn('<audio controls preload="none" src="#media-0"></audio>', md)
        self.assertTrue(embeds.media[0].startswith('data:audio/mpeg;base64,'))
        self.assertIn('b.mp3', md)
        self.assertEqual(embeds.left_out, ['b.mp3'])

    def test_budget_of_the_page_is_kept(self):
        embeds = page.Embeds(api_of({notes_path(DATE, 'file', 'a.mp3'): b'x' * 300,
                                     notes_path(DATE, 'file', 'b.mp3'): b'x' * 300}), DATE, budget=500)
        page.note_markdown('![[a.mp3]] ![[b.mp3]] ![[a.mp3]]', {}, embeds)
        self.assertEqual(embeds.left_out, ['b.mp3'])

    def test_embedded_note_becomes_a_link(self):
        md = page.note_markdown('![[Deberes]]', {'Deberes': '0'}, self.embeds({}))
        self.assertEqual(md, '[Deberes](#note-0)')


def answers_of(package=None, notes=(), date=DATE):
    """agent05 of one lesson with a recording of two minutes; notes are (path, title, kind, text)."""
    a = {'/api/sessions': lessons(lesson_row(date=date, periods=[period(date + 'T18:00:00', track('mixed', 120))]))}
    if package is not None:
        a['/api/sessions/%s/package' % date] = package
    a['/api/sessions/%s/notes' % date] = {'date': date, 'notes': [
        {'path': p, 'title': t, 'kind': k} for p, t, k, _ in notes]}
    for p, _, _, text in notes:
        a[notes_path(date, 'text', p)] = text
    return a


class PageData(unittest.TestCase):

    def test_nothing_to_show_gives_no_page(self):
        self.assertIsNone(page.page_data(api_of(answers_of()), DATE))
        with tempfile.TemporaryDirectory() as out:
            self.assertIsNone(page.write_page(api_of(answers_of()), DATE, page.day_page(out, DATE)))
            self.assertEqual(os.listdir(out), [])

    def test_items_taken_out_by_the_owner_are_not_shown(self):
        view = package_view(newWords=[{'es': 'casa', 'id': 'w1'}, {'es': 'gato', 'id': 'w2', 'leftOut': True}],
                            topics=[{'title': 't', 'leftOut': True}])
        data = page.page_data(api_of(answers_of(view)), DATE)
        self.assertEqual([w['es'] for w in data['package']['newWords']], ['casa'])
        self.assertEqual(data['package']['topics'], [])
        self.assertEqual(data['package']['compared'], {'lessons': 2})
        self.assertEqual(data['lesson']['recordings'][0]['minutes'], 2.0)

    def test_homework_comes_first_and_a_note_that_cannot_be_read_is_skipped(self):
        notes = [('b.md', 'Resumen', 'summary', 'r'), ('a.md', 'Tarea', 'homework', 'see [[Resumen]]'),
                 ('c.md', 'Gone', 'other', None)]
        answers = answers_of(notes=notes)
        del answers[notes_path(DATE, 'text', 'c.md')]
        data = page.page_data(api_of(answers), DATE)
        self.assertIsNone(data['package'])
        self.assertEqual([n['kind'] for n in data['notes']], ['homework', 'summary'])
        summary_id = data['notes'][1]['id']
        self.assertEqual(data['notes'][0]['markdown'], 'see [Resumen](#note-%s)' % summary_id)


class Render(unittest.TestCase):
    TEMPLATE = '<title>{{title}}</title><script type="application/json">{{data}}</script>'

    def test_data_cannot_close_the_script(self):
        data = {'date': DATE, 'package': {'x': 1}, 'notes': [{'markdown': '</script><b>'}]}
        html = page.render(data, self.TEMPLATE)
        self.assertTrue(html.startswith('<title>Пакет урока 07.01.2026</title>'))
        self.assertEqual(html.count('</script>'), 1)
        blob = html[html.index('">') + 2:html.rindex('</script>')]
        self.assertEqual(json.loads(blob)['notes'][0]['markdown'], '</script><b>')

    def test_notes_alone_are_named_so(self):
        html = page.render({'date': DATE, 'package': None, 'notes': []}, self.TEMPLATE)
        self.assertIn('<title>Материалы урока 07.01.2026</title>', html)

    def test_the_template_of_the_repository_has_both_places(self):
        with io.open(page.TEMPLATE, encoding='utf-8') as f:
            text = f.read()
        self.assertIn('{{title}}', text)
        self.assertIn('{{data}}', text)

    def test_the_page_is_a_whole_document_that_opens_from_disk(self):
        html = page.render({'date': DATE, 'package': None, 'notes': []})
        self.assertTrue(html.startswith('<!doctype html>'))
        self.assertIn('<meta charset="utf-8">', html[:300])
        self.assertTrue(html.rstrip().endswith('</html>'))

    def test_written_page_is_whole(self):
        view = package_view(newWords=[{'es': 'casa', 'id': 'w1'}])
        api = api_of(answers_of(view))
        with tempfile.TemporaryDirectory() as out:
            path, size, left_out = page.write_page(api, DATE, page.day_page(out, DATE))
            self.assertEqual(path, os.path.join(out, DATE, 'lesson.html'))
            with io.open(path, encoding='utf-8') as f:
                html = f.read()
            self.assertEqual(size, len(html.encode('utf-8')))
            self.assertEqual(left_out, [])
            self.assertNotIn('{{data}}', html)
            self.assertEqual(sorted(os.listdir(os.path.join(out, DATE))), ['lesson.html'])


if __name__ == '__main__':
    unittest.main()
