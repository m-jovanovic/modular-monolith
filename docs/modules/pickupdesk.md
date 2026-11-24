# PickupDesk

A librarian needs a list of copies to prepare. Lending publishes each accepted hold transition. PickupDesk owns the receipt, queue, preparation assignment and issue note. Lending owns the authoritative decision to collect or cancel. PickupDesk identifies the hold and copy without storing borrower identity.

## Follow a delivery

The relay calls [IPickupInbox.AcceptAsync](../../src/Modules/PickupDesk/Contracts/IPickupInbox.cs). The receipt becomes durable under the runtime database role. A retry can acknowledge that same fact. Reusing a message ID with changed content raises a contract error.

The [consumer](../../src/Modules/PickupDesk/PickupDesk/Inbox/ProcessPickupInbox.cs) selects one due receipt with a skip-locked row lock. The [projection](../../src/Modules/PickupDesk/PickupDesk/Inbox/ApplyHoldSnapshot.cs) applies the newest compatible sequence for that hold. A terminal snapshot leaves a closed row with the revision. Late placement cannot reopen it. The transaction commits the projection and completion marker together. Processing another worker's completed receipt has no effect.

Malformed snapshots and conflicting facts go to quarantine. A processing SQL error rolls back the projection to a savepoint and schedules an exponentially delayed retry. Five unsuccessful attempts quarantine that receipt. A database outage releases the transaction, and the background worker reconnects. Operator redrive preserves the original payload and ID and records the operator, reason and previous failure. Recovery after loss of the database requires a consistent backup or a snapshot and subsequent history.

## Staff workflows

All routes start at `/api/pickup-desk`. The host supplies the `Pickup.Manage` permission. Inbox inspection and redrive additionally require `Pickup.Operate`.

| HTTP operation | Business effect |
| --- | --- |
| GET /queue | Read active pickups in occurrence order. Filter by copyId or assignedTo and page with limit and offset. |
| GET /queue/{holdId} | Read one active pickup with its current preparation status. |
| POST /queue/{holdId}/claim | Assign the pickup to the signed-in staff member. Concurrent claims have one winner. |
| POST /queue/{holdId}/release | Give up your claim while the pickup has no issue or ready timestamp. |
| POST /queue/{holdId}/issues | Report the equipment preparation problem on your claimed pickup. The body supplies a reason. |
| POST /queue/{holdId}/issues/resolve | Resolve the issue before proceeding. |
| POST /queue/{holdId}/ready | Mark your claimed pickup ready once all issues are resolved. |
| GET /workload | Count unclaimed, assigned, blocked and ready pickups, with the oldest active occurrence. |
| GET /history | Read cancelled and collected snapshots, optionally filtered by copyId. |
| GET /inbox/quarantine | Review quarantined receipt IDs, attempts, failure classifications and timestamps. |
| POST /inbox/{messageId}/redrive | Give a reviewed quarantined receipt another attempt, with an audit reason. |

A transaction locks the projection row while checking a staff transition. A late terminal event removes a pickup from the active queue even when a librarian was preparing it. Closed pickups retain the last staff preparation details for historical inspection. Staff inputs should describe equipment and preparation only.

## Verify a change

Run the module checks from the repository root with Docker available.

```sh
./scripts/verify.sh tests/Modules/PickupDesk/EquipmentLibrary.PickupDesk.Tests.csproj
```

The tests use a disposable PostgreSQL container by default. Providing `EQLIB_TEST_PG_ADMIN` uses a dedicated new database and role on an existing PostgreSQL server. The suite runs the real [migrations](../../src/Modules/PickupDesk/Migrations/), uses a restricted runtime role, drives the staff HTTP endpoints and simulates failure after the projection write. [Executable scenarios](../../tests/Modules/PickupDesk/PickupDeskTests.cs)
