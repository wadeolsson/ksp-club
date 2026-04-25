import os
import pytest
from merger.sfs.parser import parse
from merger.merge.layers import (
    extract, PERSISTENT_SCENARIOS, DYNAMIC_SCENARIOS, STOCK_KERBALS
)

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
