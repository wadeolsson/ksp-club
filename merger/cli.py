"""
KSP CLUB command-line tool.

Usage:
  ksp-club status     [--saves-repo PATH]
  ksp-club validate   [--saves-repo PATH]
  ksp-club merge      [--saves-repo PATH] [--no-git] [--dry-run]
  ksp-club distribute [--saves-repo PATH] [--no-git]
  ksp-club add-player --id ID --name NAME --agency AGENCY [--saves-repo PATH] [--no-git]
"""

from __future__ import annotations

import argparse
import sys
from datetime import date, datetime
from pathlib import Path

from merger.sfs.parser import parse
from merger.sfs.serializer import serialize
from merger.merge.builder import build
from merger.merge.mods import load_modlist
from merger.merge.layers import extract
import merger.config as cfg
import merger.storage.git as git

# ---------------------------------------------------------------------------
# ANSI colour helpers (disabled when stdout isn't a tty)
# ---------------------------------------------------------------------------

_USE_COLOR = sys.stdout.isatty()

_G = "\033[32m"   # green
_Y = "\033[33m"   # yellow
_R = "\033[31m"   # red
_C = "\033[36m"   # cyan
_B = "\033[1m"    # bold
_X = "\033[0m"    # reset


def _c(text: str, *codes: str) -> str:
    if not _USE_COLOR:
        return text
    return "".join(codes) + str(text) + _X


def _ok(text: str) -> str:   return _c(text, _G)
def _warn(text: str) -> str: return _c(text, _Y)
def _err(text: str) -> str:  return _c(text, _R)
def _hi(text: str) -> str:   return _c(text, _B)


def _fmt_age(seconds: float) -> str:
    if seconds < 120:
        return f"{int(seconds)}s"
    if seconds < 7200:
        return f"{int(seconds / 60)}m"
    if seconds < 172800:
        return f"{int(seconds / 3600)}h"
    return f"{int(seconds / 86400)}d"


def _fmt_ut(ut: float) -> str:
    """Format UT (seconds) as  Yy Dd Hh Mm Ss (KSP calendar)."""
    total = int(ut)
    s = total % 60;      total //= 60
    m = total % 60;      total //= 60
    h = total % 6;       total //= 6    # KSP day = 6 hours
    d = total % 426;     total //= 426  # KSP year = 426 days
    y = total
    parts = []
    if y: parts.append(f"Y{y}")
    if d: parts.append(f"D{d}")
    parts.append(f"{h:02d}:{m:02d}:{s:02d}")
    return " ".join(parts)


# ---------------------------------------------------------------------------
# Argument parser
# ---------------------------------------------------------------------------

def _make_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="ksp-club",
        description="KSP CLUB — Dual-State Merge System",
    )
    sub = parser.add_subparsers(dest="command", metavar="COMMAND")
    sub.required = True

    # shared --saves-repo arg injected into each subcommand below
    def _add_repo(p: argparse.ArgumentParser) -> None:
        p.add_argument(
            "--saves-repo", metavar="PATH",
            help="path to local ksp-club-saves clone (overrides env/config)",
        )

    def _add_no_git(p: argparse.ArgumentParser) -> None:
        p.add_argument(
            "--no-git", action="store_true",
            help="skip git pull/push (work with local files only)",
        )

    # status
    p_status = sub.add_parser("status", help="show submission status for all players")
    _add_repo(p_status)

    # validate
    p_val = sub.add_parser("validate", help="parse and validate all submissions without merging")
    _add_repo(p_val)

    # merge
    p_merge = sub.add_parser("merge", help="run the full weekly merge pipeline")
    _add_repo(p_merge)
    _add_no_git(p_merge)
    p_merge.add_argument(
        "--dry-run", action="store_true",
        help="run the merge but do not write any files or push",
    )
    p_merge.add_argument(
        "--claim-untagged", action="store_true", default=True,
        help="treat vessels/Kerbals with no playerID as owned by submitter (default: on)",
    )

    # distribute
    p_dist = sub.add_parser(
        "distribute",
        help="push current output/ saves to remote (without re-merging)",
    )
    _add_repo(p_dist)
    _add_no_git(p_dist)

    # add-player
    p_add = sub.add_parser("add-player", help="register a new player")
    _add_repo(p_add)
    _add_no_git(p_add)
    p_add.add_argument("--id",     required=True, help="player ID (e.g. wade)")
    p_add.add_argument("--name",   required=True, help="display name (e.g. Wade)")
    p_add.add_argument("--agency", required=True, help="agency name (e.g. Olsson Aerospace)")

    return parser


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

