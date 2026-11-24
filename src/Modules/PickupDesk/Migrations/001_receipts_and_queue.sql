CREATE TABLE pickup_desk.inbox (
    subscriber_id text NOT NULL,
    message_id uuid NOT NULL,
    payload jsonb NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    available_at timestamptz NOT NULL DEFAULT now(),
    attempts integer NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    completed_at timestamptz,
    quarantined_at timestamptz,
    failure text,
    PRIMARY KEY (subscriber_id, message_id),
    CHECK (completed_at IS NULL OR quarantined_at IS NULL)
);

CREATE INDEX inbox_pending ON pickup_desk.inbox (available_at, received_at, message_id)
    WHERE completed_at IS NULL AND quarantined_at IS NULL;

CREATE TABLE pickup_desk.holds (
    hold_id uuid PRIMARY KEY,
    copy_id uuid NOT NULL,
    catalog_item_id uuid NOT NULL,
    barcode text NOT NULL,
    item_title text,
    hold_state text NOT NULL CHECK (hold_state IN ('held', 'cancelled', 'collected')),
    sequence bigint NOT NULL CHECK (sequence > 0),
    last_message_id uuid NOT NULL,
    occurred_at timestamptz NOT NULL,
    assigned_to uuid,
    ready_at timestamptz,
    issue text,
    CHECK (issue IS NULL OR length(issue) BETWEEN 1 AND 500),
    CHECK (ready_at IS NULL OR (assigned_to IS NOT NULL AND issue IS NULL))
);

CREATE INDEX holds_active ON pickup_desk.holds (occurred_at, hold_id) WHERE hold_state = 'held';
CREATE INDEX holds_history ON pickup_desk.holds (occurred_at DESC, hold_id) WHERE hold_state <> 'held';
