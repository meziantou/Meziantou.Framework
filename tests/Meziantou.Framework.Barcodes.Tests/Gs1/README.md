# GS1 linter tests

`gs1-linter-tests.json` holds the unit tests of the reference linters of the
[GS1 Barcode Syntax Dictionary](https://github.com/gs1/gs1-syntax-dictionary) (Apache License 2.0, see
`src/Meziantou.Framework.Barcodes/THIRD-PARTY-NOTICES.md`), at commit c63d8a12210dd0cfaceac4aae45ccf57b48eb15b.

Each `UNIT_TEST_PASS` and `UNIT_TEST_FAIL` call of the `#ifdef UNIT_TESTS` sections of `src/lint_*.c` is one entry:
the linter, its input, and the error the reference reports, with the position and the length of the faulty data (from
the `*` markers of the expected highlight). The cases the reference builds in loops from variables, the ones that need
a custom GS1 Company Prefix lookup, and the ones whose highlight is ambiguous (an input of `*`) are not included: the
unit tests list the loop cases and the ambiguous ones by hand.

`Gs1DictionaryTests.Linters_MatchTheReferenceImplementation` runs every entry, and the `barcodes-gs1-linters` fuzz target
uses the inputs as seeds.
