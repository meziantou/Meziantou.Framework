# Golden corpus

Independent reference data for the conformance and interop tests, extended with each codec.

- `manifest.json` — the versioned manifest (schema version 2: `manifest.schema.json`), **written by the reviewed generator**
  `tools/Meziantou.Framework.Imaging.CorpusGenerator` (a .NET console app: the `golden` generator, `GoldenCorpus.cs`, writes
  the PNG, APNG, GIF and JPEG entries; WebP entries: `webp`, `WebPCorpus.cs`; QOI entries: `qoi`, `QoiCorpus.cs`; BMP entries:
  `bmp`, `BmpCorpus.cs`; TGA entries: `tga`, `TgaCorpus.cs`; Netpbm entries: `pnm`, `PnmCorpus.cs`). Every file of this folder except this README, the manifest, the schema and `LICENSES/` is listed with
  its SHA-256 hash, provenance and license.
- The folder is copied to `<test output>/Fixtures` by the conformance and interop test projects
  (`FixtureRoot.GetDirectory()`). Tests never read fixtures from the source tree, never download anything and never run
  the generator.
- `GoldenCorpus.Default` loads the manifest and validates it completely (`FixtureManifestValidator`) before any pixel
  comparison; `CorpusManifestTests` reports every problem.
- Licensing and provenance rules: [Licensing and provenance](#licensing-and-provenance). Feature coverage:
  [Fixture coverage matrix](#fixture-coverage-matrix).

## Layout

```
tests/Meziantou.Framework.Imaging.Fixtures/
  png/   apng/   gif/   jpeg/   webp/   qoi/   bmp/   tga/   pnm/ encoded inputs and their raw references
  invalid/<format>/                   malformed, unsupported or over-limit inputs (expected errors, no expected pixels)
  LICENSES/                           full texts of non-standard licenses (none yet)
```

- Fixture ids are lowercase paths without extension (`png/gray16-low-bit-gradient`); `format` is the container
  (`png` for PNG and APNG, `gif`, `jpeg`, `webp`, `qoi`, `bmp`, `tga`, `pnm` for every Netpbm variant).
- Kinds: `valid` (expected pixels and metadata), `invalid` (malformed: expected exception), `unsupported` (recognized but
  unsupported feature: `UnsupportedImageFeatureException`), `limit` (decoded with `decodeOptions.limits`:
  `ImageResourceLimitException` with `limitKind`). Error fixtures never carry fabricated expected pixels and are kept
  separate from malformed-input fuzzing.
- `features` lists the encoding features actually present in each input, parsed from the encoded bytes by the generator
  (never trusted from the requested tool flags): e.g. `png.interlace=adam7`, `png.filters=0,1,2,3,4`,
  `apng.poster=separate`, `apng.dispose=background,none,previous`, `gif.disposal=0,1,2,3`, `gif.lzw.deferredClear`,
  `jpeg.sampling=4:2:2`, `jpeg.restartInterval=2`, `webp.bitstream=vp8l`, `webp.vp8l.transform=color-indexing`,
  `webp.blend=alpha`. Use them to select fixtures (`GoldenCorpus.GetIds(feature: ...)`).

## Expected images

`expected` describes the decoded image independently of the pixel bytes: canvas size (stored pixels; EXIF orientation is
never applied), default working `pixelFormat`, `colorModel`, `bitsPerComponent`, `orientation`, `iccProfile`, `animation`
(`totalPlays` normalized, `null` = infinite, plus the raw `encodedLoopValue`), `frameCount`, one entry per **full-canvas
displayed frame** with its exact rational `duration` (`numerator/denominator` seconds, normalized), and the separate
`poster` (APNG default image that is not part of the animation) or `null`. Durations and loop counts come from the
encoded fields or the hand-written instructions, never from FFmpeg timestamps. The validator checks the conventions of
the library: APNG `num_plays` 0 ↔ infinite; GIF NETSCAPE
loop `L` ↔ `L + 1` plays, absent ↔ 1; WebP `ANIM` loop count 0 ↔ infinite, `N` ↔ `N` plays (the WebP container
specification counts plays, not repetitions).

Timing and metadata expectations are parsed by the generator from the encoded bytes of every input:

| Field | Meaning |
| --- | --- |
| `frames[].encodedDelay` | Raw delay field of the frame: APNG `fcTL` `{numerator, denominator}` as encoded (`denominator` 0 means 100) or GIF Graphic Control Extension `{hundredths}` or WebP `ANMF` `{milliseconds}` (24-bit). Required for every frame of an animated APNG/GIF/WebP (a GIF frame without GCE has none and a zero duration). The validator checks `duration` = `numerator/(denominator or 100)`, `hundredths/100` or `milliseconds/1000` exactly. |
| `resolution` | `null` (absent, or aspect ratio only: PNG `pHYs` unit 0, JFIF units 0) or `{horizontalDpi, verticalDpi, encoded: {unit, x, y}}` with `unit` `meter` (PNG `pHYs`, BMP `biXPelsPerMeter`), `inch` or `centimeter` (JFIF); the dpi values are the exact IEEE results of `x × 0.0254`, `x` or `x × 2.54`. |
| `profiles` | `{icc, exif, xmp}`: `null` or `{sha256, length}` of the payload decoders must preserve byte for byte — the uncompressed ICC profile, the EXIF data from the TIFF header (no `Exif\0\0` prefix), the XMP packet (WebP: the `ICCP`, `EXIF` and `XMP ` chunk payloads). `iccProfile` must be `none` exactly when `profiles.icc` is `null`; an orientation other than 1 requires `profiles.exif`. |
| `transferFunction` | Optional: `srgb` (default) or `linear`, the transfer-function label of the decoded samples (`ImageMetadata.TransferFunction`), read from the QOI header colorspace; only QOI inputs may be `linear`. |
| `text` | Text entries in file order: `{keyword, value, languageTag, translatedKeyword}` (PNG `tEXt`/`zTXt`/`iTXt` except the XMP `iTXt`; empty language tags and translated keywords are `null`). GIF comments and JPEG `COM` segments use the keyword `Comment` without language. |

`EncodedFieldInspector` (test harness) reads the same raw fields from the inputs independently of the generator and of the
library (PNG chunks, GIF blocks, JPEG marker segments, WebP RIFF chunks; no pixel decoding).
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

Files are named `<input-name>.<role>.<layout>.raw` where role is `frame-<index>` or `poster`. 16-bit samples are always
little-endian. Every frame and the poster provide the same layouts; 16-bit fixtures always include a 16-bit layout.

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
  mean + 0.01, rounded up to 0.01), which `Tests/Conformance/JpegDecoderConformanceTests` checks against a fresh
  measurement; independent decoders whose differences exceed that policy are recorded as `differs` (with notes) and still
  bound the justified tolerance. Lossy WebP fixtures follow the same scheme: the reference is libwebp `dwebp`'s decoding of
  the input, the policy is the library's measured disagreement with it (`LibraryWebPMeasurements` in `WebPCorpus.cs`,
  checked by `Tests/Conformance/WebPDecoderConformanceTests`), and the independent cross-check is FFmpeg's libavcodec VP8
  decoding to raw `yuv420p` planes converted by the generator with exact BT.601 rationals (FFmpeg's own swscale RGB
  conversion is recorded but excluded from the tolerance: its chroma sample positions differ from centered upsampling).
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
DIB header, bit depth, mask layout, palette, row order and padding; every TGA image type, depth, origin bit, color-map
offset and packet kind; every Netpbm magic number, tuple type, `MAXVAL` and comment placement), plus files produced by
FFmpeg's independent BMP, TGA, PPM and PAM encoders from hand-defined patterns (the three formats are lossless, so the
pattern is the reference). Every input is decoded by a strict transcription of its specification in the generator and
cross-checked with FFmpeg; the comparison policy stays `exact`, and the two disagreements FFmpeg has with the
specifications are recorded as cross-checks with their measured error instead of being hidden:

