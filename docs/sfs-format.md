# KSP .sfs Format Notes

Technical reference for anyone working on the merger tool.

---

## Format Overview

KSP save files (`.sfs`) use a custom plain-text format — not JSON, not XML. The grammar is simple:

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

Rules:
- Block name is always on its own line; `{` is always the next non-blank line
- `key = value` splits on the **first** `=` only — values can contain `=`
- Values can be empty: `key = `
- Duplicate keys within a block are allowed (KSP uses them for part lists)
- Multiple sibling blocks with the same name are allowed (`VESSEL`, `KERBAL`, `PART`, ...)
- Indentation is tabs; depth mirrors the nesting level
- Comments use `//` (rare in practice)

---

## Top-Level Structure

```
GAME
{
    version = 1.12.5
    Title = My Save (CAREER)
    Mode = CAREER           ← SANDBOX, CAREER, or SCIENCE_SANDBOX
    launchID = 42           ← increments each launch; use max across saves on merge
    Seed = 1363165847       ← universe random seed; must match across all players

    RemovedROCs { }         ← surface features that have been collected
    CometNames { }          ← names given to comets
    PARAMETERS { ... }      ← game difficulty settings
    SCENARIO { ... }        ← one per module (career progression, world state, etc.)
    ...more SCENARIOs...
    FLIGHTSTATE { ... }     ← vessels and universe time
    LoaderInfo { ... }      ← mod loading info (cosmetic)
    ROSTER { ... }          ← all Kerbals
    MESSAGESYSTEM { ... }   ← in-game message history (cosmetic)
}
```

---

## FLIGHTSTATE

```
FLIGHTSTATE
{
    version = 1.12.5
    UT = 805.86225097638533     ← universal time in seconds
    activeVessel = 0            ← index of the active vessel (reset to 0 on merge)
    mapViewFiltering = -1026    ← cosmetic UI state
    commNetUIModeTracking = Network
    commNetUIModeFlight = Path

    VESSEL { ... }              ← one per vessel in the universe
    VESSEL { ... }
    ...
}
```

### VESSEL block

Key fields the merger cares about:

```
VESSEL
{
    pid = b4fe4e86...           ← UUID, unique per vessel instance
    persistentId = 3805097021   ← uint32, used as ownership key
    name = My Rocket
    type = Ship                 ← Ship, Debris, SpaceObject, Station, Probe, ...
    sit = ORBITING              ← ORBITING, LANDED, SPLASHED, PRELAUNCH, SUB_ORBITAL, ESCAPING
    playerID = wade             ← ADDED BY THE PLUGIN — identifies the owner

    ORBIT { ... }               ← present when sit = ORBITING / ESCAPING / SUB_ORBITAL
    PART { ... }                ← one per part
    ...
}
```

### ORBIT block

```
ORBIT
{
    SMA = 700000.0              ← semi-major axis (metres); negative = hyperbolic
    ECC = 0.01                  ← eccentricity (< 1 elliptical, >= 1 hyperbolic)
    INC = 0.0                   ← inclination (degrees)
    LPE = 0.0                   ← longitude of periapsis (degrees)
    LAN = 0.0                   ← longitude of ascending node (degrees)
    MNA = 1.0                   ← mean anomaly at epoch (radians)
    EPH = 100.0                 ← epoch (seconds) — when MNA was recorded
    REF = 1                     ← reference body index (see table below)
}
```

**Advancing an orbit to a new time:**
```
n       = sqrt(μ / SMA³)        # mean motion (rad/s)
new_MNA = MNA + n * (new_UT - EPH)
new_EPH = new_UT
```

**REF body index → gravitational parameter (μ, m³/s²):**

