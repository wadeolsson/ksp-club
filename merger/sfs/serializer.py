"""
Serializer for KSP's .sfs save file format.
Converts a Node tree back to the text format KSP expects.
"""

from .parser import Node


def serialize(node: Node) -> str:
    """
    Serialize a Node tree to an .sfs string.

    If node is the synthetic ROOT returned by parse(), its children are
    serialized at the top level (no wrapper block). Otherwise the node
    itself is serialized as a top-level block.
    """
    lines: list[str] = []

    if node.name == "ROOT":
        for child in node.children:
            _write_node(child, lines, depth=0)
    else:
        _write_node(node, lines, depth=0)

    return "\n".join(lines) + "\n"


def _write_node(node: Node, lines: list[str], depth: int) -> None:
    indent = "\t" * depth
    lines.append(f"{indent}{node.name}")
    lines.append(f"{indent}{{")

    for key, value in node.values:
        lines.append(f"{indent}\t{key} = {value}")

    for child in node.children:
        _write_node(child, lines, depth + 1)

    lines.append(f"{indent}}}")
