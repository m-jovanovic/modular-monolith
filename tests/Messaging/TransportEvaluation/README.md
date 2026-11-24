# Evaluate Wolverine's PostgreSQL transport

The equipment library gives Lending and PickupDesk DML rights only in their own schemas. This experiment asks whether a native PostgreSQL transport can keep a hold and its outgoing message atomic, recover a killed publisher and deliver under those grants.

The lab uses .NET SDK 11.0.100, Npgsql 11.0.0, WolverineFx.Postgresql and WolverineFx.RuntimeCompilation 6.65.1. The package lock belongs to this executable. The application runs without these package references. The configuration uses one logical Wolverine service per module, `DurabilityMode.Solo`, per-module envelope storage and a single transport queue stored in `lending`. The migration identity provisions storage before either module runtime starts. [Official transport configuration](https://wolverinefx.net/guide/durability/postgresql.html) · [Pinned transaction adapter source](https://github.com/JasperFx/wolverine/blob/1a8fde0675c9e334aea64fd8d83de3968b700ee7/src/Persistence/Wolverine.RDBMS/DatabaseEnvelopeTransaction.cs)

## Run the experiment

Start PostgreSQL with `docker compose up -d --wait postgres` from the repository root. These example credentials are for the local Compose profile. Supply your own disposable PostgreSQL admin connection when using a different instance.

```sh
export EQLIB_TEST_PG_ADMIN="Host=127.0.0.1;Port=55432;Database=equipment;Username=library_admin;Password=local-library-admin"
dotnet restore tests/Messaging/TransportEvaluation --locked-mode
dotnet run -c Release --no-restore --project tests/Messaging/TransportEvaluation
```

The fixture creates a uniquely named database and two login roles and removes them on exit. The child publisher receives only its Lending connection. The parent retains admin credentials to provision objects, compare the two IDs after delivery and remove the fixture.

| Operation | Observed on PostgreSQL 17 |
| --- | --- |
| Enlist Wolverine's message context in a native Npgsql transaction, insert the hold, publish and roll back. | Zero Lending rows, outgoing envelopes and queued messages. |
| Repeat, commit and kill the publishing process before its call to flush outgoing messages. | One business row and one outgoing envelope survived the process. The transport queue was empty at this observation. |
| Start the actual PickupDesk Wolverine listener with its own envelope schema and its own DML-only runtime role. | Its attempt to access the transport queue in Lending's schema produced PostgreSQL `42501`. No pickup was projected. |
| Grant PickupDesk usage on the containing schema and DML on just the ready and scheduled transport tables. Restart the publisher and listener. | The one pending message reached the handler. Its pickup ID matched the Lending hold. The PickupDesk role still received `42501` when it tried to read the business table. |

A team willing to operate shared queue tables can use this configuration for atomic publishing and restart recovery. A third schema for transport is another design requiring an explicit ownership and grant decision. The reference instead hands a committed fact to a subscriber-owned durable receipt through a small public interface. This run tests one Wolverine configuration. It does not establish duplicate-application semantics for every provider or handler. The production [duplicate and failure tests](../../../tests/Modules/PickupDesk/PickupDeskTests.cs) exercise those separately. [Decision](../../../docs/decisions/durable-delivery.md)
