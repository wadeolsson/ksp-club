# KSP CLUB — Dual-State Merge System: Full Build Plan

## Overview

A two-component system for asynchronous multiplayer Kerbal Space Program:
- **`ksp-club` (code repo)** — Python merger CLI + C# in-game KSP plugin
- **`ksp-club-saves` (storage repo)** — GitHub repo players use to submit saves and retrieve rebuilt ones

The merger tool pulls from the storage repo, merges all player saves into a universal state, rebuilds each player's save, and pushes results back. The in-game plugin handles ownership tagging, Kerbal restrictions, and eventually in-game submission.

---

## Repository Structure

### `ksp-club` (code)
```
ksp-club/
├── merger/
│   ├── sfs/
│   │   ├── __init__.py
│   │   ├── parser.py          # .sfs → Python dict tree
│   │   └── serializer.py      # Python dict tree → .sfs
│   ├── merge/
│   │   ├── __init__.py
│   │   ├── layers.py          # split saves into persistent/dynamic layers
│   │   ├── vessels.py         # vessel ownership resolution
│   │   ├── kerbals.py         # Kerbal deduplication + renaming
│   │   ├── time.py            # UT advancement to canonical max UT
│   │   ├── mods.py            # mod list validation + unknown part warnings
│   │   └── builder.py         # assemble universal state + rebuild player saves
│   ├── storage/
│   │   ├── __init__.py
│   │   └── git.py             # git pull/push to ksp-club-saves repo
│   ├── cli.py                 # entry point: merge, validate, distribute commands
│   └── config.py              # player registry, mod list, repo paths
├── plugin/
│   └── KSPClubPlugin/
│       ├── KSPClubPlugin.csproj
│       ├── VesselTagger.cs        # stamps playerID on vessel creation/launch
│       ├── KerbalRestrictor.cs    # blocks base-game Kerbals in astronaut complex
│       ├── PlayerConfig.cs        # stores local player ID in PluginData
│       └── AssemblyInfo.cs
├── docs/
│   ├── onboarding.md          # player setup guide
│   ├── game-master.md         # how to run weekly merge
│   └── sfs-format.md          # notes on .sfs structure
├── tests/
│   ├── test_parser.py
│   ├── test_merge.py
│   └── fixtures/              # sample .sfs snippets for tests
├── PLAN.md
└── README.md
```

### `ksp-club-saves` (storage)
```
ksp-club-saves/
├── submissions/
│   ├── wade/
│   │   └── persistent.sfs     # player uploads here
│   ├── player2/
│   │   └── persistent.sfs
│   └── .gitkeep
├── universal/
│   └── persistent.sfs         # latest merged world state (game master writes)
├── output/
│   ├── wade/
│   │   └── persistent.sfs     # rebuilt save ready to download
│   ├── player2/
│   │   └── persistent.sfs
│   └── .gitkeep
├── config/
│   └── players.json           # player registry: name, playerID, agency name
└── README.md                  # how to submit + retrieve saves
```

---

## Phase 1 — Foundation

### 1.1 Create GitHub Repos
- [ ] Create `ksp-club` repo (code) — can be public
- [ ] Create `ksp-club-saves` repo (storage) — private (save files are personal)
- [ ] Initialize folder structures with `.gitkeep` files and README stubs
- [ ] Add branch protection on `ksp-club-saves`: players can only push to `submissions/<theirname>/`

### 1.2 Player Registry
Define a `players.json` in `ksp-club-saves/config/`:
```json
{
  "players": [
    { "id": "wade", "displayName": "Wade", "agencyName": "Olsson Aerospace" },
    { "id": "player2", "displayName": "Player 2", "agencyName": "Kerman Industries" }
  ]
}
```
The `id` field is the canonical ownership key stamped on vessels by the plugin and used by the merger.

---

## Phase 2 — .sfs Parser

The `.sfs` format is KSP's custom config format. It is **not** JSON/XML but follows a simple grammar:

```
BLOCK_NAME
{
    key = value
    NESTED_BLOCK
    {
        key = value
    }
}
```

### 2.1 Parser (`merger/sfs/parser.py`)
- Read a `.sfs` file line by line
- Build a tree of nodes: `{ "type": "VESSEL", "values": {"name": "Rocket 1", ...}, "children": [...] }`
- Handle duplicate keys within a block (KSP uses them for part lists etc.) — store as lists
- Handle edge cases: empty blocks, inline comments (`//`), multi-word values

