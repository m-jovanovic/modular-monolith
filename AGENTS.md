# Equipment Library

Start with the [project charter](docs/charter.md) and the [wire and HTTP contracts](reference/contracts/README.md). Keep a change in its owning module, exercise its real public entry point, and commit one reviewable behavior at a time.

| Owner | Data and public seam | Example to follow |
| --- | --- | --- |
| Catalog | `catalog` owns descriptive models and categories. Lending can call `ICatalogLookup` during enrollment. | [Publish an item](src/Modules/Catalog/Implementation/Items/CreateItem.cs) |
| Lending | `lending` owns copies, availability, holds, loans and the outgoing facts. Other modules consume versioned integration events. | [Place a hold](src/Modules/Lending/EquipmentLibrary.Lending/Holds/PlaceHold.cs) |
| PickupDesk | `pickup_desk` owns durable receipts, staff preparation and a queue that can lag behind Lending. | [Read the queue](src/Modules/PickupDesk/PickupDesk/Features/ListPickups.cs) |

Use the SDK in `global.json` and central versions in `Directory.Packages.props`. The API composition root wires module registrations. Module implementations reach peers through contract projects. Run SQL using the owning module's data source and role. Domain invariants shared by concurrent requests need a database-backed proof.

Scope local verification to the module first. For example, run `./scripts/verify.sh tests/Modules/PickupDesk/EquipmentLibrary.PickupDesk.Tests.csproj`. PostgreSQL integration tests use Docker or a disposable database provisioned from `EQLIB_TEST_PG_ADMIN`. Then run `./scripts/verify.sh` and `./scripts/check-analyzers.sh`.

Return the changed files, commands and observed outputs, including a failing case that your change addresses. For a public contract change, include old and new payload examples and name the producers and consumers affected.
