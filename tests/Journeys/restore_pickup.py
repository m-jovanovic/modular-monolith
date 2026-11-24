"""Restore a consistent backup into a new database and verify live and terminal holds."""
import argparse
from uuid import UUID, uuid4

from borrow_a_drill import Library
from recover_delivery import compose, eventually, sql


def rehearse(base):
    library = Library(base)
    _, item = library.request("POST", "/api/catalog/items", "staff",
                              {"title": "Backup drill", "description": "Projection restore check."}, (201,))
    _, copy = library.request("POST", "/api/lending/copies", "staff",
                              {"catalogItemId": item["id"], "barcode": f"BACKUP-{uuid4().hex[:12]}"}, (201,))
    copy_id = str(UUID(copy["copyId"]))
    _, closed = library.request("POST", "/api/lending/holds", "borrower-a", {"copyId": copy_id}, (201,))
    closed_id = str(UUID(closed["holdId"]))
    library.request("POST", f"/api/lending/holds/{closed_id}/cancel", "borrower-a")
    _, active = library.request("POST", "/api/lending/holds", "borrower-b", {"copyId": copy_id}, (201,))
    active_id = str(UUID(active["holdId"]))
    library.queue(copy_id, [active_id])

    snapshot = f"""
        SELECT count(*) FROM pickup_desk.holds WHERE copy_id='{copy_id}' AND
            ((hold_id='{active_id}' AND hold_state='held' AND sequence=1) OR
             (hold_id='{closed_id}' AND hold_state='cancelled' AND sequence=2));
        SELECT count(*) FROM lending.holds WHERE copy_id='{copy_id}' AND
            ((id='{active_id}' AND state='held') OR (id='{closed_id}' AND state='cancelled'));
        SELECT count(*) FROM lending.outbox o JOIN pickup_desk.inbox i ON i.message_id=o.id
            WHERE o.payload->'data'->>'copyId'='{copy_id}' AND o.delivered_at IS NOT NULL
              AND i.completed_at IS NOT NULL AND i.subscriber_id='pickup-desk.queue.v1';
    """
    eventually("both hold states, three acknowledgements and completed receipts",
               lambda: sql(snapshot) == "2\n2\n3")
    # Model a retained history window whose lower bound falls after these transitions.
    cutoff = sql("SELECT clock_timestamp()")
    database = "restore_" + uuid4().hex
    compose("exec", "-T", "postgres", "createdb", "-U", "library_admin", database)
    try:
        # Both databases live on the same disposable cluster so PostgreSQL roles already exist.
        compose("exec", "-T", "postgres", "sh", "-ec", """
            set -o pipefail
            pg_dump -U library_admin -Fc equipment | pg_restore -U library_admin --exit-on-error --dbname="$1"
        """, "restore-pickup", database)

        def restored(statement):
            return compose("exec", "-T", "postgres", "psql", "-XAtq", "-U", "library_admin",
                           "-v", "ON_ERROR_STOP=1", "-v", f"cutoff={cutoff}", "-d", database, stdin=statement)

        assert restored(snapshot) == "2\n2\n3"
        assert restored(f"""SELECT count(*) FROM lending.outbox WHERE
            payload->'data'->>'copyId'='{copy_id}' AND occurred_at > :'cutoff'::timestamptz""") == "0"
        assert restored(f"""SET ROLE pickup_desk_app; SELECT hold_id FROM pickup_desk.holds
            WHERE copy_id='{copy_id}' AND hold_state='held'""") == active_id
        assert restored("SELECT count(*) FROM library_meta.migrations") == sql("SELECT count(*) FROM library_meta.migrations")
        print(f"PASS restored active hold {active_id}, closed hold {closed_id}, all receipts and migration ledger.")
        print("The bounded event window contains zero events for this copy. Its active pickup survived the restore.")
    finally:
        compose("exec", "-T", "postgres", "dropdb", "-U", "library_admin", database)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="http://127.0.0.1:5284")
    rehearse(parser.parse_args().base)
