CREATE TABLE pickup_desk.redrives (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    subscriber_id text NOT NULL,
    message_id uuid NOT NULL,
    operator_id uuid NOT NULL,
    reason text NOT NULL CHECK (length(reason) BETWEEN 1 AND 500),
    previous_failure text NOT NULL,
    redriven_at timestamptz NOT NULL,
    FOREIGN KEY (subscriber_id, message_id) REFERENCES pickup_desk.inbox (subscriber_id, message_id)
);
