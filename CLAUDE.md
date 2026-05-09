# KSP CLUB — Claude Code Context

## What This Project Is

A two-component system for asynchronous multiplayer Kerbal Space Program 1.12.5. A group of players ("the club") each play their own save independently, submit it weekly, and a merge tool reconciles everyone's progress into a shared universe.

**Two repos:**
- `ksp-club` (this repo) — Python merger CLI + C# KSP plugin
- `ksp-club-saves` (private, `~/ksp-club-saves`) — save file storage

**Two components:**
- `merger/` — Python CLI tool run by the game master each week
- `plugin/` — C# KSP plugin installed by each player

**Current players:** wade (Octagon Aerospace), kent (Project Nominal), ed (Ed Aerospace)

## Architecture: The Two-Layer System

Every player save has two layers:

- **Persistent layer** (player-owned, never overwritten): their vessels, Kerbals, career progression (funds, science, tech tree, contracts, building levels, strategies)
- **Dynamic layer** (synced from universe each week): world-state scenarios (ROCs, resources, asteroids, CommNet, destructibles)

The merge pipeline:
1. Extract each player's persistent layer (using `KSPClubScenario.OWNED_VESSELS/OWNED_KERBALS` as primary ownership source, `playerID` field as fallback)
2. Skip PRELAUNCH vessels; purge Debris older than 21 KSP days
3. Advance all vessel orbits to canonical UT (max UT across all submissions)
4. Merge vessels + Kerbals; strip stock Kerbals (Jeb/Val/Bill/Bob) entirely
5. Rebuild each player's save = their persistent layer + updated universal world

## Repository Layout

```
merger/
  sfs/
    parser.py        # .sfs → Node tree (Node class with values/children)
    serializer.py    # Node tree → .sfs
  merge/
    layers.py        # PERSISTENT/DYNAMIC_SCENARIOS, extract(), vessel transfer handling
    time.py          # Kepler orbit advancement (BODY_MU table for all KSP bodies)
    vessels.py       # collect vessels, dedup by persistentId
    kerbals.py       # merge rosters, dedup names, exclude STOCK_KERBALS
    mods.py          # validate part names against modlist
    builder.py       # build() pipeline + _purge_old_debris()
  storage/
    git.py           # git pull/push on the saves repo
  cli.py             # merge, validate, status, distribute, add-player commands
  config.py          # saves repo path resolution, player registry

plugin/KSPClubPlugin/
  PlayerConfig.cs      # [MainMenu, persist=true] — config, save sync, vessel/Kerbal stamping,
                       #   main-menu new-save check, KnownPlayers + VesselOwnerCache population
  ClubScenario.cs      # ScenarioModule: OWNED_VESSELS + OWNED_KERBALS lists, saved in .sfs
  VesselTagger.cs      # [Flight] — onVesselCreate → ClaimVessel
  VesselProtection.cs  # [Flight+TrackStation] — block fly/recover/delete of non-owned vessels,
                       #   eject to tracking station if you enter flight with non-owned vessel
  VesselTrading.cs     # [TrackStation] — green toolbar, transfer vessel to another player;
                       #   stamps transferTarget field, merger reassigns playerID on next merge
  KerbalRestrictor.cs  # [SpaceCentre] — warns against stock Kerbals in Astronaut Complex
  StarterKerbals.cs    # [SpaceCentre] — generates 4 random Kerbals for new players (0 owned)
  OrbitColors.cs       # [Flight+TrackStation] — colors orbit lines + icons by player;
                       #   VesselColorCache + VesselOwnerCache populated at save load
  Relations.cs         # Enum: Friendly / Neutral / Hostile
  AgencyCommNet.cs     # [Flight] — zeros antennaRelay.power for Neutral/Hostile vessels every 6s
  GitHubClient.cs      # UnityWebRequest GitHub Contents API (GetSha, DownloadFile, PutFile)
  SaveSyncUI.cs        # [SpaceCentre] — blue toolbar: submit save, open relations dialog, settings
  AssemblyInfo.cs
  KSPClubPlugin.csproj

plugin/GameData/KSPClubPlugin/
  KSPClubPlugin.dll    # built output — copy to live KSP after dotnet build
  README.txt

docs/
  onboarding.md        # player setup guide (needs update: missing relations/colors/trading)
  game-master.md       # weekly merge runbook
  sfs-format.md        # .sfs grammar, VESSEL/ORBIT fields, scenario classification

tests/
  test_parser.py       # parser + serializer (13 tests)
  test_merge.py        # layers, time, vessels, kerbals, mods, builder (46 tests, 3 skipped)
```

## Running Tests

```bash
/opt/homebrew/bin/python3.10 -m pytest tests/ -q
```

