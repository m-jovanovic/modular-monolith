# Modular Monolith Architecture in .NET

A community equipment library has one cordless drill left. Two members try to reserve it. Lending must choose one winner, and the librarian should eventually see one pickup even if a message arrives twice. This reference follows that problem through a modular monolith.

## Run the reference

Install the .NET 10 SDK pinned in [global.json](global.json). From the repository root, run the checks and start the API.

```sh
./scripts/verify.sh
dotnet run --project src/Api/EquipmentLibrary.Api.csproj --no-launch-profile -- --urls http://127.0.0.1:5080
```

In another terminal, check the host.

```sh
curl --fail http://127.0.0.1:5080/health
# Healthy
```

The first increment ships the running host, a Catalog lookup contract, versioned Lending event contracts, and executable fixtures. The reserve, cancel, collect and pickup routes described in the contract are implementation targets. Their fixtures can be exercised today without a database.

| Module | Responsibility | First place to look |
| --- | --- | --- |
| Catalog | Describe a type of equipment so a librarian can enroll a physical copy. | [Public lookup contract](src/Modules/Catalog/Contracts/) |
| Lending | Own each copy and decide which borrower gets the hold. | [Integration events](src/Modules/Lending/IntegrationEvents/) |
| PickupDesk | Show staff the pickups awaiting collection using its own local view. | [Contract and fixtures](reference/contracts/) |

## Work on a change

Read the [charter](docs/charter.md) for the business boundaries. Run the [contract tests](tests/Contracts/) to see how state changes are represented on the wire. A coding agent or human contributor can use the same scoped commands and acceptance examples.

```sh
./scripts/verify.sh tests/Contracts/EquipmentLibrary.Contracts.Tests.csproj
./scripts/check-analyzers.sh
```

The next increment connects these contracts to module-owned PostgreSQL schemas and enforces the permitted module dependencies. For project scope and review expectations, see [Contributing](CONTRIBUTING.md).

## Background

The code conventions draw on Milan Jovanović’s Evently course. Kamil Grzybek’s reference influenced how this project documents domain language, architectural decisions and executable architecture rules. [Modular Monolith Architecture course](https://milanjovanovic.tech/modular-monolith-architecture) · [Modular Monolith with DDD](https://github.com/kgrzybek/modular-monolith-with-ddd)
