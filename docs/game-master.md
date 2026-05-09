# KSP CLUB — Game Master Guide

---

## Initial Setup

### 1. Install the merger tool

```bash
git clone https://github.com/wadeolsson/ksp-club.git ~/ksp-club
cd ~/ksp-club
/opt/homebrew/bin/python3.10 -m pip install -e .
```

Test: `ksp-club --help`

### 2. Clone the saves repo

```bash
git clone https://github.com/wadeolsson/ksp-club-saves.git ~/ksp-club-saves
```

### 3. Configure

Create `~/ksp-club/.ksp-club.json` (gitignored):
```json
{ "saves_repo": "~/ksp-club-saves" }
```

### 4. Add players

```bash
ksp-club add-player --id wade  --name "Wade"  --agency "Olsson Aerospace"
ksp-club add-player --id kent  --name "Kent"  --agency "Project Nominal"
ksp-club add-player --id ed    --name "Ed"    --agency "Ed Aerospace"
```

This creates `submissions/<id>/` and `output/<id>/` folders and commits them.

### 5. Invite players to the saves repo

github.com → `ksp-club-saves` → Settings → Collaborators → Add people

Each player needs a GitHub Classic PAT with `repo` scope to use the plugin's auto-sync.

### 6. Create starting saves

Each player needs a `KSP_CLUB` career save to start from. Simplest approach:
1. Each player creates their own fresh career in KSP named `KSP_CLUB`
2. They submit via the toolbar button
3. Run `ksp-club merge`
4. Players download their output save — they're in the universe

---

## Weekly Merge Cycle

### Automated (GitHub Actions)

The merge runs automatically when all players have submitted, or every Sunday at 23:00 UTC. Players just submit and download — no game master action needed.

Check status at: **github.com/wadeolsson/ksp-club-saves → Actions**

### Manual

```bash
cd ~/ksp-club

# 1. See who has submitted
ksp-club status

# 2. Validate before merging (check for warnings)
ksp-club validate

# 3. Run the merge
ksp-club merge

# That's it. Players are notified automatically on next KSP launch.
```

### Dry run (no files written)

```bash
ksp-club merge --dry-run
```

### Skip git (test locally)

```bash
ksp-club merge --no-git
```

---

## Adding a New Player Mid-Season

```bash
ksp-club add-player --id newplayer --name "New Player" --agency "New Agency"
```

Then invite them to the saves repo. They create a fresh `KSP_CLUB` career, submit it, and you run a merge. They start at UT=0 while others are further along — the merger handles the time difference via orbital advancement.

---

## What the Merger Does

Each merge cycle:
1. Pulls latest submissions
2. Extracts each player's persistent layer (vessels, Kerbals, career progress)
3. Skips PRELAUNCH vessels; purges debris older than 21 KSP days
4. Advances all vessel orbits to the latest UT (max across all submissions)
5. Merges Kerbal rosters (no stock Kerbals — Jeb/Val/Bill/Bob excluded)
6. Processes fuel transactions (debit buyer, credit seller, reduce tanker fuel)
7. Rebuilds each player's save (their layer + universal world injected)
8. Generates a weekly news feed (vessel events, Kerbal hires/deaths)
9. Pushes output saves and news to the repo

---

## Merge Warnings Reference

| Warning | Meaning | Action |
|---------|---------|--------|
| `Vessel 'X' has no playerID` | Pre-plugin vessel, claimed for submitter | Normal for early sessions; clears once plugin runs |
| `Unknown SCENARIO 'X'` | Unrecognised mod scenario, kept as persistent | Check if mod is approved; add to classification if needed |
| `Removed N old debris` | Auto-cleanup | Informational |
| `Fuel transaction: tanker not found` | Tanker sold fuel then was recovered | Funds adjusted, fuel not reduced; acceptable |
| `Kerbal name conflict` | Two players hired same name, one renamed | Players informed |

---

## Fuel Transactions

When players use the in-game fuel tanker system, transactions are recorded in their saves. The merger automatically:
- Debits buyer's funds
- Credits seller's funds
- Reduces tanker vessel's fuel in the universal state

No action required from the game master.

---

## Mod Management

The allowed parts list lives at `ksp-club-saves/config/modlist.txt`. Each non-comment line is an allowed part name. `ksp-club validate` warns about unknown parts before you merge.

If a player uses an unapproved mod:
1. Ask them to remove it and resubmit, or
2. Add it to modlist.txt and require everyone to install it

---

## Player Relations & CommNet

Players manage their own diplomatic relations via the in-game toolbar. The merger doesn't enforce relations — the plugin does in each player's local game. Game master doesn't need to do anything here.

---

## Command Reference

```bash
ksp-club status                   # who has submitted, their UT, output readiness
ksp-club validate                 # check all submissions for issues
ksp-club merge                    # full weekly merge + push
ksp-club merge --dry-run          # preview without writing
ksp-club merge --no-git           # merge without git pull/push
ksp-club distribute               # push existing output without re-merging
ksp-club add-player --id X --name Y --agency Z
```

---

## Troubleshooting

**"No GAME block found"** — submission is corrupted or empty. Ask player to resubmit from KSP.

**"git push failed"** — run `gh auth status` and re-authenticate if needed.

**Player accidentally submitted the wrong save** — delete `submissions/<id>/persistent.sfs` from the saves repo before merging.

**Two players have same vessel name conflict** — merger keeps the first one, warns. Both players should rename vessels to avoid confusion.

**News feed empty after merge** — first merge has no previous universal to diff against. Events will appear from the second merge onward.