All should pass. A few real-save tests are `skipif` when the KSP CLUB install isn't present.

## Building the Plugin

```bash
cd plugin/KSPClubPlugin
dotnet build -c Release
# DLL auto-copied to plugin/GameData/KSPClubPlugin/KSPClubPlugin.dll
```

Then deploy to the live KSP install:
```bash
cp plugin/GameData/KSPClubPlugin/KSPClubPlugin.dll \
  ~/Library/Application\ Support/Steam/steamapps/common/KSP\ CLUB/\
  Kerbal\ Space\ Program\ CLUB/GameData/KSPClubPlugin/
```

Requires `dotnet` 7.0. KSP assemblies resolved from the default Mac Steam path automatically.

## Key Design Decisions

**Vessel ownership** — primary: `KSPClubScenario.OWNED_VESSELS` persistentId list (saved in the .sfs). Fallback: `playerID` field stamped on VESSEL nodes via `onProtoVesselSave`. Secondary fallback: `claim_untagged=True` claims untagged vessels for the submitter.

**Kerbal ownership** — same pattern: `OWNED_KERBALS` name list in the scenario, then `playerID` field stamped in KERBAL nodes via post-save file processing (`StampKerbalsInFile` uses `ConfigNode.Load/Save`).

**Vessel stamping** — vessels are stamped in `onProtoVesselSave` (fires per-vessel during serialisation — the correct hook). Kerbals are stamped by reading/writing the .sfs file directly after save, since no per-Kerbal save event exists.

**Vessel transfer** — plugin stamps `transferTarget = kent` on a vessel. Merger detects it in `layers.py`, reassigns `playerID` to the target, clears the field. Vessel routes to target's save after next merge.

**Orbit colors** — player picks a named color (`blue/red/green/etc`). Color stamped as `playerColor = R,G,B` on vessel nodes via `onProtoVesselSave`. At load time, `ClaimExistingFromNode` populates `OrbitColorsBase.VesselColorCache` (persistentId→Color) and `VesselOwnerCache` (persistentId→playerId). `OrbitColors` applies colors when map view opens; modulated by relation (Friendly=full, Neutral=dimmed, Hostile=dim red).

**CommNet** — `AgencyCommNet` caches original `antennaRelay.power` for non-owned vessels. For Neutral/Hostile vessels, zeros the relay power so they can't act as relay hops. Runs every 6s in flight. Each player's game is independent so this is per-player.

**Relations** — `Friendly / Neutral / Hostile` per player. Stored in `player.cfg` under `RELATIONS {}` block. Used by OrbitColors (brightness), AgencyCommNet (relay access), and VesselProtection (future: visibility).

**Scenario classification** — `PERSISTENT_SCENARIOS` includes career progression, building levels, strategies, waypoints, sentinel targets, contract events, and `KSPClubScenario`. `DYNAMIC_SCENARIOS` is world state only (asteroids, CommNet, ROCs, resources, destructibles). Unknown scenarios default to persistent.

**Stock Kerbals** — Jeb/Val/Bill/Bob stripped from all merged saves. New players get 4 random Kerbals auto-generated by `StarterKerbals` on first Space Center entry.

## Saves Repo Layout

```
ksp-club-saves/
  submissions/<id>/persistent.sfs   ← player uploads (in-game toolbar or GitHub Desktop)
  output/<id>/persistent.sfs        ← rebuilt save after merge (auto-downloaded by plugin)
  universal/persistent.sfs          ← canonical world state
  config/
    players.json    ← {id, displayName, agencyName}
    modlist.txt     ← allowed part names (empty = disabled)
  .github/workflows/merge.yml       ← auto-merge on all-submitted push or Sunday 23:00 UTC
```

## Weekly Workflow

**Automated** — GitHub Actions runs the merge when all players have submitted, or on Sunday 23:00 UTC. Players submit via in-game toolbar button and receive the merged save via main-menu auto-download prompt.

**Manual** (game master):
```bash
cd ~/ksp-club
ksp-club status      # who has submitted
ksp-club validate    # check saves
ksp-club merge       # pull → merge → push output
```

## Plugin Config (`player.cfg`)

Stored at `GameData/KSPClubPlugin/PluginData/player.cfg`. Fields:
- `playerId`, `agencyName`, `colorName` — identity
- `githubToken`, `repoOwner`, `repoName`, `saveName` — sync config
- `lastOutputSha` — SHA of last downloaded output save (prevents re-prompting)
- `RELATIONS {}` block — per-player Friendly/Neutral/Hostile stances

## Python Version Note

- `/Library/Developer/CommandLineTools/usr/bin/python3` — system Python 3.9, no pip access
- `/opt/homebrew/bin/python3.10` — use this for everything

CLI installed at `/opt/homebrew/bin/ksp-club`.
