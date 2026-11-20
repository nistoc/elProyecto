"""A tiny made-up lesson built end to end from hand-written answers: files mode, replay, dry."""
import contextlib
import io
import json
import os
import tempfile
import unittest

from builder import cli
from builder import ask
from builder.ask import ReplayMissing

DATE = '2026-02-01'
LINES = [
    (0.0, 'П', 'Hoy hablamos de la casa y de la lámpara.'),
    (5.0, 'У', 'Vale, la lámpara está en la sala.'),
    (10.0, 'П', 'Слово ventana значит окно, запомни: ventana.'),
    (15.0, 'У', 'Una ventana grande, ¿sí?'),
    (20.0, 'П', 'Dije que ayer comí pan, no como pan.'),
]
EARLIER = {'before': DATE, 'lessons': [{'date': '2026-01-05', 'forms': ['casa', 'lampara', 'hablamos', 'pan', 'sala', 'grande']}]}


def jread(path):
    with open(path, encoding='utf-8') as f:
        return json.load(f)


def jwrite(path, data):
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False)


def put(path, text):
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)


def make_input(work):
    inp = os.path.join(work, DATE, 'input')
    os.makedirs(inp)
    periods = [{'n': 1, 'job': 'job1', 'start': '19:00',
                'lines': [{'n': i + 1, 'at': at, 'who': who, 'text': text} for i, (at, who, text) in enumerate(LINES)]}]
    jwrite(os.path.join(inp, 'recordings.json'), {'date': DATE, 'periods': periods})
    jwrite(os.path.join(inp, 'earlier.json'), EARLIER)


def reply(content, by='test'):
    return {'choices': [{'message': {'content': json.dumps(content, ensure_ascii=False)}, 'finish_reason': 'stop'}],
            'usage': {'prompt_tokens': 1000, 'completion_tokens': 100}, '_answered_by': by}


def hand_answer(path, request):
    """What a person (or a subagent) would write for each request of the lesson."""
    name = os.path.basename(path)[:-len('.request.json')]
    first = os.sep + 'first' + os.sep in path
    if first:
        return {'task1': {'topics': [], 'grammar': [], 'examples': []},
                'task2': {'new_words': [], 'vocabulary': []},
                'task3': {'new_words': [], 'grammar': []}}[name]
    if name == 'task1':
        return {'topics': [{'title': 'La casa', 'summary': 'Hablan de la casa.', 'from_line': 1, 'to_line': 2}],
                'grammar': [{'theme': 'presente', 'topic': 'Presente', 'rule': 'Como -ar.', 'kind': 'explained',
                             'examples': [{'line': 5, 'quote': 'no como pan'}]}],
                'examples': [{'line': 4, 'quote': 'Una ventana grande', 'ru': 'Большое окно', 'shows': 'Порядок слов'}]}
    if name == 'task2':
        return {'new_words': [{'es': 'ventana', 'heard': '', 'ru': 'окно', 'how': 'explained', 'line': 3, 'quote': 'ventana значит окно', 'note': 'Перевели.'}],
                'vocabulary': [{'es': 'sala', 'ru': 'зал', 'pos': 'noun', 'line': 2, 'quote': 'en la sala'}]}
    if name == 'task1b':
        return {'grammar': []}
    if name.startswith('cq_'):
        n = len(request['messages'][-1]['content'].split('#')) - 1
        return {'words': [{'i': i, 'taught': False, 'es': '', 'ru': '', 'how': 'none', 'evidence': ''} for i in range(1, n + 1)]}
    if name == 'merge3':
        return {'groups': [{'theme': 'presente', 'title': 'Presente', 'rule': 'Como -ar.', 'ids': [1], 'is_grammar': True}]}
    if name.startswith('filter_'):
        n = len(request['messages'][-1]['content'].split('#')) - 1
        return {'items': [{'i': i, 'spanish': True, 'form': 'dictionary', 'lemma': '', 'about': 'word'} for i in range(1, n + 1)]}
    if name.startswith('bycode'):
        n = len(request['messages'][-1]['content'].split('#')) - 1
        return {'items': [{'i': i, 'kind': 'spanish', 'lemma': 'x', 'ru': 'икс', 'sure': True} for i in range(1, n + 1)]}
    raise AssertionError('no hand answer for ' + name)


def answer_all(work):
    """Writes the answer file of every request file that has none. Returns how many."""
    n = 0
    for root, _, files in os.walk(work):
        for f in files:
            if f.endswith('.request.json'):
                path = os.path.join(root, f)
                target = path[:-len('.request.json')] + '.json'
                if os.path.exists(target):
                    continue
                req = jread(path)
                jwrite(target, reply(hand_answer(path, req)))
                n += 1
    return n


def run(*argv):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = cli.main(list(argv))
    return code, out.getvalue()


