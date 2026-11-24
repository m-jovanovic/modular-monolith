# Choose a history model for a physical copy

**Decision.** Keep the reference Lending module's transactional copy, hold and loan tables. Staff can answer the current circulation questions with the copy details and copy-history endpoint. A separate event-sourcing lab studies an additional requirement: reconstruct exactly what staff knew about a barcode or a copy's state at a particular stream revision.

| Question | Conventional audit snapshots | Copy event stream |
| --- | --- | --- |
| What did staff see at revision 3? | Read one row from the audit. | Fold enrollment, placement and collection in version order. |
| Why did the barcode change? | Add and populate a reason field when the audit needs it. | The correction event records the earlier barcode, replacement and reason. |
| How do we change an old field name? | Migrate old rows or preserve a read mapping. | Keep readers for each supported schema. Here the original enrollment supplies `assetTag` and v2 uses `barcode`. |
| How do we repair incorrect data? | Append a corrected snapshot and carry forward earlier rows. | Append a correction fact. Before-and-after replay preserves both interpretations. |
| What must operators maintain? | A write transaction for the copy and its audit rows, suitable indexes and retention. | Stable event contracts, version checks, replay code, build checkpoints as streams grow, and a rollout procedure for changed projections. |

The experiment loads eight revisions and reconstructs them twice. Both projections match a separate conventional audit at all eight revisions. Every append replays the stream for validation in this small example. For a copy with thousands of revisions the repeated work would grow quadratically. Snapshots with tested restore points or incremental validation would become an explicit design cost.

The lab's stream describes one copy, including returns, retirement and correction. Lending's v1 integration messages describe individual holds. Those messages omit operations required to reconstruct a full copy history. A future adoption would need an initial snapshot, a cutover version and a way to coordinate new writes with the old representation. [Run the comparison](../../labs/copy-history/README.md)
