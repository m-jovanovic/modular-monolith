# Can an agent extend one module without crossing a boundary?

A librarian wants to scan a barcode to find one pickup. A second reporting consumer wants to count outstanding holds from Lending's published facts. Each exercise starts from the same public [Git commit](https://github.com/m-jovanovic/modular-monolith/commit/57879340fd4c114624f2c9dd6bfaa9d550e3efbe), before either feature, probe or solution existed. The exercises live in patch files so today's application can evolve independently.

## Results from 24 November 2026

| Frozen prompt | Unmodified baseline | Candidate, including new independent checks | Session |
| --- | --- | --- | --- |
| [Exact barcode search](tasks/barcode.txt) | 11/15 tests passed. The queue ignored the proposed filter. | 23/23 tests passed. Two permitted files changed. | [Transcript excerpts](evidence/barcode-transcript.json) · [Proposed diff](solutions/barcode.patch) |
| [Hold lifecycle consumer](tasks/rollup.txt) | The test project could not compile because the proposed consumer did not exist. | 14/14 tests passed. Two permitted files changed. | [Transcript excerpts](evidence/rollup-transcript.json) · [Proposed diff](solutions/rollup.patch) |

Both sessions used Codex CLI 0.171.0-alpha.13.2 with gpt-6.2-astra, one candidate each. Each started in a new clone of the public repository with the corresponding prompt. The barcode run used the CLI's configured default. The consumer run named the model explicitly after an unsupported model name stopped an earlier launch before work began. Tool-reported verification was limited by sandbox socket permissions. The independent run below exercised the actual HTTP host and PostgreSQL under the module role. The [recorded scores](evidence/results.json) identify each patch by its digest and distinguish behavioral results from human review.

The [negative control](shortcuts/cross-schema.patch) keeps the barcode change in the allowed file but looks up a copy in Lending's private schema. The external grader rejected its foreign schema reference before starting any tests.

## Reproduce the result

Use the SDK in `global.json`, Python 3.9 or newer and Docker. A local PostgreSQL admin connection in `EQLIB_TEST_PG_ADMIN` can provision isolated test databases instead. Run from a full clone of this repository.

```sh
python3 tests/AI/verify.py
python3 tests/AI/grade.py barcode tests/AI/solutions/barcode.patch
python3 tests/AI/grade.py rollup tests/AI/solutions/rollup.patch
```

To make a new attempt, check out the SHA in [tasks.json](tasks.json) in a fresh clone, provide one frozen prompt and give the agent only that checkout. Preserve its patch, including any newly added test file. In a separate checkout at the current release, run `python3 tests/AI/grade.py TASK PATCH`. The grader creates a temporary copy of the baseline, checks the changed paths and module dependencies, injects independent probes, restores locked packages and requires the entire PickupDesk test project to pass. Omit the patch argument to observe the starting behavior. Failed and unmodified candidates return status 1.

## Review the patch

A deterministic pass requires all three checks. The changed files must be within the prompt's scope. Candidate code must respect the module seam and runtime role. Both the independent observations and the existing module suite must pass. Counts grow when a candidate adds its own tests. The checks measure only the behavior exercised by these tasks.

A maintainer reviews the diff for domain naming, readability, scope and the observed failure case. In the barcode candidate, inspect how whitespace, optional input and existing filters flow into the parameterized query. In the consumer candidate, inspect the grouping key, sequence comparison and terminal-state interpretation. Record the reviewer, commit, patch digest and decision in a follow-up review. Both captured patches have received an automated code review and a deterministic score. Human maintainer review is pending.
