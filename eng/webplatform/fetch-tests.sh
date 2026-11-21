#!/usr/bin/env bash
# Materializes tests/Meziantou.Framework.WebPlatform.External/* at the commits pinned in tests/Meziantou.Framework.WebPlatform.SpecData/versions.json and the paths of tests/Meziantou.Framework.WebPlatform.SpecData/sources.json.
# Arguments are passed to fetch-tests: --publish publishes the snapshots of the mirror (tools/Meziantou.Framework.TestCorpus/readme.md).
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dotnet run --project "$root/tools/Meziantou.Framework.WebPlatform.SpecUpdate" -c Release -- fetch-tests "$@"
