"""
Mod list validation.

Scans all PART blocks in all player vessels and warns about any part names
not in the approved mod list. Unknown parts won't crash the merge, but players
without the relevant mod won't be able to load vessels that use them.
"""

from __future__ import annotations

from merger.sfs.parser import Node
from merger.merge.layers import PlayerContribution


def validate_parts(
    contributions: list[PlayerContribution],
    allowed_parts: set[str],
) -> list[str]:
    """
    Check every part in every vessel against the allowed parts set.

    Args:
        contributions:  list of PlayerContribution (vessels must already be extracted)
        allowed_parts:  set of allowed part names; empty set disables validation

    Returns:
        List of warning strings, one per unknown part occurrence.
    """
    if not allowed_parts:
        return []

    warnings: list[str] = []
    for contrib in contributions:
        for vessel in contrib.vessels:
            vessel_name = vessel.get("name", "?")
            unknown = _unknown_parts_in_vessel(vessel, allowed_parts)
            for part_name in unknown:
                warnings.append(
                    f"Player '{contrib.player_id}': vessel '{vessel_name}' "
                    f"uses unknown part '{part_name}' — check mod list"
                )
    return warnings


def load_modlist(path: str) -> set[str]:
    """
    Load an allowed-parts set from a modlist file.

    Lines starting with '#' are comments. Each non-blank, non-comment line
    is treated as an allowed part name or mod identifier. Returns an empty
    set if the file doesn't exist (disables validation).
    """
    try:
        with open(path, "r", encoding="utf-8") as f:
            lines = f.readlines()
    except FileNotFoundError:
        return set()

    return {
        line.strip()
        for line in lines
        if line.strip() and not line.strip().startswith("#")
    }


def _unknown_parts_in_vessel(vessel: Node, allowed: set[str]) -> list[str]:
    unknown = []
    seen = set()
    for part in vessel.get_children("PART"):
        name = part.get("name", "")
        if name and name not in allowed and name not in seen:
            unknown.append(name)
            seen.add(name)
    return unknown
