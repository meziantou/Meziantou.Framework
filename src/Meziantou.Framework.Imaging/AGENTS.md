# Meziantou.Framework.Imaging

A fully managed image library: PNG/APNG, GIF, JPEG, WebP, QOI, BMP, TGA, Netpbm, TIFF and ICO/CUR decoding and encoding,
animation-aware processing, and bounded-memory streaming readers and writers.

These instructions complete the repository-level `AGENTS.md`. The user guide is [readme.md](readme.md): it states what
the library guarantees to its callers (support matrix, limitations, defaults). Keep the code, the XML documentation and
`readme.md` consistent in the same change.

## Hard constraints

- Target frameworks are exactly `net10.0;net11.0` for the library and every test, sample and benchmark project. Do not
  add older targets or compatibility shims.
- Fully managed, no package dependency and no native dependency. The library is trimmable and NativeAOT-compatible
  (`IsTrimmable`, `IsAotCompatible`): no reflection-based code.
- Write implementations from the format specifications. Never copy or port code from other image libraries
  (System.Drawing, libpng, libjpeg, giflib, libwebp, stb, ...).
- No public allocator, custom pixel type, codec plug-in or image-processor interface. Implementation types are `internal`
  and live under `Internals/`.
- Public namespaces: `Meziantou.Framework.Imaging`, `.Metadata` and `.Formats` (encoder settings). The base types of the
  model (`Image`, `ImageFrame`, `ImageFrameCollection`, `ImageEncoder`) cannot be derived outside the library.
- Every public member has XML documentation (`DisableDocumentationWarnings` is `false`).
- `Meziantou.Framework.FullPath` is used by the tests, benchmarks and tools, never by the library.

## Behavior rules

- **Never lose information silently.** Losing alpha, precision, animation, a poster, metadata or a color profile needs an
  explicit setting; otherwise throw `UnsupportedImageFeatureException`. Color profiles are preserved and labeled, never
  applied.
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
- **Atomicity.** Geometry-changing operations (crop, resize, rotate, auto-orient) are transactional across all frames and
  the poster. Pixel-only edits may be partially applied on failure but leave the image valid and disposable. A failed
  operation releases every resource it acquired.
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
- A new format adds an `ImageFormat` member, an `ImageCodec` in `ImageCodecRegistry`, an encoder codec in
  `ImageEncoderRegistry` and a public `ImageEncoder` in `Formats/`, then a row in the support matrix of `readme.md`.
- `ZLibStream` reports truncation as a clean end: check that the expected number of bytes was produced.

## Performance work

- Remove unnecessary decodes, conversions and copies before micro-optimizing. Profile first and keep the evidence.
- Scalar kernels are the numerical reference. A vectorized kernel keeps its scalar version (`...Scalar`) and needs a
  test proving identical results (`VectorizedKernelTests`). No fused multiply-add in the DCTs.
- Default parallelism is one worker (`ImageConfiguration.MaxDegreeOfParallelism`). Only `Resize` and `Convolve` are
  parallel, and their result must not depend on the worker count. Decoding and encoding stay sequential.
- Static row callbacks allocate nothing per row (`PerformanceContractTests`).
- This repository builds with the updated memory-safety rules: wrap `MemoryMarshal`, `Unsafe` and `Vector*.LoadUnsafe` /
  `StoreUnsafe` calls in `unsafe(...)`, or in an `unsafe { }` block for a statement.
- Benchmarks (BenchmarkDotNet, `benchmarks/ImagingBenchmarks`) are compared on the same machine only:

  ```shell
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- --inProcess --filter "*PngBenchmarks*"
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- service --seconds 10
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- webp-quality 512 3
  dotnet run -c Release --project benchmarks/ImagingBenchmarks -f net10.0 -- profile PngBenchmarks.SaveLarge8 20
  dotnet run eng/compare-benchmarks.cs -- <baseline>/results <current>/results
  ```

  `Workloads/` has no BenchmarkDotNet dependency: the conformance tests compile it to validate the workload outputs.

## Projects

| Path | Content |
| --- | --- |
| `src/Meziantou.Framework.Imaging` | The library and the package README (`readme.md`) |
| `tests/Meziantou.Framework.Imaging.Tests` | Unit tests (no fixture file) and, under `Conformance/`, golden-corpus tests. No external tool, no network |
| `tests/Meziantou.Framework.Imaging.InteropTests` | Files produced by the encoders are decoded by pinned external tools |
| `tests/Meziantou.Framework.Imaging.TestHarness` | Shared, test-framework-agnostic support: corpus manifest, raw-buffer comparison, independent reference readers, tool helpers |
| `tests/Meziantou.Framework.Imaging.AotSmoke` | Console app exercising every format; published with NativeAOT |
| `tests/Meziantou.Framework.Imaging.Fixtures` | Golden corpus, manifest, provenance and licenses ([README](../../tests/Meziantou.Framework.Imaging.Fixtures/README.md)) |
| `fuzz/Meziantou.Framework.Imaging.FuzzTests` | Mutation fuzz tests over the corpus and the committed regressions (`FuzzRegressions/`) |
| `tools/Meziantou.Framework.Imaging.CorpusGenerator` | The reviewed recipe of every fixture; never run by the tests |
| `samples/Meziantou.Framework.Imaging.Samples` | Usage examples: the source of every C# block of the documentation |
| `benchmarks/ImagingBenchmarks` | BenchmarkDotNet workloads |

## Tests

```shell
dotnet build slnx/Meziantou.Framework.Imaging.slnx
dotnet test --solution slnx/Meziantou.Framework.Imaging.slnx
dotnet test --project tests/Meziantou.Framework.Imaging.Tests/Meziantou.Framework.Imaging.Tests.csproj --framework net11.0
dotnet publish tests/Meziantou.Framework.Imaging.AotSmoke -c Release -f net11.0 --use-current-runtime -o artifacts/aot
```

- **Expected values come from independent sources**: hand-computed values, specifications, independent decoders or the
  reference readers of the test harness. Never produce a decoder expectation with our encoder or compositor, and never
  use the code under test as its own oracle.
- Lossless comparisons are exact, 16-bit low bits included. Lossy comparisons use an independent decoding of the same
  input with a narrow, justified, per-fixture tolerance. Alpha and structure are always exact.
- Unit, conformance and fuzz tests must not need an external tool or network access.
- Interop tests download ffmpeg and libwebp from the release pinned by the `Meziantou.Prebuilt` package (same version in
  the test harness and the corpus generator). A download failure, an unexpected version or a missing capability fails the
  test; it is never skipped. The Apple ImageIO cases need `swift` and only run on macOS.
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