### 2.2 Serializer (`merger/sfs/serializer.py`)
- Walk the tree and write back to `.sfs` format
- Preserve indentation style (KSP uses tabs)
- Round-trip fidelity test: parse → serialize → parse should produce identical trees

### 2.3 Tests
- Parse a real `persistent.sfs` from the existing saves directory
- Round-trip test
- Spot-check: vessel names, UT value, Kerbal roster all parse correctly

---

## Phase 3 — Layer Separation

### 3.1 Define What Belongs Where

**Persistent layer (player-owned, never overwritten):**
- `GAME > CAREER_LOG`
- `GAME > SCENARIO[name=ResearchAndDevelopment]` — tech tree + science
- `GAME > SCENARIO[name=Funding]` — funds (career mode)
- `GAME > SCENARIO[name=Reputation]`
- `GAME > SCENARIO[name=ProgressTracking]`
- `GAME > FLIGHTSTATE > VESSEL` where `VESSEL.playerID == this player`
- `GAME > ROSTER > KERBAL` where `KERBAL.playerID == this player`
- `GAME > SCENARIO[name=ContractSystem]` — contracts

**Dynamic layer (synced from universe, always replaced):**
- `GAME > FLIGHTSTATE > VESSEL` where `VESSEL.playerID != this player`
- `GAME > ROSTER > KERBAL` where `KERBAL.playerID != this player`
- `GAME > FLIGHTSTATE` top-level fields (UT, activeVessel pointer, etc.)

**Universal state contains:**
- All vessels from all players' persistent layers
- The canonical UT (max across all submissions)
- Merged Kerbal roster (deduplicated)

### 3.2 `layers.py`
- `split_save(parsed_sfs, player_id) -> (persistent, dynamic)`
- Identify vessels by the custom `playerID` field (added by plugin)
- Identify Kerbals by the custom `playerID` field

---

## Phase 4 — Merge Logic

