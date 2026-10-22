# Meziantou.Framework.JavaScriptEngine.Data.Generator

Generates the data embedded in `Meziantou.Framework.JavaScriptEngine`, so that the engine computes the same results on
every operating system and in invariant-globalization mode (no `TimeZoneInfo`, no ICU):

- `src/Meziantou.Framework.JavaScriptEngine/TimeZones/tzdb.bin`: the IANA time zone database, compiled by a small zic
  of this tool, and the CLDR mapping of the Windows time zone identifiers (`windowsZones.xml`, territory `001`). The
  file is Brotli-compressed; `TimeZoneDatabase` reads it.
- `src/Meziantou.Framework.JavaScriptEngine/TimeZones/TimeZoneDatabase.g.cs`: the versions of the data.
- `src/Meziantou.Framework.JavaScriptEngine/Intl/Data/{locales,supplemental,collation}.bin`: the CLDR data of Intl
  (ECMA-402), read by `CldrData`.
- `src/Meziantou.Framework.JavaScriptEngine/Intl/Data/CldrData.g.cs`: the versions of the CLDR data.
- `src/Meziantou.Framework.JavaScriptEngine/Intl/Data/dictionaries.bin`: the word lists of ICU used by the word
  segmentation of `Intl.Segmenter`, read by `WordDictionaries`; `Intl/Segmenter/WordDictionaries.g.cs` has the version of ICU.

The versions are pinned, with the SHA-256 of the downloaded files, in `data-versions.json`:

| Pin | Source | Used for |
|---|---|---|
| `tzdata` | `https://data.iana.org/time-zones/releases/tzdata<version>.tar.gz` | tzdb.bin |
| `cldr` | `windowsZones.xml` of the tag of unicode-org/cldr | tzdb.bin |
| `cldr-json` | `cldr-<version>-json-full.zip` of unicode-org/cldr-json | locales.bin, supplemental.bin |
| `cldr-common` | `https://unicode.org/Public/cldr/<version>/cldr-common-<version>.zip` (the CLDR XML) | collation.bin |
| `ucd` | `PropList.txt` of the UCD of the version of UCA of the CLDR release | collation.bin (Unified_Ideograph) |
| `icu` | `icu4c-<version>-data.zip` of the release of unicode-org/icu | dictionaries.bin |

The downloaded files are cached in `artifacts/javascriptengine-data/` (ignored by git; the SHA-256 is checked on every
use) and never committed. `cldr` and `cldr-common` must be the same CLDR release (a test checks it).

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

## How the CLDR data is compiled

All the files use the same container (`CldrChunkedFile`): a small uncompressed directory, then chunks compressed
independently with Brotli. A chunk is decompressed on its first use, so a script that does not use Intl reads nothing,
and `new Intl.NumberFormat("fr")` decompresses the index and the Numbers chunks of `fr` and of the root only. Most
chunks are data trees (`CldrTreeChunk`): a string table and a table of tables (keys sorted by ordinal, shared by the
file), in which identical strings and identical tables are stored once.

- **Locales** (`locales.bin`): the locales of CLDR coverage `modern` and `moderate` (`cldr-core/coverageLevels.json`)
  and their parents (`parentLocales.json` and the rules of UTS 35, including `nonlikelyScript`), plus aliases: the
  default content locales (`fr-FR` -> `fr`) and the language-region identifiers whose likely script is not the default
  one of the language (`zh-TW` -> `zh-Hant-TW` -> `zh-Hant`). cldr-json contains the resolved data of each locale; the
  generator stores for each locale and category (`CldrCategory`: Numbers, Currencies, Units, Gregorian, Calendars,
  TimeZoneNames, Fields, DisplayNames, Misc) only the values that differ from its parent, and the CLDR marker of
  "no value" (`∅∅∅`) for a value of the parent that the child does not have (e.g. `en-001` does not use the short
  names of the American metazones of `en`). One chunk per category and language. The paths and keys are the ones of
  cldr-json, except: the units are limited to the ones of ECMA-402 and keyed by their identifier (`kilometer`), and a
  pattern with attributes (`{ "_value": "d/M/yy", "_numbers": "M=romanlow" }`) becomes two strings (`short` and
  `short-numbers`). The units also include the compound units of two units of ECMA-402 that CLDR has
  (`kilometer-per-hour`). cldr-json has only the `latn` numbers data for the root: the symbols and the patterns of the
  other numbering systems of `common/main/root.xml` that are not aliases (`arab`, `arabext`) are added to it, and every
  locale inherits them. Subdivisions, measurement system names, minimal pairs, annotations, person names, RBNF and
  segmentation suppressions are not included.
