"""
Parser for KSP's .sfs save file format.

The format is a simple nested block structure:

    BLOCK_NAME
    {
        key = value
        NESTED_BLOCK
        {
            key = value
        }
    }

Rules observed from real save files:
- Block name is always on its own line; opening { is always the next non-blank line
- key = value uses the FIRST '=' as the delimiter; values may contain '='
- Values may be empty:  key =
- Duplicate keys within a block are allowed (KSP uses them for part lists etc.)
- Multiple sibling blocks with the same name are allowed (VESSEL, KERBAL, PART, ...)
- Indentation is tabs; we strip it during parsing and restore it during serialization
"""


from __future__ import annotations


class Node:
    """One block in an .sfs file, e.g. VESSEL { ... }"""

    __slots__ = ("name", "values", "children")

    def __init__(self, name: str):
        self.name = name
        self.values: list[list[str]] = []    # [[key, value], ...]
        self.children: list["Node"] = []

    # --- value helpers ---

    def get(self, key: str, default: str = "") -> str:
        """Return the first value for key, or default."""
        for k, v in self.values:
            if k == key:
                return v
        return default

    def get_all(self, key: str) -> list[str]:
        """Return all values for key (handles duplicate keys)."""
        return [v for k, v in self.values if k == key]

    def set(self, key: str, value: str) -> None:
        """Set the first occurrence of key; append if not present."""
        for pair in self.values:
            if pair[0] == key:
                pair[1] = value
                return
        self.values.append([key, value])

    def remove(self, key: str) -> None:
        """Remove all values with this key."""
        self.values = [[k, v] for k, v in self.values if k != key]

    # --- child helpers ---

    def get_children(self, name: str) -> list["Node"]:
        """Return all direct children with this block name."""
        return [c for c in self.children if c.name == name]

    def get_child(self, name: str) -> "Node | None":
        """Return the first child with this block name, or None."""
        for c in self.children:
            if c.name == name:
                return c
        return None

    def __repr__(self) -> str:
        return f"Node({self.name!r}, values={len(self.values)}, children={len(self.children)})"


def parse(text: str) -> Node:
    """
    Parse an .sfs file string into a Node tree.
    Returns a synthetic root Node whose single child is the top-level block (e.g. GAME).
    """
    root = Node("ROOT")
    stack: list[Node] = [root]
    pending_name: str | None = None  # block name seen, waiting for '{'

    for raw_line in text.splitlines():
        line = raw_line.strip()

        # blank lines and comments
        if not line or line.startswith("//"):
            continue

        if line == "{":
            if pending_name is None:
                # Malformed file — orphan '{'; skip
                continue
            child = Node(pending_name)
            stack[-1].children.append(child)
            stack.append(child)
            pending_name = None
            continue

        if line == "}":
            if len(stack) > 1:
                stack.pop()
            pending_name = None
            continue

        if "=" in line:
            key, _, value = line.partition("=")
            stack[-1].values.append([key.strip(), value.strip()])
            pending_name = None
            continue

        # No '=' and not a brace → this is a block name; expect '{' next
        pending_name = line

    return root
