# KSP CLUB — Player Onboarding Guide

Welcome to the club. This guide walks you through everything you need to set up and play your first week.

---

## What You Need

- **Kerbal Space Program 1.12.5** (Steam)
- **The KSP CLUB plugin** — download from the [Releases page](https://github.com/wadeolsson/ksp-club/releases)
- **GitHub Desktop** — [desktop.github.com](https://desktop.github.com) (free)
- **A GitHub account** — [github.com](https://github.com) (free)
- Access to the `ksp-club-saves` repo — ask your game master to invite you

---

## One-Time Setup

### 1. Install the Plugin

1. Download `KSPClubPlugin.dll` from the latest release
2. Copy it into your KSP install:

   **Mac (Steam):**
   ```
   ~/Library/Application Support/Steam/steamapps/common/Kerbal Space Program/GameData/KSPClubPlugin/KSPClubPlugin.dll
   ```

   **Windows (Steam):**
   ```
   C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\GameData\KSPClubPlugin\KSPClubPlugin.dll
   ```

   The `KSPClubPlugin/` folder doesn't exist yet — create it.

3. Launch KSP. If the plugin loaded correctly you'll see a setup prompt the first time you open a save.

### 2. Set Your Player ID

When you first open any save, a dialog appears:

> **KSP CLUB — Player Setup**
> Enter your player ID...

Type the ID your game master gave you (e.g. `wade`). Hit **Save**.

Your ID is stored in `GameData/KSPClubPlugin/PluginData/player.cfg` and survives KSP restarts. You only need to do this once.

> If you need to change your ID later, the dialog can be re-opened — ask your game master how.

### 3. Set Up GitHub Desktop

1. Download and install [GitHub Desktop](https://desktop.github.com)
2. Sign in with your GitHub account
3. Ask your game master to add you as a collaborator on `ksp-club-saves`
4. In GitHub Desktop: **File → Clone Repository** → find `ksp-club-saves` → clone it somewhere easy to find (e.g. your Desktop)

### 4. Get Your Starting Save

Your game master will place your first save at `output/<your-id>/persistent.sfs` in the saves repo.

1. In GitHub Desktop, pull the latest changes (click **Fetch origin**)
2. In the cloned repo folder, navigate to `output/<your-id>/persistent.sfs`
3. Copy it to your KSP saves folder as a new save called `KSP_CLUB`:

   **Mac:**
   ```
   ~/Library/Application Support/Steam/steamapps/common/Kerbal Space Program/saves/KSP_CLUB/persistent.sfs
   ```

   **Windows:**
   ```
   C:\...\Kerbal Space Program\saves\KSP_CLUB\persistent.sfs
   ```

   Create the `KSP_CLUB/` folder if it doesn't exist.

5. Launch KSP and load the **KSP_CLUB** save. That's your club save — only play the club in this save.

---

## Every Week

### Start of Week — Get Your New Save

Your game master runs the merge each week and puts your rebuilt save in `output/<your-id>/persistent.sfs`.

1. Open GitHub Desktop → **Fetch origin** → **Pull**
2. Copy `output/<your-id>/persistent.sfs` → `saves/KSP_CLUB/persistent.sfs`
3. Load the **KSP_CLUB** save in KSP and play

> Your progress from last week is in there. So is everyone else's latest universe.

### During the Week — Play Normally

Launch rockets, do science, build stations — whatever you like. The plugin quietly tags every vessel you create with your player ID in the background.

**Two rules:**
1. **Only play the `KSP_CLUB` save** for club missions. Your other saves are unaffected and separate.
2. **Don't use the stock Kerbals** (Jebediah, Valentina, Bill, Bob). The game will remind you when you open the Astronaut Complex. Hire random recruits instead — they're yours to name and keep.

### End of Week — Submit Your Save

When your game master calls for submissions:

1. In KSP: **Save** and quit to the main menu
2. Find your save file:
   - **Mac:** `~/Library/Application Support/Steam/steamapps/common/Kerbal Space Program/saves/KSP_CLUB/persistent.sfs`
   - **Windows:** `C:\...\Kerbal Space Program\saves\KSP_CLUB\persistent.sfs`
3. Copy it into the saves repo at `submissions/<your-id>/persistent.sfs`
4. Open GitHub Desktop — you should see the file as a changed/new file
5. Write a commit message (e.g. `submission: wade week of 2025-07-14`)
6. Click **Commit to main** then **Push origin**

That's it. Your game master will handle the merge.

---

## Troubleshooting

**"My player ID dialog never appeared"**
Check that `KSPClubPlugin.dll` is in `GameData/KSPClubPlugin/`. If the folder has the file but still no dialog, check the KSP log (`KSP.log` in the game root) for `[KSPClub]` lines.

**"I accidentally used Jeb"**
Let your game master know before the merge. They can manually adjust the roster. Going forward, the plugin warns you — listen to it.

**"I played in the wrong save"**
You'll need to start fresh from the distributed save. Your progress from the wrong save is lost (it won't be merged). Tell your game master before submission day.

**"GitHub Desktop says there's a conflict"**
Don't try to resolve it yourself — message your game master. This usually means two people edited the same file.

**"The save won't load / KSP crashes"**
Check that your mod list exactly matches the club's required mod list. A mismatch between installed mods and the save's part list will cause load failures.
