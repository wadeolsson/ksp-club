"""
Kerbal roster merging and deduplication.

Stock Kerbals (Jeb, Val, Bill, Bob, ...) live in the universal/dynamic layer
and are taken from the most-current submission. Player-owned Kerbals come
from each player's persistent layer.

If two players' custom Kerbals share a name (shouldn't happen once the plugin
is running but can happen early on), the duplicate is renamed with a numeric
suffix: "Kerman1", "Kerman2", etc.
"""

from __future__ import annotations

from merger.sfs.parser import Node
from merger.merge.layers import PlayerContribution, STOCK_KERBALS


def get_stock_kerbals(game: Node) -> list[Node]:
    """
    Extract the stock/base-game Kerbals from a GAME node.
    These come from the universal state (taken from the max-UT submission).
    """
    roster = game.get_child("ROSTER")
    if roster is None:
        return []
    return [
        k for k in roster.get_children("KERBAL")
        if k.get("name", "") in STOCK_KERBALS
    ]


def merge_kerbals(
    contributions: list[PlayerContribution],
    stock_kerbals: list[Node],
) -> tuple[list[Node], list[str]]:
    """
    Merge Kerbal rosters from all player contributions plus the stock Kerbals.

    Returns:
        (merged_kerbal_nodes, warnings)
    """
    roster: dict[str, Node] = {}   # name -> Node
    warnings: list[str] = []

    # Stock Kerbals go in first (they come from the universal state)
    for k in stock_kerbals:
        roster[k.get("name", "")] = k

    # Player-owned Kerbals
    for contrib in contributions:
        for kerbal in contrib.kerbals:
            name = kerbal.get("name", "")
            if not name:
                continue

            if name in roster:
                # Conflict: rename the incoming duplicate
                new_name = _unique_name(name, roster)
                kerbal.set("name", new_name)
                warnings.append(
                    f"Kerbal name conflict: '{name}' "
                    f"(player '{contrib.player_id}') renamed to '{new_name}'"
                )
                roster[new_name] = kerbal
            else:
                roster[name] = kerbal

    return list(roster.values()), warnings


def _unique_name(base: str, existing: dict[str, object]) -> str:
    i = 1
    while True:
        candidate = f"{base}{i}"
        if candidate not in existing:
            return candidate
        i += 1
