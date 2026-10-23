# Meziantou.Framework.Pdf

A fully managed PDF library: an object model, a reader that repairs broken files, the standard filters, full and
incremental writers, and a document API (pages, content builder, standard 14 fonts and embedded font subsets, images,
metadata, outline, links, text extraction).

These instructions complete the repository-level `AGENTS.md`. The user guide is [readme.md](readme.md): keep the code,
the XML documentation and `readme.md` consistent in the same change.

## Hard constraints

- **Clean room.** Write the code from ISO 32000-2 (the PDF Association distributes it free of charge), the Adobe
  technical notes (CMaps, AFM, Type 1) and the specifications it references. Never copy or port code from other PDF
  libraries (PDFsharp, PdfPig, PeachPDF, pdf.js, PDFium, Poppler, MuPDF, qpdf, PDFBox, ...): they are design references
  and test oracles only. Cite the clause in the comments of the code that implements it.
- Fully managed, trimmable and NativeAOT-compatible (`IsTrimmable`): no reflection. The dependencies of the
  repository are `Meziantou.Framework.Imaging` (the image codecs and the ICC profiles) and `Meziantou.Framework.Fonts`
  (the fonts to embed: the reader, the shaper and the internal subsetter, through `InternalsVisibleTo`), with exact
  versions (`UseExactProjectReferenceVersions` in the project file, as Imaging does with Fonts).
- **Bilevel filters** (`Filters/CcittFaxFilter.cs`, `Filters/Jbig2/`): `CCITTFaxDecode` reads the rows with the CCITT
  decoder of the TIFF codec of Imaging, and JBIG2 decodes with the MQ decoder of its JPEG 2000 codec (both internal,
  through `InternalsVisibleTo`). JBIG2 follows T.88 with the context bit order of its figures: the typical prediction
  contexts alias the contexts of particular templates, so the order matters. Both were checked pixel by pixel against
  MuPDF, Poppler and PDFium on the corpora (`expectations/bilevel-images.txt` of the corpus tests records the digests).
- **Graphics objects** (`Graphics/`): the values that do not belong to a document (`PdfFunction`, `PdfShading`,
  `PdfExtGState`, `PdfShadingPattern`, `PdfSoftMask`) create their objects in each document that uses them, once
  (`PdfDocument.SharedResources`); the objects with content (`PdfForm`, `PdfTilingPattern`) belong to their document and
  are created by it. The meshes are written with 32-bit coordinates and 16-bit components mapped by `/Decode`.
- **Embedded fonts** (`Fonts/EmbeddedPdfFont.cs`, `Fonts/EmbeddedFontUsage.cs`): a `Type0` font with `Identity-H`
  codes that are the glyph identifiers (the subset keeps them: CID = GID), a `ToUnicode` CMap of the glyphs that show
  one cluster, and `/ActualText` around the texts whose glyphs cannot give them back. The content builder shapes the
  text and writes `TJ` arrays; the font objects are written when the document is saved, so the subset grows with the
  content until then. The subsetter lives in `Meziantou.Framework.Fonts` (`FontSubsetter`, `CffSubsetWriter`).
- Public namespaces: `Meziantou.Framework.Pdf` (document API) and `Meziantou.Framework.Pdf.Objects` (object model).
  Implementation types are `internal`, in sub-namespaces by folder (`Parsing`, `Writing`, `Filters`, `Fonts`, `Text`,
  `Content`, `Metadata`). Every public member is documented (`DisableDocumentationWarnings` is `false`).
- The vendored data (`Fonts/Afm`, `Fonts/GlyphList`) is embedded as resources and listed in `THIRD-PARTY-NOTICES.md`.
  Do not modify it.

## Behavior rules

- **Errors** are distinct: `InvalidPdfContentException` (malformed beyond repair), `UnsupportedPdfFeatureException`
  (valid but not supported), `PdfResourceLimitException` (a limit of `PdfReadOptions`), all deriving from
  `PdfException`; argument and state errors use the `ArgumentException` family and `InvalidOperationException`. No other
  exception may escape the reader: the fuzz tests and the corpus tests check it.
- **Readers repair.** Real files are damaged: the lexer accepts what readers accept (signs and points in numbers,
  unknown escapes, invalid hexadecimal digits), a wrong cross-reference entry falls back to a scan of the file
  (`PdfRecoveryScanner`), a missing or broken cross-reference rebuilds everything (`PdfFileReader.Repair`,
  `PdfDocument.IsRepaired`), a stream whose `/Length` is wrong ends at `endstream`, a damaged Flate stream gives the data
  before the damage. A reference to an object that does not exist is the null object (7.3.10).
- **Bounded work.** Everything a file can make expand is bounded: the decoded length of a stream
  (`MaxDecodedStreamLength`, checked as the output grows), the nesting of objects (`MaxNestingDepth`), the page tree and
  the outline (depth and visited sets), chains of references and of `/Prev` sections, the `/W` arrays of CID fonts (a
  budget of assignments). A hostile file must take time and memory in proportion to its size.
