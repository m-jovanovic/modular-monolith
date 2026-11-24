#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${CATALOG_PASSWORD:?}" "${LENDING_PASSWORD:?}" "${PICKUP_PASSWORD:?}"
psql -X -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('equipment-library-bootstrap'), hashtext('migration-ledger'));
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA IF NOT EXISTS library_meta;
CREATE TABLE IF NOT EXISTS library_meta.migrations (
    module text NOT NULL,
    script text NOT NULL,
    checksum text NOT NULL,
    applied_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (module, script)
);
COMMIT;
SQL
for module in Catalog Lending PickupDesk; do
    case "$module" in
        Catalog) schema=catalog; password=$CATALOG_PASSWORD ;;
        Lending) schema=lending; password=$LENDING_PASSWORD ;;
        PickupDesk) schema=pickup_desk; password=$PICKUP_PASSWORD ;;
    esac
    owner="${schema}_owner"
    MODULE_PASSWORD="$password" psql -X -d "${EQLIB_ROLE_DATABASE:-postgres}" -v ON_ERROR_STOP=1 -v owner="$owner" -v app="${schema}_app" -f infra/postgres/roles.sql
    psql -X -v ON_ERROR_STOP=1 -v module="$module" -v schema="$schema" -v owner="$owner" -v app="${schema}_app" -f infra/postgres/module.sql
    for file in src/Modules/"$module"/Migrations/[0-9]*.sql; do
        [ -f "$file" ] || continue
        checksum=$(sha256sum "$file" | cut -d' ' -f1)
        psql -X -v ON_ERROR_STOP=1 -v module="$module" -v owner="$owner" -v script="$(basename "$file")" -v file="$file" -v checksum="$checksum" -f infra/postgres/apply.sql
    done
done
