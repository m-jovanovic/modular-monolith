-- Existing messages keep the subscribers captured under their original deployment.
ALTER TABLE lending.outbox ALTER COLUMN subscribers DROP DEFAULT;
