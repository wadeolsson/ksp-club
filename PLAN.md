# KSP CLUB — Build Plan & Status

## What's Been Built

### Merger Tool (Python)

| Module | Status | What it does |
|--------|--------|-------------|
| `merger/sfs/parser.py` | ✅ | `.sfs` → `Node` tree |
| `merger/sfs/serializer.py` | ✅ | `Node` tree → `.sfs` |
| `merger/merge/layers.py` | ✅ | Persistent/dynamic classification, `extract()`, fuel transactions |
| `merger/merge/time.py` | ✅ | Kepler orbit advancement to canonical UT |
| `merger/merge/vessels.py` | ✅ | Collect vessels, ownership conflict detection |
| `merger/merge/kerbals.py` | ✅ | Merge rosters, dedup names, strip stock Kerbals |
| `merger/merge/mods.py` | ✅ | Part name validation against modlist |
| `merger/merge/builder.py` | ✅ | Full pipeline + fuel transaction processing |
| `merger/merge/news.py` | ✅ | Weekly news feed (vessel events + Kerbal hires/deaths) |
| `merger/storage/git.py` | ✅ | `git pull/push` on the saves repo |
| `merger/cli.py` | ✅ | `merge`, `validate`, `status`, `distribute`, `add-player` |
| `merger/config.py` | ✅ | Saves repo path resolution, player registry |

### KSP Plugin (C#)

| File | Status | What it does |
|------|--------|-------------|
| `PlayerConfig.cs` | ✅ | Persist=true — config, save sync, vessel/Kerbal stamping, news download |
| `ClubScenario.cs` | ✅ | ScenarioModule — owned vessels/Kerbals, tanker configs, fuel transactions |
| `VesselTagger.cs` | ✅ | Claims new vessels on creation, retries if scenario not ready |
| `VesselProtection.cs` | ✅ | Blocks fly/recover/delete of non-owned vessels |
| `VesselTrading.cs` | ✅ | Formal vessel transfer between players |
| `KerbalRestrictor.cs` | ✅ | Warns against using stock Kerbals |
| `StarterKerbals.cs` | ✅ | Auto-generates 4 Kerbals for new players |
| `OrbitColors.cs` | ✅ | Per-player orbit/icon colors, relation modulation, periodic reapply |
| `Relations.cs` | ✅ | Friendly/Neutral/Hostile enum |
| `AgencyCommNet.cs` | ✅ | Agency-locked relay access based on relations |
| `FuelTanker.cs` | ✅ | Tanker setup, refuel UI, live pump, map icon overlay |
| `TankerConfig.cs` | ✅ | Tanker config + transaction data classes |
| `SaveSyncUI.cs` | ✅ | Toolbar — submit, news, relations, settings |
| `GitHubClient.cs` | ✅ | GitHub Contents API via UnityWebRequest |

### Infrastructure

| Item | Status |
|------|--------|
| `ksp-club` GitHub repo (public) | ✅ |
| `ksp-club-saves` GitHub repo (private) | ✅ |
| GitHub Actions auto-merge workflow | ✅ |
| Plugin releases (v0.1 – v0.4) | ✅ |
| 3 players live (wade, kent, ed) | ✅ |

---

## Planned Features

### Near-term
- v0.5.0 release
- Kerbal protection (block hiring other players' Kerbals from roster)
- Docs update (onboarding + game-master guides)
- Discord webhook — auto-post news feed to Discord after each merge
- "First to" hall of fame — permanent record of first to reach each body
- SOS distress beacon — stranded vessel visible to friendly agencies

### Medium-term
- Contract marketplace — post bounties for other players to fulfill
- Season scoring — weekly points, season winner
- Agency profiles — auto-generated stat cards per agency
- Territorial claims — flag planting = agency control of a body
- Kerbal crew loan — temporary cross-agency Kerbal transfer

### Long-term
- Alliance treaties with enforced terms
- War declaration mechanic
- Asteroid mining claims
- Debris cleanup bounties
- Espionage / intel reports

---

## Scenario Classification

**Persistent (player-owned, never overwritten):**
`Funding`, `Reputation`, `ResearchAndDevelopment`, `ProgressTracking`,
`ContractSystem`, `VesselRecovery`, `ScenarioAchievements`, `PartUpgradeManager`,
`AlarmClockScenario`, `KerbalInventoryScenario`, `ScenarioNewGameIntro`,
`DeployedScience`, `SCANcontroller`, `KSPClubScenario`,
`ScenarioUpgradeableFacilities`, `StrategySystem`,
`ScenarioCustomWaypoints`, `ScenarioContractEvents`, `SentinelScenario`

**Dynamic (shared world, replaced each merge):**
`ROCScenario`, `ResourceScenario`, `ScenarioDestructibles`,
`ScenarioDiscoverableObjects`, `CommNetScenario`, `KPBSScenario`

---

## Key Technical Decisions

- **Canonical UT** = max UT across all submissions; orbits advanced via Kepler propagation
- **Ownership** = `KSPClubScenario.OWNED_VESSELS/KERBALS` (primary) + `playerID` stamp (fallback)
- **Fuel transactions** = recorded in `KSPClubScenario.TRANSACTIONS`, processed by merger (debit buyer, credit seller, reduce tanker fuel)
- **Tanker config** = stamped on vessel node so other players see prices in dynamic layer
- **CommNet** = `antennaRelay.power` zeroed for Neutral/Hostile vessels every 6s in flight
- **Orbit colors** = `playerColor = R,G,B` stamped on vessel nodes, cached at save load
- **News feed** = diffs universal state before/after merge + Kerbal roster changes
