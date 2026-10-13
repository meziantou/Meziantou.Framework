# Meziantou.Framework.Imaging

A fully managed image library: PNG/APNG, GIF, JPEG, WebP, QOI, BMP, TGA, Netpbm, TIFF, ICO/CUR, ANI and AVIF (with its
own AV1 decoder and encoder) decoding and encoding, JPEG XL decoding and lossless encoding, animation-aware processing,
vector drawing, ICC color conversion, and bounded-memory streaming readers and writers.

These instructions complete the repository-level `AGENTS.md`. The user guide is [readme.md](readme.md): it states what
the library guarantees to its callers (support matrix, limitations, defaults). Keep the code, the XML documentation and
`readme.md` consistent in the same change.

## Hard constraints

- Target frameworks are exactly `net10.0;net11.0` for the library and every test, sample and benchmark project. Do not
  add older targets or compatibility shims.
- Fully managed, no package dependency and no native dependency. The library is trimmable and NativeAOT-compatible
  (`IsTrimmable`, `IsAotCompatible`): no reflection-based code.
- Write implementations from the format specifications. Never copy or port code from other image libraries
  (System.Drawing, libpng, libjpeg, giflib, libwebp, libavif, libaom, dav1d, stb, ...). Those libraries are test oracles
  only: their output is compared with ours, their source is never read to write ours.
- No public allocator, custom pixel type, codec plug-in or image-processor interface. Implementation types are `internal`
  and live under `Internals/`.
- Public namespaces: `Meziantou.Framework.Imaging`, `.Metadata`, `.Formats` (encoder settings) and `.Drawing` (brushes,
  pens, paths, the canvas). The base types of the model (`Image`, `ImageFrame`, `ImageFrameCollection`, `ImageEncoder`,
  `AutoCropAnalysis`, `Brush`, `GradientBrush`) cannot be derived outside the library.
- Every public member has XML documentation (`DisableDocumentationWarnings` is `false`).
- `Meziantou.Framework.FullPath` is used by the tests, benchmarks and tools, never by the library.

## Behavior rules

- **Never lose information silently.** Losing alpha, precision, animation, a poster, metadata or a color profile needs an
  explicit setting; otherwise throw `UnsupportedImageFeatureException`. Color profiles are preserved and labeled, never
  applied implicitly: only `ConvertColorProfile`, `IccColorTransform` and a load with `CmykConversionOptions` convert
  colors, and the pixels and their profile always change together. A CICP color description is a label too: only
  `ConvertColorProfile` converts from it, and high dynamic range samples only with explicit `ToneMapping` options.
- **An image holds full displayed frames**, not encoded deltas; decoders composite, encoders write what they are given.
  Timing is exact (`FrameDuration` is a rational number); a duration or play count the output format cannot represent is
  an error unless a rounding policy is set.
- **Errors are distinct and never a clean end of input:**

  | Situation | Exception |
  | --- | --- |
  | Signature not recognized | `UnknownImageFormatException` |
  | Recognized format, malformed, inconsistent or truncated data | `InvalidImageContentException` |
  | Valid but unsupported feature, or an operation that would lose information | `UnsupportedImageFeatureException` |
  | Configured limit exceeded | `ImageResourceLimitException` |
  | Unsupported `TPixel` | `NotSupportedException`, before any I/O or allocation |
  | Invalid argument / invalid state / use after dispose | `ArgumentException` family / `InvalidOperationException` / `ObjectDisposedException` |
  | I/O failure, cancellation | `IOException` and `OperationCanceledException`, propagated unchanged |

- **Validate before doing anything.** Argument and pixel-type validation happens before any I/O or allocation; keep the
  existing validation order when changing a member.
- **Resource limits** (`ImageResourceLimits`) are enforced incrementally, before allocating or consuming. They are
  inclusive and always positive (no zero-as-unlimited). A limit failure is never turned into a truncation error or a
  success. Test each limit at the boundary and one over.
- **Atomicity.** Geometry-changing operations (crop, auto-crop, resize, rotate, auto-orient) are transactional across all frames and
  the poster. Pixel-only edits (flip, grayscale, convolve, draw) may be partially applied on failure but leave the image
  valid and disposable. A failed operation releases every resource it acquired.
- **Ownership.** Images are owned and disposed by their creator. Frames are borrowed views: they are not disposable and
  throw `ObjectDisposedException` once removed, replaced or disposed with their image. Eager loads never retain or close
  the caller's stream.
- **Thread safety.** Immutable types and static methods are thread-safe. An image, a reader or a writer supports one
  operation at a time.

## Memory

- Every library-controlled buffer (pixels, decoder and compositor state, input buffers, scratch, encoder state) is rented
  from `SlabPool` and charged at its actual rented capacity to an `AllocationScope`, which is bounded by
  `MaxLiveAllocationBytes`. Never allocate a large buffer outside a scope; charge before renting.
