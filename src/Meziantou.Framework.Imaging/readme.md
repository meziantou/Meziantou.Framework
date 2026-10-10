# Meziantou.Framework.Imaging

A fully managed, performance-oriented image library for .NET 10 and .NET 11: PNG, animated PNG (APNG), GIF, JPEG, WebP
(still and animated, lossless and lossy), QOI, BMP, TGA, Netpbm (PBM/PGM/PPM/PAM), TIFF/BigTIFF and ICO/CUR decoding and
encoding, animation-aware processing, and bounded-memory streaming.

- Static PNG, animated PNG (APNG), GIF, baseline/progressive JPEG, WebP (still and animated, lossless and lossy), QOI,
  BMP, TGA, Netpbm (PBM/PGM/PPM/PAM), TIFF/BigTIFF and ICO/CUR decoding; PNG, APNG, GIF, baseline JPEG, WebP (lossless
  and lossy, still and animated), QOI, BMP, TGA, Netpbm, TIFF/BigTIFF and ICO/CUR encoding (unsupported modes, such as
  arithmetic or CMYK JPEG, RLE BMP, 16-bit TGA color maps or LZW TIFF, are rejected explicitly)
- `ImageCollection` for the images that are not animation frames: TIFF document pages and icon/cursor representations,
  read and written one entry at a time
- Animation-aware model: every frame is a full-canvas displayed image with exact rational timing
- Six working pixel formats (`Rgba32`, `Bgra32`, `Rgb24`, `Rgba64`, `Gray8`, `Gray16`) with 16-bit precision preserved
- Crop, auto-crop (background detection), resize (alpha-aware, Contain/Cover/Stretch), rotate, auto-orient, flip,
  grayscale and convolution matrices (sharpen, blur, edge detection) applied to all frames
- Bounded-memory sequential readers and writers for long animations
- Explicit policies for alpha, precision, metadata and color-profile losses; configurable resource limits

This document is the user guide: it describes what the library guarantees to its callers. Every C# example below is
compiled and run by the test suite.

## Table of contents

