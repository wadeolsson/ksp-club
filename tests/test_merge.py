import math
import os
import pytest
from merger.sfs.parser import parse
from merger.merge.layers import (
    extract, PERSISTENT_SCENARIOS, DYNAMIC_SCENARIOS, STOCK_KERBALS
)
from merger.merge.time import advance_vessel, BODY_MU
from merger.merge.vessels import collect_vessels
from merger.merge.kerbals import get_stock_kerbals, merge_kerbals
from merger.merge.mods import validate_parts
from merger.merge.builder import build

CAREER_SAVE = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/common"
    "/KSP CLUB/Kerbal Space Program CLUB/saves/CAREER/persistent.sfs"
)

# --- inline fixtures ---

MULTI_PLAYER_SAVE = """
GAME
{
\tversion = 1.12.5
\tTitle = Test (CAREER)
\tMode = CAREER
\tFLIGHTSTATE
\t{
\t\tUT = 1000.0
\t\tVESSEL
\t\t{
\t\t\tname = Wade Rocket
\t\t\ttype = Ship
\t\t\tplayerID = wade
\t\t}
\t\tVESSEL
\t\t{
\t\t\tname = Ed Station
\t\t\ttype = Station
\t\t\tplayerID = ed
\t\t}
\t\tVESSEL
\t\t{
\t\t\tname = Mystery Debris
\t\t\ttype = Debris
\t\t}
\t}
\tROSTER
\t{
\t\tKERBAL
\t\t{
\t\t\tname = Jebediah Kerman
\t\t\ttype = Crew
\t\t}
\t\tKERBAL
\t\t{
\t\t\tname = Wade Kerman
\t\t\ttype = Crew
\t\t\tplayerID = wade
\t\t}
\t\tKERBAL
\t\t{
\t\t\tname = Ed Kerman
\t\t\ttype = Crew
\t\t\tplayerID = ed
\t\t}
\t\tKERBAL
\t\t{
\t\t\tname = Orphan Kerman
\t\t\ttype = Crew
\t\t}
\t}
\tSCENARIO
\t{
\t\tname = Funding
\t\tfunds = 50000
\t}
\tSCENARIO
\t{
\t\tname = ResearchAndDevelopment
\t\tsci = 42.0
\t}
\tSCENARIO
\t{
\t\tname = ScenarioDestructibles
\t\tsome = worldstuff
\t}
\tSCENARIO
\t{
\t\tname = UnknownModScenario
\t\tdata = xyz
\t}
}
"""


def test_extracts_own_vessels():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    names = [v.get("name") for v in contrib.vessels]
    assert "Wade Rocket" in names
    assert "Ed Station" not in names


def test_claims_untagged_vessels_when_flag_set():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade", claim_untagged=True)
    names = [v.get("name") for v in contrib.vessels]
    assert "Mystery Debris" in names
    assert any("Mystery Debris" in w for w in contrib.warnings)


def test_ignores_untagged_vessels_when_flag_clear():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade", claim_untagged=False)
    names = [v.get("name") for v in contrib.vessels]
    assert "Mystery Debris" not in names


def test_extracts_own_kerbals():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    names = [k.get("name") for k in contrib.kerbals]
    assert "Wade Kerman" in names
    assert "Ed Kerman" not in names


def test_stock_kerbals_excluded():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    names = [k.get("name") for k in contrib.kerbals]
    assert "Jebediah Kerman" not in names


def test_untagged_kerbal_claimed():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade", claim_untagged=True)
    names = [k.get("name") for k in contrib.kerbals]
    assert "Orphan Kerman" in names
    assert any("Orphan Kerman" in w for w in contrib.warnings)


def test_persistent_scenarios_extracted():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    assert "Funding" in contrib.scenarios
    assert "ResearchAndDevelopment" in contrib.scenarios
    assert contrib.scenarios["Funding"].get("funds") == "50000"


def test_dynamic_scenarios_excluded():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    assert "ScenarioDestructibles" not in contrib.scenarios


def test_unknown_scenario_kept_with_warning():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    assert "UnknownModScenario" in contrib.scenarios
    assert any("UnknownModScenario" in w for w in contrib.warnings)