class FilesMode(unittest.TestCase):
    def test_files_mode_stops_with_3_then_goes_on_to_a_package(self):
        with tempfile.TemporaryDirectory() as work:
            make_input(work)
            rounds, code = 0, None
            while code != 0 and rounds < 12:
                code, out = run('build', DATE, '--work', work, '--mode', 'files')
                rounds += 1
                if code == 3:
                    # the request files hold what is sent, in the chat-completions request shape
                    reqs = [os.path.join(r, f) for r, _, fs in os.walk(work) for f in fs if f.endswith('.request.json')]
                    body = jread(reqs[0])
                    self.assertEqual(sorted(body), ['max_tokens', 'messages', 'model', 'schema', 'temperature'])
                    self.assertEqual(body['messages'][0]['role'], 'system')
                    self.assertEqual(answer_all(work) > 0, True)
                else:
                    self.assertEqual(code, 0, out)
            self.assertEqual(code, 0)
            self.assertGreater(rounds, 3)  # first request files, then the passes that need their answers
            pkg = jread(os.path.join(work, DATE, 'package.json'))
            self.check_package(pkg)
            # everything is stored now: a replay gives the same package, and a dry call estimates nothing
            code, _ = run('build', DATE, '--work', work, '--mode', 'replay')
            self.assertEqual(code, 0)
            again = jread(os.path.join(work, DATE, 'package.json'))
            for p in (pkg, again):
                p.pop('built_at')
            self.assertEqual(pkg, again)
            code, out = run('build', DATE, '--work', work, '--mode', 'dry')
            self.assertEqual(code, 0)
            self.assertIn('ready', out)

    def check_package(self, pkg):
        self.assertEqual(pkg['schema_version'], 1)
        self.assertEqual(pkg['lesson'], DATE)
        self.assertEqual(pkg['periods'][0], {'n': 1, 'job': 'job1', 'start': '19:00', 'lines': 5, 'end_at': '0:20'})
        self.assertEqual(pkg['topics'][0]['from_sec'], 0.0)
        self.assertEqual(pkg['topics'][0]['to_at'], '0:05')
        word = pkg['new_words'][0]
        self.assertEqual((word['es'], word['check'], word['at'], word['who'], word['source']), ('ventana', 'exact', '0:10', 'teacher', 1))
        self.assertEqual(pkg['vocabulary'][0]['who'], 'student')
        self.assertEqual(pkg['examples'][0]['check'], 'exact')
        theme = pkg['grammar'][0]
        self.assertEqual((theme['theme'], theme['points'], theme['topic']), ('presente', 1, 'Presente'))
        self.assertEqual(theme['examples'][0]['quote'], 'no como pan')
        self.assertEqual(pkg['word_notes'], [])
        # "ayer" and "comí" are in no earlier lesson and in no list: found by code, told apart by the model (pass 7)
        words = {w['word'] for w in pkg['other_words']}
        self.assertIn('ayer', words)
        self.assertNotIn('casa', words)
        self.assertTrue(all(w['kind'] == 'spanish' for w in pkg['other_words']))
        self.assertEqual(pkg['stats']['lines'], 5)
        self.assertEqual(pkg['requests'], pkg['stats']['requests'])
        self.assertGreater(pkg['requests'], 8)

    def test_answer_for_only_one_pass_is_asked_by_files(self):
        with tempfile.TemporaryDirectory() as work:
            make_input(work)
            code, out = run('build', DATE, '--work', work, '--mode', 'dry', '--answer', 'pass1=files')
            self.assertEqual(code, 3)
            reqs = sorted(f for _, _, fs in os.walk(work) for f in fs if f.endswith('.request.json'))
            self.assertEqual(reqs, ['task1.request.json'])

    def test_replay_with_a_missing_answer_is_an_error(self):
        with tempfile.TemporaryDirectory() as work:
            make_input(work)
            code, out = run('build', DATE, '--work', work, '--mode', 'replay')
            self.assertEqual(code, 2)
            self.assertIn('no stored answer', out)

    def test_openai_needs_a_limit(self):
        with tempfile.TemporaryDirectory() as work:
            make_input(work)
            with self.assertRaises(SystemExit):
                run('build', DATE, '--work', work, '--mode', 'openai')

    def test_openai_stops_before_going_over_the_limit(self):
        with tempfile.TemporaryDirectory() as work:
            make_input(work)
            env = os.path.join(work, 'env')
            with open(env, 'w') as f:
                f.write('%s=%s\n' % (ask.KEY_NAME, 'not-a-key'))
            code, out = run('build', DATE, '--work', work, '--mode', 'openai', '--max-usd', '0.001', '--env', env)
            self.assertEqual(code, 4)
            self.assertIn('over the limit', out)
            self.assertFalse(any(f.endswith('.json') and not f.startswith(('recordings', 'earlier')) for _, _, fs in os.walk(work) for f in fs))


class Publish(unittest.TestCase):
    def test_publish_refuses_to_overwrite_and_leaves_marks(self):
        with tempfile.TemporaryDirectory() as work, tempfile.TemporaryDirectory() as packages:
            os.makedirs(os.path.join(work, DATE))
            put(os.path.join(work, DATE, 'package.json'), '{"a": 1}')
            self.assertEqual(run('publish', DATE, '--work', work, '--packages', packages)[0], 0)
            target = os.path.join(packages, DATE)
            put(os.path.join(target, 'marks.json'), '{"keep": true}')
            with self.assertRaises(SystemExit):
                run('publish', DATE, '--work', work, '--packages', packages)
            put(os.path.join(work, DATE, 'package.json'), '{"a": 2}')
            self.assertEqual(run('publish', DATE, '--work', work, '--packages', packages, '--replace')[0], 0)
            self.assertEqual(jread(os.path.join(target, 'package.json')), {'a': 2})
            self.assertEqual(jread(os.path.join(target, 'marks.json')), {'keep': True})


if __name__ == '__main__':
    unittest.main()
