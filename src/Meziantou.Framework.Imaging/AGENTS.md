# Meziantou.Framework.Imaging

A fully managed image library: PNG/APNG, GIF, JPEG, WebP, QOI, BMP, TGA, Netpbm, TIFF, ICO/CUR, ANI, AVIF (with its
own AV1 decoder and encoder), HEIC (with its own HEVC decoder and encoder), JPEG XL (lossless and lossy,
progressive) and JPEG 2000 decoding and encoding, lossless JPEG recompression (`JpegXlTranscoder`), animation-aware processing,
vector drawing, font files (TrueType, OpenType, WOFF, WOFF2), ICC color conversion, and bounded-memory streaming
readers and writers.

These instructions complete the repository-level `AGENTS.md`. The user guide is [readme.md](readme.md): it states what
the library guarantees to its callers (support matrix, limitations, defaults). Keep the code, the XML documentation and
`readme.md` consistent in the same change.

## Hard constraints

- Target frameworks are exactly `net10.0;net11.0` for the library and every test, sample and benchmark project. Do not
  add older targets or compatibility shims.
- Fully managed, no native dependency, and two package dependencies of this repository: `Meziantou.Framework.Unicode`,
  the Unicode algorithms text layout needs (properties, normalization, scripts, segmentation, bidi), which is itself
  fully managed and independent of ICU, and `Meziantou.Framework.Fonts`, the font reader and the shaper (see its
  [AGENTS.md](../Meziantou.Framework.Fonts/AGENTS.md)), whose internal API the library uses with an exact version.
  Nothing else (`ApiSurfaceTests`). The library is trimmable and NativeAOT-compatible
  (`IsTrimmable`, `IsAotCompatible`): no reflection-based code.
- Write implementations from the format specifications. Never copy or port code from other image libraries
  (System.Drawing, libpng, libjpeg, giflib, libwebp, libavif, libaom, dav1d, stb, ...) or from font libraries (FreeType,
  HarfBuzz, the woff2 reference code, fontTools, ...). Those libraries are test oracles only: their output is compared
  with ours, their source is never read to write ours.
- No public allocator, custom pixel type, codec plug-in or image-processor interface. Implementation types are `internal`
  and live under `Internals/`.
