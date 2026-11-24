"""Follow a caller-supplied W3C trace into durable delivery and the committed staff queue."""
import argparse
import secrets
from uuid import UUID, uuid4

from borrow_a_drill import Library
from recover_delivery import compose, eventually, sql


def trace(base, producer_query=sql, pickup_query=sql, host_logs=None, pickup_base=None):
    api = Library(base)
    trace_id = uuid4().hex
    caller_span = secrets.token_hex(8)
    _, item = api.request("POST", "/api/catalog/items", "staff",
                          {"title": "Trace drill", "description": "Tracing a durable handoff."}, (201,))
    _, copy = api.request("POST", "/api/lending/copies", "staff",
                          {"catalogItemId": item["id"], "barcode": f"TRACE-{uuid4().hex[:12]}"}, (201,))
    _, held = api.request("POST", "/api/lending/holds", "borrower-a", {"copyId": copy["copyId"]}, (201,),
                          traceparent=f"00-{trace_id}-{caller_span}-01")
    hold = str(UUID(held["holdId"]))
    Library(pickup_base or base).queue(copy["copyId"], [hold])
    producer = eventually("acknowledged traced envelope", lambda: producer_query(f"""
        SELECT id || '|' || trace_parent FROM lending.outbox
        WHERE payload->'data'->>'holdId' = '{hold}' AND delivered_at IS NOT NULL
    """))
    message_text, request_parent = producer.split("|")
    message = str(UUID(message_text))
    receipt_parent = eventually("completed traced receipt", lambda: pickup_query(f"""
        SELECT trace_parent FROM pickup_desk.inbox
        WHERE message_id = '{message}' AND subscriber_id = 'pickup-desk.queue.v1' AND completed_at IS NOT NULL
    """))
    request_fields, receipt_fields = request_parent.split("-"), receipt_parent.split("-")
    assert request_fields[1] == receipt_fields[1] == trace_id
    assert len({caller_span, request_fields[2], receipt_fields[2]}) == 3

    def has_logs():
        logs = host_logs() if host_logs else compose("logs", "--no-color", "api")
        matches = [line for line in logs.splitlines() if trace_id in line and message in line and hold in line]
        return any("Acknowledged hold message" in line for line in matches) and any(
            "Pickup receipt" in line and "completed" in line for line in matches)

    eventually("correlated producer and consumer diagnostic logs", has_logs)
    print(f"PASS trace {trace_id}: hold {hold}, message {message}, distinct request and relay spans, completed receipt.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="http://127.0.0.1:5284")
    trace(parser.parse_args().base)
