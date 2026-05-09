"""
Club news feed — diffs two universal states and generates punchy event strings.
Written to ksp-club-saves/news/latest.json after each merge.
"""

from __future__ import annotations

import json
from datetime import date
from merger.sfs.parser import Node


# REF index → body name (matches time.py BODY_MU keys)
BODY_NAMES: dict[str, str] = {
    "0":  "Kerbol",  "1":  "Kerbin",  "2":  "Mun",     "3":  "Minmus",
    "4":  "Moho",    "5":  "Eve",     "6":  "Duna",     "7":  "Ike",
    "8":  "Jool",    "9":  "Laythe",  "10": "Vall",     "11": "Bop",
    "12": "Tylo",    "13": "Gilly",   "14": "Pol",      "15": "Dres",
    "16": "Eeloo",
}

# Vessel type → readable word
TYPE_WORD: dict[str, str] = {
    "Ship":        "spacecraft",
    "Probe":       "probe",
    "Station":     "space station",
    "Lander":      "lander",
    "Rover":       "rover",
    "Base":        "surface base",
    "Relay":       "relay satellite",
    "Plane":       "aircraft",
    "EVA":         "Kerbal",
    "Flag":        "flag",
    "SpaceObject": "asteroid",
}

# Types to skip in news (not interesting)
SKIP_TYPES: frozenset[str] = frozenset({"Debris", "Unknown"})


def generate(
    old_universal: Node | None,
    new_universal: Node,
    players: list[dict],
) -> dict:
    """
    Diff old and new universal states. Returns a news dict:
    { "week": "YYYY-MM-DD", "events": [{player, agency, text}, ...] }
    """
    player_map = {p["id"]: p for p in players}

    new_fs = _flightstate(new_universal)
    old_fs = _flightstate(old_universal)

    new_vessels = _vessel_dict(new_fs)
    old_vessels = _vessel_dict(old_fs)

    events: list[dict] = []

    for pid, vessel in new_vessels.items():
        vtype = vessel.get("type", "")
        if vtype in SKIP_TYPES:
            continue

        player_id = vessel.get("playerID", "")
        player    = player_map.get(player_id)
        if not player:
            continue

        agency = player.get("agencyName", player_id)
        name   = vessel.get("name", "Unknown")
        sit    = vessel.get("sit", "")

        if pid not in old_vessels:
            # Brand-new vessel this week
            text = _new_vessel_line(agency, name, vtype, sit, vessel)
        else:
            # Existing vessel — check for situation change
            old_sit = old_vessels[pid].get("sit", "")
            text = _sit_change_line(agency, name, vtype, old_sit, sit, vessel)

        if text:
            events.append({"player": player_id, "agency": agency, "text": text})

    # Recovered/lost vessels (in old but gone from new)
    for pid, vessel in old_vessels.items():
        vtype = vessel.get("type", "")
        if vtype in SKIP_TYPES or pid in new_vessels:
            continue
        player_id = vessel.get("playerID", "")
        player    = player_map.get(player_id)
        if not player:
            continue
        agency = player.get("agencyName", player_id)
        name   = vessel.get("name", "Unknown")
        events.append({
            "player": player_id,
            "agency": agency,
            "text":   f"{agency} recovers {name}.",
        })

    return {"week": date.today().isoformat(), "events": events}


def write(news: dict, path: str) -> None:
    """Write news dict to a JSON file, creating parent dirs as needed."""
    import os
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(news, f, indent=2)
        f.write("\n")


# ------------------------------------------------------------------ internals

def _flightstate(universal: Node | None) -> Node | None:
    if universal is None:
        return None
    game = universal.get_child("GAME")
    return game.get_child("FLIGHTSTATE") if game else None


def _vessel_dict(fs: Node | None) -> dict[str, Node]:
    if fs is None:
        return {}
    return {v.get("persistentId"): v for v in fs.get_children("VESSEL")
            if v.get("persistentId")}


def _body(vessel: Node) -> str:
    orbit = vessel.get_child("ORBIT")
    if orbit:
        return BODY_NAMES.get(orbit.get("REF", "1"), "unknown body")
    # Landed: use landedAt if meaningful
    landed_at = vessel.get("landedAt", "").strip()
    if landed_at and landed_at.lower() not in ("", "ksc", "launchpad", "runway"):
        return landed_at
    return "Kerbin"


def _word(vtype: str) -> str:
    return TYPE_WORD.get(vtype, "vessel")


def _new_vessel_line(agency: str, name: str, vtype: str, sit: str,
                     vessel: Node) -> str | None:
    word = _word(vtype)
    body = _body(vessel)

    if sit == "ORBITING":
        return f"{agency} launches {name} {word} to {body} orbit!"
    if sit in ("LANDED", "SPLASHED"):
        if vtype == "Flag":
            return f"{agency} plants a flag on {body}!"
        verb = "splashes down near" if sit == "SPLASHED" else "touches down on"
        return f"{agency}'s {name} {word} {verb} {body}!"
    if sit in ("FLYING", "SUB_ORBITAL"):
        return f"{agency} launches {name} {word}!"
    return None


def _sit_change_line(agency: str, name: str, vtype: str,
                     old_sit: str, new_sit: str, vessel: Node) -> str | None:
    word = _word(vtype)
    body = _body(vessel)

    if new_sit == "ORBITING" and old_sit != "ORBITING":
        return f"{agency}'s {name} {word} achieves {body} orbit!"
    if new_sit == "LANDED" and old_sit in ("ORBITING", "SUB_ORBITAL", "FLYING"):
        if vtype == "Flag":
            return f"{agency} plants a flag on {body}!"
        return f"{agency}'s {name} {word} lands on {body}!"
    if new_sit == "SPLASHED":
        return f"{agency}'s {name} {word} splashes down near {body}!"
    if new_sit == "ESCAPING":
        return f"{agency}'s {name} {word} breaks free of {body}'s gravity!"
    return None
