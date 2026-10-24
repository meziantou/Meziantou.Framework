# Meziantou.Framework.Pdf

A fully managed PDF library: an object model, a reader that repairs broken files, the standard filters, full and
incremental writers, a document API (pages, content builder, standard 14 fonts and embedded font subsets, images,
metadata, outline, links, text extraction), the renderer of the pages (`Rendering/`) and the signatures (`Signing/`).
The renderer and the signatures were separate packages: they are part of this one so that no package depends on the
internals of another (see the sections below).

These instructions complete the repository-level `AGENTS.md`. The user guide is [readme.md](readme.md): keep the code,
the XML documentation and `readme.md` consistent in the same change.

## Hard constraints

- **Clean room.** Write the code from ISO 32000-2 (the PDF Association distributes it free of charge), the Adobe
  technical notes (CMaps, AFM, Type 1) and the specifications it references. Never copy or port code from other PDF
  libraries (PDFsharp, PdfPig, PeachPDF, pdf.js, PDFium, Poppler, MuPDF, qpdf, PDFBox, ...): they are design references
  and test oracles only. Cite the clause in the comments of the code that implements it.
- Fully managed, trimmable and NativeAOT-compatible (`IsTrimmable`): no reflection. The dependencies of the
  repository are `Meziantou.Framework.Imaging` (the image codecs and the ICC profiles) and `Meziantou.Framework.Fonts`
  (the fonts to embed: the reader, the shaper and the subsetter `FontSubsetter`, the AFM metrics of the standard fonts).
  The library uses their public API only, so it depends on them with normal (minimum) versions: never give it access to
  their internals again (`InternalsVisibleTo`, `UseExactProjectReferenceVersions`); make a type public in Fonts or
  Imaging, or compile its source into this library, instead.
- **Bilevel filters** (`Filters/CcittFaxFilter.cs`, `Filters/Jbig2/`): `CCITTFaxDecode` reads the rows with the CCITT
  decoder of the TIFF codec of Imaging, and JBIG2 decodes with the MQ decoder of its JPEG 2000 codec. Their sources
  (`TiffBitReader`, `TiffCcittCode(s)`, `TiffCcittDecoder`, `Jpeg2000MqDecoder`, `Jpeg2000MqTables`) are compiled into
  this library in its namespace (`MEZIANTOU_FRAMEWORK_PDF`, see the project file): a change to them changes both. JBIG2 follows T.88 with the context bit order of its figures: the typical prediction
  contexts alias the contexts of particular templates, so the order matters. Both were checked pixel by pixel against
  MuPDF, Poppler and PDFium on the corpora (`expectations/bilevel-images.txt` of the corpus tests records the digests).
- **Conformance** (`Conformance/`, `Structure/`): the save checks what it can of the PDF/A and PDF/UA levels and writes
  what they need (output intent, XMP identification with the extension schema of PDF/UA for PDF/A-2 and -3, viewer
  preferences, annotation flags; PDF/A-4 leaves the document information dictionary out of the trailer; PDF/A-1 is
  written as PDF 1.4 with a cross-reference table, a version 2 sRGB profile (`SrgbV2Profile`) and the `CIDSet` of the
  font subsets; PDF/UA-2 puts the PDF 2.0 standard types in the PDF 2.0 namespace and needs structure destinations).
  `PdfAConformance` is a published enum: new levels are appended, never inserted. Embedded files
  (`Navigation/PdfEmbeddedFile*.cs`) write the name tree as one flat sorted node and associate the document files with
  the catalog (`/AF`). The
  structure tree keeps its parent tree in objects as the content is tagged (`/StructParents` of the pages,
  `/StructParent` of the annotations) and writes it as one flat number tree. `ConformanceInteropTests` validates the
  documents with the veraPDF of the Meziantou.Prebuilt release: the rules they are known to fail are listed in
  `tests/Meziantou.Framework.Pdf.InteropTests/verapdf-known-failures.txt`, which must stay empty unless a rule cannot
  be met.
