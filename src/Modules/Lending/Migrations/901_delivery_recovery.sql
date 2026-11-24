CREATE TABLE lending.outbox_redrives (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    message_id uuid NOT NULL REFERENCES lending.outbox(id),
    operator_id uuid NOT NULL,
    redriven_at timestamptz NOT NULL DEFAULT now()
);
