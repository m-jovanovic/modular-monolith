# Running and recovering hold delivery

Lending commits the copy transition, hold and immutable event in one transaction. A short PostgreSQL lease gives one worker a chance to deliver it. Each registered subscriber commits its receipt before acknowledging. The producer then marks the envelope delivered. If the process stops between receipt and acknowledgement, the lease expires after 30 seconds and the same message ID is sent again.

Each hold transaction records the names of the host’s registered `IHoldSubscriber` instances in its outbox row. Adding a subscriber in a later deployment affects only newly committed events. Removing a subscriber with outstanding deliveries leaves those messages retryable and eventually quarantined. Drain the old subscription or restore a receiver with the same durable name before redriving them. A host with zero registered subscribers records the event with no outstanding delivery.

Dispatch retries the entire captured recipient set with the original message ID. If the first subscriber accepted before a later subscriber failed, its durable receipt makes the retry harmless. An expired worker that returns after a newer lease owner has acknowledged the envelope cannot alter that outcome. Run the real PostgreSQL checks with `dotnet test tests/Modules/Lending/EquipmentLibrary.Lending.Tests.csproj --filter FullyQualifiedName~SubscriberFanoutTests`.

PickupDesk stores each receipt under the pair of subscriber and message ID. It commits its projection change and completion marker together. A terminal hold leaves a sequence tombstone. These rules cover duplicate delivery, a process restart and a late placement following collection.

## Inspect the two queues

Run the local stack and authenticate as the development operator. General pickup staff can manage preparation. `Pickup.Operate` grants these recovery actions.

```sh
curl -fsS -H 'Authorization: Bearer demo-staff' http://127.0.0.1:5284/api/lending/delivery/status
curl -fsS -H 'Authorization: Bearer demo-staff' http://127.0.0.1:5284/api/lending/delivery/quarantine
curl -fsS -H 'Authorization: Bearer demo-staff' http://127.0.0.1:5284/api/pickup-desk/inbox/quarantine
```

The producer status reports the oldest pending age, pending messages, acknowledgements and quarantines. The outbox saves the originating HTTP request’s W3C `traceparent`. On its first acceptance, PickupDesk saves the delivery context in its inbox. Retries retain that original parent. The projection worker can resume the trace after a restart. Older rows without tracing context continue through the same processing path.

| Activity source and meter | Activity | Committed outcomes |
| --- | --- | --- |
| `EquipmentLibrary.Lending.Outbox` | `deliver hold` (Producer) | `acknowledged`, `failed`, or `lease-lost` when another worker owns the acknowledgement |
| `EquipmentLibrary.PickupDesk.Inbox` | `process hold receipt` (Consumer) | `completed`, `retry`, `quarantined` |

Both activities expose `messaging.message.id`, `library.hold.id` when the envelope can be decoded, and `library.delivery.outcome`. The counters `equipment_library.outbox.deliveries` and `equipment_library.inbox.receipts` group observations by outcome. Structured logs carry the trace, message and hold IDs. A broker-backed deployment carries the W3C parent in a transport header, preserving the v1 hold envelope.

Run `python3 tests/Journeys/trace_hold.py` against the local stack to send a chosen trace through a real reservation. The journey checks the staff queue, both durable parents and matching IDs in producer and consumer log entries. [Trace journey](../../tests/Journeys/trace_hold.py)

Investigate the failure, repair the unavailable subscriber or consumer, then redrive the original record through the relevant module. The producer drops its retry count, makes the existing envelope eligible and records the operator. The consumer separately records its reason and operator when redriving a quarantined receipt.

```sh
curl -fsS -X POST -H 'Authorization: Bearer demo-staff' \
  "http://127.0.0.1:5284/api/lending/delivery/quarantine/$MESSAGE_ID/redrive"
curl -fsS -X POST -H 'Authorization: Bearer demo-staff' -H 'Content-Type: application/json' \
  -d '{"reason":"Consumer corrected and verified against the stored payload"}' \
  "http://127.0.0.1:5284/api/pickup-desk/inbox/$MESSAGE_ID/redrive"
```

## Reproduce a hard crash

Use a separate Compose project. The experiment temporarily installs a trigger scoped to a newly enrolled copy and sends SIGKILL to this project's API after its subscriber has committed a receipt. It restarts the process, waits for the actual lease to expire, and proves there is one completed receipt and one staff pickup. It also exercises quarantine, healthy sibling delivery, the operator permission and a stale redrive.

```sh
export COMPOSE_PROJECT_NAME=eqlab-recovery LIBRARY_HTTP_PORT=5291 LIBRARY_PG_PORT=55433
docker compose up --build -d --wait
python3 tests/Journeys/recover_delivery.py --base http://127.0.0.1:5291
docker compose down
```

## Restore scope

The sample retains its event receipts, completion records and terminal sequences. A full database loss needs a consistent PostgreSQL backup. Restoring only recent events into an empty pickup schema could lose a long-lived active hold. Keep the module schemas, receipts, outbox, migration ledger and role configuration together when rehearsing a restore. Recheck the pickup queue and delivery status before accepting staff traffic.

Run `python3 tests/Journeys/restore_pickup.py` against the running demo stack. The rehearsal creates two real holds, waits for their three durable receipts, takes a full backup and uses `pg_restore` in a fresh database on the same PostgreSQL cluster. A pickup-role query must still show the active hold even when the retained event window starts after its placement. The restored database is removed after verification. Disaster recovery on a replacement cluster also requires the module roles and their grants.
