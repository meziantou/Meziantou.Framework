# Golden corpus

Independent reference data for the conformance and interop tests, extended with each codec.

- `manifest.json` — the versioned manifest (schema version 5: `manifest.schema.json`), **written by the reviewed generator**
  `tools/Meziantou.Framework.Imaging.CorpusGenerator` (a .NET console app: the `golden` generator, `GoldenCorpus.cs`, writes
  the PNG, APNG, GIF and JPEG entries; WebP entries: `webp`, `WebPCorpus.cs`; QOI entries: `qoi`, `QoiCorpus.cs`; BMP entries:
  `bmp`, `BmpCorpus.cs`; TGA entries: `tga`, `TgaCorpus.cs`; Netpbm entries: `pnm`, `PnmCorpus.cs`; ICC profiles and
  color conversion vectors: `icc`, `IccCorpus.cs`; the AV1 conformance vectors of the `av1Vectors` section: `avif`,
  `AvifCorpus.cs`; AVIF entries: `avif-container`, `AvifCorpus.Container.cs`; HEIC entries: `heic-container`,
  `HeicCorpus.Container.cs`; JPEG XL entries: `jxl`, `JxlCorpus.cs`; JPEG 2000 entries: `jp2`, `Jp2Corpus.cs`; OpenEXR entries: `exr`, `ExrCorpus.cs`; the HEVC
  conformance vectors of the `hevcVectors` section: `hevc`, `HevcCorpus.cs`). Every file of this folder except this README, the manifest, the schema and `LICENSES/` is listed with
  its SHA-256 hash, provenance and license.
- The tests read this folder from the Git repository root (`FixtureRoot.GetDirectory()`). They never download anything
  and never run the generator.
- `GoldenCorpus.Default` loads the manifest and validates it completely (`FixtureManifestValidator`) before any pixel
  comparison; `CorpusManifestTests` reports every problem.
