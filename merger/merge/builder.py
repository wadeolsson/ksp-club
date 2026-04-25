"""
Merge builder — orchestrates the full weekly merge pipeline.

Pipeline:
  1. Extract PlayerContribution from each submission
  2. Find canonical UT (max across all submissions)
  3. Advance all vessel orbits to canonical UT
  4. Collect all vessels (check for ownership conflicts)
  5. Merge Kerbal rosters
  6. Build universal state (all vessels + Kerbals, canonical UT)
  7. Rebuild each player's save (their persistent layer + universal world)
"""

from __future__ import annotations

import copy
from merger.sfs.parser import Node
from merger.merge.layers import extract, PlayerContribution, DYNAMIC_SCENARIOS
from merger.merge.time import advance_vessel
from merger.merge.vessels import collect_vessels
from merger.merge.kerbals import get_stock_kerbals, merge_kerbals


def build(
    submissions: dict[str, Node],
    claim_untagged: bool = True,
    allowed_parts: set[str] | None = None,
) -> tuple[Node, dict[str, Node], list[str]]:
    """
    Run the full merge pipeline.

    Args:
        submissions:    {player_id: parsed ROOT node from their save submission}
        claim_untagged: treat vessels/Kerbals with no playerID as owned by the
                        submitting player (needed before plugin is installed)
        allowed_parts:  if provided, warn about parts not in this set

    Returns:
        (universal_root, {player_id: rebuilt_save_root}, warnings)
    """
    warnings: list[str] = []

    if not submissions:
        raise ValueError("No submissions provided")

    # Step 1 — extract contributions
    contributions: dict[str, PlayerContribution] = {}
    for player_id, root in submissions.items():
        contrib = extract(root, player_id, claim_untagged)
        contributions[player_id] = contrib
        warnings.extend(contrib.warnings)

    # Step 2 — canonical UT
    canonical_ut = max(c.ut for c in contributions.values())
    base_player_id = max(contributions, key=lambda pid: contributions[pid].ut)
    base_root = submissions[base_player_id]
    base_game = base_root.get_child("GAME")

    # Step 3 — advance all vessel orbits
    for contrib in contributions.values():
        for vessel in contrib.vessels:
            w = advance_vessel(vessel, canonical_ut)
            warnings.extend(w)

    # Step 4 — collect vessels (conflict check)
    contrib_list = list(contributions.values())
    all_vessels, vessel_warnings = collect_vessels(contrib_list)
    warnings.extend(vessel_warnings)

    # Part validation (optional)
    if allowed_parts:
        from merger.merge.mods import validate_parts
        warnings.extend(validate_parts(contrib_list, allowed_parts))

    # Step 5 — merge Kerbals
    stock_kerbals = get_stock_kerbals(base_game)
    merged_kerbals, kerbal_warnings = merge_kerbals(contrib_list, stock_kerbals)
    warnings.extend(kerbal_warnings)

    # Shared world: dynamic scenarios from the most-current submission
    dynamic_scenarios = [
        s for s in base_game.get_children("SCENARIO")
        if s.get("name", "") in DYNAMIC_SCENARIOS
    ]

    # Max launchID across all submissions to prevent ID collisions
    max_launch_id = max(
        _int_val(contributions[pid].game_values, "launchID", 1)
        for pid in contributions
    )

    # Step 6 — universal state
    universal = _build_universal(
        base_game, canonical_ut, all_vessels, merged_kerbals,
        dynamic_scenarios, max_launch_id,
    )

    # Step 7 — rebuild each player's save
    rebuilt: dict[str, Node] = {}
    for player_id, contrib in contributions.items():
        player_game = submissions[player_id].get_child("GAME")
        rebuilt[player_id] = _build_player_save(
            contrib, player_game, canonical_ut,
            all_vessels, merged_kerbals, dynamic_scenarios, max_launch_id,
        )

    return universal, rebuilt, warnings


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _build_universal(
    base_game: Node,
    canonical_ut: float,
    vessels: list[Node],
    kerbals: list[Node],
    dynamic_scenarios: list[Node],
    max_launch_id: int,
) -> Node:
    game = Node("GAME")
    game.values = [list(pair) for pair in base_game.values]
    game.set("Title", "KSP CLUB - Universal State")
    game.set("launchID", str(max_launch_id))

    _copy_child(base_game, game, "RemovedROCs")
    _copy_child(base_game, game, "CometNames")
    _copy_child(base_game, game, "PARAMETERS")

    for s in dynamic_scenarios:
        game.children.append(copy.deepcopy(s))

    base_fs = base_game.get_child("FLIGHTSTATE")
    game.children.append(_build_flightstate(base_fs, canonical_ut, vessels))

    _copy_child(base_game, game, "LoaderInfo")
    game.children.append(_build_roster(kerbals))
    _copy_child(base_game, game, "MESSAGESYSTEM")

    root = Node("ROOT")
    root.children.append(game)
    return root


def _build_player_save(
    contrib: PlayerContribution,
    player_game: Node,
    canonical_ut: float,
    all_vessels: list[Node],
    merged_kerbals: list[Node],
    dynamic_scenarios: list[Node],
    max_launch_id: int,
) -> Node:
    game = Node("GAME")
    game.values = [list(pair) for pair in contrib.game_values]
    game.set("launchID", str(max_launch_id))

    _copy_child(player_game, game, "RemovedROCs")
    _copy_child(player_game, game, "CometNames")

    if contrib.parameters is not None:
        game.children.append(copy.deepcopy(contrib.parameters))

    # Persistent scenarios (player's own career progression)
    for scenario in contrib.scenarios.values():
        game.children.append(copy.deepcopy(scenario))

    # Dynamic scenarios (shared world from universal)
    for s in dynamic_scenarios:
        game.children.append(copy.deepcopy(s))

    player_fs = player_game.get_child("FLIGHTSTATE")
    game.children.append(_build_flightstate(player_fs, canonical_ut, all_vessels))

    _copy_child(player_game, game, "LoaderInfo")
    game.children.append(_build_roster(merged_kerbals))
    _copy_child(player_game, game, "MESSAGESYSTEM")

    root = Node("ROOT")
    root.children.append(game)
    return root


def _build_flightstate(
    base_fs: Node | None,
    canonical_ut: float,
    vessels: list[Node],
) -> Node:
    fs = Node("FLIGHTSTATE")
    # Preserve cosmetic player preferences from their own flightstate
    version = base_fs.get("version", "1.12.5") if base_fs else "1.12.5"
    map_filter = base_fs.get("mapViewFiltering", "-1026") if base_fs else "-1026"
    commnet_track = base_fs.get("commNetUIModeTracking", "Network") if base_fs else "Network"
    commnet_flight = base_fs.get("commNetUIModeFlight", "Path") if base_fs else "Path"

    fs.values = [
        ["version", version],
        ["UT", repr(canonical_ut)],
        ["activeVessel", "0"],
        ["mapViewFiltering", map_filter],
        ["commNetUIModeTracking", commnet_track],
        ["commNetUIModeFlight", commnet_flight],
    ]
    fs.children = list(vessels)  # shallow ref — vessels are already deep-copied by advance step
    return fs


def _build_roster(kerbals: list[Node]) -> Node:
    roster = Node("ROSTER")
    roster.children = list(kerbals)
    return roster


def _copy_child(src: Node, dst: Node, name: str) -> None:
    child = src.get_child(name)
    if child is not None:
        dst.children.append(copy.deepcopy(child))


def _int_val(values: list[list[str]], key: str, default: int) -> int:
    for k, v in values:
        if k == key:
            try:
                return int(v)
            except ValueError:
                return default
    return default
