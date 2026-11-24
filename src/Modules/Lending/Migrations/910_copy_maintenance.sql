ALTER TABLE lending.copies DROP CONSTRAINT copies_state_check;
ALTER TABLE lending.copies ADD CHECK (state IN ('available', 'held', 'on-loan', 'maintenance', 'retired'));

CREATE TABLE lending.maintenance_orders (
    id uuid PRIMARY KEY,
    copy_id uuid NOT NULL REFERENCES lending.copies(id),
    reason text NOT NULL CHECK (length(reason) BETWEEN 1 AND 500),
    opened_at timestamptz NOT NULL,
    outcome text CHECK (outcome IN ('repaired', 'withdrawn')),
    resolution text CHECK (length(resolution) BETWEEN 1 AND 500),
    closed_at timestamptz,
    CHECK ((outcome IS NULL) = (closed_at IS NULL)),
    CHECK ((resolution IS NULL) = (closed_at IS NULL))
);
CREATE UNIQUE INDEX one_open_maintenance_order ON lending.maintenance_orders (copy_id) WHERE closed_at IS NULL;
CREATE INDEX maintenance_worklist ON lending.maintenance_orders (opened_at, id) WHERE closed_at IS NULL;