- FFmpeg expands 5-bit channels of 16-bit BMP and TGA samples by replicating their high bits, where the references use the
  exact ratio `round(value * 255 / 31)` (at most one unit per sample);
- FFmpeg reads the fourth byte of a 32-bit BMP `BI_RGB` payload and of a 32-bit TGA whose descriptor declares no alpha bit
  as alpha, where the specifications leave it unspecified (every color sample agrees exactly).

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
| JPEG | baseline 4:2:0 / 4:2:2 (odd width, restart markers) / 4:4:4, progressive 4:2:0 with partial MCUs, grayscale odd size, EXIF orientation 6 (metadata only), JFIF density 300x150 dpi + XMP + COM; for the sequential decoder: high-frequency content (`pattern_detail_*`) with odd 4:2:0 and one restart interval per MCU row and optimized Huffman tables, 4:4:0 (vertical triangle filter), 4:1:1 (replication), 4:4:4 detail, grayscale with 3-block restart intervals, optimized tables and a gray ICC profile, extended sequential SOF1 with 16-bit DQT (quality 3), Adobe RGB (transform 0, `R`/`G`/`B` identifiers), multi-scan sequential frames (Cb/Y/Cr in separate non-interleaved scans with restarts; Y then interleaved CbCr), RGB ICC in three out-of-order APP2 chunks + big-endian EXIF orientation 3 + JFIF dots per centimeter; tiny frames whose chroma planes are at most 2 samples wide (4:2:0 4x9 and 2x13, 4:2:2 3x5, progressive 4:2:0 4x6: replicated like libjpeg-turbo); for the progressive decoder: the default libjpeg-turbo progression (DC/AC successive approximation, luma band split) at 4:4:4 detail, odd 4:2:2 with 2-MCU restarts, gray smaller than two blocks and Adobe RGB, and `-scans` scripts (`PROGRESSIVE_*_SCRIPT`): DC refined from bit 3 and a luma band refined from bit 4 at odd 4:2:0 with per-row restarts, spectral selection only at 4:4:0, separate DC scans with different point transforms at 4:1:1 with 3-block restarts, a gray refinement script with 3-block restarts, and a partial progression (high frequencies never refined to bit 0 or never sent) — encoded by libjpeg-turbo `cjpeg`. Every color JPEG also has an `rgb8` reference for `Rgb24` decoding |
| JPEG errors | Unsupported (header): arithmetic coding, lossless (SOF3), 12-bit precision, CMYK (four components), non-integral sampling ratios; structure: interleaved scan with 18 blocks per MCU; decoding only (valid containers): truncated entropy-coded data, an unassigned Huffman code, a zero quantization value, a truncated DQT, an undefined Huffman table, an oversubscribed Huffman table, a restart marker out of sequence, a missing last restart marker, a frame whose third component has no scan; progressive: structure (scan headers, found by a full identification): an AC scan with Ss > Se, a refinement scan moved before the first scan of its band; decoding only: a truncated scan followed by the remaining scans, an invalid code at the start of an AC refinement, a frame whose Cr component has no scan; limit: a frame header declaring 20000x20000 pixels (`MaxFramePixels`, before any allocation) |
| WebP | lossless (VP8L): 1x1, odd width, RGBA corner markers, hidden colors under alpha 0 (`-exact`), a detailed pattern with the transforms `cwebp -z 9` chooses (`lossless-detail-transforms`), a small palette with pixel bundling and a 256-color palette, extended layout with ICC/EXIF/XMP, an opaque extended file with an unknown chunk; lossy (VP8): normal and simple loop filters with segmentation, no filter at high quality, odd and tiny (3x2) sizes, raw `ALPH` and lossless-compressed `ALPH` with horizontal filtering, metadata; animations: blend/dispose combinations with partial frames and a finite loop, a partial first frame with infinite loop, mixed lossy and lossless frames, a single-frame animation |
| BMP | 24-bit bottom-up and top-down with row padding, 32-bit `BITMAPV4HEADER` with an alpha mask and the same bytes as 32-bit `BI_RGB` (unspecified fourth byte), 16-bit implicit 5-5-5 and explicit 5-6-5 masks after a 40-byte header, 16-bit 1-5-5-5 with a `BITMAPV3INFOHEADER` alpha mask, 8-bit palette with five entries and a gap before the pixel data, 4-bit and 1-bit palettes with odd widths, `biXPelsPerMeter`/`biYPelsPerMeter`, FFmpeg-encoded 24-bit and 32-bit files |
| BMP errors | invalid: zero width/height, two planes, bit count 7, undefined compression, pixel offset inside the header, truncated header and pixel data, palette index out of range, overlapping/non-contiguous/zero masks; unsupported: OS/2 `BITMAPCOREHEADER` and `BITMAPCOREHEADER2`, `BI_RLE8`, `BI_PNG`, 2 bits per pixel, a 10-bit channel mask; limits: width, frame pixels |
| TGA | uncompressed 24-bit bottom-left and top-left, 32-bit with and without declared alpha bits, 32-bit right-to-left, 16-bit 1-5-5-5 and 15-bit, 8-bit grayscale, color maps with a first-entry offset (24-bit entries) and with 32-bit RGBA entries, run-length true color with runs of 7/130/20/30 crossing scan lines (128-pixel packets) and raw packets, run-length grayscale, an 11-byte identification field with a footer, a file whose extension area (attributes type 3) and footer follow the raster, a file with a developer area between the raster and the extension area, FFmpeg-encoded run-length files |
| TGA errors | invalid: a packet past the last pixel, a truncated packet, truncated pixel data, a color-map index below the first stored entry, a footer that locates the extension area over the footer, an extension area too short for its attributes type; unsupported: 16-bit color-map indexes, 16-bit grayscale, premultiplied alpha (attributes type 4); limits: width, encoded bytes |
| Netpbm | binary PBM with padding bits and a header comment, the same pixels as plain PBM, binary PGM 8-bit (with a comment) and 16-bit, plain PGM with `MAXVAL` 15, binary PPM 8-bit and 16-bit, plain PPM with `MAXVAL` 1000, PAM `RGB_ALPHA` 8-bit and 16-bit, PAM `GRAYSCALE_ALPHA`, PAM `BLACKANDWHITE`, FFmpeg-encoded PPM and PAM files |
| Netpbm errors | invalid: zero width, `MAXVAL` 0 and 65536, a plain sample above `MAXVAL`, a truncated raster, a 70,000-byte comment (the bounded header), a comment interrupting the last header token, the digit 2 in a plain PBM raster, a PAM header without `ENDHDR`, duplicated `WIDTH`, a `DEPTH` contradicting the tuple type, an undefined PAM keyword; unsupported: `TUPLTYPE CMYK`; limits: width, total pixels |
| WebP errors | invalid: RIFF/chunk sizes past the data, `VP8X` canvas mismatch, extended file without image, VP8L bad signature/version/corrupt prefix code/truncation, VP8 bad start code/first partition overflow/truncated partition, `ALPH` bad compression method, `ANIM` missing, frame outside the canvas, truncated animation; unsupported: a VP8 interframe; limits: width, frame pixels, metadata bytes, animation frames and total pixels |
| Metadata | `png/metadata-rgb8-profiles`: RGB ICC profile (`iCCP`), `pHYs` 3780x2835 per meter, big-endian EXIF (orientation 8, Software, Exif IFD pixel dimensions, IFD1 thumbnail placeholder), XMP `iTXt`, `tEXt`/`zTXt`/international `iTXt`, a `tEXt` after `IDAT`; `png/metadata-gray8-icc`: gray ICC profile, aspect-ratio-only `pHYs`, little-endian EXIF orientation 3. The ICC profiles are small hand-built ICC v4.3 test profiles (CC0, never applied) |
| APNG errors | Header defects: `acTL` with zero frames or `num_plays` 2^31, partial default-image `fcTL`, `dispose_op` 3, `fdAT` before `IDAT`; structure defects: sequence gap, region outside the canvas, fewer/more frames than declared, `fcTL` without data, `blend_op` 2, `fdAT` shorter than its sequence number; frame datastream defects (`fdAT` zlib truncated, one extra scanline); limits: `MaxFrames` counting the poster, `MaxTotalPixels` counting full-canvas displayed frames (decode-only) |
| Errors | PNG header defects (bad IHDR CRC, IHDR not first, illegal bit depth, chunk length above 2^31 - 1, unknown critical chunk `CgBI` — unsupported), structure defects (truncated IDAT, bad IDAT CRC, non-consecutive IDAT, PLTE after IDAT, missing IEND) and pixel-data defects with valid containers (truncated zlib, missing or wrong Adler-32, bytes after the zlib datastream, one extra scanline, one missing scanline, filter type 5, out-of-range palette index); truncated GIF image data, GIF plain-text extension (unsupported), arithmetic-coded JPEG (unsupported); GIF LZW and color-table defects inside valid containers (code above the next table code, table code after a clear, end code too early, data ending without end code, too many indices, a string exceeding the image, an index outside the table, no color table) and block defects (unknown block, 3-byte Graphic Control Extension, minimum code size 12, missing trailer, no image, empty logical screen); resource limits: frame count, GIF displayed pixels (`MaxTotalPixels`, decode-only), width, frame pixels, encoded bytes (reached at IEND), and a `zTXt` whose 171-byte zlib stream inflates to 25,801 bytes with `MaxMetadataBytes` = 4096 (deterministic fixed-Huffman stream written by the generator) |

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
  are verified by independent readers and by the interop job; the corpus references are also used as encoder inputs
  (`TestHarness.Adapters.EncoderSources`), except the seeded-noise decoder inputs written by FFmpeg (the generator's
  `*FfmpegDecoding` builders: `png/ffmpeg-*`, `apng/ffmpeg-<format>`, `gif/ffmpeg-<rectangles>-<transparency>`,
  `jpeg/ffmpeg-mjpeg-*`). Those exist so decoders meet an independent encoder's own choices (filters, delta rectangles,
  dispose/blend operations, quantization and Huffman tables) without running FFmpeg at test time.

