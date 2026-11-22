"""History of the packages folder: a commit after a write when the folder is a git repository, nothing otherwise."""
import contextlib
import io
import json
import os
import shutil
import subprocess
import tempfile
import unittest

from builder import cli, history

DAY = '2026-03-04'
GIT = shutil.which('git')


def git(folder, *args):
    return subprocess.run(['git', '-C', folder] + list(args), capture_output=True, text=True, check=True).stdout


def repository():
    folder = tempfile.mkdtemp()
    git(folder, 'init', '--quiet')
    git(folder, 'config', 'user.name', 'Test Hand')
    git(folder, 'config', 'user.email', 'hand@example.invalid')
    git(folder, 'config', 'commit.gpgsign', 'false')
    return folder


def write(folder, day, name, text):
    os.makedirs(os.path.join(folder, day), exist_ok=True)
    with open(os.path.join(folder, day, name), 'w', encoding='utf-8') as f:
        f.write(text)


def quiet(call, *args):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        return call(*args), out.getvalue()


@unittest.skipUnless(GIT, 'git is not installed')
class CommitDaysTest(unittest.TestCase):
    def setUp(self):
        self.folder = repository()
        self.addCleanup(shutil.rmtree, self.folder, ignore_errors=True)

    def test_a_written_day_is_committed_with_its_edits(self):
        write(self.folder, DAY, 'lesson.html', '<p>uno</p>')
        write(self.folder, DAY, 'marks.json', '{}')
        head, out = quiet(history.commit_days, self.folder, [DAY], 'page: ' + DAY)
        self.assertTrue(head)
        self.assertIn('history: commit', out)
        self.assertEqual(git(self.folder, 'log', '--format=%s').strip(), 'page: ' + DAY)
        self.assertEqual(sorted(git(self.folder, 'ls-files').split()), [DAY + '/lesson.html', DAY + '/marks.json'])

    def test_nothing_changed_makes_no_commit(self):
        write(self.folder, DAY, 'lesson.html', '<p>uno</p>')
        quiet(history.commit_days, self.folder, [DAY], 'first')
        head, _ = quiet(history.commit_days, self.folder, [DAY], 'second')
        self.assertIsNone(head)
        self.assertEqual(git(self.folder, 'rev-list', '--count', 'HEAD').strip(), '1')

    def test_only_the_named_days_go_in(self):
        write(self.folder, DAY, 'lesson.html', '<p>uno</p>')
        write(self.folder, '2026-03-11', 'lesson.html', '<p>dos</p>')
        quiet(history.commit_days, self.folder, [DAY, '2026-09-09'], 'page')
        self.assertEqual(git(self.folder, 'ls-files').split(), [DAY + '/lesson.html'])
        self.assertIn('2026-03-11/', git(self.folder, 'status', '--porcelain'))

    def test_a_folder_that_is_no_repository_is_left_as_it_is(self):
        plain = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, plain, ignore_errors=True)
        write(plain, DAY, 'lesson.html', '<p>uno</p>')
        head, out = quiet(history.commit_days, plain, [DAY], 'page')
        self.assertIsNone(head)
        self.assertEqual(out, '')
        self.assertFalse(os.path.exists(os.path.join(plain, '.git')))

    def test_publish_commits_the_package(self):
        work = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, work, ignore_errors=True)
        write(work, DAY, 'package.json', json.dumps({'schema_version': 1, 'lesson': DAY}))
        code, _ = quiet(cli.main, ['publish', DAY, '--work', work, '--packages', self.folder])
        self.assertEqual(code, 0)
        self.assertEqual(git(self.folder, 'log', '--format=%s').strip(), 'package: ' + DAY)


class MessageTest(unittest.TestCase):
    def test_a_long_list_of_days_is_cut(self):
        days = ['2026-01-%02d' % d for d in range(1, 10)]
        self.assertEqual(history.message_of('days', days),
                         'days: 2026-01-01, 2026-01-02, 2026-01-03, 2026-01-04, 2026-01-05, 2026-01-06, +3')


if __name__ == '__main__':
    unittest.main()
