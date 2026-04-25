"""
Configuration and player registry.

Resolves the saves repo path (in priority order):
  1. --saves-repo CLI argument
  2. KSP_CLUB_SAVES_REPO environment variable
  3. saves_repo key in .ksp-club.json in the current directory
  4. saves_repo key in ~/.ksp-club.json
"""

from __future__ import annotations

import json
import os
from pathlib import Path


CONFIG_FILENAMES = [".ksp-club.json"]


class Config:
    def __init__(self, saves_repo: str | Path):
        self.saves_repo = Path(saves_repo).expanduser().resolve()

    # --- directory paths ---

    @property
    def submissions_dir(self) -> Path:
        return self.saves_repo / "submissions"

    @property
    def output_dir(self) -> Path:
        return self.saves_repo / "output"

    @property
    def universal_dir(self) -> Path:
        return self.saves_repo / "universal"

    @property
    def players_file(self) -> Path:
        return self.saves_repo / "config" / "players.json"

    @property
    def modlist_file(self) -> Path:
        return self.saves_repo / "config" / "modlist.txt"

    # --- file paths ---

    def submission_path(self, player_id: str) -> Path:
        return self.submissions_dir / player_id / "persistent.sfs"

    def output_path(self, player_id: str) -> Path:
        return self.output_dir / player_id / "persistent.sfs"

    # --- data loading ---

    def load_players(self) -> list[dict]:
        with open(self.players_file, encoding="utf-8") as f:
            data = json.load(f)
        return data["players"]

    def save_players(self, players: list[dict]) -> None:
        with open(self.players_file, "w", encoding="utf-8") as f:
            json.dump({"players": players}, f, indent=2)
            f.write("\n")

    def validate(self) -> None:
        """Raise if the saves repo doesn't look right."""
        if not self.saves_repo.is_dir():
            raise FileNotFoundError(
                f"Saves repo not found: {self.saves_repo}\n"
                "Set --saves-repo, KSP_CLUB_SAVES_REPO env var, or add saves_repo "
                "to .ksp-club.json in the current directory."
            )
        if not self.players_file.exists():
            raise FileNotFoundError(
                f"players.json not found at {self.players_file}\n"
                "Is this the right saves repo?"
            )


def resolve(saves_repo_arg: str | None = None) -> Config:
    """
    Resolve the saves repo path and return a Config.
    Raises FileNotFoundError if no path can be found.
    """
    # 1. explicit arg
    if saves_repo_arg:
        return Config(saves_repo_arg)

    # 2. env var
    env = os.environ.get("KSP_CLUB_SAVES_REPO")
    if env:
        return Config(env)

    # 3. local config file, then home config file
    search_paths = [Path.cwd()] + [Path.home()]
    for directory in search_paths:
        for name in CONFIG_FILENAMES:
            cfg_path = directory / name
            if cfg_path.exists():
                with open(cfg_path, encoding="utf-8") as f:
                    data = json.load(f)
                if "saves_repo" in data:
                    return Config(data["saves_repo"])

    raise FileNotFoundError(
        "Could not find saves repo path.\n"
        "Options:\n"
        "  --saves-repo PATH\n"
        "  export KSP_CLUB_SAVES_REPO=PATH\n"
        "  echo '{\"saves_repo\": \"PATH\"}' > .ksp-club.json"
    )
