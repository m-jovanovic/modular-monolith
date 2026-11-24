# Release the teaching reference

The public repository owns the contracts, explanations and proof. The website reads assets from a tagged GitHub release. A reader can use the same bundle independently.

## Build and verify a bundle

In a complete Git clone with Python 3.9 or later, run these commands at the revision you want to publish.

```sh
python3 tools/reference/render_diagrams.py --check
python3 -m unittest discover -s tools/reference
python3 tools/reference/export.py --revision HEAD --output /tmp/equipment-reference
cd /tmp/equipment-reference
shasum -a 256 -c equipment-library-reference.zip.sha256
unzip -p equipment-library-reference.zip manifest.json
```

The exported ZIP records a full source commit. The exporter reads each source file from that Git object, verifies unique snippet anchors and resolves diagram, test and decision IDs. Rebuilding the same revision yields identical bytes across builders. GitHub's generated source download is a separate artifact.

The [manifest schema](../../reference/schema/manifest.v1.json) defines version 1. The [working fixture](../../reference/fixtures/valid.json) and [missing-node fixture](../../reference/fixtures/unknown-node.json) share a real source revision. A consumer should reject unsafe paths, duplicate IDs, unresolved references, unknown schema versions and mismatched SHA or digest. Verify snippets against the inclusive one-based source range. A release lock should contain the tag, immutable source commit, asset name and archive SHA-256.

## Publish from a tested commit

A maintainer reviews notices, the changelog and test results, tags the approved revision and pushes the tag. The [release workflow](../../.github/workflows/release.yml) reruns the suite, architecture and analyzer probes, PostgreSQL grants, concurrent borrower and restart journeys, and benchmark controls against the tagged tree. It publishes the ZIP, its detached digest, the running host’s OpenAPI document and the verification record. Retrieve the four assets together with `gh release download TAG` and check that the manifest commit matches `git rev-parse 'TAG^{commit}'`.

Keep the last working tag accessible to consumers. Never repoint a published tag. Change additive fields within a version only when existing strict readers can still consume the documents. A changed field contract or identifier needs a new schema version or a release with explicit migration instructions and a new consumer pin.
