# KSP CLUB — Dual-State Merge System

A two-component system for asynchronous multiplayer Kerbal Space Program.

## Components

- **`merger/`** — Python CLI tool: pulls player save files, merges them into a universal state, rebuilds per-player saves
- **`plugin/`** — C# KSP plugin: stamps vessel ownership, restricts base-game Kerbals, future in-game submission

## How It Works

Each player plays KSP independently. At the end of the week they submit their save file. The merger tool:

1. Separates each save into a **persistent layer** (the player's own vessels, science, funds, tech tree) and a **dynamic layer** (everyone else's stuff)
2. Combines all persistent layers into a single **universal state**
3. Rebuilds each player's save: their persistent layer + everyone else's updated universe injected in

No tech tree conflicts. No duplicate vessels. No multiplayer desync.

## Docs

- [Full Build Plan](PLAN.md)
- [Player Onboarding](docs/onboarding.md)
- [Game Master Guide](docs/game-master.md)
- [.sfs Format Notes](docs/sfs-format.md)

## Requirements

- KSP 1.12.5
- Python 3.10+
- Agreed mod list (see `ksp-club-saves` repo)
