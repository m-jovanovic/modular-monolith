# The cordless drill contracts

A Catalog item describes a model. Lending owns each physical copy and every hold on it. PickupDesk owns the staff queue derived from Lending events. A member picks up a drill using the hold ID. Two borrowers competing for the same copy can observe different answers even though they used the same Catalog entry.

The machine-readable [hold event schema](hold-lifecycle.v1.schema.json) uses JSON Schema 2020-12. The [fixtures](../../tests/Contracts/fixtures/) are examples for any language. The [HTTP examples](../../tests/Contracts/fixtures/http-examples.json) describe expected responses from the future host. Run the executable contract examples with `dotnet test tests/Contracts/EquipmentLibrary.Contracts.Tests.csproj`.

## Who can ask what

During enrollment, Lending calls [ICatalogLookup.GetItemAsync](../../src/Modules/Catalog/Contracts/ICatalogLookup.cs) in-process. It awaits one answer on the request path. A known ID returns its ID, title and description. An unknown ID returns null; infrastructure failure and cancellation propagate as technical failures. Lending never receives a Catalog entity, crosses a schema boundary, or starts a transaction spanning both modules. A copy stores the catalog item ID; a title included on an event is a descriptive snapshot.

| HTTP operation | Caller and input | Outcome |
| --- | --- | --- |
| POST /api/catalog/items | Staff with cataloguing capability sends title and description | 201 with id, title, description and Location. |
| GET /api/catalog/items/{itemId} | Anyone with an item ID | 200 descriptive item, or 404. |
| POST /api/lending/copies | Staff with enrollment capability sends catalogItemId and barcode | 201 with copyId, catalogItemId, barcode and state. Repeating the same barcode and item returns the original copy with 200. Reusing the barcode for another item returns 409 barcode-in-use. Unknown item returns 404 catalog-item-not-found. |
| POST /api/lending/holds | A borrower sends copyId. Identity comes from the authenticated principal. | 201 with holdId, copyId, state and Location. Missing copy gives 404 copy-not-found. An unavailable copy gives 409 copy-unavailable, including a repeated reservation by the winner. |
| GET /api/lending/holds/{holdId} | The borrower who owns the hold or staff with pickup capability | 200 with holdId, copyId and state. Another borrower receives 403. Missing hold gives 404 hold-not-found for an authorized role. |
| POST /api/lending/holds/{holdId}/cancel | The borrower who owns this hold | An active hold becomes cancelled, and its copy becomes available. Returns 200 with the hold view. |
| POST /api/lending/holds/{holdId}/collect | Staff with pickup capability | An active hold becomes collected, and its copy remains unavailable. Returns 200 with the hold view. |
| GET /api/pickup-desk/queue | Staff with pickup capability | 200 with an items array. Each pending entry exposes holdId, copyId, barcode and an optional itemTitle. Borrowers receive 403. |

A development staff persona can carry all three staff capabilities. A borrower’s identity is never accepted in a reserve or cancel request body. Protected routes return 401 for missing or invalid authentication. A valid identity without a required capability or ownership gets 403. Check access before returning a transition conflict. For cancellation and collection, missing holds yield 404 and already closed holds yield 409 hold-not-active. When cancellation races collection, exactly one transition and one new event commit. Successful commands return authoritative Lending state. A queue read may lag the command.

Errors use application/problem+json with type, title, status and a stable code. Type is urn:equipment-library:problem: followed by the code. Consumers match status and code; display text can change. Bad input yields 400 invalid-request. A denied operation produces no event. The HTTP fixture’s two drills share one catalog item. Its last cancellation is on a separate active hold.

## Durable events

The first committed transition emits lending.hold-placed, state held, sequence 1. Terminal transitions emit lending.hold-cancelled or lending.hold-collected with the corresponding state and a higher sequence. A hold never reopens. Another reservation of a released copy creates a new hold ID and a new sequence. Order applies per hold. In every message, data is a complete public snapshot used by PickupDesk. A consumer may see a terminal transition before its placement. No borrower identifiers travel in the queue or event.

The stable GUID messageId identifies one committed fact and stays identical across retries. sequence identifies the revision of data.holdId; occurredAt is an offset-aware timestamp for display and diagnostics. The event name and schemaVersion travel on the wire independent of class names. itemTitle is optional and defaults to null when absent. Barcode identifies the physical copy even when older messages omit the title. V1 readers ignore unknown optional fields and reject unsupported schema versions and unrecognized transitions. The earlier and additive examples exercise both directions as independent compatibility fixtures. They describe the same logical events under different producer versions and must be tested in separate runs. The .NET codec is [HoldEventWire](../../src/Modules/Lending/IntegrationEvents/HoldEventWire.cs).

## Handoff from the relay to PickupDesk

1. Lending commits its business change and the immutable outbox envelope under its own database role. Rollback removes both. Lending owns the outbox migration. The messaging infrastructure supplies persistence and bounded dispatch.
2. The host registers a stable subscriber ID, initially pickup-desk.queue.v1, and event types with the relay. Resolve the intended consumers for the envelope and retain their delivery obligations across retries. A registered receiver accepts the original envelope using its own module role. Durable acceptance means the subscriber has committed a unique (subscriberId, messageId) inbox receipt. Repeated delivery of the same body returns the same successful acknowledgement. A reused ID with a different body is a contract error.
3. The relay completes a producer row after all intended consumers have durably accepted it. A crash between acceptance and acknowledgement can produce a duplicate on retry. A receiver failure leaves its obligation pending. The shared inbox infrastructure supplies receipt and apply behavior. PickupDesk owns its inbox, projection and tombstone migrations. Neither module can query the other module’s tables.
4. A consumer applies a snapshot only if its sequence exceeds the highest applied sequence for that hold. An earlier snapshot must never overwrite a newer one. Place adds the hold to the queue; a terminal snapshot removes it and keeps its sequence as a tombstone. Commit new state and completion marker in one local transaction. Replaying a completed receipt produces no business effect. Conflicting facts for the same hold and sequence require investigation.
5. Both workers expose a cancellable one-batch operation with an observable processed-count or outcome. Contract and component tests can commit, run one relay batch, run one consumer batch and inspect the public result. Interrupt between those steps deterministically. Retry transient failures, quarantine unprocessable envelopes, and retain the original ID on requeue. Keep terminal state through the documented redelivery window. Rebuild a lost projection from consistent backup or snapshot plus complete subsequent history.

## How the contract changes

The contract integrator reviews changes with Catalog, Lending and PickupDesk maintainers. Adding an optional field with defined absence behavior is compatible when the old and additive fixtures both pass. Removing or renaming required fields, narrowing accepted values, changing states, authorization, aggregate ordering or response meaning, or publishing a new required field requires an explicit transition plan. Introduce a new version with consumers ready before retiring the old one. Keep previous payloads as regression fixtures. No contract change can make an authorized caller depend on a private implementation.
