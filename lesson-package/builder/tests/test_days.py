"""The `days` command with the HTTP calls mocked: nothing is sent to agent05 and nothing is paid."""
import contextlib
import io
import os
import tempfile
import unittest
from unittest import mock

from builder import cli, days
from builder.tests.test_pipeline import DATE, make_input

OTHER = '2026-02-02'


def step(kind, usd=0.0, minutes=0.0, reason=''):
    return {'kind': kind, 'period': 'p1', 'track': 't1', 'minutes': minutes, 'estimateUsd': usd, 'reason': reason}


def lesson(date, *steps):
    return {'date': date, 'stage': 'x', 'steps': list(steps)}


class Fake:
    """Records every POST; answers the dry plan from `plan`, the real start from `start`."""

    def __init__(self, lessons, start=(202, {'startedJobs': ['j1'], 'dialogues': [], 'plan': {'lessons': []}})):
        self.lessons, self.start, self.calls = lessons, start, []

    def __call__(self, url, body):
        self.calls.append(body)
        if body['dryRun']:
            return 200, {'dryRun': True, 'plan': {'lessons': self.lessons}}
        return self.start

    @property
    def real_calls(self):
        return [c for c in self.calls if not c['dryRun']]


def run(fake, *argv):
    out = io.StringIO()
    with mock.patch.object(days, 'http_post_json', fake), contextlib.redirect_stdout(out):
        code = cli.main(['days', *argv])
    return code, out.getvalue()


class Days(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.work = self.tmp.name
        make_input(self.work)  # the input of DATE is stored: a build reads nothing from agent05
        self.addCleanup(self.tmp.cleanup)

    def go(self, fake, *dates, extra=()):
        return run(fake, *dates, '--work', self.work, '--mode', 'dry', *extra)

    def test_nothing_missing_goes_to_the_build(self):
        fake = Fake([lesson(DATE, step('done'))])
        code, out = self.go(fake, DATE)
        self.assertEqual(code, 0, out)
        self.assertIn('building 1 lesson(s)', out)
        self.assertEqual(fake.real_calls, [])

    def test_run_items_without_the_flag_send_nothing_paid(self):
        fake = Fake([lesson(DATE, step('run', usd=0.5, minutes=30))])
        code, out = self.go(fake, DATE)
        self.assertEqual(code, 5, out)
        self.assertIn('estimate up to $0.50', out)
        self.assertEqual(len(fake.calls), 1)
        self.assertEqual(fake.real_calls, [])

    def test_the_flag_starts_the_runs_once_with_the_limit(self):
        fake = Fake([lesson(DATE, step('run', usd=0.5), step('dialogue'))])
        code, out = self.go(fake, DATE, extra=('--transcribe-max-usd', '2'))
        self.assertEqual(code, 6, out)
        self.assertEqual(fake.real_calls, [{'dates': [DATE], 'dryRun': False, 'maxUsd': 2.0}])
        self.assertIn('queued: 1 run(s)', out)

    def test_wait_is_exit_6(self):
        fake = Fake([lesson(DATE, step('wait'))])
        code, out = self.go(fake, DATE)
        self.assertEqual(code, 6, out)
        self.assertNotIn('building', out)
        self.assertEqual(fake.real_calls, [])

    def test_failed_without_retry_is_exit_5_with_retry_it_is_started(self):
        fake = Fake([lesson(DATE, step('failed', usd=0.2, reason='no text'))])
        code, out = self.go(fake, DATE)
        self.assertEqual(code, 5, out)
        self.assertIn('no text', out)
        fake = Fake([lesson(DATE, step('failed', usd=0.2))])
        code, _ = self.go(fake, DATE, extra=('--transcribe-max-usd', '1', '--retry-failed'))
        self.assertEqual(code, 6)
        self.assertEqual(fake.real_calls[0]['retryFailed'], True)

    def test_over_the_limit_is_exit_4(self):
        fake = Fake([lesson(DATE, step('run', usd=9))], start=(409, {'detail': 'above maxUsd'}))
        code, out = self.go(fake, DATE, extra=('--transcribe-max-usd', '1'))
        self.assertEqual(code, 4, out)
        self.assertIn('above maxUsd', out)

    def test_a_mixed_group_builds_the_ready_one_and_exits_6(self):
        fake = Fake([lesson(DATE, step('done'), step('duplicate')), lesson(OTHER, step('wait'))])
        code, out = self.go(fake, DATE, OTHER)
        self.assertEqual(code, 6, out)
        self.assertIn('building 1 lesson(s) with text: %s' % DATE, out)
        self.assertIn('not built in this call: %s' % OTHER, out)
        self.assertEqual(fake.real_calls, [])

    def test_publish_leaves_an_existing_package(self):
        packages = os.path.join(self.work, 'pk')
        os.makedirs(os.path.join(packages, DATE))
        for root in (os.path.join(self.work, DATE), os.path.join(packages, DATE)):
            with open(os.path.join(root, 'package.json'), 'w') as f:
                f.write('{}')
        fake = Fake([lesson(DATE, step('done'))])
        code, out = self.go(fake, DATE, extra=('--publish', '--packages', packages))
        self.assertEqual(code, 0, out)
        self.assertIn('already published', out)


if __name__ == '__main__':
    unittest.main()
