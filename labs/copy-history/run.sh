#!/usr/bin/env bash
set -euo pipefail
lab=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$lab/../.." && pwd)

if [[ -n "${EQLIB_LAB_PG_URL:-}" ]]; then
  client=(psql -Xq -v ON_ERROR_STOP=1 "$EQLIB_LAB_PG_URL")
else
  client=(docker compose --project-directory "$repo" exec -T postgres psql -Xq -v ON_ERROR_STOP=1 -U library_admin -d equipment)
fi

{
  echo 'BEGIN;'
  cat "$lab/store.sql"
  echo 'CREATE TEMP TABLE fixture_event (position bigint GENERATED ALWAYS AS IDENTITY, entry jsonb);'
  echo 'COPY fixture_event(entry) FROM STDIN;'
  cat "$lab/fixtures/drill.ndjson"
  echo '\.'
  echo 'CREATE TEMP TABLE conventional_audit (version integer PRIMARY KEY, state text, barcode text, loans integer);'
  echo 'COPY conventional_audit FROM STDIN WITH (FORMAT csv, HEADER true);'
  cat "$lab/fixtures/audit.csv"
  echo '\.'
  cat "$lab/verify.sql"
} | "${client[@]}"
