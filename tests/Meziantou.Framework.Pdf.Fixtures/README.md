# PDF fixtures

Small files the tests of `Meziantou.Framework.Pdf` read, committed with their provenance. The large third-party corpora
are not committed: they are pinned in `tests/Meziantou.Framework.Pdf.Corpus/corpus.json` and fetched by
`eng/pdf/fetch-tests.cs`.

- `manifest.json` — **written by the reviewed generator** `eng/pdf/generate-fixtures.cs`. Every file of this folder except
  this README, the manifest, `sources/` and the license files is listed with its SHA-256 hash, provenance, license,
  features and, for the fixtures, what the library must read from it. `PdfFixtureTests` fails when a file is missing from
  the manifest, when a hash differs, or when the library does not read what the manifest expects.
- The tests read this folder from the Git repository root (`RepositoryPaths.Fixtures`). They never download anything and
  never run the generator.

## Layout

```
tests/Meziantou.Framework.Pdf.Fixtures/
  sources/          hand-authored templates (*.in): the text of a file with directives for offsets and lengths
  structure/        valid files: the expanded templates, and the variants qpdf writes from them
                    (*.objstm.pdf: object streams and a cross-reference stream; *.linearized.pdf)
  damaged/          files readers repair (wrong offsets, no cross-reference table, wrong stream lengths)
  invalid/          files the library must reject
  encryption/       structure/simple.pdf encrypted by qpdf with every revision of the standard security handler
  arlington/        the TSV files of the Arlington PDF Model (tsv/latest) and their license
```

## Regenerating

```shell
dotnet run eng/pdf/generate-fixtures.cs              # regenerate in a temporary directory and compare
dotnet run eng/pdf/generate-fixtures.cs -- --write   # replace the fixtures and the manifest
```

The generator expands the templates of `sources/` with `PdfTemplate` (the directives of the templates of the PDFium test
suite: `{{header}}`, `{{object N G}}`, `{{streamlen}}`, `{{xref}}`, `{{trailer}}`, `{{startxref}}`, plus `{{xref shift N}}`,
`{{xref update}}` and `{{prev}}` for damaged files and incremental updates), then runs qpdf (the version the
`Meziantou.Prebuilt` package pins) for the variants. Do not edit the fixtures or the manifest by hand: change a template
or the recipes of the generator and regenerate. The files of the AES-256 encryption cannot be reproduced byte for byte
(qpdf draws their salts at random): the comparison only checks they exist.

Kinds:

- `valid`: `qpdf --check` reports no warning (`PdfFixtureInteropTests`), and the library reads the expected pages, text
  and metadata without repairing the file.
- `damaged`: qpdf repairs the file; the library must read the expected content.
- `invalid`: the library must throw the expected exception.
- `encrypted`: the files of the encryption tests.

## Licensing and provenance

- The templates and the files made from them are hand-authored for this repository: `CC0-1.0`.
- `arlington/tsv/latest` is copied from [pdf-association/arlington-pdf-model](https://github.com/pdf-association/arlington-pdf-model)
  at the commit the manifest records (`vendored`), under the Apache License 2.0 (`arlington/LICENSE`). The files are
  not modified. `ArlingtonValidator` (test harness) checks the objects the library writes against them.
