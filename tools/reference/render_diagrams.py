#!/usr/bin/env python3
"""Render the editable teaching diagrams using only the Python standard library."""

from html import escape
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs/diagrams/design"
CARD_X = (24, 312, 600)
CARD_WIDTH = 206


def render(scene):
    parts = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="836" height="252" viewBox="0 0 836 252" role="img" aria-labelledby="title desc">',
        f'<title id="title">{escape(scene["title"])}</title><desc id="desc">{escape(scene["claim"])}</desc>',
        '<style>text{font-family:system-ui,sans-serif;fill:#163c36}.bg{fill:#f8faf7}.card{stroke:#426b61;stroke-width:1.5}.edge{stroke:#275a50;stroke-width:2;fill:none}.arrow{fill:#275a50}.head{font-size:26px;font-weight:700}.name{font-size:18px;font-weight:700}.body{font-size:13px}.link{font-size:12px;font-weight:600}.a{fill:#d9ebdf}.b{fill:#f4e4c9}.c{fill:#dce9f1}@media(prefers-color-scheme:dark){text{fill:#eef6f2}.bg{fill:#16312e}.a{fill:#2d5146}.b{fill:#534735}.c{fill:#334957}.card{stroke:#95b8aa}.edge{stroke:#c1d9c7}.arrow{fill:#c1d9c7}}</style>',
        '<defs><marker id="arrowhead" markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto"><path class="arrow" d="M0 0 L7 3.5 L0 7Z"/></marker></defs>',
        '<rect class="bg" width="836" height="252" rx="14"/>',
        f'<text class="head" x="24" y="44">{escape(scene["title"])}</text>',
        f'<text class="body" x="24" y="74">{escape(scene["claim"])}</text>',
    ]
    positions = {}
    for index, (key, name, action, data) in enumerate(scene["nodes"]):
        x = CARD_X[index]
        positions[key] = x
        parts.extend([
            f'<g id="{key}"><rect class="card {"abc"[index]}" x="{x}" y="109" width="{CARD_WIDTH}" height="111" rx="9"/>',
            f'<text class="name" x="{x + 12}" y="142">{escape(name)}</text>',
            f'<text class="body" x="{x + 12}" y="174">{escape(action)}</text>',
            f'<text class="body" x="{x + 12}" y="197">{escape(data)}</text></g>',
        ])
    for key, source, dest, label, kind in scene["edges"]:
        sx, dx = positions[source], positions[dest]
        start, end = (sx + CARD_WIDTH + 4, dx - 5) if sx < dx else (sx - 4, dx + CARD_WIDTH + 5)
        dashed = ' stroke-dasharray="5 4"' if kind == "message" else ""
        parts.extend([
            f'<g id="{key}"><path class="edge" d="M{start} 169 H{end}" marker-end="url(#arrowhead)"{dashed}/>',
            f'<text class="link" text-anchor="middle" x="{(start + end) / 2}" y="154">{escape(label)}</text></g>',
        ])
    return "\n".join(parts + ["</svg>", ""])


if __name__ == "__main__":
    for design in sorted(SOURCE.glob("*.json")):
        svg = design.parent.parent / (design.stem + ".svg")
        result = render(json.loads(design.read_text()))
        if "--check" in sys.argv:
            if not svg.exists() or svg.read_text() != result:
                raise SystemExit(f"Rebuild {svg.name} with {Path(__file__).name}")
        else:
            svg.write_text(result)
