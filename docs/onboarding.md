# KSP CLUB — Player Onboarding Guide

Welcome to the club. This guide gets you from zero to your first week of play.

---

## What You Need

- **Kerbal Space Program 1.12.5** (Steam)
- **KSPClubPlugin** — download from [github.com/wadeolsson/ksp-club/releases/latest](https://github.com/wadeolsson/ksp-club/releases/latest)
- **A GitHub account** — [github.com](https://github.com) (free)
- An invite to the `ksp-club-saves` repo — ask your game master

---

## One-Time Setup

### 1. Create a GitHub token

1. Go to **github.com → Settings → Developer Settings → Personal access tokens → Tokens (classic)**
2. Click **Generate new token (classic)**
3. Name it `KSP CLUB`, tick the **`repo`** scope
4. Click Generate — **copy the token immediately** (only shown once)

### 2. Install the plugin

Download `KSPClubPlugin.dll` (and the three `.png` icon files) from the [latest release](https://github.com/wadeolsson/ksp-club/releases/latest).

Create `GameData/KSPClubPlugin/` in your KSP folder and put all four files inside:

```
GameData/
  KSPClubPlugin/
    KSPClubPlugin.dll
    icon_sync.png
    icon_transfer.png
    icon_fuel.png
    icon_tanker.png
```

**Mac (Steam):** `~/Library/Application Support/Steam/steamapps/common/Kerbal Space Program/GameData/KSPClubPlugin/`

**Windows (Steam):** `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\GameData\KSPClubPlugin\`

### 3. Create your club save

In KSP, create a new **Career** save called exactly `KSP_CLUB`.

### 4. First-run setup

Load the `KSP_CLUB` save. When the Space Center loads, the setup dialog appears automatically:

```
KSP CLUB — Setup
──────────────────────────────────────────
Player ID:      [ your id, e.g. wade      ]
Agency Name:    [ your agency name         ]
Orbit Color:    [ blue / red / green / ... ]
GitHub Token:   [ ghp_...                  ]
Repo Owner:     [ wadeolsson               ]
Repo Name:      [ ksp-club-saves           ]
Club Save Name: [ KSP_CLUB                 ]
──────────────────────────────────────────
```

Available colors: `blue`, `red`, `green`, `orange`, `purple`, `yellow`, `cyan`, `pink`

Hit **Save**.

### 5. Your first Kerbals

The plugin automatically generates 4 random Kerbals for you the first time you enter the Space Center. Check the Astronaut Complex — they're your crew.

> **Important:** Do not use Jeb, Val, Bill, or Bob. They belong to the shared universe, not to any agency. The game will remind you when you open the Astronaut Complex.

### 6. Submit your first save

Click the **blue toolbar button** (top right) → **Submit My Save**. This pushes your save to the club. Your game master will run the first merge and you'll see other players' vessels in your universe next session.

---

## Every Week

### Start of week — get your new save

When KSP launches, check the main menu. If a new merged save is available, a prompt appears automatically:

> "The game master has merged this week's saves. Download now?"

Click **Download**. Load the `KSP_CLUB` save — your progress is intact and everyone else's latest universe is in there.

### During the week — play

Launch rockets, do science, hire Kerbals, expand your fleet. The plugin tags everything automatically.

**Three rules:**
1. **Only play the `KSP_CLUB` save** for club missions
2. **Don't use Jeb, Val, Bill, or Bob** — hire random recruits only
3. **Don't terminate or recover other players' vessels** — the plugin blocks this, but be aware

### End of week — submit

Click the **blue toolbar button** → **Submit My Save**. Done.

---

## Toolbar Buttons

| Icon | Scene | What it does |
|------|-------|-------------|
| 🔵 Blue rocket | Space Center | Submit save, check news, manage relations, settings |
| 🟢 Green ships | Tracking Station | Transfer a vessel to another player |
| 🟠 Orange tank | Flight | Set vessel as fuel tanker, refuel from nearby tanker |

---

## Fuel Tanker System

Any vessel can be set as a fuel tanker. In flight, click the **orange tank toolbar button**:

- **Set This Vessel as Tanker** → configure prices per resource and reserve %
- **Nearby Tankers** → if another player's tanker is within 50m (orbit) or 500m (landed), refuel from it

Pricing: tanker owner sets ◆ funds per unit. Friendly agencies can get a discount. Hostile agencies can't access your tanker at all.

Fuel transactions are processed on the next weekly merge — funds move between accounts and the tanker's fuel decreases in the universal state.

---

## Diplomatic Relations

Click the **blue toolbar button** → **Relations** to set your stance toward each agency:

- **Friendly** — full CommNet relay access, tanker discounts, bright orbit colors
- **Neutral** (default) — standard access, dimmed orbit colors
- **Hostile** — blocked from your relays and tanker, dim red orbit color

---

## Weekly News

After the game master runs the merge, a **Weekly Report** pops up when you enter the Space Center. It shows what everyone did this week — launches, landings, Kerbal hires, vessel recoveries. Also accessible anytime via the blue toolbar → **News**.

---

## Troubleshooting

**Setup dialog doesn't appear** — click the blue toolbar button → Settings.

**Accidentally used a stock Kerbal** — tell your game master before the merge. They can fix it.

**Played in the wrong save** — you'll need to redownload last week's output and start that session over. Tell your game master.

**Plugin not loading** — check KSP.log for `[KSPClub]` lines. Make sure all four files are in `GameData/KSPClubPlugin/`.

**Submit button says "Could not find save file"** — try Escape → Save Game manually first, then submit.