- Pixel storage is made of row-aligned slabs: rows are contiguous, the whole image is not. All size arithmetic is checked
  (`CheckedSizes`). Newly visible pixels are cleared; padding and scratch data are never exposed.
- Pixel access goes through exclusive scoped leases (`ProcessPixelRows`, `ProcessPixelBytes`, `CopyPixel*To`). Cross-image
  operations acquire leases in a stable order (`PixelLeaseSet`) and handle aliasing. Leases are released in `finally`.
- Geometry changes go through `PixelStorageTransaction`: replacements are budgeted while the originals are live and are
  only swapped on commit.
- Sequential readers and writers keep live storage independent of the frame count.
- `SlabPool` retention is bounded per size class and in total bytes. When the total bound is reached, a returned buffer
  releases the buffers of the size classes that were not used recently (`SlabPoolOptions.StaleAfterOperations`), so that
  a change of image sizes or codecs does not leave the pool full of buffers nobody rents; the size classes in use are
  never released for one another.

## Codec infrastructure

All entry points (`Identify`, `Load`, readers; span, stream and path; sync and async) share one pipeline in
`Internals/IO` and `Internals/Codecs`:

- Identifiers and decoders are synchronous push-model parsers (`ImageParser<TResult>`): they consume what they can and
  return `Complete`, `NeedMoreData(n)`, or `NeedMoreDataOrEnd(n)` when the end of the input satisfies the request too. The
  driver (`ImageInputPump`) performs the I/O, so the same parser serves the sync and async APIs and CPU work never runs in
  `Task.Run`. Parsers check cancellation between rows and frames.
- Streams are read forward only and never rewound, and no byte past `MaxEncodedBytes` reaches a parser.
  `MaxEncodedBytes` equal to the input length is always enough, and spans and streams fail identically at the same
  boundary, with the same `Requested`.
- A span knows where it ends; a stream only says so when it is read past its end. A parser that needs the end of the
  input (a trailer located from the last bytes, an optional terminator, a container buffered whole) must ask with
  `NeedMoreDataOrEnd`, never with `NeedMoreData`: at the limit, the driver then reads one discarded byte
  (`ImageInputBuffer.ProbeEndOfInput`) to tell an input that ends there from a longer one. It is the only read past the
  limit; do not add another.
- Each format has a structure parser (container rules, written once and walked as header, full scan or decode), a pixel
  decoder that observes it, and an encoder codec. Decoders hand rows to a `DecodedFrameSink` in a lossless source layout;
  the sink converts to the requested pixel format and charges frames before allocating them.
- A new format always adds an `ImageFormat` member, its name in `ImageFormatNames`, an `ImageCodec` in
  `ImageCodecRegistry`, a default pixel format in `DefaultPixelFormats`, and a row in the support matrix and a section in
  the format reference of `readme.md`. A format that is also written adds an encoder codec in `ImageEncoderRegistry`, a
  public `ImageEncoder` in `Formats/` and its extensions in `ImageEncoder.FromPath`. A format that is only read (none
  today) makes `ImageEncoder.FromPath` reject its extensions with a message that says so.
- **AVIF and AV1** (`Internals/Codecs/Avif`). The container walker resolves the image (`AvifImagePlan`, from the `meta`
  box) or the image sequence (`AvifMovieBox`, from the `moov` box) before any data is read, then captures the items or
  samples as their bytes go by. The AV1 decoder (`Av1/`) follows the AV1 Bitstream & Decoding Process Specification
  section by section and keeps its names: a comment names the section a method implements. Its constant tables
  (`Av1/Tables/*.g.cs`) are generated from the published specification by
  `tools/Meziantou.Framework.Imaging.Av1TableExtractor` (the specification text is not committed; the tool pins its commit
  and the hash of each file): never edit them by hand. Every plane the decoder produces must stay bit-exact with the
  reference decoders: there is no tolerance in AV1 decoding. `Av1Decoder.Tools` reports the coding tools a bitstream used;
  it is a diagnostic for the tests and never changes the decoding.
  Scalability only changes which frames are output, never how they are decoded: the units outside the operating point
  are dropped, and a temporal unit with several spatial layers outputs the frame of its highest layer (what a player
  shows, and what `lsel`-less layered AVIF images present). `OutputAllLayers` is for conformance tests only.
  An `iden` item is resolved by `AvifImagePlan` into the transformations of the coded image it presents (no pixel
  moves; the alpha plane of a derived image must be presented as its colors). An image that is built from other
  images (an `iovl` item, a grid of derived images) is an `AvifImageNode` tree, rendered by `AvifDecoder.Derived.cs`
  to straight RGBA surfaces: only that path moves or composites pixels, the direct path (a coded image or a grid of
  coded images, through identity derivations) must stay as it is. Nothing converts colors there: every color
  description of the tree is the same one or the file is rejected, and the structure parser validates the
  descriptors and the fill colors before the first pixel is decoded. The edit list of a track that selects
  parts of the media is an `AvifEditTimeline` (exact intersections in units of the two time scales; the images that
  are left out are decoded and charged as hidden pixels, and every sample is located then). A fragmented sequence
  (`mvex`) is read by `AvifMovieFragment`: each `moof` box appends samples to `AvifTrack`, whose tables only hold the
  samples that are still to be read, and the walk ends with the input (`ParseStatus.NeedMoreDataOrEnd` at a box
  boundary). The independent readers are `ReferenceAvif.GetIdentityChain`, `Track.GetPresentation` and its own `moof`
  reader; Apple ImageIO judges identity derivations and the opaque results of built images, and FFmpeg the fragments
  it can demux. The reference of a built image is the generator's own renderer (`AvifCorpus.ContainerDerived.cs`,
  exact rational source-over on the planes of the reference decoders).
