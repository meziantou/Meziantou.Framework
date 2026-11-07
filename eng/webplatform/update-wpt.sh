#!/usr/bin/env bash
# Updates the pinned web-platform-tests to the latest release, rewrites the WPT expectations from the new results and
# regenerates the WPT coverage, so the diff shows the tests whose result changed.
# Exits with 1 when a test of Meziantou.Framework.WebPlatform.Tests fails or when a test that does not pass has no reason.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"

dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -c Release -- update wpt
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -c Release -- fetch-tests
dotnet build slnx/Meziantou.Framework.WebPlatform.slnx -c Release

status=0
dotnet test --project tests/Meziantou.Framework.WebPlatform.Tests -c Release --no-build || status=1
# The expectations are written by a single target framework: the processes of the target frameworks run concurrently
WPT_UPDATE_EXPECTATIONS=1 dotnet test --project tests/Meziantou.Framework.WebPlatform.Wpt -c Release --no-build --framework net11.0
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -c Release -- wpt-coverage
dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -c Release -- wpt-coverage --check || status=1
git status --short
exit $status