### 4.1 Universal Time (`time.py`)
- Extract `GAME > FLIGHTSTATE > UT` from each submission
- Canonical UT = max across all submissions
- For each vessel not at canonical UT: advance orbital parameters using Kepler propagation
  - KSP stores orbits as Keplerian elements (SMA, ECC, INC, LAN, AOP, MNA, EPH) — can propagate mean anomaly forward analytically
  - Vessels on the ground / splashed: UT advancement is a no-op (position doesn't change)
  - Vessels with engines burning: flag as a warning — can't propagate accurately

### 4.2 Vessel Ownership (`vessels.py`)
- For each vessel in each submission, read `playerID` field
- Keep only the vessel from its owner's submission (discard all other copies)
- Warn if a vessel appears with no `playerID` (was launched before plugin was installed)
- Warn if two players both claim ownership of the same vessel name (shouldn't happen with plugin)

### 4.3 Kerbal Deduplication (`kerbals.py`)
- Merge all Kerbals from all persistent layers into one roster
- If two Kerbals share a name across different players' rosters:
  - Keep the one from the player whose `playerID` matches the Kerbal's `playerID`
  - If conflict is unresolvable: rename the second one by appending "1", "2", etc.
- Base-game Kerbals (Jebediah, Valentina, Bill, Bob, Lodwin...) are in the dynamic layer and belong to no player — they come from the universal state, not any player

### 4.4 Mod Validation (`mods.py`)
- Maintain a `config/modlist.txt` in the storage repo: list of allowed part module names
- When parsing each submission, scan all `PART` blocks for `name =` values
- Warn if any part name is not in the mod list
- Eventually: reject submissions with unknown parts (configurable strictness)

### 4.5 Builder (`builder.py`)
**Step 1 — Build Universal State:**
```
universal = base_game_template
universal.UT = max_UT
universal.vessels = all vessels from all persistent layers (UT-advanced)
universal.kerbals = merged+deduped kerbal roster
```

**Step 2 — Rebuild Each Player Save:**
```
for each player:
    save = player.persistent_layer
    save.vessels += universal.vessels where vessel.playerID != player.id
    save.kerbals += universal.kerbals where kerbal.playerID != player.id
    save.UT = universal.UT
    write to output/<player>/persistent.sfs
```

---

## Phase 5 — CLI Tool

### 5.1 Commands (`cli.py`)

```bash
# Full weekly merge cycle
ksp-club merge

# Just pull latest submissions without merging
ksp-club pull

# Validate all submissions (parse check, mod check, ownership check) without merging
ksp-club validate

# Push output saves back to storage repo
ksp-club distribute

# Show status: who has submitted this week, latest UT per player
ksp-club status

# Add a new player to the registry
ksp-club add-player --id wade --name "Wade" --agency "Olsson Aerospace"
```

### 5.2 Config (`config.py`)
- Path to local clone of `ksp-club-saves`
- Player registry (loaded from `config/players.json`)
- Mod list path
- Dry-run mode flag

---

## Phase 6 — KSP Plugin (C#)

### 6.1 Player Identity (`PlayerConfig.cs`)
- On first launch after install, prompt player to enter their player ID
- Store in `GameData/KSPClub/PluginData/player.cfg`
- Display current player ID in a small in-game UI (settings menu or toolbar button)

### 6.2 Vessel Tagger (`VesselTagger.cs`)
- Hook into `GameEvents.onVesselCreate` and `GameEvents.onVesselGoOffRails`
- On vessel creation/launch: write `playerID = <id>` into the vessel's root node
- This field persists in the `.sfs` when the game saves
- Vessels without a `playerID` are treated as unowned (warning in merger)

### 6.3 Kerbal Restrictor (`KerbalRestrictor.cs`)
- Hook into the Astronaut Complex UI
- Hide or grey out the base-game named Kerbals (Jebediah, Valentina, Bill, Bob, etc.)
- Players can only hire randomly-generated Kerbals
- Prevents ownership conflicts on iconic Kerbals

### 6.4 Build Setup
- Target KSP 1.12.5 assemblies (`Assembly-CSharp.dll`, `UnityEngine.dll`)
- Output `.dll` to `GameData/KSPClub/`
- GitHub Actions: auto-build on push to `main`, attach `.dll` to release

---

## Phase 7 — Player Onboarding

### 7.1 What Each Player Needs
1. KSP 1.12.5
2. Agreed mod list installed
3. KSPClub plugin installed (download from GitHub Releases)
4. GitHub account + GitHub Desktop
5. Write access to their `submissions/<id>/` folder in `ksp-club-saves`
6. Their rebuilt save from `output/<id>/persistent.sfs` to start the week

### 7.2 Weekly Rhythm

**End of week (player):**
1. In KSP: save and quit
2. Open GitHub Desktop → drag `persistent.sfs` into `submissions/<id>/`
3. Commit and push

**Weekly merge (game master):**
1. `ksp-club validate` — check all saves look good
2. `ksp-club merge` — run the full merge
3. `ksp-club distribute` — push output saves back
4. Notify players their new save is ready

**Start of week (player):**
1. In GitHub Desktop: pull latest
2. Copy `output/<id>/persistent.sfs` to KSP saves folder
3. Play

---

## Phase 8 — Future Features

- **In-game submission**: plugin pushes save directly to storage repo via GitHub API (no GitHub Desktop needed)
- **In-game download**: plugin fetches rebuilt save and offers one-click import
- **GitHub Actions auto-merge**: trigger merge automatically when all players have submitted
- **Conflict UI**: game master CLI shows a visual diff of what changed between submissions
- **Vessel transfer**: formal mechanism for one player to gift a vessel to another
- **Shared contracts**: some contracts exist at the universal level and multiple players contribute to
- **Dead vessel cleanup**: merger removes debris older than N weeks from universal state

---

## Implementation Order

| Phase | What | Prerequisite |
|-------|------|-------------|
| 1 | Create repos + player registry | Nothing |
| 2 | .sfs parser + serializer + tests | Phase 1 |
| 3 | Layer separation logic | Phase 2 |
| 4 | Merge logic (UT, vessels, kerbals, mods) | Phase 3 |
| 5 | CLI tool | Phase 4 |
| 6 | KSP plugin (vessel tagger + Kerbal restrictor) | Phase 1 |
| 7 | Onboarding docs + first live test | Phases 5 + 6 |
| 8 | Future features | Phase 7 |

Phases 5 and 6 can be developed in parallel once Phase 4 is done.
