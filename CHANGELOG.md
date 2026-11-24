# Equipment Library

## First public reference release

Run the .NET 11 equipment library with Docker Compose. Race two members for one barcoded copy, follow the winning hold to the librarian's queue, and take the copy through collection, renewal and return. Try the restart exercise to see a durable receipt acknowledged twice and applied once.

Catalog, Lending and PickupDesk expose module-specific use cases in one process. PostgreSQL roles, architecture tests and module tests enforce the boundaries. Every diagram and excerpt in the release bundle resolves to the tagged source tree.

The AI change exercises freeze a separate starting commit so contributors can compare a fresh patch against the same behavior and module ownership constraints. See the task records for deterministic results and human review status.

The [use-case inventory](https://github.com/m-jovanovic/modular-monolith/blob/v1.0.0/docs/use-cases.md) lists all 50 named operations and their permissions. The release includes the generated OpenAPI document, a deterministic archive for the interactive tour, its SHA-256 and a verification report with the exact tested commit.

The optional labs compare single-process delivery with an independently deployable JetStream pickup service, rebuild a copy history from versioned events, and test Wolverine with module-scoped PostgreSQL credentials.
