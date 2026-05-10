"""
Layer separation: split a parsed KSP save into persistent (player-owned)
and dynamic (shared world) pieces.

Persistent layer — belongs to a player, never overwritten by sync:
  - Their own VESSELs (identified by playerID field stamped by the plugin)
  - Their own KERBALs (same)
  - Career progression SCENARIOs (funds, science, tech tree, contracts, etc.)

Dynamic layer — synced from the universal state each week:
  - Other players' VESSELs
  - Other players' KERBALs
  - World-state SCENARIOs (facility levels, asteroids, comms network, etc.)

The FLIGHTSTATE.UT is special: the canonical UT comes from the universal state
(max UT across all submissions), not from any one player's save.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from merger.sfs.parser import Node

# --- SCENARIO classification ---

# These scenarios track a player's career progression and belong to them alone.
PERSISTENT_SCENARIOS: frozenset[str] = frozenset({
    "Funding",
    "Reputation",
    "ResearchAndDevelopment",
    "ProgressTracking",
    "ContractSystem",
    "VesselRecovery",
    "ScenarioAchievements",
    "PartUpgradeManager",
    "AlarmClockScenario",
    "KerbalInventoryScenario",
    "ScenarioNewGameIntro",
    "DeployedScience",        # player's own deployed science instruments
    "SCANcontroller",         # player's own map scan data
    "KSPClubScenario",               # plugin's owned-vessel-IDs list
    "ScenarioUpgradeableFacilities", # each player upgrades their own KSC buildings
    "StrategySystem",                # each player manages their own admin strategies
    "ScenarioCustomWaypoints",       # each player sets their own waypoints
    "ScenarioContractEvents",        # each player's own mission control event log
    "SentinelScenario",              # each player manages their own sentinel targets
    "ScenarioDestructibles",         # each player has their own building damage state
})

# These scenarios describe the shared world state and come from the universal state.
DYNAMIC_SCENARIOS: frozenset[str] = frozenset({
    "ROCScenario",               # world surface features
    "ResourceScenario",          # world resource seed / settings
    "ScenarioDiscoverableObjects", # shared asteroid / comet spawning
    "CommNetScenario",           # shared communications network
    "KPBSScenario",              # shared planetary base systems world state
})

# Base-game named Kerbals that ship with KSP. These are never player-owned;
# they live in the universal/dynamic layer.
STOCK_KERBALS: frozenset[str] = frozenset({
    "Jebediah Kerman",
    "Valentina Kerman",
    "Bill Kerman",
    "Bob Kerman",
    "Lodwin Kerman",
    "Genelan Kerman",
    "Aldoly Kerman",
    "Shepoly Kerman",
    "Mortimer Kerman",
    "Wernher von Kerman",
})


@dataclass
class FuelTransaction:
    """A fuel purchase recorded by the plugin during gameplay."""
    buyer:      str
    seller:     str
    resource:   str
    amount:     float
    total_cost: float
    tanker_pid: str   # persistentId of the tanker vessel as a string
    timestamp:  float


@dataclass
class PlayerContribution:
    """
    Everything extracted from one player's save submission that belongs to them.
    This is the unit that gets merged into the universal state.
    """
    player_id: str
    ut: float                               # UT from this save (used to find max)
    game_values: list[list[str]]            # top-level GAME key-value pairs
    parameters: Node | None                 # GAME > PARAMETERS block
    vessels: list[Node] = field(default_factory=list)
    kerbals: list[Node] = field(default_factory=list)
    scenarios: dict[str, Node] = field(default_factory=dict)  # name -> Node
    transactions: list[FuelTransaction] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)


def extract(root: Node, player_id: str, claim_untagged: bool = True) -> PlayerContribution:
    """
    Extract a PlayerContribution from a parsed save file.

    Ownership is determined in priority order:
      1. KSPClubScenario.OWNED_VESSELS / OWNED_KERBALS (most reliable — written
         by the plugin's in-memory tracking, survives before playerID stamp works)
      2. playerID field stamped on the vessel/Kerbal node by the plugin
      3. No playerID → claim_untagged fallback (pre-plugin saves)
    """
    game = root.get_child("GAME")
    if game is None:
        raise ValueError("No GAME block found in save file")

    contrib = PlayerContribution(
        player_id=player_id,
        ut=0.0,
        game_values=list(game.values),
        parameters=game.get_child("PARAMETERS"),
    )

    # --- UT ---
    fs = game.get_child("FLIGHTSTATE")
    if fs is None:
        raise ValueError("No FLIGHTSTATE block found in save file")
    try:
        contrib.ut = float(fs.get("UT", "0"))
    except ValueError:
        contrib.warnings.append("Could not parse UT value; defaulting to 0")

    # Read authoritative ownership lists from KSPClubScenario if present
    owned_vessel_ids, owned_kerbal_names = _read_club_scenario(game)

    # --- vessels ---
    for vessel in fs.get_children("VESSEL"):
        pid = vessel.get("persistentId", "")
        vid = vessel.get("playerID", "")

        # Skip PRELAUNCH — vessel hasn't left the pad, shouldn't be in the shared universe
        if vessel.get("sit", "") == "PRELAUNCH":
            continue

        # Vessel transfer: player has gifted this vessel to someone else.
        # Change ownership now so it lands in the target's save after this merge.
        transfer_target = vessel.get("transferTarget", "")
        if transfer_target and (pid in owned_vessel_ids or vid == player_id):
            vessel.set("playerID", transfer_target)
            vessel.remove("transferTarget")
            contrib.vessels.append(vessel)
            contrib.warnings.append(
                f"Vessel '{vessel.get('name', '?')}' transferred from "
                f"{player_id} → {transfer_target}."
            )
            continue

        if pid in owned_vessel_ids:
            contrib.vessels.append(vessel)
        elif vid == player_id:
            contrib.vessels.append(vessel)
        elif vid == "" and pid not in owned_vessel_ids:
            if claim_untagged:
                contrib.vessels.append(vessel)
                contrib.warnings.append(
                    f"Vessel '{vessel.get('name', '?')}' has no playerID — "
                    f"claimed for {player_id}. Install the plugin to fix this."
                )

    # --- Kerbals ---
    roster = game.get_child("ROSTER")
    if roster:
        for kerbal in roster.get_children("KERBAL"):
            name = kerbal.get("name", "")
            kid  = kerbal.get("playerID", "")

            if name in STOCK_KERBALS:
                continue  # always dynamic layer

            if name in owned_kerbal_names:
                contrib.kerbals.append(kerbal)
            elif kid == player_id:
                contrib.kerbals.append(kerbal)
            elif kid == "" and name not in owned_kerbal_names:
                if claim_untagged:
                    contrib.kerbals.append(kerbal)
                    contrib.warnings.append(
                        f"Kerbal '{name}' has no playerID — "
                        f"claimed for {player_id}. Install the plugin to fix this."
                    )

    # --- scenarios ---
    for scenario in game.get_children("SCENARIO"):
        name = scenario.get("name", "")
        if name in PERSISTENT_SCENARIOS:
            contrib.scenarios[name] = scenario
        elif name not in DYNAMIC_SCENARIOS:
            contrib.scenarios[name] = scenario
            contrib.warnings.append(
                f"Unknown SCENARIO '{name}' — treating as persistent (keeping with player)."
            )

    # --- fuel transactions from KSPClubScenario ---
    for scenario in game.get_children("SCENARIO"):
        if scenario.get("name") != "KSPClubScenario":
            continue
        txs = scenario.get_child("TRANSACTIONS")
        if not txs:
            break
        for tx in txs.get_children("TX"):
            try:
                contrib.transactions.append(FuelTransaction(
                    buyer=tx.get("buyer", ""),
                    seller=tx.get("seller", ""),
                    resource=tx.get("resource", ""),
                    amount=float(tx.get("amount", "0")),
                    total_cost=float(tx.get("totalCost", "0")),
                    tanker_pid=tx.get("tankerPid", ""),
                    timestamp=float(tx.get("timestamp", "0")),
                ))
            except ValueError:
                contrib.warnings.append("Could not parse a fuel transaction — skipping.")
        break

    return contrib


def _read_club_scenario(game: Node) -> tuple[set[str], set[str]]:
    """
    Read the KSPClubScenario block and return (owned_vessel_ids, owned_kerbal_names).
    Both are sets of strings. Returns empty sets if the scenario is absent.
    """
    for scenario in game.get_children("SCENARIO"):
        if scenario.get("name") != "KSPClubScenario":
            continue
        vessel_ids: set[str] = set()
        owned_v = scenario.get_child("OWNED_VESSELS")
        if owned_v:
            vessel_ids = set(owned_v.get_all("id"))
        kerbal_names: set[str] = set()
        owned_k = scenario.get_child("OWNED_KERBALS")
        if owned_k:
            kerbal_names = set(owned_k.get_all("name"))
        return vessel_ids, kerbal_names
    return set(), set()