- Public namespaces: `Meziantou.Framework.Imaging`, `.Metadata`, `.Formats` (encoder settings), `.Drawing` (brushes,
  pens, paths, the canvas) and `.Typography` (fonts). The base types of the model (`Image`, `ImageFrame`, `ImageFrameCollection`, `ImageEncoder`,
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
- **PNG `MinimumSize`** (`PngOptimizedEncoderSession`, a session of its own): the default session must keep writing the
  same bytes, so the search never goes through it. Candidates are compressed to a byte counter
  (`PngImageDataEncoder.StartSource` with `count: true`), in bands, and the winner is compressed again for real; nothing
  is buffered. Every still image keeps the layout of the pixel format among the candidates and the level of the default
  encoder among the trials, so it is never larger than the default output (the conformance suite asserts it for the
  corpus, animations included). Keep these findings:
  - A lower zlib level is sometimes smaller than `SmallestSize` (zlib-ng uses other match strategies): the last stage
    tries `Fastest`, `Optimal` and `SmallestSize`.
  - FFmpeg 9 misplaces the packed samples (1, 2 and 4 bits) of an APNG frame region that does not start at column 0, even
    on a byte boundary; full-canvas frames are fine. Animations therefore use 8-bit samples at least.
  - FFmpeg's conversion of 8-bit PNG output to `rgba64le` is not exact (errors of a few units): the interoperability
    tests ask FFmpeg for the native layout of the file (`PngEncoderInteropTests.GetNativeRawFormat`) and widen it
    themselves.
  - The harness reader decodes every color type and bit depth to 16-bit RGBA and composites APNG frames
    (`ReferencePng.Canonical.cs`), rejecting any OVER pixel whose result depends on the rounding of a decoder; verify
    `MinimumSize` output with `OptimizedPngOutputVerifier`, never with `ApngOutputVerifier` (full-canvas frames only).
- **HEIF container** (`Internals/Codecs/Heif`), shared by AVIF and HEIC: there is one implementation of the container
  rules. The walker (`HeifStructureParser`) resolves the image (`HeifImagePlan`, from the `meta` box) or the image
  sequence (`HeifMovieBox`, from the `moov` box) before any data is read, then captures the items or samples as their
  bytes go by.
  - Everything that depends on the codec goes through the `HeifCodecBinding` of the format (`AvifBinding`,
    `HeicBinding`): the brands, the types of the coded items and of the sample entries, the configuration record, the
    header of the bitstream, the pixel decoder, and the errors (`binding.Invalid`, `binding.Unsupported`), which carry
    the `ImageFormat` and the name of the format. Never test for a codec in the shared code, and never write "AVIF" or
    "AV1" in one of its messages: use `binding.Name` and `binding.CodecName`.
  - A container feature is implemented once, and a format reads it when its binding declares it
    (`HeifCodecBinding.ContainerFeatures`): premultiplied alpha, derived images, edit lists that select parts of the
    media, fragments and the matrix coefficients 10 to 14 are read by AVIF and still rejected by HEIC, whose corpus
    holds their rejections. Turn a flag on for a format together with its fixtures (the rejection moves to a valid
    fixture with independent pixels) and its readme rows; the image that is built from other images also needs the
    decoder of the format to render it (`AvifDecoder.Derived.cs`).
  - The format of a file is only known after its file type box. The codec that detected the file gives the first
    candidate; the brands give the formats the file declares, and a file that declares both has the format of the
    codec of its primary item, or of its color track. A generic major brand (`mif1`, `msf1`, `miaf`) is claimed by
    `AvifCodec`; `ImageCodec.GetDetectedFormat` refines what `Image.DetectFormat` reports when the prefix holds the
    whole file type box. The pixel decoder is created by `HeifDecoder` once the format is resolved.
  - A sequence gives its samples to the decoder in decoding order (`OnSequenceSample`) and asks for its images in
    presentation order (`TryWriteSequenceFrame`): a codec that reorders its images needs later samples first.
  - The rules of a format that the other one does not have are tested in its own project (`AvifDecoderTests`,
    `HeicDecoderTests`), on files assembled by `HeifFileBuilder`; the shared rules are tested once, with AVIF files.
- **AVIF and AV1** (`Internals/Codecs/Avif`). The AV1 decoder (`Av1/`) follows the AV1 Bitstream & Decoding Process
  Specification section by section and keeps its names: a comment names the section a method implements. Its constant tables
  (`Av1/Tables/*.g.cs`) are generated from the published specification by
  `tools/Meziantou.Framework.Imaging.Av1TableExtractor` (the specification text is not committed; the tool pins its commit
  and the hash of each file): never edit them by hand. Every plane the decoder produces must stay bit-exact with the
  reference decoders: there is no tolerance in AV1 decoding. `Av1Decoder.Tools` reports the coding tools a bitstream used;
  it is a diagnostic for the tests and never changes the decoding.
  Scalability only changes which frames are output, never how they are decoded: the units outside the operating point
  are dropped, and a temporal unit with several spatial layers outputs the frame of its highest layer (what a player
  shows, and what `lsel`-less layered AVIF images present). `OutputAllLayers` is for conformance tests only.
  An `iden` item is resolved by `HeifImagePlan` into the transformations of the coded image it presents (no pixel
  moves; the alpha plane of a derived image must be presented as its colors). An image that is built from other
  images (an `iovl` item, a grid of derived images) is an `HeifImageNode` tree, rendered by `AvifDecoder.Derived.cs`
  to straight RGBA surfaces: only that path moves or composites pixels, the direct path (a coded image or a grid of
  coded images, through identity derivations) must stay as it is. Nothing converts colors there: every color
  description of the tree is the same one or the file is rejected, and the structure parser validates the
  descriptors and the fill colors before the first pixel is decoded. The edit list of a track that selects
  parts of the media is an `HeifEditTimeline` (exact intersections in units of the two time scales; the images that
  are left out are decoded and charged as hidden pixels, and every sample is located then). A fragmented sequence
  (`mvex`) is read by `HeifMovieFragment`: each `moof` box appends samples to `HeifTrack`, whose tables only hold the
  samples that are still to be read, and the walk ends with the input (`ParseStatus.NeedMoreDataOrEnd` at a box
  boundary). The independent readers are `ReferenceHeif.GetIdentityChain`, `Track.GetPresentation` and its own `moof`
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
  - A tool is enabled at the efforts where the `avif-quality` report (`benchmarks/Meziantou.Framework.Imaging.Benchmarks`) shows that it
    makes its inputs smaller at equal quality, and stays out otherwise: the Limitations row of the readme lists the
    tools that were measured and left out, with their figures. Measure before and after with the report (it takes a
    filter on the names of its inputs), and add an input when the tool needs content that the report does not have.
  - A frame of a sequence that is not a key frame is an inter frame whose only reference is the previous frame
    (`LAST_FRAME`; every slot is refreshed by every frame). An inter block is written from the motion vector stack that
    `FindMvStack` builds, like a decoder: the stack reads which blocks of the frame are written, so the search marks
    the blocks of a region as not written (`RefFrameNotWritten`) whenever it restores the region. The motion search
    only proposes a motion vector: the block is then encoded like any candidate.
  - The container writer (`HeifFileWriter`) shares no code with `HeifFileBuilder`, the builder of the test harness.
- **HEIC and HEVC** (`Internals/Codecs/Heic`). `HeicBinding` is the HEIC side of the HEIF container: the `hvcC`
  configuration record (`HeicConfigurationRecord`) holds the parameter sets, which `HeicDecoder` gives to the HEVC
  decoder before the data of an item or of a sample (NAL units preceded by their length).
  - The HEVC decoder (`Hevc/`) follows Rec. ITU-T H.265 clause by clause and keeps its names: a comment names the clause
    a method implements. The specification is a PDF, so its constant tables (`Hevc/Tables`) are transcribed by hand,
    each with its edition and table number, and what the specification defines by a procedure (scans, derived arrays)
    is computed. Every plane must stay bit-exact with the reference decoders, in every chroma format at 8, 10 and 12
    bits: there is no tolerance in HEVC decoding. `HevcDecoder.Tools` reports the coding tools a bitstream used; it is a
    diagnostic for the tests and never changes the decoding.
  - No concealment: a missing reference picture, an incomplete picture, a slice that does not start where the previous
    one ended or a value outside its range is `InvalidImageContentException`; the screen content, multilayer and
    extended-precision extensions, bit depths above 12 and separate color planes are
    `UnsupportedImageFeatureException`. Only the base layer is decoded.
  - The decoder is given access units in decoding order and outputs pictures in output order (clause C.5.2): the
    container feeds the samples of a track until the decoder outputs the next image, and the composition offsets of the
    track give the durations of the images in that order. The caller releases each picture it is given.
  - What a file does not say is what H.265 infers: without `colr` box and without video signal description in the
    bitstream, the samples of a color image use the limited range (clause E.3.1), as libheif and Apple ImageIO read
    them, and not the full range that MIAF gives to an image without color information; an alpha plane is in full range
    unless its bitstream states the limited range (`HeifBitstreamFormat.IsRangeSignaled`). The chroma samples of a
    4:2:0 picture are at the left of their luma samples (`chroma_sample_loc_type` 0) unless the bitstream says
    otherwise, and those of a 4:2:2 picture at the position of the luma samples of even columns.
  - The pictures of a stream can be larger than the image, since the conformance window of a picture with subsampled
    chroma counts in chroma samples: the `clap` property of an item, or the `clap` box of the sample entry of a track,
    then selects the image. The writer codes pictures of even sizes and writes that box for an image of an odd size.
- **HEVC encoder** (`Internals/Codecs/Heic/Hevc/Encoding`, and the `Write` and `Encode` parts of `HevcSliceCoder`). It
  follows the rules of the AV1 encoder: written from the specification only, it shares the state and the processes of
  the decoder (prediction, reconstruction, deblocking, sample adaptive offset), its parameter sets and slice headers are
  written (`HevcHeaderWriter`) then parsed back by the parsers of the decoder, and every decision is made with integers.
  - A candidate is encoded for real. The winner of a coding unit is written once, in syntax order, through
    `HevcBinSink` in cost mode, which records it, gives its exact rate and leaves the picture state as a decoder has
    it; the records are then replayed to the arithmetic encoder, for each coding tree unit, or for the whole picture
    after deblocking when the sample adaptive offset is on (its syntax precedes the coding tree unit it applies to).
    `HevcEncoderRoundTripTests` (the decoder of the library must output the reconstruction of the encoder) and
    `HevcEncoderInteropTests` (FFmpeg, libde265 and HM must output the same planes) are the tests to run after any
    change. FFmpeg 9.0.2 is exempt for 12-bit streams whose slice QP is 50 or 51, which it decodes to other
    pictures with no error while libde265 and HM agree (HM also verifies the picture hash): the case fails the day
    FFmpeg agrees.
  - The streams only use tools of version 1 of the specification, whatever their profile: lossless output is the plain
    `cu_transquant_bypass_flag`, because the reference decoders do not all read the lossless tools of the range
    extensions the same way. A stream declares the narrowest profile that holds it (`HevcProfile`), which selects the
    brand of the file (`HeicBinding.WriteFileTypeBrands`, from the color images only).
  - A sequence is IDR pictures and P pictures that refer to the previous picture, in output order: the encoder writes no
    composition offsets. The parameter sets are in the configuration record (`hvc1`), never in the samples.
  - `HeifRgbConverter` computes subsampled chroma where the codec puts it: at the luma samples of even columns for HEVC
    (`HeifCodecBinding.HasCositedChromaColumns`), between two columns for AV1. The video usability information always
    states the range, the color description and the chroma sample location.
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
  noise, color transform, blending. The sRGB curve of the XYB output computes its power with two polynomials
  (`JpegXlColorTransform.PowerFiveTwelfths`, 3e-7 relative, against 1.1e-7 for `MathF.Pow`), in the vector paths and
  their scalar reference alike: it was 27 % of the instructions of a lossy decoding. The other transfer functions (BT.709,
  PQ, HLG, gamma) still call `MathF.Pow` per sample: no workload reaches them.
- **JPEG XL encoder** (`JpegXlEncoderCodec`, `Internals/Codecs/JpegXl/Encoding`). The headers are written, then parsed
  back by the readers of the decoder: the geometry of a frame is never derived twice. The lossy encoder
  (`JpegXlVarDctEncoder`) mirrors `JpegXlVarDctDecoder`: its forward DCT undoes the halving steps of the inverse DCT
  of the decoder in the opposite order, and its LF matrices and its color matrix are measured on, or inverted from,
  what the decoder computes (`JpegXlForwardTransform`, `JpegXlForwardColor`); a coefficient is written with the
  context derivation the decoder reads it with (`JpegXlCoefficientContexts`). The forward DCT was a product by bases
  measured on the decoder, O(N^2) per line: the halving one gives the same coefficients within 3e-8 and the same files
  but for a few bytes, with 2.4 to 3.2 times fewer instructions for the 64x64 and 8x8 transforms. The blocks of a VarDCT
  frame do not predict each other, so the encoder does not reconstruct what it writes; its decisions use float32 in a
  fixed order. `JpegXlEntropyCodeBuilder` clusters the contexts of the streams that share a code. Every Modular
  sub-stream, of a lossless frame or of a lossy one (LF image, block fields, alpha channel), goes through
  `JpegXlEntropyEncoder`, which writes the smaller of prefix codes and ANS at every effort
  (`JpegXlEntropyCoding.Smallest`, decided from the token counts): a prefix code spends one bit per sample at least,
  which made a constant channel larger than the colors, and the description of an ANS distribution is longer than a
  small group. The effort only selects what is searched; `jxl-quality` shows the sizes per effort. A change of the
  encoder is checked by the interop tests (djxl and jxl-oxide must read the file like the decoder of the library does).
- **Lossy JPEG XL quality tools** (`JpegXlVarDctEncoder`). Each one is kept or left out on measurements, not on what
  other encoders do: file size at equal PSNR, SSIM, MS-SSIM, XPSNR and VMAF (FFmpeg and the `vmaf` tool on djxl output)
  over photographs of several sizes, with cjxl at its efforts 3 and 7 as the yardstick. Measure again before changing a
  constant, on real photographs: the synthetic patterns of the tests are noise and say nothing.
  - *Transform selection* (`ChooseSquare`, `GetTransformCost`): squares of 16, 32 and 64 samples and their halves
    against their quarters, on the luma, by `bits + weight * squared error`. The squared error of the samples is exact
    (every DCT of the format has basis functions of the same energy); the bits are a model fitted on frames coded with
    one transform, to a few percent. What names a transform in the block list is part of the cost (about four bits):
    without it the selection loses on small images. The transforms above 16x16 add about 0.1 % on photographs (more on
    smooth graphics) and can double the encoding time, hence their effort.
  - *The transforms of one block other than its DCT are not written* (the DCTs of 4x4, 4x8 and 8x4 samples, the 2x2
    transform, the identity, AFV). They were tried, after the quantization field: their forward transform is the
    inverse of the 64x64 matrix measured on the decoder, the error of the samples comes from the same matrix, and djxl
    reads the files. With the cost of the other transforms they are chosen where they make the file larger, since the
    cost only measures the luma and its bit estimate was fitted on DCTs; a number of bits added to their cost (8 to
    128) removes the loss without giving a gain. File size at equal quality, effort 5, on five photographs (PSNR-Y,
    SSIM, XPSNR, MS-SSIM, VMAF), with 48 bits added: 4x4 +2.4 %, +11.5 %, +5.6 %, +22 %, +25 %; identity +4.7 %, +8.9 %,
    +6.1 %, +15 %, +8.6 %; 2x2 +0.1 %, +5.3 %, +2.0 %, +11 %, +8.2 %; AFV +2.1 %, +6.4 %, +3.7 %, +11 %, +6.5 %. The two
    halves of a block (4x8 and 8x4), with 64 bits added, are the only ones near a gain: -0.4 %, +0.3 %, +0.1 %, +1.1 %,
    +0.4 % on the photographs, and -1.7 %, -2.0 %, -1.5 %, -1.0 %, -0.3 % on two screenshots of text and three graphics
    (the 2x2 transform is 7 % smaller on text for PSNR and SSIM, and larger for MS-SSIM and VMAF). Not kept: they do
    not reduce the size of photographs, and what is added to their cost keeps them out of the blocks they suit best (one
    edge in the middle of a block is cheap for the 8x8 DCT too). A cost that measures the three channels is what to
    try before trying them again.
  - *The block list* is a Modular channel whose first row holds the transforms and whose second row holds the
    quantization factors: the tree gives each row its own context and predictor (`firstRowChannel`), which made the
    list 15 % smaller on a photograph.
  - *Gaborish* (`JpegXlInverseGaborish`, `UsesGaborish`): the encoder sharpens with `3 I - 3 S + S^2`, three terms of the
    inverse of the smoothing `S` of the decoder, computed from its weights. It is smaller at equal PSNR, SSIM and XPSNR
    (5 to 8 %) and larger at equal MS-SSIM (3 %) and VMAF (10 %): it stays because it removes block edges, with the
    distance scaled so that a quality keeps its file size. Above quality 90 the pair loses fine texture that the
    quantization would keep (a PSNR ceiling near 46 dB on a textured image), so it is off. A 5x5 least-squares inverse
    does not keep a constant (2.6 % of gain at the frequency 0): do not use one.
  - *Adaptive quantization* (`JpegXlAdaptiveQuantization`, `ComputeQuantField`): a quantization factor per block,
    which moves bits from texture to flat areas and to the neighborhood of edges. Unlike the other tools it is not
    kept on the measurements: none of the five metrics rewards that redistribution. Measured on the ten images,
    effort 5, file size at equal quality against one factor for the frame: PSNR-Y +4.7 %, SSIM 0.0 %, XPSNR +4.7 %,
    MS-SSIM +0.1 %, VMAF +3.9 %; against cjxl at its default effort the encoder stays smaller (-5.3 %, -4.6 %, -4.8 %,
    -5.5 %, -0.5 %). Three simpler fields (a factor that follows the activity of the block: 8x8 blocks, 16x16 cells and
    64x64 tiles; relative to the mean of the frame; with a floor that protects edges) were 2 to 25 % larger on the five
    metrics and visibly softer on textures: do not go back to one. The field is written from the effort 3: its pass
    costs 0.43 billion instructions for 1024x768 samples, 27 % of an encoding at the effort 5 and 41 % of one at the
    effort 0; below, every block has the factor of the frame. How the encoder uses it:
    - the field is computed before the transforms are chosen (a first pass over the frame, which converts it to XYB a
      second time rather than keeping its planes), so the scale of the frame comes from the field of the 8x8 blocks, not
      from the factors of the transforms;
    - a candidate transform is costed with the factor it would have, and its error is counted in the steps the field
      gives its blocks, so that two choices for the same blocks are compared on the same error;
    - `FieldScale` gives a quality the mean file size it had with one factor (0.91 at quality 20 to 1.10 at quality 92
      on the ten images); the LF image keeps its own quantization, and the scale of the frame leaves it a factor of 10
      at least;
    - the field is computed from the image once: the encoder does not decode the frame to measure it and refine
      the field.
- **Progressive reading.** `ImageReader.ReadFrameRefinementInto` is the only way to see a frame before it is complete.
  The walker stops at the lengths the decoder asks for (the end of the sections of a stage); an intermediate rendering
  decodes the sections again from the buffered payload (`JpegXlFrameDecoder.DecodePartial`), never changes the state of
  the decoder (no reference frame is saved), and counts as hidden pixels. The frame itself always goes through the
  same code as an eager load: the last rendering is the frame of `Image.Load`, byte for byte. A truncated input gives
  its complete stages, then fails: truncation is never a clean end.
- **JPEG recompression** (`JpegXlTranscoder`; `JpegXlJpegRecompressor`, `JpegXlJpegReconstructor`,
  `JpegXlJpegReconstructionData`, and `JpegBitstreamReader`, `JpegBitstreamWriter`, `JpegBitstreamModel` in
  `Internals/Codecs/Jpeg`). It works on files, never on an `Image`. `JpegBitstreamReader` is strict where the decoder is
  tolerant: it accepts nothing `JpegBitstreamWriter` does not write back byte for byte, and the recompressor checks
  both the rewritten JPEG file and the file its own output rebuilds before it returns. The layout of the `jbrd` box and
  the integer chroma from luma rule were established against files written by cjxl, since the standard is not
  available: a change there needs the interop tests (djxl and jxl-oxide rebuild the files of the library, the library
  rebuilds the files of cjxl). The library reading back what it writes proves nothing about the layout: the two lists
  of a scan (reset points, extra zero runs) were once written list by list instead of scan by scan, which only djxl
  refused, and only for a file of several scans that has them; cjxl and the JPEG encoders of the corpus never write
  such a file, `JpegTestImage` does (`FilesWithRedundantSymbolsGoThroughBothChains`). The reconstruction data may
  follow the codestream, which no encoder writes (cjxl puts them between two `jxlp` boxes, after the image header);
  jxl-oxide rebuilds such a file and djxl does not. `JpegXlJpegReconstructor` keeps a frame that could be the one of
  a JPEG file as it is coded (`IsJpegFrame`) until the end of the container, and `JpegXlReconstructionProbe` reads
  such a container to its end; any other frame without data before it ends the parsing at once, and a bare
  codestream at its header. What `JpegBitstreamReader` accepts was widened the same way, on files made for it
  (`JpegTestVariants`), each kept only once djxl rebuilt the file of the library and the library the file of cjxl:
  bytes after the last block of a scan are data between markers; a component takes the quantization table its slot
  holds when its own first scan starts, so tables may be defined, or defined again, between scans; a table that no
  component uses is described as a copy of the table before it in the file, whatever its slot (the only form cjxl
  writes and djxl rebuilds), and any other unused table is refused. cjxl refuses a slot that is used again for other
  values between two scans, which the library writes and djxl rebuilds. The JPEG entropy coding shares `JpegBitWriter` and `JpegHuffmanEncoder` with the JPEG
  encoder: there is still one JPEG implementation of each direction of pixels, and one of coefficients.
- **Lossless JPEG** (`JpegLosslessScanDecoder`, T.81 Annex H, SOF3). It is the sequential decoder with a data unit of
  one sample instead of a block (`JpegFrameComponent.DataUnitSize`): the same scan structure, restart handling, planes,
  band ring and row writers, so an MCU row is `Vmax` lines. A scan decoder keeps the differences of an MCU row and
  reconstructs its lines when the row is complete, from the last line of the previous row
  (`JpegFrameComponent.Differences`); the samples are predicted from one another before the point transform is
  undone, and the planes hold them after. The samples that do not have 8 bits (lossless frames have 2 to 16) go
  through `JpegDeepRowWriter`, whose arithmetic must hold 16-bit samples. A sample that does not fit in the precision
  of its frame is an error, not a wrap: a conforming encoder never codes one. Reference decoders: libjpeg-turbo for
  the files `cjpeg -lossless` writes (it refuses to convert the colors of a lossless YCbCr file), FFmpeg for the YCbCr
  files its `ljpeg` encoder writes (with its BGR pixel formats it writes a frame of 9-bit samples that are not the
  pixels: such a file is read as what its frame header says, or rejected when a sample does not fit), and FFmpeg
  9.0.2 misreads the category 16.
  Apple ImageIO does not read lossless files reliably. The process with arithmetic coding (SOF11) is not decoded: no
  tool at hand writes or reads it.
- **JPEG 2000** (`Internals/Codecs/Jpeg2000`). Written from Rec. ITU-T T.800 (Part 1) and T.814 (Part 15, the
  high-throughput block coder, `Jpeg2000/Ht`); OpenJPEG, OpenJPH and FFmpeg are test oracles only. The structure parser
  reads the boxes of a file and gives the codestream to `Jpeg2000CodestreamWalker`, which asks for whole marker
  segments and streams the tile-part bodies into the buffer of their tile; a tile is decoded when its last tile-part
  arrives (`Jpeg2000TileDecoder`: geometry, every packet, the planes, the code-blocks, the inverse transformations),
  then released. A header identification stops at the first SOT marker, and the EOC marker is required: truncation is
  never a clean end.
  - A block decoder returns a coefficient as its sign and twice its magnitude, with the bit-planes that are not coded
    replaced by half of their range (`Jpeg2000Tier1Decoder` and `Jpeg2000HtBlockDecoder` share this contract). The
    reversible path halves it, the irreversible path multiplies it by half of the step size: missing coding passes are
    ordinary content, not an error.
  - The reversible path (5-3, RCT, region of interest, palette, depth scaling) is integer arithmetic and exact. The
    irreversible path (9-7, ICT) is `float` with a fixed order of operations and no fused multiply-add; it is compared
    with OpenJPEG under a tolerance pinned to the measurement, like JPEG XL.
  - The wavelet and component transformations have `Vector<T>` paths (`Jpeg2000Dwt`, `Jpeg2000ColorTransform`). A
    lifting step is one operation on every sample of one parity: it is applied to the two halves of a row while they
    are separated, and to strips of `ColumnStrip` columns copied to the scratch of the tile. The filters on interleaved
    signals (`LiftReversible`, `LiftIrreversible` and their forward versions) stay the reference, and each vector path
    equals its `...Scalar` version bit for bit, in both directions (`VectorizedKernelTests`).
  - The MQ decoder keeps the LPS sub-interval at the bottom of the interval, as T.800 Annex C does; software decoders
    often use the opposite convention, and mixing the two decodes zeros. `Jpeg2000MqCoderTests` holds the test
    sequence of T.88.
  - The CxtVLC tables of T.814 Annex C (`Jpeg2000HtVlcTables`) are generated from the text of the Recommendation; the
    lookup is built when the type is first used and fails if a context is not a complete prefix code. The decoder
    follows the bit-stream procedures of clause 7 bit by bit, with their names.
  - Where the Recommendation leaves a choice, the reference decoders decide, and where they disagree with it, the
    Recommendation does: Zppt restarts in each tile-part header (OpenJPEG counts it per tile, FFmpeg only reads a PPT
    marker segment in the first tile-part), an HT cleanup magnitude may use every bit-plane (FFmpeg refuses it), a
    codestream of three components whose last two are sub-sampled alike is sYCC (both decoders read it so). The
    fixtures record each disagreement with its reason.
  - The encoder (`Jpeg2000EncoderCodec`) is one tile, one layer, LRCP, without rate control; a lossy output codes the
    opacity reversibly through COC and QCC marker segments. Its quantization steps come from the gains of the synthesis
    kernel of the library, measured, not from published tables.
  - The lossy quality is calibrated on `JpegEncoder` (`Jpeg2000QuantizationDesign`): the base step of the luminance is
    `0.47 * s^(3/4)` levels, `s` being the scale of the JPEG quality, and the two chrominances have twice that step (a
    QCC marker segment each). The constants were fitted on photographs of several sizes, so that a quality has the SSIM
    of the JPEG file of the same quality: of the five metrics measured (PSNR, SSIM, XPSNR, MS-SSIM, VMAF, from FFmpeg
    and the `vmaf` tool), SSIM asks for the step in the middle. At the same quality, PSNR and XPSNR are then up to 2 dB
    above JPEG, MS-SSIM about 1 dB below, VMAF 1 to 3 points below up to the quality 50 and the same from 80, and the
    file is about two thirds of the JPEG one (half of it at the quality 20). The ratio of the chrominances is what the default 4:2:0 sub-sampling of the JPEG encoder
    costs them; it does not grow with the quality as that cost does. The power is computed from square roots, so that
    the output is the same on every platform. Measure again before changing a constant or the quantizer, on real
    photographs: `TheQualityFollowsTheScaleOfTheJpegEncoder` only keeps the two scales together on a synthetic image.
  - A TIFF strip or tile compressed with JPEG 2000 is decoded by the same code through `Jpeg2000SampleDecoder` and a
    `Jpeg2000SampleReceiver`: the page says what the components are.
- **TIFF** (`Internals/Codecs/Tiff`). `TiffPage` resolves and validates a directory without reading a pixel, so every
  rejection also comes from `Identify`, except the ones of the compressed data. `TiffBlockReader` turns a strip or a
  tile into rows of samples (fill order, compression, predictor, YCbCr data units) and `TiffRowExpander` turns the rows
  into pixels (packed samples, palette, inks). The decompressed size of a block is always known first: a decompressor
  fills a buffer of that size, it never grows one, and it never repairs data. LZW, PackBits and the CCITT compressions
  are written from TIFF 6.0 and ITU-T T.4 and T.6; the code words of `TiffCcittCodes` are transcribed from the
  recommendations, and the tests compare strips with code words spelled out from the same tables. A JPEG strip is
  decoded by `JpegDecoder` given a `JpegSampleReceiver` and written by the session of `JpegEncoderCodec`: there is no
  second JPEG implementation, and the page says what the components are. `BitsPerSample` is the precision of the
  datastreams of a JPEG page (8 bits, 12 bits, or 8 to 16 bits when they are lossless): the decoder of a datastream
  tells its precision (`JpegSampleReceiver.OnFrame`) and hands over its samples of more than 8 bits scaled to 16 bits
  (`OnDeepRow`), so a decoded block holds samples of `TiffPage.DecodedBitsPerSample` bits, in the byte order of the
  file like the other 16-bit samples. The layouts of `TiffEncoder` store a page
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
  proven so in `VectorizedKernelTests`. `ColorConversionKernels.Convert` converts 64 colors at a time stored by
  component (`IccStage.ApplyBatch`): the stages of matrix-based conversions loop over the colors (the matrix in vectors,
  each lane with the products and sums of the scalar stage, in the same order, without fused multiply-add), the others
  apply their scalar stage to each color.
- The source of `ConvertColorProfile` may be the CICP color description of an image without profile, or the `cicp`
  tag of an ICC profile of PQ or HLG samples (`IccProfile.Cicp`, whose curves only approximate the functions)
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
  The one exception is the managed rasterizer of Meziantou.Framework.WebPlatform (same author), whose semantics
  (validated on the web-platform-tests) are the reference of what a browser engine needs: its code may be ported where
  it fits the rules of this library, which win where they differ (exact areas, no functions of the platform in the
  geometry, 16-bit precision).
- The numerical contract is documented in `readme.md` ("Rendering") and in the XML documentation of
  `CoverageRasterizer`, `DrawingKernels` and `GradientEvaluator`: change it only with them and the tests.
  - Coverage is the exact area of the pixel inside the shape (the union of overlapping contours), in 1/65536 steps,
    ties upward. The rasterizer is a sweep line with lazily recorded boundaries; its row scratch is sized up front from
    the largest number of edges on one row.
  - Compositing is exact integer source-over (`ulong`), the generalization of `AnimationCompositor.BlendOver8/16` to a
    fractional source alpha. Colors are converted to the pixel format first (`PixelConverter` rules).
  - The other operators of `CompositeMode` (a property of the brush, so that the canvas has no state) have one kernel
    for all of them, from the two factors of the operator; source-over keeps its own, unchanged. A paint without
    opacity is skipped only with the operators it changes nothing with (`IsTransparentPaintInert`): with the others it
    erases. `ReferenceCompositor` has the same operators from their definition, in rational numbers.
  - A `BlendMode` (a property of the brush, like the operator) replaces the color of the paint pixel by pixel before
    the kernels above (`CompositeBlended`): the functions are in `BlendFunctions`, shared with the paints of color
    glyphs (`ColorSurface`), in double precision without any function of the platform. The kernels do not know the
    order of the samples: the caller says when blue comes first, which only the modes that take a whole color need.
  - Gradients interpolate premultiplied values of the converted stops, evaluated per pixel at its center. With a
    `GradientColorSpace` other than `Encoded`, the stops stay 16-bit sRGB colors, are mixed in linear light or in
    Oklab (`SrgbTransfer`, `Math.Cbrt`: the one place of the drawing code that uses functions of the platform), and
    the 16-bit result is converted like a brush color; the color of a stop is always written as it is.
- `PaintProfile` is the one way a color profile reaches a drawing: the colors the caller gives go through
  `PaintColorConverter` (the pipeline of `Image.CreatePaintPipeline`, whose destination is the profile of the image,
  or its CICP color space through `IccPipeline.Create(IccProfile, CicpColorSpace, ...)`) before `ConvertDrawingColor`.
  What is samples of an image already is never converted (drawn images, layers, tiles, color glyphs). Linear light
  with an ICC profile that is not sRGB uses the tone curves of the profile (`LinearLightCurves`, tables charged to
  the scope, carried by `DrawingSource.Curves`). The session that draws the tile of a pattern has another image:
  it takes the color space, the curves and the pipeline of the session it comes from (`_colorImage`), never those
  of the tile.
- A color matrix (`ColorMatrixKernel`) is applied to stored samples at their precision, with additions and
  multiplications only; a pixel that is covered entirely is stored as computed (`Write`), so that
  `CreateGrayscale()` is `Grayscale()` bit for bit, and only a pixel on the edge of a clip goes through the
  `Source` operator.
- Only `+ - * /` and `Math.Sqrt` in the geometry (no trigonometry, no fused multiply-add), and every order that
  influences a sum is total (events and crossings are sorted with a tie-break): results must not depend on the
  platform, the framework version or a sorting algorithm.
- A path is transformed exactly (`VectorPath.Transform`): the control points of its curves, and an ellipse as its
  center and two conjugate half-diameters (`PathCommand.EllipseAxes`), flattened like the circle it is the image of.
  Never replace an ellipse by Bézier curves. `AffineTransform` gets its sines and cosines from `DeterministicAngle`.
- An arc is stored as pieces of at most a quarter turn (`ArcPiece`, `ArcGeometry`): the ellipse, the unit vectors of
  both ends and the exact end point. Only the ends of the pieces come from angles (`DeterministicAngle`); the points
  inside a piece come from bisection, as for round joins. The end point is given, never computed: a shape made of arcs
  and segments (a rounded rectangle) has no gap and no segment of a few units of the last place, which a stroke would
  turn into a spike. The shapes with arcs are written once over `IPathReceiver`, for the builder and for the sinks.
- The queries of a path (`Contains`, `GetLength`, `TryGetPointAtDistance`, `Widen`) flatten it with `PathFlattener`
  into a sink, as drawing does, so that they answer for what is drawn; `GetTightBounds` (`PathBounds`) is computed from
  the curves, and contains their chords. `Widen` runs the `Stroker` into a path builder: the same pieces in the same
  order as a draw call, so filling the result is stroking, bit for bit (`FillingAWidenedPathIsStrokingThePath`).
- `EnumerateSegments` is the only public view of the elements of a path: it gives what the builder takes, never the
  internal form (an `ArcPiece` becomes the arc of SVG, through `ArcGeometry.GetAxes`; an ellipse becomes two arcs).
  `Reverse` (`PathReverser`) works on the elements, exactly; `Trim` (`TrimSink`) works on the chords, like the other
  queries, and its result is a polyline.
- `Combine` and `Simplify` (`PathCombiner`) work on the chords of the paths, moved to a grid of 1/1024 of a pixel,
  with exact integer arithmetic (`Int128` products): cut the segments where they meet until they only share ends,
  merge equal segments into edges with a count per path, sweep the rows of vertices for the winding numbers on both
  sides of each edge, then follow the edges whose sides differ. Never decide anything there with a floating-point
  comparison. An end within 1.5 unit of a segment cuts it (`AddCutWhenNear`): the pieces of a stroke share sides that
  the grid moves apart, and would leave cracks in the outline; that cut must shorten the segment, or three close
  vertices cut each other without end (the pass and growth limits are only a safety net). The result is canonical
  (contours from their top-left vertex, in the order of their first edge): tests compare path data.
- `Offset` (`PathOffsetter`) is two runs of the combiner around a stroke: the outline of the area, the stroke of that
  outline by a pen twice as wide as the distance, and their union or their difference. The outline is what is
  stroked, never the path: a contour inside the area is no part of its boundary, and its stroke would cut a band in
  a shape that shrinks. The outline has lost the `smooth` flags of the flattening, so the vertices a curve was cut
  at are found again by their position on the grid (`VertexCollector`) and joined round, or a small circle that
  grows a lot becomes the polygon of its chords with a miter at each vertex. `RoundCorners` (`PathCornerRounder`)
  works on the elements, exactly, and only between two straight segments: the point two corners share on a segment
  (its middle) is computed once, since two points a few units of the last place apart would be a segment the stroker
  turns into a spike. `TryGetClosestPoint` (`ClosestPointSink`) walks the chords like the other queries.
- `SvgPathData` reads and writes the path data of SVG. Reading goes through the builder (an arc is `ArcTo`), writing
  through `EnumerateSegments`; numbers are parsed and formatted by the runtime with the invariant culture (correctly
  rounded on every platform). The reader rejects the whole text on an error, with its position.
- A stroke is a union of convex pieces with one orientation (`Stroker`): overlaps are harmless because the coverage is
  the area of the union. The 1/16 pixel flattening tolerance is public (`canvas.Flattening` makes it finer: it is
  `PathSink.Tolerance`, set by the session on every sink it hands a flattener, and inherited by the sessions of
  layers, planes and tiles); the positions of the chord vertices are not. A chord next to a smooth vertex is not a
  rectangle: it is swept by the perpendicular to the curve (the bisector of the chord normals, and at a curve end the
  end chord turned back by half the next turn), split into two triangles where the perpendiculars cross within the
  pen (a pen wider than the arc, the WPT `2d.path.arc.*` cases), plus the half rectangle on the outer side; a body is
  made when the vertex at its end is known, the first chord of a contour when the contour ends. `ReferenceStroke`
  models the same definition: change both together. `Offset` and the emboldening of glyphs (`Configure(width, join,
  miterLimit)`) keep the rectangles: they need the Minkowski band, and the folded lobes meet at points that the grid of
  `PathCombiner` parts into slivers.
- Dashes are cut before the stroker (`Dasher`), which strokes each dash as an open contour. The first dash of a contour
  is held back until the contour ends, because the last dash of a closed contour continues with it. On a canvas, only
  the parts of a segment whose dashes can change a pixel are cut (the canvas seen along the segment, plus the reach of
  a cap, and a short length next to an end whose join or cap can show), the pattern is advanced by the length of the
  others, and nothing is cut once the stroke covers the canvas: the cost of a dashed stroke must stay what the canvas
  shows of it, whatever the length of the path and the width of the pen
  (`WhatIsOutsideTheCanvasIsNotCutIntoDashesAndThePatternGoesOn`, `AWidePenIsNotCutIntoTheDashesTheCanvasDoesNotShow`).
  `Widen` has no canvas and cuts everything.
- A brush that is not a single color is a `PaintEvaluator`: it gives a row of colors in the pixel format of the frame
  (`GradientEvaluator`, `ImageEvaluator`). An image is read through the lease of its frame, taken when a shape is painted
  and released in `finally` (`Begin` / `End`): never keep it between two draw calls. `ImageEvaluator` interpolates
  16-bit colors read with the pixel conversion (`ReadRgba64`), then converts them like any brush color; when the rows
  of the canvas stay rows of the image, it converts each row of the image once; otherwise it converts the pixels it reads
  one by one by segments of a row, kept for the lease (a direct table, or a hashed one of 8,192 segments for a large
  region; the pixels are read one by one when the scope has no room for it). An image moved by whole pixels reads one
  period of its wrap mode run by run and copies it over the rest of the run. `DrawImage` is a fill of the rectangle
  with that evaluator: no brush is allocated. `ImageSampling.Smooth` is the bilinear paths themselves when the image
  is not reduced; when it is, `ReduceRows` and `ReducePixels` add the same terms in the same order (rows, then
  columns), so that the row-by-row path and the pixel-by-pixel one give the same bits: keep them in step.
  `ReduceRows` computes the columns of the pixels of a run and their weights once, not once per row of the image.
- A clip is a stack of levels in the session (`DrawingSession.Clipping.cs`): bounds in whole pixels and, unless the clip
  is a rectangle of whole pixels, a mask of coverages rasterized once, when the clip starts, by the same rasterizer.
  Draw calls multiply their coverage by the mask row by row (`ApplyClip`), into a row of the session: they rent nothing.
  Every path that composites pixels (`PaintRows`, the color glyphs) must go through `ApplyClip`.
- A pattern whose tile image would be far larger than what the clip shows draws a window of the pattern instead
  (`TryGetPatternWindow`), at the same pixel scale, running the callback once per visible tile: keep it bit for bit
  with the whole tile image (`ATileLargerThanWhatTheCanvasShowsIsDrawnWhereTheCanvasShowsIt`).
- A layer (`DrawingSession.Layers.cs`) replaces what the draw calls paint (`_frames`, `_storages`: never assume they
  are the frames of the image) by an internal image created from the scope of the image, one per depth, kept until
  the session ends. Starting a layer pushes a clip of whole pixels with the bounds of the clip under it (its mask is
  applied when the layer is drawn); ending it is `DrawImage` of that image at the origin, with the operator and the
  opacity of the layer. A layer whose callback throws is not drawn. Anything that composites must use the current
  `_frames` and go through `ApplyClip`, so that it works in a layer without knowing it.
- A shadow (`DrawingSession.Shadows.cs`) is a layer whose alpha is blurred, moved and painted before the layer is
  drawn: the layer covers a margin around the bounds of the clip (the reach of the blur and of the offset), and the
  blur reads beyond it as its edge pixels (a shape that leaves the canvas keeps its shadow along the edge). The
  weights are those of `GaussianKernel`, shared with `GaussianBlur`: computed without `Math.Exp`. A shadow and a blur
  read only the rows and the columns of the layer their passes reach; a row that is transparent there adds nothing
  and is skipped, which is exact because compositing a zero coverage, or a transparent paint with `Source` over a
  transparent pixel, changes nothing.
- A filter whose reach goes beyond the canvas draws on a bounded layer larger than the canvas (`TryGetFilterExtent`,
  `PushBoundedLevel`): `_canvasBounds` is the canvas in the coordinates of such a session (filling the canvas fills
  only it), the edge modes other than transparent still apply at the canvas, and `FilterProcessor.Extent` is what the
  transparent mode reads. A filter graph clears its source outside its region. A `DropShadowFilter` whose margin that
  layer cannot hold is cast from a window of its own (`PushFarShadow`): the bounds of the clip moved back by the whole
  part of the offset, with the reach of the blur, which the callback draws on in a second pass
  (`NextFilterPass`, called by `DrawingCanvas.Filter`); the shadow is composited from it (`CompositeShadow` with a
  shift). Its cost must stay what the clip shows and the blur, never the length of the offset.
- Filters (`Filter` and its derived types, `DrawingSession.FilterEffects.cs`) run on `FilterSurface`s: premultiplied
  single-precision colors of a rectangle of the canvas, read through `ReadRgba64` and written back to a layer with the
  rounding of the blurs. `FilterProcessor` holds the edges (the canvas, or the rectangle of a backdrop, always mirrored)
  and the scales of the lengths; every neighborhood operation reads through `TryMap`. A backdrop layer has one frame per
  frame that is drawn on (`LayerLevel.Begin` with a frame count), each started with its own filtered backdrop, and is
  drawn frame by frame. `Filter(ColorMatrix)` and `BlurBackdrop` keep their own exact arithmetic: they are not filters.
- The linear light of a filter graph (`FilterGraphContext.Convert` and `GetColor`) goes through
  `DeterministicSrgbTransfer`, not `Math.Pow`: a polynomial of degree 5 per segment of the significand, from tables
  built with roots found by Newton's method (`+ - * /` only). Rounded to a float, it gives the values of `SrgbTransfer`
  on every float from 0 to 1 (measured on macOS Arm64); its bound against the decimal reference and a hash of its bits
  are tested (`CicpColorSpaceTests`). Change it only with a sweep against an exact reference (integer roots of big
  integers).
- A filter that leaves transparent what is beyond its reach (`Filter.GetLocalSteps`: blurs with transparent edges,
  shadows, color matrices that add no alpha, before any reach) applies to what is within twice its reach of what is
  drawn on its layer (`GetLocalFilterArea`: the dirty rectangle, or the drawn bounds the session of a bounded layer
  tracks), and only those pixels of the layer are drawn. Twice: the running sums of a blur leave values of a few units
  of the last place after what they sum, which the next sum reads; they are written as transparent pixels, and the
  pixels within the reach are the bits of the whole layer (`AFilterThatKeepsTransparentPixelsAppliesOnlyWhere...`).
  A color matrix after a blur is excluded: the straight color of such a value is anything.
- The surfaces of a filter graph (`FilterGraphContext.Bounds`) are the filtered surface when a primitive reads the
  drawing or its alpha, and otherwise only the pixels its subregions cover, so that a small subregion of a large canvas
  stays in the buffer pool. On a layer (`FilterGraph.ApplyToLayer`), the filtered surface of such a graph is that
  rectangle too: the layer is not read, the rectangle is written back and the rest of the layer is cleared. A primitive
  must therefore read an input as transparent beyond its surface, and take the edges of the filtered surface (those of
  the lighting kernels) from `SurfaceBounds`, not from an input. A primitive whose result depends on where the surfaces
  start (the running sums of a box blur with an edge mode that is not transparent) says so with
  `DependsOnSurfaceOrigin`, which keeps the left and top of the filtered surface.
- A layer opacity of `LayerOpacity.Group` is the `alphaFactor` of `Paint` (the opacity of a brush) when the layer is
  drawn, with a full coverage; with source-over it stays a coverage, so that both modes draw the same pixels.
- The tile of a `PatternBrush` is an `Rgba32` image on every canvas with `PatternTilePrecision.EightBit`: the cached
  tile image is replaced when its pixel format is not the one the brush needs.
- Coordinates are checked once transformed, where the canvas sees them, and a vertex beyond 16777216 in magnitude is not
  rejected: `TransformSink` starts cutting the contours of a fill by that square from it (`RangeClipSink`, streaming
  Sutherland-Hodgman, resumed on the contour in progress), and the `Stroker` cuts each piece that leaves it (for a pen
  that does not scale, from the first vertex of its path that leaves it: `ClipToRange`, since its dashes and joins need
  the whole path). A contour or a piece that stays in the square must reach the edges with the same calls and
  vertices: the pixels of the drawings that did not throw before must not change. Only non-finite values throw.
- The transformation of the canvas (`DrawingSession.Transforms.cs`) is applied where a shape becomes edges, not where
  it is described: `BeginFill` wraps the fill sink in a `TransformSink`, the `Stroker` transforms the vertices of its
  pieces (`SetOutput`: the path is stroked in the coordinates of the drawing, so the pen is transformed with it; a pen
  whose `ScalesWithTransform` is off gets a `TransformSink` before its dasher and its markers instead, and strokes in
  pixels as it does without transformation), and
  the evaluators of the brushes get the transformation of the brush followed by the one of the canvas (`ToCanvas`).
  The flatteners read `PathSink.Scale` of the sink they emit to, and cut curves that much finer; it is 1 without
  transformation, where nothing may change (every product by it is exact). What is given in pixels must use
  `BeginDeviceFill`: the frame, a layer that ends, the outlines of a color glyph (whose placement is composed with
  the transformation instead, because its paints are evaluated in pixels). The whole-frame test of the stroker is off
  under an affine transformation; the dasher cuts the dashes the canvas shows through the four half-planes of the
  drawing whose image is on the canvas (`Dasher.Configure` with a `ProjectiveView`).
- A projective transformation of the canvas (`ProjectiveTransform`, `Transform3D`: the WebPlatform rasterizer of the
  same author is the reference of its semantics) is not affine anywhere in the session: `_hasTransform` is false and
  `_isProjective` true, so every use of the transformation must test both. One that is affine up to a positive `W`
  takes the affine paths, bit for bit. Shapes go through `ProjectiveSink` (each contour, or each piece of a stroke,
  cut by four half-planes in the coordinates of the drawing, then divided by `W`: never reject, never clamp a vertex
  that is not on the border), curves are cut adaptively for the factor `ProjectiveView.GetScale` bounds on what is
  seen of each part (`PathFlattener.Projected*`, the arcs of the stroker): a uniform cut of a large curve at the
  largest factor near the viewer made millions of chords. What has a size in the drawing that is not a shape (the
  blur and the offset of a shadow, a blur, color glyphs) is drawn by a session on an image of the plane
  (`DrawingSession.Projections.cs`), then drawn in perspective; a plane seen from its edge (`IsFlat`) draws nothing,
  since its pieces on one line would cross each other everywhere. Transformations of space nest in 4x4
  (`_transform3D`), so that z is kept.
- Every rental of a draw call happens before its first pixel write (edges, then the sweep and gradient scratch in
  `Render`). Draw calls allocate nothing once the scratch of the session has grown (`PerformanceContractTests`).
- The cost of a shape follows its edges and their crossings. Keep the events of the sweep logarithmic: no pass over
  every edge of a row per event.
- Tests have three independent sources: hand-computed literals (`Drawing*Tests`), the exact rational references of the
  harness (`TestHarness/Drawing`: `ReferenceCoverage`, `ReferenceCompositor`, `ReferenceGradient`, and the
  `ReferenceStroke` membership predicate; `Conformance/DrawingReferenceTests`), and invariants over random drawings
  (`DrawingFuzzTests`). The references never reuse the flattening or stroking of the library: curves are checked
  against the true curve, strokes against the predicate, and the pieces the library emits are then filled exactly.

## Fonts

The font reader and the shaper are in `Meziantou.Framework.Fonts`, shared with `Meziantou.Framework.WebPlatform`: its
[AGENTS.md](../Meziantou.Framework.Fonts/AGENTS.md) has their rules. `Font` and `FontCollection` (`Typography/`) are
thin public types over its `FontFace`.

- **The font code depends on nothing of the library.** It uses only its own types: `FontEnvironment` (the
  `FontResourceLimits` and a `FontAllocator` for the temporary buffers; `ImageFontEnvironment` adapts an
  `ImageConfiguration` and its allocation scopes, `ImageFontEnvironment.GetConfiguration` gets it back),
  `BitmapGlyphFormat` and the internal `FontException` types (`InvalidFontException`, `UnsupportedFontFeatureException`,
  `UnknownFontFormatException`, `FontResourceLimitException`), raised by `FontErrors`. Decoding the image of a bitmap
  glyph needs the codecs: it is `BitmapGlyphDecoder`, in `Internals/Drawing`.
- **A font is not an image.** No `ImageFormat` member, no codec: at the public boundary (`FontFiles`, `Font`,
  `TextLayout`, the text members of `DrawingCanvas`), `FontExceptionTranslation` turns the exceptions of the font code
  into the four content exceptions with `ImageFormat.Unknown`, as for ICC profiles. An unsupported feature is named
  `"Font: <feature>"`. A new public member that reads a font translates them too.

## Text

`TextLayoutEngine` (`Internals/Text/Layout`) turns a text, its spans and `TextLayoutOptions` into the content of a
`TextLayout`: lines, glyphs, positions and decorations. `DrawingSession.FillText` and `StrokeText` draw a layout through
`GlyphPathSink` into the sinks of the drawing pipeline.

- **A layout is the only thing that is measured and drawn.** There is no separate measuring code: `Width`, `Height`,
  `Lines` and `InkBounds` come from the positions that are drawn. A layout is immutable and thread-safe, holds exact
  managed arrays (like `VectorPath`) and no pooled storage; so are `TextStyle`, `TextLayoutOptions`, `RichText` and
  `FontStack`.
- **The positions of the text come from the positions that are drawn.** `Finish` records, for each line, its clusters
  from the left (`TextHitBox`: the characters of the cluster, and the span of the pen over its glyphs, after alignment
  and justification), with the direction of the item they come from. `HitTest`, `GetCaretRectangle` and
  `GetSelectionRectangles` only read these: the layout does not keep the text. A cluster holds the characters up to
  the next cluster of its line in the order of the text; the grapheme boundaries of a cluster of several characters
  (a ligature) are kept, and share its width. What a line adds (a hyphen, an ellipsis) has no character.
- **Validate at layout, not at draw.** Creating a layout decodes each distinct glyph once, so the errors of the font
  data surface there; drawing a layout then only fails on a limit of the image or on cancellation. Do not add a failure
  mode to `FillText` that `TextLayout.Create` could have reported. The outlines decoded then are recorded
  (`RecordedOutlines`: the calls of the font to its sink, in font units) and replayed by every draw and by `ToPath`:
  a glyph is never decoded again for a layout.
- **The Unicode algorithms are those of `Meziantou.Framework.Unicode`**: line breaking (UAX #14), bidirectional levels
  and reordering (UAX #9), script runs (UAX #24), grapheme clusters and words (UAX #29), hyphenation. Do not
  reimplement or approximate one here; a missing piece belongs in that package, with its conformance tests.
- **The font of a cluster** (`SelectFont`): the first font of the stack that has every character of the grapheme
  cluster. An emoji is first looked for in the fonts of its presentation (`GetPresentation`: UTS #51; the fonts with
  color glyphs for the emoji presentation, the others for the text presentation), then in the order of the stack: the
  presentation is a preference, never a reason to draw a missing glyph.
- **The steps of a paragraph**: levels, items (one level, script, font and shaping style each), shaping, lines, display
  order. An item is cut where shaping could differ, never where only the paint differs. A line break inside an item
  cuts it, and both parts are shaped again: no ligature or kerning pair spans two lines. The advance of each character
  (`Paragraph.Advances`) is what line breaking measures; it is refreshed whenever an item is shaped. Of the text
  after a line, only the start is shaped again (`ShapeStart`: two line widths, cut after a space), and the rest is a
  pending item that keeps its advances: shaping the whole rest after each line made a layout quadratic. A line is
  still shaped as a whole: `BuildLine` merges the parts of a run that such a cut separated.
- **Lines follow each other in the text and cover it** (`TextLine.Start` and `Length`), whatever cut them: a width, a
  line break, a trimming. `IsTruncated` is the only way text is left out. `TextLayoutFuzzTests` checks it on random
  texts and options.
- **Transform, then flatten.** The control points of an outline are transformed to pixels with `Affine2D` (one fixed
  order of operations), then flattened by the unchanged `PathFlattener` with the tolerance of every other curve. No
  hinting, no snapping, no rounding of advances: a run is positioned in whole font units, then scaled once.
  `TextLayout.ToPath` uses the same transform, and filling or stroking that path must stay bit-identical to
  `FillText` and `StrokeText` of a layout of one paint (`DrawingTextTests.DrawingALayoutIsDrawingItsPath`).
- **Synthesis** (`TextStyle.Synthesis`, `GlyphRun.Shear` and `EmboldenWidth`). An oblique is a shear in the transform
  of the glyph (`Affine2D.ForGlyph` with a shear; `Then` on a path): exact, and everywhere the outline goes (fill,
  stroke, path, bounds). A bold is the fill of the glyph united with a stroke of its outline: `Emit` sends the outline
  a second time to the `Stroker` of the session, which shares the edges of the fill and whose pieces have the
  orientation glyphs are given. The fill sink is ended before the pieces are added (it truncates what it added last
  when a contour stays on the left of the frame). The bold is not in `ToPath` nor in `StrokeText`; the advances and
  the offsets it changes are made in `TextLayoutEngine.Embolden`, and `InkBounds` adds the reach of a miter.
- **One shape per paint, one orientation per glyph.** The glyphs and decorations of one paint are rasterized in one
  sweep, so overlaps are painted once. TrueType and CFF outlines turn in opposite directions: `Emit` reverses the
  edges of a glyph whose signed area is negative (`EdgeBuilder.NegateDirections`), otherwise overlapping glyphs of two
  conventions would cancel under the nonzero rule; decorations are emitted in the same orientation. A layout of several
  parts (paints, backgrounds, pens of the styles, color glyphs) is drawn twice by `DrawingSession.FillTextParts`: a
  first time with `_isPreparing`, where `Render` rents everything and paints nothing, then for real, which keeps
  "every rental happens before the first pixel write". Add a part to `DrawTextParts`, never a second prepare path.
- **What a style adds around its glyphs.** A background (`TextStyle.BackgroundBrush`) is a `DecorationRectangle` per
  line and per style, as tall as the primary font of the style, painted before every glyph of the layout; the pen
  of a style (`TextStyle.Outline`) is an index in `GlyphRun.Outline`, and `Emit` with that index sends its glyphs
  only, to a stroke, after every fill. Neither is an outline of the text (`ToPath`, `StrokeText`).
- **A placeholder is a tabulation with a size**: one item of one character (U+FFFC, which the Unicode algorithms
  already treat as an object: a line can end on both sides of it), no glyph, an advance, and a line that is as tall
  as it needs. Its rectangle is recorded by `Finish` where the glyphs are placed. The layout holds no image: a
  layout is immutable and outlives any image.
- **A fitted text is laid out at a scale** (`TextLayout.CreateToFit`): the engine multiplies every length of the
  styles by `_scale` (`GetSize`), and takes its box as arguments instead of the options. Read a size through
  `GetSize`, never `style.Size`, or a fitted text mixes two sizes.
- **A vertical layout is a horizontal layout, turned at the very end** (`TextLayoutOptions.WritingMode`,
  `VerticalLayoutData`). The engine works in the coordinates of a line for every writing mode: `u` along the line
  from its top and `v` across the lines, from the side the ascent of text on its side points to. Itemizing, line
  breaking, alignment, justification, trimming, decorations and hit boxes do not know about the writing mode; the
  engine only swaps the two limits of the box (`_maxWidth` is the inline limit) and, in `Finish`, turns each line:
  the point `(u, v)` of a line is drawn at `(lineRight - (v - line.Y), u)`. Do not add a second code path for vertical
  lines in a step of the engine: express it in the coordinates of the line.
  - What is drawn at a point is stored turned (`Positions`, `Decorations`, `Backgrounds`, `Placeholders`, `Lines`,
    `Width`, `Height`). What works along a line keeps the coordinates of the line in `TextLayoutData.Vertical`: the
    lines, the positions, the decorations and the backgrounds that a text on a path follows, and the lines that the
    hit boxes are relative to. `HitTest`, `GetCaretRectangle` and `GetSelectionRectangles` work on those and map
    their result per line (`GetLineRectangle`).
  - **Upright or on its side** is decided per grapheme cluster in `Itemize` (`IsUpright`: UAX #50 through
    `UnicodeProperties.GetVerticalOrientation`; a `Tr` character is upright when the `vert` feature of the font
    changes its glyph, which is probed once per glyph and cached). The orientation is part of an item: an upright
    item is shaped vertically (`TextShaper.Shape(..., isVertical: true)`), an item on its side horizontally. An
    upright cluster at an odd bidirectional level gets the next even level, so that upright text always reads from
    the top; `TextOrientation.Upright` clears every level.
  - **A sideways run only differs by the transformation of its glyphs** (`GlyphRun.IsSideways`,
    `Affine2D.ForSidewaysGlyph`, always through `GlyphRun.GetTransform`): its advances, offsets, kerning and shear
    are those of horizontal text. An upright glyph uses the transformation of a horizontal glyph, at the position
    its vertical origin gives.
  - **The baseline.** With `Mixed` and `Upright`, the line has a central baseline (`_hasCentralBaseline`): it is
    symmetric around it, upright glyphs are centered on it by their vertical origins, and an item on its side is
    shifted by `(ascent - descent) / 2` (`PlacedGlyph.BaselineShift`). With `Sideways`, the line keeps the
    alphabetic baseline and the metrics of a horizontal line: the layout is exactly the horizontal one turned a
    quarter of a turn clockwise, which is what `AVerticalLineOfTextOnItsSideIsAHorizontalLineTurned` asserts for
    the pixels, the lines, the carets, the selections and the hit tests. Keep that property: it is the cheapest
    test of everything that is turned.
- **Text on a path** (`TextLayout.EmitOnPath`, `PathMeasure`). The path is flattened by `PathFlattener` into chords;
  `PathMeasure` keeps each chord with its unit direction and the distance of its start. A glyph is placed rigidly at the
  anchor of its cluster (`TextLayoutData.Anchors`: the middle of the advance of the cluster, so a base and its marks,
  or the glyphs of a ligature, turn together) with the rotation `(dx, dy, -dy, dx)` of the chord under the anchor: only
  `+ - * /` and `Math.Sqrt`, never a trigonometric function. The text is on the left of the direction; `Side.Right` is
  the reversed path, not a mirror. A single closed contour wraps; several contours are concatenated; beyond the ends
  the first and last chords continue (`Extend`) or the cluster is dropped (`Clip`). Decorations are cut into one
  quadrilateral per chord, and more than one turn around a closed path is one turn (the work is bounded by the number
  of chords, not by the length of the text). The literals are whole-pixel rectangles on axis-aligned paths and a 3-4-5
  slope (`DrawingTextTests`).
- **Color glyphs** (`ColrTable`, `ColorGlyph`, `ColorSurface`, `DrawingSession.ColorGlyphs.cs`). The graph of paints
  of a glyph is walked once, when a layout is created (`TextLayout.Measure`), and written as a flat display list with
  the transformations resolved: fills of an outline or of the clip, clips (an outline, a box), layers and their
  composition. The walk validates everything the glyph refers to, charges `GlyphBudget` per paint, and leaves out a
  paint that refers to a glyph or a layer that is being painted. Drawing runs the list on a `ColorSurface`: layers of
  premultiplied single precision colors and clip masks over the box of the glyph on the canvas, filled row by row
  from the unchanged `CoverageRasterizer`, then composited onto the frames as a paint with a color per pixel.
  - `FillText` prepares every color glyph (`paint: false`: the surface of the largest one, the sweep of every
    outline) before any shape of the call is painted, then paints them after the shapes of the brushes.
  - Angles go through `DeterministicAngle` (series and square roots, in half turns), never `Math.Sin`, `Cos` or
    `Atan2`: the rule against trigonometric functions in drawing holds for the paints.
  - The degenerate cases are those of the specification, not of a renderer: equal sweep angles or stops at one
    offset give two colors when the color line pads and nothing when it repeats or reflects.
  - A bitmap glyph (`SbixTable`, `CblcTable`, `BitmapGlyphDecoder`) is a display list of one op, `FillImage`, whose paint is
    the image: decoded with the PNG decoder of the library under the limits of the font, scaled down once at layout
    time (`Resize`) when the strike is larger than the text, sampled bilinearly at draw time. The key of a color glyph
    in a layout is (font, glyph, palette) for paints and (font, glyph, size) for images. `FontFace.IsColorGlyph` is
    about `COLR` only: use `TryGetBitmapGlyph` for images.
  - Tests: literals on `FontSamples.ColorBoxes`, `ColorTransforms`, `ColorGradients` and `ColorClips` (rectangles on
    whole pixels, gradient values at pixel centers; `ColorFontTests`), and the images `hb-raster` draws for the fonts
    of the corpus (`Conformance/ColorFontConformanceTests`: two rasterizers do not give the same edge pixels, so
    the comparison is statistical). The glyphs the reference renderer does not draw as the specification says are
    listed in `FontCorpus.CharactersWithoutReference` with the reason, and are covered by literals. `hb-raster`
    needs `--font-ppem` to choose the strike of a bitmap font (without it, the largest), and draws an image one pixel
    higher than an outline (`FontRender.ImageShift`): a line of a bitmap font has no outline glyph for that reason.
    The literals of the bitmap glyphs are on `FontSamples.BitmapBoxes` (both table families, every index and image
    format).
- **The layout overloads allocate nothing** once the scratch of the session has grown (`PerformanceContractTests`). The
  overload that takes a string creates a layout and is the documented exception.
- Tests: literals on the boxes font (rectangles on whole pixels at 16 pixels per em, compared with `FillRectangle`;
  `DrawingTextTests`, `TextLayoutTests`), layout against path on the fonts of the corpus, `FontFuzzTests`, which lays
  out and draws with every mutated font, and `TextLayoutFuzzTests`. A defect the fuzzers find becomes a literal test.

### Shaping

`TextShaper` turns the characters of a run (one font, script, language and direction) into glyphs and positions in a
`GlyphBuffer`. It is in `Meziantou.Framework.Fonts`: see its [AGENTS.md](../Meziantou.Framework.Fonts/AGENTS.md).


## Performance work

- Remove unnecessary decodes, conversions and copies before micro-optimizing. Profile first and keep the evidence.
- Scalar kernels are the numerical reference. A vectorized kernel keeps its scalar version (`...Scalar`) and needs a
  test proving identical results (`VectorizedKernelTests`). No fused multiply-add in the DCTs. The resampler and the
  convolver add every tap with a fused multiply-add in the scalar loops (`Math.FusedMultiplyAdd`) and the vector loops
  alike, the vector loops only when the processor has the instruction (`Resampler.IsFmaAccelerated`): the software
  fallback of `Vector128.FusedMultiplyAdd` is exact but computed one element at a time.
- A kernel that only has an `AdvSimd` path runs its scalar reference on x64. The AV1 vector kernels use `Av1Simd` for
  what `Vector128` does not express: each operation has an `AdvSimd` and an SSE branch, so the x64 branch only runs on
  x64 (the Windows and Linux CI legs, or an x64 machine). The inverse transform kernels are written once over
  `ILanes`: with AVX2, blocks at least 8 wide run the column pass 8 columns at a time in `Vector256`.
- `Vector256` (AVX2) and `Vector512` (AVX-512) paths never run on Arm64. Test them on an x64 machine three ways: by
  default, with `DOTNET_EnableAVX512=0` (`DOTNET_EnableAVX512F=0` leaves `Vector512` accelerated on .NET 11) and with
  `DOTNET_EnableAVX=0`, and check with a deliberately broken path that each setting runs the path it is meant to.
- A function that encodes a value with `Math.Pow` and rounds it to an 8-bit sample is replaced by its thresholds
  (`QuantizationThresholds`, built lazily from the scalar function and compared with it around every threshold): the
  linear-light `Store` of `WorkingSamples` and the linear-light compositing of drawing use it, and so do the 8-bit ICC
  conversions whose last stage is the inverse tone curves of a matrix-based destination
  (`IccPipeline.GetByteOutputTables`, one table per channel and per pipeline, built once the pipeline has converted
  `ByteOutputTablesThreshold` colors so that small images never pay for them; identity curves are cheaper than a table
  and are evaluated). A function that is not monotone at the scale of the last bits of its input (PQ, a decreasing tone
  curve) gets no table and keeps the scalar path. 16-bit samples get no thresholds: 65,535 of them cost far more
  evaluations than the 256 of 8 bits (a bisection and a check around each one), more than most images. Their inputs
  are covered instead: a 16-bit conversion of at least `IccUInt16InputTables.Threshold` colors (`ConvertColorProfile`
  counts every frame, `IccColorTransform` a call) reads its first stage (one curve per source channel) from a
  65,536-entry table per distinct curve, built with the same evaluation of every sample value, so it is exact, and
  rented from the scope of the conversion (a scope of the default limits for `IccColorTransform`). The last stage of a
  16-bit conversion is still evaluated: its inputs are the doubles that come out of a matrix, not 65,536 values.
- Default parallelism is one worker (`ImageConfiguration.MaxDegreeOfParallelism`). Only `Resize` and `Convolve` are
  parallel, and their result must not depend on the worker count. Decoding, encoding and drawing stay sequential.
- Static row callbacks allocate nothing per row (`PerformanceContractTests`).
- This repository builds with the updated memory-safety rules: wrap `MemoryMarshal`, `Unsafe` and `Vector*.LoadUnsafe` /
  `StoreUnsafe` calls in `unsafe(...)`, or in an `unsafe { }` block for a statement.
- Benchmarks (BenchmarkDotNet, `benchmarks/Meziantou.Framework.Imaging.Benchmarks`) are compared on the same machine only:

  ```shell
  dotnet run -c Release --project benchmarks/Meziantou.Framework.Imaging.Benchmarks -f net10.0 -- --inProcess --filter "*PngBenchmarks*"
  dotnet run -c Release --project benchmarks/Meziantou.Framework.Imaging.Benchmarks -f net10.0 -- service --seconds 10
  dotnet run -c Release --project benchmarks/Meziantou.Framework.Imaging.Benchmarks -f net10.0 -- webp-quality 512 3
  dotnet run -c Release --project benchmarks/Meziantou.Framework.Imaging.Benchmarks -f net10.0 -- jxl-quality 512 3
  dotnet run -c Release --project benchmarks/Meziantou.Framework.Imaging.Benchmarks -f net10.0 -- profile PngBenchmarks.SaveLarge8 20
  dotnet run -c Release --project benchmarks/Meziantou.Framework.Imaging.Benchmarks -f net10.0 -- profile PngBenchmarks.SaveLarge8 50x
  dotnet run -c Release --project benchmarks/Meziantou.Framework.Imaging.Benchmarks -f net10.0 -- profile TextBenchmarks.LayoutTibetan 200x
  dotnet run eng/compare-benchmarks.cs -- <baseline>/results <current>/results
  ```

  `Workloads/` has no BenchmarkDotNet dependency: the conformance tests compile it to validate the workload outputs.

  `profile` prints the mean, the minimum and the median time of an iteration, the managed bytes allocated per iteration and
  what the shared buffer pool did. `<n>x` runs a fixed number of iterations instead of a duration: on a busy machine,
  compare two builds with a process counter that does not depend on the load (`/usr/bin/time -l dotnet
  Meziantou.Framework.Imaging.Benchmarks.dll profile <Class.Method> <n>x` reports the retired instructions and the cycles on macOS; subtract a
  `1x` run to remove the setup) rather than with wall-clock times.
- The stack sampler of `dotnet-trace` blames the method that holds the next safe point, and it also samples the setup of
  the benchmark class: confirm a hot spot by timing the stage (temporary `Stopwatch` counters) before optimizing it.

## Projects

| Path | Content |
| --- | --- |
| `src/Meziantou.Framework.Imaging` | The library and the package README (`readme.md`) |
| `tests/Meziantou.Framework.Imaging.Tests` | Tests that are not about one codec: image model, pixels, memory, metadata, readers and writers with test codecs, format detection, defaults, API surface, the corpus and harness self-tests, the documentation examples |
| `tests/Meziantou.Framework.Imaging.Processing.Tests` | Processing, resampling, convolution, automatic cropping, drawing and color conversion, with their golden and reference suites |
| `tests/Meziantou.Framework.Imaging.<Codec>.Tests` | One project per codec (`Png` with APNG, `Gif`, `Jpeg`, `JpegXl` with the transcoder, `Jpeg2000`, `WebP`, `Qoi`, `Bmp`, `Tga`, `Pnm`, `Tiff`, `Ico` with CUR and ANI, `Av1`, `Avif`, `AvifEncoder`, `Hevc`, `Heic`, `HeicEncoder`): its unit tests (no fixture file), its conformance tests under `Conformance/`, and the generic suites for its fixtures and its encoder |
| `tests/Meziantou.Framework.Imaging.Tests.Shared` | Sources compiled into every project above (no project file): `Helpers/`, the generic suites over the corpus (`Suites/`) and `Imaging.Tests.Shared.props` |
| `tests/Meziantou.Framework.Imaging.InteropTests` | Files produced by the encoders are decoded by pinned external tools |
| `tests/Meziantou.Framework.Imaging.TestHarness` | Shared, test-framework-agnostic support: corpus manifest, raw-buffer comparison, independent reference readers, tool helpers |
| `tests/Meziantou.Framework.Imaging.AotSmoke` | Console app exercising every format; published with NativeAOT |
| `tests/Meziantou.Framework.Imaging.Fixtures` | Golden corpus, manifest, provenance and licenses ([README](../../tests/Meziantou.Framework.Imaging.Fixtures/README.md)) |
| `fuzz/Meziantou.Framework.Imaging.FuzzTests` | Mutation fuzz tests over the corpus and the committed regressions (`FuzzRegressions/`) |
| `tools/Meziantou.Framework.Imaging.CorpusGenerator` | The reviewed recipe of every fixture; never run by the tests |
| `tools/Meziantou.Framework.Imaging.Av1TableExtractor` | Generates the constant tables of the AV1 decoder from the AV1 specification |
| `samples/Meziantou.Framework.Imaging.Samples` | Usage examples: the source of every C# block of the documentation |
| `benchmarks/Meziantou.Framework.Imaging.Benchmarks` | BenchmarkDotNet workloads |

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
- **Generated tables of the text code.** `OpenTypeLanguageTable.g.cs` (`language-tags`) and
  `ArabicPresentationForms.g.cs` (`arabic-forms`) are written by commands of the corpus generator from sources that
  are not committed and are pinned by a hash: run the command without `--write` to check that a table is what its
  sources give, after downloading them from the addresses the command names.
- **TIFF has no corpus.** The decoder tests assemble files (`TiffFileBuilder`) whose compressed strips never come from
  the library: the reference encoders of the tests (`TiffReferenceCodecs`), the examples of the specifications, fax
  strips written by libtiff and kept as literals, and JPEG datastreams of `JpegTestImage` and `JpegLosslessTestImage`
  (libtiff writes pages of 12-bit JPEG samples, `tiffcp -c jpeg` on a 12-bit page, which make a local oracle: it loses
  the last sample of a row of odd width, so use even widths). The encoder tests compare
  small strips with the bytes the specifications give, and the interop tests decode every compression and layout with
  FFmpeg or Apple ImageIO (the JPEG pages, which FFmpeg does not read), and the pages FFmpeg writes with the library.
- **CMYK JPEG fixtures.** A four-component JPEG file is an `unsupported` fixture (identified, rejected by a load
  without conversion) that declares one conversion and its pixels (`inkConversion`). The reference has two independent
  stages: the samples decoded by `djpeg` from files the generator regroups scan by scan, then the decimal reference of
  the harness for the colors, with LittleCMS and Apple ColorSync as cross-checks.
  `JpegDecoderConformanceTests` pins each tolerance to the measured disagreement (`LibraryInkMeasurements`).
- **Arithmetic-coded JPEG fixtures.** `cjpeg -arithmetic` writes them and `djpeg` is the reference, but libjpeg-turbo
  is the only arithmetic decoder at hand: FFmpeg has none and Apple ImageIO does not use one of its own. The
  cross-check is FFmpeg on the file `jpegtran` rewrites with Huffman codes (the same coefficients), so the fixtures with
  chroma subsampling have even dimensions. The unit tests decode the streams of the test-side arithmetic encoder
  (`JpegTestArithmeticCoder`), itself checked against `djpeg` when it was written.
- **Lossless JPEG fixtures.** A file `cjpeg -lossless` writes expects the pattern given to `cjpeg`: `djpeg` and FFmpeg
  must both decode exactly that, and a recipe declares the files FFmpeg misreads with the reason (the generator fails
  when a declared disagreement disappears). The YCbCr files come from FFmpeg's `ljpeg` encoder and expect the planes
  FFmpeg decodes, upsampled and converted by the generator with the rules the DCT fixtures compare with `djpeg`
  (`CommonCorpus.JpegUpsample`, `JpegYCbCrToRgb`): change these with the row writers, never one without the other.
  The unit tests decode the streams of the test-side encoder (`JpegLosslessTestImage`), written one sample at a time
  from Annex H. After a change to the lossless decoder, also compare it with `djpeg` on generated files of every
  precision, predictor, point transform and restart interval (a scratch loop over `cjpeg -precision P -lossless
  psv,Pt -restart N`): the corpus only holds a few of them.
- **12-bit JPEG fixtures.** `cjpeg -precision 12` writes them and `djpeg` gives their 12-bit samples, which the
  generator scales to 16 bits like the library; the errors are on the 8-bit scale, so three units of a 12-bit sample
  count as one. FFmpeg and Apple ImageIO upsample their chroma very differently and neither reads 12-bit RGB samples as
  RGB: they bound the tolerance, the measured disagreement with `djpeg` sets it (`LibraryJpegMeasurements`).
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
- **HEVC vectors.** `tests/Meziantou.Framework.Imaging.Fixtures/hevc` holds raw HEVC byte streams (Annex B) with, in
  the `hevcVectors` section of the manifest, the SHA-256 of every plane of every output picture, cropped to the
  conformance window and in output order. A digest is recorded only when at least two of three independent decoders
  (the `hevc` decoder of FFmpeg, libde265 `dec265`, HM `TAppDecoder`) agree byte for byte; the entry names them and
  records, for the third one, that it is left out and why (`decoders.excluded`). A recipe declares the disagreement it
  expects: any other one fails the generation. Every picture also carries a decoded picture hash SEI message (MD5, CRC
  or checksum), which the manifest repeats per frame: a test checks a decoded picture with nothing but the stream.
  Decoders that agree with each other and not with the hashes are not a reference (x265 does not always write the hash
  of what it codes). The feature tags come from `trace_headers`, never from the encoder options, and their vocabulary is
  closed (`FixtureManifestValidator.AllowedHevcFeatureKeys`). `HevcDecoderConformanceTests` compares the planes and the
  hashes of the library with them, the vectors tagged `hevc.unsupported` must be rejected, and `HevcToolCoverageTests`
  checks that the vectors reach every coding tool except the ones no encoder of the pinned release writes (long-term
  reference pictures, a modified reference picture list): those are reached by streams whose headers the unit tests
  rewrite (`HevcTestStreamRewriter`), which the reference decoders confirmed.
- **The reference decoders tolerate what the specification forbids.** A vector on which three decoders agree is not
  proof that the stream is valid: kvazaar wrote scaling lists with coefficients equal to 0, which clause 7.4.5 forbids
  and every reference decoder accepts, and x265 writes slices that are not complete, which FFmpeg and libde265 conceal
  the same way. The library rejects both (`InvalidImageContentException`); when the library and the references
  disagree on a stream, read the stream against the specification before changing the decoder.
- **Known behaviours of the HEVC oracles**, beyond the ones the fixtures README lists: libde265 1.1.3 applies SAO across
  slices that disable in-loop filtering across their boundaries, and `dec265 -c` only compares the hash of the last
  picture of a stream (use HM, or FFmpeg `-err_detect crccheck` for an MD5); FFmpeg differs with implicit RDPCM in
  transquant-bypass blocks and with `high_precision_offsets_enabled_flag`; HM aborts on tiles narrower than 256 samples
  and on 16x16 coding tree blocks at some levels; `ffprobe` needs `-flags unaligned` to report a left crop exactly;
  `trace_headers` prints nothing for end of sequence and end of bitstream units.
- **Third-party HEVC conformance streams** (the JCT-VC suite of ITU-T H.265.1) have no license that allows committing
  them. They are run as a campaign after a change to the HEVC decoder: point
  `MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_STREAMS` at their directory and run `HevcDifferentialInteropTests`, which
  compares the library with the reference decoders on every stream and on seeded random streams of the three encoders
  (`MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_SEED`, `MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_CASES`).
- Interop tests download ffmpeg (with x265 and kvazaar), libwebp, libavif, libjxl (`cjxl`, `djxl`, `jxlinfo`,
  `jxl_from_tree`), jxl-oxide, libde265 (`dec265`), libheif (`heif-enc`, `heif-dec`, `heif-info`), HM (`TAppEncoder`,
  `TAppDecoder`), OpenJPEG (`opj_compress`, `opj_decompress`, `opj_dump`) and OpenJPH (`ojph_compress`, `ojph_expand`) from the release pinned by the `Meziantou.Prebuilt` package (same version in the test harness and the corpus generator). A download failure, an unexpected version or a missing
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

- **JPEG 2000 fixtures** (`jp2` generator, `Jp2Corpus.cs` and its partial files). A fixture of the reversible
  transformations expects the pattern given to `opj_compress` or `ojph_compress`; a fixture of the irreversible ones
  expects what `opj_decompress` decodes, cross-checked with the native decoder of FFmpeg (and `ojph_expand` for the
  high-throughput block coder), under a tolerance derived from the measured error of the library
  (`LibraryJp2Measurements`, printed by `Jpeg2000DecoderConformanceTests`). A decoder that does not read a fixture as
  the Recommendation says is recorded with the reason and excluded; the generator fails when it starts to agree, so a
  tool upgrade is noticed. What no pinned tool writes is made by the generator without decoding anything: files
  assembled with `Jp2FileBuilder`, codestreams whose marker segments and packets are moved (`Jp2Corpus.Editor.cs`: PPM,
  PPT, interleaved tile-parts, COC/QCC), and the coding passes of the high-throughput block coder that `ojph_compress`
  does not write (`Jp2Corpus.HighThroughput.cs`: HT SigProp and HT MagRef passes, placeholder passes, layers), whose
  expected pixels are the coefficients the generator codes. The fields of the files are read by
  `TestHarness/Jpeg2000/ReferenceJpeg2000`.

  The coverage tables of the fixtures README are checked against the manifest (`FixtureCoverageMatrixTests`): update them
  when fixtures or feature tags change.
- **Fuzzing.** The default budget is small and deterministic. Longer campaigns are opt-in through
  `MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS`, `_FUZZ_DURATION_SECONDS`, `_FUZZ_SEED`, `_FUZZ_CASE_TIMEOUT_SECONDS`
  and, for the font fuzzer, `_FUZZ_SEED_FILTER` (a text the identifier of the seeds contains, such as `variable`);
  `_FUZZ_GROUPS` (a comma-separated list, such as `hevc-intra,heic-sequence`) limits a campaign to some groups of
  seeds. A failure is minimized and committed to `FuzzRegressions/` with an entry in `regressions.json`; a file of the
  HEIF family is committed as it was found, since removing bytes breaks the offsets of its container.
  - The AV1 and HEVC vectors are fuzzed inside a container that the harness builder writes (`FuzzAvifContainer`,
    `FuzzHeicContainer`): the mutations of `FuzzHevcMutator` keep the lengths of the NAL units and their headers, so
    that they reach the parameter sets, the slice headers and the slice data and not only the container.
  - The oracle requires that an identification and a load agree: a file is described by the parameter sets that its
    pictures use. `Image.DetectFormat` is the exception inside the HEIF family, since it only sees the brands.
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
