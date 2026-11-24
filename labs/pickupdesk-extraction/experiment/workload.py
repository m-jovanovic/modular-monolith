"""Run the same public write workload with and without an independently stopped consumer."""
import time

from database import sql
from processes import MONOLITH, PICKUP, PUBLISHER, eventually, free_port, launch, reachable, request, stop

PLACEMENTS = 24
CANCELLATIONS = 12


def active_count(reader):
    return request(reader, "/api/pickup-desk/workload")["unclaimed"]


def backlog(database):
    return int(sql(database, "SELECT count(*) FROM lending.outbox WHERE delivered_at IS NULL"))


def place_holds(writer):
    item = request(writer, "/api/catalog/items", method="POST", data={"title": "Cordless drill", "description": "18 V drill"})
    holds = []
    for number in range(PLACEMENTS):
        copy = request(writer, "/api/lending/copies", method="POST", data={"catalogItemId": item["id"], "barcode": f"LAB-{number:03}"})
        hold = request(writer, "/api/lending/holds", method="POST", token="demo-borrower-a", data={"copyId": copy["copyId"]})
        holds.append(hold["holdId"])
    return holds


def measure_healthy_projection(database):
    measured = sql(database, """
        SELECT round(percentile_cont(0.5) WITHIN GROUP (ORDER BY elapsed)::numeric, 1),
               round(percentile_cont(0.95) WITHIN GROUP (ORDER BY elapsed)::numeric, 1)
        FROM (SELECT 1000 * extract(epoch FROM completed_at - (payload->>'occurredAt')::timestamptz) AS elapsed
              FROM pickup_desk.inbox WHERE completed_at IS NOT NULL) durations
        """)
    return [float(part) for part in measured.split("|")]


def run(extracted, databases, broker_url, cleanup, scratch):
    writer = f"http://127.0.0.1:{free_port()}"
    reader = writer
    if extracted:
        source, writer_env = databases.create("publisher", ("Catalog", "Lending"))
        projection, reader_env = databases.create("pickup", ("PickupDesk",))
        reader = f"http://127.0.0.1:{free_port()}"
        writer_env["BrokerUrl"] = broker_url
        reader_env["BrokerUrl"] = broker_url
        launch(PUBLISHER, writer, writer_env, cleanup, scratch)
        consumer = launch(PICKUP, reader, reader_env, cleanup, scratch)
    else:
        source, writer_env = databases.create("baseline", ("Catalog", "Lending", "PickupDesk"))
        projection = source
        consumer = launch(MONOLITH, writer, writer_env, cleanup, scratch)

    holds = place_holds(writer)
    eventually(lambda: active_count(reader) == PLACEMENTS, "Initial holds did not reach the queue")
    p50, p95 = measure_healthy_projection(projection)
    stop(consumer)
    source_alive = reachable(writer)
    assert source_alive == extracted, "Profile had unexpected failure isolation"

    if not extracted:
        # The stock host pauses both delivery workers. The API still commits new holds and cancellations.
        paused = launch(MONOLITH, writer, {**writer_env, "Workers__Enabled": "false"}, cleanup, scratch)
    for hold_id in holds[:CANCELLATIONS]:
        request(writer, f"/api/lending/holds/{hold_id}/cancel", method="POST", token="demo-borrower-a")
    if extracted:
        eventually(lambda: backlog(source) == 0, "Publisher did not receive storage acknowledgements")
        broker_lag = request(writer, "/lab/broker-backlog")["pending"]
        assert broker_lag == CANCELLATIONS, broker_lag
    else:
        broker_lag = 0
    outbox_lag = backlog(source)
    assert outbox_lag == (0 if extracted else CANCELLATIONS), outbox_lag
    before = int(sql(projection, "SELECT count(*) FROM pickup_desk.holds WHERE hold_state = 'held'"))
    assert before == PLACEMENTS, before

    recovery_start = time.monotonic()
    if extracted:
        launch(PICKUP, reader, reader_env, cleanup, scratch)
    else:
        stop(paused)
        launch(MONOLITH, writer, writer_env, cleanup, scratch)
    eventually(lambda: active_count(reader) == PLACEMENTS - CANCELLATIONS, "Queue did not recover")
    recovery = round((time.monotonic() - recovery_start) * 1000)
    history = request(reader, "/api/pickup-desk/history")["items"]
    assert {row["holdId"] for row in history} == set(holds[:CANCELLATIONS]), history
    receipts = int(sql(projection, "SELECT count(*) FROM pickup_desk.inbox WHERE completed_at IS NOT NULL"))
    assert receipts == PLACEMENTS + CANCELLATIONS, receipts
    if extracted:
        eventually(lambda: request(writer, "/lab/broker-backlog") == {"pending": 0, "awaitingAck": 0}, "Broker did not drain")
    return {"profile": "extracted" if extracted else "baseline", "healthy_p50_ms": p50, "healthy_p95_ms": p95,
            "source_live_while_process_stopped": source_alive, "paused_outbox": outbox_lag, "paused_broker": broker_lag,
            "queue_before_resume": before, "queue_after_resume": PLACEMENTS - CANCELLATIONS,
            "restart_and_catchup_ms": recovery, "completed_unique_receipts": receipts}