def cmd_status(args: argparse.Namespace) -> int:
    config = _load_config(args)
    players = config.load_players()

    print(_hi(f"KSP CLUB — {config.saves_repo}"))
    print(f"Players: {len(players)}\n")

    for player in players:
        pid = player["id"]
        label = f"{player['displayName']} ({pid})"
        path = config.submission_path(pid)

        if not path.exists():
            print(f"  {_err('✗')} {label}")
            continue

        try:
            with open(path, encoding="utf-8") as f:
                root = parse(f.read())
            game  = root.get_child("GAME")
            fs    = game.get_child("FLIGHTSTATE") if game else None
            ut    = float(fs.get("UT", "0")) if fs else 0.0
            mtime = path.stat().st_mtime
            age   = (datetime.now() - datetime.fromtimestamp(mtime)).total_seconds()
            print(
                f"  {_ok('✓')} {label:25s}  "
                f"UT {_fmt_ut(ut):20s}  "
                f"submitted {_fmt_age(age)} ago"
            )
        except Exception as exc:
            print(f"  {_warn('?')} {label:25s}  (error reading save: {exc})")

    # Output status
    print()
    has_output = any(config.output_path(p["id"]).exists() for p in players)
    if has_output:
        print("Output saves are ready in output/")
    else:
        print(_warn("No output saves yet — run 'ksp-club merge' to generate them."))

    return 0


def cmd_validate(args: argparse.Namespace) -> int:
    config = _load_config(args)
    players = config.load_players()
    allowed_parts = load_modlist(str(config.modlist_file))

    print(_hi("Validating submissions...\n"))

    total_warnings = 0
    any_error = False

    for player in players:
        pid = player["id"]
        path = config.submission_path(pid)
        label = f"{player['displayName']} ({pid})"

        if not path.exists():
            print(f"  {_err('MISSING')} {label}")
            any_error = True
            continue

        try:
            with open(path, encoding="utf-8") as f:
                root = parse(f.read())
        except Exception as exc:
            print(f"  {_err('PARSE ERROR')} {label}: {exc}")
            any_error = True
            continue

        try:
            contrib = extract(root, pid, claim_untagged=True)
        except ValueError as exc:
            print(f"  {_err('INVALID')} {label}: {exc}")
            any_error = True
            continue

        from merger.merge.mods import validate_parts
        mod_warnings = validate_parts([contrib], allowed_parts)
        all_warnings = contrib.warnings + mod_warnings
        total_warnings += len(all_warnings)

        ut_str = _fmt_ut(contrib.ut)
        print(
            f"  {_ok('✓')} {label:25s}  "
            f"{len(contrib.vessels):2d} vessels  "
            f"{len(contrib.kerbals):2d} Kerbals  "
            f"UT {ut_str}"
        )
        for w in all_warnings:
            print(f"       {_warn('!')} {w}")

    print()
    if any_error:
        print(_err(f"Validation failed — fix errors above before merging."))
        return 1
    elif total_warnings:
        print(_warn(f"Validation passed with {total_warnings} warning(s)."))
    else:
        print(_ok("Validation passed — no issues found."))
    return 0


