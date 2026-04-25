import os
import pytest
from merger.sfs.parser import parse, Node
from merger.sfs.serializer import serialize

FIXTURE_DIR = os.path.join(os.path.dirname(__file__), "fixtures")
REAL_SAVE = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/common"
    "/KSP CLUB/Kerbal Space Program CLUB/saves/default/persistent.sfs"
)


# --- unit tests against inline fixtures ---

SIMPLE = """
GAME
{
\tversion = 1.12.5
\tTitle = Test Save
\tFLIGHTSTATE
\t{
\t\tUT = 805.86
\t\tVESSEL
\t\t{
\t\t\tname = My Rocket
\t\t\ttype = Ship
\t\t}
\t\tVESSEL
\t\t{
\t\t\tname = My Probe
\t\t\ttype = Probe
\t\t}
\t}
}
"""

DUPE_KEYS = """
PART
{
\tMODULE
\t{
\t\tname = ModuleEngines
\t\tFX = exhaust
\t\tFX = glow
\t}
}
"""

EMPTY_VALUE = """
GAME
{
\tlinkURL =
\tcraftFileToLoad =
\tenvInfo =  - Environment Info - Unix 7FFFFFFFFFFFFFFF  Args: KSP  -
}
"""


def test_basic_structure():
    root = parse(SIMPLE)
    assert root.name == "ROOT"
    assert len(root.children) == 1

    game = root.children[0]
    assert game.name == "GAME"
    assert game.get("version") == "1.12.5"
    assert game.get("Title") == "Test Save"

    fs = game.get_child("FLIGHTSTATE")
    assert fs is not None
    assert fs.get("UT") == "805.86"

    vessels = fs.get_children("VESSEL")
    assert len(vessels) == 2
    assert vessels[0].get("name") == "My Rocket"
    assert vessels[1].get("name") == "My Probe"


def test_duplicate_keys():
    root = parse(DUPE_KEYS)
    module = root.children[0].get_child("MODULE")
    assert module.get("name") == "ModuleEngines"
    fx_values = module.get_all("FX")
    assert fx_values == ["exhaust", "glow"]


def test_empty_values():
    root = parse(EMPTY_VALUE)
    game = root.children[0]
    assert game.get("linkURL") == ""
    assert game.get("craftFileToLoad") == ""
    # value containing '=' should not be split on second '='
    env = game.get("envInfo")
    assert "Environment Info" in env
    assert "Unix" in env


def test_node_set_existing():
    root = parse(SIMPLE)
    game = root.children[0]
    game.set("version", "2.0")
    assert game.get("version") == "2.0"
    # Should not have added a duplicate
    assert len([v for k, v in game.values if k == "version"]) == 1


def test_node_set_new():
    root = parse(SIMPLE)
    game = root.children[0]
    game.set("playerID", "wade")
    assert game.get("playerID") == "wade"


def test_node_remove():
    root = parse(DUPE_KEYS)
    module = root.children[0].get_child("MODULE")
    module.remove("FX")
    assert module.get_all("FX") == []


def test_get_children_empty():
    root = parse(SIMPLE)
    game = root.children[0]
    assert game.get_children("NONEXISTENT") == []


def test_get_child_none():
    root = parse(SIMPLE)
    game = root.children[0]
    assert game.get_child("NONEXISTENT") is None


# --- serializer tests ---

def test_serialize_round_trip_simple():
    root = parse(SIMPLE)
    output = serialize(root)
    root2 = parse(output)

    game1 = root.children[0]
    game2 = root2.children[0]
    assert game1.get("version") == game2.get("version")
    assert game1.get("Title") == game2.get("Title")

    fs1 = game1.get_child("FLIGHTSTATE")
    fs2 = game2.get_child("FLIGHTSTATE")
    assert fs1.get("UT") == fs2.get("UT")
    assert len(fs1.get_children("VESSEL")) == len(fs2.get_children("VESSEL"))


def test_serialize_empty_value_preserved():
    root = parse(EMPTY_VALUE)
    output = serialize(root)
    root2 = parse(output)
    game = root2.children[0]
    assert game.get("linkURL") == ""
    assert game.get("craftFileToLoad") == ""


def test_serialize_root_node():
    """Serializing the ROOT wrapper should not emit a ROOT { } block."""
    root = parse(SIMPLE)
    output = serialize(root)
    assert not output.startswith("ROOT")
    assert output.startswith("GAME")


# --- real save file tests (skipped if file not present) ---

@pytest.mark.skipif(not os.path.exists(REAL_SAVE), reason="real save not available")
def test_real_save_parses():
    with open(REAL_SAVE, "r", encoding="utf-8") as f:
        text = f.read()
    root = parse(text)
    game = root.get_child("GAME")
    assert game is not None
    assert game.get("version") != ""
    fs = game.get_child("FLIGHTSTATE")
    assert fs is not None
    ut = float(fs.get("UT"))
    assert ut >= 0


@pytest.mark.skipif(not os.path.exists(REAL_SAVE), reason="real save not available")
def test_real_save_round_trip():
    """Parse → serialize → parse should produce the same structure."""
    with open(REAL_SAVE, "r", encoding="utf-8") as f:
        text = f.read()

    root1 = parse(text)
    output = serialize(root1)
    root2 = parse(output)

    game1 = root1.get_child("GAME")
    game2 = root2.get_child("GAME")

    assert game1.get("version") == game2.get("version")
    assert game1.get("Title") == game2.get("Title")

    fs1 = game1.get_child("FLIGHTSTATE")
    fs2 = game2.get_child("FLIGHTSTATE")
    assert fs1.get("UT") == fs2.get("UT")

    vessels1 = fs1.get_children("VESSEL")
    vessels2 = fs2.get_children("VESSEL")
    assert len(vessels1) == len(vessels2)

    roster1 = game1.get_child("ROSTER")
    roster2 = game2.get_child("ROSTER")
    kerbals1 = roster1.get_children("KERBAL") if roster1 else []
    kerbals2 = roster2.get_children("KERBAL") if roster2 else []
    assert len(kerbals1) == len(kerbals2)
    if kerbals1:
        assert kerbals1[0].get("name") == kerbals2[0].get("name")
