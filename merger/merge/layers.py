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
    "KSPClubScenario",        # plugin's owned-vessel-IDs list
})

# These scenarios describe the shared world state and come from the universal state.
DYNAMIC_SCENARIOS: frozenset[str] = frozenset({
    "ROCScenario",
    "ResourceScenario",
    "ScenarioCustomWaypoints",
    "ScenarioDestructibles",
    "ScenarioDiscoverableObjects",
    "ScenarioUpgradeableFacilities",
    "StrategySystem",
    "ScenarioContractEvents",
    "SentinelScenario",
    "CommNetScenario",
    "KPBSScenario",
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
    warnings: list[str] = field(default_factory=list)


def extract(root: Node, player_id: str, claim_untagged: bool = True) -> PlayerContribution:
    """
    Extract a PlayerContribution from a parsed save file.

    Args:
        root:           ROOT node returned by parse()
        player_id:      the submitting player's ID (e.g. "wade")
        claim_untagged: if True, vessels/Kerbals with no playerID are treated
                        as owned by this player (needed before the plugin is
                        installed; set False once all players have the plugin)

    Returns:
        PlayerContribution with the player's vessels, Kerbals, and scenarios.
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

    # --- vessels ---
    for vessel in fs.get_children("VESSEL"):
        vid = vessel.get("playerID", "")
        if vid == player_id:
            contrib.vessels.append(vessel)
        elif vid == "":
            if claim_untagged:
                contrib.vessels.append(vessel)
                contrib.warnings.append(
                    f"Vessel '{vessel.get('name', '?')}' has no playerID — "
                    f"claimed for {player_id}. Install the plugin to fix this."
                )
            # else: belongs to someone else, skip
        # else: belongs to a different player, skip (their own submission has it)

    # --- Kerbals ---
    roster = game.get_child("ROSTER")
    if roster:
        for kerbal in roster.get_children("KERBAL"):
            name = kerbal.get("name", "")
            kid = kerbal.get("playerID", "")

            if name in STOCK_KERBALS:
                # Stock Kerbals always live in the dynamic/universal layer
                continue

            if kid == player_id:
                contrib.kerbals.append(kerbal)
            elif kid == "":
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
            # Unknown scenario — keep it in persistent layer to be safe
            contrib.scenarios[name] = scenario
            contrib.warnings.append(
                f"Unknown SCENARIO '{name}' — treating as persistent (keeping with player)."
            )

    return contrib