- Licensing and provenance rules: [Licensing and provenance](#licensing-and-provenance). Feature coverage:
  [Fixture coverage matrix](#fixture-coverage-matrix) and, for the AV1 and the HEVC decoders,
  [AV1 conformance vectors](#av1-conformance-vectors) and [HEVC conformance vectors](#hevc-conformance-vectors).

## Layout

```
tests/Meziantou.Framework.Imaging.Fixtures/
  png/   apng/   gif/   jpeg/   webp/   qoi/   bmp/   tga/   pnm/   avif/   heic/   jxl/   jp2/   exr/
                                      encoded inputs and their raw references
  invalid/<format>/                   malformed, unsupported or over-limit inputs (expected errors, no expected pixels)
  limit/avif/                         over-limit inputs that are not valid fixtures (an AVIF sequence with hidden frames)
  av1/                                raw AV1 OBU streams (*.obu): conformance vectors with plane digests, no raw references
  hevc/                               raw HEVC byte streams (*.hevc): conformance vectors with plane digests, no raw references
  icc/profiles/   icc/vectors/        ICC profiles and colors converted between them (see Color management)
  fonts/<family>/   fonts/vectors/    font files and the values independent font readers give for them (see Fonts)
  fonts/shaping/                      glyphs and positions the reference shaper gives for texts set in the fonts
  font-suites/<suite>/                third-party suites of test fonts and expectations, stored as published
  LICENSES/                           full texts of the licenses of third-party files (OFL-1.1, Apache-2.0, Unicode-3.0, BSD-3-Clause)
```

- Fixture ids are lowercase paths without extension (`png/gray16-low-bit-gradient`); `format` is the container
  (`png` for PNG and APNG, `gif`, `jpeg`, `webp`, `qoi`, `bmp`, `tga`, `pnm` for every Netpbm variant, `avif` for AVIF
  still images and image sequences, `heic` for HEIC still images and image sequences, `jxl` for JPEG XL codestreams and
  containers, `jp2` for JPEG 2000 codestreams and files of the JP2 family, whatever their block coder, `exr` for OpenEXR
  files). The expected frames of an OpenEXR fixture are `rgbaf32le` buffers: the 32-bit floats of its `RgbaHalf` or
  `RgbaFloat` pixels (a half converts to a float exactly), compared bit for bit; they are no source of the encoder suites.
- Kinds: `valid` (expected pixels and metadata), `invalid` (malformed: expected exception), `unsupported` (recognized but
  unsupported feature: `UnsupportedImageFeatureException`), `limit` (decoded with `decodeOptions.limits`:
  `ImageResourceLimitException` with `limitKind`). Error fixtures never carry fabricated expected pixels and are kept
  separate from malformed-input fuzzing.
- `features` lists the encoding features actually present in each input, parsed from the encoded bytes by the generator
  (never trusted from the requested tool flags): e.g. `png.interlace=adam7`, `png.filters=0,1,2,3,4`,
  `apng.poster=separate`, `apng.dispose=background,none,previous`, `gif.disposal=0,1,2,3`, `gif.lzw.deferredClear`,
  `jpeg.sampling=4:2:2`, `jpeg.restartInterval=2`, `webp.bitstream=vp8l`, `webp.vp8l.transform=color-indexing`,
  `webp.blend=alpha`, `avif.grid=2x2`, `avif.seq.editList=repeat`, `avif.layers=3`, `avif.lsel=1`. Use them to select fixtures
  (`GoldenCorpus.GetIds(feature: ...)`).
- A JPEG file with four components holds CMYK or YCCK inks, which no pixel format holds: the library only loads it with a
  color conversion (`CmykConversionOptions`). Such a file is an `unsupported` fixture, so the generic suites check that
  it is identified and that a load without conversion is rejected. Its `inkConversion` records one conversion (the
  profile of the inks, embedded or assumed, the destination profile of `colorProfiles`, the rendering intent, black
  point compensation), the converted pixels (`<id>.converted.rgb8.raw`), how they were established and their comparison
  policy: see [Reference provenance](#reference-provenance). `JpegDecoderConformanceTests` loads it with that conversion.
- The files of `av1/` are **not fixtures**: they are listed in the separate `av1Vectors` section of the manifest
  ([AV1 conformance vectors](#av1-conformance-vectors)), with the same hash, provenance and license rules. They have no
  pixel reference, so `GoldenCorpus.Fixtures`, `GetIds` and the generic conformance suites never see them, and a fixture
  whose id or input is under `av1/` is a validation error.
- Neither are the files of `hevc/`: they are listed in the `hevcVectors` section
  ([HEVC conformance vectors](#hevc-conformance-vectors)), under the same rules, and a fixture whose id or input is under
  `hevc/` is a validation error. `GoldenCorpus.HevcVectors`, `GetHevcVector` and `GetHevcVectorIds` enumerate them.

## Expected images

`expected` describes the decoded image independently of the pixel bytes: canvas size (stored pixels; EXIF orientation is
never applied), default working `pixelFormat`, `colorModel`, `bitsPerComponent`, `orientation`, `iccProfile`, `animation`
(`totalPlays` normalized, `null` = infinite, plus the raw `encodedLoopValue`), `frameCount`, one entry per **full-canvas
displayed frame** with its exact rational `duration` (`numerator/denominator` seconds, normalized), and the separate
`poster` (APNG default image that is not part of the animation) or `null`. Durations and loop counts come from the
encoded fields or the hand-written instructions, never from FFmpeg timestamps. The validator checks the conventions of
the library: APNG `num_plays` 0 ↔ infinite; GIF NETSCAPE
loop `L` ↔ `L + 1` plays, absent ↔ 1; WebP `ANIM` loop count 0 ↔ infinite, `N` ↔ `N` plays (the WebP container
specification counts plays, not repetitions); AVIF image sequences have no loop field: `animation.editList` records the
edit list of the track of the color images (`repeating`, `segmentDuration`) with the duration of its track header
(`trackDuration`; `null` when every bit is set), or `null` without edit list, and `totalPlays` must be what they mean: no
edit list ↔ infinite; not repeating ↔ 1; repeating ↔ the track duration over the edit duration, rounded up, infinite when
the track duration is indefinite or when the count does not fit 31 bits.

AVIF inputs also record `cicp` (`{colorPrimaries, transferCharacteristics, matrixCoefficients, fullRange}`: the `colr`
box of type `nclx`, or else the color description of the AV1 sequence header, or `null`), `bitsPerComponent` 10 and 12,
and an `orientation` that comes from the `irot` and `imir` properties of the primary item (never from the EXIF payload,
which may say otherwise). The inputs that have the box also record `contentLightLevel` (`{maxContentLightLevel,
maxPictureAverageLightLevel}`: the `clli` box), `masteringDisplay` (`{red, green, blue, whitePoint, maxLuminance,
minLuminance}`: the `mdcv` box, whose primaries are stored in the order green, blue, red; chromaticities `[x, y]` and
luminances as the integers of the box) and `pixelAspectRatio` (`{horizontalSpacing, verticalSpacing}`: the `pasp`
box), read from the primary item or from the sample entry of the track; the keys are absent for the other inputs.
The canvas of an AVIF image is its clean aperture. The `orientation` of an AVIF image
sequence comes from the matrix of the track header of its color track (`ReferenceHeif.Track.Orientation`: 1 unless the
matrix turns the images by quarter turns or mirrors them), and its canvas is the clean aperture of its sample entry;
its `profiles.exif` and `profiles.xmp` are the items of the `meta` box of its
track, or else of the `meta` box of the file when that box precedes the `moov` box (`ReferenceHeif.FindSequenceMetadata`),
and `null` otherwise, whatever the `meta` box of the file holds. The frames of a sequence that are decoded without being
shown are not frames of the fixture: their number is the `avif.seq.hiddenFrames=N` feature, which the limit tests add to
the displayed frames (`MaxTotalPixels` counts them). A layered image (`avif.layers=N`: a frame per spatial layer) presents
one frame, the frame of the layer its properties select, at the size of that frame (`ispe`); the frames of the layers
below it are decoded on the way and counted too, each with its own size: their pixels are the
`avif.layers.hiddenPixels=N` feature (`FixtureTraits.GetHiddenPixelCount` adds both to the displayed pixels). An HEVC
picture whose conformance window removes more than the padding of its coded picture to whole coding blocks is decoded
whole: the size that then counts toward the canvas limits of a decode is the `heic.checkedSize=WxH` feature
(`FixtureTraits.GetLimitedCanvas`). A window that only removes the padding does not count.

Timing and metadata expectations are parsed by the generator from the encoded bytes of every input:

| Field | Meaning |
| --- | --- |
| `frames[].encodedDelay` | Raw delay field of the frame: APNG `fcTL` `{numerator, denominator}` as encoded (`denominator` 0 means 100) or GIF Graphic Control Extension `{hundredths}` or WebP `ANMF` `{milliseconds}` (24-bit) or the `stts` sample delta of an AVIF image sequence with the `mdhd` time scale of its media `{sampleDelta, timescale}`. Required for every frame of an animated APNG/GIF/WebP/AVIF (a GIF frame without GCE has none and a zero duration). The validator checks `duration` = `numerator/(denominator or 100)`, `hundredths/100`, `milliseconds/1000` or `sampleDelta/timescale` exactly. |
| `resolution` | `null` (absent, or aspect ratio only: PNG `pHYs` unit 0, JFIF units 0) or `{horizontalDpi, verticalDpi, encoded: {unit, x, y}}` with `unit` `meter` (PNG `pHYs`, BMP `biXPelsPerMeter`), `inch` or `centimeter` (JFIF); the dpi values are the exact IEEE results of `x × 0.0254`, `x` or `x × 2.54`. |
| `profiles` | `{icc, exif, xmp}`: `null` or `{sha256, length}` of the payload decoders must preserve byte for byte — the uncompressed ICC profile, the EXIF data from the TIFF header (no `Exif\0\0` prefix), the XMP packet (WebP: the `ICCP`, `EXIF` and `XMP ` chunk payloads; AVIF: the `colr` box of type `prof`, the `Exif` item after its offset field, the XMP `mime` item; for an image sequence, the items of `ReferenceHeif.FindSequenceMetadata`). `iccProfile` must be `none` exactly when `profiles.icc` is `null`; an orientation other than 1 requires `profiles.exif`. |
| `transferFunction` | Optional: `srgb` (default) or `linear`, the transfer-function label of the decoded samples (`ImageMetadata.TransferFunction`), read from the QOI header colorspace; only QOI inputs may be `linear`. |
| `text` | Text entries in file order: `{keyword, value, languageTag, translatedKeyword}` (PNG `tEXt`/`zTXt`/`iTXt` except the XMP `iTXt`; empty language tags and translated keywords are `null`). GIF comments and JPEG `COM` segments use the keyword `Comment` without language. |

`EncodedFieldInspector` (test harness) reads the same raw fields from the inputs independently of the generator and of the
library (PNG chunks, GIF blocks, JPEG marker segments, WebP RIFF chunks, AVIF boxes through `ReferenceHeif`; no pixel
decoding).
`TimingAndMetadataConformanceTests` checks that the manifest agrees with the bytes and that the library's timing,
play-count, orientation, resolution and profile conversions turn these raw fields into the expected exact values.

Each frame lists raw reference buffers, one per layout. Raw files carry no metadata: the descriptor is authoritative.
Fixtures with both a native gray reference and an RGBA reference (written independently by the generator) are also used by
`PixelConversionConformanceTests` to cross-check the gray/color row conversions in both directions, the 16-bit
little-endian references to check lossless 16-bit paths and byte order, and translucent references (alpha ramps,
transparent pixels) to check alpha rejection and flattening against explicitly computed rounding.

| Field | Meaning |
| --- | --- |
| `layout` | `rgba8`, `rgba16le` (canonical; straight alpha), native `gray8`/`gray16le` (sample decoding isolated from conversion), also `rgb8`, `graya8`, `graya16le` |
| `width`, `height` | Must equal the canvas (full-canvas frames) |
| `rowBytes` | `width x bytes per pixel` (tightly packed) |
| `byteLength` | `rowBytes x height`, exact decoded length |
| `alpha` | `straight` for layouts with alpha, `none` otherwise |
| `rowOrder` | `top-down` |
| `compression` | `none`, or `gzip` (bounded decompression: stops one byte past `byteLength`) |

Files are named `<input-name>.<role>.<layout>.raw` (`.raw.gz` when compressed) where role is `frame-<index>` or `poster`.
AVIF fixtures that present the same pixels (one coded payload in several containers: the assembled layouts, the
orientations) share their reference files, which are named after the first of them. 16-bit samples are always
little-endian. Every frame and the poster provide the same layouts; 16-bit fixtures always include a 16-bit layout.

## Fonts

The `fonts` section of the manifest lists the font files of the text rendering tests. Fonts are not image fixtures:
they have no expected pixels, and the generic conformance, hardening and fuzz suites never enumerate them. They are
written by the `fonts` generator (`FontCorpus.cs`).

| Field | Meaning |
| --- | --- |
| `file` | The font file and its SHA-256 |
| `flavor` | `ttf` (TrueType outlines), `otf` (CFF outlines), `ttc` (collection), `woff`, `woff2` |
| `faceCount` | The number of faces; more than one for a collection |
| `modification` | `none` (the file as published, or the output of the generator), `subset` (glyphs and tables removed by the pinned `hb-subset`), `container` (the tables of `original` in another container) |
| `reservedFontName` | The Reserved Font Name the license of the font declares, when there is one |
| `original` | For a `container`: the id of the font it wraps |
| `features` | Tags read from the file by the generator: `font.table=<tag>` for the tables that matter to a feature, `font.cmap=<format>`, `font.loca=short|long`, `font.container=ttc` |
| `vectors` | The reference values of the font, a JSON file of `fonts/vectors/` |
| `shaping` | The glyphs and positions the reference shaper gives for texts set in the font, a JSON file of `fonts/shaping/` |

Three kinds of fonts are stored:

- **Synthetic fonts** (`fonts/synthetic/`, `CC0-1.0`), built by `Common/FontBuilder.cs` from the descriptions of
  `Common/FontSamples.cs`. Both files are compiled into the tests, which build the same fonts, and variants of them, in
  memory: the geometry (coordinates that are multiples of 64 for the fonts drawn in text tests, charstrings of a few
  operators for the CFF ones) makes the expected values of a test computable by hand. `FontCorpusTests` checks that the committed files are what the builder writes.
- **Third-party fonts**, from the sources `FontCorpus.cs` lists with their pinned SHA-256. A font whose license allows a
  modified copy is subset to the characters the tests need, with every layout feature kept; a font with a Reserved Font
  Name is stored as published.
- **Other containers** of these fonts: WOFF (written by `FontBuilder.WriteWoff`, since no pinned tool writes WOFF 1.0)
  and WOFF2 (written by `woff2_compress`, the reference encoder). The reference encoder chooses what it transforms and
  how it encodes each point, and it does not transform the horizontal metrics of these fonts: the WOFF2 files with a
  variant in their name (`boxes.metrics.woff2`, `deltas.boxes.woff2`) are written by `Common/FontWoff2Writer.cs`
  instead, with the transformations the reference encoder leaves out.

The vectors of a font (`FontVectors` in the test harness) hold, for each face: the names, the units per em, the glyph
count, the metrics the font declares (by OpenType metric tag), the character map, the advance of each glyph and the
outline of each glyph as SVG path data in font units. They are read by the HarfBuzz utilities of the pinned
meziantou/prebuilt release with **two independent implementations**, HarfBuzz's own font functions (`--font-funcs=ot`)
and FreeType (`--font-funcs=ft`): a value is recorded only when both agree, and a disagreement stops the generator. The
character map is the join of `hb-info --list-unicodes` and `--list-glyphs`, so that it does not go through shaping.

A container has no vectors of its own: the generator reads it back through FreeType (`--face-loader=ft`, the only
reader of the pinned tools that opens WOFF and WOFF2) and requires the advances and the outlines of the font it wraps;
`woff2_decompress`, the reference decoder, must accept a WOFF2 file. The files written by `FontWoff2Writer` are
therefore known to mean, for two independent decoders, the font they were made from. FreeType rebuilds the glyphs of
a WOFF2 file in a buffer of the length the file declares for the glyph table and drops what does not fit: the writer
declares the length of the table with each glyph padded to four bytes, as the reference encoder does.

Two properties of the pinned readers shape the synthetic fonts. FreeType rounds the coordinates of a CFF outline to
whole units and HarfBuzz does not, so the charstrings of the corpus only use whole numbers (fractions are tested with
literals in `FontTests`). Both readers keep a contour of one point and always close a TrueType contour with a line to
its first point, even a line without length: the library does the same, so that the recorded outlines compare exactly.

| Font | Flavor | Faces | License | Covers |
| --- | --- | --- | --- | --- |
| `fonts/synthetic-boxes` | ttf | 1 | CC0-1.0 | Straight-edged glyphs, composite glyphs (offset, scale, `USE_MY_METRICS`), `cmap` formats 4 and 12, `kern`, compact glyph encoding, short `loca` |
| `fonts/synthetic-curves` | ttf | 1 | CC0-1.0 | Quadratic contours: no on-curve point, a contour that starts off-curve, consecutive off-curve points, a hole; 16-bit glyph encoding, long `loca`, `cmap` format 12 only |
| `fonts/synthetic-deltas` | ttf | 1 | CC0-1.0 | Points at every kind of distance from the previous one (each coordinate encoding of the glyph table and of WOFF2), instructions, the overlap flag, a composite with 16-bit offsets, contours of one and two points |
| `fonts/synthetic-collection` | ttc | 2 | CC0-1.0 | The boxes and curves fonts in a collection |
| `fonts/synthetic-charstrings` | otf | 1 | CC0-1.0 | CFF outlines: one glyph per group of Type 2 charstring operators (lines, curves, flex, hints and masks, widths, local and global subroutines, number encodings, empty and implicitly closed contours) |
| `fonts/synthetic-cid` | otf | 1 | CC0-1.0 | A CID-keyed CFF font: two font DICTs whose local subroutines differ, a font DICT selector of format 3 |
| `fonts/noto-sans-regular` | ttf | 1 | OFL-1.1 | Noto Sans Regular 2.015, subset to 232 characters (Basic Latin, Latin-1, combining marks, punctuation): 425 glyphs, `GDEF`, `GSUB`, `GPOS` |
| `fonts/noto-sans-regular-cff` | otf | 1 | OFL-1.1 | The CFF flavor of the same font and the same characters: 421 glyphs, subroutines |
| `fonts/noto-sans-cjk-jp-regular` | otf | 1 | OFL-1.1 | Noto Sans CJK JP Regular 2.004, subset to 408 characters (Basic Latin, kana, a few ideographs, fullwidth forms): 1,099 glyphs, a CID-keyed CFF table with several font DICTs, hints kept |
| `fonts/synthetic-variable` | ttf | 1 | CC0-1.0 | A variable font of two axes: `fvar` with named instances, `avar`, `gvar` (deltas for every point, for some points with inferred deltas, shared and private point numbers, a region that starts away from the default, composite glyphs, phantom points), `HVAR` with a delta set index map and two subtables, `MVAR`, a glyph whose left side bearing is not the left of its outline |
| `fonts/synthetic-variable-phantom` | ttf | 1 | CC0-1.0 | The same font without `HVAR`: the advances vary with the phantom points, and a glyph varies its origin |
| `fonts/synthetic-variable-charstrings` | otf | 1 | CC0-1.0 | A variable font with CFF2 outlines: `blend` of one and of several values, `vsindex` in a private DICT and in a charstring, a blending subroutine, two font DICTs, a variation store of two subtables |
| `fonts/noto-sans-variable` | ttf | 1 | OFL-1.1 | Noto Sans 2.015 variable (weight and width), subset to 123 characters (279 glyphs) with every variation table kept: `gvar`, `HVAR`, `MVAR`, `avar`, positions and anchors of `GPOS` that vary |
| `fonts/noto-sans-cjk-jp-variable` | otf | 1 | OFL-1.1 | Noto Sans CJK JP 2.004 variable (weight), subset to 133 characters (566 glyphs): a CID-keyed `CFF2` table with several font DICTs, blended hints kept, `HVAR`, `avar` |
| `fonts/noto-naskh-arabic` | ttf | 1 | OFL-1.1 | Noto Naskh Arabic 2.019, subset to 134 characters: the letters of Arabic, Persian and Urdu, the vowel marks and the marks that are reordered, both sets of digits, punctuation, the joiners and a few presentation forms, with the forms, the ligatures and the anchors of these letters |
| `fonts/noto-nastaliq-urdu` | ttf | 1 | OFL-1.1 | Noto Nastaliq Urdu 4.000, subset to the letters of Urdu: cursive attachment, marks on letters that were moved, glyphs inserted to make room for dots, many contextual lookups |
| `fonts/noto-sans-hebrew` | ttf | 1 | OFL-1.1 | Noto Sans Hebrew 3.001, stored as it is: letters, points, cantillation marks, precomposed letters, with anchors for the marks |
| `fonts/noto-sans-hebrew-without-positioning` | ttf | 1 | OFL-1.1 | The same font without its `GPOS` table (`hb-subset --drop-tables+=GPOS`), as a font made before OpenType Layout: a letter and its point are then drawn with the precomposed letter |
| `fonts/noto-sans-syriac` | ttf | 1 | OFL-1.1 | Noto Sans Syriac 3.000, stored as it is: the forms of the letters and of alaph (`fin2`, `fin3`, `med2`), marks, and the abbreviation mark that stretches (`stch`) |
| `fonts/noto-sans-devanagari`, `-bengali`, `-gurmukhi`, `-gujarati`, `-oriya`, `-tamil`, `-telugu`, `-kannada`, `-malayalam` | ttf | 1 | OFL-1.1 | The Noto Sans font of each Indic script (versions 2.003 to 2.106), stored as they are: the features of the Indic shaping model under the script tag of the second version of the specifications (half forms, forms below and after the base, reph, conjuncts, presentation forms), anchors for the marks |
| `fonts/noto-sans-devanagari-first-version`, `fonts/noto-sans-malayalam-first-version` | ttf | 1 | OFL-1.1 | The same fonts with the script tag of the first version of the specifications only (`hb-subset --layout-scripts`): the "old behavior" of the model |
| `fonts/noto-sans-khmer` | ttf | 1 | OFL-1.1 | Noto Sans Khmer 2.004, stored as it is: the forms of the subscripts (`pref`, `blwf`, `abvf`, `pstf`, `cfar`), the parts of the split vowels, ligatures of the presentation features |
| `fonts/noto-sans-myanmar` | ttf | 1 | OFL-1.1 | Noto Sans Myanmar 2.107, stored as it is: the features of the Myanmar shaping model under the tag `mym2` (kinzi, medials, stacked consonants), the letters of the extended blocks |
| `fonts/noto-serif-tibetan` | ttf | 1 | OFL-1.1 | Noto Serif Tibetan 2.103, stored as it is: stacks of subjoined letters, vowel signs and marks placed by anchors, and more than two thousand substitution subtables, which a long text tries at each glyph |
| `fonts/noto-sans-sinhala`, `-javanese`, `-balinese`, `-tai-tham`, `-cham`, `-newa`, `-kaithi`, `-lepcha` | ttf | 1 | OFL-1.1 | The Noto Sans font of scripts of the universal shaping model (versions 2.002 to 2.007), stored as they are: among them the features of the model (`rphf` in Sinhala, Newa and Kaithi; `pref` in Javanese, Cham and Newa; `akhn`, `half`, `blwf`, `abvf`, `pstf`, `vatu`, `cjct`), vowel signs Unicode decomposes, a glyph a font moves after the marks of its letter (Lepcha) |
| `fonts/noto-sans-nko`, `-mongolian`, `-adlam` | ttf | 1 | OFL-1.1 | Noto Sans NKo 2.004, Mongolian 3.002 and Adlam 3.002, stored as they are: scripts of the universal model whose letters join (`isol`, `init`, `medi`, `fina`), two of them from right to left; the free variation selectors of Mongolian |
| `fonts/noto-sans-duployan` | ttf | 1 | OFL-1.1 | Noto Sans Duployan 3.002, subset to a few letters, the marks and the format controls: each letter becomes many glyphs, connected by cursive attachments that several lookups make and remake, with marks attached before them; some six thousand subtables |
| `fonts/noto-sans-thai`, `fonts/noto-sans-lao` | ttf | 1 | OFL-1.1 | Noto Sans Thai 2.002 and Noto Sans Lao 2.003, stored as they are: the marks above tall consonants and below consonants that have a tail, the sara am, ligatures of a ring and a tone mark |
| `fonts/noto-sans-cjk-kr-regular` | otf | 1 | OFL-1.1 | Noto Sans CJK KR 2.004, subset to the letters of Hangul (the modern ones, a few of Old Hangul and of the extended blocks, the fillers), the tone marks and thirty syllables, with the glyphs the font has for a letter at each position of a block (`ljmo`, `vjmo`, `tjmo`); CFF outlines, hints kept |
| `fonts/noto-sans-cjk-kr-letters` | otf | 1 | OFL-1.1 | The same subset without any syllable: every block is drawn with its letters |
| `fonts/trt-*` | ttf, otf | 1 | OFL-1.1, Apache-2.0 or CC0-1.0 (see the manifest) | Ten fonts of the text-rendering-tests suite of the Unicode Consortium, stored as they are (`fonts/text-rendering-tests/`), each made for one feature: `avar`, `gvar` (also composite glyphs and regions between two values), `HVAR` with TrueType and with CFF2 outlines, `CFF2`, feature variations (`rvrn`) with both kinds of outlines, anchors of `GPOS` that vary |
| `fonts/synthetic-color-boxes` | ttf | 1 | CC0-1.0 | A color font of rectangles: `COLR` version 0 (layers, a layer with the color of the text), `CPAL` version 1 with two palettes and their types |
| `fonts/synthetic-color-transforms`, `-gradients`, `-clips` | ttf | 1 | CC0-1.0 | `COLR` version 1, one glyph per case: opacity, translation, scale, rotation, skew, matrix, nested clips, compositions; linear, radial and sweep gradients with each extend mode and the degenerate color lines; clip boxes, a glyph that paints another one, a glyph that paints itself |
| `fonts/colr-test-glyphs`, `fonts/colr-test-glyphs-variable` | ttf | 1 | Apache-2.0 | The COLRv1 test glyphs of the googlefonts/color-fonts project, stored as they are: 220 glyphs, one per feature of the paints (every composition mode, extend modes of each gradient, transformations, clip boxes, the color of the text, cycles), and their variable form (18 axes that vary the fields of the paints) |
| `fonts/colr-twemoji-smiley`, `fonts/colr-twemoji-smiley-cff2` | ttf, otf | 1 | CC-BY-4.0 | Fifteen emoji of the Twemoji set as `COLR` version 1 paints, with TrueType and with CFF2 outlines, from the same project |
| `fonts/synthetic-bitmap-boxes`, `fonts/synthetic-color-bitmaps` | ttf | 1 | CC0-1.0 | Glyphs that are PNG images of a few flat colors, in strikes of 16 and 32 pixels per em: an `sbix` table (an image that stands for another glyph's, a glyph without image) and `CBLC` and `CBDT` tables; one glyph is an outline only |
| `fonts/sbix-twemoji-smiley` | ttf | 1 | CC-BY-4.0 | The fifteen emoji of the same project as PNG images of an `sbix` table, in one strike of 109 pixels per em |
| `fonts/*-woff`, `fonts/*-woff2` | woff, woff2 | as the original | as the original | WOFF of the boxes, deltas, charstrings, Noto Sans and Noto Sans CJK fonts; WOFF2 of the boxes, curves, deltas, charstrings, collection and both Noto Sans fonts |
| `fonts/synthetic-boxes-woff2-metrics`, `fonts/synthetic-deltas-woff2-boxes` | woff2 | 1 | CC0-1.0 | Written by `FontWoff2Writer`: transformed horizontal metrics (both flags, then one), every bounding box stored |
| `fonts/synthetic-variable-axes` | ttf | 1 | CC0-1.0 | The synthetic variable font with version 2 of the `avar` table: a variation store whose deltas change the normalized weight and width from both of them, through a delta set index map |
| `fonts/synthetic-languages` | ttf | 1 | CC0-1.0 | The boxes font with a language system for each of the 680 tags of the registry of language system tags of OpenType: the `locl` feature of each one replaces A by four glyphs that tell which one it is |
| `fonts/synthetic-monochrome-boxes` | ttf | 1 | CC0-1.0 | The boxes font with strikes of one bit per pixel (`EBLC` and `EBDT`) at 12 and 16 pixels per em, whose bitmaps do not draw what the outlines draw. The reference tools do not draw these bitmaps: the vectors are those of the outlines, and the bitmaps have literals in the tests |
| `fonts/synthetic-marks` | ttf | 1 | CC0-1.0 | A font without substitution and positioning tables whose marks, of every kind of combining class, are all drawn at one place, with letters whose ink is not in the middle of their advance |
| `fonts/synthetic-arabic-forms` | ttf | 1 | CC0-1.0 | A font of Arabic without substitution and positioning tables: a glyph for each letter and for each character of the Arabic Presentation Forms blocks |
| `fonts/synthetic-thai-forms`, `fonts/synthetic-thai-forms-mac` | ttf | 1 | CC0-1.0 | A font of Thai without substitution and positioning tables: a glyph for each character of the Thai block and for each form of the private use area, at the code points of the fonts of Windows (U+F700 to U+F71A) or of Mac OS (U+F884 to U+F89E) |
| `fonts/noto-color-emoji` | ttf | 1 | OFL-1.1 | Noto Color Emoji 2.057 (the `COLR` version 1 build), subset to 60 characters of each kind of emoji sequence: emoji that are emoji or text by default, the skin tone modifiers, people and what joins them, four regional indicators, the keycap, the tags of a subdivision flag; the ligatures of their sequences, and their variation sequences with U+FE0F |

### Shaping vectors

A variable font also has **instances** in its vectors: for values of its axes listed in `FontCorpus.cs`, the metrics,
the advance of each glyph and the outline of each glyph. The advances are whole font units, on which the two readers
must agree. The points of an instance are fractions of font units: HarfBuzz keeps them, with the precision of single
precision numbers, where FreeType rounds the deltas of the offsets of components and gives whole units for CFF2
outlines. The outlines recorded are those of HarfBuzz to three decimals (`hb-vector --precision=3`), once FreeType has
drawn the same segments within two units; the tests accept a difference of 0.005 unit. The metrics of an instance are
those of `hb-info`. A glyph whose origin varies is only in the font without `HVAR`: with that table, FreeType takes the
side bearings from it and HarfBuzz from the phantom points.

The fonts of the text-rendering-tests suite come from `test/shape/data/text-rendering-tests/fonts` of the checkout of
HarfBuzz the AOTS suite comes from, copied to a `text-rendering-tests` directory of the fonts directory. Only the fonts
whose own license (the one their `name` table declares, or the Apache-2.0 license of the suite when they declare none)
is accepted by the corpus are stored; the fonts of the suite under another license are not.

A color font has **renders** instead of outline vectors (`renders` of its entry, `FontRender` in the test harness):
lines of its characters drawn by `hb-raster`, the rasterizer of the HarfBuzz utilities, as PNG files of
`fonts/renders/`. A line is drawn in its box (`--logical`, with the ascent and the descent given by
`--font-extents`, so that they are whole pixels) with a margin of 32 pixels, on a white background: the origin of its
first glyph is at (margin, margin + ascent). The tests draw the same line at the same place and compare the images
(`Conformance/ColorFontConformanceTests`): the two rasterizers anti-alias edges differently and the reference one
works with 8-bit colors, so the test bounds the mean difference, the part of the pixels that differ by more than 24
levels, and the size of a block of such pixels, which an edge never is. This is a cross-check of the whole pipeline,
not an exact reference: the exact expectations are the literals of `ColorFontTests`, computed by hand on the
synthetic color fonts, which `hb-raster` also draws.

A few characters are left out of the lines (`CharactersWithoutReference` in `FontCorpus.cs`) because the reference
renderer does not draw them as the OpenType specification says: it draws nothing for a sweep gradient that pads and
whose angles are equal or whose stops are at one offset (the specification gives it two colors), and nothing for a
glyph that paints the same glyph several times, which is not a cycle. These cases have literals in `ColorFontTests`.

The fonts whose glyphs are images are drawn at the sizes of their strikes (`--font-ppem`, without which the reference
renderer takes the largest strike), where a pixel of an image is a pixel of the render. The reference renderer draws
an image one pixel higher than the specification places it (the row of an image that is on the baseline is drawn above
it): a render records that shift (`imageShift`, in pixels, down when positive) and the test draws its line there. A
line of these fonts has no glyph that is an outline, which the reference renderer does not shift.

Regenerating the renders and the vectors runs the reference tools thousands of times. When
`MEZIANTOU_FRAMEWORK_IMAGING_CORPUS_CACHE` names a directory, the generator keeps there the output of each command,
keyed by its arguments and by the content of its input files, and reuses it; the variable is not set by default.

The fonts of the googlefonts/color-fonts project (`fonts/color-fonts/`) come from its `fonts` directory at the commit
`FontCorpus.cs` pins. The test glyphs are generated by a script of the project (Apache-2.0); the emoji are those of
Twemoji (CC-BY-4.0, Twitter, Inc and other contributors).

The `shaping` file of a font (`ShapingVectors` in the test harness) holds texts and what the OpenType shaper of
HarfBuzz gives for them: `hb-shape --shapers=ot --font-funcs=ot --no-glyph-names`, with the features of the case, for a variable font the
instance of the case (`--variations`), and the language of the case when it has one (`--language`, a BCP 47 language tag). A text
is stored as the hexadecimal code points of its characters, so that no invisible character is lost in an editor; a
glyph is six numbers: the glyph identifier, the cluster (an index of character), the two offsets and the two advances,
in font units. The texts are listed in `FontCorpus.cs`, chosen for what shaping does to them: kerning, ligatures, marks
that compose or are attached, invisible characters, spaces and hyphens a font lacks, fractions, features turned on
and off. `ShapingConformanceTests` compares the glyphs, the clusters, the place each glyph is drawn at and the
advance of the text; how an adjustment is split between an advance and an offset is not compared. A case with a
`direction` (`ttb`) is a vertical line of upright glyphs (`--direction=ttb`): the vertical forms of a font, its advance
heights and its vertical origins, in fonts that have the tables of vertical text and in fonts that do not.

The reference shaper guesses the script of a text from its first letter and lays it out in the direction of that
script; the test does the same, so a right-to-left text is compared in the order its glyphs are drawn in. The texts of
Arabic, Urdu, Hebrew and Syriac are about what the shaping model of these scripts does: the forms of the letters and
what joins or separates them, the order of the marks, the ligatures and what keeps them from forming, the forms of the
Syriac alaph, the mark that stretches over a word, the precomposed Hebrew letters. Those of Thai and Lao are about the
sara am and the order of the marks; those of Hangul about the letters that make a syllable or not, in a font that has
the syllables and in one that does not, and about the tone marks. The Indic scripts share a set of cases written with
the letters they have at the same place of their blocks (conjuncts, reph, joiners, the vowel sign drawn before its
syllable, signs without a base, each feature of the model turned off), followed by the cases of each script. Those of
Khmer are about the subscripts, the ro among them, the vowels with two parts and the register shifters; those of
Myanmar about the kinzi, the medials, the marks whose order changes and the bases that are not letters. Those of the
scripts of the universal model are about the classes of their characters (what a cluster can have, and in which order),
the glyphs that are moved, the forms of the letters that join, and what is not a cluster. Several of them were found by
shaping random sequences of the characters of a script with both shapers.

The fonts without layout tables are about what a shaper does in their place. For the fonts without positioning table
(the Hebrew font without `GPOS`, the marks font), the reference shaper places each mark from the shapes of the glyphs
and from its combining class, by rules no specification gives: the positions are compared like any other, on a mark
of each kind of class, on marks that stack, and on marks a font draws lower or higher than the others. The Arabic
font without substitution table has the forms of its letters as characters, and the Thai ones the forms of their marks
in the private use area. The emoji font is about the sequences a font draws with one glyph, and about the variation
selector that is part of a sequence the font has.

The languages font has a case for each of some 1,500 language tags (the codes of two letters, the codes of the
registry of OpenType, the languages of a macrolanguage and the deprecated subtags of the registry of BCP 47, and
tags with a script, a region or a variant): the glyphs tell which language system the shaper took. The differences
with the reference shaper are listed in `ShapingConformanceTests`, by group, with the reason of each.

There is one reference shaper: the pinned tools have no other one. A difference with it is settled by the
specifications, and a deliberate one is listed in the test with its reason.

### Font suites

The `fontSuites` section lists third-party suites of test fonts, each a directory stored as published and identified
as a whole: the number of its files and the SHA-256 of their list (for each file, by ordinal order of its path, the
path, a line feed, the SHA-256 of its content, a line feed).

| Suite | License | Content |
| --- | --- | --- |
| `font-suites/aots` | Apache-2.0 | The Annotated OpenType Specification tests of Adobe, as the HarfBuzz project distributes them (`test/shape/data/aots` at the commit the manifest records): 205 fonts that each exercise one format of the `cmap`, class definition, coverage, `GSUB` or `GPOS` tables, and 128 files of tests (a font, the options of `hb-shape`, characters, the expected glyphs and positions). `ShapingSuiteTests` runs every test that is not commented out; the commented ones are the cases HarfBuzz itself does not pass |

The generator copies a suite from a directory of the fonts directory (`aots`) and refuses a content whose
hash is not the pinned one. The tools HarfBuzz keeps next to the suite (its test driver and build files) are not copied.

## Color management

The `colorProfiles` and `colorTransforms` sections of the manifest are the reference of ICC color conversion
(`ColorConversionGoldenTests`). They are not image fixtures: they have no `format`, `kind` or expected pixels.

- **`colorProfiles`**: ICC profiles (`icc/profiles/`) with their hash, provenance, license and feature tags
  (`icc.version`, `icc.model`, `icc.pcs`, `icc.curve`, `icc.space`, `icc.class`, `icc.direction=both` for tables in both
  directions, `icc.media-white` and `icc.black-point` for a media white that is not the illuminant and a black that is
  not black). Two origins:
  - *external*: the whole [Compact-ICC-Profiles](https://github.com/saucecontrol/Compact-ICC-Profiles) collection
    (`CC0-1.0`, 44 profiles) at a pinned commit, stored unmodified: matrix-based RGB profiles of versions 2 and 4 with
    parametric, sampled, gamma and identity curves, monochrome profiles, and a CMYK input profile with one `lut16Type`
    table. Each one is the source and the destination of at least one conversion, except the CMYK profile, which has no
    table to be a destination; the generator rejects a checkout that has a profile the corpus does not list.
  - *generated* (`synthetic-*`): built by `Common/IccProfileBuilder.cs` of the generator, a file without any dependency
    on the library that the test projects also compile (`IccTestProfiles`). They provide what no profile under an
    allowed license does: `lut8Type`, `lutAToBType` and `lutBToAType` tables, tables in both directions (including to
    CMYK, one per rendering intent), the CIEXYZ connection space with tables, media white points and black points. Their
    tables sample simple device models: they are not realistic, only well defined.
- **`colorTransforms`**: for a source profile, a destination profile, a rendering intent and the black point
  compensation setting, a file of vectors (`icc/vectors/<id>.u16le`): for each color, the source device samples then
  the samples **converted by an independent color management system**, as little-endian 16-bit numbers covering the
  device range. The reference is LittleCMS, through `transicc -n -c0` (no precalculation: each color is evaluated,
  not interpolated in a device link; `-b` for black point compensation); its exact version and command line are
  recorded in `reference`.
- **Comparison**: two color management systems never agree bit for bit. Each entry declares `maxAbsoluteError` and
  `maxMeanAbsoluteError` in 16-bit units (257 is one level of 255; at most 64) with the measured `justification`.
  `ColorConversionGoldenTests` also fails when a declared limit is more than twice the measured difference, so a
  tolerance cannot be left wider than needed. The vectors exist to catch errors of interpretation of the ICC
  specification (encodings, table and element order, tag selection, rendering intents, media white points, black
  points), which change samples by whole levels and which the reference of the test harness (`ReferenceIccTransform`)
  would share with the library.
- **Colors**: every 8-bit level and 64 random 16-bit values for grayscale sources; a 7-level grid and 157 random 16-bit
  colors for RGB sources. For CMYK sources, every grid point of the color lookup table of the intent (the device
  values that its input tables map to the grid).
- **What the vectors do not cover**, because LittleCMS is not a valid judge there (`ReferenceIccTransform` and the
  hand-computed values of `IccColorTransformTests` cover these):
  - *Four-channel interpolation between grid points.* The ICC specification does not define the interpolation and
    color management systems differ: on the coarse 6-point table of the external CMYK profile, LittleCMS (linear along
    the first input, tetrahedral on the others) and Apple ColorSync (multilinear, like the library) are several levels
    of 255 apart for some colors, while they agree on grid points. For one and three inputs the two systems use the
    same interpolation as the library, so those colors are not restricted to grid points.
  - *Black point compensation where LittleCMS 2.19 was observed not to follow the published algorithm.* It applies no
    compensation with the relative colorimetric intent to a table-based monochrome profile whose device black is not
    black, nor (with the CMYK output profile of macOS) to a CMYK output profile used as a source; and with the
    perceptual intent of a version 4 profile it compensates even when not asked to. The library follows Adobe's
    published algorithm in all three cases. The compensated vectors are therefore a monochrome profile
    with a tone curve (both directions) and a version 2 table-based monochrome profile with the perceptual intent
    (as a destination, where the black point is read from the round trip of a lightness ramp, and as a source).

## Comparison policies

- `exact`: every sample is identical, including 16-bit low bits and the defined color of fully transparent pixels.
  Used for every lossless fixture (PNG, APNG, GIF, lossless WebP and WebP animations of lossless frames).
- `tolerance`: only for lossy inputs compared with an independent decoding of the **same encoded input** (JPEG). Color
  samples within `maxAbsoluteError` and mean color error within `maxMeanAbsoluteError` (8-bit scale, x257 for 16-bit).
  The validator requires: a justification; values at most 12 / 2.0; at least one measured cross-check with another
  independent decoder showing nonzero differences; a tolerance no looser than `measured max + 1` (at least 2) and
  `1.5 x measured mean + 0.1` (at least 0.5); and proof that channel swaps and flips of each reference are rejected.
  JPEG fixtures decoded by the sequential and progressive decoders use tighter policies: the library's measured
  disagreement with the `djpeg` reference (`LibraryJpegMeasurements` in the generator: max(1, measured max) and measured
  mean + 0.01, rounded up to 0.01), which `Jpeg.Tests/Conformance/JpegDecoderConformanceTests` checks against a fresh
  measurement; independent decoders whose differences exceed that policy are recorded as `differs` (with notes) and still
  bound the justified tolerance. Lossy WebP fixtures follow the same scheme: the reference is libwebp `dwebp`'s decoding of
  the input, the policy is the library's measured disagreement with it (`LibraryWebPMeasurements` in `WebPCorpus.cs`,
  checked by `WebP.Tests/Conformance/WebPDecoderConformanceTests`), and the independent cross-check is FFmpeg's libavcodec VP8
  decoding to raw `yuv420p` planes converted by the generator with exact BT.601 rationals (FFmpeg's own swscale RGB
  conversion is recorded but excluded from the tolerance: its chroma sample positions differ from centered upsampling).
- AVIF: decoding AV1 is exact, so the samples that no matrix applies to (identity-matrix RGB, monochrome, alpha) are
  compared exactly, lossy coding or not. Matrixed samples use `tolerance` with a maximum of **one unit**: the reference is
  the exact-rational conversion of the planes (`TestHarness/Heif/ReferenceYuvConverter`: H.273 equations, the documented
  bilinear chroma upsampling at the declared sample position, one rounding), and the library documents coefficients
  rounded to 16 fractional bits. The mean is the library's measured disagreement + 0.01 (`LibraryAvifMeasurements` in
  `AvifCorpus.Container.cs`, checked by `Avif.Tests/Conformance/AvifDecoderConformanceTests`; currently 0 for every 8-bit
  fixture). The cross-check that justifies a tolerance is libavif `avifdec`, whose own conversion differs from the exact
  one by 1 to 75 units (it ignores the chroma sample position); 16-bit errors are recorded on the 8-bit scale (units of 257).
- HEIC: the same policy as AVIF, for the same reason: decoding HEVC is exact, so identity-matrix RGB, monochrome and alpha
  samples are compared exactly, and matrixed samples within **one unit** of the exact-rational conversion of the planes by
  `ReferenceYuvConverter`, at the chroma sample position that Rec. ITU-T H.265 gives to the picture
  (`ReferenceYuvConverter.GetHevcChromaSiting`). The mean is the library's measured disagreement + 0.01
  (`LibraryHeicMeasurements` in `HeicCorpus.Container.cs`, checked by `Heic.Tests/Conformance/HeicDecoderConformanceTests`).
  The cross-check that justifies a tolerance is libheif `heif-dec`, whose own conversion differs from the exact one by 1 to
  96 units (it upsamples the chroma as if it were centered); a matrixed fixture on which `heif-dec` agrees exactly with
  the reference is compared exactly.
- Whatever the policy: alpha is compared exactly; dimensions, layouts, buffer lengths, frame counts, poster presence,
  durations, total plays and orientation are compared exactly. A tolerance can never mask channel swaps, missing frames,
  alpha loss or gross errors.

## Reference provenance

`reference.method` is `hand-computed` (pattern or literal frames written in the generator from the specifications; for
inputs encoded losslessly by FFmpeg the pattern itself is the reference) or `decoded` (decoder, actual backend and exact
command recorded). `reference.crossChecks` records the comparison with other genuinely independent implementations:
FFmpeg/libavcodec for every PNG/APNG/GIF fixture (all `exact`, except `gif/disposal-transparency`: `equivalent`, FFmpeg
fills disposed areas with transparent white instead of transparent black; and `png/gray4-trns-key`: `differs`, FFmpeg
ignores `tRNS` for grayscale below 8 bits, so Apple ImageIO is recorded as the second independent decoder and agrees
exactly — the generator requires such an arbiter for every FFmpeg disagreement on a hand-authored PNG), FFmpeg plus Apple
ImageIO (`CGImageSource` through `tools/Meziantou.Framework.Imaging.CorpusGenerator/imageio_frames.swift`, straight-alpha
frames, never converted) for the APNG compositing fixtures and the GIF decoding fixtures (`--binary-alpha`: ImageIO's
opaque and premultiplied GIF frames are accepted only when every alpha is 0 or 255, which converts losslessly), and FFmpeg
plus Apple ImageIO for JPEG (libjpeg-turbo `djpeg` is the reference; FFmpeg's odd-width chroma conversion artifact is
recorded and excluded from the tolerance; for progressive fixtures djpeg's optional block smoothing is inactive because
every scan script completes the DC and AC coefficients 1-9, so the reference is a plain decoding of the coefficients as
sent). Disagreements are documented, never hidden.

12-bit JPEG fixtures (`jpeg/precision12-*`, `jpeg/progressive-precision12-*`): `cjpeg -precision 12` encodes a pattern of
12-bit samples and `djpeg` writes the decoded 12-bit samples as they are (a portable anymap with the maximum value 4095).
The reference is each sample `s` scaled to 16 bits as the library does, `(s * 65535 + 2047) / 4095`, in 16-bit layouts;
the errors are recorded on the 8-bit scale like every tolerance (units of 257, the maximum rounded up), so three units of
a 12-bit sample count as one. The cross-checks are FFmpeg, whose decoder shifts the samples left by four bits instead (a
difference below one unit of the 8-bit scale) and whose planes are converted by swscale with its default scaler (the
bilinear scaler of FFmpeg 9.0.2 does not convert subsampled planes of more than 8 bits), and Apple ImageIO, whose 16-bit
PNG output FFmpeg reads back without loss. Both upsample the chroma of 12-bit files very differently from `djpeg` on
detailed content (maximum 19 to 44, mean 5 to 10), which bounds what independent decoders justify; the tolerance of each
fixture is the disagreement measured for the library (`LibraryJpegMeasurements`), which decodes like `djpeg`. The ISO
JPEG reference codec (below) is a third cross-check, the only one of `jpeg/precision12-rgb-adobe`, whose 12-bit RGB
samples (Adobe transform 0) neither FFmpeg nor ImageIO reads as RGB: they convert them from YCbCr, so they are recorded
and excluded (the generator checks that they still do).

Arithmetic-coded JPEG fixtures (`jpeg/arithmetic-*`, `jpeg/progressive-arithmetic-*`): `cjpeg -arithmetic` encodes them
and `djpeg` is the reference, as for the Huffman-coded ones. FFmpeg has no arithmetic decoder (the generator checks that
it still fails), and Apple ImageIO agrees exactly with `djpeg` on the files without chroma subsampling, where its own
decoder differs on Huffman-coded files, so it is recorded and excluded from the tolerance; it rejects 12-bit samples with
arithmetic coding. One cross-check is FFmpeg on the file `jpegtran` rewrites with Huffman codes, which has the same
coefficients: it checks everything but the arithmetic decoding itself. The fixtures with chroma subsampling therefore
have even dimensions (FFmpeg's conversion of odd chroma planes is excluded from tolerances). The other is the ISO JPEG
reference codec (thorfdbg/libjpeg, the `jpeg` tool of the meziantou/prebuilt release pinned by the `Meziantou.Prebuilt`
package), the only arithmetic decoder that shares no code with libjpeg-turbo: it decodes every arithmetic-coded fixture
to the same pixels as its Huffman-coded rewrite (the generator checks it), so it confirms the coefficients the reference
decodes. It also cross-checks every 12-bit fixture. It upsamples the chroma with a filter of its own, which differs
from the triangle filters of `djpeg` and of the library by more than the IDCT and the color conversion do: on fixtures
with chroma subsampling it is recorded and excluded from the tolerance. The tool exits with 0 when it fails, so the
generator checks its output instead. The unit tests decode arithmetic-coded streams written by the test-side encoder
(`JpegTestArithmeticCoder`), 8-bit and 12-bit, with every conditioning, and the interoperability tests
(`JpegDecoderDifferentialTests`) decode files the reference codec writes at test time.

Lossless JPEG fixtures (`jpeg/lossless-*`, `BuildLosslessJpeg`), compared exactly:

- **Written by `cjpeg -lossless`** (grayscale, and RGB with the Adobe transform 0, at 5, 8, 10, 12 and 16 bits): the
  expected pixels are the pattern given to `cjpeg`, since the file stores its samples, without the low bits a point
  transform drops, and scaled to 16 bits when the precision is not 8 bits. `djpeg`, which writes the samples in a
  portable anymap whose maximum value is `2^P - 1`, and FFmpeg must both decode exactly that. FFmpeg gives the samples
  as they are stored, or shifted to the width of its pixel format, which the generator recognizes. A recipe may declare
  that FFmpeg disagrees and why: `jpeg/lossless-gray16-largest-difference` codes the difference 32,768, whose category
  16 has no additional bit (T.81 table H.2), and the decoder of FFmpeg 9.0.2 reads some. The generator fails when a
  declared disagreement disappears, and when an undeclared one appears. Apple ImageIO does not read these files (it
  fails on the 8-bit and 16-bit ones), so it is not a cross-check.
- **Written by FFmpeg's `ljpeg` encoder** (`jpeg/lossless-ffmpeg-ycbcr-*`): YCbCr files, with the chroma planes
  subsampled in an interleaved scan, which no libjpeg-turbo tool writes. The encoder declares the vertical sampling
  factor 2 for every component of its 4:4:4 and 4:2:2 files, so that an MCU holds two lines of each. `djpeg` decodes
  these files but refuses to convert their colors ("Unsupported color conversion request": a conversion is not
  lossless), so the reference has two stages: the Y, Cb and Cr planes FFmpeg decodes, which are the stored samples,
  then the chroma upsampling and the YCbCr to RGB conversion of the frames of the other processes, computed by the
  generator (`CommonCorpus.JpegUpsample`, `CommonCorpus.JpegYCbCrToRgb`) from the rules the DCT fixtures compare with
  `djpeg`. The colors swscale converts the same planes to are recorded and excluded: they differ by one unit without
  subsampling, and by its own interpolation of the chroma with it.
- **Rejections**: the frame marker of a lossless file is replaced by SOF11 (the lossless process with arithmetic
  coding, which no tool at hand writes) for the unsupported fixture; the invalid ones have a predictor of 0, a point
  transform that leaves no bit, a scan cut short, and a restart interval that is not a whole number of lines.

Four-component JPEG fixtures (`BuildInkJpeg`, `inkConversion`): LittleCMS `jpgicc` separates an RGB JPEG into a YCCK
file, `jpegtran` rewrites its scans (progressive, restart intervals) without changing a coefficient, and the generator
edits the Adobe APP14 segment for the CMYK variants (transform 0, or no segment). No pinned tool writes the four planes
of such a file and none converts them the way a caller asks, so the reference has two independent stages:

- **Samples**: `jpegtran` codes each component in its own scan (the same coefficients), and the generator regroups the
  scans, with their tables and their entropy-coded bytes unchanged, into files libjpeg-turbo `djpeg` decodes: one
  grayscale file per component, and for YCCK a three-component file that `djpeg` reads as YCbCr and converts to RGB.
  Stored ink samples are complements (255 is no ink), with or without Adobe segment, and the YCCK transform complements
  the first three again: the RGB samples `djpeg` gives are the cyan, magenta and yellow amounts.
- **Colors**: the ink amounts are converted by the decimal reference of the test harness (`ReferenceIccTransform`, its
  own profile reader), from the embedded profile or from `icc/cgats001-compat-v2-micro` (the profile the library
  assumes: `JpegDecoderConformanceTests` checks they are the same bytes) to the destination profile.

The cross-checks are other color management systems: LittleCMS `transicc` on the same inks, and for the fixture with an
embedded profile Apple ImageIO and ColorSync on the whole file (`sips -m`, perceptual intent), which shares nothing with
the reference, not even the reading of the ink polarity and of the YCCK transform. They interpolate a four-dimensional
table differently between its grid points, so they are a few levels apart (maximum 2 to 9, mean 0.4 to 0.6): this
bounds an error of interpretation and the tolerance independent implementations justify. The tolerance of each fixture
is the much smaller disagreement measured for the library (one inverse DCT rounding unit amplified by the conversion:
maximum 1, `LibraryInkMeasurements`), pinned by `JpegDecoderConformanceTests`.

APNG compositing fixtures (`build_apng_compositing`): every displayed frame must be confirmed by at least one of the two
decoders (exact, or identical except hidden colors), or by a verified rounding explanation, unless the fixture lists it as
unconfirmed with a reason in its notes. The generator checks each recorded explanation: `rounding` (visible differences of
at most one unit, only on the translucent OVER results: FFmpeg and ImageIO truncate where the
library contract rounds to nearest), `frames` (only the
listed frames differ: ImageIO returns the second animation frame at index 0 when a separate poster precedes several
frames), `failure` (FFmpeg's demuxer rejects a partial first frame after a poster; its decoder stops at 16-bit OVER
frames) and `invalid` (ImageIO's 16-bit OVER output is not an alpha composite). The 16-bit OVER results of
`apng/over-alpha-rgba16` (frames 1 and 2) and the partial first frame of `apng/poster-rgba16-dispose` have no independent
confirmation: they are written from the contract (`over()` with exact rationals, checked against literal values) and from
the specification.

GIF decoding fixtures (`build_gif_decoding`) follow the same rule with GIF-specific explanations, each verified pixel by
pixel by the generator: `background` (every differing pixel is transparent black in the reference and the opaque
logical-screen background color, or transparent, in FFmpeg's output), `opaque` (every differing pixel is transparent black
in the reference and opaque black in ImageIO's output: ImageIO returns frames without transparent index as opaque images),
`frames` (only the listed frames differ: undefined disposal values) and `failure` (FFmpeg drops an image outside the
logical screen). The generator also decodes every new LZW datastream with its own reader (`lzw_analyze`) to check the
encoded indices and to derive the `gif.lzw.*` features from the bytes (`noInitialClear`, `missingEndCode`,
`dataAfterEndCode`, `kwkwk`, `clearCodes=N`, `maxCodeSize=N`, `tableFull`, `deferredClear`).

WebP fixtures (`WebPCorpus.cs`): lossless inputs are hand-defined patterns encoded by libwebp `cwebp -lossless -exact` (the
pattern is the reference, `hand-computed`), cross-checked with `dwebp` and FFmpeg; lossy inputs are `decoded` by `dwebp`
(see the comparison policies); animation canvases are composed by the generator from the WebP container specification
(disposal clears the frame rectangle to transparent black, the `ANIM` background color is never painted, the first frame is
drawn with SOURCE) with the exact OVER rounding of the library,
and cross-checked with libwebp `anim_dump` (whose 8-bit blending truncates: verified by the generator to differ by at most
one unit on translucent OVER results and on hidden colors only). FFmpeg 6.1.1 does not decode animated WebP, which is
recorded. Malformed and limit inputs are produced by byte edits of valid files written by the generator.

QOI fixtures (`QoiCorpus.cs`): hand-assembled chunk streams, each chunk written next to the literal pixel it must produce
(every chunk form, bias extremes, wrap-around, the initial pixel's index slot after a leading run, an unset slot, runs of
1 and 62 across rows, a 3-channel stream with alpha-changing chunks, the linear colorspace), and patterns encoded by the
qoi.h reference encoder (compiled from the file pinned by SHA-256 in the script, passed with `--qoi-header`) and by
FFmpeg's QOI encoder; QOI is lossless, so the pattern is the reference (`hand-computed`). Every input is decoded exactly by
a strict transcription of the specification in the generator, by qoi.h and by FFmpeg. `expected.transferFunction` records
the colorspace label (`srgb`, the default when absent, or `linear`); it never changes the expected samples. Malformed
inputs are byte edits of valid streams confirmed by the strict transcription (qoi.h does not validate the end marker or
truncation).

BMP, TGA and Netpbm fixtures (`BmpCorpus.cs`, `TgaCorpus.cs` and `PnmCorpus.cs`): files assembled byte by byte from literal pixel grids (every supported
Windows and OS/2 DIB header, bit depth, mask layout, palette, row order, padding and run-length command; every TGA image type, depth, index
width, origin bit, color-map offset and packet kind; every Netpbm magic number, tuple type, `MAXVAL` and comment placement), plus files produced by
FFmpeg's independent BMP, TGA, PPM and PAM encoders from hand-defined patterns (the three formats are lossless, so the
pattern is the reference). Every input is decoded by a strict transcription of its specification in the generator and
cross-checked with FFmpeg; the comparison policy stays `exact`, and the disagreements FFmpeg has with the
specifications are recorded as cross-checks with their measured error or notes instead of being hidden:

- FFmpeg expands 5-bit channels of 16-bit BMP and TGA samples by replicating their high bits, where the references use the
  exact ratio `round(value * 255 / 31)` (at most one unit per sample);
- FFmpeg reads the fourth byte of a 32-bit BMP `BI_RGB` payload and of a 32-bit TGA whose descriptor declares no alpha bit
  as alpha, where the specifications leave it unspecified (every color sample agrees exactly);
- FFmpeg does not decode two OS/2 2.x variants: a `BITMAPCOREHEADER2` shorter than 40 bytes and `BCA_RLE24`. Their
  references are the literal pixels of the generator, confirmed by its strict transcription and by the independent reader
  of the test harness. FFmpeg agrees exactly with every other run-length and OS/2 fixture, including the pixels a
  run-length stream skips (palette index 0);
- FFmpeg decodes every two-byte TGA pixel as 5-5-5 true color, whatever the image type: it implements neither 16-bit
  grayscale (a gray byte and an attribute byte) nor 16-bit color-map indexes, so the literal grids are the only reference
  of these fixtures. Pillow and Apple ImageIO, run by hand when the fixtures were authored, read 16-bit grayscale as gray
  and alpha and reject 16-bit indexes.

AVIF fixtures (`AvifCorpus.Container.cs`, recipes in `AvifCorpus.ContainerFixtures.cs` and `AvifCorpus.ContainerErrors.cs`):
inputs are written by libavif `avifenc` from hand-defined patterns (two sequences by the FFmpeg `avif` muxer, around
libaom and SVT-AV1 frames), or assembled
box by box (`TestHarness/Heif/HeifFileBuilder`) around the AV1 payloads of such a file for what the encoders do not write:
media data before the index, item data in `idat`, several extents, free and unknown boxes, a `mif1` major brand, 64-bit
box sizes, a box that extends to the end of the file, files without `colr` box (after and before their media data, with
and without color description in the bitstream) or with an ICC profile only, a grid smaller than its tiles, a 3x2 grid of
16x16 tiles coded one by one, sequences without edit list or with `co64`/`stz2` tables, sequences with Exif and XMP items
(`avifenc` crashes on `--exif` with several frames) in the `meta` box of the file or of the track, in `mdat` or `idat`,
before or after the samples, and a sequence whose chunks are written from the last one to the first one. Every reference is `decoded`, never taken from one tool: the structure
is read by `TestHarness/Heif/ReferenceHeif` (an independent reader written from ISO/IEC 14496-12, ISO/IEC 23008-12 and
the AV1 Image File Format), the AV1 payloads of the items and of the samples are extracted and decoded to planes by
**two** decoders, dav1d and libaom, through the pinned FFmpeg (the same command and the same rule as the
[AV1 conformance vectors](#av1-conformance-vectors): same bytes or no reference), and the planes are converted by
`ReferenceYuvConverter` with the matrix coefficients and the range of the `colr` box, or else of the bitstream
(unspecified: BT.601). The constant luminance matrix coefficients (10 and 13) and ICtCp (14) go through the transfer
characteristics of the file, which `TestHarness/Color/ReferenceCicpTransfer` evaluates in `decimal` arithmetic with the
series of `DecimalMath`. **No tool judges these conversions**: libavif and FFmpeg have none (avifenc only labels the
planes of a y4m source and avifdec refuses to convert them). What stands for a second opinion is a calculation with
the `decimal` module of Python at 50 digits, whose results are literals of
`AvifDecoderTests.ReferenceConverterMatchesAnIndependentCalculationOfTheMatricesThatGoThroughTheTransferFunction`, and
the round trip of four fixtures: their planes are a pattern converted with the forward equations of ITU-T H.273 and of
Rec. ITU-R BT.2100, coded without loss, and the reference must give the pattern back within the rounding of the planes
(measured: 78/65535 at 10 bits and 1/255 at 8 bits for constant luminance, 31/65535 and 22/65535 at 12 bits for ICtCp,
on a pattern that keeps away from black, where the transfer functions multiply the rounding of the planes). The extents
of the chroma of constant luminance are the ones the equations of H.273 give (for BT.2020: 1.9403, 1.5820, 1.7182 and
0.9938), not the four-digit constants that Rec. ITU-R BT.2020 prints (1.9404, 1.5816, 1.7184 and 0.9936). The images
that the edit list of a sequence shows and their durations are computed by `ReferenceAvif.Track.GetPresentation`, with
exact fractions, one image and one edit at a time (the library counts them run by run and reads them with a cursor);
the durations of the fixtures were also derived by hand in their notes. No pinned tool presents them: libavif only
reads the repetition of an edit list, and FFmpeg presents the primary item of these files. An image that is built
from other images (an overlay, a grid of derived images) is rendered by the generator
(`AvifCorpus.ContainerDerived.cs`): each coded image is decoded by the two reference decoders and converted as an
image that is presented alone, the transformations of the items are applied to pixels, and the overlays are
composited with exact fractions (source-over on straight alpha, rounded to the nearest sample, halves upward).
libavif rejects these files. Apple ImageIO builds them with its own reader and returns an opaque image
(`BuiltImagesDecodeAsAppleImageIO`): the pixels that the reference has opaque are identical where one tile or one
layer covers them (the grid of derived images, the overlay of a grid and of an identity derivation, the overlay with
a transparent fill), and within two units where translucent layers are composited, which ImageIO does not round as
an exact source-over operation. **No tool judges** the transparent and translucent results, the 10-bit overlay
(ImageIO returns another layout) and the lossy layers (measured: up to 17 units, from its own chroma upsampling), nor
the rules on the color descriptions of the input images, which are the reading of ISO/IEC 23008-12 that the library
documents. The samples of a
fragmented sequence are located by the `moof` reader of `ReferenceAvif` (written from ISO/IEC 14496-12 section 8.8,
with the three base rules of the data offsets), and their payloads are those of a sequence that every tool reads:
libavif does not read fragments, and FFmpeg demuxes three of the six fixtures to the frames of their source
(`FragmentedSequencesAreDemuxedByFFmpegAsTheirSource`), not the ones with explicit base offsets or with samples in
both the `moov` box and fragments. The generator tiles the planes of a grid before the conversion (as libavif does: the chroma
upsampling interpolates across tile boundaries), scales the alpha plane, and applies the clean aperture. Lossless
content is also compared with its source pattern. Cross-checks, recorded with their measured differences and never used as
references: libavif `avifdec` for every input (its own parser, dav1d and conversion; it applies `clap`, `irot` and
`imir`, so its output is compared with the reference displayed with the expected orientation, which confirms the
orientation; it applies them in the order of MIAF whatever the order of their association, so it is not compared for the
two files that order them otherwise, whose expected rectangle is derived by hand in the recipe and found again by
`ReferenceAvif.GetPresentedRectangle`, which crops, turns and mirrors a grid of sample positions; it rejects `stz2`, in which case its measurement on the same payloads in `stsz` tables is recorded, and grid
tiles smaller than 64 samples; it returns 8-bit monochrome samples in limited range as stored, without the range, where
the reference scales them as ITU-T H.273 says and as `avifdec` itself does at 12 bits), and
FFmpeg (mov demuxer, swscale) for the still images whose samples no matrix applies to. The expected durations, plays,
orientation, color description and metadata payloads are read from the bytes by `ReferenceHeif`. Malformed, unsupported
and over-limit inputs are assembled the same way with one defect each (two wrap a committed AV1 vector in an image
sequence, one temporal unit per sample: frames that change size, frames that are not shown).

HEIC fixtures (`HeicCorpus.Container.cs`, recipes in `HeicCorpus.ContainerFixtures.cs` and `HeicCorpus.ContainerErrors.cs`):
inputs are written by libheif `heif-enc` (with x265) from hand-defined patterns, or assembled box by box
(`TestHarness/Heif/HeifFileBuilder`) around the payloads of such a file, or around a stream that the stand-alone x265 of
the pinned release codes from a pattern, for what `heif-enc` does not write: pictures with a conformance window (it codes a
small image as a 64x64 picture under a `clap` property), a conformance window that removes more than the padding
(rewritten by the `hevc_metadata` filter of FFmpeg), files without `colr` box whose bitstream has a color description,
a range only, or no video signal description at all (their planes are then coded in limited range), each `chroma_sample_loc_type` of a frame, an alpha plane that is a
4:2:0 picture, that has the auxiliary type of MPEG-B or the limited range, NAL unit lengths of one and two bytes, a
configuration record with two sequence and picture parameter sets, or without any (the parameter sets are then in the
coded data), a grid with an alpha grid, the layout of a phone (a grid of equal tiles, a rotation, an ICC profile, an Exif
item, a hidden thumbnail and a depth map), `hev1` tracks, sequences of small pictures with B pictures, composition offsets
and an edit list that starts at the first presentation time, a `clap` box in the sample entry of a color track and of
its alpha track, and the layouts the AVIF fixtures also have (media data
first, `idat`, extents, free and unknown boxes, generic major brands, 64-bit box sizes, `co64`/`stz2` tables, metadata of
a sequence in its file or in its track). Every reference is `decoded`, never taken from one tool: the structure is read by
`TestHarness/Heif/ReferenceHeif`; the data of each coded item and the samples of each track are turned into a byte stream
(the NAL units of the `hvcC` record, then those of the data) and decoded to planes by the **three** decoders of the
[HEVC conformance vectors](#hevc-conformance-vectors), FFmpeg's `hevc` decoder, libde265 and HM, under the same rule: at
least two must produce the same bytes, a recipe names the decoder it expects to be left out, and any other disagreement
fails the generation (HM is left out of the lossless pictures of 16x16 coding tree blocks, which x265 signals at level
8.5: [the table](#hevc-decoders-that-agree-and-decoders-that-are-left-out)). The x265 streams carry a decoded picture hash
per picture, which HM and FFmpeg verify. The planes are converted by `ReferenceYuvConverter` with the matrix coefficients
and the range of the `colr` box, or else of the video signal description of the bitstream (none: limited range, the
value Rec. ITU-T H.265 infers for `video_full_range_flag` in clause E.3.1, and what libheif and Apple ImageIO do;
unspecified coefficients: BT.601; an alpha plane: full range unless its bitstream says limited, since it is not video and
encoders say nothing of it), at the chroma sample position of the picture (4:2:0: `chroma_sample_loc_type`, 0
when the bitstream is silent; 4:2:2: on the luma samples of even columns). The generator tiles the planes of a grid before
the conversion, scales the alpha plane, and applies the clean aperture to the picture its conformance window left.
Lossless content is also compared with its source pattern. The composition times of a sequence must order its samples as
the bitstream outputs its pictures (clause C.5.2); the expected durations are the distances between them. Cross-check,
recorded with its measured differences and never used as a reference: libheif `heif-dec` for every input (its own parser,
libde265 and conversion; it applies `clap`, `irot` and `imir`, so its output is compared with the reference displayed with
the expected orientation). It agrees exactly with every reference that no matrix applies to, except monochrome samples in
limited range, signalled or inferred, which it returns as stored (Apple ImageIO does the same), and a limited-range
alpha plane; it ignores the `clap` box of a sample entry; it does not read NAL unit lengths of one or two bytes, `co64` or `stz2` tables,
`hev1` tracks, or the last image of the x265 sequence with B pictures, which is recorded. Malformed, unsupported and
over-limit inputs are assembled the same way with one defect each; three wrap a committed HEVC vector that the decoder
rejects, and three are files `heif-enc` writes (premultiplied alpha; a grid of tiles smaller than 64x64, each with a `clap`
property; a sequence of images smaller than 64x64, whose pictures are larger than its sample entry).

A valid HEIC fixture costs the generic suites what an AVIF one does, and `heif-enc` codes nothing smaller than a 64x64
picture: its images are 8x8 under a clean aperture where the feature allows, its grids have 64x64 tiles and its sequences
64x64 images (117 valid fixtures, 196,263 bytes of inputs). The x265 pictures are 16x16, or 24x24 with a conformance
window. The content is smooth, as for AVIF: the references are also the sources of the encoder suites.

Layered AVIF fixtures (`AvifCorpus.ContainerLayered.cs`; the errors are in `AvifCorpus.ContainerErrors.cs`). A layered
(progressive) image is one AV1 temporal unit that holds a frame per spatial layer, each refining the previous ones; the
image is the frame of the highest layer of the operating point, unless the `a1op` property selects another operating
point or the `lsel` property a layer:

- **Inputs.** `avifenc --progressive` (two layers of one input, the first one at half the size) and `avifenc --layered`
  (one input per layer, 2 to 4 layers, a quality and a scale per layer) write one item with one extent per layer and an
  `a1lx` property (`avif.a1lx`: the sizes of the layers, which a decoder does not need). avifenc writes neither `a1op`
  nor `lsel`, and it refuses a layered grid ("Layered grid image unimplemented in avifenc") and codes one layer per frame
  of a sequence: the files with an `lsel` or an `a1op` property (`avif.lsel=N`, 65535 for the value that selects no
  layer; `avif.a1op=N`), the grids of layered tiles and the sequence of layered samples are assembled around the
  payloads of avifenc files.
- **References.** As for every AVIF fixture: the planes dav1d and libaom agree on. Both decoders output **one frame per
  temporal unit, the shown frame of the highest spatial layer it holds** (the same rule as the
  [layered AV1 vectors](#layered-vectors)), which is the image of a file without selection. For a coded item that selects
  an operating point or a layer, the generator first removes from the payload what a decoder of that selection does not
  decode, from the specification (`DecodeCodedItem`, `SelectObus`): the units outside the operating point (AV1
  specification, section 5.3.1: a unit with an extension whose layers are not in the `operating_point_idc` of the
  operating point is dropped) and the units of the layers above the selected one. What is left is an ordinary stream
  whose highest layer is the selected one, which both decoders decode by themselves; the generator checks that the frame
  they output is of the selected layer. This was chosen over asking each decoder for the layer because FFmpeg only
  exposes that choice for dav1d (`-oppoint`, `-alllayers`), not for libaom, and a reference needs both.
- **Cross-checks.** `avifdec` reads `a1op` and `lsel` itself and decodes the selected frame with its own parser and
  dav1d: it agrees exactly with every reference that no matrix applies to. `avifdec --progressive --index N` on the
  file the payloads come from is recorded as one more cross-check when layer N has the size of the image (it scales a
  smaller layer up to the size of the last one, so it says nothing about a smaller selected frame). FFmpeg does not
  read an item that has several extents, nor `a1op` and `lsel`: its failure on the files avifenc writes is recorded, and
  it is not run on the files that select a layer.
- **Lower layers.** The pixels of the shown frames that the selection leaves below the returned frame are summed from
  the frame headers (`trace_headers`) of what is decoded: `avif.layers.hiddenPixels`. `avif.layers.scaled` tells that
  the layers of a temporal unit have different sizes.
- A second layer that is coded losslessly (`avif/layered-2-contents-rgb`, the tiles of `avif/layered-grid-2x1`) is also
  compared with its source pattern.

A valid AVIF fixture costs every generic suite (eager and sequential decoding, fault injection, truncation, limits,
resize and convolution against decimal references, every encoder) a time in proportion to its pixels and its frames, so
its content is as small as its feature allows: 8x8 for a matrix, a range, a bit depth or a metadata layout; 16x16 for film
grain and for the sequences of 8 to 12 images; the 64x64 and 67x65 images, the grids of 64-sample tiles that `avifenc`
writes and the 64x64 minimum of SVT-AV1 are the exceptions, each for its feature. Matrixed content is subsampled wherever
`avifdec` converts 4:4:4 samples exactly, because a tolerance must be justified by the measured disagreement of an
independent decoder; the 4:4:4 conversion of every matrix is checked in `Avif.Tests/Conformance/AvifContainerConformanceTests`.

Malformed, unsupported and over-limit inputs are byte edits of the valid files, each confirmed by the strict transcription.
One exception is recorded in the tests: `invalid/pnm/header-too-long` is legal Netpbm that only this library's bounded
header (65,536 bytes) rejects.

## Starter corpus

| Area | Fixtures |
| --- | --- |
| Patterns | corner markers (`png/rgba8-corner-markers`), single pixel, odd widths, checkerboard, alpha ramp with hidden gray values, 16-bit low-bit gradients (gray and RGBA) |
| PNG | every legal color type/bit depth: gray 1/2/4/8/16, RGB 8/16, palette 1/2/4/8 (partial tRNS, 256 colors, fewer entries than the depth allows), gray+alpha 8/16, RGBA 8/16; tRNS keys for gray 4/8/16 and RGB 8/16 (16-bit keys differing from opaque pixels only in the low bit or by a byte swap); 16-bit low-bit gradients (gray, gray+alpha, RGB, RGBA); Adam7 for 8/16-bit and sub-byte samples and the edge cases 1x1 (passes 2-7 empty), 1x9, 9x1, 3x2 and odd sizes; split and empty IDAT chunks; every filter type on 1-, 2-, 4-, 6- and 8-byte pixels; FFmpeg-encoded (compressed) and hand-authored (stored deflate) inputs; native `gray8`/`gray16le` and `rgb8` references for `Gray8`/`Gray16`/`Rgb24` decoding |
| APNG | SOURCE/OVER blending, dispose NONE/BACKGROUND/PREVIOUS, separate poster with first-frame PREVIOUS (treated as BACKGROUND), exact rational delays (`3/30`, `0/0`, `7/1000`), finite and infinite plays, FFmpeg-encoded 8/16-bit animations; compositing fixtures: translucent OVER at 8 bits (`over-alpha-rgba8`: over opaque, translucent and cleared pixels, OVER on the first frame drawn with SOURCE, BACKGROUND then OVER, SOURCE transparent replacement with hidden colors, PREVIOUS, delays `65535/1000`, `7/0`) and 16 bits (`over-alpha-rgba16`), a separate 16-bit poster with partial first frame and consecutive PREVIOUS (`poster-rgba16-dispose`, delays `0/1`, `1/65535`), gray + `tRNS` key with Adam7 frame regions of odd sizes and `fdAT` split in three chunks (`gray8-trns-adam7`), a poster plus one partial frame (`poster-partial-single-frame`), and a one-frame palette + `tRNS` animation (`palette-single-frame`) |
| GIF | GIF87a 2-color palette (minimum code size 2), interlaced frame with local palette, disposal 0-3 with transparency, LZW full table with clear codes and with deferred clear (KwKwK runs), FFmpeg-encoded animation with finite loop; for the decoder (`build_gif_decoding`, every frame a literal grid): palette changes (global, local 4/16/2 entries with a transparent index, an image without Graphic Control Extension, delay 65535, loop 1), disposal and the background color (bg index never painted: partial first image with disposal 3, disposal 2 without transparent index), undefined disposals 4 and 5-7, a transparent first image, interlaced images of heights 1-9, rectangles larger than / partly / entirely outside the screen, LZW minimum code size 1, code-size boundaries (no initial clear, two leading clears, clears at 2^k and 2^k + 1, KwKwK runs, minimum code size 8 with 16 colors), a missing end code, data after the end code, ANIMEXTS1.0, loop extensions after the first image (the last wins), a single image with loop 65535 and a NETSCAPE2.0 buffering sub-block, skipped extensions and comments |
| JPEG | baseline 4:2:0 / 4:2:2 (odd width, restart markers) / 4:4:4, progressive 4:2:0 with partial MCUs, grayscale odd size, EXIF orientation 6 (metadata only), JFIF density 300x150 dpi + XMP + COM; for the sequential decoder: high-frequency content (`pattern_detail_*`) with odd 4:2:0 and one restart interval per MCU row and optimized Huffman tables, 4:4:0 (vertical triangle filter), 4:1:1 (replication), 4:4:4 detail, grayscale with 3-block restart intervals, optimized tables and a gray ICC profile, extended sequential SOF1 with 16-bit DQT (quality 3), Adobe RGB (transform 0, `R`/`G`/`B` identifiers), multi-scan sequential frames (Cb/Y/Cr in separate non-interleaved scans with restarts; Y then interleaved CbCr), RGB ICC in three out-of-order APP2 chunks + big-endian EXIF orientation 3 + JFIF dots per centimeter; tiny frames whose chroma planes are at most 2 samples wide (4:2:0 4x9 and 2x13, 4:2:2 3x5, progressive 4:2:0 4x6: replicated like libjpeg-turbo); for the progressive decoder: the default libjpeg-turbo progression (DC/AC successive approximation, luma band split) at 4:4:4 detail, odd 4:2:2 with 2-MCU restarts, gray smaller than two blocks and Adobe RGB, and `-scans` scripts (`PROGRESSIVE_*_SCRIPT`): DC refined from bit 3 and a luma band refined from bit 4 at odd 4:2:0 with per-row restarts, spectral selection only at 4:4:0, separate DC scans with different point transforms at 4:1:1 with 3-block restarts, a gray refinement script with 3-block restarts, and a partial progression (high frequencies never refined to bit 0 or never sent) — encoded by libjpeg-turbo `cjpeg`. Every color JPEG also has an `rgb8` reference for `Rgb24` decoding; 12-bit samples (`cjpeg -precision 12`, references in 16-bit layouts): 4:4:4 detail, odd 4:2:0 with optimized tables and one restart interval per MCU row, odd 4:2:2 on a smooth gradient, grayscale with 3-block restart intervals, the default progression at 4:2:0, a grayscale refinement script and RGB samples (Adobe transform 0); arithmetic coding (`cjpeg -arithmetic`): sequential frames at 4:4:4 detail, 4:2:0 with partial MCUs and one restart interval per MCU row, 4:2:2 on a smooth gradient and grayscale with 3-block restart intervals, progressive frames with the default progression at 4:2:0, a scan script that refines DC from bit 3 and a luma band from bit 4 with per-row restarts, and a grayscale refinement script; arithmetic coding of 12-bit samples: 4:4:4 detail, 4:2:0 with per-row restarts and a progressive grayscale refinement script; lossless frames (`cjpeg -lossless`, compared exactly): grayscale and RGB with each of the seven predictors, restart intervals of one to four lines, point transforms of 1 and 2, samples of 5, 8, 10, 12 and 16 bits, and the difference 32,768 of a 16-bit file; YCbCr lossless frames written by FFmpeg's `ljpeg` encoder at 4:4:4, odd 4:2:0 and odd 4:2:2 |
| JPEG errors | Unsupported (header): lossless (SOF3), a sample precision of 16 bits in an extended sequential frame (the DCT processes have 8-bit or 12-bit samples), non-integral sampling ratios; unsupported without a color conversion (identified, and loaded with the conversion their `inkConversion` declares): four components, as YCCK inks with an embedded CMYK profile, as CMYK inks (Adobe transform 0) in a progressive frame, and as CMYK inks without Adobe segment with restart intervals; structure: interleaved scan with 18 blocks per MCU; decoding only (valid containers): truncated entropy-coded data, an unassigned Huffman code, a zero quantization value, a truncated DQT, an undefined Huffman table, an oversubscribed Huffman table, a restart marker out of sequence, a missing last restart marker, a frame whose third component has no scan; progressive: structure (scan headers, found by a full identification): an AC scan with Ss > Se, a refinement scan moved before the first scan of its band; decoding only: a truncated scan followed by the remaining scans, an invalid code at the start of an AC refinement, a frame whose Cr component has no scan; limit: a frame header declaring 20000x20000 pixels (`MaxFramePixels`, before any allocation) |
| WebP | lossless (VP8L): 1x1, odd width, RGBA corner markers, hidden colors under alpha 0 (`-exact`), a detailed pattern with the transforms `cwebp -z 9` chooses (`lossless-detail-transforms`), a small palette with pixel bundling and a 256-color palette, extended layout with ICC/EXIF/XMP, an opaque extended file with an unknown chunk; lossy (VP8): normal and simple loop filters with segmentation, no filter at high quality, odd and tiny (3x2) sizes, raw `ALPH` and lossless-compressed `ALPH` with horizontal filtering, metadata; animations: blend/dispose combinations with partial frames and a finite loop, a partial first frame with infinite loop, mixed lossy and lossless frames, a single-frame animation |
| BMP | 24-bit bottom-up and top-down with row padding, 32-bit `BITMAPV4HEADER` with an alpha mask and the same bytes as 32-bit `BI_RGB` (unspecified fourth byte), 16-bit implicit 5-5-5 and explicit 5-6-5 masks after a 40-byte header, 16-bit 1-5-5-5 with a `BITMAPV3INFOHEADER` alpha mask, 8-bit palette with five entries and a gap before the pixel data, 4-bit and 1-bit palettes with odd widths, `biXPelsPerMeter`/`biYPelsPerMeter`, FFmpeg-encoded 24-bit and 32-bit files; run-length streams: `BI_RLE8` and `BI_RLE4` with encoded runs (alternating indexes for 4 bits), absolute runs with and without a padding byte, and every way of skipping pixels (delta to the right, delta to a later row, early end of line, early end of bitmap) over a palette whose first entry is not black; OS/2: `BITMAPCOREHEADER` with 24-bit rows and with 1-, 4- and 8-bit `RGBTRIPLE` palettes (one shorter than its bit count allows, ended by `bfOffBits`), `BITMAPCOREHEADER2` complete (64 bytes, with a resolution) and cut after its bit count (16 bytes), and `BCA_RLE24` |
| BMP errors | invalid: zero width/height, two planes, bit count 7, undefined compression, pixel offset inside the header, truncated header and pixel data, palette index out of range, overlapping/non-contiguous/zero masks; run-length: `BI_RLE8` with top-down rows or 4 bits per pixel, a run past the end of its row, a delta outside the canvas, data after the last row, a missing end-of-bitmap marker, a palette index out of range; OS/2: `BITMAPCOREHEADER` with 16 bits per pixel or a pixel offset that leaves no palette entry; unsupported: OS/2 Huffman 1D, `BI_PNG`, 2 bits per pixel, a 10-bit channel mask; limits: width, frame pixels |
| TGA | uncompressed 24-bit bottom-left and top-left, 32-bit with and without declared alpha bits, 32-bit right-to-left, 16-bit 1-5-5-5 and 15-bit, 8-bit grayscale, 16-bit grayscale with eight declared alpha bits (gray and alpha) and with none (the attribute byte is discarded), color maps with a first-entry offset (24-bit entries) and with 32-bit RGBA entries, 16-bit little-endian color-map indexes on both sides of 255 (300 entries from index 250), run-length true color with runs of 7/130/20/30 crossing scan lines (128-pixel packets) and raw packets, run-length grayscale, run-length 16-bit grayscale with alpha and run-length color-mapped data with 16-bit indexes (two-byte pixels in packets), an 11-byte identification field with a footer, a file whose extension area (attributes type 3) and footer follow the raster, a file with a developer area between the raster and the extension area, FFmpeg-encoded run-length files |
| TGA errors | invalid: a packet past the last pixel, a truncated packet, truncated pixel data, a color-map index below the first stored entry, a 16-bit color-map index one past the last stored entry, a footer that locates the extension area over the footer, an extension area too short for its attributes type; unsupported: premultiplied alpha (attributes type 4); limits: width, encoded bytes |
| Netpbm | binary PBM with padding bits and a header comment, the same pixels as plain PBM, binary PGM 8-bit (with a comment) and 16-bit, plain PGM with `MAXVAL` 15, binary PPM 8-bit and 16-bit, plain PPM with `MAXVAL` 1000, PAM `RGB_ALPHA` 8-bit and 16-bit, PAM `GRAYSCALE_ALPHA`, PAM `BLACKANDWHITE`, FFmpeg-encoded PPM and PAM files |
| Netpbm errors | invalid: zero width, `MAXVAL` 0 and 65536, a plain sample above `MAXVAL`, a truncated raster, a 70,000-byte comment (the bounded header), a comment interrupting the last header token, the digit 2 in a plain PBM raster, a PAM header without `ENDHDR`, duplicated `WIDTH`, a `DEPTH` contradicting the tuple type, an undefined PAM keyword; unsupported: `TUPLTYPE CMYK`; limits: width, total pixels |
| WebP errors | invalid: RIFF/chunk sizes past the data, `VP8X` canvas mismatch, extended file without image, VP8L bad signature/version/corrupt prefix code/truncation, VP8 bad start code/first partition overflow/truncated partition, `ALPH` bad compression method, `ANIM` missing, frame outside the canvas, truncated animation; unsupported: a VP8 interframe; limits: width, frame pixels, metadata bytes, animation frames and total pixels |
| Metadata | `png/metadata-rgb8-profiles`: RGB ICC profile (`iCCP`), `pHYs` 3780x2835 per meter, big-endian EXIF (orientation 8, Software, Exif IFD pixel dimensions, IFD1 thumbnail placeholder), XMP `iTXt`, `tEXt`/`zTXt`/international `iTXt`, a `tEXt` after `IDAT`; `png/metadata-gray8-icc`: gray ICC profile, aspect-ratio-only `pHYs`, little-endian EXIF orientation 3. The ICC profiles are small hand-built ICC v4.3 test profiles (CC0, never applied) |
| APNG errors | Header defects: `acTL` with zero frames or `num_plays` 2^31, partial default-image `fcTL`, `dispose_op` 3, `fdAT` before `IDAT`; structure defects: sequence gap, region outside the canvas, fewer/more frames than declared, `fcTL` without data, `blend_op` 2, `fdAT` shorter than its sequence number; frame datastream defects (`fdAT` zlib truncated, one extra scanline); limits: `MaxFrames` counting the poster, `MaxTotalPixels` counting full-canvas displayed frames (decode-only) |
| AVIF | lossless identity-matrix RGB at 8, 10 and 12 bits, monochrome (lossless and lossy at 8, 10 and 12 bits, full and limited range), alpha at 8, 10 and 12 bits (lossless, lossy, under limited-range colors, under lossless colors; hidden colors kept), gray + alpha; premultiplied alpha (avifenc `-p`: lossless at 8 and 10 bits, lossy 4:2:0 under a lossy alpha plane, and a `prem` track reference assembled on a sequence whose colors are clipped); lossy 4:4:4 / 4:2:2 / 4:2:0 with the BT.709, FCC, BT.470BG, BT.601, SMPTE 240M, YCgCo and BT.2020 matrices and unspecified coefficients, in full and limited range, at 8, 10 (HDR 9/16/9) and 12 bits; the chromaticity-derived matrix with the BT.2020 primaries (4:4:4) and with the Display P3 primaries (4:2:0, limited range, 10-bit), and the SMPTE ST 2085 matrix on the planes of a y4m source; the matrices that go through the transfer characteristics, on patterns converted with the forward equations and coded without loss (BT.2020 constant luminance at 10 bits, chromaticity-derived constant luminance with the Display P3 primaries, ICtCp with PQ and with HLG at 12 bits) and on arbitrary 4:2:0 planes whose colors leave the gamut (BT.2020 constant luminance, ICtCp with PQ), and ICtCp with HLG in limited range in the sample entry of a sequence; identity derivations (`iden`, assembled on committed payloads): without transformation, a clean aperture and a mirror on the derivation of a turned image, two nested derivations with an alpha plane derived the same way, a derivation whose alpha plane is a derivation with the same clean aperture, an untransformed derivation whose alpha plane is a coded image, a clean aperture across the tiles of a grid, and a derivation without `colr` box whose input is matrixed; images built from other images (`iovl` and `grid` items assembled the same way): an overlay of four copies of an image with alpha on an opaque canvas, two of them across its edges, an overlay with a transparent fill, a clean aperture and a rotation, an overlay of a grid and of a turned and cropped identity derivation of one of its tiles on a half transparent canvas, a grid whose tiles are mirrored and turned identity derivations and exceed the image, an identity derivation of a mirrored 10-bit overlay, an overlay of lossy matrixed images, an overlay that takes its ICC profile from its input image, one that no `colr` box describes (the bitstream of its input image does), and an overlay of a PQ image on an opaque black canvas; sizes 1x1, 5x1, 1x5, odd 4:2:0 and 4:2:2 images at every depth, 33x17, 64x64, 67x65; vertical and co-located chroma samples (y4m inputs); a lossless 2x2 grid, lossy 4:2:0 2x1 (with alpha) and 3x1 grids, a 1x3 grid with an alpha grid, a 10-bit 2x1 grid, a 3x2 grid of 16x16 tiles, a grid cropped below its tiles, a grid whose tiles repeat its `clap`, `irot` and `imir` as avifenc writes them; `irot` 0-3, `imir` 0-1 and both; a clean aperture followed by a rotation, and one on 4:2:0 content; a clean aperture associated after a rotation, and one between a mirroring and a rotation (not the order of MIAF: the properties apply in the order of their association); Exif (whose orientation tag contradicts `irot`), XMP and ICC, an ICC profile without `nclx`; colr variants of one payload (`nclx` overriding the bitstream, no `colr` box with identity, BT.709 and undescribed bitstreams, after and before the media data); a pixel aspect ratio and a content light level (avifenc `--pasp`, `--clli`), a mastering display color volume with its light levels (`mdcv` and `clli` properties assembled on an HDR image), and the three boxes in the sample entry of a sequence; film grain (4:2:0, odd, monochrome 10-bit, 4:2:2 10-bit limited, 4:4:4 12-bit with chroma scaled from luma, under an alpha plane); assembled layouts (`layout-*`); sequences: lossless with three durations and infinite repetition, alpha with two repetitions, 10-bit played once at 1001/30000 s, 8 to 12 images with inter frames and frames that are not shown (4:2:0, 4:4:4 at speed 2, 4:2:2 with key frames every three images, monochrome, 10-bit, lossless 12-bit, a lossy alpha track, 10-bit alpha), XMP in the `meta` box of the track and an ICC profile (avifenc), Exif and XMP in the `meta` box of the file or of the track with their data in `mdat`, in `idat` and after the samples, a track `meta` box that wins over the file one, a file `meta` box after `moov` (not read), chunks written in reverse order, two from the FFmpeg muxer (libaom, SVT-AV1), a matrix that turns the images a quarter turn, a matrix that mirrors them with a clean aperture in the sample entries of both tracks, no edit list, edit lists that select parts of the media (an edit that starts inside the first image, an edit that ends before the second image, two edits around an image that the next one refers to, edits in another time scale than the media that show images for a part of their duration, three edits whose durations are thirds of a second on a media in tenths, the same edit on a color track and on its alpha track), fragmented sequences assembled on the same payloads (three fragments with offsets relative to the `moof` box; explicit base offsets, two runs per track fragment and no `tfdt`; images in the `moov` box followed by fragments; an alpha track whose data follow the color data without base flag; default durations in `tfhd` and in `trex`; alpha planes described by a later fragment than their images), `mdat` first, `co64` + `stz2` + one sample per chunk; layered images: `--progressive` (4:2:0, identity-matrix 10-bit, with a layered alpha item), `--layered` with 2, 3 and 4 quality layers (4:2:0 limited range, identity, monochrome 12-bit), three layers of growing sizes (4:2:2 10-bit), a second layer that is another picture, coded losslessly; `lsel` 0xFFFF, 0, 1, 2 and 3, `a1op` 0, 1 and 2, both together, a selected frame smaller than the last layer (`ispe` of its size) by `lsel` and by `a1op`, `lsel` on 4:2:0 content; a grid of two layered tiles, grids whose tiles select their own layer and their own operating point; a sequence of two layered samples |
| AVIF errors | invalid: truncated `meta` box and item data, a box smaller than its header, a child box larger than its parent, no file type box and no AVIF brand (unknown format), no `pitm`, primary item, `ispe` or `av1C`, two `meta` boxes, item data outside the file and outside `idat`, `ispe` and `av1C` contradicting the bitstream, an `a1op` operating point the sequence header does not have (one and three operating points), an `lsel` layer the bitstream does not have (one and three layers) or that its operating point does not have, layer 4, a selected frame of another size than `ispe`, `lsel` and `a1op` on a grid item, grids (tile count, descriptor size, tile sizes), a sequence without AV1 track, with an edit whose media time is negative or that shows no image, with a `moof` box that the `moov` box does not announce, with a fragment that lacks the alpha plane of an image, without chunk offsets, truncated, with a malformed `meta` box in its track, with a metadata item of its track or of the file located after the end of the file; an identity derivation with two input images, with an input that does not exist, derived from itself, or of another size than its input; an overlay whose descriptor declares another size than its `ispe` property or lacks an offset, a derived image that is its own input, a grid of derived tiles of two sizes; unsupported: an overlay whose visible fill color would have to be converted to the color space of the image, an alpha plane for an overlay itself, an overlay of images of two color spaces, of two ICC profiles, or whose input image has another color space in its bitstream than the overlay declares, nine nested identity derivations (of a coded image and of an overlay), an untransformed alpha plane for an identity derivation that has a clean aperture, fractional `clap`, an essential unknown property, reserved matrix coefficients, ICtCp matrix coefficients with other transfer characteristics than PQ and HLG, chromaticity-derived constant luminance matrix coefficients with unspecified primaries, an alpha plane of another size, a `moof` box before the `moov` box, fragments that do not follow each other, with composition time offsets or whose samples precede them, a fragmented sequence whose edit list selects a part of its media, edit lists with an empty edit, with a media rate of 2 or of 0, with edits that are not in the order of the media or that overlap, an alpha track whose edit is not the one of its color track, constant luminance matrix coefficients with unspecified transfer characteristics in the sample entry of a sequence, a grid tile that declares another rotation than its grid, a sequence whose images change size; limits: width, height, frame pixels, encoded bytes, live allocations, metadata bytes (ICC, then Exif; the XMP packet of a sequence), sequence frames, displayed pixels, frames that are decoded without being shown, and the lower layers of a layered image |
| HEIC | lossless identity-matrix RGB at 8, 10 and 12 bits, monochrome (lossless and lossy, at 8, 10 and 12 bits, full and limited range), alpha at 8, 10 and 12 bits (monochrome and 4:2:0 alpha pictures, both auxiliary types, a limited-range alpha plane, hidden colors kept), gray + alpha; lossy 4:4:4, 4:2:2 and 4:2:0 with the BT.709, BT.470BG, BT.601, YCgCo and BT.2020 matrices and unspecified coefficients, in full and limited range, at 8, 10 (HDR 9/16/9) and 12 bits; 4:2:0 at the default chroma sample location and at each signalled one (0, 1, 2); sizes 1x1, odd sizes, 33x17, 64x64, 67x65; pictures of 64x64 under a clean aperture (`heif-enc`) and pictures with a conformance window (x265), a window that removes more than the padding, and both together; files without `colr` box whose bitstream has a color description, a range only or no video signal description, a `colr` box that overrides the bitstream, an ICC profile with and without `nclx`; lossless 2x2 and 3x2 grids (the second cropped below its tiles), a grid cropped by its descriptor, 10-bit and 4:2:0 grids, a grid with an alpha grid, the layout of a phone (grid, rotation, ICC profile, Exif item, hidden thumbnail and depth map) at 8 and 10 bits; `irot` 1-3, `imir` 0-1 and both, a clean aperture followed by a rotation and one at an odd position of 4:2:0 content; Exif (whose orientation tag contradicts `irot`) and XMP; configuration records with NAL unit lengths of 1 and 2 bytes, two parameter sets of each type, incomplete arrays, and parameter sets in the coded data only; assembled layouts (`layout-*`, generic and AVIF brands); sequences: lossless intra pictures played three times, lossless P pictures repeated forever, B pictures whose composition offsets reorder the samples (`heif-enc` and x265, with an edit list that starts at the first presentation time, without edit list, under an `hev1` sample entry with the parameter sets in the first sample, with two-byte NAL unit lengths), an alpha track, 10-bit, monochrome, 4:2:0 and 4:2:2 images, pictures with a conformance window, a clean aperture in the sample entry (an odd size, and an offset on a color and an alpha track), `mdat` first, `co64` + `stz2` + one sample per chunk, chunks written in reverse order, generic and AVIF brands, Exif and XMP in the `meta` box of the file after the samples and in the `idat` box of the `meta` box of the track |
| HEIC errors | invalid: truncated `meta` box and item data, a child box larger than its parent, no file type box (unknown format), no `pitm`, `ispe` or `hvcC`, item data outside the file, `ispe` contradicting the picture, an `hvcC` record that is truncated, reserves its length size or contradicts its sequence parameter set (bit depth, chroma format), an item with no picture, with two pictures, whose picture is not an intra random access point, whose NAL unit lengths exceed or do not fill its data, whose slice data are truncated or are random bytes, grids (tile count, a tile that does not exist, descriptor size, tile sizes), a sequence without HEVC track, without chunk offsets, truncated, with two samples at one composition time, with a missing reference picture, with a record that contradicts its bitstream, with a malformed `meta` box in its track; unsupported: an `hvc1` item under the brands of AVIF and an `av01` item under the brands of HEIC, `lhv1` items and layer selectors, an `hvcC` record of another version, premultiplied alpha, `iden`/`iovl` derived images, a grid whose tiles have a `clap` property (as `heif-enc` writes small tiles), fractional `clap`, an essential unknown property, ICtCp matrix coefficients, an alpha plane of another size, 16-bit samples, luma and chroma planes of different depths, extended precision processing, `chroma_sample_loc_type` 3, `moof`, an edit that starts after the first image, a sequence whose pictures do not have the size of its sample entry (assembled, and as `heif-enc` writes small images); limits: width, height, frame pixels (of the image, and of the picture under a clean aperture), encoded bytes, live allocations, metadata bytes (ICC, then Exif; the items of a sequence), sequence frames and displayed pixels |
| JPEG XL | lossless (Modular): gray, gray + alpha, RGB and RGBA at 8 and 16 bits, 12-bit samples scaled to 16 bits, efforts 1 and 9, an image of few colors (palette), a responsive image (squeeze), four groups of 128x128 samples, a bare codestream and a container; metadata: Exif and XMP boxes, plain and compressed (`brob`), ICC profiles (RGB and gray) in the codestream, the linear transfer function, enumerated color encodings with code points (Display P3, BT.2100 PQ, gray BT.709), orientation 6 in the image header; containers assembled around one codestream: three `jxlp` parts, a 64-bit box size, a box that extends to the end of the file, unknown boxes, metadata after the codestream; lossy (references decoded by djxl): VarDCT RGB, RGBA, gray and 16-bit, effort 3, large transforms on a smooth image, progressive passes with a permuted table of contents, an LF frame, noise, patches, a frame upsampled by 2, a lossy Modular frame, without and with strong restoration filters, JPEG files recompressed without loss (4:2:0, 4:2:2, 4:4:4, gray), a spline; animations: full frames at 10 ticks per second, 30000/1001 ticks per second, a layer without duration and a cropped frame, a multiplied frame, an alpha animation played three times |
| JPEG XL errors | invalid (rejected by djxl and jxl-oxide too): inverted bytes in the frame header and in the coded samples, a codestream without its signature, a container without codestream, with two codestreams, with parts out of order, with a box smaller than its header; unsupported: floating-point samples, premultiplied alpha, PQ with the white point E, an XYB image with an ICC profile that is not sRGB; limits: width, height, frame pixels, animation frames, metadata bytes, and the pixels of a layer that is decoded without being shown |
| JPEG 2000 | reversible (5-3): gray, gray + opacity, RGB and RGBA at 8 and 16 bits, samples of 4 and 12 bits scaled exactly, with and without the component transformation, zero to six decomposition levels, images of one pixel, one column and one row; the five progression orders with layers and precincts, a POC marker segment, tiles (with offsets of the image and of the tile grid), tile-parts by resolution level, layer and component, interleaved tile-parts, SOP/EPH, PLT/TLM, packet headers packed in the main header (PPM) and in tile-part headers (PPT), COC/QCC marker segments, a coding style in the tile-part headers, a region of interest, five guard bits, every style of the coding passes (bypass, reset, termination on each pass, vertically causal, predictable termination, segmentation symbols, all of them), code-blocks of 4x4 and 64x4; irreversible (9-7): gray, RGB with and without the component transformation, RGBA, 16 bits, layers, tiles, a region of interest, and a reversible codestream cut by a rate; JP2 files: written by opj_compress (RGB, gray 16 bits, RGBA), a palette (with an opacity column), channel definitions (colors reordered, opacity first, gray + opacity), sYCC with and without sub-sampled chrominances, ICC profiles (RGB and gray), a capture resolution, EXIF and XMP UUID boxes before and after the codestream, boxes of 64-bit length, a codestream box that extends to the end of the file; the high-throughput block coder (Rec. ITU-T T.814): reversible gray and RGB at 8, 12 and 16 bits, tiles, precincts, code-blocks of 1024 columns, images of one column and one row, irreversible, a JPH file, and hand-authored coding passes: HT SigProp and HT MagRef passes (with and without the vertically causal contexts), an HT SigProp pass alone, placeholder passes, the passes of a code-block over two and three layers |
| JPEG 2000 errors | invalid (rejected by opj_decompress, and by FFmpeg unless stated): a codestream without EOC marker and one cut in its tile-part, a tile of width 0, no component, an empty image area, no COD and no QCD marker segment, a tile index, a tile-part length and a tile-part index out of range, a file without codestream box, without JP2 header box (FFmpeg reads it), with an image header that contradicts the codestream (FFmpeg reads it), with a codestream box that is cut (FFmpeg reads it), without color specification box and with a box length of 4 (the reference decoders read both: the Recommendation is the reference), an HT cleanup segment with a suffix longer than itself; unsupported: signed samples, five components without color specification, a sub-sampled first component, a premultiplied opacity, several opacity channels, the extensions of ISO/IEC 15444-2, the brand of Motion JPEG 2000, two codestreams, mixed block coders, a second HT set, placeholder passes in another packet than the HT cleanup pass; limits: width, height, frame pixels, metadata bytes before and after the codestream |
| OpenEXR | every compression the library reads (NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A) with HALF RGBA and FLOAT RGB samples; luminance (Y) with and without alpha, a red channel alone, HALF, FLOAT and UINT channels in one file, UINT luminance, infinities, not-a-number and the extreme floats, one pixel; flat B44A blocks, a pLinear B44 channel, PXR24 rounding ties, a PIZ chunk of more than 16384 values (16-bit wavelet); tiles of one level, mipmaps rounded up, ripmaps, tiles in random order, B44 tiles of odd sizes; a decreasing line order, a data window inside the display window from negative coordinates, other attributes and a long name; the chromaticities of BT.709, BT.2020 and Display P3; four sample images of the OpenEXR project (stripes, every half value, mipmapped PXR24 tiles, t01 of DisplayWindow) |
| OpenEXR errors | invalid (rejected by OpenEXR too): a file cut in its last chunk, an offset after the end of the file, a chunk with the coordinates of another one, corrupt zlib data, a PIZ chunk whose Huffman data declare more bits than they have, an RLE run longer than its chunk, no channel list; unsupported: the DWAA, DWAB, ZSTD and HTJ2K32 compressions, a Z channel, the channels of a layer, luminance and chroma (RY, BY), a data window larger than the display window (and three sample images of the OpenEXR project whose windows differ), deep data; limits: frame pixels and the bytes of the attributes |
| Errors | PNG header defects (bad IHDR CRC, IHDR not first, illegal bit depth, chunk length above 2^31 - 1, unknown critical chunk `CgBI` — unsupported), structure defects (truncated IDAT, bad IDAT CRC, non-consecutive IDAT, PLTE after IDAT, missing IEND) and pixel-data defects with valid containers (truncated zlib, missing or wrong Adler-32, bytes after the zlib datastream, one extra scanline, one missing scanline, filter type 5, out-of-range palette index); truncated GIF image data, GIF plain-text extension (unsupported), arithmetic-coded JPEG (unsupported); GIF LZW and color-table defects inside valid containers (code above the next table code, table code after a clear, end code too early, data ending without end code, an index outside the table, no color table) and block defects (unknown block, 3-byte Graphic Control Extension, minimum code size 12, missing trailer, no image, empty logical screen); resource limits: frame count, GIF displayed pixels (`MaxTotalPixels`, decode-only), width, frame pixels, encoded bytes (reached at IEND), and a `zTXt` whose 171-byte zlib stream inflates to 25,801 bytes with `MaxMetadataBytes` = 4096 (deterministic fixed-Huffman stream written by the generator) |

## Fixture coverage matrix

The two tables below map the supported decoding features
([readme.md](../../src/Meziantou.Framework.Imaging/readme.md)) to the golden-corpus fixtures that exercise them
(`manifest.json`). They are **verified**: `Tests/Conformance/FixtureCoverageMatrixTests` recomputes every count from the
manifest and fails when the tables are stale, when a feature row has no fixture of the expected kind, or when a feature
key recorded in the manifest is not mapped by any row.
[Regenerating the corpus](#regenerating-the-corpus) therefore requires reviewing these tables too; the failing test prints
the expected rows. The test locates each table by its exact heading line and reads the rows of the first table that
follows it, so keep both headings unique in this file.

- **Selector**: the fixture `features` that a row requires (all of them). `key=value` matches a fixture with a `key`
  feature whose comma-separated values include `value`; a bare `key` matches any value. Features are parsed from the
  encoded bytes by the generator, never trusted from tool flags ([Layout](#layout)).
- **Kinds**: `valid` fixtures have independent expected pixels and metadata; `invalid` (malformed), `unsupported`
  (recognized and rejected with `UnsupportedImageFeatureException`) and `limit` (`ImageResourceLimitException`) fixtures
  have expected errors only. Rows marked *(rejected)* must have an `unsupported` fixture, every other row a `valid` one.
- Every valid fixture is decoded by the eager, sequential and identify suites in every input variant, on `net10.0` and
  `net11.0`, without external tools. Encoders
  are verified by independent readers and by the interop job (the AV1 streams of the AVIF encoder have no reader in
  the harness: they are decoded by dav1d and libaom in the interop job, and by the library elsewhere); the corpus
  references are also used as encoder inputs
  (`TestHarness.Adapters.EncoderSources`), except the seeded-noise decoder inputs written by FFmpeg (the generator's
  `*FfmpegDecoding` builders: `png/ffmpeg-*`, `apng/ffmpeg-<format>`, `gif/ffmpeg-<rectangles>-<transparency>`,
  `jpeg/ffmpeg-mjpeg-*`). Those exist so decoders meet an independent encoder's own choices (filters, delta rectangles,
  dispose/blend operations, quantization and Huffman tables) without running FFmpeg at test time.

## Fixtures per format and kind

| Format | Valid | Invalid | Unsupported | Limit |
| --- | --- | --- | --- | --- |
| avif | 189 | 45 | 28 | 12 |
| bmp | 24 | 21 | 4 | 2 |
| exr | 43 | 7 | 12 | 2 |
| gif | 24 | 13 | 1 | 2 |
| heic | 117 | 31 | 21 | 11 |
| jpeg | 72 | 19 | 6 | 1 |
| jp2 | 92 | 17 | 11 | 5 |
| jxl | 54 | 7 | 4 | 6 |
| png | 109 | 31 | 1 | 6 |
| pnm | 15 | 12 | 1 | 2 |
| qoi | 10 | 12 | 0 | 4 |
| tga | 23 | 7 | 1 | 2 |
| webp | 21 | 15 | 1 | 5 |

## Features

| Feature | Selector | Valid | Invalid | Unsupported | Limit |
| --- | --- | --- | --- | --- | --- |
| PNG grayscale 1-bit | `png.colorType=0` + `png.bitDepth=1` | 7 | 0 | 0 | 0 |
| PNG grayscale 2-bit | `png.colorType=0` + `png.bitDepth=2` | 1 | 0 | 0 | 0 |
| PNG grayscale 4-bit | `png.colorType=0` + `png.bitDepth=4` | 2 | 0 | 0 | 0 |
| PNG grayscale 8-bit | `png.colorType=0` + `png.bitDepth=8` | 12 | 0 | 0 | 0 |
| PNG grayscale 16-bit | `png.colorType=0` + `png.bitDepth=16` | 10 | 0 | 0 | 0 |
| PNG RGB 8-bit | `png.colorType=2` + `png.bitDepth=8` | 11 | 0 | 0 | 0 |
| PNG RGB 16-bit | `png.colorType=2` + `png.bitDepth=16` | 10 | 0 | 0 | 0 |
| PNG palette (1/2/4/8-bit) | `png.colorType=3` | 11 | 0 | 0 | 0 |
| PNG gray + alpha 8-bit | `png.colorType=4` + `png.bitDepth=8` | 8 | 0 | 0 | 0 |
| PNG gray + alpha 16-bit | `png.colorType=4` + `png.bitDepth=16` | 8 | 0 | 0 | 0 |
| PNG RGBA 8-bit | `png.colorType=6` + `png.bitDepth=8` | 17 | 0 | 0 | 0 |
| PNG RGBA 16-bit | `png.colorType=6` + `png.bitDepth=16` | 12 | 0 | 0 | 0 |
| PNG transparency (`tRNS`: palette alpha, gray/RGB keys) | `png.chunk=tRNS` | 9 | 0 | 0 | 0 |
| PNG Adam7 interlacing | `png.interlace=adam7` | 19 | 0 | 0 | 0 |
| PNG filter None | `png.filters=0` | 52 | 0 | 0 | 0 |
| PNG filter Sub | `png.filters=1` | 92 | 0 | 0 | 0 |
| PNG filter Up | `png.filters=2` | 33 | 0 | 0 | 0 |
| PNG filter Average | `png.filters=3` | 24 | 0 | 0 | 0 |
| PNG filter Paeth | `png.filters=4` | 39 | 0 | 0 | 0 |
| PNG split image data (several `IDAT` chunks) | `png.idatChunks` | 1 | 0 | 0 | 0 |
| PNG metadata: ICC profile (`iCCP`) | `png.chunk=iCCP` | 2 | 0 | 0 | 0 |
| PNG metadata: EXIF (`eXIf`) | `png.chunk=eXIf` | 2 | 0 | 0 | 0 |
| PNG metadata: text and XMP (`iTXt`) | `png.chunk=iTXt` | 1 | 0 | 0 | 0 |
| PNG metadata: text (`tEXt`) | `png.chunk=tEXt` | 1 | 0 | 0 | 0 |
| PNG metadata: compressed text (`zTXt`) | `png.chunk=zTXt` | 1 | 0 | 0 | 0 |
| PNG metadata: resolution (`pHYs`) | `png.chunk=pHYs` | 86 | 0 | 0 | 0 |
| APNG animations | `apng` | 19 | 14 | 0 | 2 |
| APNG blend SOURCE | `apng.blend=source` | 18 | 0 | 0 | 0 |
| APNG blend OVER | `apng.blend=over` | 10 | 0 | 0 | 0 |
| APNG dispose NONE | `apng.dispose=none` | 17 | 0 | 0 | 0 |
| APNG dispose BACKGROUND | `apng.dispose=background` | 6 | 0 | 0 | 0 |
| APNG dispose PREVIOUS | `apng.dispose=previous` | 11 | 0 | 0 | 0 |
| APNG partial frame regions | `apng.subRectangles` | 18 | 0 | 0 | 0 |
| APNG default image as frame zero | `apng.poster=frame0` | 16 | 0 | 0 | 0 |
| APNG separate poster frame | `apng.poster=separate` | 3 | 0 | 0 | 0 |
| APNG infinite plays (`num_plays` 0) | `apng.numPlays=0` | 12 | 0 | 0 | 0 |
| APNG finite plays | `apng.numPlays=2` | 3 | 0 | 0 | 0 |
| APNG Adam7 frames | `apng` + `png.interlace=adam7` | 1 | 0 | 0 | 0 |
| APNG 16-bit frames | `apng` + `png.bitDepth=16` | 7 | 0 | 0 | 0 |
| GIF87a | `gif.version=87a` | 1 | 0 | 0 | 0 |
| GIF89a | `gif.version=89a` | 23 | 0 | 1 | 0 |
| GIF global palette | `gif.globalPalette` | 24 | 0 | 1 | 0 |
| GIF local palettes | `gif.localPalette` | 2 | 0 | 0 | 0 |
| GIF interlacing | `gif.interlaced` | 2 | 0 | 0 | 0 |
| GIF transparency | `gif.transparency` | 6 | 0 | 0 | 0 |
| GIF partial frame rectangles | `gif.subRectangles` | 11 | 0 | 0 | 0 |
| GIF rectangles clipped to the logical screen | `gif.clipped` | 1 | 0 | 0 | 0 |
| GIF disposal 0 (unspecified) | `gif.disposal=0` | 5 | 0 | 0 | 0 |
| GIF disposal 1 (none) | `gif.disposal=1` | 16 | 0 | 0 | 0 |
| GIF disposal 2 (background) | `gif.disposal=2` | 3 | 0 | 0 | 0 |
| GIF disposal 3 (previous) | `gif.disposal=3` | 2 | 0 | 0 | 0 |
| GIF undefined disposal values 4–7 | `gif.disposal.undefined` | 2 | 0 | 0 | 0 |
| GIF NETSCAPE2.0 loop (infinite) | `gif.netscapeLoop=0` | 5 | 0 | 0 | 0 |
| GIF NETSCAPE2.0 loop (finite) | `gif.netscapeLoop=2` | 1 | 0 | 0 | 0 |
| GIF ANIMEXTS1.0 loop | `gif.animextsLoop` | 1 | 0 | 0 | 0 |
| GIF comment extension | `gif.extension=comment` | 1 | 0 | 0 | 0 |
| GIF application extensions (skipped) | `gif.extension=application` | 13 | 0 | 0 | 0 |
| GIF unknown extensions (skipped) | `gif.extension=0x99` | 1 | 0 | 0 | 0 |
| GIF plain-text extension (rejected) | `gif.extension=plainText` | 0 | 0 | 1 | 0 |
| GIF LZW minimum code size 1 | `gif.minCodeSize=1` | 1 | 0 | 0 | 0 |
| GIF LZW minimum code size 8 | `gif.minCodeSize=8` | 7 | 0 | 0 | 0 |
| GIF LZW codes growing to 12 bits | `gif.lzw.maxCodeSize=12` | 2 | 0 | 0 | 0 |
| GIF LZW full code table | `gif.lzw.tableFull` | 2 | 0 | 0 | 0 |
| GIF LZW deferred clear | `gif.lzw.deferredClear` | 1 | 0 | 0 | 0 |
| GIF LZW clear codes inside the data | `gif.lzw.clearCodes=2` | 1 | 0 | 0 | 0 |
| GIF LZW KwKwK sequences | `gif.lzw.kwkwk` | 7 | 0 | 0 | 0 |
| GIF LZW without initial clear code | `gif.lzw.noInitialClear` | 1 | 0 | 0 | 0 |
| GIF LZW missing end code | `gif.lzw.missingEndCode` | 1 | 0 | 0 | 0 |
| GIF LZW data after the end code | `gif.lzw.dataAfterEndCode` | 1 | 0 | 0 | 0 |
| JPEG baseline (SOF0) | `jpeg.process=baseline` | 30 | 10 | 3 | 1 |
| JPEG extended sequential (SOF1) | `jpeg.process=extended` | 6 | 0 | 1 | 0 |
| JPEG progressive (SOF2) | `jpeg.process=progressive` | 13 | 5 | 1 | 0 |
| JPEG progressive spectral selection | `jpeg.progressive=spectral-selection` | 16 | 4 | 1 | 0 |
| JPEG progressive successive approximation | `jpeg.progressive=successive-approximation` | 16 | 4 | 1 | 0 |
| JPEG progressive DC refinement | `jpeg.progressive=dc-refinement` | 16 | 4 | 1 | 0 |
| JPEG progressive AC refinement | `jpeg.progressive=ac-refinement` | 16 | 4 | 1 | 0 |
| JPEG progressive separate DC scans | `jpeg.progressive=dc-separate` | 1 | 1 | 0 | 0 |
| JPEG partial progression | `jpeg.progressive=partial` | 1 | 1 | 0 | 0 |
| JPEG multi-scan sequential frames | `jpeg.process=baseline` + `jpeg.scans=3` | 1 | 0 | 0 | 0 |
| JPEG non-interleaved scans | `jpeg.scanComponents=1` | 2 | 1 | 0 | 0 |
| JPEG grayscale (one component) | `jpeg.components=1` | 14 | 0 | 0 | 1 |
| JPEG three components (YCbCr or RGB) | `jpeg.components=3` | 58 | 19 | 3 | 0 |
| JPEG Adobe APP14 segment (RGB, CMYK or YCCK) | `jpeg.app14=Adobe` | 8 | 4 | 3 | 0 |
| JPEG sampling 4:4:4 | `jpeg.sampling=4:4:4` | 16 | 4 | 1 | 0 |
| JPEG sampling 4:2:2 | `jpeg.sampling=4:2:2` | 7 | 0 | 0 | 0 |
| JPEG sampling 4:2:0 | `jpeg.sampling=4:2:0` | 21 | 14 | 1 | 0 |
| JPEG sampling 4:4:0 | `jpeg.sampling=4:4:0` | 2 | 0 | 0 | 0 |
| JPEG sampling 4:1:1 | `jpeg.sampling=4:1:1` | 2 | 0 | 0 | 0 |
| JPEG restart intervals | `jpeg.restartInterval` | 21 | 3 | 1 | 0 |
| JPEG 16-bit quantization tables | `jpeg.quantization=16-bit` | 1 | 0 | 0 | 0 |
| JPEG JFIF APP0 (density) | `jpeg.app0=JFIF` | 49 | 15 | 5 | 0 |
| JPEG EXIF (APP1) | `jpeg.app1=Exif` | 2 | 0 | 0 | 0 |
| JPEG XMP (APP1) | `jpeg.app1=http://ns.adobe.com/xap/1.0/` | 1 | 0 | 0 | 0 |
| JPEG ICC profile (APP2) | `jpeg.app2=ICC_PROFILE` | 2 | 0 | 1 | 0 |
| JPEG arithmetic coding, sequential (SOF9) | `jpeg.process=arithmetic-sequential` | 6 | 0 | 0 | 0 |
| JPEG arithmetic coding, progressive (SOF10) | `jpeg.process=arithmetic-progressive` | 4 | 0 | 0 | 0 |
| JPEG lossless (SOF3) | `jpeg.process=lossless` | 13 | 4 | 0 | 0 |
| JPEG lossless with arithmetic coding, SOF11 (rejected) | `jpeg.process=lossless-arithmetic` | 0 | 0 | 1 | 0 |
| JPEG lossless predictors | `jpeg.predictor` | 13 | 4 | 1 | 0 |
| JPEG lossless point transform | `jpeg.pointTransform` | 2 | 1 | 0 | 0 |
| JPEG 12-bit samples | `jpeg.precision=12` | 12 | 0 | 0 | 0 |
| JPEG 16-bit samples (lossless; rejected in a DCT frame) | `jpeg.precision=16` | 3 | 0 | 1 | 0 |
| JPEG lossless 5-bit samples (scaled to 16 bits) | `jpeg.precision=5` | 1 | 0 | 0 | 0 |
| JPEG lossless 10-bit samples | `jpeg.precision=10` | 1 | 0 | 0 | 0 |
| JPEG lossless YCbCr with subsampled chroma | `jpeg.process=lossless` + `jpeg.sampling=4:2:0` | 1 | 0 | 0 | 0 |
| JPEG lossless with two lines of every component per MCU (vertical factor 2 for all) | `jpeg.process=lossless` + `jpeg.sampling=1x2` | 2 | 0 | 0 | 0 |
| JPEG four components (CMYK/YCCK inks), loaded only with a color conversion (rejected) | `jpeg.components=4` | 0 | 0 | 3 | 0 |
| WebP simple layout | `webp.layout=simple` | 12 | 4 | 1 | 2 |
| WebP extended layout (`VP8X`) | `webp.layout=extended` | 9 | 4 | 0 | 3 |
| WebP `VP8X` alpha flag | `webp.vp8x.alphaFlag` | 7 | 3 | 0 | 3 |
| WebP lossy (VP8 key frames) | `webp.bitstream=vp8` | 9 | 2 | 1 | 1 |
| WebP lossless (VP8L) | `webp.bitstream=vp8l` | 13 | 5 | 0 | 4 |
| WebP lossy alpha (`ALPH`) | `webp.alpha=alph` | 3 | 0 | 0 | 0 |
| WebP `ALPH` uncompressed | `webp.alph.compression=none` | 1 | 0 | 0 | 0 |
| WebP `ALPH` lossless-compressed (VP8L image stream) | `webp.alph.compression=vp8l` | 2 | 0 | 0 | 0 |
| WebP `ALPH` without prediction filter | `webp.alph.filter=none` | 2 | 0 | 0 | 0 |
| WebP `ALPH` horizontal prediction filter | `webp.alph.filter=horizontal` | 1 | 0 | 0 | 0 |
| WebP `ALPH` color indexing transform | `webp.alph.transform=color-indexing` | 2 | 0 | 0 | 0 |
| WebP lossless alpha (VP8L alpha hint) | `webp.alpha=vp8l` | 9 | 3 | 0 | 4 |
| VP8 normal loop filter | `webp.vp8.filter=normal` | 8 | 2 | 0 | 1 |
| VP8 simple loop filter | `webp.vp8.filter=simple` | 1 | 0 | 0 | 0 |
| VP8 loop filter level 0 | `webp.vp8.filterLevel=0` | 1 | 0 | 0 | 0 |
| VP8 segmentation | `webp.vp8.segmentation` | 3 | 2 | 0 | 1 |
| VP8 one token partition | `webp.vp8.partitions=1` | 9 | 2 | 0 | 1 |
| VP8 interframe (rejected) | `webp.vp8.frame=inter` | 0 | 0 | 1 | 0 |
| VP8L predictor transform | `webp.vp8l.transform=predictor` | 6 | 3 | 0 | 2 |
| VP8L color indexing transform | `webp.vp8l.transform=color-indexing` | 7 | 2 | 0 | 2 |
| WebP ICC profile (`ICCP`) | `webp.chunk=ICCP` | 2 | 1 | 0 | 1 |
| WebP EXIF (`EXIF`) | `webp.chunk=EXIF` | 2 | 0 | 0 | 1 |
| WebP XMP (`XMP `) | `webp.chunk=XMP` | 2 | 0 | 0 | 1 |
| WebP unknown chunks (skipped) | `webp.chunk=unknown` | 1 | 0 | 0 | 0 |
| WebP animations (`ANIM`, `ANMF`) | `webp.animation` | 4 | 1 | 0 | 2 |
| WebP alpha blending | `webp.blend=alpha` | 3 | 2 | 0 | 2 |
| WebP no blending | `webp.blend=none` | 4 | 2 | 0 | 2 |
| WebP dispose to background | `webp.dispose=background` | 3 | 2 | 0 | 2 |
| WebP no disposal | `webp.dispose=none` | 4 | 2 | 0 | 2 |
| WebP partial frame rectangles | `webp.frame=partial` | 3 | 2 | 0 | 2 |
| WebP mixed lossy and lossless frames | `webp.frames=mixed` | 1 | 0 | 0 | 0 |
| WebP infinite loop (loop count 0) | `webp.loop=infinite` | 1 | 0 | 0 | 0 |
| WebP finite loop count | `webp.loop=finite` | 3 | 1 | 0 | 2 |
| QOI RGB streams (3 channels) | `qoi.channels=3` | 3 | 0 | 0 | 1 |
| QOI RGBA streams (4 channels) | `qoi.channels=4` | 7 | 0 | 0 | 2 |
| QOI sRGB colorspace indicator (0) | `qoi.colorspace=srgb` | 8 | 0 | 0 | 3 |
| QOI linear colorspace indicator (1, `TransferFunction.Linear`) | `qoi.colorspace=linear` | 2 | 0 | 0 | 0 |
| QOI `QOI_OP_RGB` | `qoi.op=rgb` | 6 | 0 | 0 | 2 |
| QOI `QOI_OP_RGBA` | `qoi.op=rgba` | 8 | 0 | 0 | 2 |
| QOI `QOI_OP_INDEX` | `qoi.op=index` | 6 | 0 | 0 | 2 |
| QOI `QOI_OP_DIFF` | `qoi.op=diff` | 6 | 0 | 0 | 3 |
| QOI `QOI_OP_LUMA` | `qoi.op=luma` | 5 | 0 | 0 | 3 |
| QOI `QOI_OP_RUN` | `qoi.op=run` | 5 | 0 | 0 | 2 |
| QOI run of 1 pixel | `qoi.run=1` | 3 | 0 | 0 | 1 |
| QOI run of 62 pixels (longest) | `qoi.run=62` | 2 | 0 | 0 | 2 |
| QOI run across rows | `qoi.run.crossesRow` | 2 | 0 | 0 | 2 |
| QOI run from the initial pixel | `qoi.run.atStart` | 1 | 0 | 0 | 1 |
| QOI run ending the image | `qoi.run.atEnd` | 4 | 0 | 0 | 2 |
| QOI `QOI_OP_DIFF` wrap-around | `qoi.diff.wrap` | 3 | 0 | 0 | 2 |
| QOI `QOI_OP_LUMA` wrap-around | `qoi.luma.wrap` | 2 | 0 | 0 | 2 |
| QOI index slot never written | `qoi.index.unset` | 1 | 0 | 0 | 1 |
| QOI index slot of the initial pixel | `qoi.index.initialPixel` | 1 | 0 | 0 | 1 |
| QOI alpha kept by RGB/DIFF/LUMA chunks | `qoi.alpha.kept` | 3 | 0 | 0 | 1 |
| QOI RGBA chunks in an RGB stream | `qoi.rgbStream.alphaChunks` | 1 | 0 | 0 | 0 |
| QOI odd width | `qoi.width=odd` | 7 | 0 | 0 | 3 |
| BMP 1-bit indexed | `bmp.bpp=1` | 2 | 0 | 0 | 0 |
| BMP 4-bit indexed | `bmp.bpp=4` | 5 | 0 | 0 | 0 |
| BMP 8-bit indexed | `bmp.bpp=8` | 4 | 0 | 0 | 1 |
| BMP 16-bit samples | `bmp.bpp=16` | 3 | 0 | 0 | 0 |
| BMP 24-bit BGR | `bmp.bpp=24` | 7 | 0 | 0 | 1 |
| BMP 32-bit samples | `bmp.bpp=32` | 3 | 0 | 0 | 0 |
| BMP palette | `bmp.palette` | 11 | 0 | 0 | 1 |
| BMP `RGBTRIPLE` palette entries (OS/2 1.x) | `bmp.paletteEntry=rgbtriple` | 3 | 0 | 0 | 0 |
| BMP palette length taken from `bfOffBits` (OS/2 1.x) | `bmp.paletteFromOffset` | 1 | 0 | 0 | 0 |
| BMP OS/2 `BITMAPCOREHEADER` (12 bytes) | `bmp.header=12` | 4 | 0 | 0 | 0 |
| BMP OS/2 `BITMAPCOREHEADER2` cut after its bit count (16 bytes) | `bmp.header=16` | 1 | 0 | 0 | 0 |
| BMP `BITMAPINFOHEADER` (40 bytes) | `bmp.header=40` | 15 | 0 | 0 | 2 |
| BMP `BITMAPV3INFOHEADER` (56 bytes) | `bmp.header=56` | 1 | 0 | 0 | 0 |
| BMP OS/2 `BITMAPCOREHEADER2` (64 bytes) | `bmp.header=64` | 2 | 0 | 0 | 0 |
| BMP `BITMAPV4HEADER` (108 bytes) | `bmp.header=108` | 1 | 0 | 0 | 0 |
| BMP `BI_RGB` (implicit masks) | `bmp.compression=rgb` | 16 | 0 | 0 | 2 |
| BMP `BI_BITFIELDS` (explicit masks) | `bmp.compression=bitfields` | 3 | 0 | 0 | 0 |
| BMP `BI_RLE8` | `bmp.compression=rle8` | 2 | 0 | 0 | 0 |
| BMP `BI_RLE4` | `bmp.compression=rle4` | 2 | 0 | 0 | 0 |
| BMP OS/2 `BCA_RLE24` | `bmp.compression=rle24` | 1 | 0 | 0 | 0 |
| BMP run-length absolute run | `bmp.rle=absolute` | 4 | 0 | 0 | 0 |
| BMP run-length absolute run with a padding byte | `bmp.rle=absolutePadding` | 3 | 0 | 0 | 0 |
| BMP run-length delta | `bmp.rle=delta` | 3 | 0 | 0 | 0 |
| BMP run-length end of line before the end of the row | `bmp.rle=earlyEndOfLine` | 3 | 0 | 0 | 0 |
| BMP run-length end of bitmap before the last pixel | `bmp.rle=earlyEndOfBitmap` | 2 | 0 | 0 | 0 |
| BMP real alpha mask | `bmp.alphaMask=yes` | 2 | 0 | 0 | 0 |
| BMP unspecified fourth byte (no alpha mask) | `bmp.alphaMask=no` | 4 | 0 | 0 | 0 |
| BMP bottom-up rows | `bmp.rowOrder=bottom-up` | 23 | 0 | 0 | 2 |
| BMP top-down rows (negative `biHeight`) | `bmp.rowOrder=top-down` | 1 | 0 | 0 | 0 |
| BMP row padding to four bytes | `bmp.rowPadding` | 16 | 0 | 0 | 2 |
| BMP gap between the palette and the pixel data | `bmp.gapBeforePixels` | 2 | 0 | 0 | 1 |
| BMP resolution (`biXPelsPerMeter`) | `bmp.resolution` | 2 | 0 | 0 | 0 |
| BMP odd width | `bmp.width=odd` | 20 | 0 | 0 | 2 |
| BMP OS/2 Huffman 1D (rejected) | `bmp.unsupported=huffman1d` | 0 | 0 | 1 | 0 |
| BMP embedded JPEG/PNG payload (rejected) | `bmp.unsupported=embedded-codec` | 0 | 0 | 1 | 0 |
| BMP 2 bits per pixel (rejected) | `bmp.unsupported=bit-count-2` | 0 | 0 | 1 | 0 |
| BMP channel mask wider than 8 bits (rejected) | `bmp.unsupported=wide-mask` | 0 | 0 | 1 | 0 |
| TGA color-mapped (type 1) | `tga.imageType=1` | 3 | 0 | 0 | 0 |
| TGA uncompressed true color (type 2) | `tga.imageType=2` | 10 | 0 | 0 | 1 |
| TGA uncompressed grayscale (type 3) | `tga.imageType=3` | 3 | 0 | 0 | 0 |
| TGA run-length color-mapped (type 9) | `tga.imageType=9` | 1 | 0 | 0 | 0 |
| TGA run-length true color (type 10) | `tga.imageType=10` | 3 | 0 | 0 | 1 |
| TGA run-length grayscale (type 11) | `tga.imageType=11` | 3 | 0 | 0 | 0 |
| TGA 8-bit samples or indexes | `tga.depth=8` | 5 | 0 | 0 | 0 |
| TGA 15-bit samples | `tga.depth=15` | 1 | 0 | 0 | 0 |
| TGA 16-bit samples, gray pixels or indexes | `tga.depth=16` | 6 | 0 | 0 | 0 |
| TGA 24-bit samples | `tga.depth=24` | 5 | 0 | 0 | 2 |
| TGA 32-bit samples | `tga.depth=32` | 6 | 0 | 0 | 0 |
| TGA no declared alpha bit | `tga.alphaBits=0` | 13 | 0 | 0 | 2 |
| TGA one declared alpha bit | `tga.alphaBits=1` | 1 | 0 | 0 | 0 |
| TGA eight declared alpha bits | `tga.alphaBits=8` | 9 | 0 | 0 | 0 |
| TGA 16-bit grayscale, the attribute byte is alpha | `tga.grayAttribute=alpha` | 2 | 0 | 0 | 0 |
| TGA 16-bit grayscale, the attribute byte is unspecified (discarded) | `tga.grayAttribute=unspecified` | 1 | 0 | 0 | 0 |
| TGA bottom-up rows | `tga.rowOrder=bottom-up` | 15 | 0 | 0 | 1 |
| TGA top-down rows | `tga.rowOrder=top-down` | 8 | 0 | 0 | 1 |
| TGA left-to-right columns | `tga.columnOrder=left-to-right` | 22 | 0 | 0 | 2 |
| TGA right-to-left columns | `tga.columnOrder=right-to-left` | 1 | 0 | 0 | 0 |
| TGA color map | `tga.colorMap` | 4 | 0 | 0 | 0 |
| TGA color-map first-entry offset | `tga.colorMap.offset` | 2 | 0 | 0 | 0 |
| TGA 8-bit color-map indexes | `tga.indexBits=8` | 2 | 0 | 0 | 0 |
| TGA 16-bit color-map indexes | `tga.indexBits=16` | 2 | 0 | 0 | 0 |
| TGA run-length packet | `tga.rle.run` | 7 | 0 | 0 | 1 |
| TGA raw packet | `tga.rle.raw` | 7 | 0 | 0 | 1 |
| TGA 128-pixel packet (longest) | `tga.rle.packet=128` | 1 | 0 | 0 | 1 |
| TGA packet across scan lines | `tga.rle.crossesRow` | 4 | 0 | 0 | 1 |
| TGA image identification field | `tga.idField` | 1 | 0 | 0 | 0 |
| TGA 2.0 footer | `tga.footer` | 6 | 0 | 0 | 0 |
| TGA 2.0 extension area (attributes type) | `tga.attributesType` | 2 | 0 | 0 | 0 |
| TGA 2.0 developer area (not interpreted) | `tga.developerArea` | 1 | 0 | 0 | 0 |
| TGA bytes after the image data | `tga.trailingData` | 6 | 0 | 0 | 0 |
| TGA odd width | `tga.width=odd` | 19 | 0 | 0 | 1 |
| TGA premultiplied alpha (rejected) | `tga.unsupported=premultiplied-alpha` | 0 | 0 | 1 | 0 |
| Netpbm plain PBM (`P1`) | `pnm.magic=P1` | 1 | 0 | 0 | 0 |
| Netpbm plain PGM (`P2`) | `pnm.magic=P2` | 1 | 0 | 0 | 0 |
| Netpbm plain PPM (`P3`) | `pnm.magic=P3` | 1 | 0 | 0 | 0 |
| Netpbm binary PBM (`P4`) | `pnm.magic=P4` | 1 | 0 | 0 | 1 |
| Netpbm binary PGM (`P5`) | `pnm.magic=P5` | 3 | 0 | 0 | 0 |
| Netpbm binary PPM (`P6`) | `pnm.magic=P6` | 3 | 0 | 0 | 1 |
| Netpbm PAM (`P7`) | `pnm.magic=P7` | 5 | 0 | 0 | 0 |
| Netpbm plain (ASCII) raster | `pnm.raster=plain` | 3 | 0 | 0 | 0 |
| Netpbm binary raster | `pnm.raster=binary` | 12 | 0 | 0 | 2 |
| Netpbm `BLACKANDWHITE` tuple | `pnm.tuple=BLACKANDWHITE` | 3 | 0 | 0 | 1 |
| Netpbm `GRAYSCALE` tuple | `pnm.tuple=GRAYSCALE` | 4 | 0 | 0 | 0 |
| Netpbm `RGB` tuple | `pnm.tuple=RGB` | 4 | 0 | 0 | 1 |
| Netpbm `GRAYSCALE_ALPHA` tuple | `pnm.tuple=GRAYSCALE_ALPHA` | 1 | 0 | 0 | 0 |
| Netpbm `RGB_ALPHA` tuple | `pnm.tuple=RGB_ALPHA` | 3 | 0 | 0 | 0 |
| Netpbm `MAXVAL` 1 | `pnm.maxval=1` | 3 | 0 | 0 | 1 |
| Netpbm `MAXVAL` 255 (8-bit samples) | `pnm.maxval=255` | 7 | 0 | 0 | 1 |
| Netpbm `MAXVAL` 65535 (16-bit samples) | `pnm.maxval=65535` | 3 | 0 | 0 | 0 |
| Netpbm `MAXVAL` needing normalization | `pnm.maxval.normalized` | 2 | 0 | 0 | 0 |
| Netpbm header comments | `pnm.comments` | 4 | 0 | 0 | 1 |
| Netpbm PBM row padding bits | `pnm.pbm.rowPadding` | 2 | 0 | 0 | 1 |
| Netpbm bytes after the first image (not read) | `pnm.trailingData` | 3 | 0 | 0 | 0 |
| Netpbm odd width | `pnm.width=odd` | 15 | 0 | 0 | 2 |
| Netpbm unsupported PAM tuple type (rejected) | `pnm.unsupported=tuple-type` | 0 | 0 | 1 | 0 |
| AVIF 8-bit samples | `avif.bitDepth=8` | 135 | 0 | 0 | 0 |
| AVIF 10-bit samples | `avif.bitDepth=10` | 30 | 0 | 0 | 0 |
| AVIF 12-bit samples | `avif.bitDepth=12` | 16 | 0 | 0 | 0 |
| AVIF 4:2:0, 10-bit | `avif.subsampling=4:2:0` + `avif.bitDepth=10` | 10 | 0 | 0 | 0 |
| AVIF 4:2:0, 12-bit | `avif.subsampling=4:2:0` + `avif.bitDepth=12` | 4 | 0 | 0 | 0 |
| AVIF 4:4:4 | `avif.subsampling=4:4:4` | 102 | 0 | 0 | 0 |
| AVIF 4:2:2 | `avif.subsampling=4:2:2` | 17 | 0 | 0 | 0 |
| AVIF 4:2:0 | `avif.subsampling=4:2:0` | 47 | 0 | 0 | 0 |
| AVIF monochrome | `avif.subsampling=4:0:0` | 14 | 0 | 0 | 0 |
| AVIF monochrome, 10-bit | `avif.subsampling=4:0:0` + `avif.bitDepth=10` | 4 | 0 | 0 | 0 |
| AVIF monochrome, 12-bit | `avif.subsampling=4:0:0` + `avif.bitDepth=12` | 4 | 0 | 0 | 0 |
| AVIF monochrome, limited range | `avif.subsampling=4:0:0` + `avif.range=limited` | 2 | 0 | 0 | 0 |
| AVIF identity matrix (RGB planes) | `avif.matrix=0` | 79 | 0 | 0 | 0 |
| AVIF BT.709 matrix | `avif.matrix=1` | 15 | 0 | 0 | 0 |
| AVIF unspecified matrix (BT.601) | `avif.matrix=2` | 5 | 0 | 0 | 0 |
| AVIF FCC matrix | `avif.matrix=4` | 2 | 0 | 0 | 0 |
| AVIF BT.470BG matrix | `avif.matrix=5` | 2 | 0 | 0 | 0 |
| AVIF BT.601 matrix | `avif.matrix=6` | 38 | 0 | 0 | 0 |
| AVIF SMPTE 240M matrix | `avif.matrix=7` | 2 | 0 | 0 | 0 |
| AVIF YCgCo matrix | `avif.matrix=8` | 3 | 0 | 0 | 0 |
| AVIF BT.2020 non-constant luminance matrix | `avif.matrix=9` | 10 | 0 | 0 | 0 |
| AVIF SMPTE ST 2085 matrix | `avif.matrix=11` | 1 | 0 | 0 | 0 |
| AVIF chromaticity-derived non-constant luminance matrix | `avif.matrix=12` | 2 | 0 | 0 | 0 |
| AVIF chromaticity-derived matrix, limited range, 10-bit | `avif.matrix=12` + `avif.range=limited` + `avif.bitDepth=10` | 1 | 0 | 0 | 0 |
| AVIF BT.2020 constant luminance matrix | `avif.matrix=10` | 2 | 0 | 0 | 0 |
| AVIF chromaticity-derived constant luminance matrix | `avif.matrix=13` | 1 | 0 | 0 | 0 |
| AVIF ICtCp (PQ and HLG) | `avif.matrix=14` | 4 | 0 | 0 | 0 |
| AVIF ICtCp, limited range, in an image sequence | `avif.matrix=14` + `avif.range=limited` + `avif.sequence` | 1 | 0 | 0 | 0 |
| AVIF BT.2020 matrix, limited range | `avif.matrix=9` + `avif.range=limited` | 6 | 0 | 0 | 0 |
| AVIF BT.2020 matrix, full range | `avif.matrix=9` + `avif.range=full` | 4 | 0 | 0 | 0 |
| AVIF full range | `avif.range=full` | 151 | 0 | 0 | 0 |
| AVIF limited range | `avif.range=limited` | 29 | 0 | 0 | 0 |
| AVIF color description (`colr` `nclx`) | `avif.colr=nclx` | 172 | 0 | 0 | 0 |
| AVIF ICC profile (`colr` `prof`) | `avif.colr=icc` | 3 | 0 | 0 | 0 |
| AVIF color description of the bitstream (no `colr`) | `avif.colr=none` | 8 | 0 | 0 | 0 |
| AVIF no `colr` box and media data first (a header identification cannot tell the color model) | `avif.colr=none` + `avif.layout=media-first` | 2 | 0 | 0 | 0 |
| AVIF chroma samples on the luma columns | `avif.chromaSamplePosition=vertical` | 3 | 0 | 0 | 0 |
| AVIF co-located chroma samples | `avif.chromaSamplePosition=colocated` | 2 | 0 | 0 | 0 |
| AVIF lossless AV1 coding | `avif.lossless` | 69 | 0 | 0 | 0 |
| AVIF film grain | `avif.filmGrain` | 6 | 0 | 0 | 0 |
| AVIF film grain, 4:2:2 | `avif.filmGrain` + `avif.subsampling=4:2:2` | 1 | 0 | 0 | 0 |
| AVIF film grain, 4:4:4 | `avif.filmGrain` + `avif.subsampling=4:4:4` | 1 | 0 | 0 | 0 |
| AVIF film grain, 12-bit | `avif.filmGrain` + `avif.bitDepth=12` | 1 | 0 | 0 | 0 |
| AVIF film grain under an alpha plane | `avif.filmGrain` + `avif.alpha` | 1 | 0 | 0 | 0 |
| AVIF alpha (auxiliary image or track) | `avif.alpha` | 39 | 0 | 0 | 0 |
| AVIF alpha, 10-bit | `avif.alpha` + `avif.bitDepth=10` | 6 | 0 | 0 | 0 |
| AVIF alpha, 12-bit | `avif.alpha` + `avif.bitDepth=12` | 2 | 0 | 0 | 0 |
| AVIF alpha under limited-range colors | `avif.alpha` + `avif.range=limited` | 1 | 0 | 0 | 0 |
| AVIF premultiplied alpha (`prem` reference) | `avif.premultiplied` | 4 | 0 | 0 | 0 |
| AVIF premultiplied alpha, 10-bit | `avif.premultiplied` + `avif.bitDepth=10` | 1 | 0 | 0 | 0 |
| AVIF premultiplied alpha, matrixed colors | `avif.premultiplied` + `avif.matrix=6` | 2 | 0 | 0 | 0 |
| AVIF sequence with premultiplied alpha (`prem` track reference) | `avif.sequence` + `avif.premultiplied` | 1 | 0 | 0 | 0 |
| AVIF grids | `avif.grid` | 12 | 0 | 0 | 0 |
| AVIF grid of 3 columns | `avif.grid=3x1` | 1 | 0 | 0 | 0 |
| AVIF grid of 3 rows | `avif.grid=1x3` | 1 | 0 | 0 | 0 |
| AVIF grid of 3 columns and 2 rows | `avif.grid=3x2` | 1 | 0 | 0 | 0 |
| AVIF grid with an alpha grid | `avif.grid` + `avif.alpha` | 2 | 0 | 0 | 0 |
| AVIF grid of 10-bit tiles | `avif.grid` + `avif.bitDepth=10` | 1 | 0 | 0 | 0 |
| AVIF grid whose tiles repeat its transformations (as avifenc writes them) | `avif.gridTileTransforms` | 1 | 0 | 0 | 0 |
| AVIF rotation (`irot`) | `avif.irot` | 16 | 0 | 0 | 0 |
| AVIF mirror (`imir`) | `avif.imir` | 7 | 0 | 0 | 0 |
| AVIF rotation and mirror | `avif.irot` + `avif.imir` | 4 | 0 | 0 | 0 |
| AVIF clean aperture (`clap`) | `avif.clap` | 11 | 0 | 0 | 0 |
| AVIF content light level (`clli`) | `avif.clli` | 3 | 0 | 0 | 0 |
| AVIF mastering display color volume (`mdcv`) | `avif.mdcv` | 2 | 0 | 0 | 0 |
| AVIF pixel aspect ratio (`pasp`) | `avif.pasp` | 2 | 0 | 0 | 0 |
| AVIF sequence with a content light level, a mastering display and a pixel aspect ratio in its sample entry | `avif.sequence` + `avif.clli` + `avif.mdcv` + `avif.pasp` | 1 | 0 | 0 | 0 |
| AVIF transformations that are not in the order of MIAF (applied in the order of their association) | `avif.transformOrder=association` | 2 | 0 | 0 | 0 |
| AVIF clean aperture after a rotation or a mirroring | `avif.clap` + `avif.transformOrder=association` | 2 | 0 | 0 | 0 |
| AVIF Exif item | `avif.metadata=exif` | 8 | 0 | 0 | 0 |
| AVIF XMP item | `avif.metadata=xmp` | 10 | 0 | 0 | 0 |
| AVIF major brand `avif` | `avif.majorBrand=avif` | 142 | 0 | 0 | 0 |
| AVIF major brand `avis` | `avif.majorBrand=avis` | 46 | 0 | 0 | 0 |
| AVIF major brand `mif1`, `avif` compatible | `avif.majorBrand=mif1` | 1 | 0 | 0 | 0 |
| AVIF media data before their index | `avif.layout=media-first` | 4 | 0 | 0 | 0 |
| AVIF item data in `idat` | `avif.idat` | 3 | 0 | 0 | 0 |
| AVIF item in several extents | `avif.extents` | 9 | 0 | 0 | 0 |
| AVIF `free` boxes | `avif.boxes=free` | 1 | 0 | 0 | 0 |
| AVIF unknown boxes (skipped) | `avif.boxes=unknown` | 7 | 0 | 0 | 0 |
| AVIF 64-bit box sizes | `avif.boxes=large-size` | 1 | 0 | 0 | 0 |
| AVIF box that extends to the end of the file | `avif.boxes=to-end` | 1 | 0 | 0 | 0 |
| AVIF bytes after the last item (not read) | `avif.trailingData` | 1 | 0 | 0 | 0 |
| AVIF image sequences | `avif.sequence` | 46 | 0 | 0 | 0 |
| AVIF sequence with inter frames | `avif.seq.interFrames` | 46 | 0 | 0 | 0 |
| AVIF sequence with frames that are decoded without being shown | `avif.seq.hiddenFrames` | 15 | 0 | 0 | 0 |
| AVIF sequence, monochrome | `avif.sequence` + `avif.subsampling=4:0:0` | 2 | 0 | 0 | 0 |
| AVIF sequence, 4:2:2 | `avif.sequence` + `avif.subsampling=4:2:2` | 1 | 0 | 0 | 0 |
| AVIF sequence, 10-bit | `avif.sequence` + `avif.bitDepth=10` | 6 | 0 | 0 | 0 |
| AVIF sequence, 12-bit | `avif.sequence` + `avif.bitDepth=12` | 1 | 0 | 0 | 0 |
| AVIF sequence with an alpha track | `avif.sequence` + `avif.alpha` | 11 | 0 | 0 | 0 |
| AVIF sequence with an ICC profile | `avif.sequence` + `avif.colr=icc` | 1 | 0 | 0 | 0 |
| AVIF sequence with Exif data | `avif.sequence` + `avif.metadata=exif` | 6 | 0 | 0 | 0 |
| AVIF sequence with an XMP packet | `avif.sequence` + `avif.metadata=xmp` | 8 | 0 | 0 | 0 |
| AVIF sequence metadata in the `meta` box of the track | `avif.seq.metadataSource=track` | 5 | 0 | 0 | 0 |
| AVIF sequence metadata in the `meta` box of the file | `avif.seq.metadataSource=file` | 3 | 0 | 0 | 0 |
| AVIF sequence metadata in an `idat` box | `avif.sequence` + `avif.idat` | 2 | 0 | 0 | 0 |
| AVIF sequence metadata whose data follow the samples | `avif.seq.metadataAfterSamples` | 2 | 0 | 0 | 0 |
| AVIF sequence whose file-level Exif and XMP items are not its metadata | `avif.seq.fileMetadataNotRead` | 2 | 0 | 0 | 0 |
| AVIF sequence whose first sample is not the first one in the file | `avif.seq.samplesOutOfOrder` | 1 | 0 | 0 | 0 |
| AVIF sequence whose track header matrix is an orientation | `avif.seq.matrix` | 2 | 0 | 0 | 0 |
| AVIF sequence turned a quarter turn clockwise by its matrix | `avif.seq.matrix=6` | 1 | 0 | 0 | 0 |
| AVIF sequence mirrored by its matrix | `avif.seq.matrix=2` | 1 | 0 | 0 | 0 |
| AVIF sequence with a clean aperture in its sample entries | `avif.seq.clap` | 1 | 0 | 0 | 0 |
| AVIF sequence with a clean aperture and a matrix on its alpha track too | `avif.seq.clap` + `avif.seq.matrix` + `avif.alpha` | 1 | 0 | 0 | 0 |
| AVIF sequence without edit list (infinite) | `avif.seq.editList=none` | 2 | 0 | 0 | 0 |
| AVIF sequence played once | `avif.seq.editList=once` | 12 | 0 | 0 | 0 |
| AVIF sequence repeated a number of times | `avif.seq.editList=repeat` | 11 | 0 | 0 | 0 |
| AVIF sequence repeated forever (indefinite track duration) | `avif.seq.editList=repeat-indefinite` | 21 | 0 | 0 | 0 |
| AVIF sample sizes in `stsz` | `avif.seq.sampleSizes=stsz` | 45 | 0 | 0 | 0 |
| AVIF compact sample sizes (`stz2`) | `avif.seq.sampleSizes=stz2` | 1 | 0 | 0 | 0 |
| AVIF chunk offsets in `stco` | `avif.seq.chunkOffsets=stco` | 45 | 0 | 0 | 0 |
| AVIF 64-bit chunk offsets (`co64`) | `avif.seq.chunkOffsets=co64` | 1 | 0 | 0 | 0 |
| AVIF layered images (a frame per spatial layer) | `avif.layers` | 24 | 0 | 0 | 0 |
| AVIF layered image, 2 layers | `avif.layers=2` | 10 | 0 | 0 | 0 |
| AVIF layered image, 3 layers | `avif.layers=3` | 12 | 0 | 0 | 0 |
| AVIF layered image, 4 layers | `avif.layers=4` | 2 | 0 | 0 | 0 |
| AVIF layers of growing sizes | `avif.layers.scaled` | 7 | 0 | 0 | 0 |
| AVIF lower layers that are decoded without being returned | `avif.layers.hiddenPixels` | 18 | 0 | 0 | 0 |
| AVIF layer sizes (`a1lx`, not needed) | `avif.a1lx` | 23 | 0 | 0 | 0 |
| AVIF layered image with a layered alpha plane | `avif.layers` + `avif.alpha` | 1 | 0 | 0 | 0 |
| AVIF layered image, 10-bit | `avif.layers` + `avif.bitDepth=10` | 4 | 0 | 0 | 0 |
| AVIF layered image, 12-bit | `avif.layers` + `avif.bitDepth=12` | 2 | 0 | 0 | 0 |
| AVIF layered image, monochrome | `avif.layers` + `avif.subsampling=4:0:0` | 2 | 0 | 0 | 0 |
| AVIF layered image, 4:2:0 | `avif.layers` + `avif.subsampling=4:2:0` | 4 | 0 | 0 | 0 |
| AVIF layered image, 4:2:2 | `avif.layers` + `avif.subsampling=4:2:2` | 1 | 0 | 0 | 0 |
| AVIF grid of layered tiles | `avif.layers` + `avif.grid` | 3 | 0 | 0 | 0 |
| AVIF sequence of layered samples | `avif.layers` + `avif.sequence` | 1 | 0 | 0 | 0 |
| AVIF layer selection (`lsel`) | `avif.lsel` | 9 | 0 | 0 | 0 |
| AVIF `lsel` that selects no layer (0xFFFF) | `avif.lsel=65535` | 1 | 0 | 0 | 0 |
| AVIF `lsel` layer 0 | `avif.lsel=0` | 5 | 0 | 0 | 0 |
| AVIF `lsel` layer 1 | `avif.lsel=1` | 2 | 0 | 0 | 0 |
| AVIF `lsel` layer 2 | `avif.lsel=2` | 1 | 0 | 0 | 0 |
| AVIF `lsel` layer 3 | `avif.lsel=3` | 1 | 0 | 0 | 0 |
| AVIF `lsel` of a frame smaller than the last layer | `avif.lsel=0` + `avif.layers.scaled` | 1 | 0 | 0 | 0 |
| AVIF operating point selection (`a1op`) | `avif.a1op` | 6 | 0 | 0 | 0 |
| AVIF `a1op` operating point 0 | `avif.a1op=0` | 1 | 0 | 0 | 0 |
| AVIF `a1op` operating point 1 | `avif.a1op=1` | 4 | 0 | 0 | 0 |
| AVIF `a1op` operating point 2 | `avif.a1op=2` | 2 | 0 | 0 | 0 |
| AVIF `a1op` and `lsel` together | `avif.a1op` + `avif.lsel` | 1 | 0 | 0 | 0 |
| AVIF grid whose tiles select their own layer or operating point | `avif.grid` + `avif.layers=3` | 2 | 0 | 0 | 0 |
| AVIF identity derivation (`iden`) | `avif.iden=1` | 6 | 0 | 0 | 0 |
| AVIF nested identity derivations | `avif.iden=2` | 1 | 0 | 0 | 0 |
| AVIF identity derivation with transformations on it and on its input | `avif.iden` + `avif.clap` + `avif.irot=1` | 1 | 0 | 0 | 0 |
| AVIF identity derivation with an alpha plane | `avif.iden` + `avif.alpha` | 3 | 0 | 0 | 0 |
| AVIF identity derivation of a grid | `avif.iden` + `avif.grid=2x2` | 1 | 0 | 0 | 0 |
| AVIF identity derivation of matrixed colors | `avif.iden` + `avif.matrix=6` | 1 | 0 | 0 | 0 |
| AVIF overlay (`iovl`) | `avif.iovl` | 8 | 0 | 0 | 0 |
| AVIF overlay, 10-bit, under an identity derivation | `avif.iovl` + `avif.bitDepth=10` + `avif.derived.iden` | 1 | 0 | 0 | 0 |
| AVIF overlay with a clean aperture and a rotation | `avif.iovl` + `avif.clap` + `avif.irot=1` | 1 | 0 | 0 | 0 |
| AVIF grid of derived images | `avif.derived.grid` | 1 | 0 | 0 | 0 |
| AVIF identity derivation inside an image that is built | `avif.derived.iden` | 3 | 0 | 0 | 0 |
| AVIF image built from coded images that a matrix converts | `avif.derived.matrix=6` | 1 | 0 | 0 | 0 |
| AVIF image built from other images, ICC profile of its input images | `avif.derived.colr=icc` | 1 | 0 | 0 | 0 |
| AVIF image built from other images, color description of the bitstream | `avif.iovl` + `avif.colr=none` | 1 | 0 | 0 | 0 |
| AVIF image built from other images (pixels of its coded images) | `avif.derived.inputPixels` | 9 | 0 | 0 | 0 |
| AVIF derived images that are not supported (rejected) | `avif.unsupported=derived-image` | 0 | 0 | 5 | 0 |
| AVIF more than eight nested derivations (rejected) | `avif.unsupported=derivation-depth` | 0 | 0 | 2 | 0 |
| AVIF alpha plane that is not transformed as the color image (rejected) | `avif.unsupported=alpha-transform` | 0 | 0 | 1 | 0 |
| AVIF grid tile that declares other transformations than its grid (rejected) | `avif.unsupported=grid-tile-transform` | 0 | 0 | 1 | 0 |
| AVIF clean aperture that does not select whole samples (rejected) | `avif.unsupported=clap` | 0 | 0 | 1 | 0 |
| AVIF essential unknown property (rejected) | `avif.unsupported=essential-property` | 0 | 0 | 1 | 0 |
| AVIF matrix coefficients that are reserved, or that lack the transfer characteristics or the primaries they are defined with (rejected) | `avif.unsupported=matrix` | 0 | 0 | 4 | 0 |
| AVIF alpha plane of another size (rejected) | `avif.unsupported=alpha-size` | 0 | 0 | 1 | 0 |
| AVIF fragmented sequence | `avif.seq.fragments` | 6 | 0 | 0 | 0 |
| AVIF fragmented sequence, three fragments | `avif.seq.fragments=3` | 1 | 0 | 0 | 0 |
| AVIF fragmented sequence with alpha | `avif.seq.fragments` + `avif.alpha` | 2 | 0 | 0 | 0 |
| AVIF fragmented sequence, 10-bit | `avif.seq.fragments` + `avif.bitDepth=10` | 1 | 0 | 0 | 0 |
| AVIF movie fragments that are not supported (rejected) | `avif.unsupported=fragmented` | 0 | 0 | 5 | 0 |
| AVIF edit list with several edits | `avif.seq.edits` | 3 | 0 | 0 | 0 |
| AVIF edit list that leaves images out | `avif.seq.imagesLeftOut` | 3 | 0 | 0 | 0 |
| AVIF edit list that shows images for a part of their duration | `avif.seq.partialDurations` | 4 | 0 | 0 | 0 |
| AVIF edit list that leaves out an image the next ones refer to | `avif.seq.imagesLeftOut` + `avif.seq.interFrames` + `avif.seq.edits=2` | 2 | 0 | 0 | 0 |
| AVIF edit list of a sequence with alpha | `avif.seq.partialDurations` + `avif.alpha` | 1 | 0 | 0 | 0 |
| AVIF edit list that does not play the media forward at its rate (rejected) | `avif.unsupported=edit-list` | 0 | 0 | 5 | 0 |
| AVIF alpha track with another edit list than the color track (rejected) | `avif.unsupported=alpha-edit-list` | 0 | 0 | 1 | 0 |
| AVIF sequence whose images change size (rejected) | `avif.unsupported=size-change` | 0 | 0 | 1 | 0 |
| HEIC 8-bit samples | `heic.bitDepth=8` | 96 | 0 | 0 | 0 |
| HEIC 10-bit samples | `heic.bitDepth=10` | 15 | 0 | 0 | 0 |
| HEIC 12-bit samples | `heic.bitDepth=12` | 6 | 0 | 0 | 0 |
| HEIC 4:4:4 | `heic.subsampling=4:4:4` | 74 | 0 | 0 | 0 |
| HEIC 4:2:2 (chroma samples on the luma samples of even columns) | `heic.subsampling=4:2:2` | 7 | 0 | 0 | 0 |
| HEIC 4:2:2, 10-bit | `heic.subsampling=4:2:2` + `heic.bitDepth=10` | 1 | 0 | 0 | 0 |
| HEIC 4:2:2, 12-bit | `heic.subsampling=4:2:2` + `heic.bitDepth=12` | 1 | 0 | 0 | 0 |
| HEIC 4:2:0 | `heic.subsampling=4:2:0` | 27 | 0 | 0 | 0 |
| HEIC 4:2:0, 10-bit | `heic.subsampling=4:2:0` + `heic.bitDepth=10` | 5 | 0 | 0 | 0 |
| HEIC 4:2:0, 12-bit | `heic.subsampling=4:2:0` + `heic.bitDepth=12` | 1 | 0 | 0 | 0 |
| HEIC monochrome | `heic.subsampling=4:0:0` | 9 | 0 | 0 | 0 |
| HEIC monochrome, 10-bit | `heic.subsampling=4:0:0` + `heic.bitDepth=10` | 2 | 0 | 0 | 0 |
| HEIC monochrome, 12-bit | `heic.subsampling=4:0:0` + `heic.bitDepth=12` | 1 | 0 | 0 | 0 |
| HEIC monochrome, limited range | `heic.subsampling=4:0:0` + `heic.range=limited` | 2 | 0 | 0 | 0 |
| HEIC 4:2:0 chroma samples at the default location (left, between two rows: nothing is signalled) | `heic.chromaLoc=default` | 14 | 0 | 0 | 0 |
| HEIC default chroma sample location above 8 bits | `heic.chromaLoc=default` + `heic.bitDepth=10` | 3 | 0 | 0 | 0 |
| HEIC `chroma_sample_loc_type` 0, signalled | `heic.chromaLoc=0` | 1 | 0 | 0 | 0 |
| HEIC `chroma_sample_loc_type` 1 (centered) | `heic.chromaLoc=1` | 9 | 0 | 0 | 0 |
| HEIC `chroma_sample_loc_type` 2 (on the top-left luma sample) | `heic.chromaLoc=2` | 3 | 0 | 0 | 0 |
| HEIC identity matrix (RGB planes) | `heic.matrix=0` | 45 | 0 | 0 | 0 |
| HEIC BT.709 matrix | `heic.matrix=1` | 7 | 0 | 0 | 0 |
| HEIC unspecified matrix (BT.601) | `heic.matrix=2` | 19 | 0 | 0 | 0 |
| HEIC BT.470BG matrix | `heic.matrix=5` | 6 | 0 | 0 | 0 |
| HEIC BT.601 matrix | `heic.matrix=6` | 22 | 0 | 0 | 0 |
| HEIC YCgCo matrix | `heic.matrix=8` | 1 | 0 | 0 | 0 |
| HEIC BT.2020 non-constant luminance matrix | `heic.matrix=9` | 8 | 0 | 0 | 0 |
| HEIC full range | `heic.range=full` | 85 | 0 | 0 | 0 |
| HEIC limited range | `heic.range=limited` | 32 | 0 | 0 | 0 |
| HEIC color description (`colr` `nclx`) | `heic.colr=nclx` | 63 | 0 | 0 | 0 |
| HEIC ICC profile (`colr` `prof`) | `heic.colr=icc` | 5 | 0 | 0 | 0 |
| HEIC color description of the bitstream (no `colr`) | `heic.colr=none` | 50 | 0 | 0 | 0 |
| HEIC bitstream with a color description (video signal description of the VUI) | `heic.videoSignal=colour` | 89 | 0 | 0 | 0 |
| HEIC bitstream whose video signal description only gives the range | `heic.videoSignal=range` | 9 | 0 | 0 | 0 |
| HEIC bitstream without video signal description | `heic.videoSignal=none` | 19 | 0 | 0 | 0 |
| HEIC no `colr` box and no video signal description (limited range, converted as BT.601) | `heic.colr=none` + `heic.videoSignal=none` | 18 | 0 | 0 | 0 |
| HEIC monochrome without video signal description (limited range) | `heic.subsampling=4:0:0` + `heic.videoSignal=none` | 1 | 0 | 0 | 0 |
| HEIC no `colr` box and media data first | `heic.colr=none` + `heic.layout=media-first` | 2 | 0 | 0 | 0 |
| HEIC lossless HEVC coding (transform and quantization bypass) | `heic.lossless` | 38 | 0 | 0 | 0 |
| HEIC picture cropped by its conformance window | `heic.conformanceWindow` | 27 | 0 | 0 | 0 |
| HEIC conformance window that removes more than the padding (the coded picture counts toward the canvas limits) | `heic.checkedSize` | 2 | 0 | 0 | 0 |
| HEIC conformance window and clean aperture | `heic.conformanceWindow` + `heic.clap` | 5 | 0 | 0 | 0 |
| HEIC alpha (auxiliary image or track) | `heic.alpha` | 14 | 0 | 0 | 0 |
| HEIC alpha, 10-bit | `heic.alpha` + `heic.bitDepth=10` | 2 | 0 | 0 | 0 |
| HEIC alpha, 12-bit | `heic.alpha` + `heic.bitDepth=12` | 1 | 0 | 0 | 0 |
| HEIC alpha under limited-range colors | `heic.alpha` + `heic.range=limited` | 4 | 0 | 0 | 0 |
| HEIC alpha plane coded as a monochrome picture | `heic.alpha.subsampling=4:0:0` | 13 | 0 | 0 | 0 |
| HEIC alpha plane that is the luma plane of a 4:2:0 picture | `heic.alpha.subsampling=4:2:0` | 1 | 0 | 0 | 0 |
| HEIC alpha auxiliary type of HEVC (`urn:mpeg:hevc:2015:auxid:1`) | `heic.alpha.urn=hevc` | 13 | 0 | 0 | 0 |
| HEIC alpha auxiliary type of MPEG-B | `heic.alpha.urn=mpegB` | 1 | 0 | 0 | 0 |
| HEIC limited-range alpha plane | `heic.alpha.range=limited` | 1 | 0 | 0 | 0 |
| HEIC grids | `heic.grid` | 10 | 0 | 0 | 0 |
| HEIC grid of 3 columns and 2 rows | `heic.grid=3x2` | 1 | 0 | 0 | 0 |
| HEIC grid with an alpha grid | `heic.grid` + `heic.alpha` | 1 | 0 | 0 | 0 |
| HEIC grid of 10-bit tiles | `heic.grid` + `heic.bitDepth=10` | 2 | 0 | 0 | 0 |
| HEIC grid of 4:2:0 tiles | `heic.grid` + `heic.subsampling=4:2:0` | 5 | 0 | 0 | 0 |
| HEIC rotation (`irot`) | `heic.irot` | 9 | 0 | 0 | 0 |
| HEIC mirror (`imir`) | `heic.imir` | 3 | 0 | 0 | 0 |
| HEIC rotation and mirror | `heic.irot` + `heic.imir` | 1 | 0 | 0 | 0 |
| HEIC clean aperture (`clap`) | `heic.clap` | 61 | 0 | 0 | 0 |
| HEIC Exif item | `heic.metadata=exif` | 7 | 0 | 0 | 0 |
| HEIC XMP item | `heic.metadata=xmp` | 4 | 0 | 0 | 0 |
| HEIC thumbnail (not read) | `heic.thumbnail` | 4 | 0 | 0 | 0 |
| HEIC auxiliary image that is not an alpha plane (not read) | `heic.auxiliary=other` | 3 | 0 | 0 | 0 |
| HEIC major brand `heic` | `heic.majorBrand=heic` | 17 | 0 | 0 | 0 |
| HEIC major brand `heix` | `heic.majorBrand=heix` | 73 | 0 | 0 | 0 |
| HEIC major brand `hevc` | `heic.majorBrand=hevc` | 4 | 0 | 0 | 0 |
| HEIC major brand `hevx` | `heic.majorBrand=hevx` | 18 | 0 | 0 | 0 |
| HEIC major brand `mif1`, an HEVC brand compatible | `heic.majorBrand=mif1` | 2 | 0 | 0 | 0 |
| HEIC major brand `miaf`, an HEVC brand compatible | `heic.majorBrand=miaf` | 1 | 0 | 0 | 0 |
| HEIC major brand `msf1`, an HEVC brand compatible | `heic.majorBrand=msf1` | 2 | 0 | 0 | 0 |
| HEIC file that also declares a brand of AVIF | `heic.brands=avif` | 2 | 0 | 0 | 0 |
| HEIC NAL unit lengths of one byte | `heic.hvcc.lengthSize=1` | 1 | 0 | 0 | 0 |
| HEIC NAL unit lengths of two bytes | `heic.hvcc.lengthSize=2` | 2 | 0 | 0 | 0 |
| HEIC NAL unit lengths of four bytes | `heic.hvcc.lengthSize=4` | 114 | 0 | 0 | 0 |
| HEIC configuration record whose arrays are not complete | `heic.hvcc.incompleteArrays` | 76 | 0 | 0 | 0 |
| HEIC configuration record with several sequence and picture parameter sets | `heic.hvcc.severalParameterSets` | 1 | 0 | 0 | 0 |
| HEIC parameter sets in the coded data | `heic.parameterSets=in-band` | 3 | 0 | 0 | 0 |
| HEIC media data before their index | `heic.layout=media-first` | 3 | 0 | 0 | 0 |
| HEIC item data in `idat` | `heic.idat` | 11 | 0 | 0 | 0 |
| HEIC item in several extents | `heic.extents` | 1 | 0 | 0 | 0 |
| HEIC `free` boxes | `heic.boxes=free` | 1 | 0 | 0 | 0 |
| HEIC unknown boxes (skipped) | `heic.boxes=unknown` | 1 | 0 | 0 | 0 |
| HEIC 64-bit box sizes | `heic.boxes=large-size` | 1 | 0 | 0 | 0 |
| HEIC box that extends to the end of the file | `heic.boxes=to-end` | 1 | 0 | 0 | 0 |
| HEIC bytes after the last item (not read) | `heic.trailingData` | 2 | 0 | 0 | 0 |
| HEIC image sequences | `heic.sequence` | 24 | 0 | 0 | 0 |
| HEIC sequence with inter pictures | `heic.seq.interFrames` | 21 | 0 | 0 | 0 |
| HEIC sequence with composition offsets (`ctts`) | `heic.seq.compositionOffsets` | 6 | 0 | 0 | 0 |
| HEIC sequence whose images are not in the order of its samples | `heic.seq.reordered` | 6 | 0 | 0 | 0 |
| HEIC sequence whose edit starts at the presentation time of its first image | `heic.seq.editMediaTime` | 3 | 0 | 0 | 0 |
| HEIC sequence under an `hvc1` sample entry | `heic.seq.sampleEntry=hvc1` | 23 | 0 | 0 | 0 |
| HEIC sequence under an `hev1` sample entry | `heic.seq.sampleEntry=hev1` | 1 | 0 | 0 | 0 |
| HEIC sequence with a conformance window | `heic.sequence` + `heic.conformanceWindow` | 14 | 0 | 0 | 0 |
| HEIC sequence with a clean aperture in its sample entry | `heic.sequence` + `heic.clap` | 2 | 0 | 0 | 0 |
| HEIC sequence with a clean aperture and an alpha track | `heic.sequence` + `heic.clap` + `heic.alpha` | 1 | 0 | 0 | 0 |
| HEIC sequence, monochrome | `heic.sequence` + `heic.subsampling=4:0:0` | 1 | 0 | 0 | 0 |
| HEIC sequence, 4:2:2 | `heic.sequence` + `heic.subsampling=4:2:2` | 1 | 0 | 0 | 0 |
| HEIC sequence, 4:2:0 | `heic.sequence` + `heic.subsampling=4:2:0` | 4 | 0 | 0 | 0 |
| HEIC sequence, 10-bit | `heic.sequence` + `heic.bitDepth=10` | 1 | 0 | 0 | 0 |
| HEIC sequence with an alpha track | `heic.sequence` + `heic.alpha` | 2 | 0 | 0 | 0 |
| HEIC sequence with Exif data | `heic.sequence` + `heic.metadata=exif` | 2 | 0 | 0 | 0 |
| HEIC sequence metadata in the `meta` box of the track | `heic.seq.metadataSource=track` | 1 | 0 | 0 | 0 |
| HEIC sequence metadata in the `meta` box of the file | `heic.seq.metadataSource=file` | 1 | 0 | 0 | 0 |
| HEIC sequence metadata in an `idat` box | `heic.sequence` + `heic.idat` | 1 | 0 | 0 | 0 |
| HEIC sequence metadata whose data follow the samples | `heic.seq.metadataAfterSamples` | 1 | 0 | 0 | 0 |
| HEIC sequence whose file also has an image item, which is not read | `heic.seq.fileImageNotRead` | 10 | 0 | 0 | 0 |
| HEIC sequence whose first sample is not the first one in the file | `heic.seq.samplesOutOfOrder` | 1 | 0 | 0 | 0 |
| HEIC sequence without edit list (infinite) | `heic.seq.editList=none` | 9 | 0 | 0 | 0 |
| HEIC sequence played once | `heic.seq.editList=once` | 1 | 0 | 0 | 0 |
| HEIC sequence repeated a number of times | `heic.seq.editList=repeat` | 2 | 0 | 0 | 0 |
| HEIC sequence repeated forever (indefinite track duration) | `heic.seq.editList=repeat-indefinite` | 12 | 0 | 0 | 0 |
| HEIC sample sizes in `stsz` | `heic.seq.sampleSizes=stsz` | 23 | 0 | 0 | 0 |
| HEIC compact sample sizes (`stz2`) | `heic.seq.sampleSizes=stz2` | 1 | 0 | 0 | 0 |
| HEIC chunk offsets in `stco` | `heic.seq.chunkOffsets=stco` | 23 | 0 | 0 | 0 |
| HEIC 64-bit chunk offsets (`co64`) | `heic.seq.chunkOffsets=co64` | 1 | 0 | 0 | 0 |
| HEIC image of another codec than its brands, or brands of another codec than its image (rejected) | `heic.unsupported=codec` | 0 | 0 | 2 | 0 |
| HEIC layers of a layered HEVC bitstream (rejected) | `heic.unsupported=layers` | 0 | 0 | 2 | 0 |
| HEIC configuration record of another version (rejected) | `heic.unsupported=configuration-record` | 0 | 0 | 1 | 0 |
| HEIC premultiplied alpha (rejected) | `heic.unsupported=premultiplied-alpha` | 0 | 0 | 1 | 0 |
| HEIC derived images other than grids (rejected) | `heic.unsupported=derived-image` | 0 | 0 | 2 | 0 |
| HEIC grid tile with a clean aperture that its grid does not have (rejected) | `heic.unsupported=grid-tile-transform` | 0 | 0 | 1 | 0 |
| HEIC fractional clean aperture (rejected) | `heic.unsupported=clap` | 0 | 0 | 1 | 0 |
| HEIC essential unknown property (rejected) | `heic.unsupported=essential-property` | 0 | 0 | 1 | 0 |
| HEIC other matrix coefficients (rejected) | `heic.unsupported=matrix` | 0 | 0 | 1 | 0 |
| HEIC alpha plane of another size (rejected) | `heic.unsupported=alpha-size` | 0 | 0 | 1 | 0 |
| HEIC bit depths above 12, or luma and chroma planes of different bit depths (rejected) | `heic.unsupported=bit-depth` | 0 | 0 | 2 | 0 |
| HEIC extended precision processing (rejected) | `heic.unsupported=extended-precision` | 0 | 0 | 1 | 0 |
| HEIC chroma sample location of a bottom field (rejected) | `heic.unsupported=chroma-sample-location` | 0 | 0 | 1 | 0 |
| HEIC sequence whose pictures do not have the size of its sample entry (rejected) | `heic.unsupported=sequence-size` | 0 | 0 | 2 | 0 |
| HEIC fragmented files (rejected) | `heic.unsupported=fragmented` | 0 | 0 | 1 | 0 |
| HEIC edit list that does not play the whole track (rejected) | `heic.unsupported=edit-list` | 0 | 0 | 1 | 0 |
| JPEG XL alpha channel | `jxl.alpha` | 11 | 0 | 1 | 0 |
| JPEG XL animation | `jxl.animation` | 5 | 0 | 0 | 2 |
| JPEG XL 12-bit samples | `jxl.bits=12` | 1 | 0 | 0 | 0 |
| JPEG XL 16-bit samples | `jxl.bits=16` | 6 | 0 | 0 | 0 |
| JPEG XL 32-bit floating-point samples (rejected) | `jxl.bits=32` | 0 | 0 | 1 | 0 |
| JPEG XL 8-bit samples | `jxl.bits=8` | 47 | 7 | 3 | 6 |
| JPEG XL frame that is added | `jxl.blend=add` | 1 | 0 | 0 | 1 |
| JPEG XL frame with the blend mode | `jxl.blend=blend` | 2 | 0 | 0 | 1 |
| JPEG XL frame that is multiplied | `jxl.blend=multiply` | 1 | 0 | 0 | 0 |
| JPEG XL frame that replaces | `jxl.blend=replace` | 52 | 7 | 3 | 6 |
| JPEG XL `Exif` box | `jxl.box=Exif` | 2 | 0 | 0 | 1 |
| JPEG XL `abcd` box | `jxl.box=abcd` | 1 | 0 | 0 | 0 |
| JPEG XL `brob` box | `jxl.box=brob` | 1 | 0 | 0 | 0 |
| JPEG XL `free` box | `jxl.box=free` | 1 | 0 | 0 | 0 |
| JPEG XL `jbrd` box | `jxl.box=jbrd` | 5 | 0 | 0 | 0 |
| JPEG XL `jxlc` box | `jxl.box=jxlc` | 10 | 0 | 1 | 0 |
| JPEG XL `jxll` box | `jxl.box=jxll` | 5 | 0 | 1 | 0 |
| JPEG XL `jxlp` box | `jxl.box=jxlp` | 8 | 0 | 0 | 1 |
| JPEG XL `xml ` box | `jxl.box=xml` | 2 | 0 | 0 | 1 |
| JPEG XL boxes after the codestream | `jxl.boxesAfterCodestream` | 3 | 0 | 0 | 0 |
| JPEG XL enumerated color encoding other than sRGB | `jxl.color=enumerated` | 4 | 0 | 1 | 0 |
| JPEG XL ICC profile in the codestream | `jxl.color=icc` | 2 | 0 | 1 | 0 |
| JPEG XL grayscale | `jxl.colorSpace=gray` | 8 | 0 | 0 | 0 |
| JPEG XL RGB | `jxl.colorSpace=rgb` | 46 | 7 | 4 | 6 |
| JPEG XL samples stored in the color space of the image | `jxl.colorTransform=none` | 39 | 7 | 3 | 6 |
| JPEG XL samples stored in XYB (lossy) | `jxl.colorTransform=xyb` | 15 | 0 | 1 | 0 |
| JPEG XL YCbCr frame (recompressed JPEG) | `jxl.colorTransform=ycbcr` | 5 | 0 | 0 | 0 |
| JPEG XL Modular frame | `jxl.encoding=modular` | 37 | 7 | 3 | 6 |
| JPEG XL VarDCT frame | `jxl.encoding=vardct` | 17 | 0 | 0 | 0 |
| JPEG XL bare codestream | `jxl.file=codestream` | 36 | 7 | 3 | 5 |
| JPEG XL container (boxes) | `jxl.file=container` | 18 | 0 | 1 | 1 |
| JPEG XL frame that is decoded without being shown | `jxl.frame=hidden` | 3 | 0 | 0 | 1 |
| JPEG XL LF frame | `jxl.frame=lf` | 2 | 0 | 0 | 0 |
| JPEG XL frame smaller than the image | `jxl.frame=partial` | 1 | 0 | 0 | 1 |
| JPEG XL reference-only frame | `jxl.frame=reference` | 1 | 0 | 0 | 0 |
| JPEG XL pixels decoded without being shown (count) | `jxl.hiddenPixels` | 4 | 0 | 0 | 1 |
| JPEG XL finite play count | `jxl.loop=finite` | 1 | 0 | 0 | 0 |
| JPEG XL animation that repeats forever (num_loops 0) | `jxl.loop=infinite` | 4 | 0 | 0 | 2 |
| JPEG XL metadata boxes after the codestream | `jxl.metadataAfterCodestream` | 1 | 0 | 0 | 0 |
| JPEG XL noise | `jxl.noise` | 1 | 0 | 0 | 0 |
| JPEG XL orientation 6 in the image header | `jxl.orientation=6` | 1 | 0 | 0 | 0 |
| JPEG XL several passes | `jxl.passes` | 1 | 0 | 0 | 0 |
| JPEG XL patches | `jxl.patches` | 1 | 0 | 0 | 0 |
| JPEG XL frame of several sections | `jxl.sections=many` | 2 | 0 | 0 | 0 |
| JPEG XL splines | `jxl.splines` | 1 | 0 | 0 | 0 |
| JPEG XL permuted table of contents | `jxl.toc=permuted` | 1 | 0 | 0 | 0 |
| JPEG XL frame upsampled by 2 | `jxl.upsampling=2` | 1 | 0 | 0 | 0 |
| JPEG 2000 12-bit samples | `jp2.bits=12` | 2 | 0 | 0 | 0 |
| JPEG 2000 16-bit samples | `jp2.bits=16` | 7 | 0 | 0 | 0 |
| JPEG 2000 2-bit samples (palette indexes) | `jp2.bits=2` | 1 | 0 | 0 | 0 |
| JPEG 2000 4-bit samples | `jp2.bits=4` | 1 | 0 | 0 | 0 |
| JPEG 2000 8-bit samples | `jp2.bits=8` | 81 | 17 | 11 | 5 |
| JP2 channel definition box (`cdef`) | `jp2.box=cdef` | 4 | 0 | 2 | 0 |
| JP2 component mapping box (`cmap`) | `jp2.box=cmap` | 2 | 0 | 0 | 0 |
| JP2 palette box (`pclr`) | `jp2.box=pclr` | 2 | 0 | 0 | 0 |
| JP2 resolution box (`res `) | `jp2.box=res` | 1 | 0 | 0 | 0 |
| JP2 capture resolution box (`resc`) | `jp2.box=resc` | 1 | 0 | 0 | 0 |
| JP2 UUID box (EXIF, XMP) | `jp2.box=uuid` | 2 | 0 | 0 | 2 |
| JP2 boxes after the codestream | `jp2.boxesAfterCodestream` | 1 | 0 | 1 | 1 |
| JP2 file (brand `jp2 `) | `jp2.brand=jp2` | 17 | 6 | 2 | 2 |
| JPH file (brand `jph `, Rec. ITU-T T.814) | `jp2.brand=jph` | 1 | 0 | 0 | 0 |
| JPX file of several codestreams (rejected) | `jp2.brand=jpx` | 0 | 0 | 1 | 0 |
| Motion JPEG 2000 file (rejected) | `jp2.brand=mjp2` | 0 | 0 | 1 | 0 |
| JPEG 2000 COC marker segment | `jp2.coc` | 1 | 0 | 0 | 0 |
| JP2 enumerated color space | `jp2.color=enumerated` | 16 | 6 | 4 | 2 |
| JP2 ICC profile | `jp2.color=icc` | 2 | 0 | 0 | 0 |
| JP2 grayscale color space | `jp2.colorSpace=gray` | 2 | 0 | 1 | 0 |
| JP2 sRGB color space | `jp2.colorSpace=srgb` | 12 | 6 | 3 | 2 |
| JP2 sYCC color space | `jp2.colorSpace=sycc` | 2 | 0 | 0 | 0 |
| JPEG 2000 multiple component transformation | `jp2.componentTransform` | 48 | 16 | 5 | 5 |
| JPEG 2000 one component | `jp2.components=1` | 34 | 1 | 4 | 0 |
| JPEG 2000 two components | `jp2.components=2` | 3 | 0 | 0 | 0 |
| JPEG 2000 three components | `jp2.components=3` | 50 | 16 | 4 | 5 |
| JPEG 2000 four components | `jp2.components=4` | 5 | 0 | 2 | 0 |
| JPEG 2000 five components without color specification (rejected) | `jp2.components=5` | 0 | 0 | 1 | 0 |
| JPEG 2000 EPH markers | `jp2.eph` | 3 | 0 | 0 | 0 |
| JPEG 2000 bare codestream | `jp2.file=codestream` | 74 | 11 | 7 | 3 |
| JPEG 2000 file (boxes) | `jp2.file=jp2` | 18 | 6 | 4 | 2 |
| JPEG 2000 high-throughput block coder | `jp2.ht` | 19 | 1 | 3 | 0 |
| JPEG 2000 HT MagRef pass | `jp2.ht.magRef` | 5 | 0 | 0 | 0 |
| JPEG 2000 placeholder passes | `jp2.ht.placeholders` | 2 | 0 | 0 | 0 |
| JPEG 2000 HT SigProp pass | `jp2.ht.sigProp` | 6 | 0 | 0 | 0 |
| JPEG 2000 several layers | `jp2.layers` | 15 | 0 | 2 | 0 |
| JPEG 2000 no decomposition level | `jp2.levels=0` | 16 | 0 | 2 | 0 |
| JPEG 2000 one decomposition level | `jp2.levels=1` | 14 | 0 | 3 | 0 |
| JPEG 2000 two decomposition levels | `jp2.levels=2` | 52 | 16 | 6 | 5 |
| JPEG 2000 three decomposition levels | `jp2.levels=3` | 9 | 1 | 0 | 0 |
| JPEG 2000 six decomposition levels | `jp2.levels=6` | 1 | 0 | 0 | 0 |
| JP2 metadata boxes after the codestream | `jp2.metadataAfterCodestream` | 1 | 0 | 0 | 1 |
| JPEG 2000 image or tile grid offset | `jp2.offsets` | 1 | 0 | 0 | 0 |
| JP2 opacity channel (channel definition) | `jp2.opacity` | 3 | 0 | 1 | 0 |
| JP2 palette | `jp2.palette` | 2 | 0 | 0 | 0 |
| JPEG 2000 packet lengths (PLT) | `jp2.plt` | 1 | 0 | 0 | 0 |
| JPEG 2000 progression order changes (POC) | `jp2.poc` | 1 | 0 | 0 | 0 |
| JPEG 2000 packet headers in the main header (PPM) | `jp2.ppm` | 1 | 0 | 0 | 0 |
| JPEG 2000 packet headers in tile-part headers (PPT) | `jp2.ppt` | 1 | 0 | 0 | 0 |
| JPEG 2000 precinct sizes | `jp2.precincts` | 6 | 0 | 0 | 0 |
| JPEG 2000 CPRL progression | `jp2.progression=cprl` | 3 | 0 | 0 | 0 |
| JPEG 2000 LRCP progression | `jp2.progression=lrcp` | 67 | 16 | 8 | 5 |
| JPEG 2000 PCRL progression | `jp2.progression=pcrl` | 1 | 0 | 0 | 0 |
| JPEG 2000 RLCP progression | `jp2.progression=rlcp` | 1 | 0 | 0 | 0 |
| JPEG 2000 RPCL progression | `jp2.progression=rpcl` | 20 | 1 | 3 | 0 |
| JPEG 2000 QCC marker segment | `jp2.qcc` | 1 | 0 | 0 | 0 |
| JPEG 2000 scalar expounded quantization | `jp2.quantization=expounded` | 11 | 0 | 0 | 0 |
| JPEG 2000 no quantization | `jp2.quantization=none` | 81 | 17 | 11 | 5 |
| JPEG 2000 region of interest (RGN) | `jp2.roi` | 2 | 0 | 0 | 0 |
| JPEG 2000 SOP marker segments | `jp2.sop` | 3 | 0 | 0 | 0 |
| JPEG 2000 selective arithmetic coding bypass | `jp2.style=bypass` | 2 | 0 | 0 | 0 |
| JPEG 2000 vertically causal contexts | `jp2.style=causal` | 3 | 0 | 0 | 0 |
| JPEG 2000 predictable termination | `jp2.style=pterm` | 2 | 0 | 0 | 0 |
| JPEG 2000 reset of the context probabilities | `jp2.style=reset` | 2 | 0 | 0 | 0 |
| JPEG 2000 segmentation symbols | `jp2.style=segsym` | 2 | 0 | 0 | 0 |
| JPEG 2000 termination on each coding pass | `jp2.style=termall` | 2 | 0 | 0 | 0 |
| JPEG 2000 sub-sampled components | `jp2.subsampling` | 1 | 0 | 1 | 0 |
| JPEG 2000 coding style in a tile-part header | `jp2.tileCodingStyle` | 1 | 0 | 0 | 0 |
| JPEG 2000 several tile-parts per tile | `jp2.tileParts` | 5 | 0 | 0 | 0 |
| JPEG 2000 several tiles | `jp2.tiles` | 13 | 0 | 0 | 0 |
| JPEG 2000 tile-part lengths (TLM) | `jp2.tlm` | 1 | 0 | 0 | 0 |
| JPEG 2000 reversible 5-3 transformation | `jp2.transform=5-3` | 81 | 17 | 11 | 5 |
| JPEG 2000 irreversible 9-7 transformation | `jp2.transform=9-7` | 11 | 0 | 0 | 0 |
| OpenEXR NONE compression | `exr.compression=none` | 4 | 0 | 0 | 0 |
| OpenEXR RLE compression | `exr.compression=rle` | 4 | 0 | 0 | 0 |
| OpenEXR ZIPS compression | `exr.compression=zips` | 3 | 0 | 0 | 0 |
| OpenEXR ZIP compression | `exr.compression=zip` | 10 | 0 | 0 | 0 |
| OpenEXR PIZ compression | `exr.compression=piz` | 11 | 0 | 0 | 0 |
| OpenEXR PXR24 compression | `exr.compression=pxr24` | 4 | 0 | 0 | 0 |
| OpenEXR B44 compression | `exr.compression=b44` | 4 | 0 | 0 | 0 |
| OpenEXR B44A compression | `exr.compression=b44a` | 3 | 0 | 0 | 0 |
| OpenEXR scan lines | `exr.storage=scanline` | 37 | 0 | 0 | 0 |
| OpenEXR tiles | `exr.storage=tiled` | 6 | 0 | 0 | 0 |
| OpenEXR one level of tiles | `exr.levels=one-level` | 3 | 0 | 0 | 0 |
| OpenEXR mipmap levels | `exr.levels=mipmap` | 2 | 0 | 0 | 0 |
| OpenEXR ripmap levels | `exr.levels=ripmap` | 1 | 0 | 0 | 0 |
| OpenEXR HALF samples | `exr.type=half` | 29 | 0 | 0 | 0 |
| OpenEXR FLOAT samples | `exr.type=float` | 14 | 0 | 0 | 0 |
| OpenEXR UINT samples | `exr.type=uint` | 2 | 0 | 0 | 0 |
| OpenEXR channel sets (R, G, B, A, Y) | `exr.channels` | 43 | 0 | 0 | 0 |
| OpenEXR decreasing line order | `exr.lineOrder=decreasing-y` | 1 | 0 | 0 | 0 |
| OpenEXR tiles in random order | `exr.lineOrder=random-y` | 1 | 0 | 0 | 0 |
| OpenEXR data window inside the display window | `exr.dataWindow=inside` | 1 | 0 | 0 | 0 |
| OpenEXR negative coordinates | `exr.negativeCoordinates` | 1 | 0 | 0 | 0 |
| OpenEXR chromaticities | `exr.chromaticities` | 3 | 0 | 0 | 0 |
| OpenEXR other attributes | `exr.otherAttributes` | 5 | 0 | 0 | 0 |
| OpenEXR long names | `exr.longNames` | 1 | 0 | 0 | 0 |
| OpenEXR sample images of the OpenEXR project | `exr.external` | 4 | 0 | 0 | 0 |

Not covered by fixtures, and verified elsewhere: encoder outputs (independent readers, FFmpeg and Apple ImageIO), malformed-input robustness beyond the error
fixtures (`FuzzTests/MutationFuzzTests`, `Tests.Shared/Suites/Decode/TruncationTests`), and limits at every boundary (`Tests.Shared/Suites/Decode/ResourceLimitBoundaryTests`).

## AV1 conformance vectors

AV1 decoding is normative: every conforming decoder produces the same samples, film grain synthesis included. The AV1
decoder is therefore tested below the AVIF container and the color conversion, on raw OBU streams (`av1/*.obu`, the
"low overhead bitstream format" of the AV1 specification) whose expected output is the **SHA-256 of every plane of every
output frame**. There are intra vectors (one or several key frames, up to 512x256) and inter vectors (2 to 30 output
frames, up to 352x288: inter, intra-only and switch frames, hidden frames, frames shown again, frame sizes that change
inside the stream) and layered vectors (2 to 4 spatial layers in a temporal unit: [below](#layered-vectors)). Most streams
are what an encoder wrote; a few have a frame header patched or inter frames written by the generator, for the coding
tools no encoder produces ([below](#vectors-an-encoder-did-not-write-alone)). The vectors are in the `av1Vectors` section
of `manifest.json`, apart from `fixtures`:

```json
"av1Vectors": {
  "generator": { "script": "tools/Meziantou.Framework.Imaging.CorpusGenerator/AvifCorpus.cs", "command": "...", "tools": ["ffmpeg 9.0.2", "libaom 3.15.1", "SVT-AV1 4.2.0", "libdav1d 1.5.4", "libavif 1.4.2 avifenc (aom 3.14.1)", ".NET 10.0"] },
  "headerParser": { "name": "ffmpeg 9.0.2 (trace_headers bitstream filter)", "command": "ffmpeg ... -bsf:v trace_headers ..." },
  "decoders": [{ "name": "libdav1d 1.5.4", "command": "ffmpeg ... -c:v libdav1d ..." }, { "name": "libaom 3.15.1", "command": "ffmpeg ... -c:v libaom-av1 ..." }],
  "planeDigest": "SHA-256 of the samples of one plane ...",
  "entries": [{
    "id": "av1/lossless-gbr-8bit-34x36",
    "input": { "path": "av1/lossless-gbr-8bit-34x36.obu", "sha256": "...", "role": "encoded" },
    "provenance": { "origin": "generated", "license": "CC0-1.0", "generator": "...", "tools": ["ffmpeg 9.0.2", "libaom 3.15.1"], "commands": ["ffmpeg ... -c:v libaom-av1 ... -f obu {input}"] },
    "features": ["av1.bitDepth=8", "av1.lossless", "av1.matrix=identity", "av1.subsampling=4:4:4", "..."],
    "pixelFormat": "gbrp", "bitDepth": 8, "subsampling": "4:4:4", "frameCount": 1,
    "frames": [{ "width": 34, "height": 36, "planes": ["<sha256 of Y>", "<sha256 of U>", "<sha256 of V>"] }]
  }]
}
```

- **Inputs.** Deterministic FFmpeg `lavfi` sources (`testsrc2`, `testsrc`, `mandelbrot`, `smptebars`, seeded `noise`;
  for the inter vectors, `testsrc2` at 10 frames per second, as is or rotating around its center with `rotate`, so that
  the motion is not a translation) encoded by libaom (`libaom-av1`) and SVT-AV1 (`libsvtav1`), single-threaded
  (`-threads 1`, `-row-mt 0`; `lp=1` for the SVT-AV1 key-frame vectors and `lp=2` for its inter vectors: with `lp=1`
  this build writes an empty stream as soon as there are inter frames, and without `lp` the stream with a hidden switch
  frame came out in two forms, one run out of three) and bit-exact (`-flags +bitexact`), written as raw OBU streams (`-f obu`). The
  exact command of each vector is in its `provenance.commands`, and the check mode of the generator proves that every
  stream is reproduced byte for byte. Every tool comes from the FFmpeg build of the meziantou/prebuilt release pinned by
  the `Meziantou.Prebuilt` package (the binaries of the interop tests); the versions of FFmpeg, libaom, SVT-AV1 and dav1d
  are pinned in `AvifCorpus.PinnedTools`.
- **Output frames.** `frames` lists what a decoder outputs, in output order: one frame per shown frame and per
  `show_existing_frame`; a hidden frame (`show_frame = 0`) produces no output, and a frame shown again is output again.
  When the operating point has several spatial layers, a temporal unit outputs one frame, the shown frame of its highest
  layer ([layered vectors](#layered-vectors)).
  Each frame has its own `width` and `height`: with reference scaling they change inside a stream, and they are then
  smaller than the maximum of the sequence header. The generator computes the size of every output frame from the
  headers alone, following the reference slots (`AvifCorpus.ResolveFrameSizes`), and fails when `ffprobe` reports another
  size for a frame of either decoder or when a decoded frame does not have exactly the bytes of that size.
- **Plane digests.** Each stream is decoded by two independent AV1 decoders, dav1d (`-c:v libdav1d`) and libaom
  (`-c:v libaom-av1`), to raw planes in the native pixel format of the stream (`pixelFormat`; `ffprobe` must report that
  format for every frame of both decoders, so no swscale conversion takes part), one raw file per output frame and
  without scaling (`-noautoscale -f image2 -c:v rawvideo`), so that a frame is never resampled to the size of the first
  one. **A digest is recorded only when both decoders produce the same bytes for every plane of every output frame, in
  output order**; when they disagree or when either of them rejects the stream, the generation fails for that vector:
  a digest from a single decoder is never committed. Film grain is applied by both decoders (their default), and the
  generator checks that decoding without it gives other samples.
- **Digest layout** (`planeDigest`): the samples of one plane, rows in order, exactly plane-width samples per row,
  1 byte per sample for 8-bit streams and 2 little-endian bytes for 10 and 12-bit streams, no padding. Planes are in
  bitstream order Y, U, V; for an identity-matrix stream (`av1.matrix=identity`) that is FFmpeg's planar GBR order
  G, B, R. A monochrome stream (`4:0:0`) has one plane. `width` and `height` are those of the luma plane after
  super-resolution upscaling; a chroma plane is `(width + subsampling_x) >> subsampling_x` samples wide and
  `(height + subsampling_y) >> subsampling_y` rows high.
- **Features** are what the sequence header and the frame headers signal, as parsed by a third implementation, FFmpeg's
  `trace_headers` bitstream filter: never the encoder options, which do not guarantee that a tool is used. A value of a
  multi-frame vector lists the distinct values of its frames (hidden frames included; a `show_existing_frame` header
  codes nothing else). The tags of the inter tools (`av1.reference_select`, `av1.interpolation_filter`,
  `av1.primary_ref_frame`, `av1.gm`...) are those of the inter and switch frames; `av1.frame_types` is absent from a
  stream made of key frames. Each recipe declares the tags it exists for (`Av1Vector.Requires`), and the generation
  fails when the encoder did not produce them.
- **Coding tools.** The headers do not tell which tools the blocks use (palette, chroma from luma, intra block copy,
  compound prediction types, overlapped and warped motion compensation, scaled references, 64-point transforms...).
  `Av1.Tests/Conformance/Av1ToolCoverageTests` decodes every vector and requires that, together, they reach every tool the
  decoder reports (`Av1Tools`), without exception. The four tools that neither encoder produces (the bilinear
  interpolation filter, an interpolation filter per direction, a predicted segment id and the reference-frame
  segmentation feature) are each required on the vectors made for them
  ([below](#vectors-an-encoder-did-not-write-alone)), and may be reached by no other vector.
- **Intra and inter vectors.** An intra vector is made of shown key frames and must not signal `allow_intrabc` (libaom
  turns it on by itself for screen content) unless it requires that tag: the two `intrabc-*` vectors. An inter vector
  (`Av1Vector.Inter`) must have a frame that is not a shown key frame. There is no size limit ([Size](#size)): the
  largest inputs are the two 512x256 loop restoration vectors (25 and 37 KB).
- **Tests.** `Av1.Tests/Conformance/Av1DecoderConformanceTests` decodes every vector with the library and compares the digest
  of each plane of each output frame, then decodes it again from a pool whose buffers are all filled with a non-zero
  pattern (`Hardening/DirtySlabPool`): a read of uninitialized state changes the digests. `CorpusManifestTests`
  validates the section (files, hashes, licenses, two decoders, orphan files), the feature coverage below, and what the
  provenance of a patched or hand-authored vector claims (the bits that differ from the source vector; the key frame
  stream the vector starts with).
  The `verify` subcommand of the generator checks the hash of every vector and re-derives the digests with both decoders.

### Vectors an encoder did not write alone

Four coding tools are produced by neither libaom nor SVT-AV1, whatever their options. They are covered by vectors that
the generator completes itself, and that are recorded like any other vector: headers and feature tags read back from the
final stream by `trace_headers`, planes decoded by dav1d and by libaom, digests recorded only when both agree.

- **Header patches** (`av1.patched_header`, `AvifCorpus.Patched`): the stream of another vector whose inter frame headers
  code `is_filter_switchable = 0` and the 2-bit `interpolation_filter` element, with these 2 bits overwritten in every
  such header: 3 (`BILINEAR`) or 1 (`EIGHTTAP_SMOOTH`), the two frame-level filters no encoder selects. The position of
  the element in its OBU is the one `trace_headers` reports, and the bits found there must be the value it read. No
  length changes and no block codes a filter, so the result is a valid stream that decodes to other pictures (the
  encoder would not have chosen these residuals for them, which does not matter to a decoder). `provenance.parameters`
  records the source vector (`patchOf`), the SHA-256 of its stream (`sourceSha256`), the patch and the offset of every
  patched element (`patchedBitOffsets`); `provenance.commands` is the encoder command of the source.
- **Hand-authored inter frames** (`av1.hand_authored`, `AvifCorpus.HandAuthored` and `Av1HandAuthoredStream.cs`): one
  key frame encoded by libaom (128x128, 64x64 superblocks, a full sequence header), followed by inter frames written bit
  by bit from the specification: the uncompressed header, the tile group and the symbols of the blocks, coded by an
  arithmetic encoder that mirrors the symbol decoder of section 8.2. The frames are kept to what can be written without
  a mode decision or a context model (2x2 tiles of one skipped 64x64 block, `disable_cdf_update = 1`), but every syntax
  element of the tools is coded and shows in the pixels. Two recipes, each at 4:2:0 8 and 10-bit, 4:4:4 10-bit, 4:2:2
  12-bit and monochrome 8-bit: `dual-filters` (4 frames: `enable_dual_filter` set in the sequence header, `interp_filter`
  coded per direction with every pair of filters, a last frame with the frame-level `BILINEAR` filter) and
  `segment-features` (8 frames: a segment map with the reference frame feature, a map predicted from the previous frame
  with `seg_id_predicted` 1 and 0, a map that is not updated, the skip and global motion features).
  `provenance.parameters` records the recipe (`handAuthoredRecipe`, `description`) and the key frame stream the frames
  are appended to (`keyFrameSha256`, `keyFrameLength`); `provenance.commands` is the encoder command of the key frame.
  The tags that `trace_headers` exposes are read back from the final stream like every other tag
  (`av1.enable_dual_filter`, `av1.interpolation_filter=bilinear,switchable`, `av1.segmentation.features=5,6,7`: the
  reference frame, skip and global motion features, `av1.segmentation.temporal_update`, `av1.segmentation.update_map=0,1`,
  `av1.disable_cdf_update`, `av1.tiles=2x2`). What the blocks code is not in a header and has no tag: the filter of each
  direction, `seg_id_predicted`, the reference a segment imposes. `Av1ToolCoverageTests` requires these tools by name on
  these vectors (`DualInterpolationFilter` and `BilinearInterpolationFilter`; `PredictedSegmentId`,
  `SegmentReferenceFrame`, `IntraBlockInInterFrame` and `GlobalMotionVector`), as the decoder reports them.

`av1.patched_header` and `av1.hand_authored` are the only two tags that do not come from the headers.

### Layered vectors

Spatial layers (AV1 specification, sections 6.2.3 and 6.4.1) are what a layered (progressive) AVIF image is made of: one
temporal unit holds a shown frame per spatial layer, each one predicting from the layer below, and the sequence header
declares the operating points a decoder may select. No encoder option of the pinned FFmpeg codes them; libavif does,
with libaom. The layered vectors (`AvifCorpus.Layered.cs`) are therefore **the payload of the coded item of a file
written by `avifenc`** from a hand-defined pattern (`--layered`: one input per layer with its quality and its scale;
`--progressive`: two layers, the first one at half the size), extracted by `ReferenceHeif`. The payload is a temporal
delimiter (libavif keeps it, so the FFmpeg `obu` demuxer needs nothing more), a sequence header and one frame per layer.
`provenance.commands` is the `avifenc` command of each temporal unit and `provenance.parameters.files` the SHA-256 of the
file it wrote; everything else is recorded like any vector, from the stream: headers and tags by `trace_headers`,
digests only when both decoders agree.

- **What the decoders output.** dav1d and libaom, through FFmpeg and by default, output **one frame per temporal unit:
  the shown frame of the highest spatial layer the unit holds** (section 7.18.1 "Output process"; dav1d: all layers off,
  libaom: output all layers off). `frames` records that frame, and `frameCount` the number of temporal units. The
  generator also asks dav1d for every layer (`-alllayers 1`) and checks that it outputs one frame per shown frame and
  that the last frame of each temporal unit is the recorded one: this is what a test relies on when it asks the library
  for every layer (`Av1Decoder.OutputAllLayers`).
- **Tags** (from the headers, like every tag): `av1.operating_points=N` (the operating points of the sequence header),
  `av1.operating_point_idc=0xNNN` (the first operating point, which a decoder selects by default: spatial layers in bits
  8 to 11), `av1.spatial_layers=N` (the layers of that operating point), `av1.spatial_layer_sizes=WxH,...` (the size of
  the shown frame of each layer, when they differ: a smaller layer is coded with `frame_size_override_flag` and the next
  one predicts from it with reference scaling) and `av1.spatial_layers.present=N` (the layers the stream holds, when it
  does not hold every layer of the operating point). avifenc declares one operating point per number of layers
  (`0x701`, `0x301`, `0x101` for three layers). A stream without scalability information has none of these tags.
- **Lower layers.** The frames of the lower layers of a layered vector have no digest of their own: FFmpeg gives no
  access to them for libaom. They have one in the *truncated* vectors (`...-first-layer`, `...-first-N-layers`;
  `provenance.parameters.truncationOf` and `keptSpatialLayers`): the same payload without the units of its higher layers
  (section 5.3.1: nothing in a layer refers to a higher one). Such a stream still declares every layer, and both decoders
  output the frame of the highest layer they find, which is the frame of that layer in the whole stream: the generator
  checks that it is what dav1d outputs for that layer of the whole stream when it is asked for every layer.
  `CorpusManifestTests` checks that a truncated vector is its source without those units and nothing else.
- **Tests.** Every layered vector goes through the tests of every vector (the default output). In addition,
  `Av1DecoderConformanceTests` decodes it with `OutputAllLayers` (one frame per layer and per temporal unit, in order,
  with the layer and the size the tags give, the last one with the recorded digests), compares the frame of each lower
  layer (`OutputAllLayers`, and `MaxSpatialLayer` with and without it) with the digests of the truncated vector of that
  layer, and decodes two layered temporal units without the temporal delimiter between them and one input at a time.

### AV1 vector features

The table maps the decoding tools to the vectors that exercise them. It is **verified** like the
[fixture coverage matrix](#fixture-coverage-matrix), with the same selector syntax: `FixtureCoverageMatrixTests` recomputes
every count from the `av1Vectors` section and fails when the table is stale, when a row has no vector, or when a feature
key recorded in the section is not mapped by any row.

| Feature | Selector | Vectors |
| --- | --- | --- |
| Main profile | `av1.profile=0` | 144 |
| High profile | `av1.profile=1` | 32 |
| Professional profile | `av1.profile=2` | 45 |
| Monochrome (4:0:0), 8-bit | `av1.subsampling=4:0:0` + `av1.bitDepth=8` | 9 |
| Monochrome (4:0:0), 10-bit | `av1.subsampling=4:0:0` + `av1.bitDepth=10` | 4 |
| Monochrome (4:0:0), 12-bit | `av1.subsampling=4:0:0` + `av1.bitDepth=12` | 6 |
| 4:2:0, 8-bit | `av1.subsampling=4:2:0` + `av1.bitDepth=8` | 110 |
| 4:2:0, 10-bit | `av1.subsampling=4:2:0` + `av1.bitDepth=10` | 21 |
| 4:2:0, 12-bit | `av1.subsampling=4:2:0` + `av1.bitDepth=12` | 10 |
| 4:2:2, 8-bit | `av1.subsampling=4:2:2` + `av1.bitDepth=8` | 9 |
| 4:2:2, 10-bit | `av1.subsampling=4:2:2` + `av1.bitDepth=10` | 6 |
| 4:2:2, 12-bit | `av1.subsampling=4:2:2` + `av1.bitDepth=12` | 7 |
| 4:4:4, 8-bit | `av1.subsampling=4:4:4` + `av1.bitDepth=8` | 15 |
| 4:4:4, 10-bit | `av1.subsampling=4:4:4` + `av1.bitDepth=10` | 17 |
| 4:4:4, 12-bit | `av1.subsampling=4:4:4` + `av1.bitDepth=12` | 7 |
| Identity matrix (planar GBR) | `av1.matrix=identity` | 4 |
| Full-range samples | `av1.color_range=full` | 38 |
| Lossless (Walsh-Hadamard transform) | `av1.lossless` | 13 |
| Lossless, identity matrix | `av1.lossless` + `av1.matrix=identity` | 3 |
| Quantizer index (every vector) | `av1.base_q_idx` | 221 |
| Highest quantizer index | `av1.base_q_idx=255` | 1 |
| Still picture | `av1.still_picture` | 75 |
| Reduced still picture header | `av1.reduced_still_picture_header` | 75 |
| Operating points in the sequence header | `av1.operating_points` | 22 |
| First operating point with several spatial layers | `av1.operating_point_idc` | 22 |
| 2 spatial layers | `av1.spatial_layers=2` | 11 |
| 3 spatial layers | `av1.spatial_layers=3` | 6 |
| 4 spatial layers | `av1.spatial_layers=4` | 5 |
| Spatial layers of growing sizes (reference scaling between layers) | `av1.spatial_layer_sizes` | 5 |
| Stream without its highest layers (truncated layered vector) | `av1.spatial_layers.present` | 9 |
| Stream that only holds its first layer | `av1.spatial_layers.present=1` | 5 |
| Spatial layers, monochrome | `av1.spatial_layers` + `av1.subsampling=4:0:0` | 2 |
| Spatial layers, 4:2:2 | `av1.spatial_layers` + `av1.subsampling=4:2:2` | 2 |
| Spatial layers, 4:4:4 | `av1.spatial_layers` + `av1.subsampling=4:4:4` | 8 |
| Spatial layers, identity matrix | `av1.spatial_layers` + `av1.matrix=identity` | 1 |
| Spatial layers, 10-bit | `av1.spatial_layers` + `av1.bitDepth=10` | 7 |
| Spatial layers, 12-bit | `av1.spatial_layers` + `av1.bitDepth=12` | 5 |
| Spatial layers in several temporal units | `av1.spatial_layers` + `av1.frames` | 1 |
| Several output frames | `av1.frames` | 97 |
| Frame types other than key frames | `av1.frame_types` | 111 |
| Inter frames | `av1.frame_types=inter` | 111 |
| Intra-only frames | `av1.frame_types=intra_only` | 1 |
| Switch frames | `av1.frame_types=switch` | 2 |
| Hidden frames (no output) | `av1.hidden_frames` | 69 |
| Frames shown again (`show_existing_frame`) | `av1.show_existing_frame` | 59 |
| Hidden switch frame | `av1.frame_types=switch` + `av1.hidden_frames` | 1 |
| Inter frames, monochrome | `av1.frame_types=inter` + `av1.subsampling=4:0:0` | 9 |
| Inter frames, 4:2:2 | `av1.frame_types=inter` + `av1.subsampling=4:2:2` | 10 |
| Inter frames, 4:4:4 | `av1.frame_types=inter` + `av1.subsampling=4:4:4` | 16 |
| Inter frames, 10-bit | `av1.frame_types=inter` + `av1.bitDepth=10` | 23 |
| Inter frames, 12-bit | `av1.frame_types=inter` + `av1.bitDepth=12` | 16 |
| Inter frames, lossless | `av1.frame_types=inter` + `av1.lossless` | 1 |
| Inter frames, 128x128 superblocks | `av1.frame_types=inter` + `av1.superblock=128` | 3 |
| Inter frames, several tiles | `av1.frame_types=inter` + `av1.tiles` | 13 |
| Inter frames, quantizer matrices | `av1.frame_types=inter` + `av1.using_qmatrix` | 17 |
| Inter frames, screen content tools | `av1.frame_types=inter` + `av1.allow_screen_content_tools` | 64 |
| Inter frames, CDEF | `av1.frame_types=inter` + `av1.cdef_bits` | 108 |
| Inter frames, loop restoration | `av1.frame_types=inter` + `av1.lr.y` | 74 |
| Inter frames, super-resolution | `av1.frame_types=inter` + `av1.superres.denominator` | 4 |
| Inter frames, film grain | `av1.frame_types=inter` + `av1.film_grain` | 12 |
| Inter frames, film grain parameters of a reference frame (`update_grain = 0`) | `av1.film_grain.from_reference` | 1 |
| Inter frames, luma DC quantizer delta | `av1.frame_types=inter` + `av1.delta_q.y_dc` | 1 |
| Inter frames, reference scaling at 10 bits | `av1.frame_sizes` + `av1.bitDepth=10` | 1 |
| Inter frames, super-resolution and reference scaling | `av1.superres.denominator` + `av1.frame_sizes` | 1 |
| 17 output frames (a whole group of pictures) | `av1.frames=17` | 4 |
| 30 output frames | `av1.frames=30` | 1 |
| Film grain on a frame shown again | `av1.show_existing_frame` + `av1.film_grain` | 10 |
| No primary reference frame | `av1.primary_ref_frame=none` | 79 |
| Primary reference frame | `av1.primary_ref_frame=set` | 90 |
| Error-resilient mode | `av1.error_resilient_mode` | 1 |
| Frame id numbers | `av1.frame_id_numbers_present` | 1 |
| CDF update at the end of the frame disabled | `av1.disable_frame_end_update_cdf` | 1 |
| Compound references (`reference_select`) | `av1.reference_select` | 74 |
| Skip mode | `av1.skip_mode_present` | 67 |
| Motion vectors projected from the reference frames | `av1.use_ref_frame_mvs` | 98 |
| Warped motion allowed | `av1.allow_warped_motion` | 81 |
| Switchable motion mode | `av1.is_motion_mode_switchable` | 100 |
| Integer motion vectors | `av1.force_integer_mv` | 2 |
| Global motion, rotation and zoom | `av1.gm=rotzoom` | 24 |
| Interpolation filter, regular | `av1.interpolation_filter=eighttap` | 60 |
| Interpolation filter, sharp | `av1.interpolation_filter=sharp` | 6 |
| Interpolation filter, smooth (header patch or hand-authored) | `av1.interpolation_filter=smooth` | 7 |
| Interpolation filter, bilinear (header patch or hand-authored) | `av1.interpolation_filter=bilinear` | 11 |
| Vectors made by a header patch | `av1.patched_header` | 8 |
| Vectors with hand-authored inter frames | `av1.hand_authored` | 10 |
| `enable_dual_filter` in the sequence header | `av1.enable_dual_filter` | 12 |
| Interpolation filter per direction, coded by the blocks (hand-authored) | `av1.enable_dual_filter` + `av1.interpolation_filter=switchable` + `av1.hand_authored` | 5 |
| Interpolation filter, selected per block | `av1.interpolation_filter=switchable` | 52 |
| Explicit frame size (`frame_size_override_flag`) | `av1.frame_size_override_flag` | 17 |
| Frame size of a reference frame (`found_ref`) | `av1.found_ref` | 9 |
| Frame size changing inside the stream (reference scaling) | `av1.frame_sizes` | 8 |
| Render size different from the frame size | `av1.render_and_frame_size_different` | 15 |
| Intra block copy allowed | `av1.allow_intrabc` | 29 |
| Intra block copy allowed, 4:4:4 | `av1.allow_intrabc` + `av1.subsampling=4:4:4` | 2 |
| Intra block copy allowed, 10-bit | `av1.allow_intrabc` + `av1.bitDepth=10` | 6 |
| Intra block copy allowed, 12-bit | `av1.allow_intrabc` + `av1.bitDepth=12` | 1 |
| Intra block copy allowed, stream with inter frames | `av1.allow_intrabc` + `av1.frame_types=inter` | 24 |
| Segmentation map kept from the primary reference frame | `av1.segmentation.update_map=0` | 6 |
| Segmentation map coded in a frame that has a primary reference frame | `av1.segmentation.update_map=1` | 7 |
| Segmentation map with temporal prediction | `av1.segmentation.temporal_update` | 6 |
| Segmentation map with temporal prediction and predicted segment ids (hand-authored) | `av1.segmentation.temporal_update` + `av1.hand_authored` | 5 |
| 64x64 superblocks | `av1.superblock=64` | 213 |
| 128x128 superblocks | `av1.superblock=128` | 8 |
| Several tiles | `av1.tiles` | 19 |
| 2x2 tiles | `av1.tiles=2x2` | 16 |
| 4x1 tiles | `av1.tiles=4x1` | 2 |
| 1x2 tiles, 128x128 superblocks | `av1.tiles=1x2` + `av1.superblock=128` | 1 |
| Uniform tile spacing | `av1.tiles.spacing=uniform` | 18 |
| Explicit tile sizes | `av1.tiles.spacing=explicit` | 1 |
| Several tile groups | `av1.tileGroups` | 2 |
| Quantizer matrices | `av1.using_qmatrix` | 26 |
| Chroma quantizer deltas | `av1.delta_q.uv` | 21 |
| Luma DC quantizer delta | `av1.delta_q.y_dc` | 2 |
| Block-level quantizer deltas | `av1.delta_q_present` | 55 |
| Block-level loop filter deltas | `av1.delta_lf_present` | 3 |
| Segmentation | `av1.segmentation_enabled` | 11 |
| Segmentation feature: quantizer | `av1.segmentation.features=0` | 6 |
| Segmentation feature: reference frame (hand-authored) | `av1.segmentation.features=5` | 5 |
| Segmentation feature: skip (hand-authored) | `av1.segmentation.features=6` | 5 |
| Segmentation feature: global motion (hand-authored) | `av1.segmentation.features=7` | 5 |
| Screen content tools (palette) | `av1.allow_screen_content_tools` | 80 |
| Filter intra disabled | `av1.enable_filter_intra=0` | 34 |
| Intra edge filter disabled | `av1.enable_intra_edge_filter=0` | 4 |
| CDF update disabled | `av1.disable_cdf_update` | 11 |
| Reduced transform set | `av1.reduced_tx_set` | 2 |
| Largest transform size only | `av1.tx_mode=largest` | 107 |
| Transform size selection | `av1.tx_mode=select` | 195 |
| Deblocking filter | `av1.loop_filter` | 165 |
| Deblocking filter sharpness | `av1.loop_filter_sharpness` | 16 |
| CDEF | `av1.cdef_bits` | 141 |
| CDEF, 1 strength preset | `av1.cdef_bits=0` | 125 |
| CDEF, 2 strength presets | `av1.cdef_bits=1` | 23 |
| CDEF, 4 strength presets | `av1.cdef_bits=2` | 3 |
| Loop restoration, luma Wiener | `av1.lr.y=wiener` | 84 |
| Loop restoration, luma self-guided | `av1.lr.y=sgrproj` | 33 |
| Loop restoration, luma switchable | `av1.lr.y=switchable` | 2 |
| Loop restoration, U switchable | `av1.lr.u=switchable` | 1 |
| Loop restoration, switchable in every plane | `av1.lr.y=switchable` + `av1.lr.u=switchable` + `av1.lr.v=switchable` | 1 |
| Loop restoration, U Wiener | `av1.lr.u=wiener` | 49 |
| Loop restoration, U self-guided | `av1.lr.u=sgrproj` | 5 |
| Loop restoration, V Wiener | `av1.lr.v=wiener` | 54 |
| Loop restoration, V self-guided | `av1.lr.v=sgrproj` | 6 |
| Loop restoration, V switchable | `av1.lr.v=switchable` | 4 |
| Loop restoration, 64-sample units | `av1.lr.unitSize=64` | 1 |
| Loop restoration, 128-sample units | `av1.lr.unitSize=128` | 10 |
| Loop restoration, 256-sample units | `av1.lr.unitSize=256` | 98 |
| Super-resolution | `av1.superres.denominator` | 9 |
| Super-resolution, smallest denominator | `av1.superres.denominator=9` | 2 |
| Super-resolution, largest denominator | `av1.superres.denominator=16` | 1 |
| Super-resolution with loop restoration | `av1.superres.denominator` + `av1.lr.y` | 8 |
| Film grain | `av1.film_grain` | 30 |
| Film grain, scaling points | `av1.film_grain.points` | 30 |
| Film grain, auto-regression lag 2 | `av1.film_grain.ar_coeff_lag=2` | 13 |
| Film grain, auto-regression lag 3 | `av1.film_grain.ar_coeff_lag=3` | 17 |
| Film grain, chroma scaling from luma | `av1.film_grain.chroma_scaling_from_luma` | 2 |
| Film grain, overlap between blocks | `av1.film_grain.overlap` | 26 |
| Film grain, clipping to the restricted range | `av1.film_grain.clip_to_restricted_range` | 9 |
| Film grain, monochrome | `av1.film_grain` + `av1.subsampling=4:0:0` | 3 |
| Film grain, 4:2:2 | `av1.film_grain` + `av1.subsampling=4:2:2` | 5 |
| Film grain, 4:4:4 | `av1.film_grain` + `av1.subsampling=4:4:4` | 6 |
| Film grain, 10-bit | `av1.film_grain` + `av1.bitDepth=10` | 9 |
| Film grain, 12-bit | `av1.film_grain` + `av1.bitDepth=12` | 7 |

Not obtained from either encoder, and not covered by a vector: `delta_lf_multi = 1` (libaom rejects `delta-lf-mode=2`
and never codes it; SVT-AV1 has no block-level loop filter delta), global motion other than rotation and zoom
(translation, affine), and `frame_refs_short_signaling`. The segmentation features other than the quantizer, the
interpolation filter per direction and the predicted segment ids only exist in the hand-authored vectors, the bilinear
and smooth frame-level filters in the patched and hand-authored ones.

What the earlier size limits (8 KB per vector, 176x144, 12 frames) had left out, and what lifted them:

- loop restoration is only switchable within a plane that has several restoration units, and both encoders pick
  256-sample units for small frames: the vectors with switchable restoration in the U plane, in every plane at once, and
  with 64-sample units are 512x256 frames whose halves differ (libaom, speed 1; 5 to 37 KB);
- whole groups of pictures: 30 frames from libaom and 17 from SVT-AV1 at 176x144, where every reference slot is in use
  and the reference lists change from frame to frame; 352x288 streams from both encoders (several superblocks in both
  directions); 2x2 tiles of 128x128 superblocks with motion vectors that cross the tile boundaries;
- reference scaling with five to eight frame sizes in one stream, at 8 and 10 bits, and super-resolution combined with
  reference scaling (SVT-AV1; its sizes must be multiples of 8: otherwise it codes frames larger than the maximum of
  the sequence, which libaom rejects);
- found on the way: SVT-AV1 offsets the luma DC quantizer (`luma-y-dc-qindex-offset`), which libaom never does; the
  libaom film grain test vector 9 codes `update_grain = 0` on inter frames (parameters of a reference frame).

Still from SVT-AV1 only, because libaom rejects `superres-mode`, `resize-mode`, `sframe-dist` and `enable-fwd-kf` in this
build: super-resolution, reference scaling, switch frames and intra-only frames, in 4:2:0 at 8 and 10 bits.

## HEVC conformance vectors

HEVC decoding is normative too: every conforming decoder produces the same samples. The HEVC decoder is tested below the
HEIF container and the color conversion, on raw byte streams (`hevc/*.hevc`, the byte stream format of H.265 Annex B: NAL
units after start code prefixes) whose expected output is the **SHA-256 of every plane of every output picture**. There
are intra vectors (1 to 3 pictures) and inter vectors (2 to 68 pictures: P and B pictures, pictures output in another
order than they are decoded in, leading pictures that are skipped, several coded video sequences). Most are 16x16 to
136x136 pictures of a few coding tree blocks, whose sizes are not multiples of the minimum coding block (the conformance
window crops them, to odd sizes where the chroma format allows); the tile vectors are 512 samples wide or more (a tile
of the Main profiles is at least 256x64), and a few are CIF pictures or whole groups of pictures. Three are the streams
the benchmarks decode (`hevc/x265-benchmark-*`: two lossy 384x216 stills, 4:2:0 and 4:4:4, and five 176x144 pictures with
B pictures, all with the deblocking filter and sample adaptive offset). Most streams are what
an encoder wrote; a few are derived from the stream of another vector, for what no encoder writes
([below](#hevc-vectors-an-encoder-did-not-write-alone)). The vectors are in the `hevcVectors` section of `manifest.json`,
apart from `fixtures`:

```json
"hevcVectors": {
  "generator": { "script": "tools/Meziantou.Framework.Imaging.CorpusGenerator/HevcCorpus.cs", "command": "...", "tools": ["ffmpeg 9.0.2", "x265 4.2", "kvazaar 2.3.2", "libde265 1.1.3", "HM 18.0", ".NET 10.0"] },
  "headerParser": { "name": "ffmpeg 9.0.2 (trace_headers bitstream filter)", "command": "ffmpeg ... -bsf:v trace_headers ..." },
  "decoders": [
    { "id": "ffmpeg", "name": "ffmpeg 9.0.2 (hevc)", "command": "ffmpeg ... -c:v hevc ..." },
    { "id": "libde265", "name": "libde265 1.1.3 (dec265)", "command": "dec265 -q -t 0 -o {frames}.yuv {input}" },
    { "id": "hm", "name": "HM 18.0 (TAppDecoder)", "command": "TAppDecoder -b {input} -o {frames}.yuv -d 0" }
  ],
  "planeDigest": "SHA-256 of the samples of one plane ...",
  "pictureHash": "The decoded picture hash SEI message of each picture ...",
  "entries": [{
    "id": "hevc/kvazaar-tiles-slices-sao-512x128",
    "input": { "path": "hevc/kvazaar-tiles-slices-sao-512x128.hevc", "sha256": "...", "role": "encoded" },
    "provenance": { "origin": "generated", "license": "CC0-1.0", "generator": "...", "tools": ["ffmpeg 9.0.2", "kvazaar 2.3.2"], "commands": ["ffmpeg ... -c:v libkvazaar -kvazaar-params hash=md5,... -f hevc {input}"] },
    "features": ["hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.ctb=64", "hevc.encoder=kvazaar", "hevc.loopFilterAcrossSlices=0", "hevc.slices=4", "hevc.tiles=2x2", "..."],
    "pixelFormat": "yuv420p", "bitDepth": 8, "chromaFormat": "4:2:0", "pictureCount": 1, "frameCount": 1, "outputOrder": "decoding",
    "decoders": { "agree": ["ffmpeg", "hm"], "excluded": [{ "decoder": "libde265", "outcome": "differs", "reason": "libde265 1.1.3 applies sample adaptive offset across ...", "observed": "outputs other samples in 1 of the 1 pictures (plane 1, 2; ...)" }] },
    "pictureHash": { "type": "md5", "verifiedBy": ["ffmpeg", "hm"] },
    "frames": [{ "width": 512, "height": 128, "poc": 0, "decodeOrder": 0, "planes": ["<sha256 of Y>", "<sha256 of Cb>", "<sha256 of Cr>"], "hash": ["<md5 of Y>", "<md5 of Cb>", "<md5 of Cr>"] }]
  }]
}
```

- **Inputs.** Deterministic FFmpeg `lavfi` sources (`testsrc2`, `testsrc`, `mandelbrot`, seeded `noise`; for the inter
  vectors, `testsrc2` at 10 frames per second, as is, rotating around its center, or fading in from black for weighted
  prediction; `testsrc2` rounds its size down to even numbers, so the odd sizes are cropped from it in the pixel format
  of the stream) encoded by three encoders, single-threaded:
  x265 and kvazaar through FFmpeg (`-c:v libx265 -x265-params ...` with `pools=1` and `-threads 1`, `-c:v libkvazaar
  -kvazaar-params ...` with `threads=0,owf=0`; `-flags +bitexact`, raw byte stream `-f hevc`), and HM (`TAppEncoder`),
  which reads a raw file FFmpeg writes from the same sources and takes every option on its command line (no
  configuration file). x265 covers every chroma format at 8, 10 and 12 bits and most tools of version 1 of the
  specification; kvazaar (8-bit 4:2:0, 64x64 coding tree blocks) adds tiles, a slice per tile and dependent slice
  segments; HM is the only producer of PCM and of the coding tools of the range extensions, and the one that writes
  reference picture sets in the sequence parameter set, slice-level deblocking and chroma QP offsets, and conformance
  windows with a left and a top offset. The exact commands of each vector are in its `provenance.commands` (the scaling
  list file some of them read is described in `provenance.parameters.files`, with its hash), and the check mode of the
  generator proves that every stream is reproduced byte for byte. Every tool comes from the meziantou/prebuilt release
  pinned by the `Meziantou.Prebuilt` package; the versions of FFmpeg, x265, kvazaar, libde265 and HM are pinned in
  `HevcCorpus.PinnedTools`.
- **Output pictures.** `frames` lists what a decoder outputs, in **output order**, which is not the decoding order as
  soon as a stream has B pictures (`outputOrder`, `hevc.outputOrder=reordered`): increasing picture order count inside a
  coded video sequence, sequence after sequence. Each frame records its picture order count (`poc`, which starts again
  with each sequence) and the index of its picture among the coded pictures of the stream, in decoding order
  (`decodeOrder`); `pictureCount` is the number of coded pictures. Fewer pictures are output than the stream codes in
  two cases: a RASL picture whose IRAP picture starts a sequence (a stream that starts at a CRA picture, a BLA picture,
  a CRA picture after an end of sequence) is not decoded (`hevc.raslSkipped`), and the pictures that were still waiting
  for their output are discarded when a CRA picture follows an end of sequence (`hevc.noOutputOfPriorPics`:
  `NoOutputOfPriorPicsFlag` is then 1, clause C.5.2.2). The generator derives the output pictures from the headers alone
  (the coded video sequences and the picture order counts of clauses 8.1.3 and 8.3.1, the "bumping" process of clause
  C.5.2 with `sps_max_num_reorder_pics` and `sps_max_latency_increase_plus1`), and fails when a decoder reports
  something else: `ffprobe` gives the size, the pixel format and the picture type of every frame FFmpeg outputs, and
  TAppDecoder logs the picture order count of every picture it decodes. Each frame has its own `width` and `height`:
  they change when a second coded video sequence has another sequence parameter set (`hevc.frameSizes`).
- **Plane digests.** Each stream is decoded by three independent HEVC decoders, to raw planes at the bit depth of the
  stream and **cropped to the conformance window** (the output of the decoding process): FFmpeg's native `hevc` decoder
  (one raw file per output frame in the native pixel format, which `ffprobe` must report for every frame so that no
  swscale conversion takes part, without scaling, `-flags unaligned` so that a left offset of the window is applied
  exactly), libde265 (`dec265`) and HM (`TAppDecoder -d 0`, the reference decoder). **A digest is recorded only when at
  least two of them produce the same bytes for every plane of every output picture, in output order**, and the entry
  names them (`decoders.agree`); see [below](#hevc-decoders-that-agree-and-decoders-that-are-left-out) for the third
  one. A digest from a single decoder is never committed.
- **Digest layout** (`planeDigest`): the samples of one plane of the cropped picture, rows in order, exactly plane-width
  samples per row, 1 byte per sample up to 8 bits and 2 little-endian bytes above, no padding. Planes are in bitstream
  order Y, Cb, Cr; for an identity-matrix stream (`hevc.matrix=identity`, `matrix_coeffs = 0` in the VUI of a 4:4:4
  stream) that is FFmpeg's planar GBR order G, B, R. A monochrome stream (`4:0:0`) has one plane. `width` and `height`
  are those of the cropped luma plane; a chroma plane is `width / SubWidthC` samples wide and `height / SubHeightC` rows
  high (the offsets of the conformance window are in chroma samples, so the divisions are exact). `pixelFormat` is the
  FFmpeg name of these planes (`yuv420p`, `yuv422p10le`, `gbrp12le`, `gray`...), even for a full-range 8-bit 4:2:0
  stream, which FFmpeg still decodes to the deprecated `yuvj420p` (the same planes).
- **Picture hashes.** Every picture of every vector carries a decoded picture hash SEI message (H.265, clause D.3.19),
  so that a decoder can be checked with nothing but the stream: an MD5 for most vectors, a CRC or a checksum for some
  (`pictureHash.type`, `hevc.pictureHash`; x265 `hash=1|2|3`, kvazaar `hash=md5|checksum`, HM `SEIDecodedPictureHash`).
  The hash covers each colour component of the **whole decoded picture**, before the conformance window is applied,
  where the plane digests cover the cropped picture. Each frame records the values the header parser reads in the
  stream (`hash`: one lowercase hexadecimal value per component, 16 bytes for an MD5, 2 for a CRC, 4 for a checksum).
  At generation, the decoders that can check the hashes do, and `pictureHash.verifiedBy` names the agreeing ones that
  computed the hash of every picture and found the values of the stream: FFmpeg checks the MD5 of every picture
  (`-err_detect crccheck`) and TAppDecoder every type. `dec265 -c` only compares the hash of the last picture of a
  stream (a wrong hash of any other picture goes unnoticed), and computes the checksum above 8 bits as for 8 bits: a
  mismatch it reports rules it out, but it is only named in `verifiedBy` for a stream of one picture. **Decoders that
  agree with each other and not with the hashes fail the generation**: the encoder did not write the hash of what it
  coded, or both decoders are wrong.
- **Features** are what the parameter sets, the slice segment headers and the other NAL units signal, as parsed by a
  fourth implementation, FFmpeg's `trace_headers` bitstream filter (libavcodec's coded bitstream reader, which its hevc
  decoder does not use): never the encoder options, which do not guarantee that a tool is used (x265 ignores `slices`
  without enough rows of coding tree blocks, only codes RASL pictures with enough B pictures before a CRA picture,
  picks another `log2_max_pic_order_cnt_lsb` than the one asked for). A value lists the distinct values of the stream,
  and the parameter sets that count are the ones a slice segment refers to. Each recipe declares the tags it exists for
  (`HevcVector.Requires`), and the generation fails when the stream does not have them. Four tags do not come from the
  headers: `hevc.encoder`, `hevc.derived`, `hevc.lossless` (measured: the stream enables the transform and quantization
  bypass, and the decoded pictures are the pictures given to the encoder) and `hevc.unsupported` (derived from the
  headers: what the library rejects). The vocabulary is closed: `FixtureManifestValidator.AllowedHevcFeatureKeys` lists
  the keys, and the values of the keys that are enumerations.
- **Coding tools.** The headers do not tell which tools the blocks use (the 35 intra prediction modes, NxN intra
  partitions, the transform sizes of a residual quadtree, merge and AMVP candidates, the band and the edge classes of
  sample adaptive offset, PCM and bypass coding units...): the recipes vary the content and the search for them
  (`rd=6` of x265 with rotated and noisy sources, `sao=band` and `sao=edge` of kvazaar, a very low QP on noise for PCM).
  Whether the vectors reach every tool is for a tool coverage test of the HEVC decoder to tell, as
  `Av1ToolCoverageTests` does for AV1.
- **Unsupported streams.** A few vectors are streams the library rejects (`hevc.unsupported=<reason>`): bit depths above
  12, luma and chroma planes of different bit depths, extended precision processing, CABAC bypass alignment (all from
  HM), and a field sequence (x265). They are recorded like the others when two decoders agree. The two extended
  precision vectors have no digest (`planes` is empty, `decoders.agree` is HM alone): FFmpeg and libde265 ignore the
  flag and decode other samples, which the hashes of the stream contradict. No encoder of the pinned release writes
  separate colour planes.
- **Tests.** `CorpusManifestTests` validates the section (files, hashes, licenses, the decoders that agree and the ones
  left out, picture hashes, orphan files), the feature coverage below, what the frames and the tags say together, and
  what the provenance of a derived vector claims (the NAL units that differ from the source vector).
  `FixtureManifestValidatorTests` covers the rules of the section. The `verify` subcommand of the generator checks the
  hash of every vector and re-derives the digests with each of the decoders that agreed.

### HEVC decoders that agree, and decoders that are left out

Three decoders, and two are enough: each of them has known differences, on streams that the two others decode alike and
whose picture hashes they confirm. A recipe declares the decoder it expects to be left out, with the reason
(`HevcVector.Excluded`); the entry records it (`decoders.excluded`: the decoder, `rejects` or `differs`, the reason, and
what the generator observed: the message of the decoder, or the pictures and the planes that differ). **Any other
disagreement fails the generation, and so does a decoder that is declared left out and agrees**: when a decoder is fixed,
the note goes away with a reviewed regeneration. What the pinned versions do:

| Decoder left out | Streams | What it does |
| --- | --- | --- |
| libde265 1.1.3 | Slices that disable in-loop filtering across their boundaries (`slice_loop_filter_across_slices_enabled_flag = 0`: a slice per tile of kvazaar, the slices of x265), with sample adaptive offset | Applies sample adaptive offset across the boundaries: other chroma samples, and its own hash check fails |
| libde265 1.1.3 | The range extensions tools of residual coding combined, in 4:2:0 and 4:2:2 (HM) | Other chroma samples in the inter pictures; its own hash check fails |
| libde265 1.1.3 | A CRA picture after an end of sequence, with pictures of the previous sequence still waiting for their output | Outputs the pictures that `NoOutputOfPriorPicsFlag` discards (clause C.5.2.2) |
| libde265 1.1.3 (`dec265`) | A conformance window with a left or a top offset above 8 bits | Writes other samples than the ones it decodes (the hash check of a one-picture stream passes) |
| libde265 1.1.3 (`dec265`) | Monochrome above 8 bits, cropped on the right by x265 | Decodes the stream when it writes nothing, and ends with `std::bad_alloc` when it writes the pictures |
| FFmpeg 9.0.2 | Residual DPCM (implicit or explicit) or rotation in coding units that bypass the transform and the quantization (HM, kvazaar `implicit-rdpcm`) | Other samples; its MD5 check fails |
| FFmpeg 9.0.2 | `high_precision_offsets_enabled_flag` (weighted prediction of the range extensions) | Other samples; its MD5 check fails |
| FFmpeg 9.0.2 | A 12-bit 4:2:0 stream at slice QP 51 (x265) | Other luma samples; its MD5 check fails |
| FFmpeg 9.0.2 | `cabac_bypass_alignment_enabled_flag`, `extended_precision_processing_flag` (rejected by the library) | Ignores the flag: other samples |
| FFmpeg 9.0.2 | Bit depths above 12, luma and chroma planes of different bit depths (rejected by the library) | Rejects the stream |
| HM 18.0 | A tile narrower than 256 luma samples or lower than 64 (kvazaar) | Rejects the stream: the limit of the Main profiles (clause A.3.2) |
| HM 18.0 | A 16x16 coding tree block in a lossless stream of x265, which signals level 8.5 | Rejects the stream: the limit of level 5 and above (clause A.4.1) |
| HM 18.0 (`TAppDecoder`) | A field sequence (x265 `interlace`) | Decodes the same pictures (it finds the hash of each one) and writes the two fields of a frame interleaved |

Not recorded, because no two decoders agree on them or because the hashes contradict the decoders that do:

- a 12-bit 4:4:4 stream of x265 at slice QP 51: FFmpeg, libde265 and HM decode three different pictures, and HM alone
  finds the hashes;
- tiles together with wavefronts (kvazaar, in a Main profile stream; TAppEncoder only codes them in a high throughput
  profile): again three different pictures, HM alone with the hashes;
- x265 does not write the hashes of the pictures it codes when an inter picture is a single 64x64 coding tree block:
  the three decoders agree with each other and not with the hashes, from the second or third picture on. The inter
  recipes of x265 use 32x32 coding tree blocks unless they ask for another size on a larger picture;
- x265 writes a wrong CRC for the chroma planes of a picture, unless the picture is exactly one row of whole coding
  tree blocks: its CRC vectors are monochrome or one coding tree block high (its MD5 and its checksum are right; HM
  writes every type).

And one kind of stream is not recorded although the three decoders agree on it and confirm its hashes, because it is not
a conforming stream: kvazaar 2.3.2 does not write the scaling lists of a file (`scaling-list=custom` with `cqmfile`).
Whatever the layout of the file, each list it codes has the first half of the values of the file at the even positions,
in raster order, and 0 at the odd ones, and a scaling list coefficient shall be greater than 0 (clause 7.4.5). **Three
decoders that agree do not make a stream conforming**: the generation reconstructs the coefficients of every scaling
list a parameter set codes and fails on a value of 0. The lists of a file are in the vectors of x265 and HM.

### HEVC vectors an encoder did not write alone

Some cases are written by no encoder option. They are covered by vectors **derived from the stream of another vector**
(`hevc.derived=<kind>`, `HevcCorpus.Derived.cs` and `HevcCorpus.Stream.cs`), recorded like any other vector: headers and
feature tags read back from the final stream by `trace_headers`, planes decoded by the three decoders, digests recorded
only when two agree. A derivation never touches slice data: it removes, inserts or retypes whole NAL units, or has an
FFmpeg bitstream filter rewrite a parameter set. `provenance.parameters` records the source vector (`derivedFrom`), the
SHA-256 of its stream (`sourceSha256`), the kind (`derivation`) and what was done (`description`); `provenance.commands`
is the encoder command of the source, followed by the FFmpeg command of a bitstream filter.

- `metadata`: FFmpeg's `hevc_metadata` rewrites the sequence parameter set: a **conformance window with an offset on
  every side** in each chroma format (`crop_left=...:crop_right=...:crop_top=...:crop_bottom=...`; the decoded pictures
  and their hashes do not change, the output pictures are other parts of them; HM also writes such windows directly,
  `ConformanceWindowMode=3`), a chroma sample location, access unit delimiters (`aud=insert`).
- `filterUnits`: FFmpeg's `filter_units` removes the prefix SEI messages (`remove_types=39`); the picture hashes are
  suffix SEI messages and stay.
- `startAtIrap`: the stream from the access unit of its first CRA picture after the first picture, as after a random
  access. The RASL pictures that follow the CRA picture refer to pictures that are not there: they are not decoded.
- `nalType`: every CRA picture after the first picture retyped as a BLA picture (`BLA_W_LP`, `BLA_W_RADL` or
  `BLA_N_LP`), as a splicer does: the 6 bits of `nal_unit_type` of its slice segment NAL units are overwritten (the
  slice segments of the IRAP pictures that are not IDR pictures have the same syntax). The access units of the RASL
  pictures are removed, unless the type allows leading pictures that are skipped (`BLA_W_LP`). A BLA picture starts a
  coded video sequence, and the picture order count starts again from its least significant bits.
- `endOfSequence`: an end of sequence NAL unit (`00 00 01 48 01`) inserted before the access unit of every CRA picture
  after the first picture. The CRA picture then starts a coded video sequence, its RASL pictures are skipped, and the
  pictures of the previous sequence that were still waiting for their output are discarded (libde265 outputs them, and
  is left out).
- `subLayers`: the access units of the pictures of the higher temporal sub-layers removed (sub-bitstream extraction,
  clause 10): what a decoder that drops them decodes.
- `concatenation`: the stream of another vector appended, with or without an end of sequence between them: two coded
  video sequences whose pictures have different sizes (`provenance.parameters.appended`, `appendedSha256`).
- `trailingUnits`: a filler data NAL unit, an end of sequence and an end of bitstream after the last picture.

No encoder of the pinned release writes long-term reference pictures, a reference picture list modification, scaling
lists in the picture parameter set, `pic_output_flag = 0` or separate colour planes: they would need slice headers
written by the generator, and are not covered by a vector.

`hevc.encoder`, `hevc.derived`, `hevc.lossless` and `hevc.unsupported` are the only tags that are not read in a header.

### HEVC vector features

The table maps the decoding tools to the vectors that exercise them. It is **verified** like the
[fixture coverage matrix](#fixture-coverage-matrix), with the same selector syntax: `FixtureCoverageMatrixTests` recomputes
every count from the `hevcVectors` section and fails when the table is stale, when a row has no vector, or when a feature
key recorded in the section is not mapped by any row.

| Feature | Selector | Vectors |
| --- | --- | --- |
| Main profile | `hevc.profile=main` | 157 |
| Main 10 profile | `hevc.profile=main10` | 11 |
| Main Still Picture profile | `hevc.profile=mainStillPicture` | 1 |
| Range extensions profile, monochrome | `hevc.profile=rext-400-8` | 3 |
| Range extensions profile, monochrome 12 | `hevc.profile=rext-400-12` | 5 |
| Range extensions profile, Main 12 | `hevc.profile=rext-420-12` | 5 |
| Range extensions profile, Main 4:2:2 10 | `hevc.profile=rext-422-10` | 23 |
| Range extensions profile, Main 4:2:2 12 | `hevc.profile=rext-422-12` | 3 |
| Range extensions profile, Main 4:4:4 | `hevc.profile=rext-444-8` | 20 |
| Range extensions profile, Main 4:4:4 10 | `hevc.profile=rext-444-10` | 22 |
| Range extensions profile, Main 4:4:4 12 | `hevc.profile=rext-444-12` | 20 |
| Range extensions profile, Main Intra (what x265 signals for an all-intra 8-bit 4:2:0 stream) | `hevc.profile=rext-420-8-intra` | 53 |
| Range extensions profile, Main 4:2:2 10 Intra | `hevc.profile=rext-422-10-intra` | 15 |
| Range extensions profile, Main 4:4:4 16 Intra | `hevc.profile=rext-444-16-intra` | 3 |
| Range extensions profile, Main 4:4:4 Still Picture | `hevc.profile=rext-444-8-still` | 1 |
| High throughput profile (rejected by the library) | `hevc.profile=highThroughput-444-16-intra` | 1 |
| Level (every vector) | `hevc.level` | 402 |
| Level 8.5 (x265, lossless) | `hevc.level=8.5` | 11 |
| High tier | `hevc.tier=high` | 1 |
| Monochrome (4:0:0), 8-bit | `hevc.chroma=4:0:0` + `hevc.bitDepth=8` | 6 |
| Monochrome (4:0:0), 10-bit | `hevc.chroma=4:0:0` + `hevc.bitDepth=10` | 7 |
| Monochrome (4:0:0), 12-bit | `hevc.chroma=4:0:0` + `hevc.bitDepth=12` | 8 |
| 4:2:0, 8-bit | `hevc.chroma=4:2:0` + `hevc.bitDepth=8` | 215 |
| 4:2:0, 10-bit | `hevc.chroma=4:2:0` + `hevc.bitDepth=10` | 21 |
| 4:2:0, 12-bit | `hevc.chroma=4:2:0` + `hevc.bitDepth=12` | 6 |
| 4:2:2, 8-bit | `hevc.chroma=4:2:2` + `hevc.bitDepth=8` | 13 |
| 4:2:2, 10-bit | `hevc.chroma=4:2:2` + `hevc.bitDepth=10` | 30 |
| 4:2:2, 12-bit | `hevc.chroma=4:2:2` + `hevc.bitDepth=12` | 10 |
| 4:4:4, 8-bit | `hevc.chroma=4:4:4` + `hevc.bitDepth=8` | 27 |
| 4:4:4, 10-bit | `hevc.chroma=4:4:4` + `hevc.bitDepth=10` | 30 |
| 4:4:4, 12-bit | `hevc.chroma=4:4:4` + `hevc.bitDepth=12` | 26 |
| Identity matrix (planar GBR) | `hevc.matrix=identity` | 13 |
| Identity matrix, 10-bit | `hevc.matrix=identity` + `hevc.bitDepth=10` | 3 |
| Identity matrix, 12-bit | `hevc.matrix=identity` + `hevc.bitDepth=12` | 4 |
| 16x16 coding tree blocks | `hevc.ctb=16` | 44 |
| 32x32 coding tree blocks | `hevc.ctb=32` | 152 |
| 64x64 coding tree blocks | `hevc.ctb=64` | 206 |
| Minimum coding block 8x8 | `hevc.minCb=8` | 393 |
| Minimum coding block 16x16 | `hevc.minCb=16` | 5 |
| Minimum coding block 32x32 | `hevc.minCb=32` | 3 |
| Minimum coding block 64x64 (the coding tree block) | `hevc.minCb=64` | 1 |
| Minimum transform block 4x4 | `hevc.minTb=4` | 399 |
| Minimum transform block 8x8 | `hevc.minTb=8` | 1 |
| Minimum transform block 16x16 | `hevc.minTb=16` | 1 |
| Minimum transform block 32x32 | `hevc.minTb=32` | 1 |
| Maximum transform block 4x4 (DST for intra luma) | `hevc.maxTb=4` | 5 |
| Maximum transform block 8x8 | `hevc.maxTb=8` | 2 |
| Maximum transform block 16x16 | `hevc.maxTb=16` | 38 |
| Maximum transform block 32x32 | `hevc.maxTb=32` | 357 |
| Transform hierarchy depth of intra coding units: 0 | `hevc.tuDepthIntra=0` | 251 |
| Transform hierarchy depth of intra coding units: 1 | `hevc.tuDepthIntra=1` | 4 |
| Transform hierarchy depth of intra coding units: 2 | `hevc.tuDepthIntra=2` | 144 |
| Transform hierarchy depth of intra coding units: 3 | `hevc.tuDepthIntra=3` | 3 |
| Transform hierarchy depth of inter coding units: 1 | `hevc.tuDepthInter=1` + `hevc.frameTypes=inter` | 1 |
| Transform hierarchy depth of inter coding units: 2 | `hevc.tuDepthInter=2` + `hevc.frameTypes=inter` | 82 |
| Transform hierarchy depth of inter coding units: 3 | `hevc.tuDepthInter=3` + `hevc.frameTypes=inter` | 2 |
| Picture of one coding tree block | `hevc.ctbs=1x1` | 140 |
| Picture of 6x5 coding tree blocks (CIF, or QCIF with 32x32 blocks) | `hevc.ctbs=6x5` | 5 |
| Picture of 6x4 coding tree blocks (384x216, the stills of the benchmarks) | `hevc.ctbs=6x4` | 2 |
| Picture one coding tree block wide, with wavefronts | `hevc.ctbs=1x4` + `hevc.wpp` | 2 |
| Conformance window: right offset | `hevc.crop=right` | 71 |
| Conformance window: bottom offset | `hevc.crop=bottom` | 73 |
| Conformance window: left offset | `hevc.crop=left` | 12 |
| Conformance window: top offset | `hevc.crop=top` | 12 |
| Conformance window on every side, monochrome | `hevc.crop=left` + `hevc.crop=top` + `hevc.chroma=4:0:0` | 2 |
| Conformance window on every side, 4:2:0 | `hevc.crop=left` + `hevc.crop=top` + `hevc.chroma=4:2:0` | 4 |
| Conformance window on every side, 4:2:2 | `hevc.crop=left` + `hevc.crop=top` + `hevc.chroma=4:2:2` | 3 |
| Conformance window on every side, 4:4:4 | `hevc.crop=left` + `hevc.crop=top` + `hevc.chroma=4:4:4` | 3 |
| Strong intra smoothing off | `hevc.strongIntraSmoothing=0` | 36 |
| Strong intra smoothing on | `hevc.strongIntraSmoothing=1` | 366 |
| Constrained intra prediction | `hevc.constrainedIntraPred` | 3 |
| Transform skip | `hevc.transformSkip` | 24 |
| Sign data hiding off | `hevc.signDataHiding=0` | 36 |
| Sign data hiding on | `hevc.signDataHiding=1` | 366 |
| Asymmetric motion partitions | `hevc.amp` | 155 |
| PCM coding units | `hevc.pcm` | 8 |
| PCM, samples at the bit depth of an 8-bit stream | `hevc.pcm.bitDepth=8/8` + `hevc.bitDepth=8` | 4 |
| PCM, 8-bit samples in a 10-bit stream | `hevc.pcm.bitDepth=8/8` + `hevc.bitDepth=10` | 1 |
| PCM, 8-bit samples in a 12-bit stream | `hevc.pcm.bitDepth=8/8` + `hevc.bitDepth=12` | 1 |
| PCM, 8x8 to 32x32 coding units | `hevc.pcm.size=8-32` | 5 |
| PCM, deblocking of the PCM samples disabled | `hevc.pcm.loopFilterDisabled` | 4 |
| PCM in inter pictures | `hevc.pcm` + `hevc.frameTypes=inter` | 2 |
| Transform and quantization bypass enabled | `hevc.transquantBypass` | 27 |
| Lossless: the decoded pictures are the source pictures (measured) | `hevc.lossless` | 22 |
| Lossless, inter pictures | `hevc.lossless` + `hevc.frameTypes=inter` | 10 |
| Lossless, identity matrix | `hevc.lossless` + `hevc.matrix=identity` | 3 |
| Slice QP (every vector) | `hevc.sliceQp` | 402 |
| Lowest slice QP of a 12-bit stream (-24) | `hevc.sliceQp=-24` | 1 |
| Slice QP 0 | `hevc.sliceQp=0` | 11 |
| Slice QP 51 | `hevc.sliceQp=51` | 5 |
| Chroma QP offsets of the picture parameter set | `hevc.chromaQpOffset` | 49 |
| Chroma QP offsets, 4:2:2 | `hevc.chromaQpOffset` + `hevc.chroma=4:2:2` | 2 |
| Chroma QP offsets of the slice header | `hevc.sliceChromaQpOffset` | 2 |
| QP deltas of coding units | `hevc.cuQpDelta` | 159 |
| Quantization groups of 64x64 samples | `hevc.qgSize=64` | 3 |
| Quantization groups of 32x32 samples | `hevc.qgSize=32` | 129 |
| Quantization groups of 16x16 samples | `hevc.qgSize=16` | 23 |
| Quantization groups of 8x8 samples | `hevc.qgSize=8` | 4 |
| Scaling lists: the default lists | `hevc.scalingList=default` | 7 |
| Scaling lists of the sequence parameter set | `hevc.scalingList=sps` | 9 |
| Scaling list coded with its coefficients | `hevc.scalingList.coding=explicit` | 9 |
| Scaling list copied from another list | `hevc.scalingList.coding=copy` | 7 |
| Scaling list set to the default list by its prediction | `hevc.scalingList.coding=default` | 2 |
| Deblocking filter on | `hevc.deblocking=1` | 395 |
| Deblocking filter off | `hevc.deblocking=0` | 7 |
| Deblocking offsets (beta/tc, halved) | `hevc.deblocking.offsets` | 7 |
| Deblocking parameters in the slice header | `hevc.deblocking.sliceOverride` | 1 |
| Sample adaptive offset off | `hevc.sao=off` | 10 |
| Sample adaptive offset, luma | `hevc.sao=luma` | 392 |
| Sample adaptive offset, chroma | `hevc.sao=chroma` | 370 |
| Sample adaptive offset disabled by a slice | `hevc.sao=sliceOff` | 11 |
| Sample adaptive offset, 10-bit | `hevc.sao=luma` + `hevc.bitDepth=10` | 88 |
| Sample adaptive offset, 12-bit | `hevc.sao=luma` + `hevc.bitDepth=12` | 50 |
| In-loop filters disabled across slices | `hevc.loopFilterAcrossSlices=0` | 136 |
| Several slices in a picture | `hevc.slices` | 16 |
| Dependent slice segments | `hevc.dependentSlices` | 6 |
| Tiles | `hevc.tiles` | 15 |
| 2x2 tiles | `hevc.tiles=2x2` | 9 |
| Tiles, uniform spacing | `hevc.tiles.spacing=uniform` | 11 |
| Tiles, explicit sizes | `hevc.tiles.spacing=explicit` | 4 |
| Tiles, in-loop filters across their boundaries | `hevc.tiles.loopFilterAcross=1` | 4 |
| Tiles, no in-loop filter across their boundaries | `hevc.tiles.loopFilterAcross=0` | 11 |
| Tiles with a slice per tile | `hevc.tiles` + `hevc.slices` | 5 |
| Tiles in inter pictures | `hevc.tiles` + `hevc.frameTypes=inter` | 8 |
| Wavefront parallel processing | `hevc.wpp` | 39 |
| Wavefronts with dependent slice segments | `hevc.wpp` + `hevc.dependentSlices` | 3 |
| Wavefronts with several slices | `hevc.wpp` + `hevc.slices` | 6 |
| CABAC initialization type chosen by the slice | `hevc.cabacInit` | 3 |
| I slices only | `hevc.frameTypes=intra` | 198 |
| P or B slices | `hevc.frameTypes=inter` | 204 |
| Slice types (every vector) | `hevc.sliceTypes` | 402 |
| P slices | `hevc.sliceTypes=P` | 163 |
| B slices | `hevc.sliceTypes=B` | 121 |
| B pictures used as references | `hevc.referenceB` | 92 |
| Temporal motion vector prediction off | `hevc.temporalMvp=0` | 8 |
| Temporal motion vector prediction on | `hevc.temporalMvp=1` | 196 |
| Collocated picture from list 1 | `hevc.collocatedFromL1` | 114 |
| Motion vector differences of list 1 inferred to be zero | `hevc.mvdL1Zero` | 39 |
| One merge candidate | `hevc.mergeCand=1` | 2 |
| Two merge candidates | `hevc.mergeCand=2` | 3 |
| Three merge candidates | `hevc.mergeCand=3` | 96 |
| Four merge candidates | `hevc.mergeCand=4` | 1 |
| Five merge candidates | `hevc.mergeCand=5` | 102 |
| Parallel merge level | `hevc.parallelMergeLevel` | 3 |
| Two active reference pictures | `hevc.refs=2` | 145 |
| Three active reference pictures | `hevc.refs=3` | 63 |
| Four active reference pictures | `hevc.refs=4` | 3 |
| Weighted prediction enabled for P slices | `hevc.weightedPred=p` | 103 |
| Weighted prediction enabled for B slices | `hevc.weightedPred=b` | 5 |
| Weights coded for luma | `hevc.weights=luma` | 21 |
| Weights coded for chroma | `hevc.weights=chroma` | 19 |
| Reference picture set of the sequence parameter set | `hevc.rps=sps` | 84 |
| Reference picture set coded in the slice header | `hevc.rps=slice` | 133 |
| Reference picture set predicted from another one | `hevc.rps.interPrediction` | 13 |
| NAL unit types (every vector) | `hevc.nal` | 402 |
| IDR pictures without leading pictures | `hevc.nal=IDR_N_LP` | 217 |
| IDR pictures that may have RADL pictures | `hevc.nal=IDR_W_RADL` | 183 |
| CRA pictures | `hevc.nal=CRA` | 17 |
| RASL pictures | `hevc.nal=RASL_N` | 15 |
| RASL pictures used as references | `hevc.nal=RASL_R` | 11 |
| RADL pictures | `hevc.nal=RADL_N` | 2 |
| BLA pictures with leading pictures | `hevc.nal=BLA_W_LP` | 2 |
| BLA pictures with RADL pictures only | `hevc.nal=BLA_W_RADL` | 1 |
| BLA pictures without leading pictures | `hevc.nal=BLA_N_LP` | 2 |
| Sub-layer non-reference pictures | `hevc.nal=TRAIL_N` | 86 |
| Temporal sub-layer access pictures | `hevc.nal=TSA_N` | 12 |
| Access unit delimiters | `hevc.nal=AUD` | 6 |
| End of sequence | `hevc.nal=EOS` | 4 |
| End of bitstream | `hevc.nal=EOB` | 1 |
| Filler data | `hevc.nal=FD` | 1 |
| Temporal sub-layers | `hevc.temporalLayers` | 12 |
| Three temporal sub-layers | `hevc.temporalLayers=3` | 11 |
| Parameter sets sent again | `hevc.repeatedParameterSets` | 37 |
| Picture parameter set that changes inside the stream | `hevc.pps` | 3 |
| Sequence parameter set that changes inside the stream | `hevc.sps` | 3 |
| Several output pictures | `hevc.frames` | 216 |
| 16 output pictures | `hevc.frames=16` | 1 |
| 17 output pictures | `hevc.frames=17` | 2 |
| Output order different from the decoding order | `hevc.outputOrder=reordered` | 92 |
| Pictures that wait for their output (sps_max_num_reorder_pics) | `hevc.reorder` | 94 |
| Several coded video sequences | `hevc.sequences` | 27 |
| Picture size that changes with the coded video sequence | `hevc.frameSizes` | 3 |
| RASL pictures that are not decoded | `hevc.raslSkipped` | 7 |
| Pictures discarded before their output (NoOutputOfPriorPicsFlag) | `hevc.noOutputOfPriorPics` | 2 |
| Picture order count that wraps | `hevc.pocWrap` | 5 |
| Picture order count coded with 4 bits | `hevc.pocLsbBits=4` | 31 |
| Picture order count coded with 8 bits | `hevc.pocLsbBits=8` | 366 |
| SEI messages (every vector) | `hevc.sei` | 402 |
| Decoded picture hash: MD5 | `hevc.pictureHash=md5` | 370 |
| Decoded picture hash: CRC | `hevc.pictureHash=crc` | 14 |
| Decoded picture hash: checksum | `hevc.pictureHash=checksum` | 18 |
| Mastering display colour volume | `hevc.sei=masteringDisplayColourVolume` | 2 |
| Content light level | `hevc.sei=contentLightLevel` | 2 |
| Active parameter sets, recovery point | `hevc.sei=activeParameterSets` + `hevc.sei=recoveryPoint` | 1 |
| Buffering period and picture timing (HRD) | `hevc.sei=bufferingPeriod` + `hevc.sei=pictureTiming` + `hevc.hrd` | 2 |
| User data | `hevc.sei=userDataUnregistered` | 1 |
| Colour description | `hevc.colourDescription` | 19 |
| Full-range samples | `hevc.fullRange` | 28 |
| Chroma sample location | `hevc.chromaLoc` | 8 |
| Chroma sample location type 5 | `hevc.chromaLoc=5` | 2 |
| Default display window (not applied) | `hevc.defaultDisplayWindow` | 3 |
| Implicit residual DPCM | `hevc.implicitRdpcm` | 13 |
| Explicit residual DPCM | `hevc.explicitRdpcm` | 10 |
| Rotation of transform-skipped residuals | `hevc.transformSkipRotation` | 10 |
| Transform skip context | `hevc.transformSkipContext` | 9 |
| Transform skip up to 8x8 | `hevc.transformSkip.maxSize=8` | 1 |
| Transform skip up to 16x16 | `hevc.transformSkip.maxSize=16` | 8 |
| Transform skip up to 32x32 | `hevc.transformSkip.maxSize=32` | 1 |
| Cross-component prediction | `hevc.crossComponentPrediction` | 7 |
| Chroma QP offset lists | `hevc.chromaQpOffsetList` + `hevc.cuChromaQpOffset` | 6 |
| Persistent Rice adaptation | `hevc.persistentRice` | 10 |
| Scaled sample adaptive offsets | `hevc.saoOffsetScale` | 2 |
| High precision weighted prediction offsets | `hevc.highPrecisionOffsets` | 2 |
| Intra smoothing disabled | `hevc.intraSmoothingDisabled` | 4 |
| Range extensions tools in lossless coding units | `hevc.implicitRdpcm` + `hevc.lossless` | 5 |
| Rejected by the library | `hevc.unsupported` | 8 |
| Rejected: bit depth above 12 | `hevc.unsupported=bitDepth` | 3 |
| Rejected: luma and chroma bit depths differ | `hevc.unsupported=unequalBitDepth` + `hevc.bitDepthChroma` | 2 |
| Rejected: extended precision processing (no digest) | `hevc.unsupported=extendedPrecision` + `hevc.extendedPrecision` | 2 |
| Rejected: CABAC bypass alignment | `hevc.unsupported=cabacBypassAlignment` + `hevc.cabacBypassAlignment` | 1 |
| Rejected: field sequence | `hevc.unsupported=fieldSeq` + `hevc.fieldSeq` | 1 |
| Encoded by x265 | `hevc.encoder=x265` | 218 |
| Encoded by kvazaar | `hevc.encoder=kvazaar` | 34 |
| Encoded by HM | `hevc.encoder=hm` | 150 |
| Derived from the stream of another vector | `hevc.derived` | 28 |
| Derived: parameter set rewritten by hevc_metadata | `hevc.derived=metadata` | 10 |
| Derived: NAL units removed by filter_units | `hevc.derived=filterUnits` | 1 |
| Derived: stream that starts at a CRA picture | `hevc.derived=startAtIrap` | 3 |
| Derived: CRA pictures retyped as BLA pictures | `hevc.derived=nalType` | 5 |
| Derived: end of sequence before a CRA picture | `hevc.derived=endOfSequence` | 2 |
| Derived: lower temporal sub-layers | `hevc.derived=subLayers` | 3 |
| Derived: two streams one after the other | `hevc.derived=concatenation` | 3 |
| Derived: filler data, end of sequence and end of bitstream after the last picture | `hevc.derived=trailingUnits` | 1 |

Tags and what they hold, beyond the rows above: `hevc.ctbs=CxR` (the picture in coding tree blocks), `hevc.level` (the
level number, `3.1`), `hevc.sliceQp` (the QP of every slice: `26 + init_qp_minus26 + slice_qp_delta`),
`hevc.chromaQpOffset=cb/cr`, `hevc.deblocking.offsets=beta/tc` (the halved offsets a slice uses, when they are not
zero), `hevc.colourDescription=primaries/transfer/matrix`, `hevc.saoOffsetScale=luma/chroma`, `hevc.pcm.bitDepth=luma/chroma`,
`hevc.pcm.size=min-max`, `hevc.slices=N` and `hevc.dependentSlices=N` (the largest number of slices, and of slice
segments, of a picture), `hevc.refs` (the numbers of active reference pictures of the lists of the P and B slices),
`hevc.reorder` (`sps_max_num_reorder_pics`), `hevc.sps=N` and `hevc.pps=N` (parameter sets of N different contents),
`hevc.sei` (the SEI payloads by name), `hevc.frameSizes=WxH,...`.

## Using the corpus in tests

```csharp
public static TheoryData<string> Fixtures => [.. GoldenCorpus.Default.GetIds(format: "png", kind: FixtureKinds.Valid)];

[Theory, MemberData(nameof(Fixtures))]
public void Decode(string id)
{
    var fixture = GoldenCorpus.Default.Get(id);
    using var image = Image.Load(fixture.InputPath);
    GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true)); // Image -> DecodedImageSnapshot
}
```

- `TestHarness.Adapters.ImageSnapshots` copies an `Image` into the comparison model through the public API only:
  `Capture` (untyped `ProcessPixelBytes` rows), `CaptureTyped` (typed `ProcessPixelRows`, components read from the pixel
  fields), `CaptureFrameCopy`/`CaptureFrameDataCopy` (strided `CopyPixelBytesTo`/`CopyPixelDataTo`, padding checked), and
  `CaptureMetadata`. Frames are captured in the canonical layout of their pixel format: `rgba8` (`Rgba32`, and `Bgra32`
  reordered), `rgb8`, `rgba16le`, `gray8`, `gray16le`. `TestHarness.Adapters.RawImport` lays references out for the copied
  raw imports (padded rows, BGRA order, native-endian 16-bit samples). Fixtures whose default representation is `Rgb24`
  carry generator `rgb8` references (PNG decoding); for other fixtures the image-model tests derive `rgb8` from opaque
  `rgba8` references by dropping the alpha byte (`RawImport.DropOpaqueAlpha`, a pure reorder).
- Provide `DecodedImageSnapshot.Metadata` (`DecodedMetadataSnapshot`: resolution, ICC/EXIF/XMP bytes, text entries) so
  `GoldenAssert.ImageMatches` also compares metadata exactly (dpi values, payload hashes, text order). Raw pixel files
  carry no metadata.
- Decoded rows are copied into `RawPixelBuffer`s with `RawPixelBufferBuilder` (`SetRow` for 8-bit rows, `SetRow16` for
  native-endian `Rgba64`/`Gray16` samples) and compared directly — never by encoding our output to PNG/BMP.
- `GoldenAssert.FrameMatches` / `PosterMatches` / `FrameBytesMatch` compare single frames; `GoldenAssert.ImageMatches`
  checks structure, timing, loops, orientation, pixel format and every frame; `GoldenAssert.FailsAsExpected[Async]`
  checks error fixtures (exception type, `Format`, `Feature`, `Kind`). Decode error fixtures with
  `fixture.Entry.DecodeOptions`.
- Failures report fixture, frame, coordinates, channel, expected/actual samples (16-bit in decimal and hexadecimal),
  violation count, maximum and mean error, and hints (channel swap, flips, 90-degree rotations, alpha loss, byte-swapped
  or 8-bit-reduced 16-bit samples, hidden colors). Expected/actual/difference PNG previews and a full numeric report are
  written to `MEZIANTOU_FRAMEWORK_IMAGING_TEST_ARTIFACTS` (default `<test output>/TestArtifacts/<fixture id>`; disable with
  `MEZIANTOU_FRAMEWORK_IMAGING_TEST_PREVIEWS=false`). Previews are for diagnosis only.
- Add a layout the test needs to the generator instead of converting references in tests.
- Processing tests (`Processing.Tests/Conformance/ProcessingGoldenTests`) reuse the decode references as inputs: they import every
  frame and poster, apply crop/rotate/flip/auto-orient, and compare exactly with the reference transformed by the
  harness (`RawPixelBuffer.Crop`, `FlipHorizontal`/`FlipVertical`, `Rotate180`, `Rotate90Clockwise`/`CounterClockwise`,
  `Transpose`, `Transverse`: pure index permutations checked against literals in `PixelBufferTests`). No processed
  reference files are stored; the expected output is never produced by the library.
- Auto-crop tests (`Processing.Tests/Conformance/AutoCropGoldenTests`) import every frame and poster as is and wrapped in a
  synthetic border (`RawPixelBuffer.Extend`, checked against literals in `PixelBufferTests`), and compare the detected
  box, the background and the cropped or enlarged pixels exactly with `TestHarness/AutoCrop/ReferenceAutoCrop`, an
  independent implementation of the auto-crop contract (plain loops over every pixel, linear color lists, exact
  `BigInteger` weights). No processed reference files are stored.
- Resize tests (`Processing.Tests/Conformance/ResizeGoldenTests`) also import every frame and poster and compare the resized pixels
  with `TestHarness/Resampling/ReferenceResampler`, an independent high-precision implementation of the resize contract
  (exact rational geometry, decimal kernels and
  sums). Generic tool resize output (FFmpeg, ImageMagick) is not an oracle: their pixel-center, edge, alpha and rounding
  conventions differ. No resized reference files are stored.
- Convolution tests (`Processing.Tests/Conformance/ConvolutionGoldenTests`) do the same with
  `TestHarness/Convolution/ReferenceConvolver`, an independent implementation of the convolution contract:
  a direct two-dimensional decimal sum per output
  pixel over a copy of the source, for every edge mode, alpha mode and working space, with contiguous and segmented
  storages. No convolved reference files are stored.
- Run inputs through every input variant with `TestHarness.Adapters.InputVariants` (span, path, misleading extension,
  seekable/non-seekable/short-read/nonzero-position streams, async path and stream; the ownership contract is checked after
  each call) and build options from `decodeOptions` with `FixtureOptions`. `IdentifyConformanceTests` and
  `EagerLoadConformanceTests` already cover every fixture: `Tests/Conformance/DecoderAvailability` lists the formats whose
  decoder exists, which switches the corpus loads of that format from the expected `NotImplementedException` to golden
  comparisons (every format of the corpus is enabled). A new codec registers its decoder there and classifies any new
  error fixture in `ErrorFixtureStages`: header defects (found by a header identification), structure defects (found by a
  full scan) or pixel defects (inside compressed pixel data of a valid container: only decoding finds them, a full scan
  succeeds; limits that only decoding charges, such as `MaxTotalPixels`, are classified with them).
- APNG processing (`Processing.Tests/Conformance/ApngProcessingConformanceTests`) decodes every corpus APNG with the library and
  crops, rotates, flips, resizes, convolves and reorders it: frames and posters keep the canvas size, pixel format,
  identities and durations, and their pixels equal the references transformed by the harness (`RawPixelBuffer`,
  `ReferenceResampler`, `ReferenceConvolver`).

## Regenerating the corpus

Regeneration is an explicit, reviewed operation; the tests never run it and expected results are never derived from or
overwritten automatically by the library under test. Run the committed generator with the pinned tools, review the diff of
the manifest (hashes, tool versions, settings, cross-check results) and of the references, review the
[coverage matrix](#fixture-coverage-matrix), and explain the change in the pull request.

```shell
dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- golden          # check: regenerates in a temporary directory and diffs
dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- golden --write  # replace the corpus, then review the diff
dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- verify          # re-decode the committed corpus with the pinned prebuilt tools
```

`verify` is read-only and uses the ffmpeg, `dwebp`, `anim_dump`, `avifenc` and `avifdec` of the meziantou/prebuilt release pinned by the
`Meziantou.Prebuilt` package, like the interop tests (downloaded on first use, whatever their versions compared with the manifest; and, for the HEVC vectors, `dec265` and `TAppDecoder`): it re-decodes every valid non-JPEG
fixture whose generation-time FFmpeg cross-check agreed, and every WebP fixture with `dwebp` and `anim_dump`, and compares
with the committed references. It also checks the [AV1 conformance vectors](#av1-conformance-vectors): the SHA-256 of every
stream, and the digest of every plane of every output frame, re-derived with each of the two decoders (`libdav1d`,
`libaom-av1`) the FFmpeg build has; a decoder the build lacks is reported and skipped. And the
[HEVC conformance vectors](#hevc-conformance-vectors): the SHA-256 of every stream, and the digest of every plane of every
output picture, re-derived with each of the decoders that agreed when the vector was generated (FFmpeg's `hevc` decoder,
`dec265`, `TAppDecoder`). The references of the valid AVIF
fixtures are re-derived from their committed inputs the way the generator does (`ReferenceHeif`, both decoders,
`ReferenceYuvConverter`) and must be identical to the committed buffers. Run it before regenerating with a new
tool version to see what changes; the interop CI job runs it on every build to detect reference drift.

- The generators are the subcommands of one .NET console app (`tools/Meziantou.Framework.Imaging.CorpusGenerator`) and
  never use the library under test.
- A generator writes a whole corpus to a temporary directory: its own entries and files, and a copy of the files the
  other sections of the committed manifest reference. `--write` then removes every committed file that is not in that
  directory. The files each section references are listed in `Infrastructure/CorpusDriver.Files.cs`: a section that
  gains a kind of file (the `renders` of a font, for instance) must be added there, or the next `--write` of another
  generator deletes those files. A generator stops before comparing or writing when its manifest describes a file its
  output does not have, or when its output has a file these lists do not name, and `CorpusManifestTests` checks the
  lists against the committed corpus.
- Requirements of `golden`: macOS (Apple ImageIO `sips`: JPEG cross-checks and PNG arbitration; `swift` from the Xcode
  command line tools runs `imageio_frames.swift` for the APNG cross-checks), the .NET SDK of `global.json` (the generator
  targets .NET 10 and has no dependency on the library), FFmpeg, libjpeg-turbo `cjpeg`/`djpeg`/`jpegtran` and LittleCMS
  `jpgicc`/`transicc` (the four-component JPEG fixtures) at the versions pinned in `GoldenTools.PinnedVersions`
  (currently FFmpeg 9.0.2, libjpeg-turbo 3.2.0, LittleCMS 2.19, sips-316), and the ISO JPEG reference codec (the
  arithmetic-coded and 12-bit JPEG cross-checks), downloaded from the meziantou/prebuilt release pinned by the
  `Meziantou.Prebuilt` package (`GoldenTools.PinnedIsoJpeg`, currently 1.72+702114c). Other versions require
  `--accept-tool-versions` and a review of the changed inputs/references. `--output <directory>` keeps the generated
  corpus for inspection.
- The generator records tool versions, command lines, pattern parameters and seeds in the manifest, parses every
  encoded input to record its actual features, checks FFmpeg outputs (interlace, frame counts, encoded delays and loop
  fields) and the byte counts of every decoded buffer, and cross-checks references with independent decoders.
- FFmpeg safeguards: explicit raw formats (`-pix_fmt rgba` / `rgba64le` / `gray` / `gray16le`), `-noautorotate`, no
  scaling, `-ignore_loop 1` and `-fps_mode passthrough` (one pass), explicit swscale flags; timestamps are never used for
  durations or loops.
- Hand-authored PNG/APNG use stored deflate blocks so their bytes do not depend on a zlib version; GIF inputs use the
  generator's LZW encoder. Expected animation frames are written literally (character grids), computed by hand from the
  specifications, not produced by a compositor; translucent APNG OVER results are named grid entries computed by the
  generator's `over()` transcription of the library
  contract (exact rationals, asserted against literal values).
- WebP fixtures have their own generator, `webp` (any OS; libwebp `cwebp`/`dwebp`/`webpmux`/`anim_dump` 1.3.2 and FFmpeg
  6.1.1-3ubuntu5 pinned in `WebPCorpus.cs`); it replaces only the WebP entries and files of the manifest, and the
  `golden` generator keeps them. The QOI (`qoi`), BMP (`bmp`), TGA (`tga`) and Netpbm (`pnm`) generators
  follow the same convention:

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- webp          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- webp --write  # replace the WebP fixtures, then review the diff
  ```
- `qoi` (`QoiCorpus.cs`; any OS, FFmpeg and a C compiler) compiles the qoi.h reference implementation (MIT, never
  committed: pass the downloaded file with `--qoi-header <path of qoi.h>`; its SHA-256 is pinned in the generator) into a
  small raw <-> QOI driver used as a tool. Its outputs are contributed under `CC0-1.0` like other tool output.
- `bmp` (`BmpCorpus.cs`), `tga` (`TgaCorpus.cs`) and `pnm` (`PnmCorpus.cs`) (any OS, FFmpeg only) each replace only the
  entries and files of their format; they share their driver, helpers and pinned FFmpeg version
  (`Common/HandAssembledCorpus.cs`).
- `avif` (`AvifCorpus.cs`, recipes in `AvifCorpus.Vectors.cs`, header parsing in `AvifCorpus.Trace.cs`, hand-authored
  inter frames in `Av1HandAuthoredStream.cs`, layered streams in `AvifCorpus.Layered.cs`) generates the
  [AV1 conformance vectors](#av1-conformance-vectors): it replaces only the `av1Vectors` section of the manifest and the
  files of `av1/`, and every other generator keeps them. It needs no installed tool: FFmpeg and ffprobe are downloaded
  from the meziantou/prebuilt release pinned by the `Meziantou.Prebuilt` package, and the versions of the libraries they
  are linked with are pinned in `AvifCorpus.PinnedTools` (currently FFmpeg 9.0.2, libaom 3.15.1, SVT-AV1 4.2.0,
  libdav1d 1.5.4); the layered vectors also need the pinned `avifenc` of `avif-container`. The committed vectors were generated on macOS arm64; an encoder may make other decisions on another
  platform, in which case the check mode reports the differences.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif          # check: re-encodes, re-decodes with both decoders and diffs
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif --write  # replace the AV1 vectors, then review the diff
  ```

  To add a vector, add a recipe to `AvifCorpus.Vectors` with the feature tags it must signal (`Requires`; `Inter = true`
  when it has frames other than shown key frames), run the generator with `--write`, and update the
  [AV1 vector features](#av1-vector-features) table. `Av1ToolCoverageTests` tells whether the vectors still reach every
  coding tool. A patched vector is `Patched(vectors, source, name, filter)`, a hand-authored one
  `HandAuthored(recipe, format, pixelFormat, size)`: see [Vectors an encoder did not write alone](#vectors-an-encoder-did-not-write-alone).
  A layered vector is a `LayeredVector` of `AvifCorpus.LayeredVectors` (an `avifenc` command per temporal unit), and its
  truncations `FirstLayers(vector, count)`: see [Layered vectors](#layered-vectors).
- `hevc` (`HevcCorpus.cs`: the driver, the three decoders and the agreement rule; `HevcCorpus.Trace.cs`: header parsing,
  output order and feature tags; `HevcCorpus.Vectors.cs`: the recipe model, the sources and the encoder helpers;
  `HevcCorpus.X265.cs`, `HevcCorpus.Kvazaar.cs`, `HevcCorpus.Hm.cs`: the recipes of each encoder; `HevcCorpus.Derived.cs`
  and `HevcCorpus.Stream.cs`: the derived vectors and the byte stream) generates the
  [HEVC conformance vectors](#hevc-conformance-vectors): it replaces only the `hevcVectors` section of the manifest and the
  files of `hevc/`, and every other generator keeps them. It needs no installed tool: FFmpeg, ffprobe, `dec265`,
  `TAppEncoder` and `TAppDecoder` are downloaded from the meziantou/prebuilt release pinned by the `Meziantou.Prebuilt`
  package, and the versions are pinned in `HevcCorpus.PinnedTools` (currently FFmpeg 9.0.2 with x265 4.2 and kvazaar
  2.3.2, libde265 1.1.3, HM 18.0). The committed vectors were generated on macOS arm64; an encoder may make other
  decisions on another platform, in which case the check mode reports the differences.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- hevc          # check: re-encodes, re-decodes with the three decoders and diffs
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- hevc --write  # replace the HEVC vectors, then review the diff
  ```

  To add a vector, add a recipe to the list of its encoder (`X265Intra`, `X265`, `Kvazaar`, `HmIntra`, `Hm`) with the
  feature tags it must signal, run the generator with `--write`, and update the
  [HEVC vector features](#hevc-vector-features) table; a new feature key also goes to
  `FixtureManifestValidator.AllowedHevcFeatureKeys`. When the generation reports that a decoder does not agree with the two
  others, read the message before anything else: either the recipe hits a known behavior of that decoder
  (`.Without(decoder, reason)`, see [the table](#hevc-decoders-that-agree-and-decoders-that-are-left-out)), or the stream
  is not one to commit. A derived vector is `Derived(vectors, source, name, derivation, frames)`: see
  [HEVC vectors an encoder did not write alone](#hevc-vectors-an-encoder-did-not-write-alone).
- `jxl` (`JxlCorpus.cs`: tools, references, cross-checks and manifest entries; `JxlCorpus.Fixtures.cs`: the recipes)
  generates the JPEG XL fixtures; it replaces only the entries of format `jxl` and their files (`jxl/`, `invalid/jxl/`).
  It needs no installed tool: `cjxl`, `jxl_from_tree`, `djxl` (libjxl, pinned in `PinnedLibjxl`, currently 0.12.0) and
  `jxl-oxide` (`PinnedJxlOxide`, currently 0.12.6) come from the meziantou/prebuilt release of the `Meziantou.Prebuilt`
  package. A lossless fixture expects the pattern given to the encoder (or the values a tree sets) and both decoders
  must give it exactly; a lossy fixture expects the samples djxl decodes, rounded to the nearest integer, and records
  how jxl-oxide differs from them; an invalid fixture is rejected by both decoders. The fields of the files and their
  feature tags are read by `TestHarness/JpegXl/ReferenceJpegXl` (linked into the generator project, no dependency on
  the library), which does not read entropy-coded data: the ICC profiles of the ICC fixtures are profiles of
  `icc/profiles/`, and the play count of `jxl/anim-alpha-three-plays` is written by rewriting the `num_loops` field of
  the image header, since no tool of libjxl writes one. The encoders make platform-dependent decisions: the committed
  fixtures were generated on macOS arm64.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jxl          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jxl --write  # replace the JPEG XL fixtures, then review the diff
  ```

  After a change of the lossy decoder, run `JpegXlDecoderConformanceTests.ToleranceIsTheMeasuredDisagreementWithTheReference`
  and copy the measurements it prints into `LibraryJxlMeasurements`, then regenerate.
- `jp2` (`Jp2Corpus.cs`: tools, references, cross-checks and manifest entries; `Jp2Corpus.Fixtures.cs`: the recipes;
  `Jp2Corpus.Editor.cs`: the codestreams that are rearranged; `Jp2Corpus.HighThroughput.cs`: the hand-authored coding
  passes) generates the JPEG 2000 fixtures; it replaces only the entries of format `jp2` and their files (`jp2/`,
  `invalid/jp2/`). It needs no installed tool: `opj_compress` and `opj_decompress` (OpenJPEG, pinned in
  `PinnedOpenJpeg`, currently 2.5.4), `ojph_compress` and `ojph_expand` (OpenJPH, `PinnedOpenJph`, currently 0.32.0) and
  FFmpeg come from the meziantou/prebuilt release of the `Meziantou.Prebuilt` package. A fixture of the reversible
  transformations expects the pattern given to the encoder; a fixture of the irreversible ones, and a codestream cut by a
  rate, expect the samples opj_decompress decodes, and record how the native decoder of FFmpeg (and ojph_expand, for the
  high-throughput block coder) differs from them. The cross-checks compare the channels each decoder returns, at the
  depth of the file, scaled the way the library scales them. A decoder that does not read a fixture as the
  Recommendation says is recorded with the reason, excluded from the tolerance, and the generator fails when it starts
  to agree: OpenJPEG refuses placeholder passes and does not read an HT refinement segment from another packet than
  its HT cleanup segment, OpenJPH reads one layer, FFmpeg refuses the HT cleanup magnitudes that use every bit-plane,
  a palette of four columns, and takes the last of four components as the opacity. The fields of the files and their
  feature tags are read by `TestHarness/Jpeg2000/ReferenceJpeg2000` (linked into the generator project, no dependency
  on the library). Three kinds of fixtures are not the output of a tool alone:
  - files assembled around a codestream of opj_compress with `TestHarness/Jpeg2000/Jp2FileBuilder` (palettes, channel
    definitions, sYCC, ICC profiles of `icc/profiles/`, resolution, UUID boxes, box lengths);
  - codestreams whose marker segments and packets are moved (`Jp2Corpus.Editor.cs`): packet headers packed in PPM and PPT
    marker segments (the packets are found by the SOP marker segments and EPH markers the encoder was asked to write),
    interleaved tile-parts, COC and QCC marker segments, a coding style moved to the tile-part headers;
  - codestreams of the high-throughput block coder with coding passes no pinned encoder writes
    (`Jp2Corpus.HighThroughput.cs`): one code-block without wavelet transformation, whose HT cleanup segment comes from
    ojph_compress and whose packet headers and HT refinement segment are written from Rec. ITU-T T.814; their expected
    pixels are the coefficients the generator codes.

  ojph_compress loses the sample 0 of an 8-bit image without decomposition level (its coefficient, -128, does not fit
  the bit-planes it declares): the hand-authored codestreams keep their coefficients below 64.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jp2          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- jp2 --write  # replace the JPEG 2000 fixtures, then review the diff
  ```

  After a change of the irreversible path of the decoder, run
  `Jpeg2000DecoderConformanceTests.ToleranceIsTheMeasuredDisagreementWithTheReference` and copy the measurements it
  prints into `LibraryJp2Measurements`, then regenerate.
- `exr` (`ExrCorpus.cs`) generates the OpenEXR fixtures; it replaces only the entries of format `exr` and their files
  (`exr/`, `invalid/exr/`, `limit/exr/`). The files are written by `exr-from-raw` from raw samples and a description of
  their header, and decoded by `exr-to-raw` (both built with OpenEXR, pinned in `PinnedOpenExr`, currently 3.5.2, by the
  meziantou/prebuilt release of the `Meziantou.Prebuilt` package). The reference is what OpenEXR decodes, bit for bit,
  lossy compressions included, presented by the rules of the library (the display window is the image, a missing color
  is 0 and a missing alpha 1, Y gives the three colors, the colors are divided by alpha unless it is 0). It is
  cross-checked with the EXR decoder of FFmpeg, on the samples the file stores, wherever FFmpeg reads the file as it is
  (one type of samples, R, G and B with or without A, or Y, equal windows), and with the pattern given to `exr-from-raw`
  for the channels a compression keeps exactly. FFmpeg 9.0.2 does not apply the conversion of the pLinear channels of
  B44 (`exr/b44-plinear` records it). An invalid fixture is a byte edit that OpenEXR rejects too. Four valid and three
  unsupported fixtures are sample images of the OpenEXR project (BSD-3-Clause), checked by their SHA-256 at the pinned
  commit `OpenExrImagesCommit` of https://github.com/AcademySoftwareFoundation/openexr-images; the generator never
  downloads them:

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- exr --openexr-images <checkout>          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- exr --openexr-images <checkout> --write  # replace the OpenEXR fixtures, then review the diff
  ```

- `heic-container` (`HeicCorpus.Container.cs`: tools, references, cross-check and manifest entries;
  `HeicCorpus.ContainerFixtures.cs`: the valid recipes; `HeicCorpus.ContainerErrors.cs`: the malformed, unsupported and
  over-limit ones; `HevcCorpus.Reference.cs`: the three decoders and x265 for the streams of a container) generates the
  HEIC fixtures of `heic/` and `invalid/heic/` (the `limit/heic` entries reuse valid inputs): it replaces only the entries
  of format `heic`, and every other generator keeps them. It needs no installed tool: `heif-enc`, `heif-dec`, x265,
  FFmpeg, ffprobe, `dec265` and `TAppDecoder` are downloaded from the meziantou/prebuilt release pinned by the
  `Meziantou.Prebuilt` package; libheif is pinned in `HeicCorpus.PinnedLibheif` (currently 1.23.6) and the others in
  `HevcCorpus.PinnedTools`. The committed fixtures were generated on macOS arm64.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- heic-container          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- heic-container --write  # replace the HEIC fixtures, then review the diff
  ```

  To add a fixture, add a recipe with the feature tags it must have (`requires`), run the generator with `--write`, run
  `HeicDecoderConformanceTests.ToleranceIsTheMeasuredDisagreementWithTheReference` and copy the line it prints for a new
  matrixed fixture into `LibraryHeicMeasurements`, regenerate, classify a new error fixture in
  `Tests.Shared/Suites/Decode/ErrorFixtureStages.cs`, and update the [Features](#features) rows.
- `avif-container` (`AvifCorpus.Container.cs`: tools, references, cross-checks and manifest entries;
  `AvifCorpus.ContainerFixtures.cs`: the valid recipes; `AvifCorpus.ContainerLayered.cs`: the layered images;
  `AvifCorpus.ContainerErrors.cs`: the error recipes) generates the
  AVIF fixtures; it replaces only the entries of format `avif` and their files (`avif/`, `invalid/avif/`, `limit/avif/`)
  and keeps the AV1 vectors, as `avif` keeps these fixtures. It needs no installed tool: it uses the pinned FFmpeg of
  `avif` and the `avifenc` and `avifdec` of the same meziantou/prebuilt release (libavif pinned in `PinnedLibavif`,
  currently 1.4.2, whose `avifenc` is linked with aom 3.14.1 and `avifdec` with dav1d 1.5.3). It compiles the
  files of `TestHarness/Avif` and `TestHarness/Heif` (linked into the generator project), which have no dependency on the library. Gzip-compressed
  references are written by the .NET runtime of the generator, like the encoders an AVIF input depends on the platform:
  the committed fixtures were generated on macOS arm64.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif-container          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif-container --write  # replace the AVIF fixtures, then review the diff
  ```

  A valid recipe lists the feature tags it exists for; they are read back from the bytes. After adding a matrixed
  fixture, run `AvifDecoderConformanceTests`: it prints the measured disagreement to record in `LibraryAvifMeasurements`.
- `icc` (`IccCorpus.cs`; any OS, `transicc` of LittleCMS 2.19 pinned in the generator) writes the `colorProfiles` and
  `colorTransforms` sections and the `icc/` directory, and keeps every image fixture; the image generators keep these
  sections. The synthetic profiles are built by the generator. The external profiles are never downloaded by it: pass the `profiles` directory of a checkout of
  Compact-ICC-Profiles at the commit pinned in the generator with `--icc-profiles <directory>`; each file is checked
  against its pinned SHA-256. The tolerances are literals of the generator, measured with `ColorConversionGoldenTests`.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- icc --icc-profiles <directory>          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- icc --icc-profiles <directory> --write  # replace, then review the diff
  ```
- `icc-builtin` (`IccCorpus.BuiltInProfile.cs`; any OS, no tool) transcribes the committed
  `icc/profiles/CGATS001Compat-v2-micro.icc`, checked against its pinned SHA-256, into the source of the CMYK profile the
  library provides (`src/Meziantou.Framework.Imaging/Internals/Color/BuiltInCmykProfile.g.cs`): the bytes of third-party
  data are never typed by hand.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- icc-builtin          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- icc-builtin --write  # replace, then review the diff
  ```
- `fonts` (`FontCorpus.cs`; any OS; the HarfBuzz utilities `hb-info`, `hb-shape`, `hb-vector`, `hb-subset` and the
  woff2 tools of the pinned meziantou/prebuilt release) writes the `fonts` section and the `fonts/` directory, and
  keeps the rest of the corpus unchanged. It needs the third-party font files listed in `FontCorpus.cs`, downloaded
  from their pinned sources into one directory given by `--fonts <directory>`; each file is checked against its pinned
  SHA-256 before it is used. The same directory holds the font suite `aots`: `test/shape/data/aots` of a checkout of
  HarfBuzz at the commit pinned in the generator. The generator also records the shaping vectors. The WOFF files are compressed by the zlib of the runtime that runs the generator, which
  the manifest records: they are reproducible with that runtime.

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- fonts --fonts <directory>          # check
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- fonts --fonts <directory> --write  # replace, then review the diff
  ```
- To add fixtures for a feature: add a builder (or a case) to the generator, run it with `--write`, review the diff and
  the cross-check results in the manifest, update the [coverage matrix](#fixture-coverage-matrix), and reference the
  fixture ids from the feature's tests.

## Licensing and provenance

### Package license

`Meziantou.Framework.Imaging` is released under the MIT license ([LICENSE.txt](../../LICENSE.txt), package license
expression `MIT`). The package has no runtime dependency; any future dependency requires a license review and an update of
this section.

### Source code

- All code is original work written from the format specifications (PNG/APNG: W3C PNG specification; GIF: GIF89a
  specification; JPEG: ITU-T T.81 and JFIF; WebP: RFC 9649 (container, VP8L lossless bitstream, alpha) and RFC 6386 (VP8);
  QOI: the QOI specification 1.0 (qoiformat.org); BMP: the Microsoft Windows GDI `BITMAPFILEHEADER`/`BITMAPINFOHEADER`
  documentation (including its description of `BI_RLE8` and `BI_RLE4`) and the OS/2 Presentation Manager
  `BITMAPCOREHEADER`/`BITMAPINFOHEADER2` documentation; TGA: the Truevision TGA File Format Specification 2.0; PBM/PGM/PPM/PAM: the Netpbm format
  specifications; AVIF: ISO/IEC 14496-12 (boxes), ISO/IEC 23008-12 (image items), the AV1 Image File Format, the AV1
  Codec ISO Media File Format Binding, the AV1 Bitstream & Decoding Process Specification and ITU-T H.273 (color
  conversion); EXIF/TIFF; ICC; future formats: their official specifications).
- Do **not** copy, translate or closely paraphrase code from other image libraries or codecs, whatever their license
  (System.Drawing, libpng, zlib, libjpeg/libjpeg-turbo, giflib, libwebp, qoi.h, stb_image, Pillow, ImageMagick, FFmpeg,
  ...). Specification pseudo-code and published numeric tables (e.g. JPEG Annex K quantization and
  Huffman tables, CRC polynomials) may be used with a citation.
- Compression uses the .NET base class library (`System.IO.Compression.ZLibStream`); no third-party compression code.
- Tools and generators committed under `tests/`, `tools/` or `eng/` follow the same rules and are MIT-licensed like the
  repository.

### Fixture provenance

Every file of this folder (except `README.md`, `manifest.json`, `manifest.schema.json` and `LICENSES/`) must be listed in
`manifest.json` with:

| Field | Requirement |
| --- | --- |
| `provenance.origin` | `hand-authored` (written by a contributor, byte by byte or with a committed script), `generated` (produced by a committed generator script from documented parameters/seeds), or `external` (third-party file) |
| `provenance.license` | An SPDX identifier from the allow-list below |
| `provenance.source` | Required for `external`: URL or citation of the original, including version/date |
| `provenance.generator` | Required for `generated`: script path relative to the repository root and its function |
| `provenance.tools` | Required for `generated`: pinned tools with exact versions used to produce inputs or references (e.g. `ffmpeg 9.0.2`) |
| `provenance.parameters` / `provenance.commands` | Pattern parameters and seeds; exact external-tool command lines |
| `reference` | For valid fixtures: `hand-computed` or `decoded` (decoder, actual backend, command) and independent cross-checks |
| `sha256` | Hash of each file (inputs and raw references); the conformance tests fail on any mismatch |

The rest of the manifest model (expected frames, raw-buffer layouts, timing/loop/metadata expectations, comparison
policies, expected errors) is described in the sections above.

The manifest is validated by `FixtureManifestValidator` (run by `CorpusManifestTests` and by `GoldenCorpus` before any
comparison): unknown or missing licenses, missing provenance, hash mismatches, missing files, path traversal, unreferenced
(orphan) files, inconsistent raw-buffer layouts or lengths, frame/timing/loop inconsistencies, unjustified or
undiscriminating tolerances and error fixtures with expected pixels fail the build's tests.

### Allowed fixture licenses

`CC0-1.0`, `MIT`, `Unlicense`, `CC-BY-4.0`, `Libpng`, `Zlib`, `Apache-2.0`, `Unicode-3.0`, `BSD-3-Clause` (the sample
images of the OpenEXR project, `LICENSES/BSD-3-Clause-OpenEXR.txt`), and `OFL-1.1` for font files only.

The SIL Open Font License is accepted for the entries of the `fonts` section and for nothing else, because open fonts
for most scripts exist under no other license. Its conditions are met as follows, and the validator enforces the ones
it can check:

- A font stays under the OFL, with its copyright notice (in its `name` table) and the license text
  (`LICENSES/OFL-1.1.txt`). It is test data only: it is never embedded in a package or in a test assembly.
- A font with a Reserved Font Name is stored exactly as published (`modification: none`): no subset, no other
  container. A font without one may be subset by the pinned subsetter or stored in another container.
- A third-party font always names its source, with the commit or the version it was taken from.

`Apache-2.0` and `Unicode-3.0` are the licenses of the test suites and test fonts published for font implementations;
their texts are in `LICENSES/`. The AFM files of the 14 standard fonts of Adobe are not fixtures: Meziantou.Framework.Fonts
embeds them (`src/Meziantou.Framework.Fonts/Afm/StandardFonts`), and the tests read them with `AfmFontMetrics`.

Content created by contributors for this repository (hand-authored patterns, outputs of committed generators, and files
produced by running third-party tools on such content) is contributed under `CC0-1.0` unless stated otherwise. Tool
output does not carry the tool's license.

Metadata payloads embedded in fixtures (EXIF blocks, XMP packets, text, and the small ICC v4.3 test profiles built by
`icc_profile` in the generator) are hand-built test data under `CC0-1.0`. Never embed third-party ICC profiles (for
example vendor sRGB profiles) unless their license is on the allow-list. The profiles of `icc/profiles/` are either
third-party files under `CC0-1.0` (Compact-ICC-Profiles), stored unmodified, or synthetic profiles built by the
generator (`synthetic-*`, `CC0-1.0`).

Adding another license (for example the custom permissive terms of PngSuite as `LicenseRef-PngSuite`) requires a
reviewed change that updates the allow-list in **both** `manifest.schema.json` and `FixtureManifestValidator`, and adds the
full license text to `LICENSES/<identifier>.txt` in this folder. Files under copyleft or non-commercial licenses (GPL,
LGPL, CC-BY-SA, CC-BY-NC, ...) or without a clear license must not be committed. Photographs of identifiable people must
not be committed.

### Size

There is no size budget: the corpus is as large as a good coverage needs. Still prefer the smallest input that exercises
a feature (tiny images, few frames): every valid fixture is decoded thousands of times by the conformance, hardening and
fuzz suites, so a large input costs test time long before it costs disk space. Large inputs for benchmarks are generated
at run time and never committed. The manifest entry of an AV1 or an HEVC vector holds a digest per plane and per output
frame, so a long stream is mostly paid for there.

The corpus once had one (a total size, 8 KB per AV1 vector, inter vectors of at most 12 frames and 176x144). It was
removed because it decided what was tested: coding tools that only show on frames of several restoration units or on
whole groups of pictures had no vector, and AVIF features had been moved to unit tests or dropped to save bytes. What
its removal brought is listed with the [AV1 vector features](#av1-vector-features) (63 vectors: 199 in all, 431 KB)
and in the AVIF rows of the [starter corpus](#starter-corpus) (72 valid and 7 error fixtures: 166 in all). What bounds
a fixture now is test time, not bytes: an AV1 vector is decoded three times by the tests, whatever its size, but a
valid fixture goes through every generic suite, and a 192x128 grid alone cost them more than fifty small fixtures.

### Fuzz regressions

Minimized inputs found by the fuzz tests (and hand-authored variants of the same defects) live in
`fuzz/Meziantou.Framework.Imaging.FuzzTests/FuzzRegressions`, apart from the golden corpus: they are
malformed-input regressions, never pixel references. Each file is listed in `FuzzRegressions/regressions.json` with its
SHA-256, `origin` (`fuzz-minimized` or `hand-authored`), an allowed `license` (the minimized inputs derive from corpus
fixtures and are `CC0-1.0`), how it was found (seed group, base seed, iteration, minimization), the defect and fix, and
the expected outcomes; `FuzzRegressionTests` validates the manifest (hashes, licenses, provenance, missing or orphan files)
and replays every input.

### Interop outputs

Files produced by our encoders during interoperability tests are written to the interop artifacts directory
(`MEZIANTOU_FRAMEWORK_IMAGING_INTEROP_ARTIFACTS`, uploaded by CI for diagnosis) and are never committed as references.