def cmd_merge(args: argparse.Namespace) -> int:
    config = _load_config(args)

    print(_hi("KSP CLUB — Weekly Merge"))
    print("=" * 40)

    # 1. Git pull
    if not args.no_git:
        print("Pulling latest submissions...", end=" ", flush=True)
        try:
            result = git.pull(config.saves_repo)
            print(_ok(result))
        except RuntimeError as exc:
            print(_err(f"FAILED\n{exc}"))
            return 1

    # 2. Load players + submissions
    players  = config.load_players()
    player_ids = [p["id"] for p in players]
    submissions: dict = {}
    missing: list[str] = []

    print()
    for player in players:
        pid  = player["id"]
        path = config.submission_path(pid)
        if path.exists():
            try:
                with open(path, encoding="utf-8") as f:
                    submissions[pid] = parse(f.read())
                print(f"  {_ok('✓')} {player['displayName']:15s}  {path.name}")
            except Exception as exc:
                print(f"  {_err('✗')} {player['displayName']:15s}  parse error: {exc}")
                return 1
        else:
            print(f"  {_warn('—')} {player['displayName']:15s}  no submission")
            missing.append(pid)

    if missing:
        print(_warn(f"\nMissing submissions: {', '.join(missing)}"))

    if not submissions:
        print(_err("\nNo submissions found. Nothing to merge."))
        return 1

    # 3. Load mod list
    allowed_parts = load_modlist(str(config.modlist_file))

    # 4. Run merge
    print(f"\nMerging {len(submissions)} submission(s)...")
    try:
        universal, rebuilt, warnings = build(
            submissions,
            claim_untagged=getattr(args, "claim_untagged", True),
            allowed_parts=allowed_parts or None,
        )
    except Exception as exc:
        print(_err(f"Merge failed: {exc}"))
        return 1

    if warnings:
        print(f"\n{_warn(f'Warnings ({len(warnings)}):')} ")
        for w in warnings:
            print(f"  {_warn('!')} {w}")

    # 5. Write output
    if args.dry_run:
        print(_warn("\nDry run — no files written."))
    else:
        print("\nWriting output...")

        # Universal state
        config.universal_dir.mkdir(parents=True, exist_ok=True)
        _write_save(config.universal_dir / "persistent.sfs", universal)
        print(f"  → universal/persistent.sfs")

        # Per-player saves
        for pid, save_root in rebuilt.items():
            out_path = config.output_path(pid)
            out_path.parent.mkdir(parents=True, exist_ok=True)
            _write_save(out_path, save_root)
            print(f"  → output/{pid}/persistent.sfs")

        # 6. Git push
        if not args.no_git:
            print("\nPushing to remote...", end=" ", flush=True)
            try:
                week = date.today().isoformat()
                result = git.commit_and_push(
                    config.saves_repo,
                    f"merge: week of {week} ({len(submissions)} players)",
                )
                print(_ok(result))
            except RuntimeError as exc:
                print(_err(f"FAILED\n{exc}"))
                return 1

    print()
    print(_ok(f"Merge complete!") + f"  {len(rebuilt)} player save(s) ready in output/")
    if missing:
        print(_warn(f"Note: {', '.join(missing)} did not submit this week."))
    return 0


def cmd_distribute(args: argparse.Namespace) -> int:
    config = _load_config(args)
    players = config.load_players()

    # Check output saves exist
    ready = [p for p in players if config.output_path(p["id"]).exists()]
    if not ready:
        print(_err("No output saves found. Run 'ksp-club merge' first."))
        return 1

    print(f"Output saves ready for {len(ready)} player(s):")
    for player in ready:
        print(f"  → output/{player['id']}/persistent.sfs")

    if not args.no_git:
        print("\nPushing to remote...", end=" ", flush=True)
        try:
            result = git.commit_and_push(
                config.saves_repo,
                f"distribute: {date.today().isoformat()}",
            )
            print(_ok(result))
        except RuntimeError as exc:
            print(_err(f"FAILED\n{exc}"))
            return 1

    print(_ok("Done!") + " Players can pull and download from output/")
    return 0


def cmd_add_player(args: argparse.Namespace) -> int:
    config = _load_config(args)
    players = config.load_players()

    # Check for duplicate
    if any(p["id"] == args.id for p in players):
        print(_err(f"Player '{args.id}' already exists."))
        return 1

    # Update registry
    players.append({
        "id":          args.id,
        "displayName": args.name,
        "agencyName":  args.agency,
    })
    config.save_players(players)
    print(f"Added {args.name} ({args.id}) / {args.agency} to players.json")

    # Create dirs
    for directory in (config.submissions_dir / args.id, config.output_dir / args.id):
        directory.mkdir(parents=True, exist_ok=True)
        gitkeep = directory / ".gitkeep"
        if not gitkeep.exists():
            gitkeep.touch()

    print(f"Created submissions/{args.id}/  output/{args.id}/")

    if not args.no_git:
        print("Committing...", end=" ", flush=True)
        try:
            result = git.commit_and_push(
                config.saves_repo,
                f"add player: {args.id} ({args.name})",
            )
            print(_ok(result))
        except RuntimeError as exc:
            print(_err(f"FAILED\n{exc}"))
            return 1

    print(_ok(f"\nPlayer '{args.id}' is ready!"))
    print(f"Share the saves repo URL so they can upload to submissions/{args.id}/")
    return 0


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _load_config(args: argparse.Namespace) -> cfg.Config:
    try:
        config = cfg.resolve(getattr(args, "saves_repo", None))
        config.validate()
        return config
    except FileNotFoundError as exc:
        print(_err(str(exc)))
        sys.exit(1)


def _write_save(path: Path, root) -> None:
    with open(path, "w", encoding="utf-8") as f:
        f.write(serialize(root))


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> None:
    parser = _make_parser()
    args   = parser.parse_args()

    commands = {
        "status":     cmd_status,
        "validate":   cmd_validate,
        "merge":      cmd_merge,
        "distribute": cmd_distribute,
        "add-player": cmd_add_player,
    }

    handler = commands.get(args.command)
    if handler is None:
        parser.print_help()
        sys.exit(1)

    sys.exit(handler(args) or 0)


if __name__ == "__main__":
    main()