- **AV1 encoder** (`Internals/Codecs/Avif/Av1/Encoding`, and the `Write` and `Encode` parts of `Av1TileCoder`). The
  specification only describes decoding: the encoder is written from it, never from another encoder.
  - The encoder shares the state and the processes of the decoder (`Av1FrameCodingState`, the prediction, the
    reconstruction, the in-loop filters) and never duplicates them: the frame it keeps is the frame a decoder outputs.
    A syntax element is written by a `Write` method that sits on the same context derivation as its `Read` method in
    `Av1TileCoder`; extract the derivation when a `Read` method computes it inline.
  - The headers are written (`Av1HeaderWriter`), then parsed back by the parsers of the decoder: the encoder never
    derives a header variable itself.
  - The search encodes its candidates for real, records its decisions (`Av1EncoderWorkspace`) and replays the records to
    the symbol encoder. A candidate that changes a context that the search does not save, or a decision that is not in
    the record of its block, is a stream that decodes to something else: `Av1EncoderRoundTripTests` (the decoder of the
    library must output the reconstruction of the encoder) and `AvifEncoderInteropTests` (dav1d and libaom must output
    the same planes) are the tests to run after any change.
  - Every decision is made with integers (rates, distortions, the forward transforms): the output must not depend on
    the platform. The forward transforms are measured on the inverse kernels of the decoder, not written out.
  - The palettes of the block being coded live where the decoder has them (the scratch of the tile), and those of the
    blocks above and to the left are contexts: a candidate with a palette changes them, so they are in the context
    snapshots of a frame that has palettes, and the palettes of a block are in its record.
  - A lossless key frame of screen content allows intra block copy (`Av1TileCoder.BlockCopy.cs`): the blocks of 8x8
    source samples that a block may copy are in a hash table of the workspace, filled superblock by superblock with
    the delay that `IsMvValid` requires, and a candidate is always validated by `IsMvValid`, which is the function
    of the decoder. A copy is an inter block of an intra frame: it is searched, recorded and replayed by the code of
    the inter blocks. Lossy frames do not copy blocks, because a frame that does has no in-loop filter.
  - A frame with loop restoration (lossy, efforts 0 to 2) writes its tiles last: the units of the loop restoration
    are coded with the superblocks and chosen after the whole frame is filtered. The syntax elements go to the tape of
    the workspace (`SymbolSink.Tape`, which adapts the distributions like the range encoder) and `ReplayTape` writes
    them with the units. Whatever the block syntax does when it produces the stream (`SymbolSink.Range`), it must do
    for the tape too: `Av1EncoderRoundTripTests.SyntaxElementsWrittenAfterTheFrameAreTheSameBytes` compares both. The
    filters are proposed by least squares (`Av1RestorationSearch`) and judged with the filter of the decoder, on the
    stripes that `Av1PostFilter.TryGetRestorationStripe` builds for both.
  - A tool is enabled at the efforts where the `avif-quality` report (`benchmarks/ImagingBenchmarks`) shows that it
    makes its inputs smaller at equal quality, and stays out otherwise: the Limitations row of the readme lists the
    tools that were measured and left out, with their figures. Measure before and after with the report (it takes a
    filter on the names of its inputs), and add an input when the tool needs content that the report does not have.
  - A frame of a sequence that is not a key frame is an inter frame whose only reference is the previous frame
    (`LAST_FRAME`; every slot is refreshed by every frame). An inter block is written from the motion vector stack that
    `FindMvStack` builds, like a decoder: the stack reads which blocks of the frame are written, so the search marks
    the blocks of a region as not written (`RefFrameNotWritten`) whenever it restores the region. The motion search
    only proposes a motion vector: the block is then encoded like any candidate.
  - The container writer (`AvifFileWriter`) shares no code with `AvifFileBuilder`, the builder of the test harness.