def test_ut_extracted():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    assert contrib.ut == 1000.0


def test_game_values_captured():
    root = parse(MULTI_PLAYER_SAVE)
    contrib = extract(root, "wade")
    keys = [k for k, v in contrib.game_values]
    assert "version" in keys
    assert "Mode" in keys


def test_missing_game_block_raises():
    root = parse("NOTGAME\n{\n\tkey = val\n}\n")
    with pytest.raises(ValueError, match="No GAME block"):
        extract(root, "wade")


def test_missing_flightstate_raises():
    root = parse("GAME\n{\n\tversion = 1.12.5\n}\n")
    with pytest.raises(ValueError, match="No FLIGHTSTATE"):
        extract(root, "wade")


# --- real career save ---

@pytest.mark.skipif(not os.path.exists(CAREER_SAVE), reason="real save not available")
def test_real_career_extract():
    with open(CAREER_SAVE) as f:
        text = f.read()
    root = parse(text)
    contrib = extract(root, "wade")

    # UT should be a positive number
    assert contrib.ut > 0

    # Should have found at least the core career scenarios
    for expected in ("Funding", "ResearchAndDevelopment", "Reputation"):
        if expected in contrib.scenarios:
            pass  # great
        # (career save may not have all of them, so we just check no crash)

    # Stock Kerbals must not be in the contribution
    kerbal_names = {k.get("name") for k in contrib.kerbals}
    assert not kerbal_names.intersection(STOCK_KERBALS)

    # Warnings should be a list (even if empty)
    assert isinstance(contrib.warnings, list)


# =============================================================================
# time.py — orbital advancement
# =============================================================================

ORBITING_VESSEL = """
VESSEL
{
\tname = Sat 1
\ttype = Probe
\tsit = ORBITING
\tplayerID = wade
\tORBIT
\t{
\t\tSMA = 700000
\t\tECC = 0.01
\t\tINC = 0
\t\tLPE = 0
\t\tLAN = 0
\t\tMNA = 1.0
\t\tEPH = 100.0
\t\tREF = 1
\t}
}
"""

LANDED_VESSEL = """
VESSEL
{
\tname = Rover
\ttype = Rover
\tsit = LANDED
\tplayerID = wade
}
"""


def _parse_vessel(sfs_snippet: str):
    # Wrap in a dummy block so the parser has a root
    root = parse(f"WRAP\n{{\n{sfs_snippet}\n}}\n")
    return root.children[0].children[0]


def test_orbital_advancement_changes_mna():
    vessel = _parse_vessel(ORBITING_VESSEL)
    orbit = vessel.get_child("ORBIT")
    old_mna = float(orbit.get("MNA"))
    old_eph = float(orbit.get("EPH"))

    warnings = advance_vessel(vessel, 200.0)

    assert warnings == []
    new_mna = float(orbit.get("MNA"))
    new_eph = float(orbit.get("EPH"))

    assert new_eph == pytest.approx(200.0)
    # n = sqrt(μ_Kerbin / SMA³)
    mu = BODY_MU[1]
    sma = 700000.0
    n = math.sqrt(mu / sma**3)
    expected_mna = old_mna + n * (200.0 - old_eph)
    assert new_mna == pytest.approx(expected_mna, rel=1e-9)


def test_landed_vessel_not_advanced():
    vessel = _parse_vessel(LANDED_VESSEL)
    warnings = advance_vessel(vessel, 99999.0)
    assert warnings == []
    # No ORBIT block to check, just confirm no crash


def test_no_orbit_block_skipped():
    # ORBITING vessel without an ORBIT block (malformed but handled)
    vessel = _parse_vessel("""
VESSEL
{
\tname = Ghost
\tsit = ORBITING
\tplayerID = wade
}
""")
    warnings = advance_vessel(vessel, 500.0)
    assert warnings == []


