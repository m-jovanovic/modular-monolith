"""Write verifiable release metadata from the completed Actions verification job."""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile


def report(commit, tag, run, jobs, directory):
    matching = [job for job in jobs["jobs"] if job["name"] == "verify" and job["conclusion"] == "success"]
    if len(matching) != 1:
        raise ValueError("Expected one successful verification job in this run.")
    steps = [
        {"name": step["name"], "conclusion": step["conclusion"], "startedAt": step["started_at"],
         "completedAt": step["completed_at"]}
        for step in matching[0]["steps"] if step["name"].startswith("Run ")
    ]
    if not steps or any(step["conclusion"] != "success" for step in steps):
        raise ValueError("The verification job contains unfinished or failed commands.")
    archive = directory / "equipment-library-reference.zip"
    with ZipFile(archive) as bundle:
        manifest = json.loads(bundle.read("manifest.json"))
    if manifest["source"]["commit"] != commit:
        raise ValueError("Archive source differs from the tested commit.")
    evidence = {
        "commit": commit, "tag": tag, "verificationRun": run, "verifiedAt": matching[0]["completed_at"],
        "asset": archive.name, "sha256": hashlib.sha256(archive.read_bytes()).hexdigest(),
        "tourSteps": len(manifest["steps"]), "diagrams": len(manifest["diagrams"]),
        "upgradeBaseline": "001 in Catalog, Lending and PickupDesk", "verificationSteps": steps,
    }
    (directory / "release-verification.json").write_text(json.dumps(evidence, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("commit", "tag", "run"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--jobs", type=Path, required=True)
    parser.add_argument("--directory", type=Path, required=True)
    args = parser.parse_args()
    report(args.commit, args.tag, args.run, json.loads(args.jobs.read_text()), args.directory)