- **JPEG XL** (`Internals/Codecs/JpegXl`). The structure parser reads the container and gives the codestream to
  `JpegXlCodestreamWalker` in whatever slices arrive; the image header and each frame header are parsed speculatively
  and retried with more bytes, a frame is decoded once its sections are complete. The constant tables
  (`JpegXl/Tables/*.cs`) are transcribed by hand, since there is no public specification text to extract them from:
  every table must be reached by a test that compares the decoder with the reference decoders. Lossless (Modular)
  decoding and the encoder are exact. VarDCT is defined in floating point, so lossy decoding is compared with a
  tolerance, measured against both djxl and jxl-oxide, which must also agree with each other: never loosen a tolerance
  to hide a file that is read in two ways. The integer path (`JpegXlDecoder.WriteFrame`) is only taken by frames that
  are shown as they are stored; everything else goes through `JpegXlFrameRenderer`, in floating point, where 1 is the
  largest integer sample. The render order is the one of libjxl: restoration filters, patches, splines, upsampling,
  noise, color transform, blending.
- **TIFF** (`Internals/Codecs/Tiff`). `TiffPage` resolves and validates a directory without reading a pixel, so every
  rejection also comes from `Identify`, except the ones of the compressed data. `TiffBlockReader` turns a strip or a
  tile into rows of samples (fill order, compression, predictor, YCbCr data units) and `TiffRowExpander` turns the rows
  into pixels (packed samples, palette, inks). The decompressed size of a block is always known first: a decompressor
  fills a buffer of that size, it never grows one, and it never repairs data. LZW, PackBits and the CCITT compressions
  are written from TIFF 6.0 and ITU-T T.4 and T.6; the code words of `TiffCcittCodes` are transcribed from the
  recommendations, and the tests compare strips with code words spelled out from the same tables. A JPEG strip is
  decoded by `JpegDecoder` given a `JpegSampleReceiver` and written by the session of `JpegEncoderCodec`: there is no
  second JPEG implementation, and the page says what the components are. The layouts of `TiffEncoder` store a page
  exactly or reject it before writing anything of it (`TiffDocumentWriter.PreparePage`): no setting makes them lossy.
- `ZLibStream` reports truncation as a clean end: check that the expected number of bytes was produced.

## Color management

`IccColorTransform` (sample buffers), `ConvertColorProfile` (images) and the decoding of CMYK samples
(`CmykConversionOptions`) are the only code that applies an ICC profile; everything is under `Internals/Color`.

- No pixel format holds CMYK samples. A decoder that meets them (four-component JPEG, separated TIFF) converts its rows
  with the pipeline of an `IccColorTransform`, only when the request carries `CmykConversionOptions`, and labels the image with
  the destination profile; without the options it throws `UnsupportedImageFeatureException` before allocating. An
  embedded profile that cannot be used is an error, never replaced by the assumed one.
- `IccProfile.Cgats001CompatibleCmyk` is third-party data (CC0-1.0): `Internals/Color/BuiltInCmykProfile.g.cs` is
  transcribed from the corpus by the `icc-builtin` command of the corpus generator. Never edit it by hand.

- Written from ICC.1:2001-04 (version 2 profiles), ICC.1:2022 (version 4 profiles) and Adobe's published black point
  compensation algorithm. Never port code from another color management system; cite the clause in the code.
- A profile is untrusted input. Validate every size against the tag before reading (64-bit arithmetic for table
  sizes), read color lookup tables in place (`ReadOnlyMemory<byte>`, no copy) so that a transform only copies
  one-dimensional curves, and allocate nothing per conversion (`PerformanceContractTests`). A malformed profile is `InvalidImageContentException`; a valid profile that is not
  supported or cannot be a destination is `UnsupportedImageFeatureException`. Nothing else may escape
  (`ColorProfileFuzzTests`).
- A conversion is a list of `IccStage` evaluated in `double`, one color at a time (`ColorConversionKernels.ConvertScalar`
  is the numerical reference). Both profiles meet in CIEXYZ relative to D50. A faster kernel must be bit-identical and
  proven so in `VectorizedKernelTests`.
