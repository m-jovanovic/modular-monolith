#!/usr/bin/env python3
"""Grade a local benchmark patch against the pinned public starting tree."""
import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from xml.etree import ElementTree

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
PROJECT = "tests/Modules/PickupDesk/EquipmentLibrary.PickupDesk.Tests.csproj"


def run(cwd, *args):
    return subprocess.run(args, cwd=cwd, capture_output=True, text=True, check=False)


def require(cwd, *args):
    result = run(cwd, *args)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    return result.stdout


def check_boundaries(source, check_contract_imports):
    # These signals explain common wrong turns. Runtime tests also execute as the module role.
    stripped = source.replace("EquipmentLibrary.Lending.IntegrationEvents", "")
    if re.search(r'\b(?:catalog|lending)"?\s*\.\s*"?[a-z_]', stripped, re.I):
        return "foreign schema or implementation"
    if "FromKeyedServices(" in source and any(
        key != "PickupDesk" for key in re.findall(r'FromKeyedServices\(\s*"([^"]+)"', source)
    ):
        return "foreign database role"
    if check_contract_imports:
        for name in re.findall(r"^using ([\w.]+);", source, re.MULTILINE):
            if name != "EquipmentLibrary.Lending.IntegrationEvents" and name.split(".")[0] != "System":
                return f"outside the event contract and BCL: {name}"
    return None


def assess(task, patch, checkout):
    config = json.loads((HERE / "tasks.json").read_text())
    definition = config["tasks"][task]
    baseline = config["baseline"]
    require(ROOT, "git", "clone", "--quiet", "--shared", "--no-checkout", str(ROOT), str(checkout))
    require(checkout, "git", "checkout", "--quiet", "--detach", baseline)
    if patch:
        require(checkout, "git", "apply", "--index", str(patch.resolve()))
    changed = require(checkout, "git", "diff", "--cached", "--name-only", "-z").strip("\0").split("\0") if patch else []
    allowed = set(definition.values())
    scope_ok = definition["implementation"] in changed and set(changed).issubset(allowed)
    result = dict(task=task, baseline=baseline, changedPaths=changed, scope=scope_ok,
                  boundaries=None, behavior=None, testsPassed=0, testsTotal=0)
    if patch and not scope_ok:
        return result
    for path in changed:
        # Reject symlinks and deletions before reading proposed files.
        target = checkout / path
        if target.is_symlink() or not target.is_file():
            result.update(boundaries=False, reason="expected a regular source file")
            return result
        violation = check_boundaries(target.read_text(), task == "rollup" and path == definition["implementation"])
        if violation:
            result.update(boundaries=False, reason=violation)
            return result
    result["boundaries"] = True
    shutil.copyfile(HERE / f"probes/{task}.cs.txt", checkout / "tests/Modules/PickupDesk/IndependentBenchmark.cs")
    require(checkout, "dotnet", "restore", PROJECT, "--locked-mode")
    output = checkout / "results"
    tests = run(checkout, "dotnet", "test", PROJECT, "-c", "Release", "--no-restore",
                "--results-directory", str(output), "--logger", "trx;LogFileName=grade.trx")
    report = output / "grade.trx"
    if report.is_file():
        document = ElementTree.parse(report)
        entries = document.findall(".//{*}UnitTestResult")
        assertions = [e for e in entries if "Independent" in e.attrib["testName"]]
        result.update(testsPassed=sum(e.attrib["outcome"] == "Passed" for e in entries), testsTotal=len(entries))
        result["behavior"] = bool(assertions) and all(e.attrib["outcome"] == "Passed" for e in assertions) and tests.returncode == 0
    else:
        result["behavior"] = False
    if not result["behavior"]:
        print(tests.stdout[-4000:], file=sys.stderr)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("task", choices=["barcode", "rollup"])
    parser.add_argument("patch", nargs="?", type=Path, help="Omit to measure the unchanged baseline.")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="equipment-grade-") as temp:
        result = assess(args.task, args.patch, Path(temp) / "candidate")
    result["passed"] = all(result[key] is True for key in ("scope", "boundaries", "behavior"))
    print(json.dumps(result, indent=2))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
