ALTER TABLE lending.outbox
    ADD COLUMN subscribers text[] NOT NULL DEFAULT ARRAY['pickup-desk.queue.v1']::text[],
    ADD COLUMN eligible_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN lease_id uuid,
    ADD COLUMN failure_count integer NOT NULL DEFAULT 0,
    ADD COLUMN last_failure text,
    ADD COLUMN delivered_at timestamptz,
    ADD COLUMN quarantined_at timestamptz,
    ADD COLUMN trace_parent text;

CREATE INDEX outbox_pending_by_time ON lending.outbox (eligible_at, occurred_at)
    WHERE delivered_at IS NULL AND quarantined_at IS NULL;