## Fixtures per format and kind

| Format | Valid | Invalid | Unsupported | Limit |
| --- | --- | --- | --- | --- |
| bmp | 13 | 12 | 6 | 2 |
| gif | 24 | 15 | 1 | 2 |
| jpeg | 42 | 15 | 5 | 1 |
| png | 109 | 31 | 1 | 6 |
| pnm | 15 | 12 | 1 | 2 |
| qoi | 10 | 12 | 0 | 4 |
| tga | 18 | 6 | 3 | 2 |
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
| JPEG baseline (SOF0) | `jpeg.process=baseline` | 30 | 10 | 2 | 1 |
| JPEG extended sequential (SOF1) | `jpeg.process=extended` | 1 | 0 | 1 | 0 |
| JPEG progressive (SOF2) | `jpeg.process=progressive` | 11 | 5 | 0 | 0 |
| JPEG progressive spectral selection | `jpeg.progressive=spectral-selection` | 10 | 4 | 0 | 0 |
| JPEG progressive successive approximation | `jpeg.progressive=successive-approximation` | 10 | 4 | 0 | 0 |
| JPEG progressive DC refinement | `jpeg.progressive=dc-refinement` | 10 | 4 | 0 | 0 |
| JPEG progressive AC refinement | `jpeg.progressive=ac-refinement` | 10 | 4 | 0 | 0 |
| JPEG progressive separate DC scans | `jpeg.progressive=dc-separate` | 1 | 1 | 0 | 0 |
| JPEG partial progression | `jpeg.progressive=partial` | 1 | 1 | 0 | 0 |
| JPEG multi-scan sequential frames | `jpeg.process=baseline` + `jpeg.scans=3` | 1 | 0 | 0 | 0 |
| JPEG non-interleaved scans | `jpeg.scanComponents=1` | 2 | 1 | 0 | 0 |
| JPEG grayscale (one component) | `jpeg.components=1` | 4 | 0 | 0 | 1 |
| JPEG three components (YCbCr or RGB) | `jpeg.components=3` | 38 | 15 | 4 | 0 |
| JPEG Adobe APP14 (RGB transform) | `jpeg.app14=Adobe` | 2 | 0 | 2 | 0 |
| JPEG sampling 4:4:4 | `jpeg.sampling=4:4:4` | 7 | 0 | 1 | 0 |
| JPEG sampling 4:2:2 | `jpeg.sampling=4:2:2` | 5 | 0 | 0 | 0 |
| JPEG sampling 4:2:0 | `jpeg.sampling=4:2:0` | 14 | 14 | 2 | 0 |
| JPEG sampling 4:4:0 | `jpeg.sampling=4:4:0` | 2 | 0 | 0 | 0 |
| JPEG sampling 4:1:1 | `jpeg.sampling=4:1:1` | 2 | 0 | 0 | 0 |
| JPEG restart intervals | `jpeg.restartInterval` | 8 | 2 | 0 | 0 |
| JPEG 16-bit quantization tables | `jpeg.quantization=16-bit` | 1 | 0 | 0 | 0 |
| JPEG JFIF APP0 (density) | `jpeg.app0=JFIF` | 28 | 15 | 3 | 0 |
| JPEG EXIF (APP1) | `jpeg.app1=Exif` | 2 | 0 | 0 | 0 |
| JPEG XMP (APP1) | `jpeg.app1=http://ns.adobe.com/xap/1.0/` | 1 | 0 | 0 | 0 |
| JPEG ICC profile (APP2) | `jpeg.app2=ICC_PROFILE` | 2 | 0 | 0 | 0 |
| JPEG arithmetic coding (rejected) | `jpeg.process=arithmetic-sequential` | 0 | 0 | 1 | 0 |
| JPEG lossless (rejected) | `jpeg.process=lossless` | 0 | 0 | 1 | 0 |
| JPEG 12-bit precision (rejected) | `jpeg.precision=12` | 0 | 0 | 1 | 0 |
| JPEG four components, CMYK/YCCK (rejected) | `jpeg.components=4` | 0 | 0 | 1 | 0 |
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
| BMP 1-bit indexed | `bmp.bpp=1` | 1 | 0 | 0 | 0 |
| BMP 4-bit indexed | `bmp.bpp=4` | 1 | 0 | 0 | 0 |
| BMP 8-bit indexed | `bmp.bpp=8` | 1 | 0 | 0 | 1 |
| BMP 16-bit samples | `bmp.bpp=16` | 3 | 0 | 0 | 0 |
| BMP 24-bit BGR | `bmp.bpp=24` | 4 | 0 | 0 | 1 |
| BMP 32-bit samples | `bmp.bpp=32` | 3 | 0 | 0 | 0 |
| BMP partial palette | `bmp.palette` | 3 | 0 | 0 | 1 |
| BMP `BITMAPINFOHEADER` (40 bytes) | `bmp.header=40` | 11 | 0 | 0 | 2 |
| BMP `BITMAPV3INFOHEADER` (56 bytes) | `bmp.header=56` | 1 | 0 | 0 | 0 |
| BMP `BITMAPV4HEADER` (108 bytes) | `bmp.header=108` | 1 | 0 | 0 | 0 |
| BMP `BI_RGB` (implicit masks) | `bmp.compression=rgb` | 10 | 0 | 0 | 2 |
| BMP `BI_BITFIELDS` (explicit masks) | `bmp.compression=bitfields` | 3 | 0 | 0 | 0 |
| BMP real alpha mask | `bmp.alphaMask=yes` | 2 | 0 | 0 | 0 |
| BMP unspecified fourth byte (no alpha mask) | `bmp.alphaMask=no` | 4 | 0 | 0 | 0 |
| BMP bottom-up rows | `bmp.rowOrder=bottom-up` | 12 | 0 | 0 | 2 |
| BMP top-down rows (negative `biHeight`) | `bmp.rowOrder=top-down` | 1 | 0 | 0 | 0 |
| BMP row padding to four bytes | `bmp.rowPadding` | 10 | 0 | 0 | 2 |
| BMP gap between the palette and the pixel data | `bmp.gapBeforePixels` | 1 | 0 | 0 | 1 |
| BMP resolution (`biXPelsPerMeter`) | `bmp.resolution` | 1 | 0 | 0 | 0 |
| BMP odd width | `bmp.width=odd` | 11 | 0 | 0 | 2 |
| BMP OS/2 `BITMAPCOREHEADER` (rejected) | `bmp.unsupported=core-header` | 0 | 0 | 1 | 0 |
| BMP OS/2 `BITMAPCOREHEADER2` (rejected) | `bmp.unsupported=os2-v2-header` | 0 | 0 | 1 | 0 |
| BMP run-length encoding (rejected) | `bmp.unsupported=rle` | 0 | 0 | 1 | 0 |
| BMP embedded JPEG/PNG payload (rejected) | `bmp.unsupported=embedded-codec` | 0 | 0 | 1 | 0 |
| BMP 2 bits per pixel (rejected) | `bmp.unsupported=bit-count-2` | 0 | 0 | 1 | 0 |
| BMP channel mask wider than 8 bits (rejected) | `bmp.unsupported=wide-mask` | 0 | 0 | 1 | 0 |
| TGA color-mapped (type 1) | `tga.imageType=1` | 2 | 0 | 0 | 0 |
| TGA uncompressed true color (type 2) | `tga.imageType=2` | 10 | 0 | 0 | 1 |
| TGA uncompressed grayscale (type 3) | `tga.imageType=3` | 1 | 0 | 0 | 0 |
| TGA run-length true color (type 10) | `tga.imageType=10` | 3 | 0 | 0 | 1 |
| TGA run-length grayscale (type 11) | `tga.imageType=11` | 2 | 0 | 0 | 0 |
| TGA 8-bit samples or indexes | `tga.depth=8` | 5 | 0 | 0 | 0 |
| TGA 15-bit samples | `tga.depth=15` | 1 | 0 | 0 | 0 |
| TGA 16-bit samples | `tga.depth=16` | 1 | 0 | 0 | 0 |
| TGA 24-bit samples | `tga.depth=24` | 5 | 0 | 0 | 2 |
| TGA 32-bit samples | `tga.depth=32` | 6 | 0 | 0 | 0 |
| TGA no declared alpha bit | `tga.alphaBits=0` | 11 | 0 | 0 | 2 |
| TGA one declared alpha bit | `tga.alphaBits=1` | 1 | 0 | 0 | 0 |
| TGA eight declared alpha bits | `tga.alphaBits=8` | 6 | 0 | 0 | 0 |
| TGA bottom-up rows | `tga.rowOrder=bottom-up` | 12 | 0 | 0 | 1 |
| TGA top-down rows | `tga.rowOrder=top-down` | 6 | 0 | 0 | 1 |
| TGA left-to-right columns | `tga.columnOrder=left-to-right` | 17 | 0 | 0 | 2 |
| TGA right-to-left columns | `tga.columnOrder=right-to-left` | 1 | 0 | 0 | 0 |
| TGA color map | `tga.colorMap` | 2 | 0 | 0 | 0 |
| TGA color-map first-entry offset | `tga.colorMap.offset` | 1 | 0 | 0 | 0 |
| TGA run-length packet | `tga.rle.run` | 5 | 0 | 0 | 1 |
| TGA raw packet | `tga.rle.raw` | 5 | 0 | 0 | 1 |
| TGA 128-pixel packet (longest) | `tga.rle.packet=128` | 1 | 0 | 0 | 1 |
| TGA packet across scan lines | `tga.rle.crossesRow` | 2 | 0 | 0 | 1 |
| TGA image identification field | `tga.idField` | 1 | 0 | 0 | 0 |
| TGA 2.0 footer | `tga.footer` | 6 | 0 | 0 | 0 |
| TGA 2.0 extension area (attributes type) | `tga.attributesType` | 2 | 0 | 0 | 0 |
| TGA 2.0 developer area (not interpreted) | `tga.developerArea` | 1 | 0 | 0 | 0 |
| TGA bytes after the image data | `tga.trailingData` | 6 | 0 | 0 | 0 |
| TGA odd width | `tga.width=odd` | 16 | 0 | 0 | 1 |
| TGA 16-bit color-map indexes (rejected) | `tga.unsupported=colormap-16bit` | 0 | 0 | 1 | 0 |
| TGA 16-bit grayscale (rejected) | `tga.unsupported=grayscale-16bit` | 0 | 0 | 1 | 0 |
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

