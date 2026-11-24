#!/usr/bin/env python3
"""Compare the default equipment-library host with the optional JetStream-backed extraction."""
from contextlib import ExitStack
import json
from pathlib import Path
import subprocess
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parent / "experiment"))
from database import Databases
from processes import MONOLITH, PICKUP, PUBLISHER, broker
from workload import run


def main():
    for project in (MONOLITH, PUBLISHER, PICKUP):
        subprocess.run(["dotnet", "restore", str(project), "--locked-mode"], check=True)
        subprocess.run(["dotnet", "build", str(project), "--no-restore", "-c", "Release", "--nologo"], check=True)
    observations = []
    with ExitStack() as resources:
        scratch = Path(resources.enter_context(tempfile.TemporaryDirectory(prefix="equipment-extraction-")))
        databases = Databases(resources)
        with ExitStack() as processes:
            observations.append(run(False, databases, None, processes, scratch))
        with ExitStack() as processes:
            broker_url = broker(processes, scratch)
            observations.append(run(True, databases, broker_url, processes, scratch))
    print(json.dumps(observations, indent=2))


if __name__ == "__main__":
    main()
