# Meziantou.Framework.Unicode.Dictionaries.Generator

Generates the word lists embedded in `src/Meziantou.Framework.Unicode.Dictionaries`:

- `Data/*.dict`: the word lists of `data/brkitr/dictionaries/` of the ICU data release (`icu4c-<version>-data.zip` of
  the release of unicode-org/icu): `cjdict.txt` (Chinese and Japanese), `thaidict.txt`, `laodict.txt`,
  `khmerdict.txt` and `burmesedict.txt`, one embedded resource per list.
- `IcuWordDictionary.g.cs`: the version of ICU, and the version line of `THIRD-PARTY-NOTICES.md`.

The release is pinned in `data-versions.json` (version and SHA-256 of the archive). The archive is downloaded once into
`artifacts/unicode-dictionaries` (ignored by git).

## How the word lists are compiled

A list is read as `gendict` reads it (a word, then optionally white space and its value; `#` starts a comment) and
compiled into a trie in breadth-first order (the format is described by `IcuWordDictionary`). As in the build of ICU
(`icu4c/source/data/BUILDRULES.py`), `cjdict.txt` keeps the UTF-16 code units of the words and their values (the costs
of the Chinese and Japanese segmentation), and the other lists are byte tries: a letter is the offset of the character
from the start of the block of the script (`gendict --transform offset-0x0e00` for Thai), U+200C is 0xFE and U+200D is
0xFF. A file is the length of the trie (32 bits, little endian), then the trie compressed with Brotli. The generator
reads every word back through the runtime (`IcuWordDictionary`) with its value; any difference fails the run (exit code
3). A file is only rewritten when its content changes once decompressed: the compressed bytes depend on the version of
Brotli.

## Usage

```
dotnet run --project tools/Meziantou.Framework.Unicode.Dictionaries.Generator [-- --update]
```

- `--update`: moves the pin to the latest release of ICU before generating.

The exit code is 0 when the files are up to date, 1 when they were updated, and another value on failure. The weekly
workflow `.github/workflows/update-unicode-dictionaries.yml` runs it with `--update` and opens a pull request.

The dictionary break engines (`IcuCjkBreakEngine`, `IcuComplexContextBreakEngine`) are manual ports of
`icu4c/source/common/dictbe.cpp`: a new release of ICU may need their changes to be ported. The corpus of
`tests/Meziantou.Framework.JavaScriptEngine.Tests/Corpus/SegmenterV8.jsonl` (recorded with V8) checks them.
