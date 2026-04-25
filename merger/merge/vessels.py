"""
Vessel ownership resolution.

Since each vessel belongs to exactly one player (enforced by the plugin),
conflicts should be rare. This module checks for them and returns the
deduplicated vessel list for the universal state.
"""

from __future__ import annotations

from merger.sfs.parser import Node
from merger.merge.layers import PlayerContribution


def collect_vessels(
    contributions: list[PlayerContribution],
) -> tuple[list[Node], list[str]]:
    """
    Collect all vessels from all player contributions into one list.

    Uses persistentId to detect ownership conflicts (two players claiming
    the same vessel). The first player's version wins; a warning is emitted.

    Returns:
        (vessels, warnings)
    """
    seen: dict[str, str] = {}   # persistentId -> player_id that first claimed it
    vessels: list[Node] = []
    warnings: list[str] = []

    for contrib in contributions:
        for vessel in contrib.vessels:
            pid = vessel.get("persistentId", "")
            name = vessel.get("name", "?")

            if pid and pid in seen:
                warnings.append(
                    f"Vessel '{name}' (persistentId={pid}) claimed by both "
                    f"'{seen[pid]}' and '{contrib.player_id}' — "
                    f"keeping '{seen[pid]}' version"
                )
            else:
                if pid:
                    seen[pid] = contrib.player_id
                vessels.append(vessel)

    return vessels, warnings
