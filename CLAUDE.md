# KSP CLUB — Claude Code Context

## What This Project Is

A two-component system for asynchronous multiplayer Kerbal Space Program 1.12.5. A group of players ("the club") each play their own save independently, submit it weekly, and a merge tool reconciles everyone's progress into a shared universe.

**Two repos:**
- `ksp-club` (this repo) — Python merger CLI + C# KSP plugin
- `ksp-club-saves` (private) — save file storage: `submissions/`, `output/`, `universal/`

**Two components:**
- `merger/` — Python CLI tool run by the game master each week
- `plugin/` — C# KSP plugin installed by each player

## Architecture: The Two-Layer System

Every player save has two layers:

- **Persistent layer** (player-owned, never overwritten): their vessels, Kerbals, career progression (funds, science, tech tree, contracts)
- **Dynamic layer** (synced from universe each week): everyone else's vessels, Kerbals, and world-state scenarios

The merge pipeline:
1. Extract each player's persistent layer from their submission
2. Advance all vessel orbits to canonical UT (max UT across all submissions)
3. Merge vessels + Kerbals into a universal state
4. Rebuild each player's save = their persistent layer + updated universal world

## Repository Layout

```
merger/
  sfs/
    parser.py        # .sfs → Node tree
    serializer.py    # Node tree → .sfs
  merge/
    layers.py        # persistent/dynamic classification + extract()
    time.py          # Kepler orbit advancement to canonical UT
    vessels.py       # collect vessels, check ownership conflicts
    kerbals.py       # merge rosters, dedup names
    mods.py          # validate part names against modlist
    builder.py       # full pipeline: build(submissions) → (universal, rebuilt, warnings)
  storage/
    git.py           # git pull/push on the saves repo
  cli.py             # argparse CLI: merge, validate, status, distribute, add-player
  config.py          # saves repo path resolution, player registry

plugin/
  KSPClubPlugin/
    PlayerConfig.cs  # persists across scenes; loads player ID; stamps VESSEL nodes on save
    ClubScenario.cs  # ScenarioModule tracking owned vessel persistentIds
    VesselTagger.cs  # hooks onVesselCreate in flight to claim new vessels
    KerbalRestrictor.cs  # warns against using stock Kerbals
    KSPClubPlugin.csproj
    AssemblyInfo.cs
  GameData/
    KSPClubPlugin/
      KSPClubPlugin.dll   # built output (committed for distribution)
      README.txt

docs/
  onboarding.md      # player setup guide
  game-master.md     # weekly merge runbook
  sfs-format.md      # .sfs grammar, VESSEL/ORBIT fields, scenario classification

tests/
  test_parser.py     # parser + serializer tests (13 tests)
  test_merge.py      # layers, time, vessels, kerbals, mods, builder (36 tests)
```

## Running Tests

```bash
# Python 3.10 (Homebrew) is required — system Python 3.9 has pip permission issues
/opt/homebrew/bin/python3.10 -m pytest tests/ -v

# Or if ksp-club is installed in the active Python:
python3 -m pytest tests/ -v
```

49 tests, all should pass. Tests include real-save tests that read from the KSP CLUB install on this machine — they're marked `skipif` if the save isn't present.

## Building the Plugin

```bash
cd plugin/KSPClubPlugin
dotnet build -c Release
```

Requires `dotnet` 7.0 (at `/usr/local/share/dotnet/x64/dotnet`). The build post-step automatically copies the DLL to `plugin/GameData/KSPClubPlugin/KSPClubPlugin.dll`.

The KSP managed assemblies are resolved from the default Mac Steam path. To override:
```bash
dotnet build -c Release -p:KSP_MANAGED=/path/to/KSP/Managed
```

## Installing the Plugin Locally

The plugin is already installed in the KSP CLUB game at:
```
~/Library/Application Support/Steam/steamapps/common/KSP CLUB/
  Kerbal Space Program CLUB/GameData/KSPClubPlugin/KSPClubPlugin.dll
```

After rebuilding, copy the new DLL there:
```bash
cp plugin/GameData/KSPClubPlugin/KSPClubPlugin.dll \
  ~/Library/Application\ Support/Steam/steamapps/common/KSP\ CLUB/\
Kerbal\ Space\ Program\ CLUB/GameData/KSPClubPlugin/
```

## CLI Tool Setup

```bash
# Install in editable mode (use Homebrew Python, not system Python)
/opt/homebrew/bin/python3.10 -m pip install -e .

# Config — create this file (it's gitignored):
echo '{"saves_repo": "~/ksp-club-saves"}' > .ksp-club.json

# Test
ksp-club status
```

## Key Design Decisions

**Vessel ownership** is identified by `playerID = <id>` stamped into each `VESSEL` block in `persistent.sfs`. The plugin writes this on every game save via `onGameStateSave`. The merger reads it to decide whose vessel is whose.

**Canonical UT** is always `max(all submitted UTs)`. Vessels at earlier UTs have their orbits mathematically advanced using Kepler propagation (`merger/merge/time.py`).

**Kerbal ownership** works the same way — `playerID` field in `KERBAL` blocks. Stock Kerbals (Jeb, Val, Bill, Bob, etc.) are always in the dynamic/universal layer and hardcoded in `layers.py:STOCK_KERBALS`.

**Scenario classification** — `layers.py` has `PERSISTENT_SCENARIOS` and `DYNAMIC_SCENARIOS` frozensets. Unknown scenarios default to persistent (safe — we never lose player data). `KSPClubScenario` (the plugin's own ScenarioModule) is in `PERSISTENT_SCENARIOS`.

**`claim_untagged=True`** (the default) — vessels/Kerbals with no `playerID` are claimed by whoever submitted that save. This handles pre-plugin saves gracefully but breaks down with multiple players. Once everyone has the plugin installed, consider switching to `False`.

## The .sfs Format

Plain text, tab-indented, block-based. See `docs/sfs-format.md` for full reference.
The parser in `merger/sfs/parser.py` handles all edge cases: duplicate keys, empty values, values containing `=`, multiple sibling blocks with the same name.

## Saves Repo Layout

```
ksp-club-saves/
  submissions/<player-id>/persistent.sfs   ← player uploads here
  output/<player-id>/persistent.sfs        ← game master writes here after merge
  universal/persistent.sfs                 ← canonical world state
  config/
    players.json    ← player registry {id, displayName, agencyName}
    modlist.txt     ← allowed part names (empty = validation disabled)
```

## Weekly Workflow (Game Master)

```bash
ksp-club status      # see who submitted
ksp-club validate    # check saves before merging
ksp-club merge       # full pipeline: pull → merge → write output → push
```

## Python Version Note

This machine has two Pythons:
- `/Library/Developer/CommandLineTools/usr/bin/python3` — system Python 3.9, no pip write access
- `/opt/homebrew/bin/python3.10` — Homebrew Python 3.10, use this for everything

The `ksp-club` CLI is installed under the Homebrew Python at `/opt/homebrew/bin/ksp-club`.
