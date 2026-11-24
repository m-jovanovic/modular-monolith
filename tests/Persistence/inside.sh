#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
export PGHOST="${PGHOST:-127.0.0.1}" PGUSER=postgres PGPASSWORD=fixture-only
export CATALOG_PASSWORD=catalog-fixture LENDING_PASSWORD=lending-fixture PICKUP_PASSWORD=pickup-fixture
createdb equipment_upgrade
createdb equipment_empty

# The first migration in each module is the oldest database shape supported by this reference.
prior=$(mktemp -d)
trap 'rm -rf -- "$prior"' EXIT
mkdir -p "$prior/infra"
cp -R infra/postgres "$prior/infra/"
for module in Catalog Lending PickupDesk; do
    mkdir -p "$prior/src/Modules/$module/Migrations"
    cp src/Modules/"$module"/Migrations/001_*.sql "$prior/src/Modules/$module/Migrations/"
done
export PGDATABASE=equipment_upgrade
"$prior/infra/postgres/migrate.sh" >/dev/null
psql -Xq -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO catalog.items VALUES ('4892f78a-d909-4a67-944a-4bdad8b9a001', 'Migration drill', 'Before publication flags');
INSERT INTO lending.copies (id, catalog_item_id, item_title, barcode, state, enrolled_at)
VALUES ('4892f78a-d909-4a67-944a-4bdad8b9a002', '4892f78a-d909-4a67-944a-4bdad8b9a001', 'Migration drill', 'PRE-001', 'available', now());
INSERT INTO pickup_desk.inbox (subscriber_id, message_id, payload)
VALUES ('migration-fixture', '4892f78a-d909-4a67-944a-4bdad8b9a003', '{}');
SQL

# Roles now exist cluster-wide. Separate databases exercise independent migration ledgers concurrently.
PGDATABASE=equipment_empty ./infra/postgres/migrate.sh >/dev/null &
empty_pid=$!
./infra/postgres/migrate.sh >/dev/null
wait "$empty_pid"
./infra/postgres/migrate.sh >/dev/null

one() { test "$(psql -XAtq -v ON_ERROR_STOP=1 -c "$1")" = 1; }
one "SELECT count(*) FROM catalog.items WHERE title = 'Migration drill' AND is_archived = false"
one "SELECT count(*) FROM lending.copies WHERE barcode = 'PRE-001' AND state = 'available'"
one "SELECT count(*) FROM pickup_desk.inbox WHERE subscriber_id = 'migration-fixture' AND attempts = 0"
ledger=$(psql -XAtq -c 'SELECT count(*) FROM library_meta.migrations')
test "$ledger" = "$(PGDATABASE=equipment_empty psql -XAtq -c 'SELECT count(*) FROM library_meta.migrations')"
test "$(PGDATABASE=equipment_empty psql -XAtq -c 'SELECT count(*) FROM catalog.items')" = 0
echo "Validated empty and upgraded databases, preserved prior rows and replayed $ledger migration entries."

denied() {
    if psql -Xq -v ON_ERROR_STOP=1 -v VERBOSITY=verbose -c "$1" > "$prior/permission-result" 2>&1; then
        echo "$PGUSER unexpectedly executed: $1" >&2
        exit 1
    fi
    grep -q '42501:' "$prior/permission-result" || { cat "$prior/permission-result" >&2; exit 1; }
}

for schema in catalog lending pickup_desk; do
    PGUSER=postgres PGPASSWORD=fixture-only psql -Xq -v ON_ERROR_STOP=1 -c "BEGIN; SET LOCAL ROLE ${schema}_owner; CREATE TABLE $schema.migration_rule_probe (id integer); ROLLBACK"
    PGUSER=postgres PGPASSWORD=fixture-only psql -Xq -v ON_ERROR_STOP=1 -c "SET ROLE ${schema}_owner; CREATE VIEW $schema.owner_probe AS SELECT 1 AS id"
done

for schema in catalog lending pickup_desk; do
    export PGUSER="${schema}_app"
    case "$schema" in
        catalog) PGPASSWORD=$CATALOG_PASSWORD; table=catalog.items
            write="INSERT INTO catalog.items (id, title, description) VALUES (gen_random_uuid(), 'Test', 'Fixture')" ;;
        lending) PGPASSWORD=$LENDING_PASSWORD; table=lending.copies
            write="INSERT INTO lending.copies (id, catalog_item_id, item_title, barcode, state, enrolled_at) VALUES (gen_random_uuid(), gen_random_uuid(), 'Test', 'ROLE-001', 'available', now())" ;;
        pickup_desk) PGPASSWORD=$PICKUP_PASSWORD; table=pickup_desk.inbox
            write="INSERT INTO pickup_desk.inbox (subscriber_id, message_id, payload) VALUES ('fixture', gen_random_uuid(), '{}')" ;;
    esac
    export PGPASSWORD
    test "$(psql -XAtq -c 'SELECT current_user')" = "$PGUSER"
    psql -Xq -v ON_ERROR_STOP=1 -c "BEGIN; $write; SELECT * FROM $table LIMIT 1; ROLLBACK" >/dev/null
    denied "CREATE TABLE $schema.forbidden_ddl (id integer)"
    denied "CREATE TABLE public.forbidden_ddl (id integer)"
    denied "SELECT * FROM library_meta.migrations"
    denied "SET ROLE ${schema}_owner"
    for target in catalog.items lending.copies pickup_desk.inbox; do
        target_schema=${target%%.*}
        [ "$target_schema" != "$schema" ] || continue
        denied "SELECT * FROM $target LIMIT 1"
        denied "DELETE FROM $target WHERE FALSE"
        denied "SELECT * FROM $table own CROSS JOIN $target peer LIMIT 1"
        denied "SELECT * FROM $target_schema.owner_probe"
    done
    echo "$PGUSER passed own reads/writes and rejected cross-schema reads, writes, joins, views and DDL."
done

export PGUSER=postgres PGPASSWORD=fixture-only
echo '-- changed historical migration' >> "$prior/src/Modules/Catalog/Migrations/001_items.sql"
if "$prior/infra/postgres/migrate.sh" >"$prior/checksum-result" 2>&1; then
    echo 'Expected historical migration mutation to fail' >&2
    exit 1
fi
grep -q 'Previously applied migration has changed' "$prior/checksum-result"
echo 'Previously applied migration edits are rejected.'
