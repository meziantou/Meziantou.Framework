# Meziantou.Framework.JavaScriptEngine.Data.Generator

Generates the data embedded in `Meziantou.Framework.JavaScriptEngine`, so that the engine computes the same results on
every operating system and in invariant-globalization mode (no `TimeZoneInfo`, no ICU):

- `src/Meziantou.Framework.JavaScriptEngine/TimeZones/tzdb.bin`: the IANA time zone database, compiled by a small zic
  of this tool, and the CLDR mapping of the Windows time zone identifiers (`windowsZones.xml`, territory `001`). The
  file is Brotli-compressed; `TimeZoneDatabase` reads it.
- `src/Meziantou.Framework.JavaScriptEngine/TimeZones/TimeZoneDatabase.g.cs`: the versions of the data.

The versions are pinned, with the SHA-256 of the downloaded files, in `data-versions.json`. The files are downloaded
in memory and never committed.

## How the time zone database is compiled

The source files `africa`, `antarctica`, `asia`, `australasia`, `europe`, `northamerica`, `southamerica`,
`etcetera` and `backward` are read in the main data form, then `backzone` with the zic build options
`PACKRATDATA=backzone PACKRATLIST=zone.tab` (as ICU does, and as ECMA-402 recommends): each country of `zone.tab` has
its own zone, with its history before 1970 (e.g. `Europe/Amsterdam` is a zone with its LMT offset of +0:19:32, not a
link to `Europe/Brussels`). The `factory` zone is not included.

For each zone, the file contains the transitions (in seconds, the offsets keep their seconds) until the year after
the last year a non-recurring rule applies, then the rules whose TO field is `max`: the engine computes the later
transitions from them, until year +275760. The links resolve to their zone. ECMA-402 primary identifiers: a link
resolves to its zone, and `Etc/UTC`, `Etc/GMT` and their links to `UTC`.

## Usage

```shell
dotnet run --project tools/Meziantou.Framework.JavaScriptEngine.Data.Generator [-- [--update] [--verify]]
```

- `--update`: moves the pins to the latest tzdata release and the latest CLDR release before generating.
- `--verify`: compiles the same source with the `zic` of the system (`zic -b fat`) and compares the transitions of
  every zone and link until 2037 with the ones the engine computes from the generated data (requires `zic`, e.g. on
  macOS or Linux).

The exit code is 0 when the files are up to date, 1 when they were updated, any other value on failure (3 when the
verification fails). The `update-javascript-engine-data` workflow runs `--update` every week and opens a pull request.
