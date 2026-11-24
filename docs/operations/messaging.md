# Running and recovering hold delivery

Lending commits the copy transition, hold and immutable event in one transaction. A short PostgreSQL lease gives one worker a chance to deliver it. Each registered subscriber commits its receipt before acknowledging. The producer then marks the envelope delivered. If the process stops between receipt and acknowledgement, the lease expires after 30 seconds and the same message ID is sent again.

PickupDesk stores each receipt under the pair of subscriber and message ID. It commits its projection change and completion marker together. A terminal hold leaves a sequence tombstone. These rules cover duplicate delivery, a process restart and a late placement following collection.

## Inspect the two queues

Run the local stack and authenticate as the development operator. General pickup staff can manage preparation. `Pickup.Operate` grants these recovery actions.

```sh
curl -fsS -H 'Authorization: Bearer demo-staff' http://127.0.0.1:5284/api/lending/delivery/status
curl -fsS -H 'Authorization: Bearer demo-staff' http://127.0.0.1:5284/api/lending/delivery/quarantine
curl -fsS -H 'Authorization: Bearer demo-staff' http://127.0.0.1:5284/api/pickup-desk/inbox/quarantine
```

The producer status reports the oldest pending age, pending messages, acknowledgements and quarantines. A per-message structured log includes the original message ID and hold ID. Subscribe to the activity source and meter named `EquipmentLibrary.Lending.Outbox` to collect producer spans and acknowledgement/failure counts. The original request trace parent is retained in Lending's outbox.

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
