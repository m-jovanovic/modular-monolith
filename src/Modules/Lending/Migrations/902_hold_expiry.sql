ALTER TABLE lending.holds
    ADD COLUMN expired boolean NOT NULL DEFAULT false,
    ADD CONSTRAINT expired_hold_is_cancelled CHECK (NOT expired OR state = 'cancelled');

CREATE INDEX due_pickups ON lending.holds (placed_at, id) WHERE state = 'held';
