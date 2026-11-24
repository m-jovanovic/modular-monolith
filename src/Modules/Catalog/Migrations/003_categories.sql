CREATE TABLE IF NOT EXISTS catalog.categories (
    id uuid PRIMARY KEY,
    name varchar(80) NOT NULL CHECK (length(btrim(name)) > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_categories_name ON catalog.categories (lower(name));

CREATE TABLE IF NOT EXISTS catalog.item_categories (
    item_id uuid NOT NULL REFERENCES catalog.items(id),
    category_id uuid NOT NULL REFERENCES catalog.categories(id),
    PRIMARY KEY (item_id, category_id)
);
CREATE INDEX IF NOT EXISTS ix_item_categories_category ON catalog.item_categories (category_id, item_id);
