CREATE TABLE IF NOT EXISTS catalog.items (
    id uuid PRIMARY KEY,
    title varchar(160) NOT NULL CHECK (length(btrim(title)) > 0),
    description varchar(2000) NOT NULL CHECK (length(btrim(description)) > 0)
);