- **Supplemental** (`supplemental.bin`): one chunk per section (likelySubtags, aliases, plurals, ordinals,
  pluralRanges, currencyData, timeData, weekData, calendarPreferenceData, calendarData, numberingSystems, metaZones,
  dayPeriods, primaryZones, bcp47), with the structure of cldr-json (see `CldrSupplemental`). The plural rules are kept
  as text (with their samples, which the tests check) and parsed on first use by `PluralRules`. The metazone periods
  are in seconds since the epoch.
- **Collation** (`collation.bin`): the root collation from `common/uca/allkeys_CLDR.txt` (the collation elements of
  each code point and contraction), the implicit weights of the siniform scripts (from the DUCET weights in the comments
  of `FractionalUCA.txt`) and of the Unified_Ideograph code points (`PropList.txt`). The tailorings of
  `common/collation/*.xml` of the languages of the embedded locales are stored as rules (one tree per locale: type =
  rules, `_default` = the default type), with the imports inlined and the comments removed; the generator checks that
  the parser of the engine (`CollationTailoring.Parse`) accepts them. The collator applies them to the root table. As
  ICU, the collations marked `draft="unconfirmed"` or `"provisional"` and the `alt="proposed"` ones are not included.
  The collations inherit by truncation (`zh-Hant` -> `zh`, as ICU); the parents of the index are the parentLocales of
  the "collations" component and the main parentLocales to a locale other than the root of the locales without a script
  subtag (`nb` -> `no`).

Before writing the files, the generator reads the resolved value of every path of every locale back through the
runtime (`CldrLocale`) and compares it with cldr-json; any difference fails the run (exit code 3).

## How the word lists are compiled

`dictionaries.bin` uses the same container as the CLDR data, with one chunk per word list of
`data/brkitr/dictionaries/` of the ICU data release, in the order of `WordDictionaryKind`: `cjdict.txt` (Chinese and
Japanese), `thaidict.txt`, `laodict.txt`, `khmerdict.txt` and `burmesedict.txt`. A list is read as `gendict` reads it
(a word, then optionally white space and its value; `#` starts a comment) and compiled into a trie in breadth-first
order (the format is described by `WordDictionary`). As in the build of ICU (`icu4c/source/data/BUILDRULES.py`),
`cjdict.txt` keeps the UTF-16 code units of the words and their values (the costs of the Chinese and Japanese
segmentation), and the other lists are byte tries: a letter is the offset of the character from the start of the block
of the script (`gendict --transform offset-0x0e00` for Thai), U+200C is 0xFE and U+200D is 0xFF. The generator reads
every word back through the runtime (`WordDictionary`) with its value; any difference fails the run (exit code 3).

## Usage

```shell
dotnet run --project tools/Meziantou.Framework.JavaScriptEngine.Data.Generator [-- [--update] [--verify] [tzdb] [cldr] [dictionaries]]
```

- `tzdb`, `cldr`, `dictionaries`: the data to generate (all by default). The CLDR step takes about 30 seconds once the files are
  downloaded (about 120 MB the first time), the dictionaries step about 5 seconds (the ICU data release is about 20 MB).
- `--update`: moves the pins to the latest releases (tzdata, CLDR, cldr-json, the UCD of the UCA version of the
  CLDR release, and ICU) before generating.
- `--verify`: compiles the same source with the `zic` of the system (`zic -b fat`) and compares the transitions of
  every zone and link until 2037 with the ones the engine computes from the generated data (requires `zic`, e.g. on
  macOS or Linux).

The exit code is 0 when the files are up to date, 1 when they were updated, any other value on failure (3 when the
verification fails). The `update-javascript-engine-data` workflow runs `--update --verify` every week and opens a pull
request. `eng/update-all.cs` does not run this tool: downloading the CLDR releases on every run would be too slow.
