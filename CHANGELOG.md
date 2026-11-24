# Equipment Library

## v1.0.1

The OpenAPI document now describes the actual success statuses, JSON bodies and authorization errors for all 50 operations. A barcode enrollment documents both the created response and the successful retry. A pickup claim documents an empty 204. Feature-local request records have distinct schema identifiers, so clients can discover which fields to send when creating an item, enrolling a copy or reserving it.

Lending now exposes typed application commands for cancellation, collection and loan renewal. The renewal decision reads the clock after PostgreSQL grants the loan lock. A renewal that waited past the due time returns a conflict, and two simultaneous requests from the same borrower can extend an eligible loan only once. The collection test also crosses the pickup deadline while a request waits for its row lock.

## v1.0.0

Run the .NET 11 equipment library with Docker Compose. Race two members for one barcoded copy, follow the winning hold to the librarian's queue, and take the copy through collection, renewal and return. Try the restart exercise to see a durable receipt acknowledged twice and applied once.

Catalog, Lending and PickupDesk expose module-specific use cases in one process. PostgreSQL roles, architecture tests and module tests enforce the boundaries. Every diagram and excerpt in the release bundle resolves to the tagged source tree.

The AI change exercises freeze a separate starting commit so contributors can compare a fresh patch against the same behavior and module ownership constraints. See the task records for deterministic results and human review status.

The [use-case inventory](https://github.com/m-jovanovic/modular-monolith/blob/v1.0.0/docs/use-cases.md) lists all 50 named operations and their permissions. The release includes the generated OpenAPI document, a deterministic archive for the interactive tour, its SHA-256 and a verification report with the exact tested commit.

The optional labs compare single-process delivery with an independently deployable JetStream pickup service, rebuild a copy history from versioned events, and test Wolverine with module-scoped PostgreSQL credentials.