- The source of `ConvertColorProfile` may be the CICP color description of an image without profile
  (`CicpColorSpace`, `IccPipeline.Create(CicpColorSpace, ...)`): a `CicpDecodeStage` (the functions of
  `CicpTransfer`, Rec. ITU-T H.273 table 3), the matrix of the primaries (`CicpPrimaries`, table 2) adapted to D50 with
  the linear Bradford transform, then the destination half of the ICC pipeline. Descriptions equivalent to sRGB keep
  the built-in profiles, bit for bit. PQ and HLG samples go through `HdrToneMapStage` first (BT.2100 display light,
  then a curve of `HdrToneMappingOperator` on the largest component), and are rejected without `ToneMapping` options.
  The decimal references are `ReferenceCicpColorSpace`, `ReferenceCicpTransfer` and `ReferenceHdrToneMap`; the curves
  also have literals computed outside the library, and FFmpeg's `tonemap` filter agrees with three of them
  (`ToneMappingCurvesAreThoseOfFFmpeg`). `ResizeWorkingSpace.LinearSrgb` and `ConvolutionWorkingSpace.LinearSrgb` use
  the transfer function of the description (`Image.GetLinearLightTransfer`), whatever its primaries.
- The interpolation of color lookup tables (simplex along the main diagonal for gray, RGB and CIEXYZ inputs,
  multilinear for CIELAB and CMYK inputs), the generalized inverse of tone curves and the black point estimates are
  part of the documented numerical contract: change them only with `readme.md` and the tests.
- Tests have three independent sources: literals computed outside the library (`IccColorTransformTests`), the decimal
  reference of the harness (`ReferenceIccTransform`, `ColorConversionReferenceTests`), and colors converted by LittleCMS
  (`colorTransforms` of the corpus, `ColorConversionGoldenTests`). The reference shares the reading of the
  specification with the library: only the LittleCMS vectors and the literals catch an error of interpretation, so a
  new tag type or encoding needs one of them. Synthetic profiles are built by `Common/IccProfileBuilder.cs` of the
  corpus generator (no dependency on the library), shared with the tests through `IccTestProfiles`: add a profile
  there, then a conversion to `IccCorpus.cs`. The fixtures README lists what LittleCMS cannot judge (CMYK
  interpolation between grid points, three black point compensation cases).
- Pixels and their profile change together: `ConvertColorProfile` is transactional. No other operation converts colors.

## Drawing

`ImageFrame.Draw` / `Image.Draw` open a `DrawingSession` (leases, scratch, the validity of the `DrawingCanvas`); everything
is under `Internals/Drawing`. A draw call goes through: path or shortcut -> `PathFlattener` (curves to chords) ->
`FillSink` or `Stroker` -> `EdgeBuilder` (clipped edges) -> `CoverageRasterizer` (one row of coverage at a time) ->
`GradientEvaluator` for gradients -> `DrawingKernels` (compositing on each frame).

- Written from first principles: never port code from another rasterizer or stroker (FreeType, Skia, AGG, stb, ...).
- The numerical contract is documented in `readme.md` ("Rendering") and in the XML documentation of
  `CoverageRasterizer`, `DrawingKernels` and `GradientEvaluator`: change it only with them and the tests.
  - Coverage is the exact area of the pixel inside the shape (the union of overlapping contours), in 1/65536 steps,
    ties upward. The rasterizer is a sweep line with lazily recorded boundaries; its row scratch is sized up front from
    the largest number of edges on one row.
  - Compositing is exact integer source-over (`ulong`), the generalization of `AnimationCompositor.BlendOver8/16` to a
    fractional source alpha. Colors are converted to the pixel format first (`PixelConverter` rules).
  - Gradients interpolate premultiplied values of the converted stops, evaluated per pixel at its center.
- Only `+ - * /` and `Math.Sqrt` in the geometry (no trigonometry, no fused multiply-add), and every order that
  influences a sum is total (events and crossings are sorted with a tie-break): results must not depend on the
  platform, the framework version or a sorting algorithm.
- A stroke is a union of convex pieces with one orientation (`Stroker`): overlaps are harmless because the coverage is
  the area of the union. The 1/16 pixel flattening tolerance is public; the positions of the chord vertices are not.
- Every rental of a draw call happens before its first pixel write (edges, then the sweep and gradient scratch in
  `Render`). Draw calls allocate nothing once the scratch of the session has grown (`PerformanceContractTests`).
- The cost of a shape follows its edges and their crossings. Keep the events of the sweep logarithmic: no pass over
  every edge of a row per event.
- Tests have three independent sources: hand-computed literals (`Drawing*Tests`), the exact rational references of the
  harness (`TestHarness/Drawing`: `ReferenceCoverage`, `ReferenceCompositor`, `ReferenceGradient`, and the
  `ReferenceStroke` membership predicate; `Conformance/DrawingReferenceTests`), and invariants over random drawings
  (`DrawingFuzzTests`). The references never reuse the flattening or stroking of the library: curves are checked
  against the true curve, strokes against the predicate, and the pieces the library emits are then filled exactly.

## Performance work