| REF | Body     | μ (m³/s²)        |
|-----|----------|------------------|
| 0   | Kerbol   | 1.1723328 × 10¹⁸ |
| 1   | Kerbin   | 3.5316 × 10¹²    |
| 2   | Mun      | 6.5138 × 10¹⁰    |
| 3   | Minmus   | 1.7658 × 10⁹     |
| 4   | Moho     | 1.6861 × 10¹¹    |
| 5   | Eve      | 8.1717 × 10¹²    |
| 6   | Duna     | 3.0136 × 10¹¹    |
| 7   | Ike      | 1.8568 × 10¹⁰    |
| 8   | Jool     | 2.8253 × 10¹⁴    |
| 9   | Laythe   | 1.9620 × 10¹²    |
| 10  | Vall     | 2.0748 × 10¹¹    |
| 11  | Bop      | 2.4868 × 10⁹     |
| 12  | Tylo     | 2.8253 × 10¹²    |
| 13  | Gilly    | 8.2894 × 10⁶     |
| 14  | Pol      | 7.2170 × 10⁸     |
| 15  | Dres     | 2.1484 × 10¹⁰    |
| 16  | Eeloo    | 7.4411 × 10¹⁰    |

---

## ROSTER

```
ROSTER
{
    KERBAL
    {
        name = Jebediah Kerman
        gender = Male
        type = Crew             ← Crew, Tourist, Unowned
        trait = Pilot           ← Pilot, Engineer, Scientist
        state = Available       ← Available, Assigned, Dead, Missing
        brave = 0.5
        dumb = 0.5
        badS = True             ← "badS" = the orange suit heroes
        playerID = wade         ← ADDED BY THE PLUGIN; absent for stock Kerbals
        ...
    }
}
```

---

## SCENARIO blocks

Each installed module (stock or mod) contributes a SCENARIO block. The merger classifies them as persistent (player-owned) or dynamic (world state).

**Persistent** (kept with the player across merges):

| Name | Contents |
|------|----------|
| `Funding` | `funds = 32154.19` |
| `Reputation` | `rep = 0` |
| `ResearchAndDevelopment` | science points + unlocked tech nodes |
| `ProgressTracking` | mission milestones (first orbit, first Mun landing, etc.) |
| `ContractSystem` | active, completed, and failed contracts |
| `VesselRecovery` | recovery stats |
| `ScenarioAchievements` | ribbon/achievement state |
| `PartUpgradeManager` | unlocked part upgrades |
| `AlarmClockScenario` | player's saved alarms |
| `KerbalInventoryScenario` | Kerbal suit/inventory state |
| `DeployedScience` | player's deployed science instruments |
| `SCANcontroller` | player's planetary map scan data |
| `KSPClubScenario` | **plugin data** — owned vessel persistentId list |

**Dynamic** (taken from the universal state / max-UT submission):

| Name | Contents |
|------|----------|
| `ROCScenario` | surface feature state |
| `ResourceScenario` | ore/resource settings |
| `ScenarioCustomWaypoints` | world waypoints |
| `ScenarioDestructibles` | KSC building damage state |
| `ScenarioDiscoverableObjects` | asteroids, comets |
| `ScenarioUpgradeableFacilities` | KSC facility levels |
| `StrategySystem` | administration building strategies |
| `ScenarioContractEvents` | contract event log |
| `SentinelScenario` | sentinel telescope targets |
| `CommNetScenario` | communications network state |
| `KPBSScenario` | Kerbal Planetary Base Systems state |

Unknown scenarios (from unrecognised mods) are kept as persistent by default with a warning logged.

---

## Parser / Serializer

See `merger/sfs/parser.py` and `merger/sfs/serializer.py`.

The `Node` class represents one block:
- `node.name` — block name (e.g. `"VESSEL"`)
- `node.values` — `[[key, value], ...]` — ordered, allows duplicates
- `node.children` — `[Node, ...]` — ordered list of child blocks

`parse(text)` returns a synthetic `ROOT` node whose only child is the top-level `GAME` block.
`serialize(root)` writes the tree back to `.sfs` format with tab indentation.