Not covered by fixtures, and verified elsewhere: encoder outputs (independent readers, FFmpeg and Apple ImageIO), malformed-input robustness beyond the error
fixtures (`FuzzTests/MutationFuzzTests`, `Tests/Conformance/Hardening/TruncationTests`), and limits at every boundary (`Tests/Conformance/Hardening/ResourceLimitBoundaryTests`).

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
- Processing tests (`Tests/Conformance/ProcessingGoldenTests`) reuse the decode references as inputs: they import every
  frame and poster, apply crop/rotate/flip/auto-orient, and compare exactly with the reference transformed by the
  harness (`RawPixelBuffer.Crop`, `FlipHorizontal`/`FlipVertical`, `Rotate180`, `Rotate90Clockwise`/`CounterClockwise`,
  `Transpose`, `Transverse`: pure index permutations checked against literals in `PixelBufferTests`). No processed
  reference files are stored; the expected output is never produced by the library.
- Auto-crop tests (`Tests/Conformance/AutoCropGoldenTests`) import every frame and poster as is and wrapped in a
  synthetic border (`RawPixelBuffer.Extend`, checked against literals in `PixelBufferTests`), and compare the detected
  box, the background and the cropped or enlarged pixels exactly with `TestHarness/AutoCrop/ReferenceAutoCrop`, an
  independent implementation of the auto-crop contract (plain loops over every pixel, linear color lists, exact
  `BigInteger` weights). No processed reference files are stored.
