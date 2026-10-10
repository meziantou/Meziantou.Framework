#!/usr/bin/env bash
# Verifies that the package is reproducible: packs the library twice from a clean state (bin/obj
# removed) and compares the payload of both packages file by file. The DLLs (deterministic compilation, embedded PDBs), XML
# documentation, nuspec, README, license and icon must be byte-identical. Only the OPC packaging metadata that NuGet
# randomizes for every pack (the core-properties part name and the relationship ids of _rels/.rels) and the zip entry
# timestamps are excluded.
# Usage: verify-reproducible-pack.sh [extra dotnet pack arguments...]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/Meziantou.Framework.Imaging"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

for run in 1 2; do
  rm -rf "$project/bin" "$project/obj"
  dotnet pack "$project" --configuration Release --output "$work/pack$run" "$@" >"$work/pack$run.log" 2>&1 || { cat "$work/pack$run.log" >&2; exit 1; }
  package="$(find "$work/pack$run" -name 'Meziantou.Framework.Imaging.*.nupkg' ! -name '*.symbols.nupkg' | head -n 1)"
  mkdir -p "$work/content$run"
  unzip -q "$package" -d "$work/content$run"
  rm -rf "$work/content$run/_rels" "$work/content$run/package"
done

if ! diff -r "$work/content1" "$work/content2"; then
  echo "error: two packs of the same sources produced different package content" >&2
  exit 1
fi

echo "Package content is reproducible:"
(cd "$work/content1" && find . -type f -print0 | sort -z | xargs -0 sha256sum)