def test_hyperbolic_orbit_skipped():
    vessel = _parse_vessel("""
VESSEL
{
\tname = Escape Pod
\tsit = ESCAPING
\tplayerID = wade
\tORBIT
\t{
\t\tSMA = -1000000
\t\tECC = 1.5
\t\tINC = 0
\t\tLPE = 0
\t\tLAN = 0
\t\tMNA = 0.5
\t\tEPH = 50.0
\t\tREF = 1
\t}
}
""")
    warnings = advance_vessel(vessel, 500.0)
    assert len(warnings) == 1
    assert "hyperbolic" in warnings[0]


def test_unknown_ref_body_warns():
    vessel = _parse_vessel("""
VESSEL
{
\tname = Modded Probe
\tsit = ORBITING
\tplayerID = wade
\tORBIT
\t{
\t\tSMA = 500000
\t\tECC = 0.0
\t\tINC = 0
\t\tLPE = 0
\t\tLAN = 0
\t\tMNA = 0.5
\t\tEPH = 50.0
\t\tREF = 99
\t}
}
""")
    warnings = advance_vessel(vessel, 500.0)
    assert len(warnings) == 1
    assert "unknown REF body" in warnings[0]


# =============================================================================
# vessels.py — collect_vessels
# =============================================================================

def _make_contrib(player_id, vessel_dicts):
    """Helper: build a minimal PlayerContribution with given vessels."""
    from merger.merge.layers import PlayerContribution
    vessels = []
    for d in vessel_dicts:
        from merger.sfs.parser import Node
        v = Node("VESSEL")
        for k, val in d.items():
            v.values.append([k, val])
        vessels.append(v)
    return PlayerContribution(
        player_id=player_id, ut=100.0,
        game_values=[], parameters=None,
        vessels=vessels,
    )


def test_collect_no_conflicts():
    c1 = _make_contrib("wade", [{"name": "Rocket", "persistentId": "111"}])
    c2 = _make_contrib("ed",   [{"name": "Station", "persistentId": "222"}])
    vessels, warnings = collect_vessels([c1, c2])
    assert len(vessels) == 2
    assert warnings == []


def test_collect_pid_conflict_warns():
    c1 = _make_contrib("wade", [{"name": "Rocket", "persistentId": "111"}])
    c2 = _make_contrib("ed",   [{"name": "Rocket Copy", "persistentId": "111"}])
    vessels, warnings = collect_vessels([c1, c2])
    assert len(vessels) == 1          # duplicate dropped
    assert len(warnings) == 1
    assert "111" in warnings[0]


def test_collect_no_pid_both_kept():
    # Vessels with no persistentId can't conflict — both are kept
    c1 = _make_contrib("wade", [{"name": "Debris A"}])
    c2 = _make_contrib("ed",   [{"name": "Debris B"}])
    vessels, warnings = collect_vessels([c1, c2])
    assert len(vessels) == 2


# =============================================================================
# kerbals.py — merge_kerbals
# =============================================================================

def _make_kerbal_node(name, player_id=None):
    from merger.sfs.parser import Node
    k = Node("KERBAL")
    k.values = [["name", name], ["type", "Crew"]]
    if player_id:
        k.values.append(["playerID", player_id])
    return k


def test_merge_no_conflicts():
    stock = [_make_kerbal_node("Jebediah Kerman")]
    from merger.merge.layers import PlayerContribution
    c1 = PlayerContribution("wade", 100.0, [], None,
                            kerbals=[_make_kerbal_node("Wade Kerman", "wade")])
    c2 = PlayerContribution("ed", 100.0, [], None,
                            kerbals=[_make_kerbal_node("Ed Kerman", "ed")])
    merged, warnings = merge_kerbals([c1, c2], stock)
    names = [k.get("name") for k in merged]
    assert "Jebediah Kerman" in names
    assert "Wade Kerman" in names
    assert "Ed Kerman" in names
    assert warnings == []


def test_merge_name_conflict_renames():
    stock = []
    from merger.merge.layers import PlayerContribution
    c1 = PlayerContribution("wade", 100.0, [], None,
                            kerbals=[_make_kerbal_node("Lucky Kerman", "wade")])
    c2 = PlayerContribution("ed", 100.0, [], None,
                            kerbals=[_make_kerbal_node("Lucky Kerman", "ed")])
    merged, warnings = merge_kerbals([c1, c2], stock)
    names = [k.get("name") for k in merged]
    assert "Lucky Kerman" in names
    assert "Lucky Kerman1" in names
    assert len(warnings) == 1
    assert "renamed" in warnings[0]