- Resize tests (`Tests/Conformance/ResizeGoldenTests`) also import every frame and poster and compare the resized pixels
  with `TestHarness/Resampling/ReferenceResampler`, an independent high-precision implementation of the resize contract
  (exact rational geometry, decimal kernels and
  sums). Generic tool resize output (FFmpeg, ImageMagick) is not an oracle: their pixel-center, edge, alpha and rounding
  conventions differ. No resized reference files are stored.
- Convolution tests (`Tests/Conformance/ConvolutionGoldenTests`) do the same with
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
- APNG processing (`Tests/Conformance/ApngProcessingConformanceTests`) decodes every corpus APNG with the library and
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

`verify` is read-only and uses the ffmpeg, `dwebp` and `anim_dump` of the meziantou/prebuilt release pinned by the
`Meziantou.Prebuilt` package, like the interop tests (downloaded on first use, whatever their versions compared with the manifest): it re-decodes every valid non-JPEG
fixture whose generation-time FFmpeg cross-check agreed, and every WebP fixture with `dwebp` and `anim_dump`, and compares
with the committed references. Run it before regenerating with a new tool version to see what changes; the interop CI job runs it on every
build to detect reference drift.

- The generators are the subcommands of one .NET console app (`tools/Meziantou.Framework.Imaging.CorpusGenerator`) and
  never use the library under test.
