#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
fixture=tests/Fixtures/AnalyzerGuard/AnalyzerGuard.csproj
log=$(mktemp)
trap 'rm -f "$log"' EXIT

dotnet restore "$fixture" --locked-mode
if dotnet build "$fixture" --no-restore --no-incremental --configuration Release >"$log" 2>&1; then
    echo "Analyzer fixture compiled despite the planted rethrow." >&2
    exit 1
fi
if ! grep -Eq 'error CA2200:' "$log"; then
    cat "$log" >&2
    echo "Expected the analyzer to reject the stack trace reset with CA2200." >&2
    exit 1
fi
echo "Verified CA2200 rejects a rethrow that resets the original stack trace."