- **Encryption** (`Security/`): the standard security handler (7.6.4), revisions 2 to 6. The reader decrypts each
  object when it is loaded (`PdfFileReader.Security`), except the encryption dictionary, the cross-reference streams,
  the metadata when `/EncryptMetadata` is false and the signature of a signature dictionary; the objects of an object
  stream are decrypted with their container. The writer encrypts the strings through the serializer and the streams
  after compression, with the keys of the file the document was read from or new ones (`PdfEncryption.Handler`). RC4 is
  implemented here (the base class library has none); the passwords of revision 6 go through SASLprep with the managed
  normalization of `Meziantou.Framework.Unicode`, so that the reading does not depend on ICU. Every variant is checked
  against qpdf in both directions (interop tests).
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
- The vendored data (`Fonts/GlyphList`) is embedded as resources and listed in `THIRD-PARTY-NOTICES.md`.
  Do not modify it. The metrics of the standard 14 fonts come from `AfmFontMetrics.GetStandardFont` of
  Meziantou.Framework.Fonts, which embeds the Adobe AFM files: do not add another copy.

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

## Renderer (`Rendering/`, namespace `Meziantou.Framework.Pdf.Rendering`)

The renderer of the pages: a content stream interpreter that draws on the `DrawingCanvas` of
`Meziantou.Framework.Imaging`. It reads the embedded font programs with the public `CffFont` and `TrueTypeGlyphOutlines`
of `Meziantou.Framework.Fonts`; the errors of the font programs it parses itself are `InvalidPdfContentException`.

- **Clean room**: write the code from ISO 32000-2, the Adobe technical notes (Type 1, CFF, Type 2 charstrings) and the
  specifications they reference. PDFium, pdf.js, Poppler and MuPDF are test oracles only.
- `DrawingCanvas` is a `ref struct`: the canvas is a parameter of the methods that draw, never a field; the callbacks
  of `Clip`, `Layer`, `Mask` and `Filter` receive the canvas they draw on.
- **Bounded work.** Every operator, glyph, image and shading is charged to `PdfRenderOptions.MaxOperations`
  (`RenderCache.Charge`); the nesting of forms, patterns, Type 3 glyphs and soft masks is limited (`MaxDepth`), as are
  the pixels of images and shadings, the meshes and the clips. The errors of the content are skipped where they occur
  (`RenderCache.IsContentError`): a damaged object does not stop the page.
- **Bundled fonts** (`Rendering/Fonts/Substitution/`): the Liberation fonts (SIL Open Font License 1.1) are generated
  by `dotnet run eng/pdf/update-substitution-fonts.cs` from the pinned release; never edit them by hand. Keep
  `THIRD-PARTY-NOTICES.md` in sync with them.
- **Bounded layers.** A drawing is clipped to the pixels it can change before the clip, the soft mask and the blend
  mode of the graphics state are applied (`PaintWithState` with bounds): the layers of the canvas are as large as the
  clip they are created in.
- Tests: `PdfRendererTests` (the pixels of small documents built with the content builder), and
  `tests/Meziantou.Framework.Pdf.Corpus` (`PdfJsRenderedPages`): the pages of the reference tests of pdf.js are
  rendered at 36 dpi and compared with PDFium (`pdfium_test` of the Meziantou.Prebuilt release). The pages that differ
  by more than 5% of their pixels are recorded in `expectations/rendering.txt`; run the test on Windows with
  `PDF_UPDATE_EXPECTATIONS=1` (one target framework) to record them.

## Signatures (`Signing/`, namespace `Meziantou.Framework.Pdf.Signing`)

Signs and verifies the signatures of documents, with `System.Security.Cryptography.Pkcs`.

- **Clean room**: write the code from ISO 32000-2, ETSI EN 319 142-1 (PAdES), RFC 5652 (CMS), RFC 5035 (ESS) and
  RFC 3161. Apache PDFBox and Poppler are test oracles only.
- The signature is appended as an incremental update: the bytes of the file it is added to never change, so that the
  earlier signatures stay valid. The byte ranges and the signature are written in place of placeholders of fixed
  widths; the writer does not encrypt the `/Contents` of a signature dictionary.
- PAdES baseline: the signed attributes are content-type, message-digest and ESS signing-certificate-v2; the
  signing-time attribute is not written (the time is `/M`).
- Tests: `PdfSignerTests` (certificates created by the tests and a time stamp authority of the tests, an
  `HttpMessageHandler`, no network); `tests/Meziantou.Framework.Pdf.InteropTests` (`SignatureInteropTests`): Poppler's
  pdfsig checks the signatures (the pinned release has no pdfsig for Windows, so they run on Linux and macOS);
  `tests/Meziantou.Framework.Pdf.Corpus`: the signed files of the corpora are verified (`expectations/signatures.txt`),
  and the documents of the signature tests of Apache PDFBox are signed with their keystore.

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
