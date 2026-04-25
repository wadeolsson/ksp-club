"""
Git operations on the ksp-club-saves repository.
"""

from __future__ import annotations

import subprocess
from pathlib import Path


def pull(repo_path: Path) -> str:
    """Pull latest changes. Returns a short status string."""
    result = _run(["git", "pull"], repo_path)
    return result.stdout.strip() or "already up to date"


def commit_and_push(repo_path: Path, message: str) -> str:
    """
    Stage all changes, commit, and push.
    Returns a short status string.
    Silently succeeds if there is nothing to commit.
    """
    _run(["git", "add", "-A"], repo_path)

    commit = subprocess.run(
        ["git", "commit", "-m", message],
        cwd=repo_path,
        capture_output=True,
        text=True,
    )

    if commit.returncode != 0:
        if "nothing to commit" in commit.stdout or "nothing to commit" in commit.stderr:
            return "nothing to commit"
        raise RuntimeError(
            f"git commit failed (exit {commit.returncode}):\n{commit.stderr.strip()}"
        )

    push = _run(["git", "push"], repo_path)
    return push.stdout.strip() or "pushed"


def status(repo_path: Path) -> str:
    """Return a short git status summary."""
    result = _run(["git", "status", "--short"], repo_path)
    return result.stdout.strip()


def _run(cmd: list[str], cwd: Path) -> subprocess.CompletedProcess:
    result = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(
            f"{' '.join(cmd)} failed (exit {result.returncode}):\n"
            f"{result.stderr.strip()}"
        )
    return result
