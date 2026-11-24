# Commit one winning hold

**Accepted.** Two members may submit a reservation for the same barcode before either request returns. Checking availability in application memory would allow both requests to act on the same observation.

Lending attempts a conditional update on the copy inside the transaction that writes the hold and its outbox entry. PostgreSQL rechecks the update condition when a competitor commits. A failed claim yields a documented conflict if the copy exists. Only the winner commits a hold and an outgoing fact. A separate synchronous read is useful for display and carries no guarantee about a subsequent reservation.

This design adds row contention on a popular copy. It limits the contention to an asset whose allocation is inherently exclusive. [Reservation handler](../../src/Modules/Lending/EquipmentLibrary.Lending/Holds/PostgresHoldReservationStore.cs) · [Two-borrower test](../../tests/Modules/Lending/ReservationTests.cs)
