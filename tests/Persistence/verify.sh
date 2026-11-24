#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.."
# Docker assigns a unique ID and keeps the test cluster isolated from any running application.
cluster=$(docker run --detach --rm --env POSTGRES_PASSWORD=fixture-only postgres:17.12-alpine3.23)
trap 'docker rm --force "$cluster" >/dev/null 2>&1 || true' EXIT
for attempt in {1..60}; do
    if docker exec "$cluster" pg_isready -U postgres >/dev/null 2>&1; then break; fi
    sleep 1
done
docker exec "$cluster" pg_isready -U postgres >/dev/null
docker exec "$cluster" mkdir /library-check
tar -cf - infra/postgres src/Modules/{Catalog,Lending,PickupDesk}/Migrations tests/Persistence/inside.sh |
    docker exec --interactive "$cluster" tar -xf - -C /library-check
docker exec "$cluster" sh /library-check/tests/Persistence/inside.sh
