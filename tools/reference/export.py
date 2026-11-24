#!/usr/bin/env python3
"""Export a reproducible walkthrough bundle from a committed Git tree."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[2]
ARCHIVE = "equipment-library-reference.zip"


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args])


def safe_path(path):
    parts = path.split("/")
    if any(part in ("", ".", "..") for part in parts) or "\\" in path or ":" in path:
        raise ValueError(f"Invalid relative path: {path}")
    return PurePosixPath(path).as_posix()


def indexed(records):
    result = {}
    for entry in records:
        key = entry["id"]
        if not re.fullmatch("[a-z][a-z0-9-]*", key) or key in result:
            raise ValueError(f"Invalid or duplicate ID: {key}")
        result[key] = entry
    return result


def check_links(manifest):
    diagrams, snippets, tests, decisions = (indexed(manifest[key]) for key in ("diagrams", "snippets", "tests", "decisions"))
    for collection in (diagrams, snippets, tests, decisions):
        for record in collection.values():
            for field in ("path", "sourcePath", "asset"):
                if field in record:
                    safe_path(record[field])
    for step in indexed(manifest["steps"]).values():
        if step["diagramId"] not in diagrams:
            raise ValueError(f'{step["id"]}: unknown diagram {step["diagramId"]}')
        diagram = diagrams[step["diagramId"]]
        for key in ("node", "edge"):
            selected = set(step[f"{key}Ids"])
            if len(selected) != len(step[f"{key}Ids"]) or not selected or not selected.issubset(diagram[f"{key}s"]):
                raise ValueError(f'{step["id"]}: unknown {key} in {diagram["id"]}')
        for group, key in ((snippets, "snippetId"), (tests, "testId"), (decisions, "decisionId")):
            if step[key] not in group:
                raise ValueError(f'{step["id"]}: unknown {key} {step[key]}')


def line_index(lines, hint):
    matches = [i for i, line in enumerate(lines) if hint in line]
    if len(matches) != 1:
        raise ValueError(f"Expected one anchor {hint!r}, found {len(matches)}")
    return matches[0]


def evidence(entry, lines):
    if "start" in entry:
        start = line_index(lines, entry["start"])
        end = line_index(lines, entry["end"])
    else:
        start = line_index(lines, entry["symbol"] + "(")
        indent = lines[start][:len(lines[start]) - len(lines[start].lstrip())]
        end = next((i for i in range(start + 1, len(lines)) if lines[i] == indent + "}"), None)
        if end is None:
            raise ValueError(f'No method end for {entry["symbol"]}')
    if end < start:
        raise ValueError(f'Reversed source region for {entry["id"]}')
    return {k: entry[k] for k in ("id", "path", "symbol")} | {"startLine": start + 1, "endLine": end + 1}


def collect(revision):
    commit = git("rev-parse", "--verify", "--end-of-options", revision + "^{commit}").decode().strip()

    def read(path):
        return git("show", f"{commit}:{safe_path(path)}")

    hints = json.loads(read("reference/sources/tour.json"))
    files = {"schema.json": read("reference/schema/manifest.v1.json")}
    manifest = {"schemaVersion": 1, "source": {"repository": "m-jovanovic/modular-monolith", "commit": commit},
                "steps": hints["steps"], "diagrams": [], "snippets": [], "tests": [], "decisions": []}
    for key in hints["diagrams"]:
        path = f"docs/diagrams/{key}.svg"
        design = json.loads(read(f"docs/diagrams/design/{key}.json"))
        asset = f"diagrams/{key}.svg"
        files[asset] = read(path)
        svg = ElementTree.fromstring(files[asset])
        groups = [g.get("id") for g in svg.findall("{http://www.w3.org/2000/svg}g")]
        nodes = [node[0] for node in design["nodes"]]
        edges = [edge[0] for edge in design["edges"]]
        if sorted(groups) != sorted(nodes + edges) or len(set(groups)) != len(groups):
            raise ValueError(f"{key}: SVG and design group IDs differ")
        manifest["diagrams"].append(dict(id=key, title=design["title"], path=asset, sourcePath=path, nodes=nodes, edges=edges))
    for kind in ("snippets", "tests"):
        for entry in hints[kind]:
            lines = read(entry["path"]).decode().splitlines()
            record = evidence(entry, lines)
            if kind == "snippets":
                asset = f'snippets/{entry["id"]}.txt'
                record["asset"] = asset
                files[asset] = ("\n".join(lines[record["startLine"] - 1:record["endLine"]]) + "\n").encode()
            manifest[kind].append(record)
    for key in hints["decisions"]:
        path = f"docs/decisions/{key}.md"
        asset = f"decisions/{key}.md"
        files[asset] = read(path)
        header = files[asset].decode().splitlines()[0]
        if not header.startswith("# "):
            raise ValueError(f"{path} has no decision title")
        manifest["decisions"].append(dict(id=key, title=header[2:], path=path, asset=asset))
    check_links(manifest)
    files["manifest.json"] = (json.dumps(manifest, indent=2, ensure_ascii=False) + "\n").encode()
    return files


def write_bundle(revision, output):
    files = collect(revision)
    output.mkdir(parents=True, exist_ok=True)
    archive = output / ARCHIVE
    # Standard ZIP storage avoids compression-library differences across release builders.
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED) as bundle:
        for name, data in sorted(files.items()):
            member = zipfile.ZipInfo(safe_path(name), date_time=(1980, 1, 1, 0, 0, 0))
            member.create_system = 3
            member.external_attr = 0o100644 << 16
            bundle.writestr(member, data)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (output / (ARCHIVE + ".sha256")).write_text(f"{digest}  {ARCHIVE}\n")
    return digest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", default="HEAD")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(write_bundle(args.revision, args.output), ARCHIVE)
