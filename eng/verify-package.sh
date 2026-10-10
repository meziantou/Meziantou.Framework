#!/usr/bin/env bash
# Verifies the content of the Meziantou.Framework.Imaging package:
# - library assets exist for exactly net10.0 and net11.0 (no older .NET, .NET Standard or .NET Framework assets)
# - no package dependency (fully managed, no native or third-party runtime dependency)
# - MIT license expression, README and LICENSE are included, project/repository metadata is set
# - optionally (release builds): the expected version, and the commit the package was built from (EXPECTED_VERSION and
#   EXPECTED_COMMIT environment variables)
set -euo pipefail

package="${1:?Usage: verify-package.sh <path-to-nupkg>}"
listing="$(unzip -Z1 "$package")"

frameworks="$(echo "$listing" | grep -E '^lib/' | cut -d/ -f2 | sort -u | tr '\n' ' ')"
if [[ "$frameworks" != "net10.0 net11.0 " ]]; then
  echo "error: unexpected lib target frameworks: '$frameworks' (expected 'net10.0 net11.0')" >&2
  exit 1
fi

if echo "$listing" | grep -qE '^(runtimes|native|build|buildTransitive|analyzers|contentFiles)/'; then
  echo "error: the package must not contain native, build, analyzer or content assets" >&2
  echo "$listing" >&2
  exit 1
fi

nuspec="$(unzip -p "$package" '*.nuspec')"
if echo "$nuspec" | grep -q '<dependency '; then
  echo "error: the package must not have dependencies" >&2
  echo "$nuspec" >&2
  exit 1
fi

if ! echo "$nuspec" | grep -q '<license type="expression">MIT</license>'; then
  echo "error: the package license must be the MIT expression" >&2
  exit 1
fi

for element in '<readme>README.md</readme>' '<projectUrl>https://github.com/meziantou/Meziantou.Framework</projectUrl>' '<repository type="git"'; do
  if ! echo "$nuspec" | grep -qF "$element"; then
    echo "error: the nuspec must contain '$element'" >&2
    echo "$nuspec" >&2
    exit 1
  fi
done

if [[ -n "${EXPECTED_VERSION:-}" ]] && ! echo "$nuspec" | grep -qF "<version>$EXPECTED_VERSION</version>"; then
  echo "error: the package version is not '$EXPECTED_VERSION'" >&2
  exit 1
fi

if [[ -n "${EXPECTED_COMMIT:-}" ]] && ! echo "$nuspec" | grep -qF "commit=\"$EXPECTED_COMMIT\""; then
  echo "error: the package does not record the source commit '$EXPECTED_COMMIT' (SourceLink repository metadata)" >&2
  echo "$nuspec" >&2
  exit 1
fi

for file in README.md LICENSE.txt lib/net10.0/Meziantou.Framework.Imaging.xml lib/net11.0/Meziantou.Framework.Imaging.xml; do
  if ! echo "$listing" | grep -qx "$file"; then
    echo "error: missing '$file' in the package" >&2
    exit 1
  fi
done

echo "Package '$package' is valid (lib: $frameworks)"