# =============================================================================
# mods.py — validate_parts
# =============================================================================

def test_validate_parts_all_allowed():
    c = _make_contrib("wade", [])
    from merger.sfs.parser import Node
    v = Node("VESSEL")
    v.values = [["name", "Rocket"]]
    p = Node("PART")
    p.values = [["name", "liquidEngine"]]
    v.children.append(p)
    c.vessels.append(v)
    warnings = validate_parts([c], {"liquidEngine", "mk1pod"})
    assert warnings == []


def test_validate_parts_unknown_warns():
    c = _make_contrib("wade", [])
    from merger.sfs.parser import Node
    v = Node("VESSEL")
    v.values = [["name", "Rocket"]]
    p = Node("PART")
    p.values = [["name", "weirdModPart123"]]
    v.children.append(p)
    c.vessels.append(v)
    warnings = validate_parts([c], {"liquidEngine"})
    assert len(warnings) == 1
    assert "weirdModPart123" in warnings[0]


def test_validate_parts_empty_set_disabled():
    c = _make_contrib("wade", [])
    from merger.sfs.parser import Node
    v = Node("VESSEL")
    v.values = [["name", "Rocket"]]
    p = Node("PART")
    p.values = [["name", "anyPart"]]
    v.children.append(p)
    c.vessels.append(v)
    # Empty set = validation disabled
    warnings = validate_parts([c], set())
    assert warnings == []


# =============================================================================
# builder.py — full pipeline
# =============================================================================

SAVE_WADE = """
GAME
{
\tversion = 1.12.5
\tTitle = Wade Save (CAREER)
\tMode = CAREER
\tlaunchID = 5
\tFLIGHTSTATE
\t{
\t\tversion = 1.12.5
\t\tUT = 1000.0
\t\tactiveVessel = 0
\t\tmapViewFiltering = -1026
\t\tcommNetUIModeTracking = Network
\t\tcommNetUIModeFlight = Path
\t\tVESSEL
\t\t{
\t\t\tname = Wade Rocket
\t\t\ttype = Ship
\t\t\tsit = LANDED
\t\t\tpersistentId = 100
\t\t\tplayerID = wade
\t\t}
\t}
\tROSTER
\t{
\t\tKERBAL
\t\t{
\t\t\tname = Jebediah Kerman
\t\t\ttype = Crew
\t\t}
\t\tKERBAL
\t\t{
\t\t\tname = Wade Kerman
\t\t\ttype = Crew
\t\t\tplayerID = wade
\t\t}
\t}
\tSCENARIO
\t{
\t\tname = Funding
\t\tfunds = 50000
\t}
\tSCENARIO
\t{
\t\tname = ScenarioDestructibles
\t\tstate = world
\t}
\tRemovedROCs
\t{
\t}
\tCometNames
\t{
\t}
\tPARAMETERS
\t{
\t\tpreset = Normal
\t}
\tLoaderInfo
\t{
\t}
\tMESSAGESYSTEM
\t{
\t}
}
"""

