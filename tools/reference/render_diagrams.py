#!/usr/bin/env python3
"""Render editable scene descriptions to responsive and fixed-theme SVGs."""
import argparse
from html import escape
import json
from pathlib import Path
import textwrap

ROOT = Path(__file__).resolve().parents[2]
DIAGRAMS = ROOT / "docs/diagrams"
COLORS = {
    "light": ("#f8faf6", "#153c37", "#57736b", "#ffffff", "#d8eade", "#fae7cd", "#ddecf4", "#416b60"),
    "dark": ("#172c2a", "#f0f7f2", "#b4cbc1", "#1e3531", "#2c5144", "#534331", "#314957", "#b5d7c8"),
}


def palette(theme):
    bg, ink, muted, panel, first, second, third, line = COLORS[theme]
    return (f".bg{{fill:{bg}}}text{{fill:{ink}}}.muted{{fill:{muted}}}.frame{{fill:{panel};stroke:{line}}}"
            f".a{{fill:{first}}}.b{{fill:{second}}}.c{{fill:{third}}}.card,.conn{{stroke:{line}}}.arrow{{fill:{line}}}")


def text(cls, x, y, value, anchor="start"):
    return f'<text class="{cls}" text-anchor="{anchor}" x="{x}" y="{y}">{escape(value)}</text>'


def box(cls, rect, radius):
    x, y, w, h = rect
    return f'<rect class="{cls}" x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}"/>'


def edge_markup(edge, boxes, compact):
    key, first, second, label, kind = edge
    ax, ay, aw, ah = boxes[first]
    bx, by, bw, bh = boxes[second]
    if compact:
        sy, ey = (ay + ah + 3, by - 5) if ay < by else (ay - 3, by + bh + 5)
        x = ax + aw // 2
        path, label_x, label_y, anchor = f"M{x} {sy} V{ey}", x + 14, (sy + ey) // 2 + 4, "start"
    else:
        sx, ex = (ax + aw + 3, bx - 5) if ax < bx else (ax - 3, bx + bw + 5)
        y = ay + ah // 2
        path, label_x, label_y, anchor = f"M{sx} {y} H{ex}", (sx + ex) // 2, y - 11, "middle"
    dash = ' stroke-dasharray="6 4"' if kind == "message" else ""
    return [f'<g id="{key}"><path d="{path}" class="conn" marker-end="url(#arrowhead)"{dash}/>',
            text("edge-label", label_x, label_y, label, anchor), "</g>"]


def render(scene, layout, compact=False, theme="adaptive"):
    w, h = layout["size"]
    styles = palette("light" if theme == "adaptive" else theme)
    if theme == "adaptive":
        styles += "@media(prefers-color-scheme:dark){" + palette("dark") + "}"
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" role="img" aria-labelledby="title desc">',
        f'<title id="title">{escape(scene["title"])}</title><desc id="desc">{escape(scene["alternative"])}</desc>',
        '<style>text{font-family:Barlow,system-ui,sans-serif}.title{font-size:' + ('23' if compact else '32') +
        'px;font-weight:700}.name{font-size:20px;font-weight:700}.body{font-size:15px}.cap,.edge-label{font-size:14px;font-weight:700}.conn{fill:none;stroke-width:2}.card,.frame{stroke-width:1.3}' + styles + '</style>',
        '<defs><marker id="arrowhead" markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto"><path class="arrow" d="M0 0 L7 3.5 L0 7Z"/></marker></defs>',
        box("bg", (0, 0, w, h), 12),
        text("title", 22 if compact else 36, 40 if compact else 48, scene["title"]),
    ]
    for i, line in enumerate(textwrap.wrap(scene["claim"], 44 if compact else 110)):
        parts.append(text("body muted", 22 if compact else 36, 69 + i * 19 if compact else 80, line))
    frame = layout["transaction"] if scene["id"] == "reserve-sequence" else layout["frame"]
    parts.extend([box("frame", frame, 12), text("cap", frame[0] + 16, frame[1] + 23, scene["frame"])])
    boxes = {}
    for i, (key, title, action, data) in enumerate(scene["nodes"]):
        rect = layout["cards"][i]
        boxes[key] = rect
        x, y, width, height = rect
        parts.extend([f'<g id="{key}">', box("card " + "abc"[i], rect, 8), text("name", x + 13, y + 29, title),
                      text("body", x + 13, y + 55, action), text("body muted", x + 13, y + 77, data), "</g>"])
    for edge in scene["edges"]:
        parts.extend(edge_markup(edge, boxes, compact))
    x, y, width, height = layout["footer"]
    parts.extend([box("frame", layout["footer"], 10), text("cap", x + 16, y + 26, scene["footer"])])
    if "schemas" in scene:
        for i, schema in enumerate(scene["schemas"]):
            px = x + 16 + i * (102 if compact else 341)
            parts.append(text("body", px, y + 55, schema))
        if compact:
            parts.extend([text("body muted", x + 16, y + 84, "Catalog, Lending and PickupDesk each"), text("body muted", x + 16, y + 103, "connect with their own runtime role.")])
        else:
            for nx, ny, nw, nh in layout["cards"]:
                px = nx + nw // 2
                parts.append(f'<path class="conn" d="M{px} {ny+nh+2} V{y-2}"/>')
    else:
        for i, line in enumerate(textwrap.wrap(scene["detail"], 40 if compact else 120)):
            parts.append(text("body muted", x + 16, y + 52 + i * 19, line))
    return "\n".join(parts + ["</svg>", ""])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    options = parser.parse_args()
    layouts = json.loads((DIAGRAMS / "layout.json").read_text())
    for path in sorted((DIAGRAMS / "design").glob("*.json")):
        scene = json.loads(path.read_text())
        exports = DIAGRAMS / "exports" / path.stem
        variants = [(DIAGRAMS / (path.stem + ".svg"), "wide", "adaptive")]
        for layout, prefix in (("wide", ""), ("compact", "mobile-")):
            for theme in COLORS:
                variants.append((exports / (prefix + theme + ".svg"), layout, theme))
        for output, layout, theme in variants:
            svg = render(scene, layouts[layout], layout == "compact", theme)
            if options.check:
                if not output.exists() or output.read_text() != svg:
                    raise SystemExit(f"Rebuild {output.relative_to(ROOT)} with {Path(__file__).name}")
            else:
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_text(svg)


if __name__ == "__main__":
    main()
