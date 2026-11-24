from copy import deepcopy
import hashlib
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from zipfile import ZipFile

import export


class ReleaseTests(unittest.TestCase):
    def fixture(self, name):
        return json.loads((export.ROOT / "reference/fixtures" / f"{name}.json").read_bytes())

    def test_published_fixtures(self):
        export.check_links(self.fixture("valid"))
        with self.assertRaisesRegex(ValueError, "unknown node"):
            export.check_links(self.fixture("unknown-node"))

    def test_duplicate_and_dangling_ids(self):
        manifest = self.fixture("valid")
        manifest["tests"].append(deepcopy(manifest["tests"][0]))
        with self.assertRaisesRegex(ValueError, "duplicate ID"):
            export.check_links(manifest)
        manifest = self.fixture("valid")
        manifest["steps"][0]["testId"] = "lost-test"
        with self.assertRaisesRegex(ValueError, "unknown testId"):
            export.check_links(manifest)

    def test_relative_source_and_asset_paths(self):
        for field, value in (("path", "../untracked"), ("asset", "/external"), ("sourcePath", "a\\b")):
            with self.subTest(field=field):
                manifest = self.fixture("valid")
                manifest["diagrams"][0][field] = value
                with self.assertRaisesRegex(ValueError, "Invalid relative path"):
                    export.check_links(manifest)

    def test_moved_or_ambiguous_source_anchors(self):
        for hint in ("wrong", "same"):
            with self.subTest(hint=hint), self.assertRaisesRegex(ValueError, "Expected one anchor"):
                export.line_index(["same", "same"], hint)

    def test_rebuild_produces_identical_bytes_and_resolves_every_excerpt(self):
        commit = export.git("rev-parse", "HEAD").decode().strip()
        with TemporaryDirectory() as temp:
            path = Path(temp)
            digest = export.write_bundle(commit, path / "first")
            export.write_bundle(commit, path / "second")
            archive = path / "first" / export.ARCHIVE
            self.assertEqual(archive.read_bytes(), (path / "second" / export.ARCHIVE).read_bytes())
            self.assertEqual(digest, hashlib.sha256(archive.read_bytes()).hexdigest())
            self.assertEqual((archive.parent / (export.ARCHIVE + ".sha256")).read_text(), f"{digest}  {archive.name}\n")
            with ZipFile(archive) as bundle:
                manifest = json.loads(bundle.read("manifest.json"))
                self.assertEqual(commit, manifest["source"]["commit"])
                for entry in manifest["snippets"] + manifest["tests"]:
                    lines = export.git("show", f'{commit}:{entry["path"]}').decode().splitlines()
                    self.assertTrue(1 <= entry["startLine"] <= entry["endLine"] <= len(lines))
                    if "asset" in entry:
                        excerpt = "\n".join(lines[entry["startLine"] - 1:entry["endLine"]]) + "\n"
                        self.assertEqual(excerpt, bundle.read(entry["asset"]).decode())
                for diagram in manifest["diagrams"]:
                    self.assertEqual(export.git("show", f'{commit}:{diagram["sourcePath"]}'), bundle.read(diagram["path"]))


if __name__ == "__main__":
    unittest.main()