- Remove unnecessary decodes, conversions and copies before micro-optimizing. Profile first and keep the evidence.
- Scalar kernels are the numerical reference. A vectorized kernel keeps its scalar version (`...Scalar`) and needs a
  test proving identical results (`VectorizedKernelTests`). No fused multiply-add in the DCTs.
- Default parallelism is one worker (`ImageConfiguration.MaxDegreeOfParallelism`). Only `Resize` and `Convolve` are
  parallel, and their result must not depend on the worker count. Decoding, encoding and drawing stay sequential.
- Static row callbacks allocate nothing per row (`PerformanceContractTests`).
- This repository builds with the updated memory-safety rules: wrap `MemoryMarshal`, `Unsafe` and `Vector*.LoadUnsafe` /
  `StoreUnsafe` calls in `unsafe(...)`, or in an `unsafe { }` block for a statement.
- Benchmarks (BenchmarkDotNet, `benchmarks/ImagingBenchmarks`) are compared on the same machine only:

  ```shell
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- --inProcess --filter "*PngBenchmarks*"
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- service --seconds 10
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- webp-quality 512 3
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- profile PngBenchmarks.SaveLarge8 20
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- profile PngBenchmarks.SaveLarge8 50x
  dotnet run eng/compare-benchmarks.cs -- <baseline>/results <current>/results
  ```

  `Workloads/` has no BenchmarkDotNet dependency: the conformance tests compile it to validate the workload outputs.

  `profile` prints the mean, the minimum and the median time of an iteration, the managed bytes allocated per iteration and
  what the shared buffer pool did. `<n>x` runs a fixed number of iterations instead of a duration: on a busy machine,
  compare two builds with a process counter that does not depend on the load (`/usr/bin/time -l dotnet
  ImagingBenchmarks.dll profile <Class.Method> <n>x` reports the retired instructions and the cycles on macOS; subtract a
  `1x` run to remove the setup) rather than with wall-clock times.
- The stack sampler of `dotnet-trace` blames the method that holds the next safe point, and it also samples the setup of
  the benchmark class: confirm a hot spot by timing the stage (temporary `Stopwatch` counters) before optimizing it.

## Projects

| Path | Content |
| --- | --- |
| `src/Meziantou.Framework.Imaging` | The library and the package README (`readme.md`) |
| `tests/Meziantou.Framework.Imaging.Tests` | Tests that are not about one codec: image model, pixels, memory, metadata, readers and writers with test codecs, format detection, defaults, API surface, the corpus and harness self-tests, the documentation examples |
| `tests/Meziantou.Framework.Imaging.Processing.Tests` | Processing, resampling, convolution, automatic cropping, drawing and color conversion, with their golden and reference suites |
| `tests/Meziantou.Framework.Imaging.<Codec>.Tests` | One project per codec (`Png` with APNG, `Gif`, `Jpeg`, `JpegXl`, `WebP`, `Qoi`, `Bmp`, `Tga`, `Pnm`, `Tiff`, `Ico` with CUR and ANI, `Av1`, `Avif`, `AvifEncoder`): its unit tests (no fixture file), its conformance tests under `Conformance/`, and the generic suites for its fixtures and its encoder |
| `tests/Meziantou.Framework.Imaging.Tests.Shared` | Sources compiled into every project above (no project file): `Helpers/`, the generic suites over the corpus (`Suites/`) and `Imaging.Tests.Shared.props` |
| `tests/Meziantou.Framework.Imaging.InteropTests` | Files produced by the encoders are decoded by pinned external tools |
| `tests/Meziantou.Framework.Imaging.TestHarness` | Shared, test-framework-agnostic support: corpus manifest, raw-buffer comparison, independent reference readers, tool helpers |
| `tests/Meziantou.Framework.Imaging.AotSmoke` | Console app exercising every format; published with NativeAOT |
| `tests/Meziantou.Framework.Imaging.Fixtures` | Golden corpus, manifest, provenance and licenses ([README](../../tests/Meziantou.Framework.Imaging.Fixtures/README.md)) |
| `fuzz/Meziantou.Framework.Imaging.FuzzTests` | Mutation fuzz tests over the corpus and the committed regressions (`FuzzRegressions/`) |
| `tools/Meziantou.Framework.Imaging.CorpusGenerator` | The reviewed recipe of every fixture; never run by the tests |
| `tools/Meziantou.Framework.Imaging.Av1TableExtractor` | Generates the constant tables of the AV1 decoder from the AV1 specification |
| `samples/Meziantou.Framework.Imaging.Samples` | Usage examples: the source of every C# block of the documentation |
| `benchmarks/ImagingBenchmarks` | BenchmarkDotNet workloads |

## Tests

