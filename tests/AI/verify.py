#!/usr/bin/env python3
"""Check positive and negative controls for both frozen benchmark tasks."""
import json
from pathlib import Path
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def measure(task, patch, expected):
    command = [sys.executable, str(HERE / "grade.py"), task]
    if patch:
        command.append(str(HERE / patch))
    run = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if not run.stdout:
        raise RuntimeError(run.stderr)
    result = json.loads(run.stdout)
    for key, value in expected.items():
        if result[key] != value:
            raise AssertionError(f"{task} {patch}: expected {key}={value}, got {result}\n{run.stderr}")
    if (run.returncode == 0) != result["passed"]:
        raise AssertionError(f"{task}: exit status disagrees with score")
    print(f"{task} {patch or 'baseline'}: {result['testsPassed']}/{result['testsTotal']} tests passed, accepted={result['passed']}")
    return result


if __name__ == "__main__":
    for task in ("barcode", "rollup"):
        measure(task, None, dict(passed=False, scope=False, behavior=False))
        measure(task, f"solutions/{task}.patch", dict(passed=True, scope=True, boundaries=True, behavior=True))
    measure("barcode", "shortcuts/cross-schema.patch", dict(passed=False, scope=True, boundaries=False, behavior=None))
