CREATE TABLE lending.copies (
    id uuid PRIMARY KEY,
    catalog_item_id uuid NOT NULL,
    item_title text NOT NULL,
    barcode text NOT NULL UNIQUE CHECK (length(barcode) BETWEEN 1 AND 80),
    state text NOT NULL CHECK (state IN ('available', 'held', 'on-loan', 'retired')),
    enrolled_at timestamptz NOT NULL
);

CREATE INDEX copies_by_item ON lending.copies (catalog_item_id, state, id);

CREATE TABLE lending.holds (
    id uuid PRIMARY KEY,
    copy_id uuid NOT NULL REFERENCES lending.copies (id),
    borrower_id uuid NOT NULL,
    state text NOT NULL CHECK (state IN ('held', 'cancelled', 'collected')),
    placed_at timestamptz NOT NULL,
    closed_at timestamptz,
    CHECK ((state = 'held') = (closed_at IS NULL))
);

CREATE UNIQUE INDEX one_active_hold_per_copy ON lending.holds (copy_id) WHERE state = 'held';
CREATE INDEX borrower_holds ON lending.holds (borrower_id, placed_at DESC, id);
CREATE INDEX copy_holds ON lending.holds (copy_id, placed_at DESC, id);

CREATE TABLE lending.loans (
    id uuid PRIMARY KEY REFERENCES lending.holds (id),
    copy_id uuid NOT NULL REFERENCES lending.copies (id),
    borrower_id uuid NOT NULL,
    checked_out_at timestamptz NOT NULL,
    due_at timestamptz NOT NULL,
    renewals smallint NOT NULL DEFAULT 0 CHECK (renewals BETWEEN 0 AND 1),
    returned_at timestamptz,
    CHECK (due_at > checked_out_at)
);

CREATE UNIQUE INDEX one_open_loan_per_copy ON lending.loans (copy_id) WHERE returned_at IS NULL;
CREATE INDEX borrower_loans ON lending.loans (borrower_id, checked_out_at DESC, id);
CREATE INDEX loans_due ON lending.loans (due_at, id) WHERE returned_at IS NULL;

CREATE TABLE lending.outbox (
    id uuid PRIMARY KEY,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL
);