```shell
dotnet build slnx/Meziantou.Framework.Imaging.slnx
dotnet test --solution slnx/Meziantou.Framework.Imaging.slnx
dotnet test --project tests/Meziantou.Framework.Imaging.Png.Tests/Meziantou.Framework.Imaging.Png.Tests.csproj --framework net11.0
dotnet publish tests/Meziantou.Framework.Imaging.AotSmoke -c Release -f net11.0 --use-current-runtime -o artifacts/aot
```

- **One test project per codec.** The unit and conformance tests are split so that CI runs them in several jobs
  (`$ImagingProjects` in `.github/workflows/ci.yml`); none of them needs an external tool or network access. The suites
  that run every fixture through the decoders (identification, loading, sequential reading, truncation, limits, accounting,
  fault injection) and every encoder through the save fault injection live once in `Tests.Shared/Suites` and are compiled
  into each codec project, which declares its part in its project file: `ImagingTestFixtureFormats` (manifest formats) and
  `ImagingTestEncoders` (`CorpusScope` filters the cases). The sources of the encoder suites are never filtered: an encoder
  is tested with the fixtures of every format. `CorpusScopeTests` fails when a format or an encoder belongs to no project
  or to two.
- **Adding a codec**: create `tests/Meziantou.Framework.Imaging.<Codec>.Tests` from an existing codec project, set its two
  scope properties, add its `InternalsVisibleTo` to the library, add the encoder name to `CorpusScope.AllEncoderNames` and
  to `FaultInjectionTests.LoadForEncoder`, add the project with a weight to `$ImagingProjects` in `ci.yml`, and run
  `dotnet run ./eng/update-all.cs`. A helper used by several projects goes to `Tests.Shared/Helpers`; a test class never
  calls a member of a test class of another project.
- **Expected values come from independent sources**: hand-computed values, specifications, independent decoders or the
  reference readers of the test harness. Never produce a decoder expectation with our encoder or compositor, and never
  use the code under test as its own oracle.
- Lossless comparisons are exact, 16-bit low bits included. Lossy comparisons use an independent decoding of the same
  input with a narrow, justified, per-fixture tolerance. Alpha and structure are always exact.
- Unit, conformance and fuzz tests must not need an external tool or network access.
- **TIFF has no corpus.** The decoder tests assemble files (`TiffFileBuilder`) whose compressed strips never come from
  the library: the reference encoders of the tests (`TiffReferenceCodecs`), the examples of the specifications, fax
  strips written by libtiff and kept as literals, and JPEG datastreams of `JpegTestImage`. The encoder tests compare
  small strips with the bytes the specifications give, and the interop tests decode every compression and layout with
  FFmpeg or Apple ImageIO (the JPEG pages, which FFmpeg does not read), and the pages FFmpeg writes with the library.
- **CMYK JPEG fixtures.** A four-component JPEG file is an `unsupported` fixture (identified, rejected by a load
  without conversion) that declares one conversion and its pixels (`inkConversion`). The reference has two independent
  stages: the samples decoded by `djpeg` from files the generator regroups scan by scan, then the decimal reference of
  the harness for the colors, with LittleCMS and Apple ColorSync as cross-checks.
  `JpegDecoderConformanceTests` pins each tolerance to the measured disagreement (`LibraryInkMeasurements`).
- **AV1 vectors.** `tests/Meziantou.Framework.Imaging.Fixtures/av1` holds raw AV1 streams with, in the manifest, the
  SHA-256 of every plane of every output frame. A digest is recorded only when two independent decoders (dav1d and
  libaom, through the pinned ffmpeg) agree byte for byte. `Av1DecoderConformanceTests` compares the planes of the
  library with them, and `Av1ToolCoverageTests` checks that the vectors reach every coding tool. The tools that no
  encoder of the pinned build produces (an interpolation filter per direction, a bilinear filter, predicted segment
  ids, the reference, skip and global motion features of a segment) come from hand-authored streams:
  `Av1HandAuthoredStream` in the generator writes inter frames bit by bit, from the specification, after a libaom key
  frame. They are recorded like any vector, with the digests both decoders agree on.
- **Third-party AV1 conformance streams** (the Argon suite, the libaom test vectors) have no license that allows
  committing them. They are run as a campaign after a change to the AV1 decoder: download them, point
  `MEZIANTOU_FRAMEWORK_IMAGING_AV1_DIFF_STREAMS` at the directory and run `Av1DifferentialInteropTests`, which compares
  the library with dav1d and libaom on every stream (Annex B streams are converted). No stream may fail. A stream does
  not count when a reference decoder rejects it, or when its sample format or color description changes (FFmpeg then
  converts the frames, which are no longer the samples of the stream); for those, compare with the `dav1d` command-line
  tool directly (`--alllayers 1`, its default, against `Av1Decoder.OutputAllLayers`: the reference digests of the Argon
  suite hold every shown frame of every layer, where a player and this test only take the highest layer of a temporal
  unit).
