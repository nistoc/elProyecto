"""History of the packages folder: when the folder is a git repository of the owner's own, every write of the builder
there (a package published, the page of a day made) ends with a commit of the day folders it touched.

The repository has no remote and is never pushed by the builder: the folder holds the text of the lessons and the
owner's edits, which stay on the owner's disk (the repository of this code is public). A folder that is not a
repository is left as it is - the builder does not make one. The owner's edits (`marks.json`, `speakers.json`,
written by agent05) go into the commit of the next write of their day.
"""
import os
import subprocess

from .cli import say


def is_repository(folder):
    return bool(folder) and os.path.isdir(os.path.join(folder, '.git'))


def _git(folder, *args):
    # safe.directory: an external disk may read as owned by another user, and git refuses such a folder otherwise
    where = os.path.abspath(folder).replace('\\', '/')
    return subprocess.run(['git', '-C', folder, '-c', 'safe.directory=' + where] + list(args),
                          stdin=subprocess.DEVNULL, capture_output=True, text=True, encoding='utf-8', errors='replace')


def commit_days(folder, dates, message):
    """Commits the day folders of `dates` in `folder` when it is a git repository. Returns the short id of the
    commit, or None when the folder is no repository or nothing changed. A failure of git is said and never stops
    the call: the files are written already."""
    if not is_repository(folder):
        return None
    days = sorted({d for d in dates if os.path.isdir(os.path.join(folder, d))})
    if not days:
        return None
    try:
        added = _git(folder, 'add', '--all', '--', *days)
        if added.returncode != 0:
            say('history: git add failed: %s' % added.stderr.strip())
            return None
        if _git(folder, 'diff', '--cached', '--quiet', '--', *days).returncode == 0:
            say('history: nothing changed, no commit')
            return None
        done = _git(folder, 'commit', '--quiet', '-m', message, '--', *days)
        if done.returncode != 0:
            say('history: git commit failed: %s' % (done.stderr.strip() or done.stdout.strip()))
            return None
        head = _git(folder, 'rev-parse', '--short', 'HEAD').stdout.strip()
        say('history: commit %s in %s' % (head, folder))
        return head
    except OSError as e:  # git is not installed
        say('history: git cannot be run (%s); the files are written, no commit' % e)
        return None


def message_of(what, dates):
    days = sorted(set(dates))
    shown = ', '.join(days[:6]) + (', +%d' % (len(days) - 6) if len(days) > 6 else '')
    return '%s: %s' % (what, shown)
