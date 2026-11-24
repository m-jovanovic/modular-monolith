# Expire a pickup hold

Run from the repository root with Docker available.

```sh
dotnet test tests/Modules/Lending/EquipmentLibrary.Lending.Tests.csproj -c Release --filter 'FullyQualifiedName~HoldExpiryTests|FullyQualifiedName~HoldPickupWindowTests'
```

The fixture creates an isolated PostgreSQL database and a restricted Lending identity. A controlled clock advances to the deadline while the application and database run normally. The tests exercise a worker tick, two competing sweeps, staff collection and a locked oldest row. The losing collector receives a conflict; a later borrower can reserve the released copy. Duplicate expiry attempts leave the original terminal event intact. The module guide documents using an existing test cluster. [Run the module suite](../../docs/modules/lending.md) · [Read the decision](../../docs/decisions/expiry-and-compensation.md)
