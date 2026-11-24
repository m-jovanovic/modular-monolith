"""Write the public use-case inventory from a running Equipment Library OpenAPI document."""
import argparse
import json
from pathlib import Path
import re
from urllib.request import urlopen


def inventory(document):
    groups = {}
    for path, methods in document["paths"].items():
        if not path.startswith("/api/"):
            continue
        for method, operation in methods.items():
            if method not in ("delete", "get", "patch", "post", "put"):
                continue
            tag = operation["tags"][0]
            access = operation.get("description", "Public")
            access = access.removeprefix("Required permissions: ").removesuffix(".")
            name = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", operation["operationId"])
            groups.setdefault(tag, []).append((name, method.upper(), path, access))
    count = sum(map(len, groups.values()))
    lines = ["# Equipment library use cases", "",
             f"The running .NET host exposes {count} named business and operator operations. "
             "The declared permissions below apply at the endpoint. Lending also checks ownership when a borrower reads or changes an individual hold or loan.",
             "", "Use the [quickstart](../README.md#run-the-library) to try these operations. "
             "The host serves its OpenAPI document at `/openapi/v1.json`.", ""]
    for tag, operations in sorted(groups.items()):
        lines.extend([f"## {tag}", "", "| Use case | HTTP | Endpoint authorization |", "| --- | --- | --- |"])
        for name, method, path, access in sorted(operations):
            lines.append(f"| {name} | `{method} {path}` | {access} |")
        lines.append("")
    return "\n".join(lines)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="http://127.0.0.1:5284")
    parser.add_argument("--markdown", type=Path, required=True)
    parser.add_argument("--openapi", type=Path)
    args = parser.parse_args()
    with urlopen(args.base.rstrip("/") + "/openapi/v1.json", timeout=15) as response:
        spec = json.load(response)
    args.markdown.write_text(inventory(spec))
    if args.openapi:
        args.openapi.write_text(json.dumps(spec, indent=2) + "\n")