- Requirements of `golden`: macOS (Apple ImageIO `sips`: JPEG cross-checks and PNG arbitration; `swift` from the Xcode
  command line tools runs `imageio_frames.swift` for the APNG cross-checks), the .NET SDK of `global.json` (the generator
  targets .NET 10 and has no dependency on the library), FFmpeg, and libjpeg-turbo `cjpeg`/`djpeg` at the versions pinned
  in `GoldenTools.PinnedVersions` (currently FFmpeg 9.0.2, libjpeg-turbo 3.2.0, sips-316). Other versions require
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
  documentation; TGA: the Truevision TGA File Format Specification 2.0; PBM/PGM/PPM/PAM: the Netpbm format
  specifications; EXIF/TIFF; ICC; future formats: their official specifications).
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

`CC0-1.0`, `MIT`, `Unlicense`, `CC-BY-4.0`, `Libpng`, `Zlib`.

Content created by contributors for this repository (hand-authored patterns, outputs of committed generators, and files
produced by running third-party tools on such content) is contributed under `CC0-1.0` unless stated otherwise. Tool
output does not carry the tool's license.

Metadata payloads embedded in fixtures (EXIF blocks, XMP packets, text, and the small ICC v4.3 test profiles built by
`icc_profile` in the generator) are hand-built test data under `CC0-1.0`. Never embed third-party ICC profiles (for
example vendor sRGB profiles) unless their license is on the allow-list.

Adding another license (for example the custom permissive terms of PngSuite as `LicenseRef-PngSuite`) requires a
reviewed change that updates the allow-list in **both** `manifest.schema.json` and `FixtureManifestValidator`, and adds the
full license text to `LICENSES/<identifier>.txt` in this folder. Files under copyleft or non-commercial licenses (GPL,
LGPL, CC-BY-SA, CC-BY-NC, ...) or without a clear license must not be committed. Photographs of identifiable people must
not be committed.

### Size

Keep fixtures small (prefer tiny images; a few MB in total). Large inputs for benchmarks are generated at run time and
never committed.

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