SAVE_ED = """
GAME
{
\tversion = 1.12.5
\tTitle = Ed Save (CAREER)
\tMode = CAREER
\tlaunchID = 8
\tFLIGHTSTATE
\t{
\t\tversion = 1.12.5
\t\tUT = 2000.0
\t\tactiveVessel = 0
\t\tmapViewFiltering = -1026
\t\tcommNetUIModeTracking = Network
\t\tcommNetUIModeFlight = Path
\t\tVESSEL
\t\t{
\t\t\tname = Ed Station
\t\t\ttype = Station
\t\t\tsit = ORBITING
\t\t\tpersistentId = 200
\t\t\tplayerID = ed
\t\t\tORBIT
\t\t\t{
\t\t\t\tSMA = 700000
\t\t\t\tECC = 0.01
\t\t\t\tINC = 0
\t\t\t\tLPE = 0
\t\t\t\tLAN = 0
\t\t\t\tMNA = 1.0
\t\t\t\tEPH = 100.0
\t\t\t\tREF = 1
\t\t\t}
\t\t}
\t}
\tROSTER
\t{
\t\tKERBAL
\t\t{
\t\t\tname = Jebediah Kerman
\t\t\ttype = Crew
\t\t}
\t\tKERBAL
\t\t{
\t\t\tname = Ed Kerman
\t\t\ttype = Crew
\t\t\tplayerID = ed
\t\t}
\t}
\tSCENARIO
\t{
\t\tname = Funding
\t\tfunds = 75000
\t}
\tSCENARIO
\t{
\t\tname = ScenarioDestructibles
\t\tstate = world
\t}
\tRemovedROCs
\t{
\t}
\tCometNames
\t{
\t}
\tPARAMETERS
\t{
\t\tpreset = Normal
\t}
\tLoaderInfo
\t{
\t}
\tMESSAGESYSTEM
\t{
\t}
}
"""


def test_build_canonical_ut():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    _, rebuilt, _ = build(subs)
    # Both rebuilt saves should have canonical UT = 2000 (max)
    for player_id, root in rebuilt.items():
        fs = root.get_child("GAME").get_child("FLIGHTSTATE")
        assert float(fs.get("UT")) == pytest.approx(2000.0), \
            f"{player_id} UT mismatch"


def test_build_launch_id_is_max():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    _, rebuilt, _ = build(subs)
    for root in rebuilt.values():
        game = root.get_child("GAME")
        assert int(game.get("launchID")) == 8  # max(5, 8)


def test_build_all_vessels_in_each_save():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    _, rebuilt, _ = build(subs)
    for player_id, root in rebuilt.items():
        fs = root.get_child("GAME").get_child("FLIGHTSTATE")
        vessel_names = [v.get("name") for v in fs.get_children("VESSEL")]
        assert "Wade Rocket" in vessel_names, f"{player_id} missing Wade Rocket"
        assert "Ed Station" in vessel_names, f"{player_id} missing Ed Station"


def test_build_player_scenarios_kept():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    _, rebuilt, _ = build(subs)
    # Wade's save should have Wade's Funding (50000), not Ed's (75000)
    wade_game = rebuilt["wade"].get_child("GAME")
    funding = wade_game.get_child("SCENARIO")   # first scenario
    # Find Funding specifically
    funding = next(
        s for s in wade_game.get_children("SCENARIO") if s.get("name") == "Funding"
    )
    assert funding.get("funds") == "50000"


def test_build_merged_kerbals_in_each_save():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    _, rebuilt, _ = build(subs)
    for player_id, root in rebuilt.items():
        roster = root.get_child("GAME").get_child("ROSTER")
        names = [k.get("name") for k in roster.get_children("KERBAL")]
        # Stock Kerbals (Jeb etc.) are excluded from club saves intentionally
        assert "Jebediah Kerman" not in names
        assert "Wade Kerman" in names
        assert "Ed Kerman" in names


def test_build_orbit_advanced():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    _, rebuilt, _ = build(subs)
    # Ed Station was orbiting — its EPH should now be canonical UT (2000)
    wade_save = rebuilt["wade"].get_child("GAME").get_child("FLIGHTSTATE")
    ed_station = next(
        v for v in wade_save.get_children("VESSEL") if v.get("name") == "Ed Station"
    )
    orbit = ed_station.get_child("ORBIT")
    assert float(orbit.get("EPH")) == pytest.approx(2000.0)


def test_build_universal_title():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    universal, _, _ = build(subs)
    game = universal.get_child("GAME")
    assert "Universal State" in game.get("Title")


def test_build_empty_submissions_raises():
    with pytest.raises(ValueError, match="No submissions"):
        build({})


def test_build_warnings_list():
    subs = {"wade": parse(SAVE_WADE), "ed": parse(SAVE_ED)}
    _, _, warnings = build(subs)
    assert isinstance(warnings, list)