- **Objects are loaded lazily** (`PdfIndirectObjectCollection`): an object of the file is parsed when a reference to it
  is first resolved. When it is loaded, its fingerprint (`PdfObjectFingerprint`) is taken: an incremental save writes
  the objects whose fingerprint changed, and the new ones. Mutating an object therefore needs no other bookkeeping.
- **The high-level API writes the objects.** `PdfPage`, `PdfDocumentInformation`, `PdfAnnotationCollection` read and
  write the dictionaries directly; the outline is the exception (`PdfOutlineCollection` is a model written back by
  `PdfDocument.PrepareForSave`, which only rewrites what changed).
- **Deterministic output.** A save depends only on the document and the options: objects are numbered in the order
  they are reached from the trailer, dictionaries keep the order of their entries, the file identifier is an MD5 of the
  content, numbers are written without exponent with at most six decimals. Never write a date, a random value or the
  version of the library implicitly.
- A full save writes the objects reachable from `/Root` and `/Info`, with the `/Length` of the streams written direct
  (the indirect length objects of a file become unreachable). An incremental save keeps the kind of cross-reference of
  the original file and refuses a repaired file.

## Tests

| Path | Content |
| --- | --- |
| `tests/Meziantou.Framework.Pdf.Tests` | Unit tests: lexer, parser, filters (round trips and the vectors of the specification), reader and recovery (files written by `HandWrittenPdf`, independent of the writer), writer goldens (`__snapshots__`, `Meziantou.Framework.SnapshotTesting`) and round trips, incremental saves, document API, text extraction, fonts, the fixtures and their manifest, and the Arlington PDF Model checks of what the writer produces |
| `tests/Meziantou.Framework.Pdf.TestHarness` | The fixture manifest, the template expander (`PdfTemplate`), the validator of the vendored Arlington PDF Model (`ArlingtonValidator`), and the external tools (qpdf, TestGrammar, Poppler) from the release `Meziantou.Prebuilt` pins |
| `tests/Meziantou.Framework.Pdf.InteropTests` | `qpdf --check` of what the library writes, the objects the parser reads compared with `qpdf --json`, TestGrammar, `pdftotext` and `pdfinfo` |
| `tests/Meziantou.Framework.Pdf.Fixtures` | The fixtures, their templates and the manifest ([README](../../tests/Meziantou.Framework.Pdf.Fixtures/README.md)) |
| `tests/Meziantou.Framework.Pdf.Corpus` | No-crash tests over the external corpora pinned in `corpus.json` (`expectations/`, `corpus-skip.txt`) |
| `fuzz/Meziantou.Framework.Pdf.FuzzTests` | Deterministic mutation fuzzing of the fixtures, the filters and the parser, and the committed regressions |

```shell
dotnet test --project tests/Meziantou.Framework.Pdf.Tests/Meziantou.Framework.Pdf.Tests.csproj
dotnet test --project tests/Meziantou.Framework.Pdf.InteropTests/Meziantou.Framework.Pdf.InteropTests.csproj
dotnet run eng/pdf/fetch-tests.cs
dotnet test --project tests/Meziantou.Framework.Pdf.Corpus/Meziantou.Framework.Pdf.Corpus.csproj
dotnet test --project fuzz/Meziantou.Framework.Pdf.FuzzTests/Meziantou.Framework.Pdf.FuzzTests.csproj
```

- **Expected values come from independent sources**: hand-computed values, the specification, files written by hand
  (`HandWrittenPdf`, the templates of the fixtures) or by qpdf, and the external tools. Never check the reader with a
  file written by the writer alone, except in round-trip tests.
- **Fixtures** are generated by `eng/pdf/generate-fixtures.cs` (see the fixtures README); never edit them or the
  manifest by hand. A valid fixture passes `qpdf --check` without warning and TestGrammar without error.
- **Goldens**: the writer goldens are exact bytes. Change them only when the output changes on purpose, and review the
  diff of the `.verified.*` files.
- **Corpus**: the outcomes that are not a clean read are listed in `expectations/<suite>.txt`; only the documented
  exceptions may be listed. After a change of the reader, run the corpus tests with `PDF_UPDATE_EXPECTATIONS=1` on one
  target framework and review the diff: an expectation that disappears is an improvement, one that appears is a
  regression to explain.
- **Fuzzing**: the default budget runs in CI. Longer campaigns: `MEZIANTOU_FRAMEWORK_PDF_FUZZ_ITERATIONS`,
  `MEZIANTOU_FRAMEWORK_PDF_FUZZ_SEED`, `MEZIANTOU_FRAMEWORK_PDF_FUZZ_CASE_TIMEOUT_SECONDS`. A failure is minimized and
  committed to `FuzzRegressions/`.
