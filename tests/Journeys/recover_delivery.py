"""Kill and restart the app in a disposable eqlab-* Compose project."""
import argparse
import os
import subprocess
import time
from urllib.request import urlopen
from uuid import UUID, uuid4

from borrow_a_drill import Library


def compose(*args, stdin=None):
    return subprocess.run(["docker", "compose", *args], check=True, text=True,
                          input=stdin, stdout=subprocess.PIPE, timeout=120).stdout.strip()


def sql(query):
    return compose("exec", "-T", "postgres", "psql", "-XAtq", "-U", "library_admin",
                   "-d", "equipment", "-v", "ON_ERROR_STOP=1", stdin=query)


def eventually(description, probe, timeout=45):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = probe()
        if result:
            return result
        time.sleep(.2)
    raise AssertionError(f"Timed out waiting for {description}")


def ready(base):
    try:
        with urlopen(base + "/ready", timeout=2) as reply:
            return reply.status == 200
    except OSError:
        return False


def recover(base):
    project = os.environ.get("COMPOSE_PROJECT_NAME", "")
    if not project.startswith("eqlab-"):
        raise SystemExit("Run this crash experiment in a separate Compose project named eqlab-<name>.")
    api_container = compose("ps", "-q", "api")
    actual = subprocess.check_output(["docker", "inspect", "-f",
        '{{ index .Config.Labels "com.docker.compose.project" }}', api_container], text=True).strip()
    if actual != project:
        raise SystemExit("The API container must belong to the disposable project.")

    library = Library(base)
    _, item = library.request("POST", "/api/catalog/items", "staff",
                              {"title": "Recovery drill", "description": "Durable receipt proof."}, (201,))
    _, copy = library.request("POST", "/api/lending/copies", "staff",
                              {"catalogItemId": item["id"], "barcode": f"REPLAY-{uuid4().hex[:10]}"}, (201,))
    copy_id = str(UUID(copy["copyId"]))
    # Pause only this copy's acknowledgement, after the independent subscriber transaction commits.
    sql(f"""
        CREATE FUNCTION library_meta.pause_test_ack() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF NEW.delivered_at IS NOT NULL AND OLD.delivered_at IS NULL
               AND NEW.payload->'data'->>'copyId' = '{copy_id}' THEN
                PERFORM pg_sleep(100);
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER pause_test_ack BEFORE UPDATE ON lending.outbox
            FOR EACH ROW EXECUTE FUNCTION library_meta.pause_test_ack();
    """)
    try:
        _, hold = library.request("POST", "/api/lending/holds", "borrower-a", {"copyId": copy_id}, (201,))
        hold_id = str(UUID(hold["holdId"]))
        original = eventually("durable receipt and unacknowledged outbox", lambda: sql(f"""
            SELECT o.id FROM lending.outbox o JOIN pickup_desk.inbox i ON i.message_id = o.id
            WHERE o.payload->'data'->>'holdId' = '{hold_id}' AND o.delivered_at IS NULL
              AND o.lease_id IS NOT NULL
        """))
        original = str(UUID(original))
        compose("kill", "-s", "SIGKILL", "api")
    finally:
        sql("DROP TRIGGER IF EXISTS pause_test_ack ON lending.outbox; DROP FUNCTION IF EXISTS library_meta.pause_test_ack();")
        compose("start", "api")

    eventually("API readiness after SIGKILL", lambda: ready(base))
    eventually("expired lease and acknowledgement of original envelope", lambda: sql(
        f"SELECT 1 FROM lending.outbox WHERE id='{original}' AND delivered_at IS NOT NULL"))
    library.queue(copy_id, [hold_id])
    counts = sql(f"""SELECT count(*) || '/' || count(*) FILTER (WHERE completed_at IS NOT NULL)
        FROM pickup_desk.inbox WHERE message_id='{original}' AND subscriber_id='pickup-desk.queue.v1'""")
    assert counts == "1/1", counts
    print(f"PASS recovered message {original}: process killed after durable receipt, one completed receipt and one pickup.")

    # Corrupt a synthetic unresolved obligation. A healthy sibling must still drain.
    poison = str(uuid4())
    sql(f"""INSERT INTO lending.outbox(id,payload,occurred_at,subscribers,failure_count)
        SELECT '{poison}', jsonb_set(payload,'{{messageId}}',to_jsonb('{poison}'::text)),now(),
            ARRAY['test.missing-receiver'],4 FROM lending.outbox WHERE id='{original}'""")
    eventually("operator-visible quarantine", lambda: sql(
        f"SELECT 1 FROM lending.outbox WHERE id='{poison}' AND quarantined_at IS NOT NULL"))
    library.request("POST", f"/api/lending/holds/{hold_id}/cancel", "borrower-a")
    library.queue(copy_id, [])
    library.request("POST", f"/api/lending/delivery/quarantine/{poison}/redrive", "pickup", expected=(403,))
    # Restore the subscriber obligation before the operator requeues the record through the API.
    sql(f"UPDATE lending.outbox SET subscribers=ARRAY['pickup-desk.queue.v1'] WHERE id='{poison}'")
    library.request("POST", f"/api/lending/delivery/quarantine/{poison}/redrive", "staff", expected=(204,))
    eventually("redriven message acknowledgement", lambda: sql(
        f"SELECT 1 FROM lending.outbox WHERE id='{poison}' AND delivered_at IS NOT NULL"))
    eventually("stale placement consumed", lambda: sql(
        f"SELECT 1 FROM pickup_desk.inbox WHERE message_id='{poison}' AND completed_at IS NOT NULL"))
    library.queue(copy_id, [])
    assert sql(f"SELECT count(*) FROM lending.outbox_redrives WHERE message_id='{poison}'") == "1"
    print("PASS poison isolated, healthy cancel delivered, operator redrive audited and stale hold stayed closed.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="http://127.0.0.1:5284")
    recover(parser.parse_args().base.rstrip("/"))
