"""
Orbital time advancement for KSP vessels.

When merging saves from players who may be at different game times, all vessel
orbits must be advanced to the canonical UT (max UT across all submissions)
so the universe is internally consistent.

Uses Kepler propagation: mean anomaly advances linearly at rate n = sqrt(μ/a³).
Works for elliptical orbits (ECC < 1). Hyperbolic trajectories are skipped.
Landed/splashed vessels have no orbit to advance.
"""

from __future__ import annotations

import math
from merger.sfs.parser import Node

# Standard gravitational parameters (μ = GM) for KSP 1.12.5 stock bodies, m³/s²
# Key is the REF index stored in the ORBIT block.
BODY_MU: dict[int, float] = {
    0:  1.1723328e18,   # Kerbol (Sun)
    1:  3.5316000e12,   # Kerbin
    2:  6.5138398e10,   # Mun
    3:  1.7658000e9,    # Minmus
    4:  1.6860938e11,   # Moho
    5:  8.1717302e12,   # Eve
    6:  3.0136321e11,   # Duna
    7:  1.8568369e10,   # Ike
    8:  2.8252800e14,   # Jool
    9:  1.9620000e12,   # Laythe
    10: 2.0748150e11,   # Vall
    11: 2.4868349e9,    # Bop
    12: 2.8252800e12,   # Tylo
    13: 8.2894498e6,    # Gilly
    14: 7.2170208e8,    # Pol
    15: 2.1484489e10,   # Dres
    16: 7.4410814e10,   # Eeloo
}

# Vessel situations that have a live orbit to advance
_ORBITAL_SITS = {"ORBITING", "ESCAPING", "SUB_ORBITAL"}


def advance_vessel(vessel: Node, canonical_ut: float) -> list[str]:
    """
    Advance a vessel's orbital state to canonical_ut in-place.
    Returns a list of warning strings (empty if all went fine).
    """
    sit = vessel.get("sit", "")
    if sit not in _ORBITAL_SITS:
        return []  # landed/splashed/prelaunch — nothing to do

    orbit = vessel.get_child("ORBIT")
    if orbit is None:
        return []

    warnings = []
    try:
        sma = float(orbit.get("SMA", "0"))
        ecc = float(orbit.get("ECC", "0"))
        mna = float(orbit.get("MNA", "0"))
        eph = float(orbit.get("EPH", "0"))
        ref = int(float(orbit.get("REF", "0")))
    except ValueError:
        name = vessel.get("name", "?")
        return [f"Vessel '{name}': could not parse orbit values — skipping advancement"]

    if abs(sma) < 1:
        return []  # degenerate

    if ecc >= 1.0:
        # Hyperbolic/parabolic escape trajectory — skip, these are transient
        name = vessel.get("name", "?")
        return [f"Vessel '{name}': hyperbolic orbit (ECC={ecc:.3f}) — orbit not advanced"]

    mu = BODY_MU.get(ref)
    if mu is None:
        name = vessel.get("name", "?")
        warnings.append(
            f"Vessel '{name}': unknown REF body {ref} (modded system?) — orbit not advanced"
        )
        return warnings

    # Mean motion (rad/s) and propagated mean anomaly
    n = math.sqrt(mu / (sma ** 3))
    new_mna = mna + n * (canonical_ut - eph)

    orbit.set("MNA", repr(new_mna))
    orbit.set("EPH", repr(canonical_ut))
    return warnings
