#!/usr/bin/env bash
set -euo pipefail

if (( $# > 1 )); then
    echo "Usage: $0 [solution-or-test-project]" >&2
    exit 2
fi

cd "$(dirname "$0")/.."
target=${1:-EquipmentLibrary.slnx}
dotnet restore "$target" --locked-mode
dotnet build "$target" --configuration Release --no-restore
dotnet test "$target" --configuration Release --no-build