- Interop tests download ffmpeg, libwebp, libavif, libjxl (`cjxl`, `djxl`, `jxlinfo`, `jxl_from_tree`) and jxl-oxide from
  the release pinned by the `Meziantou.Prebuilt` package (same version in the test harness and the corpus generator). A download failure, an unexpected version or a missing
  capability fails the test; it is never skipped. The Apple ImageIO cases need `swift` and only run on macOS.
- Interop tests start every tool through `ProcessRunner` (test harness), never through `System.Diagnostics.Process`
  directly: on macOS, `Process` forks before .NET 11, which makes the thousands of tool runs of a test process wait on
  each other (the net10.0 run took five times longer than the net11.0 one), so the net10.0 build of `ProcessRunner`
  uses `posix_spawn` there.
- When implementing or changing a member, add unit tests, conformance tests with independent references, and interop
  tests for encoders.
- **Fixtures.** Every file of the corpus is listed in `manifest.json` with its hash, provenance and license; the manifest
  is validated before any comparison. Do not edit fixtures or the manifest by hand: change the generator and regenerate.
  Regeneration is an explicit, reviewed operation:

  ```shell
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- <generator>            # regenerate and diff
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- <generator> --write    # replace the fixtures
  dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- verify                 # re-decode with the pinned tools
  ```

  The `icc` generator (color profiles and LittleCMS vectors) also needs `--icc-profiles <directory>`: see the fixtures
  README.
- **JPEG XL fixtures** (`jxl` generator, `JxlCorpus.cs` and `JxlCorpus.Fixtures.cs`). A lossless fixture expects the
  pattern given to `cjxl`, or the values its `jxl_from_tree` tree sets; djxl and jxl-oxide must both give exactly that. A
  lossy fixture expects what djxl decodes, cross-checked with jxl-oxide, under a tolerance derived from the measured error
  of the library (`LibraryJxlMeasurements`): after a change of the lossy decoder, run
  `JpegXlDecoderConformanceTests.ToleranceIsTheMeasuredDisagreementWithTheReference`, copy the lines it prints into the
  table and regenerate. An invalid fixture is only added when both reference decoders reject it. The fields of the files
  (boxes, image header, frame headers, feature tags, hidden pixels) are read by `TestHarness/JpegXl/ReferenceJpegXl`,
  which is linked into the generator and has no dependency on the library; it does not read what is entropy coded (the
  ICC profile of a codestream, a permuted table of contents), so the ICC fixtures use profiles of the corpus and the
  conformance tests look them up by their hash.

  The coverage tables of the fixtures README are checked against the manifest (`FixtureCoverageMatrixTests`): update them
  when fixtures or feature tags change.
- **Fuzzing.** The default budget is small and deterministic. Longer campaigns are opt-in through
  `MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS`, `_FUZZ_DURATION_SECONDS`, `_FUZZ_SEED` and `_FUZZ_CASE_TIMEOUT_SECONDS`.
  A failure is minimized and committed to `FuzzRegressions/` with an entry in `regressions.json`.
- Failed golden comparisons write expected/actual/difference previews to `MEZIANTOU_FRAMEWORK_IMAGING_TEST_ARTIFACTS`
  (default: `TestArtifacts` next to the test assembly). Interop outputs go to
  `MEZIANTOU_FRAMEWORK_IMAGING_INTEROP_ARTIFACTS`.

## Documentation

- Every C# block of `readme.md` is a compiled snippet: put the code in `samples/Meziantou.Framework.Imaging.Samples`
  between `// begin-snippet: <name>` and `// end-snippet`, and precede the block with `<!-- snippet: <name> -->`.
  `DocumentationSnippetTests` fails when a block differs from its snippet, and `DocumentationExampleTests` runs the
  examples of `Examples.cs`.
- Update the support matrix and the limitations of `readme.md` when a format, a setting or a default changes
  (`DefaultsTests` pins the defaults).

## Public API and versioning

- Every public API change is intentional. The build regenerates `ref/Meziantou.Framework.Imaging.cs`: commit it and review
  its diff. `ApiSurfaceTests` pins the shape of the API (namespaces, sealed and immutable settings, exception
  constructors, dependencies, target frameworks).
- Semantic versioning. Part of the contract: the public API, the behavior documented in `readme.md` and the XML
  documentation, decoded pixels (exact for lossless formats, within the documented tolerance for lossy ones), timing and
  play counts, and error categories. Not part of the contract: the exact encoded bytes, exception messages, internal
  types and performance numbers.
- Enums can gain members in minor versions (new formats add `ImageFormat` members).
