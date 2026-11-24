# Keep one owner for each decision

**Accepted.** A catalog item describes a model, a copy is an individual asset, and a pickup entry is a staff view of an accepted hold. Combining them in a shared entity would make changing a display field and changing reservation policy look equally simple.

Each module owns its tables, migrations and database role. A Catalog call during enrollment gives Lending a descriptive snapshot. Lending can decide availability in its own transaction even if the catalog title changes later. PickupDesk learns committed hold changes through public events and reads its local queue. Code dependencies and database permissions enforce different parts of this boundary.

A title on a previously enrolled copy can be stale. A future requirement for immediate descriptive changes should specify a refresh contract and its cost. [Catalog lookup](../../src/Modules/Catalog/Contracts/ICatalogLookup.cs) · [Enrollment slice](../../src/Modules/Lending/EquipmentLibrary.Lending/Copies/EnrollCopy.cs) · [Role setup](../../infra/postgres/module.sql)
