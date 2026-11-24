ALTER TABLE catalog.items ADD COLUMN IF NOT EXISTS is_archived boolean NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS ix_items_publication_title ON catalog.items (is_archived, lower(title), id);
