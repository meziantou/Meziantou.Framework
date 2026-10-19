#!/usr/bin/env bash
# Updates every specification input and test suite to its latest revision, regenerates code and runs the conformance suites.
#   eng/webplatform/update-specs.sh            update everything
#   eng/webplatform/update-specs.sh encoding   update only the given sources
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"

dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -c Release -- update "$@"
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -c Release -- fetch-tests
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecGen -c Release
# Meziantou.Framework.Uri is generated from the same public suffix list (exit code 1 means that its files changed)
dotnet run --project tools/Meziantou.Framework.Uri.PublicSuffix.Generator || [ $? -eq 1 ]
dotnet test --project tests/Meziantou.Framework.WebPlatform.Tests -c Release
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -c Release -- wpt-coverage
git status --short
