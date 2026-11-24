# Lending

Lending owns physical copies, availability, borrower holds and loans. A Catalog item can describe many copies. Enrollment calls `ICatalogLookup` before writing a copy and snapshots the item title. Subsequent circulation decisions use Lending's own tables. Only a librarian can enroll or retire a copy. A borrower reserves one identified copy at a time.

## Follow a copy

1. Staff enroll a barcode and catalog item. Repeating the enrollment returns the existing copy. A barcode assigned to another item returns a conflict.
2. A borrower places a hold. A conditional PostgreSQL UPDATE changes one available copy to held. A concurrent request for the same copy receives a conflict. The hold, the copy state and the versioned HoldPlaced outbox entry commit together.
3. The borrower can cancel the hold, freeing the copy. Staff can collect it, opening a fourteen-day loan identified by the hold ID. Both actions lock the same hold and check its active state. Exactly one terminal event can commit.
4. The borrower may renew an open loan once before its due date. Staff can see overdue loans and record a return. Returning the same loan twice reports the original return timestamp. An old return request cannot release a later borrower’s hold or loan.
5. Staff can retire an available copy and subsequently reinstate it. The circulation history retains prior reservations and returns.

## HTTP use cases

Paths have the prefix `/api/lending`. List queries accept a nonnegative offset and a limit of 1–100 (default 25).

| Operation | Caller | Result |
| --- | --- | --- |
| POST /copies | Lending.Enroll | Enroll a barcode using a public Catalog lookup. |
| GET /copies/{copyId} | Anyone | Read the copy's catalog ID, barcode and authoritative state. |
| GET /copies | Anyone | Find copies by optional catalogItemId and state. |
| GET /availability/{catalogItemId} | Anyone | Read counts of available, held, loaned and retired copies. |
| POST /copies/{copyId}/retire | Lending.Enroll | Take an available copy out of circulation. |
| POST /copies/{copyId}/reinstate | Lending.Enroll | Restore a retired copy to availability. |
| GET /copies/{copyId}/history | Pickup.Manage | Read its holds and return times. |
| POST /holds | Borrower | Place a hold for the authenticated borrower. |
| GET /holds/{holdId} | Owning borrower or Pickup.Manage | Read authoritative hold state. |
| GET /me/holds | Borrower | List the caller’s holds. |
| POST /holds/{holdId}/cancel | Owning borrower | Release the copy and enqueue a cancellation. |
| POST /holds/{holdId}/collect | Pickup.Manage | Record collection, open a loan and enqueue the terminal event. |
| GET /loans/{loanId} | Owning borrower or Pickup.Manage | Read checkout, renewal, due and return details. |
| GET /me/loans | Borrower | List the caller's loans. Supply openOnly=true to see outstanding loans. |
| GET /loans/overdue | Pickup.Manage | Find open loans due at or before the current clock time. |
| POST /loans/{loanId}/renew | Owning borrower | Extend the due date by fourteen days, at most once. |
| POST /loans/{loanId}/return | Pickup.Manage | Record the return against this loan. |

## Persistence and event delivery

The runtime role has access to `lending`. Each feature states the query it executes. Conditional updates and row locks make the winning transition visible under PostgreSQL’s default read-committed isolation. A partial unique index also enforces one active hold per copy. The library uses explicit transaction boundaries in these slices to expose both the database invariant and the atomic outbox insert in the source.

PickupDesk consumes full-state events carrying hold and copy identifiers, barcode and an optional title. Only committed hold transitions appear on this integration stream. Loan renewals and returns change Lending’s own records. The outbox records the W3C trace context when present. [Wire contract and examples](../../reference/contracts/README.md)

## Verification

Run `./scripts/verify.sh tests/Modules/Lending/EquipmentLibrary.Lending.Tests.csproj` with Docker running. The suite uses a real PostgreSQL database, restricted runtime credentials, an isolated test identity provider and ASP.NET Core TestServer. To use an existing test cluster, set `EQLIB_TEST_PG_ADMIN` to an administrative connection that can create a temporary database and role. The fixture creates uniquely named resources and removes them after the run.

- [Last-copy contention, authorization and cancel/collect race](../../tests/Modules/Lending/ReservationTests.cs)
- [Outbox write failure and transaction rollback](../../tests/Modules/Lending/AtomicPublicationTests.cs)
- [Loan renewal, overdue reports and safe return retries](../../tests/Modules/Lending/LoanLifecycleTests.cs)
- [Enrollment, retirement and reinstatement](../../tests/Modules/Lending/CopyInventoryTests.cs)
