# KSP CLUB — Game Master Guide

This guide covers everything the game master does: initial setup, the weekly merge cycle, adding players, and troubleshooting.

---

## Initial Setup

### 1. Install the merger tool

```bash
git clone https://github.com/wadeolsson/ksp-club.git
cd ksp-club
pip install -e .
```

Requires Python 3.9+. Test it worked:

```bash
ksp-club --help
```

### 2. Clone the saves repo

```bash
git clone https://github.com/wadeolsson/ksp-club-saves.git ~/ksp-club-saves
```

### 3. Configure the tool

Create a `.ksp-club.json` in the `ksp-club/` directory (it's gitignored):

```json
{
  "saves_repo": "~/ksp-club-saves"
}
```

Or set the env var instead:

```bash
export KSP_CLUB_SAVES_REPO=~/ksp-club-saves
```

### 4. Add players

For each player in the club:

```bash
ksp-club add-player --id wade   --name "Wade"    --agency "Olsson Aerospace"
ksp-club add-player --id ed     --name "Ed"      --agency "Kerman Industries"
```

This creates `submissions/<id>/` and `output/<id>/` folders in the saves repo and commits them.

### 5. Generate starting saves

Players need a save to start from — you can't just hand them a blank KSP save because it won't have the club structure. The easiest way is to use an existing career save as the template and run a first merge:

1. Copy a suitable `persistent.sfs` into `submissions/wade/persistent.sfs`
2. Run the merge: `ksp-club merge`
3. Players download from `output/<id>/persistent.sfs`

Alternatively: create one career save in KSP yourself (with the plugin installed and your ID set), submit it, and merge — the output is a clean starting point.

---

## Weekly Cycle

### Step 1 — Call for submissions

Tell players the submission deadline. They copy their `KSP_CLUB` save to `submissions/<id>/persistent.sfs` and push via GitHub Desktop.

### Step 2 — Check status

```bash
ksp-club status
```

Shows who has submitted, their in-game UT, and how long ago they pushed.

```
KSP CLUB — /Users/wade/ksp-club-saves
Players: 3

  ✓ Wade (wade)       UT Y1 D42 03:12:05   submitted 2h ago
  ✓ Ed (ed)           UT Y1 D41 18:44:30   submitted 5h ago
  ✗ Alex (alex)       (no submission)
```

### Step 3 — Validate before merging

```bash
ksp-club validate
```

Catches problems before they get into the merge: parse errors, missing player IDs, unknown mod parts, untagged vessels.

Fix any errors before proceeding. Common issues:
- **MISSING** — player hasn't submitted yet, chase them up
- **no playerID warnings** — player launched vessels before installing the plugin; they'll be claimed anyway with a warning
- **unknown part** — player has a mod installed that isn't on the approved list

### Step 4 — Run the merge

```bash
ksp-club merge
```

This will:
1. `git pull` the latest submissions
2. Parse and validate all saves
3. Advance all vessel orbits to the canonical UT (highest UT across all submissions)
4. Merge vessels, Kerbals, and career scenarios
5. Write `universal/persistent.sfs` (the canonical world state)
6. Write `output/<id>/persistent.sfs` for each player
7. `git commit` and `git push` the results

If a player missed the deadline, they simply won't appear in the output. Their persistent layer carries forward to next week's merge automatically (it was in last week's output).

**Dry run** (inspect without writing):

```bash
ksp-club merge --dry-run
```

**Skip git** (useful for testing or manual review):

```bash
ksp-club merge --no-git
```

### Step 5 — Notify players

Tell players their new saves are ready. They pull the repo and copy `output/<id>/persistent.sfs` to their KSP saves folder.

---

## Adding a New Mid-Season Player

```bash
ksp-club add-player --id alex --name "Alex" --agency "Kerbin First"
```

Then give them a starting save. Options:

**Option A — Fresh start:** copy a blank career save to `submissions/alex/persistent.sfs`, run `ksp-club merge`, they start at UT=0 while others are further along. Their vessels will be in the world next week.

**Option B — Catch-up save:** give them last week's `output/wade/persistent.sfs` as a template, strip out Wade's vessels and career progress, use it as their submission. Fiddly but puts them at the current UT.

Option A is simpler. The UT difference is handled by the merger's orbital advancement.

---

## Mod Management

The approved mod list lives at `ksp-club-saves/config/modlist.txt`. Each non-comment line is an allowed part name prefix or identifier.

When a player uses an unapproved part, `ksp-club validate` will warn you. You have three options:
1. Ask the player to remove the mod and resubmit
2. Add the mod to `modlist.txt` and require everyone to install it
3. Accept the warning (the vessel will still be in the merge, but players without the mod can't physically load it in-game — it'll appear in the tracking station but crash on load)

---

## Troubleshooting

**"Vessel conflict: persistentId appears in two submissions"**
Two players both have a vessel with the same ID. This shouldn't happen with the plugin installed. The first player's version is kept. If it's the wrong one, manually edit the output save or ask both players to submit again after identifying ownership.

**"Kerbal name conflict: renamed to Lucky Kerman1"**
Two players had a Kerbal with the same name. One was auto-renamed. This is fine and the players should be informed so they don't get confused.

**"git push failed"**
Check your GitHub credentials: `gh auth status`. Re-authenticate if needed: `gh auth login`.

**"Merge failed: No GAME block found"**
A save file is corrupted or empty. Ask the player to resubmit. If they can't, use their `persistent.sfs.bak` (KSP creates these automatically).

**"Unknown SCENARIO warnings"**
A player has a mod that adds a scenario module. It's being kept with their save (the safe default). If it causes issues, add the scenario name to `DYNAMIC_SCENARIOS` in `merger/merge/layers.py`.

**A player accidentally submitted from the wrong save**
Before running the merge, manually delete their `submissions/<id>/persistent.sfs` and ask them to resubmit the correct file.

---

## Useful Commands Reference

```bash
ksp-club status                            # who has submitted
ksp-club validate                          # check all saves
ksp-club merge                             # full weekly merge
ksp-club merge --dry-run                   # preview without writing
ksp-club merge --no-git                    # merge without git pull/push
ksp-club distribute                        # push output/ without re-merging
ksp-club add-player --id X --name Y --agency Z
```