- [Quick start](#quick-start)
- [Design highlights](#design-highlights)
- [Platforms](#platforms)
- [Supported formats](#supported-formats)
- [Usage guide](#usage-guide)
  - [Images, frames and animations](#images-frames-and-animations)
  - [Pages and representations](#pages-and-representations)
  - [Ownership, lifetimes and leases](#ownership-lifetimes-and-leases)
  - [Thread safety](#thread-safety)
  - [Failure atomicity](#failure-atomicity)
  - [Pixel formats, precision and alpha](#pixel-formats-precision-and-alpha)
  - [Processing](#processing)
  - [Metadata, color profiles and orientation](#metadata-color-profiles-and-orientation)
  - [Loading, identification and limits](#loading-identification-and-limits)
  - [Streaming readers and writers](#streaming-readers-and-writers)
  - [Saving files](#saving-files)
  - [Errors](#errors)
- [Format reference](#format-reference)
  - [PNG and APNG](#png-and-apng)
  - [GIF](#gif)
  - [JPEG](#jpeg)
  - [WebP](#webp)
  - [QOI](#qoi)
  - [BMP](#bmp)
  - [TGA](#tga)
  - [Netpbm: PBM, PGM, PPM and PAM](#netpbm-pbm-pgm-ppm-and-pam)
  - [TIFF and BigTIFF](#tiff-and-bigtiff)
  - [ICO and CUR](#ico-and-cur)
- [Limitations](#limitations)

## Quick start

Namespaces: `Meziantou.Framework.Imaging` (images, I/O, processing, configuration), `Meziantou.Framework.Imaging.Formats`
(encoders and their settings), `Meziantou.Framework.Imaging.Metadata`.

<!-- snippet: package-readme-quickstart -->
```csharp
using Meziantou.Framework.Imaging;

using var image = Image.Load("animation.gif");
image.Resize(new ResizeOptions(320, 240));
image.Save("thumbnail.gif");
```

The output format follows the extension, and the timing and play count of the animation are kept. An explicit encoder
chooses the format and its settings; formats that store one still image require the frame to be exported explicitly:

<!-- snippet: readme-quickstart -->
```csharp
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

using var image = Image.Load("animation.gif");          // every displayed frame, exact timing
image.Resize(new ResizeOptions(320, 240));               // all frames, atomically
image.Save("thumbnail.png", new PngEncoder());           // APNG, because the image is animated

using var frame = image.CloneFrame(0);                    // JPEG stores one frame: export it explicitly
frame.Save("first.jpg", new JpegEncoder { Quality = 85, BackgroundColor = new Rgba32(255, 255, 255) });
```

## Design highlights

- **Full displayed frames.** An image is one or more full-canvas frames sharing a size and pixel type; encoded delta
  rectangles, blending and disposal are codec internals. APNG poster frames are kept separately.
- **Pages and representations are not frames.** The pages of a TIFF document and the sizes inside an icon live in a
  separate `ImageCollection`, whose entries may differ in size and pixel format and never carry timing or looping.
- **Explicit ownership.** `Image`/`Image<TPixel>` are disposable owners; frames are borrowed views; clones, extracted
  frames and reader results are independent owned images. Eager loads never retain their input.
- **Precision and alpha.** Six working pixel formats (`Rgba32`, `Bgra32`, `Rgb24`, `Rgba64`, `Gray8`, `Gray16`), 16-bit
  precision preserved, straight alpha, alpha-aware resampling, and no silent alpha/precision/metadata/animation loss.
- **Exact timing.** Rational `FrameDuration` preserves GIF, APNG and WebP delays exactly; `TotalPlays` counts total plays.
- **Atomic geometry.** Crop, auto-crop, resize, rotate and auto-orient apply to every frame (and the poster)
  transactionally.
- **Bounded streaming.** Sequential readers and writers process long animations with memory bounded by one frame.
- **Safety.** Configurable resource limits, distinct exceptions for malformed data, unsupported features, limits,
  cancellation and I/O failures, and atomic file publication.

## Platforms

| Item | Support |
| --- | --- |
| `net10.0`, `net11.0` | Supported targets, built and tested on both runtimes |
| x64, arm64 (Windows, Linux, macOS) | Tested on Linux and Windows x64, macOS arm64, and Linux and Windows arm64 where hosted runners are available |
| Older .NET, .NET Standard, .NET Framework | Not supported (no assets) |
| Native dependencies | None (fully managed) |
| Trimming / NativeAOT | Supported: the library is marked trimmable and AOT-compatible (analyzers on every build), and a NativeAOT smoke app (every format, streaming, processing, errors; trim/AOT warnings as errors) is published and run for both frameworks |

## Supported formats

Detection is content-based, never by extension. "Not supported" features are recognized and rejected explicitly, never
decoded or encoded approximately; see [Format reference](#format-reference) and [Limitations](#limitations).

| Format | Decoding | Encoding | `Save(path)` extensions |
| --- | --- | --- | --- |
| PNG / APNG | Every color type and bit depth, palettes, Adam7, APNG animations and poster frames | Gray, RGB and RGBA at 8/16 bits, Adam7, APNG with full-canvas frames and posters | `.png` (APNG when animated), `.apng` |
| GIF | GIF87a/89a, animations, interlacing, transparency, comments | GIF89a, deterministic quantization per frame, optional dithering, animations with unknown frame counts | `.gif` |
| JPEG | Baseline, extended sequential and progressive Huffman, 8-bit; gray, YCbCr, RGB; any integral sampling | Baseline 8-bit Huffman, gray or YCbCr, 4:4:4/4:2:2/4:2:0 | `.jpg`, `.jpeg` |
| WebP | Lossless, lossy, alpha, animations, ICC/EXIF/XMP | Lossless and lossy, alpha, animations (seekable destination) | `.webp` (lossless by default) |
| QOI | Every chunk form, 3 or 4 channels, colorspace label | Lossless 8-bit RGB/RGBA | `.qoi` |
| BMP | Windows headers up to V5, 1/4/8/16/24/32-bit, bit fields | Uncompressed 24-bit or 32-bit with an alpha mask | `.bmp`, `.dib` |
| TGA | True-color, grayscale and color-mapped, uncompressed or run-length | True-color and grayscale, uncompressed or run-length | `.tga`, `.icb`, `.vda`, `.vst` |
| Netpbm | `P1` to `P7` (PBM, PGM, PPM, PAM), 8 and 16 bits | `P2`/`P3`/`P5`/`P6`/`P7`, lossless | `.pnm`, `.pam`, `.ppm`, `.pgm` |
| TIFF / BigTIFF | Both byte orders and offset sizes, 8/16-bit gray, gray+alpha, RGB, RGBA, strips and tiles, Deflate, predictor; multi-page documents | Strips, uncompressed or Deflate, classic or BigTIFF; multi-page documents (seekable destination) | `.tif`, `.tiff` |
| ICO / CUR | PNG and DIB representations, cursor hotspots | PNG or 32-bit DIB representations, cursor hotspots | `.ico`, `.cur` |

JPEG XL and AVIF are not supported.

## Usage guide

### Images, frames and animations

**Full displayed frames, not encoded deltas**. An `Image` holds one or more frames; every frame is
the complete canvas a viewer shows at that point of the animation, with the canvas size and the image's pixel type.
Encoded delta rectangles, offsets, APNG blend/dispose operations and GIF disposal methods are resolved by the decoders (one
compositor shared by eager and sequential decoding) and recomputed by the encoders from the displayed frames. They are not
public state: editing, removing or reordering frames can never leave stale encoded instructions behind.

- `Frames[i].Metadata.Duration` is the frame's exact `FrameDuration`; `Image.Animation` (`TotalPlays`) holds the
  animation-wide settings. Animation settings are structural data, never removed by metadata policies.
- `IsAnimated` is true with several frames, a poster frame, or non-null `Animation`. Adding a second frame or a poster
  creates default settings (infinite loop); removing frames down to one keeps them, so the image stays animated until
  `Animation` is set to `null` (allowed only with one frame and no poster).
- Static outputs (static PNG, JPEG) reject animated images with `UnsupportedImageFeatureException` instead of silently
  saving the first frame; export a frame explicitly with `CloneFrame`.

**Posters and playback**. APNG can store a default image that is *not* part of the animation:
decoders that do not support APNG show it, APNG players skip it. It is exposed as `PosterFrame`, excluded from
`Frames.Count`, preserved by eager loads and by whole-image operations (geometry, flip, grayscale, convolution), and returned once by
`ImageReader.ReadPosterFrame` before the first displayed frame. An APNG whose default image *is* frame zero has no poster.
Only animated PNG output can store a poster; other outputs reject an image that has one (remove it with
`RemovePosterFrame`).

**Exact timing**. `FrameDuration` is a normalized rational number of seconds: GIF delays (`d/100`)
and APNG delays (`num/den`, `den = 0` meaning `/100`) are kept exactly, zero durations included; no player minimum-delay
heuristic is applied. `TotalPlays` counts the total number of plays including the first (`null` = infinite): a GIF
NETSCAPE2.0 loop count `L` stores repetitions, so it maps to `L + 1` plays (`L = 0` is infinite, no loop extension is one
play); APNG `num_plays` maps directly (`0` is infinite). On export, `FrameDurationRounding` decides what happens when a
duration is not representable: `RequireExact` throws, `RoundToNearest` picks the nearest representable value (APNG
default: `RequireExact`; GIF default: `RoundToNearest`, to the nearest hundredth with ties up). Values beyond the format's
range are always rejected, never clamped. `ToTimeSpan` rounds to the nearest 100 ns tick (ties up); `TotalSeconds` and
`TotalMilliseconds` are floating-point approximations for display.

<!-- snippet: build-animation -->
```csharp
public static void BuildAnimation(string apngPath, string gifPath)
{
    using var animation = new Image<Rgba32>(64, 64, new Rgba32(255, 0, 0));
    animation.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(100);

    var second = animation.AppendFrame(); // creates default animation settings (infinite loop)
    second.Metadata.Duration = new FrameDuration(1, 30); // exactly 1/30 s
    second.ProcessPixelRows(static pixels =>
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            pixels.GetRowSpan(y).Fill(new Rgba32(0, 0, 255));
        }
    });

    animation.Animation!.TotalPlays = 3; // played three times in total

    animation.Save(apngPath, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
    animation.Save(gifPath, new GifEncoder { Dithering = GifDithering.FloydSteinberg });
}
```

### Pages and representations

**Pages and representations are not frames**. A TIFF document and an icon file hold images that
are not an animation: the pages of a document in order, and the alternative sizes and color depths of one drawing. They
live in a separate, disposable `ImageCollection` whose entries may differ in size, pixel format and metadata, and which
never carries a duration, a blend or disposal operation or a loop count. A decoded entry is always a one-frame still
image, and `Image.OpenReader` rejects TIFF, ICO and CUR explicitly, so a consumer that plays frames can never receive pages
or icon sizes by accident.

- Loading (`ImageCollection.Load` from a path, a seekable stream or a span) reads only the container structure, so
  describing every entry of a large document costs a few kilobytes of reads. Each entry exposes its `Size`,
  `PixelFormat`, `ColorModel`, `BitsPerComponent`, `MayHaveTransparency`, `PayloadFormat`, `Hotspot` and `Metadata`;
  nothing is resized or converted implicitly.
- `ImageCollectionEntry.Decode` (or `Decode<TPixel>`) reads and decodes exactly one entry, with its own limits and
  allocation scope, and returns a normal independently owned `Image`.
- A loaded collection keeps its input open until it is disposed: a path stays open, a stream must be seekable and is
  seeked freely (its position afterward is unspecified), and a span is copied once. Loading is synchronous by design (a
  random-access decoder issues many small dependent reads); `Image.LoadAsync` and `Image.IdentifyAsync` do work on these
  formats, by buffering the input under `MaxEncodedBytes`.
- Entries are borrowed from their collection and are valid until it is disposed or they are removed. `Add` and `Insert`
  copy the image, so the caller keeps and disposes its own; `RemoveAt` and `Move` edit the order, and removed owned
  entries are released.
- `Image.Load` and `Image.Identify` use the first page of a document, or the largest (then deepest) representation of an
  icon, and report the number of entries in `ImageInfo.CollectionEntryCount`. `ImageCollection.SelectBySize` chooses a
  representation explicitly: the smallest one at least as large as the request, else the largest (ties by bit depth, then
  container order). Nothing is resized.
- `ImageCollection.Save` writes pages with `TiffEncoder` and representations with `IcoEncoder`; entries are materialized
  one at a time, and path destinations are published atomically.
- An icon or cursor representation stores at most 256 pixels per side. A cursor hotspot
  (`ImageCollectionEntry.Hotspot`) is metadata validated against its own representation, never a pixel offset.

<!-- snippet: tiff-pages -->
```csharp
public static Size WriteAndReadTiffDocument(string inputPath, string documentPath)
{
    using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
    {
        using var source = Image.Load(inputPath);
        using var first = source.CloneFrame(0); // a page is a still image, never an animation frame
        using var second = first.Clone();
        second.Resize(new ResizeOptions(first.Width / 2, first.Height / 2));

        // Add copies the image: the caller keeps and disposes its own
        pages.Add(first);
        pages.Add(second);
        pages.Save(documentPath, new TiffEncoder { Compression = TiffCompression.Deflate });
    }

    // Loading reads the directory structure only: the pixels of a page are read when that page is decoded
    using var document = ImageCollection.Load(documentPath);
    using var page = document[1].Decode();
    return page.Size; // the second page keeps its own size, never the first one's
}
```

<!-- snippet: ico-representations -->
```csharp
public static Point WriteAndReadIcon(string inputPath, string iconPath, string cursorPath)
{
    using var loaded = Image.Load(inputPath);
    using var source = loaded.CloneFrame(0); // a representation is a still image, never an animation frame
    using var representations = ImageCollection.Create(ImageCollectionKind.Representations);
    foreach (var side in (int[])[16, 32, 256])
    {
        using var sized = source.Clone();
        sized.Resize(new ResizeOptions(side, side) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
        representations.Add(sized, hotspot: new Point(side / 2, side / 2));
    }

    // An icon cannot store a hotspot, so the entry hotspots are unsupported metadata there
    representations.Save(iconPath, new IcoEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
    representations.Save(cursorPath, new IcoEncoder { Kind = IconKind.Cursor });

    using var icon = ImageCollection.Load(iconPath);
    using var best = icon.SelectBySize(new Size(24, 24)).Decode(); // the 32x32 representation: nothing is resized
    using var cursor = ImageCollection.Load(cursorPath);
    return cursor.SelectBySize(new Size(16, 16)).Hotspot!.Value;
}
```

### Ownership, lifetimes and leases

**Owned and borrowed objects**.

| Object | Lifetime |
| --- | --- |
| `Image`, `Image<TPixel>` (constructors, `Load`, `Clone`, `CloneAs`, `CloneFrame`, `ClonePosterFrame`, `ImportPixel*`, reader results) | Owned by the caller: dispose it. `Dispose` is idempotent. |
| `ImageFrame`, `ImageFrame<TPixel>`, frame collections | Borrowed views, not disposable. Invalid (`ObjectDisposedException`) once the frame is removed or replaced, or the image is disposed. After a geometry change, a frame reference still designates the same logical frame. |
| `ImageCollection` | Owned by the caller: dispose it. Its entries are borrowed, and the images they decode are owned. |
| Clones, extracted frames, reader results | Independent: they stay valid after the source image or the reader is disposed. |
| Input streams, spans and files of eager `Load`/`Identify` | Never retained: everything needed is copied while loading. |
| Raw pixel imports (`ImportPixelData`, `ImportPixelBytes`) | Always copied; caller memory is never wrapped. |
| `MetadataBlob`, profiles, options, encoders | Immutable (blobs copy their input); safe to share. |

**Leases**. Pixels are accessed through exclusive scoped leases: `ProcessPixelRows` (typed rows),
`ProcessPixelBytes` (raw native-endian bytes), the scalar indexer and `CopyPixelDataTo`/`CopyPixelBytesTo`. Row spans
contain exactly the visible pixels of one row; rows are contiguous but the whole image is not, and no API exposes
whole-image memory. The accessors are `ref struct`s, so a callback cannot be asynchronous or keep a span. While a lease
is active, disposing the image, structural edits and conflicting access throw `InvalidOperationException` (best-effort
detection, not synchronization). Leases are released even when the callback throws. Static callbacks and the explicit
state overloads allocate nothing per row.

<!-- snippet: typed-rows -->
```csharp
public static void InvertColors(string path)
{
    using var image = Image.Load<Rgba32>(path);
    foreach (var frame in image.Frames)
    {
        frame.ProcessPixelRows(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (ref var pixel in pixels.GetRowSpan(y))
                {
                    pixel = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A);
                }
            }
        });
    }

    image.Save(path);
}
```

<!-- snippet: typed-rows-state -->
```csharp
public static long ComputeLuminanceSum(Image<Gray16> image)
{
    var total = new StrongBox<long>();
    image.Frames[0].ProcessPixelRows(total, static (pixels, state) =>
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            foreach (var pixel in pixels.GetRowSpan(y))
            {
                state.Value += pixel.Value;
            }
        }
    });

    return total.Value;
}
```

<!-- snippet: raw-pixels -->
```csharp
public static byte[] RoundTripRawPixels(ReadOnlySpan<byte> rgba, int width, int height)
{
    using var image = Image.ImportPixelBytes<Rgba32>(rgba, width, height);
    image.Crop(new Rectangle(0, 0, width / 2, height));
    var result = new byte[image.Width * image.Height * 4];
    image.Frames[0].CopyPixelBytesTo(result);
    return result;
}
```

**Memory accounting**. Every library-controlled buffer is charged, at its actual rented
capacity, to the allocation scope of its owner (an image, or a reader and the images it returns) and bounded by
`ImageResourceLimits.MaxLiveAllocationBytes`. Images returned by a reader stay charged to the reader's scope until they are
disposed, even after the reader is disposed. Scopes are safety bounds per owner, not process-wide memory or CPU caps.

### Thread safety

Static methods (`Load`, `Identify`, `DetectFormat`, ...) are thread-safe, and so are the immutable
types (configuration, limits, options, encoders, `MetadataBlob`, profiles). Distinct images can be processed concurrently.
An `Image` and its frames must not be used concurrently while any thread mutates them or holds a lease. Readers and
writers support one operation at a time: overlapping calls throw `InvalidOperationException`. Internal parallelism is off
by default (`ImageConfiguration.MaxDegreeOfParallelism = 1`); with more workers, only large resizes and convolutions are
split into row bands, with results identical to the sequential ones.

### Failure atomicity



- **Geometry is atomic.** `Crop`, `AutoCrop`, `Resize`, `Rotate` and `AutoOrient` apply to every frame and the poster in
  one transaction: replacements are allocated (and charged) while the originals are live, and any failure before the commit
  (limit, allocation failure, cancellation) leaves dimensions, pixels, frame identities and order, and metadata unchanged.
- **Pixel mutations may be partial.** `Flip`, `Grayscale`, `Convolve`, row callbacks and `ReadFrameInto` write in place: a
  failure or a cancellation can leave some rows or frames updated, but the image stays structurally valid and
  disposable. `Convolve` rents its scratch rows first: exceeding the allocation limit leaves the image unchanged.
- Failed operations release every resource they acquired; nothing is leaked or returned to a pool while still visible.

### Pixel formats, precision and alpha

**Working formats**. Six pixel types: `Rgba32`, `Bgra32`, `Rgb24`, `Rgba64`, `Gray8`,
`Gray16` (sequential `Pack = 1` layouts, straight alpha, 16-bit components in native endianness; `PixelFormats` describes
them). Untyped `Load` picks the format that preserves the source: for example 16-bit PNGs load as `Gray16`/`Rgba64`,
palettes and transparency as `Rgba32`, JPEG as `Gray8`/`Rgb24`, GIF as `Rgba32` (`ImageInfo.PixelFormat` tells it before
decoding). Typed `Load<TPixel>` and `CloneAs<TPixel>` convert explicitly between any two formats: 8 to 16 bits is exact
(`v * 257`), 16 to 8 bits rounds to nearest, color to gray uses Rec. 709 luma on the encoded values. A 16-bit path never
goes through 8 bits.

**No silent loss**. Converting non-opaque pixels to a format without alpha throws
`UnsupportedImageFeatureException` unless an opaque background is given; fully opaque pixels never need one. The controls:

| Loss | Control |
| --- | --- |
| Alpha, when converting or loading into a format without alpha | `PixelConversionOptions.BackgroundColor` (flattened at source precision in the encoded color space) |
| Alpha, when encoding JPEG | `JpegEncoder.BackgroundColor` |
| Alpha, when encoding 24-bit BMP or plain Netpbm | `BmpEncoder.BackgroundColor`, `PnmEncoder.BackgroundColor` |
| Partial alpha, when encoding GIF (GIF has one transparent index) | `GifEncoder.AlphaMode`: `Threshold` (default, `AlphaThreshold` 128) or `Flatten` with `BackgroundColor` |
| 16-bit precision, when encoding JPEG, WebP, QOI, BMP or TGA | `AllowBitDepthReduction` on the encoder |
| 16-bit precision, when loading or converting | Request the narrower type explicitly (`Load<Rgba32>`, `CloneAs<Gray8>`) |
| Colors beyond 256 per frame, when encoding GIF | Lossy by design: deterministic quantization (`MaxColors`), optional `Dithering` |
| A color profile that cannot label the converted pixels | `PixelConversionOptions.DiscardIncompatibleColorProfile` |
| Metadata the output format cannot store | `ImageEncoder.MetadataHandling` (see [Metadata](#metadata-color-profiles-and-orientation)) |

<!-- snippet: convert-precision -->
```csharp
public static void ConvertToGray16(string inputPath, string outputPath)
{
    using var image = Image.Load<Rgba64>(inputPath);
    image.AutoOrient();
    using var gray = image.CloneAs<Gray16>(new PixelConversionOptions
    {
        BackgroundColor = new Rgba64(65535, 65535, 65535),
        DiscardIncompatibleColorProfile = true,
    });
    gray.Save(outputPath, new PngEncoder());
}
```

<!-- snippet: export-jpeg-frame -->
```csharp
public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
{
    using var animation = Image.Load(animatedPath);
    using var frame = animation.CloneFrame(frameIndex);
    frame.Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        BackgroundColor = new Rgba32(255, 255, 255),
        AllowBitDepthReduction = true,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```

### Processing

In-place extension methods (`ImageProcessingExtensions`), applied to every frame and the poster:
`Crop` (an in-canvas rectangle, never clamped), `AutoCrop` (the detected content), `Resize`, `Rotate` (exact 90/180/270
permutations), `Flip`, `AutoOrient` (all eight EXIF orientations), `Grayscale` (Rec. 709 at storage precision, ties upward; keeps the storage format and
alpha, and rejects incompatible ICC profiles; use `CloneAs<Gray8>` to change the storage) and `Convolve` (a convolution
matrix). `Flip`, `Grayscale` and `Convolve` also exist for one frame. Geometry changes preserve 16-bit precision and alpha, and reconcile the EXIF dimensions,
orientation and thumbnail.

**Auto-cropping**. `AutoCrop` removes the uniform background around the content of an image, such as the margin of a
product picture or of a scan, and returns whether the image changed. `AnalyzeAutoCrop` does the detection alone: it
returns an `AutoCropAnalysis` (`Success`, `Bounds`, `BackgroundColor`, `WeightX`, `WeightY`) and never changes the image.
On an `Image<TPixel>` the result is an `AutoCropAnalysis<TPixel>`, whose `BackgroundColor` is a `TPixel` as stored in the
image; on an untyped `Image`, `BackgroundColor` is the same color widened to `Rgba64`. An analysis can be applied later,
or to another image of the same size, with `AutoCrop(analysis, options)`.

- **Background.** The most frequent color of the one-pixel outer border, over every frame and the poster. The border is
  accepted when it has fewer than `ColorThreshold` (default 35) distinct colors, or, for noisy and JPEG backgrounds, when
  at least `BucketThreshold` (unset by default; 0.945 is a good start) of its pixels fall in the same of 11 luma buckets
  as the background. Otherwise the detection is retried once with half the threshold, without the outer 5% of the image
  on each side.
- **Content.** The bounding box of the pixels that are not background, over every frame and the poster, so that an
  animation is cropped consistently. A pixel is background when its luma-weighted color difference from the background
  (`0.2126 |dR| + 0.7152 |dG| + 0.0722 |dB|`) is at most `ColorThreshold` and its alpha difference is below it. Thresholds
  are on the 8-bit scale and comparisons are made at the storage precision. Fully transparent pixels are all equal,
  whatever their hidden color. The box must be at least 3x3 pixels.
- **Padding.** `PaddingX` and `PaddingY` keep a margin, in pixels, made of the original pixels. Where the margin reaches
  outside the canvas, `AutoCropPaddingMode.Expand` (default) enlarges the canvas and fills the new area with the
  background color, and `Contain` clamps the margin to the canvas.
- **Weights.** `AnalyzeWeights` also measures on which side of the canvas the content is heavier (from -1 to 1 on each
  axis) and moves the padded rectangle that way by `padding * weight` pixels.

When no border or no content is found (a uniform image, for instance), `AutoCrop` returns `false` and leaves the image
untouched. It works in stored-pixel coordinates: call `AutoOrient` first. The options follow those of
[ImageSharp.Processing.AutoCrop](https://github.com/Geta/ImageSharp.Processing.AutoCrop), with these differences: the
padding is in pixels instead of percents, the color difference uses the Rec. 709 weights of the library, the six pixel
formats and every frame are analyzed, the retry removes the 5% on all four sides, `ColorThreshold` is always set, and a
uniform image reports the whole canvas as its box.

<!-- snippet: auto-crop -->
```csharp
public static bool TrimBackground(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // The detection works on the stored pixels: apply the EXIF orientation first
    image.AutoOrient();

    // Read-only: the background color and the bounding box of everything else, in every frame
    var options = new AutoCropOptions { PaddingX = 8, PaddingY = 8, BucketThreshold = 0.945 };
    var analysis = image.AnalyzeAutoCrop(options);
    if (!analysis.Success)
        return false; // no uniform border, or no content of at least 3x3 pixels

    // Keeps 8 pixels around the content. Where the canvas is too small for the margin, it is enlarged and filled with
    // the background color (AutoCropPaddingMode.Contain clamps the margin instead)
    var changed = image.AutoCrop(analysis, options);
    image.Save(outputPath);
    return changed;
}
```

**Resizing**. `ResizeOptions` sets the target box and `Mode`: `Contain` (default: fit inside,
never padded), `Cover` (fill exactly, cropped around `Anchor`, one of nine positions) or `Stretch`;
`AllowUpscaling = false` keeps `Contain` within the original size and rejects `Cover`/`Stretch` enlargements. Target sizes
are computed with exact rational arithmetic, rounded to nearest (ties up), and are at least 1 pixel. Filters: nearest
neighbor, bilinear, bicubic Catmull-Rom (default) and Lanczos3, sampled at pixel centers with clamped edges, normalized
weights, and a support widened when downsampling. Colors are filtered premultiplied by alpha, so hidden colors of
transparent pixels never bleed (zero alpha becomes transparent black), and 16-bit images are filtered with 16-bit
precision.

- `ResizeWorkingSpace.Encoded` (default) filters the stored sample values directly, like most image tools.
- `ResizeWorkingSpace.LinearSrgb` converts sRGB samples to linear light (IEC 61966-2-1 transfer) before filtering and back
  afterward (more physically correct averages, for example for thin bright lines). It accepts untagged images (assumed
  sRGB) and ICC profiles recognized as sRGB or sGray only; any other profile is rejected rather than misinterpreted. No
  ICC transform is applied in either space.

A resize to the current size leaves the image unchanged. Results do not depend on the worker count or on SIMD support:
the vectorized and parallel kernels are bit-identical to the scalar reference.

<!-- snippet: resize-animation -->
```csharp
public static void ResizeAnimation(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Contain, Filter = ResamplingFilter.Lanczos3 });

    // Same format as the extension; APNG/GIF timing and play count come from the decoded image
    image.Save(outputPath);
}
```

**Convolution matrices**. `Convolve` replaces every sample by the weighted sum of the same sample
of the neighboring pixels: sharpening, blurring, embossing and edge detection are all matrices. A `ConvolutionKernel` has
odd dimensions, so its middle weight is the one of the pixel itself, and holds its weights row by row. It is applied
*as written* (the top-left weight multiplies the top-left neighbor; it is not flipped) and the weights are used as given:
divide them by their sum to keep the overall brightness. `ConvolutionOptions` then chooses:

- `EdgeMode`, the pixels read outside the image: `Clamp` (default, the nearest edge pixel), `Mirror` (the image reflected
  about its border), `Wrap` (the image tiled) or `Zero` (nothing: transparent black).
- `PreserveAlpha`. By default the filter is alpha-aware, like resizing: colors are filtered premultiplied by alpha, alpha
  is filtered too, and a pixel whose alpha becomes zero is transparent black, so hidden colors never bleed. A matrix
  whose weights add up to zero (edge detection) therefore turns every pixel transparent: set `PreserveAlpha = true` to
  filter the colors as stored and leave alpha untouched. Pixel formats without alpha are not concerned.
- `WorkingSpace`: `Encoded` (default) or `LinearSrgb`, with the same meaning and the same ICC profile rule as
  `ResizeWorkingSpace`.

Sums are computed in double precision at the storage precision (16-bit images are filtered with 16-bit precision),
rounded to nearest (ties up) and clamped to the sample range: negative sums become 0. The image is rewritten in place,
with a scratch of a few rows per worker proportional to the matrix height, charged to the allocation limit; the pixel
format, the size, the frames and the metadata are kept (the EXIF thumbnail, now stale, is removed). As for resizing,
results do not depend on the worker count or on SIMD support. The cost grows with the number of non-zero weights: there
is no dedicated fast path for separable matrices such as large blurs.

<!-- snippet: convolve -->
```csharp
public static void SharpenAndDetectEdges(string inputPath, string sharpenedPath, string edgesPath)
{
    using var image = Image.Load(inputPath);

    // The matrix is applied as written, row by row; weights that add up to 1 keep the overall brightness
    var sharpen = new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]);
    using (var sharpened = image.Clone())
    {
        sharpened.Convolve(new ConvolutionOptions(sharpen));
        sharpened.Save(sharpenedPath);
    }

    // Weights that add up to 0 would also filter alpha down to 0 (transparent black): keep the alpha instead
    var laplacian = new ConvolutionKernel(3, 3, [0, 1, 0, 1, -4, 1, 0, 1, 0]);
    image.Convolve(new ConvolutionOptions(laplacian) { PreserveAlpha = true, EdgeMode = ConvolutionEdgeMode.Mirror });
    image.Save(edgesPath);
}
```

There is no ICC color conversion, HDR processing, drawing, text rendering or custom processor.

### Metadata, color profiles and orientation

`Image.Metadata` holds the source format (informational only), the EXIF orientation, the
resolution, ICC/EXIF/XMP profiles (byte-for-byte payloads) and text entries.

- **Color profiles are preserved, never applied.** A profile labels the pixels; untagged pixels are assumed sRGB.
  Decoders keep it, encoders write it (for example PNG `iCCP`, JPEG APP2), conversions keep it only when it can still
  label the converted pixels (gray profile for gray formats, RGB profile for color formats). There is no color
  management.
- **Transfer functions are labels too.** `Metadata.TransferFunction` is `Srgb` (the default: sRGB samples, or as described
  by the ICC profile) or `Linear` (linear-light samples, declared by a QOI colorspace of 1). No operation converts the
  pixels; linear-light resizing filters `Linear` samples directly. Only QOI stores the label: the other encoders treat
  `Linear` as metadata they cannot store, so the samples are never silently relabeled as sRGB.
- **Orientation is reported, never applied by decoders.** `Metadata.Orientation` reports the EXIF orientation;
  `AutoOrient()` transforms the pixels for all eight orientations and resets it to `TopLeft`. On save, the typed
  orientation is authoritative: EXIF orientation and dimension tags are rewritten and thumbnails made stale by geometry
  changes are removed.
- **Saving follows `ImageEncoder.MetadataHandling`**: `Strict` (default) throws `UnsupportedImageFeatureException` for
  metadata the format cannot store, `DiscardUnsupported` drops it, `Strip` writes no optional metadata. Animation timing
  is never affected. A malformed caller-supplied EXIF, ICC or XMP payload is rejected at save time.
- Decoded metadata is bounded: payloads, decompressed text included, are charged to `MaxMetadataBytes`.

| Metadata | PNG | GIF | JPEG | WebP | QOI | BMP | TGA | PNM |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ICC profile | `iCCP` | not stored | APP2 (up to 255 segments) | `ICCP` (RGB profiles) | not stored | not stored | not stored | not stored |
| EXIF and orientation | `eXIf` | not stored (orientation must be `TopLeft`) | APP1 (at most 65,527 bytes) | `EXIF` | not stored (orientation must be `TopLeft`) | not stored (orientation must be `TopLeft`) | not stored (orientation must be `TopLeft`) | not stored (orientation must be `TopLeft`) |
| XMP | `iTXt` `XML:com.adobe.xmp` | not stored | APP1, standard XMP only (at most 65,504 bytes, no extended XMP) | `XMP ` | not stored | not stored | not stored | not stored |
| Text | `tEXt`/`zTXt`/`iTXt` | Comment extension (`Comment` entries, Latin-1) | COM (`Comment` entries, Latin-1, at most 65,533 characters) | not stored | not stored | not stored | not stored | not stored |
| Resolution | `pHYs` (pixels per meter) | not stored | JFIF density | not stored | not stored | `biXPelsPerMeter`/`biYPelsPerMeter` | not stored | not stored |
| Linear transfer function | not stored | not stored | not stored | not stored | header colorspace 1 | not stored | not stored | not stored |

TIFF stores the resolution, the orientation, an ICC profile and XMP (no EXIF block, no text entries); ICO and CUR store no
metadata. Unknown chunks, segments and extensions are skipped on decode and not round-tripped.

<!-- snippet: edit-metadata -->
```csharp
public static void EditMetadata(string path)
{
    using var image = Image.Load(path);
    image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Sunset"));
    image.Metadata.Resolution = new ImageResolution(300, 300);
    image.Save(path, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
}
```

### Loading, identification and limits

Detection is content-based (never by extension); `Image.DetectFormat` needs at most
`Image.FormatDetectionPrefixLength` (18) bytes, because TGA has no signature and is recognized from a strictly plausible
18-byte header, consulted last. `Load` exists in synchronous and asynchronous, path, stream and span, typed and untyped
forms, with identical results and error categories. `Identify` returns an `ImageInfo` without decoding pixels: `Header`
mode reads up to the first pixel data, `FullScan` walks the whole structure (chunks, blocks, markers, CRCs, rows). Unknown
values stay `null` and are never guessed (a GIF header never yields a frame count).

<!-- snippet: identify -->
```csharp
public static string Describe(Stream stream)
{
    var info = Image.Identify(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
    var frames = info.FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    return $"{info.Format} {info.Size} {info.PixelFormat}, frames: {frames}, orientation: {info.Metadata.Orientation}";
}
```

**Limits versus prefix selection.** `ImageResourceLimits` (canvas width/height, pixels per frame, frames, total pixels,
encoded bytes, metadata bytes, live allocations) are safety bounds: exceeding one throws `ImageResourceLimitException`
(`Kind`, `Limit`, `Requested`) and is never turned into a successful truncation. `FrameLimit` (decode and reader options)
is deliberate prefix selection: decoding stops after that many displayed frames and the rest of the input is neither
examined nor validated. Limits are inclusive and enforced before allocating or consuming.

TIFF, ICO and CUR are random-access containers: on a seekable source, only the bytes their structure points at are read
(the stream position afterward is unspecified), and a non-seekable or asynchronous load buffers the input, bounded by
`MaxEncodedBytes`.

### Streaming readers and writers

Readers and writers process long animations frame by frame with memory bounded by about one frame
of decoder or encoder state, plus the images the caller keeps.

**Streams.** Every stream API starts at the stream's current position and consumes what it reads (no rewinding).
Non-seekable streams are supported for input (bounded prefix replay) and output (no seeking, except for the WebP
animations and TIFF files described below). Caller streams are left open: eager APIs never close them, and
readers/writers close them only with `LeaveOpen = false` (for a reader, ownership then transfers even when opening fails).
Path overloads own and close their files. Asynchronous methods use asynchronous I/O and accept a `CancellationToken`;
argument errors are thrown synchronously.

**Readers** (`Image.OpenReader<TPixel>`):

- `Info` is a header snapshot: format, canvas, frame count when the header declares it (`null` for GIF), animation
  settings and the metadata before the first frame.
- `ReadPosterFrame` returns the APNG poster (or `null`); it is allowed once, before the first displayed frame. A poster that
  is not read is still validated and counted against `MaxFrames`.
- `ReadFrame` returns an owned single-frame still image (with its duration and image metadata; animation-wide settings are
  in `Info`), or `null` at the clean end of the animation or when `FrameLimit` is reached.
- `ReadFrameInto(destination)` overwrites a caller-owned image instead of allocating one. The destination must have the
  canvas size, exactly one frame, no poster and no animation settings; its duration and metadata are refreshed. It returns
  `false` at the clean end (destination unchanged); after a failure the destination may be partially updated but stays
  valid, and it stays charged to its own owner.
- `null`/`false` always means a clean end. Malformed data, limits, I/O errors and cancellation throw and fault the reader
  (later calls throw `InvalidOperationException`); argument errors leave it usable.
- TIFF, ICO and CUR are rejected explicitly: their entries are pages or representations, not a frame sequence (use
  `ImageCollection`).

**Writers** (`Image.CreateWriter<TPixel>`): an optional `WritePosterFrame` (animated PNG only, before the first frame),
then `WriteFrame` for each frame, then **`Complete`, which is mandatory**: disposing a writer without a successful
`Complete` aborts the output, it is never finalized implicitly. `Complete` is idempotent. The options (canvas, encoder,
metadata, animation settings) are snapshotted when the writer is created, and the metadata policy is applied before any
output.

| Output | `ExpectedFrameCount` |
| --- | --- |
| PNG (static) | Required, 1 |
| APNG | Required (the count precedes the image data and seeking is never used) |
| GIF | Optional: unknown counts are supported with fixed memory (local palettes, no global prepass) |
| WebP | Optional; an animation (any count other than 1) needs a seekable destination |
| JPEG, QOI, BMP, TGA, Netpbm, TIFF, ICO/CUR | Optional; exactly one frame |

`Complete` checks that at least one frame was written and that the count matches. Errors detected before any work
(state, order, frame size or pixel type, a frame duration the format cannot store) leave the writer usable; a failure once
encoding started faults it.

<!-- snippet: streaming-transform -->
```csharp
public static async Task StreamingResizeAsync(Stream input, Stream output, CancellationToken cancellationToken)
{
    await using var reader = await Image.OpenReaderAsync<Rgba32>(input, cancellationToken: cancellationToken);
    var info = reader.Info;
    var size = new Size(info.Width / 2, info.Height / 2);

    await using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(size)
    {
        Encoder = new GifEncoder(),
        Animation = info.Animation, // e.g. preserve the number of plays
        Metadata = info.Metadata,
    });

    while (await reader.ReadFrameAsync(cancellationToken) is { } frame)
    {
        using (frame)
        {
            frame.Resize(new ResizeOptions(size) { Mode = ResizeMode.Stretch }, cancellationToken);
            await writer.WriteFrameAsync(frame.Frames[0], cancellationToken);
        }
    }

    await writer.CompleteAsync(cancellationToken); // mandatory: disposing without Complete aborts the output
}
```

<!-- snippet: read-frame-into -->
```csharp
public static void StreamingWithReusedBuffer(string inputPath, string outputPath)
{
    using var reader = Image.OpenReader<Rgba32>(inputPath);
    var info = reader.Info;
    var frameCount = info.FrameCount ?? throw new InvalidOperationException("PNG output requires a known frame count.");

    using var writer = Image.CreateWriter<Rgba32>(outputPath, new ImageWriterOptions(info.Size)
    {
        Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated },
        ExpectedFrameCount = frameCount,
        Animation = info.Animation,
    });

    if (reader.ReadPosterFrame() is { } poster)
    {
        using (poster)
        {
            writer.WritePosterFrame(poster.Frames[0]);
        }
    }

    using var buffer = new Image<Rgba32>(info.Width, info.Height);
    while (reader.ReadFrameInto(buffer))
    {
        buffer.Grayscale();
        writer.WriteFrame(buffer.Frames[0]);
    }

    writer.Complete(); // publishes outputPath atomically
}
```

### Saving files

`Save(stream, encoder)` requires an encoder. `Save(path)` infers it from the extension: `.png`
(APNG when the image is animated), `.apng` (always APNG), `.gif`, `.jpg`/`.jpeg`, `.webp` (lossless by default), `.qoi`,
`.bmp`/`.dib`, `.tga`/`.icb`/`.vda`/`.vst`, `.pnm`/`.pam`/`.ppm`/`.pgm`, `.tif`/`.tiff`, `.ico` and `.cur`; anything
else throws `ArgumentException`. An eager save validates the whole image against the output (animation, poster,
durations, alpha, precision, metadata) before writing anything.

Path outputs, eager or through writers, are written to a temporary file in the destination directory and published by a
rename only after a successful `Complete`: on any failure, cancellation or uncompleted writer, an existing destination is
left untouched and only the library's own temporary file is deleted. No crash durability (`fsync`) is claimed. Stream
outputs are written as they go; after a failure, the bytes already written remain and nothing is finalized.

### Errors

Content errors derive from `ImageException`:

| Exception | Meaning |
| --- | --- |
| `UnknownImageFormatException` | The signature is not recognized. |
| `InvalidImageContentException` (`Format`) | Malformed, inconsistent or truncated data; also a malformed caller-supplied EXIF/ICC/XMP payload at save time. |
| `UnsupportedImageFeatureException` (`Format`, `Feature`) | A valid but unsupported feature (arithmetic JPEG, CMYK, GIF plain text...), or an operation that would lose animation, alpha, precision, metadata, a poster or a profile without an explicit setting, or a duration/play count the output cannot represent. |
| `ImageResourceLimitException` (`Kind`, `Limit`, `Requested`) | A configured limit was exceeded. |

Other failures use the standard exceptions: `ArgumentException` (invalid arguments, mismatched frames),
`NotSupportedException` (an unsupported `TPixel`, before any I/O), `InvalidOperationException` (invalid state, lease
conflicts, overlapping or faulted reader/writer calls), `ObjectDisposedException`, `OperationCanceledException`, and
`IOException` from the stream, propagated unchanged. Malformed data and resource failures are never reported as a clean
end of input.

<!-- snippet: error-handling -->
```csharp
public static Image? TryLoad(string path, ImageConfiguration configuration)
{
    try
    {
        return Image.Load(path, new ImageDecodeOptions { Configuration = configuration });
    }
    catch (UnknownImageFormatException)
    {
        return null; // not an image
    }
    catch (UnsupportedImageFeatureException ex)
    {
        Console.Error.WriteLine($"Unsupported {ex.Format} feature: {ex.Feature}");
        return null;
    }
    catch (ImageResourceLimitException ex)
    {
        Console.Error.WriteLine($"Limit {ex.Kind} exceeded ({ex.Requested} > {ex.Limit})");
        return null;
    }
    catch (InvalidImageContentException)
    {
        return null; // corrupted or truncated file
    }
}
```

## Format reference

Every codec works eagerly (`Load`, `Save`) and sequentially (readers, writers) with the same results, and streams with
bounded memory unless stated otherwise.

### PNG and APNG

`PngEncoder.AnimationMode`: `Auto` writes an APNG for animated images (several frames,
a poster or animation settings), `Animated` and the `.apng` extension always do; the static mode rejects animated images.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Grayscale 1/2/4/8/16-bit | Sub-byte samples scaled exactly to `Gray8`; 16-bit kept in `Gray16`; a `tRNS` key gives `Rgba32`/`Rgba64` | `Gray8` → gray 8, `Gray16` → gray 16 (all 16 bits kept) |
| RGB / RGBA 8/16-bit, gray+alpha | 16-bit precision preserved, gray+alpha expanded to RGBA | `Rgb24` → RGB 8, `Rgba32`/`Bgra32` → RGBA 8, `Rgba64` → RGBA 16; fixed mapping, no data-dependent reduction; RGB 16 and gray+alpha are not produced |
| Palette + `tRNS` | 1/2/4/8-bit, partial `tRNS`, out-of-range indices rejected | Not supported (re-encoded as RGB/RGBA) |
| Adam7 interlacing | Empty passes, pass rows scattered directly into the frame (no intermediate image) | Option; pass rows gathered from the frame storage, empty passes have no scanline |
| Filters | All five; invalid types rejected | Adaptive minimum-sum-of-absolute-differences heuristic, or one fixed type for every scanline |
| zlib datastream | Truncation, extra data, Adler-32 and expansion (bounded by the image size) validated; streamed with bounded memory | BCL zlib at the four `CompressionLevel` values; `IDAT` chunks of at most 32 KiB, streamed in bounded bands |
| Metadata (`iCCP`, `eXIf`, XMP `iTXt`, `tEXt`/`zTXt`/`iTXt`, `pHYs`) | Bounded; metadata after `IDAT` included | Written before `IDAT`; typed orientation and canvas reconciled into EXIF; incompatible ICC profiles and invalid text entries follow `MetadataHandling`; long texts compressed |
| APNG animations | Full-canvas displayed frames from one compositor (SOURCE/OVER blending with an exact rounding contract at 8 and 16 bits, NONE/BACKGROUND/PREVIOUS disposal; BACKGROUND clears to transparent black), every color type and bit depth, Adam7 frame regions, partial rectangles, split `fdAT` chunks, exact rational delays, total plays; control-data errors are never reported as a clean end or a still image | Every displayed frame written as a full-canvas `SOURCE` frame with freshly computed control data, so decoded delta/disposal/blend data never survives an edit; exact delays (`RequireExact`, default) or `RoundToNearest` (nearest 16-bit fraction, ties to the longer delay); writers require `ExpectedFrameCount` and stream to non-seekable outputs |
| APNG poster frame | `PosterFrame`, excluded from `Frames.Count`; a default image with an `fcTL` is frame zero | The `IDAT` image without a preceding `fcTL`, written once before frame zero, not counted in `num_frames` |
| APNG delta-rectangle optimization | — | Not supported (full-canvas frames) |
| Unknown critical chunks (e.g. Apple `CgBI`) | Not supported (`UnsupportedImageFeatureException`) | — |
| Unknown ancillary chunks | Skipped, not round-tripped | Not written |

### GIF



| Feature | Decoding | Encoding |
| --- | --- | --- |
| GIF87a / GIF89a, LZW | Minimum code sizes 1-11, codes up to 12 bits, clear codes anywhere, full tables without clear; a missing end code after a complete image and data after the end code are tolerated; invalid codes, short or overlong data, out-of-table indices and images without a color table are `InvalidImageContentException` | GIF89a, no global color table, one full-canvas image per frame, LZW with a clear code on a full table, 255-byte sub-blocks |
| Palettes | Global and local, per image; output always `Rgba32` | One local palette per frame of at most `MaxColors` entries (2–256, the transparent entry included); exact palette when the frame's colors fit, otherwise a deterministic variance-based median cut; nearest-color mapping. Lossy by design |
| Interlacing | Four passes, any height | `GifEncoder.Interlaced` |
| Transparency and disposal | Full-canvas displayed frames from one compositor (partial rectangles clipped to the screen, the transparent index keeps the canvas); the canvas and disposal 2 are transparent black (the background color index is never painted); disposal 3 restores (first image: clears); undefined 4 is "restore previous", 5–7 "none" | Recomputed from the displayed frames: every frame covers the canvas with disposal 2, so pixels that become transparent never show the previous frame; alpha thresholded (default 128) or flattened onto an explicit background, never partial |
| Dithering | — | `GifDithering.FloydSteinberg`: deterministic integer error diffusion, quantized frames only |
| Loop extension (NETSCAPE2.0, ANIMEXTS1.0) | `L` repetitions → `TotalPlays = L + 1`, `0` → infinite, none → one play; the last extension wins; exact hundredth-second delays | NETSCAPE2.0 with `TotalPlays - 1` repetitions (none for one play, at most 65,536 plays); delays to the nearest hundredth (ties up) or exact (`RequireExact`), beyond 655.35 s rejected, never clamped |
| Comment extension | `Comment` text entries, Latin-1 (readers see those before the first image) | `Comment` entries only, Latin-1, any length; ICC, EXIF, XMP and resolution follow `MetadataHandling` |
| Plain-text extension | Not supported (`UnsupportedImageFeatureException`) | — |
| Other application extensions (XMP, ICC...) | Skipped | — |

### JPEG



| Feature | Decoding | Encoding |
| --- | --- | --- |
| Sequential Huffman, 8-bit | Baseline (SOF0) and extended sequential (SOF1): 8/16-bit DQT, DHT identifiers 0-3, tables redefined between scans, interleaved or non-interleaved scans, double-precision IDCT with documented rounding; streamed with three MCU rows of decoder state (full planes for multi-scan frames) | Baseline (SOF0) only: one interleaved scan, Annex K typical Huffman tables (no optimized tables), Annex K quantization tables scaled by `Quality` 1-100 (default 90), double-precision forward DCT with documented rounding, no restart markers; streamed by MCU rows |
| Progressive Huffman (SOF2), 8-bit | DC and AC first and refinement scans, spectral selection, EOB runs, successive approximation, restart intervals, partial progressions, every sampling layout and color model of the sequential decoder; scan parameters validated from the scan headers (also by a full `Identify`). Not row-streamed: the 16-bit coefficients of the whole image (about 2 bytes per sample and component) are kept until the end of the image | Not supported |
| Color models | Grayscale (`Gray8`), JFIF full-range YCbCr and RGB (Adobe transform 0 or `R`,`G`,`B` identifiers) to `Rgb24` | The layout follows the pixel format, never the data: gray formats → one grayscale component, color formats → JFIF full-range YCbCr (BT.601); JFIF APP0 always written, no Adobe APP14. Non-opaque pixels require `BackgroundColor`; `Rgba64`/`Gray16` require `AllowBitDepthReduction` |
| Chroma subsampling | 4:4:4, 4:2:2, 4:2:0, 4:4:0, 4:1:1 and any integral sampling ratio (libjpeg-turbo-compatible triangle filters for factor 2, replication otherwise) | `Auto` (= 4:2:0 for color), 4:4:4, 4:2:2, 4:2:0; box-filter downsampling, canvas padded to whole MCUs by edge replication; ignored for gray formats |
| Restart markers | Sequential and progressive scans, sequence checked | Not written |
| Metadata (EXIF, XMP, ICC, JFIF density, COM) | Bounded; ICC reassembled from APP2 chunks; segments after the scans included | APP1 EXIF (orientation and dimensions reconciled; at most 65,527 bytes), APP1 standard XMP (at most 65,504 bytes), APP2 ICC in up to 255 chunks, JFIF density, COM for `Comment` entries; anything else follows `MetadataHandling` (`Strip` writes only the JFIF segment) |
| Animated images, posters | — | Rejected with `UnsupportedImageFeatureException` before any output; export a frame with `CloneFrame` |
| Arithmetic, lossless, hierarchical, 12/16-bit, CMYK/YCCK, non-integral sampling | Not supported (`UnsupportedImageFeatureException`, also from `Identify`) | Not supported |

### WebP

`WebPEncoder.Compression` chooses the bitstream for every frame: `Lossless` (the
default) keeps every 8-bit sample, including the colors of fully transparent pixels (`ClearTransparentColors` opts into
transparent black); `Lossy` is VP8 with 4:2:0 chroma, controlled by `Quality` (0-100), and keeps alpha losslessly.
`Effort` (0-9, default 5) trades encoding time for size and never changes lossless pixels. Gray images are stored as RGB;
16-bit images need `AllowBitDepthReduction`.

A still image streams to any destination. An animation (several frames, animation settings, or a writer without
`ExpectedFrameCount = 1`) declares its size before its frames, so it needs a **seekable** destination: frames are written
as they come and the RIFF size and alpha flag are patched at the end. Non-seekable streams are rejected with an
`ArgumentException` before anything is written (there is no spooling); path outputs are always seekable and atomic.
Durations are rounded to the nearest millisecond by default (`DurationRounding`), and the loop count holds 1 to 65,535
plays or infinite.

<!-- snippet: save-webp -->
```csharp
public static void SaveWebP(string photoPath, string webpPath, string animatedPath, string animatedWebPPath)
{
    using (var photo = Image.Load(photoPath))
    {
        photo.Save(webpPath, new WebPEncoder
        {
            Compression = WebPCompression.Lossy,
            Quality = 80, // 0-100: size versus fidelity
            Effort = 6,   // 0-9: encoding time versus size
            AllowBitDepthReduction = true, // 16-bit sources are written with 8 bits
        });
    }

    // Every sample is kept, including the colors of fully transparent pixels. An animation needs a seekable
    // destination (the file size is patched at the end): a path, a FileStream or a MemoryStream.
    using var animation = Image.Load(animatedPath);
    animation.Save(animatedWebPPath, new WebPEncoder { Compression = WebPCompression.Lossless });
}
```

Decoding: lossless images decode exactly; lossy images decode to the exact VP8 reconstruction (RFC 6386) converted to RGB
with a documented BT.601 contract and centered bilinear chroma upsampling (within one unit of libwebp). Still images
without alpha load as `Rgb24`, others and every animation as `Rgba32`. Animation frames are full-canvas displayed images
(alpha blending with an exact OVER contract, mixed lossy and lossless frames, exact millisecond durations); disposed areas
become transparent black and the `ANIM` background color is never painted. The container walker buffers one image's
payloads at most, skips unknown chunks, and charges the canvas to the limits before any image. ICC (RGB profiles), EXIF
and XMP are read from and written to extended files; text and resolution have no WebP chunk. VP8 interframes and hidden
frames are not supported.

### QOI

QOI is a small lossless format for one 8-bit RGB or RGBA still image. `QoiEncoder`
keeps every 8-bit sample, including the colors of fully transparent pixels; pixel formats with alpha are written with 4
channels, the others with 3 (gray as RGB), and 16-bit images need `AllowBitDepthReduction`. Its output is byte-identical to
the qoi.h reference encoder. QOI stores no metadata: ICC, EXIF, XMP, orientation, resolution and text follow
`MetadataHandling` (rejected by default). The header colorspace is written from `Metadata.TransferFunction` (`Strip`
writes 0) and decoded back into it. Encoding and decoding stream: the decoder keeps one row and consumes the input as it
arrives, the encoder writes bounded bands to any destination.

<!-- snippet: save-qoi -->
```csharp
public static void SaveQoi(string inputPath, string qoiPath, string linearQoiPath, string pngPath)
{
    using var image = Image.Load(inputPath);
    using var frame = image.CloneFrame(0); // QOI stores one still image
    frame.Save(qoiPath, new QoiEncoder { AllowBitDepthReduction = true });

    // A QOI colorspace of 1 (linear samples) is reported as a label, never applied: other formats cannot store it, so
    // saving the pixels elsewhere requires an explicit decision instead of silently relabeling them as sRGB
    using var linear = Image.Load(linearQoiPath);
    if (linear.Metadata.TransferFunction == ColorTransferFunction.Linear)
    {
        linear.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
    }
}
```

Decoding: 3-channel files load as `Rgb24` (opaque, whatever alpha values the chunks carry), 4-channel files as `Rgba32`.
Every chunk form is supported, runs may cross rows, and bytes after the end marker are not read. Zero dimensions, other
channel counts or colorspaces, a run past the last pixel, a missing end marker and truncation are
`InvalidImageContentException`; dimensions above 2^31 - 1 or the configured limits are `ImageResourceLimitException`
before any allocation.

### BMP

BMP, TGA and Netpbm are uncompressed (or run-length) formats for desktop interchange
(BMP), asset pipelines (TGA) and tooling fixtures (Netpbm). Each stores one still image, and each encoder makes its
format-specific choices explicit.

<!-- snippet: save-asset-formats -->
```csharp
public static void SaveAssetFormats(string inputPath, string bmpPath, string tgaPath, string pnmPath)
{
    using var image = Image.Load(inputPath);
    using var frame = image.CloneFrame(0); // BMP, TGA and Netpbm store one still image

    // BMP: 32-bit with an explicit alpha mask for pixel formats with alpha, 24-bit otherwise. Forcing 24-bit discards
    // alpha, so it needs a background color.
    frame.Save(bmpPath, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr24, BackgroundColor = new Rgba64(65535, 65535, 65535), AllowBitDepthReduction = true });

    // TGA: run-length packets are lossless and usually smaller on graphic content
    frame.Save(tgaPath, new TgaEncoder { Compression = TgaCompression.RunLength, AllowBitDepthReduction = true });

    // Netpbm: the variant follows the pixel format (PGM, PPM, or PAM when the pixels have alpha), and MAXVAL follows
    // the precision, so 16-bit images keep every bit
    frame.Save(pnmPath, new PnmEncoder());
}
```

| Feature | Decoding | Encoding |
| --- | --- | --- |
| DIB headers | `BITMAPINFOHEADER` and the V2, V3, V4 and V5 headers. OS/2 headers are `UnsupportedImageFeatureException`; inconsistent headers are `InvalidImageContentException` | `BITMAPINFOHEADER` for 24-bit output, `BITMAPV4HEADER` (`LCS_sRGB`) for 32-bit output |
| Layouts | 1/4/8-bit indexed (partial palettes, indexes outside the palette rejected), 16-bit (5-5-5), 24-bit BGR, 32-bit BGRX; 2-bit (Windows CE) is not supported | 24-bit `BI_RGB` and 32-bit `BI_BITFIELDS`; indexed and 16-bit output are never written |
| Bit fields (`BI_BITFIELDS`, `BI_ALPHABITFIELDS`) | 16 and 32 bits, contiguous masks of 1 to 8 bits, expanded with `round(value * 255 / (2^n - 1))`; channels wider than 8 bits (10-10-10-2) are not supported | Explicit BGRA masks for 32-bit output |
| Alpha | Only a declared alpha mask is transparency: the fourth byte of a 32-bit `BI_RGB` payload is unspecified padding, so such a file loads as opaque `Rgb24` | `BmpPixelLayout.Auto` writes 32-bit with an explicit alpha mask for pixel formats with alpha; `Bgr24` discards alpha and needs `BackgroundColor` |
| Rows | Padded to four bytes, bottom-up or top-down; bytes after the last row are not read | Bottom-up, padded to four bytes (read from the frame in reverse, no seeking) |
| Compression | RLE4, RLE8 and embedded JPEG/PNG payloads are `UnsupportedImageFeatureException` | Never written |
| Metadata | Resolution (`biXPelsPerMeter`/`biYPelsPerMeter`); V4/V5 color-space fields and embedded ICC profiles are not read | Resolution only; everything else follows `MetadataHandling` |

### TGA

TGA has no signature, so it is recognized from a strictly plausible 18-byte header
and checked last; give `DetectFormat` 18 bytes (`Image.FormatDetectionPrefixLength`). The TGA 2.0 developer area,
extension area and footer are located by offsets stored at the end of the file, which a forward-only decoder cannot
follow: they are not read, so a TGA with a trailer leaves its trailing bytes unread, and a prefix that holds the whole
raster is a valid TGA 1.0 file with the same pixels (truncation is only detected before the end of the image data).

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Image types | Uncompressed and run-length true-color, grayscale and color-mapped | True-color and grayscale; color-mapped output is never written |
| Depths | 15/16-bit `A1 R5 G5 B5`, 24-bit BGR, 32-bit BGRA, 8-bit grayscale, 8-bit indexes with 15/16/24/32-bit map entries; 16-bit indexes and 16-bit grayscale are not supported | 8-bit grayscale, 24-bit BGR, 32-bit BGRA; 15/16-bit output is never written |
| Color map | The first-entry offset is honored; an index outside the stored map is `InvalidImageContentException` | Never written |
| Run-length packets | Packets of 1 to 128 pixels, crossing scan lines accepted; a packet past the last pixel is `InvalidImageContentException` | `TgaCompression.RunLength`: runs of two or more identical neighbours, never crossing a scan line |
| Orientation | Both origin bits are resolved to the displayed image | Origin at the bottom left |
| Alpha | The descriptor's alpha-bit count is authoritative: a 32-bit payload with 0 alpha bits has an unspecified fourth byte, which is discarded | 8 declared alpha bits for pixel formats with alpha |
| Identification field, TGA 2.0 trailer | Skipped / not read | No identification field, a TGA 2.0 footer with zero offsets; at most 65,535 pixels per side, no metadata |

### Netpbm: PBM, PGM, PPM and PAM

`MAXVAL` up to 255 gives 8-bit samples, above it 16-bit, normalized with
`round(value * targetMax / MAXVAL)`. Only the first image of a concatenation is read, and it is never reported as an
animation.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Magic numbers | `P1` to `P6` (plain and binary PBM, PGM, PPM) and `P7` (PAM) | `P5`/`P6` (binary) or `P2`/`P3` (plain, `PnmEncoding.Plain`), and `P7` for pixel formats with alpha; the variant follows the pixel format, not the extension; `P1`/`P4` are never written |
| Header | White space and `#` comments between tokens, exactly one white-space byte before a binary raster, `ENDHDR` for PAM; bounded at 65,536 bytes; illegal values are `InvalidImageContentException` | — |
| Tuple types | `BLACKANDWHITE`, `GRAYSCALE`, `RGB` and their `_ALPHA` forms, inferred from `DEPTH` when `TUPLTYPE` is absent; others are not supported | `RGB_ALPHA` for pixel formats with alpha |
| Samples | 8 or 16 bits (big-endian); a sample above `MAXVAL` is `InvalidImageContentException`; a set PBM bit is black, while a PAM `BLACKANDWHITE` sample is an intensity | `MAXVAL` 255 or 65,535, so the output is always lossless (no bit-depth setting) |
| Plain rasters | Decimal samples with comments allowed; a prefix of a plain raster can be a complete raster with a smaller last sample, so truncation is not detectable there | Lines within 70 characters; PAM has no plain form, so alpha then needs `BackgroundColor` |
| Metadata | Netpbm stores none | Follows `MetadataHandling` |

### TIFF and BigTIFF

Use `ImageCollection` for multi-page documents (see
[Pages and representations](#pages-and-representations)); `Image.Load` reads the first page and `Image.Save` writes one.
`TiffEncoder` writes a conservative subset whose sample layout follows the pixel format losslessly, which is why it has
neither a background color nor a bit-depth switch. **It needs a seekable destination**, because a directory stores the
offsets of the strips it describes.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Container | Classic TIFF and BigTIFF (64-bit offsets), both byte orders, IFD chains with checked offsets, cycle and overlap rejection, page count bounded by `MaxFrames` | Classic or BigTIFF (`TiffEncoder.BigTiff`), little- or big-endian (`BigEndian`) |
| Samples | Unsigned 8 or 16 bits, gray, gray+alpha, RGB or RGBA → `Gray8`/`Gray16`/`Rgb24`/`Rgba32`/`Rgba64` (16-bit RGB is widened to `Rgba64`, losslessly) | Follows the pixel format losslessly: gray formats → gray, `Rgb24` → RGB 8, formats with alpha → RGBA; nothing is quantized and alpha is never dropped |
| Photometric interpretation | `WhiteIsZero`, `BlackIsZero`, `RGB` | `BlackIsZero`, `RGB` |
| Alpha | Only unassociated (straight) extra samples are alpha | Always unassociated |
| Compression | None and Deflate | `TiffCompression.None` or `Deflate` (the default) |
| Predictor | None and horizontal differencing, at 8 and 16 bits | Not written |
| Layout | Strips and tiles, chunky samples only | Strips only (`TiffEncoder.RowsPerStrip`, or about 64 KiB of samples per strip) |
| Metadata | Resolution, orientation (reported, never applied), ICC profile, XMP | The same four, following `MetadataHandling`; EXIF and text entries are not written |
| Memory | One strip or one row of tiles at a time; one page of a large document reads only the bytes its structure points at | One strip at a time, whatever the size of the page or the number of pages |

LZW, PackBits, CCITT and JPEG compression, palette, CMYK, YCbCr and L\*a\*b\* data, planar storage, reversed fill order,
the floating-point predictor, signed or floating-point samples, other sample widths, more than one extra sample and
premultiplied or unspecified extra samples are recognized and rejected with `UnsupportedImageFeatureException`.

### ICO and CUR

Use `ImageCollection` to read and write every representation (see
[Pages and representations](#pages-and-representations)); `Image.Load` reads the largest (then deepest) one.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Directory | Checked payload offsets and lengths, reserved fields validated, count bounded by `MaxFrames` | One entry per representation; payloads are encoded in memory first, so no seeking is needed |
| Geometry | The payload is authoritative (a directory byte cannot express more than 256, and 0 means 256); a contradicting non-zero directory dimension is rejected | At most 256 pixels per side |
| PNG payloads | Decoded by the PNG codec; an animated PNG payload is rejected | A still PNG without metadata (`IconPayloadFormat.Png`, or `Auto` above 64 pixels per side) |
| DIB payloads | The icon rules, not the BMP ones: the stored height is doubled (color rows plus the 1-bit AND mask), bottom-up rows, 1/4/8/16/24/32-bit layouts, palettes, bit-field masks; every DIB-backed representation decodes to `Rgba32` | 32-bit `BI_RGB` with straight alpha and an all-zero AND mask (`IconPayloadFormat.Dib`, or `Auto` up to 64 pixels per side) |
| AND mask and alpha | A 32-bit `BI_RGB` payload stores alpha in its fourth byte, unless that channel is entirely zero, in which case the AND mask decides transparency | The alpha channel carries the transparency |
| Cursor hotspots | Read from a `CUR` entry and validated against its own representation | `IcoEncoder.Kind = Cursor` writes the hotspot of every entry (top-left when it has none); an icon cannot store one and follows `MetadataHandling` |

`.ani` animated cursors are a different container and are not supported.

## Limitations

Everything below is rejected explicitly (never decoded or encoded approximately) or documented as lossy.

| Area | Behavior |
| --- | --- |
| JPEG decoding | Lossless (SOF3), hierarchical (SOF5–7, SOF13–15, DHP, EXP), arithmetic coding (SOF9–11, DAC), JPEG-LS, 12/16-bit precision, heights defined by DNL, CMYK/YCCK (four components) and other component counts than 1 or 3, non-integral sampling ratios: `UnsupportedImageFeatureException` (also from `Identify`). Scaled and region decoding: not available |
| JPEG encoding | Baseline 8-bit Huffman only: no progressive, arithmetic, lossless, 12-bit, RGB, CMYK, restart markers or optimized Huffman tables. One frame: animated images and posters are rejected (export one with `CloneFrame`); alpha requires `BackgroundColor`; `Rgba64`/`Gray16` require `AllowBitDepthReduction` |
| GIF decoding | Plain-text extensions (they require text rendering) are `UnsupportedImageFeatureException`; other unknown extensions (XMP and ICC application extensions included) are skipped; the background color index is never painted (cleared areas are transparent black) |
| GIF encoding | Lossy by design: at most 256 colors per frame (deterministic quantization, optional dithering), one transparent index (alpha thresholded or flattened, never partial); full-canvas frames (no delta rectangles); comments only (no ICC, EXIF, XMP or resolution); at most 65,535 pixels per side, 655.35 s per frame and 65,536 plays |
| WebP decoding | VP8 interframes and frames that are not shown (never produced by WebP encoders): `UnsupportedImageFeatureException`; the `ANIM` background color is a hint and is never painted (disposed areas are transparent black); metadata of simple-layout files and text/resolution do not exist in WebP |
| WebP encoding | 8-bit RGB(A) only (gray written as RGB, 16-bit after explicit reduction); lossy output is 4:2:0 and not pixel-exact; animations are full-canvas frames (no delta rectangles or blending) and need a seekable destination (no spooling to non-seekable streams); at most 16,383 (lossy) or 16,384 (lossless) pixels per side, 16,777,215 ms per frame, 65,535 plays and 4 GiB per file |
| QOI | 8-bit RGB(A) only (gray written as RGB, 16-bit after explicit reduction); one still image; no metadata; the colorspace field is a label (no linear-to-sRGB conversion exists) |
| BMP | Decoding: no RLE4/RLE8, no embedded JPEG/PNG payloads, no OS/2 headers, no 2-bit depth, no channel wider than 8 bits, no embedded ICC profile. Encoding: uncompressed 24-bit or 32-bit, bottom-up, one still image; no indexed, 16-bit or top-down output; resolution is the only metadata; the file must fit the 32-bit `bfSize` field |
| TGA | Decoding: no 16-bit color-map indexes or 16-bit grayscale; the developer area, extension area and footer are not read (a forward-only decoder cannot follow offsets stored at the end of the file), so the attributes type never changes the decoded representation and trailing bytes are left unread; truncation is only detected before the end of the image data. Encoding: true-color and grayscale only, bottom-up, one still image, at most 65,535 pixels per side; no color map, no 15/16-bit output, no metadata |
| Netpbm | Decoding: only the first image of a concatenation is read; tuple types other than the standard grayscale, RGB and alpha forms are rejected; the header is bounded at 65,536 bytes; truncation of a plain raster is not detectable. Encoding: `P2`/`P3`/`P5`/`P6`/`P7` only (the variant follows the pixel format, not the extension), one image, no metadata; plain output has no alpha (PAM has no plain form) |
| TIFF | Decoding: LZW, PackBits, CCITT fax and JPEG compression, palette, CMYK, YCbCr and L\*a\*b\* photometric interpretations, planar storage, reversed fill order, the floating-point predictor, signed and floating-point samples, sample widths other than 8 and 16 bits, more than one extra sample, and associated (premultiplied) or unspecified extra samples are all rejected with `UnsupportedImageFeatureException`. No EXIF block, no sub-IFD, no unknown-tag round trip. Encoding: one page per `Image.Save` (use `ImageCollection.Save`), strips only, no predictor, no tiles, a seekable destination required |
| ICO/CUR | Decoding: a top-down DIB payload, a DIB whose stored height is not doubled, an animated PNG payload, a representation larger than 256 pixels per side and the DIB variants the BMP decoder rejects (RLE, embedded codecs, OS/2 headers) are rejected. `.ani` animated cursors are a different container and are not supported. Encoding: 32-bit DIB or still PNG payloads only, at most 256 pixels per side, no metadata; an icon cannot store a hotspot |
| PNG/APNG | No palette output (paletted inputs are re-encoded as RGB/RGBA), no RGB 16-bit or gray+alpha output layouts (16-bit color is written as RGBA 16), no APNG delta-rectangle optimization, unknown critical chunks rejected, unknown ancillary chunks not round-tripped |
| Metadata | Profiles are preserved and labeled, never applied (no color management); EXIF orientation is reported, applied only by `AutoOrient`; extended XMP in JPEG is not supported; per-format storage limits are listed in [Metadata, color profiles and orientation](#metadata-color-profiles-and-orientation); unsupported items follow `MetadataHandling` (`Strict` throws by default) |
| Processing | No ICC color conversion, HDR, drawing, text rendering or custom processors; `LinearSrgb` resizing and convolution accept untagged or recognized sRGB/sGray profiles only; convolution matrices have odd dimensions, no bias or divisor, no ready-made matrices and no separable fast path |
| Formats | JPEG XL and AVIF are not supported |
| API | No public allocator, custom pixel type, codec plug-in or image-processor interface; implementation types are internal |
