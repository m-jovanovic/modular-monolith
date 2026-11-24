#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."
copy_suite() {
    tar -cf - infra/postgres src/Modules/{Catalog,Lending,PickupDesk}/Migrations tests/Persistence/inside.sh
}

# Reuse a running PostgreSQL development container only as a process host. The new
# cluster has a separate data directory, socket, users and databases.
if [[ -n "${EQLIB_TEST_PG_CONTAINER:-}" ]]; then
    host=$EQLIB_TEST_PG_CONTAINER
    sandbox=$(docker exec --user postgres "$host" mktemp -d /tmp/equipment-proof.XXXXXXXX)
    cleanup() {
        docker exec --user postgres "$host" pg_ctl -D "$sandbox/data" -m fast -w stop >/dev/null 2>&1 || true
        docker exec --user postgres "$host" rm -rf -- "$sandbox"
    }
    trap cleanup EXIT
    docker exec --user postgres "$host" sh -eu -c '
        mkdir "$1/source" "$1/socket"
        printf "%s\n" fixture-only > "$1/password"
        initdb -D "$1/data" -U postgres --auth-local=scram-sha-256 --auth-host=scram-sha-256 --pwfile="$1/password" >/dev/null
        pg_ctl -D "$1/data" -l "$1/postgres.log" -o "-c listen_addresses= -k $1/socket" -w start >/dev/null
    ' sh "$sandbox"
    copy_suite | docker exec --interactive --user postgres "$host" tar -xf - -C "$sandbox/source"
    docker exec --user postgres --env "PGHOST=$sandbox/socket" "$host" sh "$sandbox/source/tests/Persistence/inside.sh"
    exit
fi

# Docker assigns a unique ID and keeps the test cluster isolated from any running application.
cluster=$(docker run --detach --rm --env POSTGRES_PASSWORD=fixture-only \
    --env 'POSTGRES_INITDB_ARGS=--auth-local=scram-sha-256 --auth-host=scram-sha-256' postgres:17.12-alpine3.23)
cleanup_cluster() {
    result=$?
    if (( result != 0 )); then docker logs --tail 80 "$cluster" >&2; fi
    docker rm --force "$cluster" >/dev/null 2>&1 || true
}
trap cleanup_cluster EXIT
for attempt in {1..60}; do
    if docker exec "$cluster" pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1; then break; fi
    sleep 1
done
docker exec "$cluster" pg_isready -h 127.0.0.1 -U postgres >/dev/null
docker exec "$cluster" mkdir /library-check
copy_suite |
    docker exec --interactive "$cluster" tar -xf - -C /library-check
docker exec "$cluster" sh /library-check/tests/Persistence/inside.sh
