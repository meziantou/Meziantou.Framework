# Meziantou.Framework.Imaging

A fully managed, performance-oriented image library for .NET 10 and .NET 11: PNG, animated PNG (APNG), GIF, JPEG, WebP
(still and animated, lossless and lossy), QOI, BMP, TGA, Netpbm (PBM/PGM/PPM/PAM), TIFF/BigTIFF, ICO/CUR and animated
cursor (ANI) and AVIF (still and animated, lossless and lossy) decoding and encoding, JPEG XL decoding and lossless
encoding, animation-aware processing, vector drawing, ICC color conversion, and bounded-memory streaming.

- Static PNG, animated PNG (APNG), GIF, baseline/progressive JPEG, WebP (still and animated, lossless and lossy), QOI,
  BMP, TGA, Netpbm (PBM/PGM/PPM/PAM), TIFF/BigTIFF, ICO/CUR and ANI decoding; PNG, APNG, GIF, baseline JPEG, WebP
  (lossless and lossy, still and animated), QOI, BMP, TGA, Netpbm, TIFF/BigTIFF, ICO/CUR and ANI encoding (unsupported
  modes, such as arithmetic JPEG, embedded JPEG or PNG in BMP, premultiplied-alpha TGA or LZW TIFF, are rejected
  explicitly; a CMYK JPEG is loaded with an explicit color conversion)
- JPEG XL decoding with a fully managed decoder: lossy (VarDCT) and lossless (Modular) files, alpha, animations and
  layers, losslessly recompressed JPEG files; lossless JPEG XL encoding of every pixel format, animations included
  (there is no lossy JPEG XL encoder)
- AVIF decoding and encoding with a fully managed AV1 decoder and a fully managed AV1 encoder: still images and
  animations, lossless and lossy, 8, 10 and 12 bits, alpha; grids and film grain are decoded
- `ImageCollection` for the images that are not animation frames: TIFF document pages and icon/cursor representations,
  read and written one entry at a time
- Animation-aware model: every frame is a full-canvas displayed image with exact rational timing and, for cursors, a
  hotspot that follows its pixel through geometry operations
- Six working pixel formats (`Rgba32`, `Bgra32`, `Rgb24`, `Rgba64`, `Gray8`, `Gray16`) with 16-bit precision preserved
- Crop, auto-crop (background detection), resize (alpha-aware, Contain/Cover/Stretch), rotate, auto-orient, flip,
  grayscale and convolution matrices (sharpen, blur, edge detection) applied to all frames
- Antialiased vector drawing on a frame or on every frame: filled and stroked rectangles, ellipses and paths with
  Bézier curves, solid colors, linear and radial gradients, with exact pixel coverage
- Text drawn from font files with the same rasterizer, measured by the layout that is drawn
- Font files read without any system library: TrueType and OpenType (TrueType and CFF outlines), font collections,
  WOFF and WOFF2, with their names, metrics, character map and glyph outlines
- ICC color management on request: convert an image to another color profile, or convert sample buffers between two
  profiles: matrix- and table-based grayscale, RGB and CMYK profiles of versions 2 and 4, the four rendering intents
  and black point compensation (profiles are never applied implicitly)
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
  - [Drawing](#drawing)
  - [Fonts](#fonts)
  - [Text](#text)
  - [Metadata, color profiles and orientation](#metadata-color-profiles-and-orientation)
  - [Color management](#color-management)
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
  - [AVIF](#avif)
  - [ANI](#ani)
  - [JPEG XL](#jpeg-xl)
- [Limitations](#limitations)

## Quick start

Namespaces: `Meziantou.Framework.Imaging` (images, I/O, processing, configuration), `Meziantou.Framework.Imaging.Formats`
(encoders and their settings), `Meziantou.Framework.Imaging.Metadata`, `Meziantou.Framework.Imaging.Drawing` (brushes,
pens and paths).

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
- **Exact timing.** Rational `FrameDuration` preserves GIF, APNG, WebP and ANI delays exactly; `TotalPlays` counts total plays.
- **Atomic geometry.** Crop, auto-crop, resize, rotate and auto-orient apply to every frame (and the poster)
  transactionally.
- **Exact drawing.** Shapes are antialiased with the exact area of each pixel they cover, overlaps included, and
  composited with exact integer arithmetic: the same pixels on every platform.
- **Bounded streaming.** Sequential readers and writers process long animations with memory bounded by one frame.
- **Safety.** Configurable resource limits, distinct exceptions for malformed data, unsupported features, limits,
  cancellation and I/O failures, and atomic file publication.

## Platforms

| Item | Support |
| --- | --- |
| `net10.0`, `net11.0` | Supported targets, built and tested on both runtimes |
| x64, arm64 (Windows, Linux, macOS) | Tested on Linux and Windows x64, macOS arm64, and Linux and Windows arm64 where hosted runners are available |
| Older .NET, .NET Standard, .NET Framework | Not supported (no assets) |
| Dependencies | One package, `Meziantou.Framework.Unicode` (the Unicode algorithms of text layout, fully managed, no ICU); no native dependency |
| Trimming / NativeAOT | Supported: the library is marked trimmable and AOT-compatible (analyzers on every build), and a NativeAOT smoke app (every format, streaming, processing, errors; trim/AOT warnings as errors) is published and run for both frameworks |

## Supported formats

Detection is content-based, never by extension. "Not supported" features are recognized and rejected explicitly, never
decoded or encoded approximately; see [Format reference](#format-reference) and [Limitations](#limitations).

| Format | Decoding | Encoding | `Save(path)` extensions |
| --- | --- | --- | --- |
| PNG / APNG | Every color type and bit depth, palettes, Adam7, APNG animations and poster frames | Gray, RGB and RGBA at 8/16 bits, Adam7, APNG with full-canvas frames and posters | `.png` (APNG when animated), `.apng` |
| GIF | GIF87a/89a, animations, interlacing, transparency, comments | GIF89a, deterministic quantization per frame, optional dithering, animations with unknown frame counts | `.gif` |
| JPEG | Baseline, extended sequential and progressive Huffman, 8-bit; gray, YCbCr, RGB, and CMYK/YCCK with a color conversion; any integral sampling | Baseline 8-bit Huffman, gray or YCbCr, 4:4:4/4:2:2/4:2:0 | `.jpg`, `.jpeg` |
| WebP | Lossless, lossy, alpha, animations, ICC/EXIF/XMP | Lossless and lossy, alpha, animations (seekable destination) | `.webp` (lossless by default) |
| QOI | Every chunk form, 3 or 4 channels, colorspace label | Lossless 8-bit RGB/RGBA | `.qoi` |
| BMP | Windows headers up to V5 and OS/2 1.x/2.x headers, 1/4/8/16/24/32-bit, bit fields, RLE4/RLE8/RLE24 | 24-bit or 32-bit with an alpha mask by default; 1/4/8-bit indexed, RLE4/RLE8, 16-bit and top-down on request | `.bmp`, `.dib` |
| TGA | True-color, grayscale and color-mapped, uncompressed or run-length | True-color and grayscale, uncompressed or run-length | `.tga`, `.icb`, `.vda`, `.vst` |
| Netpbm | `P1` to `P7` (PBM, PGM, PPM, PAM), 8 and 16 bits | `P1` to `P7`, lossless (PBM on request, from black and white pixels) | `.pnm`, `.pam`, `.ppm`, `.pgm`, `.pbm` |
| TIFF / BigTIFF | Both byte orders and offset sizes, 8/16-bit gray, gray+alpha, RGB, RGBA, strips and tiles, Deflate, predictor; multi-page documents | Strips, uncompressed or Deflate, classic or BigTIFF; multi-page documents (seekable destination) | `.tif`, `.tiff` |
| ICO / CUR | PNG and DIB representations, cursor hotspots | PNG or 32-bit DIB representations, cursor hotspots | `.ico`, `.cur` |
| AVIF | Still images and image sequences (animations); 8, 10 and 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome; alpha; grids; film grain; ICC/EXIF/XMP and CICP color descriptions | Lossless and lossy still images and image sequences (seekable destination); 8, 10 and 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome; lossless alpha; ICC/EXIF/XMP, CICP color descriptions and orientation | `.avif`, `.avifs` (lossless by default) |
| JPEG XL | Bare codestreams and containers; lossy (VarDCT) and lossless (Modular) frames; up to 16 bits; alpha; animations and layers; patches, splines, noise; recompressed JPEG files; ICC/EXIF/XMP and enumerated color encodings | Lossless, every pixel format at its own depth, alpha, animations with any frame count, ICC/EXIF/XMP, orientation | `.jxl` |
| ANI | Animated cursors: icon and cursor frames (PNG and DIB), rate and sequence tables, hotspots, title and author | One cursor image per frame (PNG or 32-bit DIB), hotspots, identical frames stored once, title and author (seekable destination) | `.ani` |

## Usage guide

### Images, frames and animations

**Full displayed frames, not encoded deltas**. An `Image` holds one or more frames; every frame is
the complete canvas a viewer shows at that point of the animation, with the canvas size and the image's pixel type.
Encoded delta rectangles, offsets, APNG blend/dispose operations and GIF disposal methods are resolved by the decoders (one
compositor shared by eager and sequential decoding) and recomputed by the encoders from the displayed frames. They are not
public state: editing, removing or reordering frames can never leave stale encoded instructions behind.

- `Frames[i].Metadata.Duration` is the frame's exact `FrameDuration`; `Image.Animation` (`TotalPlays`) holds the
  animation-wide settings. Animation settings are structural data, never removed by metadata policies.
- `Frames[i].Metadata.Hotspot` is the cursor hotspot of the frame: the pixel that designates the pointer position, or
  `null` for a frame that is not a cursor image. It is read from CUR and ANI files and written back to them, it is always
  inside its frame (setting it elsewhere throws `ArgumentOutOfRangeException`), and geometry operations move it with its
  pixel (see [Processing](#processing)). Outputs that cannot store it reject it by default (see
  [Metadata, color profiles and orientation](#metadata-color-profiles-and-orientation)).
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

**Exact timing**. `FrameDuration` is a normalized rational number of seconds: GIF delays (`d/100`),
APNG delays (`num/den`, `den = 0` meaning `/100`) and ANI rates (`j/60`, jiffies) are kept exactly, zero durations
included; no player minimum-delay heuristic is applied. `TotalPlays` counts the total number of plays including the first (`null` = infinite): a GIF
NETSCAPE2.0 loop count `L` stores repetitions, so it maps to `L + 1` plays (`L = 0` is infinite, no loop extension is one
play); APNG `num_plays` maps directly (`0` is infinite); an animated cursor has no play count and always loops, so it
decodes to `null` and `AniEncoder` rejects any other value. On export, `FrameDurationRounding` decides what happens when a
duration is not representable: `RequireExact` throws, `RoundToNearest` picks the nearest representable value (APNG
default: `RequireExact`; GIF default: `RoundToNearest`, to the nearest hundredth with ties up; ANI default:
`RoundToNearest`, to the nearest sixtieth with ties up). Values beyond the format's
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

**Animated cursors**. An ANI file is an animation whose frames are cursor images: each frame has
its own hotspot, durations are whole sixtieths of a second, and the animation always loops.

<!-- snippet: animated-cursor -->
```csharp
public static Point WriteAndReadAnimatedCursor(string animationPath, string cursorPath)
{
    using var animation = Image.Load(animationPath);
    animation.Resize(new ResizeOptions(32, 32) { Mode = ResizeMode.Stretch });
    foreach (var frame in animation.Frames)
    {
        frame.Metadata.Hotspot = new Point(4, 2); // the pixel that designates the pointer position
    }

    // An animated cursor always loops: a finite play count is rejected instead of being dropped
    animation.Animation = new AnimationMetadata();
    animation.Save(cursorPath); // ".ani" selects AniEncoder; durations are rounded to sixtieths of a second

    // A hotspot follows its pixel through geometry operations: (4, 2) at 32x32 is (8, 4) at 64x64
    using var cursor = Image.Load(cursorPath);
    cursor.Resize(new ResizeOptions(64, 64) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
    return cursor.Frames[0].Metadata.Hotspot!.Value;
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
  allocation scope, and returns a normal independently owned `Image`. A cursor representation carries its hotspot on
  the frame of that image.
- A loaded collection keeps its input open until it is disposed: a path stays open, a stream must be seekable and is
  seeked freely (its position afterward is unspecified), and a span is copied once. Loading is synchronous by design (a
  random-access decoder issues many small dependent reads); `Image.LoadAsync` and `Image.IdentifyAsync` do work on these
  formats, by buffering the input under `MaxEncodedBytes`.
- Entries are borrowed from their collection and are valid until it is disposed or they are removed. `Add` and `Insert`
  copy the image, so the caller keeps and disposes its own (without an explicit hotspot, the entry takes the hotspot of
  the image's frame); `RemoveAt` and `Move` edit the order, and removed owned
  entries are released.
- `Image.Load` and `Image.Identify` use the first page of a document, or the largest (then deepest) representation of an
  icon, and report the number of entries in `ImageInfo.CollectionEntryCount`. `ImageCollection.SelectBySize` chooses a
  representation explicitly: the smallest one at least as large as the request, else the largest (ties by bit depth, then
  container order). Nothing is resized.
- `ImageCollection.Save` writes pages with `TiffEncoder` and representations with `IcoEncoder`; entries are materialized
  one at a time, and path destinations are published atomically.
- An icon or cursor representation stores at most 256 pixels per side. A cursor hotspot
  (`ImageCollectionEntry.Hotspot`) is metadata validated against its own representation, never a pixel offset.
  `Image.Load` of a cursor exposes the hotspot of the representation it decodes as `Frames[0].Metadata.Hotspot`, and
  `Image.Save` with `IcoEncoder { Kind = IconKind.Cursor }` writes it.

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
| `MetadataBlob`, profiles, options, encoders, brushes, pens, paths | Immutable (blobs copy their input); safe to share. |

**Leases**. Pixels are accessed through exclusive scoped leases: `ProcessPixelRows` (typed rows),
`ProcessPixelBytes` (raw native-endian bytes), the scalar indexer, `CopyPixelDataTo`/`CopyPixelBytesTo` and `Draw` (one
frame, or every frame of the image). Row spans
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
types (configuration, limits, options, encoders, `MetadataBlob`, profiles, brushes, pens, paths). Distinct images can be
processed concurrently.
An `Image` and its frames must not be used concurrently while any thread mutates them or holds a lease. Readers and
writers support one operation at a time: overlapping calls throw `InvalidOperationException`. Internal parallelism is off
by default (`ImageConfiguration.MaxDegreeOfParallelism = 1`); with more workers, only large resizes and convolutions are
split into row bands, with results identical to the sequential ones.

### Failure atomicity



- **Geometry is atomic.** `Crop`, `AutoCrop`, `Resize`, `Rotate` and `AutoOrient` apply to every frame and the poster in
  one transaction: replacements are allocated (and charged) while the originals are live, and any failure before the commit
  (limit, allocation failure, cancellation) leaves dimensions, pixels, frame identities and order, cursor hotspots and
  metadata unchanged.
- **Pixel mutations may be partial.** `Flip`, `Grayscale`, `Convolve`, `Draw`, row callbacks and `ReadFrameInto` write in
  place: a failure or a cancellation can leave some rows or frames updated, but the image stays structurally valid and
  disposable. `Convolve` rents its scratch rows first: exceeding the allocation limit leaves the image unchanged. `Flip`
  moves the hotspot of a frame once that frame is completely mirrored. In a `Draw` callback, the shapes already drawn
  stay; each shape rents its working storage before it changes a pixel, and a canceled shape leaves the same rows
  painted on every frame.
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
| Alpha, when encoding BMP in any layout but 32-bit, plain Netpbm or a portable bitmap | `BmpEncoder.BackgroundColor`, `PnmEncoder.BackgroundColor` |
| Gray levels and colors, when encoding a portable bitmap (`PnmPixelLayout.Bitmap`, `.pbm`) | None: pixels that are not exactly black or white are rejected, never thresholded or dithered |
| Partial alpha, when encoding GIF (GIF has one transparent index) | `GifEncoder.AlphaMode`: `Threshold` (default, `AlphaThreshold` 128) or `Flatten` with `BackgroundColor` |
| 16-bit precision, when encoding JPEG, WebP, QOI, BMP or TGA | `AllowBitDepthReduction` on the encoder |
| 16-bit precision, when encoding AVIF (at most 12 bits) | `AvifEncoder.BitDepth` (10 or 12), or `AllowBitDepthReduction` (12 bits) |
| 8-bit precision, when encoding 16-bit BMP (`Bgr565`, `Bgr555`: 5 or 6 bits per channel) | `BmpEncoder.AllowBitDepthReduction` |
| 16-bit precision, when loading or converting | Request the narrower type explicitly (`Load<Rgba32>`, `CloneAs<Gray8>`) |
| Colors beyond 256 per frame, when encoding GIF | Lossy by design: deterministic quantization (`MaxColors`), optional `Dithering` |
| Colors beyond the palette, when encoding indexed BMP (2, 16 or 256 entries, or `MaxColors`) | `BmpEncoder.AllowColorReduction` (the same deterministic quantization, optional `Dithering`); an image whose colors fit is always stored exactly |
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
orientation and thumbnail. `ConvertColorProfile` converts the colors to another ICC profile
(see [Color management](#color-management)).

**Cursor hotspots**. A hotspot (`Frames[i].Metadata.Hotspot`) follows the pixel it designates:
`Crop` and `AutoCrop` translate it, `Rotate`, `Flip` and `AutoOrient` permute it exactly, and `Resize` moves it to
`floor(x * newWidth / width)` (and likewise vertically, through the kept region for `Cover`), which is how Windows
scales a cursor: a hotspot in the top-left corner stays there. When that pixel is not part of the result (a crop, an auto-crop or
a `Cover` resize that removes it), the operation throws `UnsupportedImageFeatureException` and leaves the image unchanged;
set the hotspot to `null` or to a pixel that is kept first. Pixel-only operations keep it.

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
  ICC transform is applied in either space: convert such an image first with `ConvertColorProfile(IccProfile.Srgb)`.

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

There is no HDR processing or custom processor.

### Drawing

`ImageFrame.Draw` hands a `DrawingCanvas` to a callback, for one frame; `Image.Draw` does the same for a whole image and
paints every frame and the poster identically. The callback runs once, while the pixels are leased: the canvas is only
valid inside it, and other pixel access or geometry changes on the same image throw `InvalidOperationException` until it
returns. The drawing types are in the `Meziantou.Framework.Imaging.Drawing` namespace.

<!-- snippet: draw-shapes -->
```csharp
public static void DrawLandscape(string outputPath)
{
    using var image = new Image<Rgba32>(256, 160);

    // Brushes, pens and paths are immutable: create them once and reuse them
    var sky = new LinearGradientBrush(new PointD(0, 0), new PointD(0, 160), [new GradientStop(0, new Rgba32(30, 60, 160)), new GradientStop(1, new Rgba32(200, 220, 255))]);
    var sun = new RadialGradientBrush(new PointD(200, 50), 30, [new GradientStop(0, new Rgba32(255, 255, 200)), new GradientStop(1, new Rgba32(255, 180, 0, 0))]);
    var hill = new VectorPathBuilder()
        .MoveTo(0, 160).LineTo(0, 120)
        .CubicBezierTo(60, 70, 120, 150, 256, 100)
        .LineTo(256, 160).Close()
        .Build();
    var outline = new Pen(new Rgba32(20, 60, 20), 3) { LineJoin = LineJoin.Round };
    var frame = new Pen(new Rgba32(0, 0, 0, 128), 2);

    image.Frames[0].Draw(canvas =>
    {
        canvas.Fill(sky); // the whole canvas
        canvas.FillCircle(sun, new PointD(200, 50), 30); // fades to transparent at its edge
        canvas.Fill(new Rgba32(60, 140, 60), hill); // a color converts to a solid brush
        canvas.Stroke(outline, hill);
        canvas.StrokeRectangle(frame, new RectangleD(1, 1, 254, 158)); // centered on its path: from 0 to 2
    });

    image.Save(outputPath);
}
```

**Shapes**. A canvas fills (`Fill`, `FillRectangle`, `FillEllipse`, `FillCircle`) and strokes (`Stroke`,
`StrokeRectangle`, `StrokeEllipse`, `StrokeCircle`, `StrokeLine`, `StrokeBezier`). Anything else is a `VectorPath`, built
once with a `VectorPathBuilder` (`MoveTo`, `LineTo`, `QuadraticBezierTo`, `CubicBezierTo`, `Close`, `AddRectangle`,
`AddEllipse`, `AddCircle`, `AddPolygon`) and drawn as many times as needed. A path holds any number of contours; an open
contour is closed by a straight segment when it is filled, and gets the caps of the pen when it is stroked. `FillRule`
decides what is inside when contours overlap: `NonZero` (default) fills everything the contours wind around, `EvenOdd`
makes a hole wherever an even number of them overlap.

**Coordinates**. Positions are `double` values in pixels (`PointD`, `RectangleD`; the integer `Point` and `Rectangle`
convert implicitly). The origin is the top-left corner, and integer coordinates are pixel boundaries: the pixel
`(x, y)` covers the square from `(x, y)` to `(x + 1, y + 1)`. A rectangle on integer coordinates is filled exactly, and
a one-pixel horizontal line is crisp when it runs through the middle of a row (`y + 0.5`). Shapes are clipped to the
canvas. A coordinate must be finite (`ArgumentException`) and no larger than 16,777,216 in magnitude
(`ArgumentOutOfRangeException`).

**Brushes**. A `SolidBrush` paints one color (an `Rgba32` or `Rgba64` converts implicitly to a brush). A
`LinearGradientBrush` (between two points) and a `RadialGradientBrush` (around a center, up to a radius) paint the color
of their `GradientStop`s at the center of each pixel; `SpreadMode` gives the colors beyond the stops: `Pad` (default),
`Repeat` or `Reflect`. Gradients are positioned in canvas coordinates, whatever the shape they fill. Two stops with the
same offset make a hard edge, and stops are interpolated on alpha-premultiplied values, so a transparent stop does not
tint its neighbors.

**Pens**. A `Pen` is a brush and a width, centered on the path, with a `LineJoin` for the corners (`Miter` by default,
beveled beyond `MiterLimit`; `Round`; `Bevel`) and a `LineCap` for the ends of open contours (`Butt` by default; `Round`;
`Square`). The parts of a stroke that overlap are painted once, also with a translucent paint.

<!-- snippet: draw-on-animation -->
```csharp
public static void MarkAnimation(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    var marker = new Pen(new Rgba32(255, 255, 0, 200), 4) { LineCap = LineCap.Round };

    // Image.Draw paints every frame and the poster identically; the callback runs once
    image.Draw(marker, static (canvas, pen) =>
    {
        // Coordinates are in pixels, with sub-pixel precision; shapes are clipped to the canvas
        var center = new PointD(canvas.Width / 2.0, canvas.Height / 2.0);
        var radius = Math.Min(canvas.Width, canvas.Height) / 4.0;
        canvas.StrokeCircle(pen, center, radius);
        canvas.StrokeLine(pen, new PointD(center.X - radius, center.Y), new PointD(center.X + radius, center.Y));
    });

    image.Save(outputPath);
}
```

**Rendering**. The result is defined exactly and does not depend on the platform:

- **Coverage.** Each pixel is painted in proportion to the area of it that is inside the shape, rounded to the nearest
  1/65536 (ties upward). The area is exact, including where the contours of a shape overlap or intersect themselves: two
  contours over the same half of a pixel cover half of it, not all of it. A pixel that is not covered is left
  untouched, bit for bit.
- **Curves.** Curves, ellipses, round joins and round caps are replaced by chords that stay within 1/16 of a pixel of
  them. The vertices of the chords are not part of the contract.
- **Colors.** Brush colors are sample values in the color space of the image: no color profile is applied. A color is
  first converted to the pixel format of the frame with the rules of `CloneAs` (Rec. 709 luma on grayscale frames,
  rounding to 8 bits on 8-bit frames); its alpha stays the opacity of the paint, also on frames without alpha.
- **Compositing.** The paint is composited over the pixels (source-over) on the stored sample values, with straight
  alpha and exact integer arithmetic, rounded to nearest with ties upward. A full coverage with an opaque color writes
  that color exactly; frames without alpha stay opaque; a pixel that would stay fully transparent is not written and
  keeps its hidden color. A gradient whose stops have the same color paints exactly what a solid brush paints.
- **Metadata.** The first shape that changes a pixel removes the EXIF thumbnail, which no longer matches. Cursor
  hotspots and everything else are kept.

**Cost**. Drawing is sequential. A shape rents working storage from the allocation scope of the image (proportional to
the width and height of the canvas, and to the number of edges of the shape), and each `Draw` call allocates a few small
objects; the shapes themselves allocate nothing once that storage has grown to the largest of them, so reuse brushes,
pens and paths and use the overloads with a state and a static callback. The time to draw a shape grows with the number
of its edges and with the number of points where they cross each other, which the exact area depends on. One family of
shapes has millions of crossings: a pen far wider than the bends of a long curve it follows. Pass a `CancellationToken`
when shapes come from untrusted input; it is checked by every shape, every 64 rows and every few thousand crossings.

### Fonts

`Font` and `FontCollection` (namespace `Meziantou.Framework.Imaging.Typography`) read font files. Nothing comes from the
operating system: no installed font is used, and a font is always a file, a stream or bytes given by the caller.

<!-- snippet: load-font -->
```csharp
public static string DescribeFont(string path)
{
    // TrueType, OpenType, WOFF and WOFF2 are detected from the content of the file.
    var font = Font.Load(path);
    var metrics = font.GetMetrics(size: 16);
    var hasEuro = font.ContainsGlyph(new Rune('€'));
    return string.Create(
        CultureInfo.InvariantCulture,
        $"{font.FamilyName} {font.SubfamilyName}: {font.GlyphCount} glyphs, ascent {metrics.Ascent:0.##}, descent {metrics.Descent:0.##}, euro sign: {hasEuro}");
}
```

| | Supported |
| --- | --- |
| Containers | TrueType and OpenType files (`.ttf`, `.otf`), font collections (`.ttc`, `.otc`), WOFF 1.0 and WOFF 2.0 (with the transformed glyph, location and horizontal metrics tables, and collections); detected from the content, never from the extension |
| Outlines | TrueType (`glyf`: simple and composite glyphs, scaled, rotated and placed by matching points), CFF version 1 (`CFF `: Type 2 charstrings, subroutines, CID-keyed fonts) and CFF version 2 (`CFF2`) |
| Variable fonts | The axes and the named instances (`fvar`), the mapping of the axes (`avar` version 1), and what an instance varies: TrueType outlines (`gvar`), CFF2 outlines (the `blend` operator), advances (`HVAR`, or the phantom points of the glyphs), metrics (`MVAR`), the positions and anchors of `GPOS` (the variation store of `GDEF`) and the features a font replaces in a part of its variation space (feature variations, `rvrn`) |
| Color glyphs | The layers of version 0 of the `COLR` table and the paints of its version 1 (solid colors, linear, radial and sweep gradients, transformations, clips, clip boxes, the 28 composition modes, and their variations in a variable font), with the palettes of the `CPAL` table; glyphs that are PNG images, in the `sbix` table and in the `CBLC` and `CBDT` tables (index subtables of formats 1 to 5, images of formats 17, 18 and 19) |
| Character maps | Unicode subtables of formats 0, 4, 6, 12 and 13, variation sequences (format 14), and the Macintosh Roman subtable for ASCII when a font has no Unicode one |
| Names | The typographic family and subfamily when the font has them, the legacy ones otherwise; Windows and Unicode platform names, then Macintosh Roman |
| Metrics | In font units (`Font.Metrics`) or scaled to a size in pixels (`Font.GetMetrics`): ascent, descent, line gap, cap height, x-height, underline and strikethrough. The ascent, descent and line gap come from the table the font designates (`hhea`, or the typographic values of `OS/2` when the font sets `USE_TYPO_METRICS` or has an empty `hhea`) |

A variable font is one file for a whole family: its axes (`Font.VariationAxes`) say what varies, such as the weight or
the width, and `Font.WithVariations` gives the font of an instance.

<!-- snippet: variable-font -->
```csharp
public static void DrawWeights(string variableFontPath, string outputPath)
{
    var font = Font.Load(variableFontPath);

    // The axes say what can vary, and in what range
    var weight = font.VariationAxes.First(axis => axis.Tag == "wght");

    using var image = new Image<Rgba32>(220, 200, new Rgba32(255, 255, 255));
    image.Draw(canvas =>
    {
        for (var i = 0; i < 5; i++)
        {
            // An instance is a font: it shares the data of the font it comes from
            var value = weight.Minimum + ((weight.Maximum - weight.Minimum) * i / 4);
            var instance = font.WithVariations(new FontVariationSetting("wght", value));
            canvas.FillText(new Rgba32(0, 0, 0), "Variable", instance, size: 32, new PointD(10, i * 38));
        }
    });

    image.Save(outputPath);
}
```

- **Instances.** A font is loaded at its default instance. `WithVariations` takes the value of some axes, by their tag
  (`wght`, `wdth`, `slnt`, `ital`, `opsz`, or a tag of the font); an axis that is not named is at its default, a value
  outside the range of its axis is brought to its nearest end, and a tag the font does not have is ignored.
  `NamedInstances` lists the instances the font names ("Light", "Bold Condensed"), which `WithVariations` also
  accepts, and `Variations` gives the values of a font. An instance is a `Font`: it shares the bytes of the font it
  comes from, is as immutable, and can be used wherever a font is; `Weight` is the value of its weight axis. A font
  that does not vary has no axis and is its only instance.
- **What an instance is.** The values are normalized and mapped as the OpenType specification describes, and the
  glyphs are varied when they are used: their points are not rounded to font units, so an instance is as smooth as
  the default one. The advances, the metrics and the positions of the layout tables of an instance are whole font
  units. An instance costs a few small arrays when it is created (no glyph is read), and its glyphs cost more to
  decode than those of the default instance; that work counts in `MaxGlyphComplexity`.
- **Color glyphs.** `Font.HasColorGlyphs` says that a font has glyphs with their own colors, such as emoji, and
  `Font.ColorPalettes` gives its palettes. `FillText` draws these glyphs with their colors: each one is drawn on a
  surface of its own, layer after layer, then composited over the image like any other paint. The edges of its shapes
  have the exact coverage of every shape of the drawing API, gradients are evaluated at the center of each pixel, and
  no function of the platform is involved (the angles of sweep gradients and of rotations are computed by the
  library), so the pixels are the same everywhere. The colors of a palette are drawn as the colors of a brush are:
  as sample values, without color management.
  - The brush of the text is the color of the parts of a glyph the font leaves to the text ("foreground"); it can be a
    gradient. `TextStyle.ColorPaletteIndex` selects another palette of the font.
  - A color glyph is drawn after the glyphs that are not, in the order of the text. `StrokeText` and
    `TextLayout.ToPath` use the outline the font gives to the glyph itself, which can be empty.
  - A glyph the font gives as an image (`sbix`, `CBDT`) is taken from the smallest strike that is at least as large
    as the size of the text, or from the largest one. Its image is decoded when the layout is created, with the PNG
    decoder and the limits of the library, and scaled down to the size of the text with the default filter of
    `Resize`; an image smaller than the text is enlarged when it is drawn, by bilinear interpolation. At the size of
    its strike, at a whole position, the pixels of the image are the pixels drawn. A glyph that has both paints and
    an image is drawn with its paints; a glyph without image in the strike is drawn with its outline.
  - A graph of paints that refers to itself is cut where it would never end. A gradient the font defines without
    length, or whose color line repeats over a length of zero, is not drawn, as the specification says.
  - The surface of a glyph is rented from the allocation scope of the image (16 bytes per pixel of the box of the
    glyph, per layer it composes): a glyph that needs more than the image allows fails with
    `ImageResourceLimitException` before any pixel of the text is drawn.
- **A glyph is drawn from its origin.** The outline of a TrueType glyph is placed by its left side bearing, as a
  TrueType rasterizer does: a font whose side bearings are not the left of its outlines is drawn where its metrics
  say.
- **A font is immutable and thread-safe**, and is not disposable: it keeps the bytes of its file (the expanded font for
  WOFF and WOFF2) in one array for its lifetime and has no cache. The fonts of a collection share that array.
  `Font.Load` on a collection gives its first font; `FontCollection.Load` gives them all, and a file that holds one font
  is a collection of one.
- **Loading validates what every use needs**: the table directory, the required tables (`head`, `hhea`, `maxp`,
  `hmtx`, `cmap`, and `loca` with `glyf`), the structure of the character map and of the CFF table. The data of a glyph
  is validated when the glyph is used.
- **A font is untrusted input.** The bytes read are bounded by `MaxEncodedBytes`, the bytes a font keeps by
  `MaxFontBytes` (256 MiB by default; checked before a WOFF or WOFF2 file is expanded), and the work to produce one
  glyph by `MaxGlyphComplexity` (1,000,000 points, components and charstring operators by default): glyphs made of
  glyphs, or subroutines that call subroutines, can describe an outline that is exponentially larger than the file.
  The limits are those of `FontLoadOptions.Configuration`.
- **Errors** are the exceptions of the library with `ImageFormat.Unknown`: `UnknownImageFormatException` when the data
  is not a font, `InvalidImageContentException` for a malformed font, `UnsupportedImageFeatureException` for the
  features below, `ImageResourceLimitException` for a limit.
- **Not supported**: version 2 of the `avar` table (its version 1 part is applied), the variation of the vertical
  metrics and of the hinting values (`VVAR`, `cvar`), glyphs in SVG (`SVG `: their outlines are drawn), the images of an `sbix` table that are not PNG files (JPEG, TIFF)
  and the bitmaps without colors of the `EBDT` table (their outlines are drawn too), PostScript Type 1 fonts, Type 1
  charstrings and the deprecated `seac` form of `endchar` in a CFF table, the font matrix of a CFF table (the units of
  the `head` table are used), and hinting: instructions and hints are skipped. A WOFF or WOFF2 file is expanded in
  memory; its metadata and private blocks are not read. Table checksums are not verified.

### Text

A `TextLayout` is a text laid out: its lines, its glyphs and their positions. It measures the text (`Width`,
`Height`, `Baseline`, `Lines`, `InkBounds`) and is what `DrawingCanvas.FillText` and `StrokeText` draw, so that what is
measured is what is drawn. The text types are in the `Meziantou.Framework.Imaging.Typography` namespace.

<!-- snippet: draw-text -->
```csharp
public static void DrawLabel(string fontPath, string outputPath)
{
    var font = Font.Load(fontPath);

    // A layout holds the glyphs of a text and their positions: it measures the text and is what is drawn
    var layout = TextLayout.Create("Hello, world!\nBonjour", font, size: 32);

    const int Margin = 8;
    using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + (2 * Margin), (int)Math.Ceiling(layout.Height) + (2 * Margin), new Rgba32(255, 255, 255));
    var outline = new Pen(new Rgba32(0, 0, 0), 1) { LineJoin = LineJoin.Round };
    image.Draw(canvas =>
    {
        // The origin is the top-left corner of the layout; its first baseline is layout.Baseline below
        var origin = new PointD(Margin, Margin);
        canvas.FillText(new Rgba32(30, 60, 160), layout, origin);
        canvas.StrokeText(outline, layout, origin);

        // A line under the first line of text, at its baseline
        canvas.StrokeLine(outline, new PointD(Margin, Margin + layout.Baseline + 3), new PointD(Margin + layout.Width, Margin + layout.Baseline + 3));
    });

    image.Save(outputPath);
}
```

A text of one font is `TextLayout.Create(text, font, size)`. A `TextStyle` holds everything else about how a text is
set, a `RichText` gives each part of a text its own style, and `TextLayoutOptions` say how the text is wrapped and
aligned:

<!-- snippet: draw-paragraph -->
```csharp
public static void DrawParagraph(string fontPath, string outputPath)
{
    var font = Font.Load(fontPath);
    var regular = new TextStyle(font, 18);
    var emphasis = new TextStyle(font, 18) { Brush = new SolidBrush(new Rgba32(180, 30, 30)), Decorations = TextDecorations.Underline };

    // A span is not a word or a line: the layout wraps the whole text, and shapes the letters of two spans together
    var text = new RichTextBuilder()
        .Append("Text is wrapped where ", regular)
        .Append("Unicode", emphasis)
        .Append(" allows a line to break, and each line is stretched to the width of the paragraph.", regular)
        .Build();
    var layout = TextLayout.Create(text, new TextLayoutOptions { MaxWidth = 300, Alignment = TextAlignment.Justify });

    using var image = new Image<Rgba32>(320, (int)Math.Ceiling(layout.Height) + 20, new Rgba32(255, 255, 255));

    // The brush of the call paints the spans that have none
    image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
    image.Save(outputPath);
}
```

A layout can also follow a path: `FillText` and `StrokeText` take a `VectorPath` and `TextPathOptions` instead of an
origin.

<!-- snippet: draw-text-on-path -->
```csharp
public static void DrawTextOnPath(string fontPath, string outputPath)
{
    var font = Font.Load(fontPath);
    var layout = TextLayout.Create("Text follows the path it is given", font, size: 20);

    // An arch: the text is centered on it, its baseline 4 pixels above the path
    var arch = new VectorPathBuilder().MoveTo(20, 150).CubicBezierTo(100, 10, 300, 10, 380, 150).Build();
    var options = new TextPathOptions { Alignment = TextPathAlignment.Center, BaselineOffset = 4 };

    using var image = new Image<Rgba32>(400, 170, new Rgba32(255, 255, 255));
    image.Draw(canvas =>
    {
        canvas.Stroke(new Pen(new Rgba32(200, 200, 200), 1), arch);
        canvas.FillText(new Rgba32(30, 60, 160), layout, arch, options);
    });

    image.Save(outputPath);
}
```

- **Paragraphs and lines.** A paragraph ends at a line break of the text (line feed, carriage return with or without
  a line feed, vertical tab, form feed, U+0085, U+2028, U+2029); a line break at the end starts an empty line. With
  `MaxWidth`, the lines of a paragraph are wrapped where the Unicode line breaking algorithm (UAX #14) allows it;
  `WordBreak` and `LineBreak` select its variants, and `OverflowWrap` lets a word that is wider than the layout be cut
  between two characters. The white space that ends a wrapped line hangs: it does not count in the width of the line.
  A line is as tall as its fonts ask (the largest ascent and descent, each with half of the line gap), or as
  `LineHeight` says.
- **Hyphenation.** A word is broken at a soft hyphen (U+00AD), and the line then ends with a hyphen. With
  `Hyphenation = Automatic`, a word is also broken where the patterns of its style allow it: `TextStyle.Hyphenator` is
  a `Hyphenator` of `Meziantou.Framework.Unicode`, loaded from TeX hyphenation patterns. No pattern is provided with
  the library.
- **Direction.** Each paragraph is ordered by the Unicode Bidirectional Algorithm (UAX #9): left-to-right and
  right-to-left text can be mixed, mirrored characters are mirrored, and each line is put in display order.
  `Direction` imposes the base direction of the paragraphs; by default it is the one of their first strong character.
  `Alignment` is relative to it: `Start` is the left of a left-to-right paragraph and the right of a right-to-left one.
- **Alignment.** Lines are aligned in `MaxWidth`, or in the width of the widest line without it. `Justify` stretches
  each line to that width by widening its spaces, except the last line of a paragraph. `FirstLineIndent` indents the
  first line of each paragraph, and `TabSize` sets the tabulation stops.
- **Fitting.** `MaxLines` and `MaxHeight` cut the text (the first line is always kept); `Trimming` ends the last line
  with an ellipsis after the last character or the last word that leaves room for it, and also trims a line that is
  wider than `MaxWidth` when lines are not wrapped. `IsTruncated` says that a part of the text is not in the layout.
- **Styles.** A `TextStyle` has a `FontStack` (a font, then the fonts used for the characters the ones before lack; a
  `Font` converts to a stack of one font; an instance of a variable font is a font) and a size, and optionally a brush, OpenType features to turn on or off
  (`new FontFeature("liga", 0)`, `new FontFeature("smcp")`), letter and word spacing, decorations (underline,
  strikethrough, overline, drawn with the metrics of the font) and hyphenation patterns. Spans that only differ by
  what is painted are shaped together: letters of two colors still kern and join.
- **Shaping.** Each run of one script, direction and font is shaped with the OpenType Layout tables of the font:
  - the characters are put in the form the font has glyphs for: a character the font lacks is decomposed into parts
    it has, and a character and its marks use the composed glyph when the font has it, so `é` typed as one character
    or as `e` and a combining accent gives the same result; a variation sequence selects its glyph;
  - the `GSUB` table substitutes glyphs (ligatures, contextual and localized forms, composition) and the `GPOS` table
    positions them (kerning, marks on their base or on a component of a ligature, marks on marks, cursive
    connection), with the features a client applies by default to horizontal text: `ccmp`, `locl`, `rlig`, `calt`,
    `clig`, `liga`, `rclm`, the direction features (`ltra`, `ltrm`, `rtla`, `rtlm`), `mark`, `mkmk`, `abvm`, `blwm`,
    `curs`, `dist`, `kern`, and `frac`, `numr`, `dnom` around a fraction slash (`1⁄2`);
  - a font without `GPOS` kerning is kerned by its `kern` table; a mark does not move the pen;
  - spaces of a fixed width and the non-breaking hyphen that a font lacks are made from its space and its hyphen.

  Control characters are not drawn and format characters (joiners, variation selectors, the soft hyphen where the line
  is not broken) are invisible. A character no font of the stack has is drawn with the missing glyph of the primary
  font, and `HasMissingGlyphs` says so: nothing throws.
- **Rendering.** The outlines are scaled to the size and placed at fractions of a pixel, then filled or stroked like any
  path: exact coverage, the same compositing, the same flattening tolerance. Nothing is snapped to the pixel grid and
  the hinting of the font is not applied, so the same text gives the same pixels on every platform. The glyphs and the
  decorations of one paint are one shape: where glyphs overlap, the brush is applied once. The brush of `FillText`
  paints the spans that have none; `StrokeText` strokes every outline with its pen. `TextLayout.ToPath` gives the
  outlines as a `VectorPath`; filling or stroking it changes the same pixels to the same values as a layout of one
  paint.
- **On a path.** The baseline of the first line follows the path, from its start, and the text stands on the left of
  its direction: above a path drawn from left to right. Each character, with its marks, is placed whole where the
  middle of its advance falls on the path and is turned to the direction of the path there; a glyph is never bent. The
  other lines of the layout, the decorations and the offsets of the glyphs keep their distance to the first baseline,
  across the path. `TextPathOptions` move the text along the path (`Alignment`, `StartOffset`) and away from it
  (`BaselineOffset`), put it on the other side (`Side = Right`: the path is followed from its end, as if it were
  reversed) and say what happens beyond the ends of an open path: by default (`Overflow = Extend`) the text continues
  straight, in the direction the path has at that end; with `Clip`, a character whose middle is not on the path is not
  drawn. A path of one closed contour has no end: the text goes around it. The contours of a path follow each other,
  without the gap between them, and a path without any segment draws nothing. A path is measured along the same
  segments that are drawn for it, and the direction is the one of the segment: no trigonometric function is involved,
  so the pixels are the same on every platform. A decoration is cut where the direction of the path changes. On a
  tight curve, neighboring characters overlap or part; give letter spacing to the style when that matters.
- **Measuring.** `Width` and `Height` are the size of the text, `Lines` gives each line (its part of the text, its
  position, its width, its baseline), and `InkBounds` contains everything that is drawn.
- **Cost.** Creating a layout allocates (its glyphs and positions) and decodes each distinct glyph once, which validates
  the font data the text uses: drawing a layout then only fails on a limit of the image or on cancellation, and
  allocates nothing. `FillText(brush, text, font, size, origin)` creates a layout on each call; create the layout
  yourself to measure a text or to draw it more than once. Layouts, styles, options and rich texts are immutable and
  thread-safe.
- **Limits.** `MaxTextLength` (UTF-16 code units), `MaxTextGlyphs`, `MaxTextLines` and `MaxGlyphComplexity`, from the
  configuration the primary font of the first span was loaded with.

What text layout does not do yet: the scripts that need more than the lookups of the font to be shaped (Arabic
joining, the reordering of Indic and Southeast Asian scripts, which also have no dictionary to break their lines) are
not shaped correctly. The language of a text cannot be given. Lines are broken one after the other, each as full as
possible, not by optimizing the whole paragraph. Text is horizontal. Along a path, the glyphs are placed and turned,
not bent to the curve, and the length of a path is not exposed.

The glyphs and positions are checked against the Annotated OpenType Specification tests of Adobe for every format of
the lookups, and against the OpenType shaper of HarfBuzz for texts set in the fonts of the test corpus; line breaking
and bidirectional ordering are the algorithms of `Meziantou.Framework.Unicode`, which pass the conformance tests of
the Unicode Consortium.

### Metadata, color profiles and orientation

`Image.Metadata` holds the source format (informational only), the EXIF orientation, the
resolution, ICC/EXIF/XMP profiles (byte-for-byte payloads) and text entries.

- **Color profiles are preserved, never applied implicitly.** A profile labels the pixels; untagged pixels are assumed
  sRGB. Decoders keep it, encoders write it (for example PNG `iCCP`, JPEG APP2), conversions keep it only when it can
  still label the converted pixels (gray profile for gray formats, RGB profile for color formats). Colors are converted
  only on request (see [Color management](#color-management)). A CMYK JPEG is the one image that cannot be loaded as
  it is, since no pixel format holds inks: it is rejected unless the load asks for its conversion
  (`CmykConversionOptions`).
- **Transfer functions are labels too.** `Metadata.TransferFunction` is `Srgb` (the default: sRGB samples, or as described
  by the ICC profile) or `Linear` (linear-light samples, declared by a QOI colorspace of 1 or by a JPEG XL color
  encoding with the sRGB primaries and a linear transfer function). Only `ConvertColorProfile` converts the pixels (and
  resets the label); linear-light resizing filters `Linear` samples directly. Only QOI and JPEG XL store the label: the
  other encoders treat `Linear` as metadata they cannot store, so the samples are never silently relabeled as sRGB.
- **CICP color descriptions are labels too.** `Metadata.Cicp` holds the code points of ITU-T H.273 that an AVIF file
  declares, or that are equivalent to the enumerated color encoding of a JPEG XL file: color primaries, transfer
  characteristics, matrix coefficients and range. The decoder applies the matrix and the range to produce RGB; the
  primaries and the transfer characteristics are never applied (HDR samples are not tone mapped). The AVIF encoder
  stores the description (with the matrix and the range that it used), and the JPEG XL encoder stores it when the
  format can enumerate it (full-range RGB with the sRGB, Display P3, DCI-P3 or BT.2020 primaries). For the other
  encoders, a description that is not equivalent to sRGB is metadata the format cannot store, unless an ICC profile is
  written. Linear-light processing rejects such a description.
- **Orientation is reported, never applied by decoders.** `Metadata.Orientation` reports the EXIF orientation;
  `AutoOrient()` transforms the pixels for all eight orientations and resets it to `TopLeft`. On save, the typed
  orientation is authoritative: EXIF orientation and dimension tags are rewritten and thumbnails made stale by geometry
  changes are removed.
- **Saving follows `ImageEncoder.MetadataHandling`**: `Strict` (default) throws `UnsupportedImageFeatureException` for
  metadata the format cannot store, `DiscardUnsupported` drops it, `Strip` writes no optional metadata. Animation timing
  is never affected. A malformed caller-supplied EXIF, ICC or XMP payload is rejected at save time.
- **A cursor hotspot is per-frame metadata** (`FrameMetadata.Hotspot`). CUR and ANI always store one per image (the
  top-left corner when the frame has none), whatever the policy. For every other output a frame hotspot is metadata the
  format cannot store: it follows `MetadataHandling` like the rest, so a cursor is never saved as a plain picture
  without an explicit choice.
- Decoded metadata is bounded: payloads, decompressed text included, are charged to `MaxMetadataBytes`.

| Metadata | PNG | GIF | JPEG | WebP | QOI | BMP | TGA | PNM |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ICC profile | `iCCP` | not stored | APP2 (up to 255 segments) | `ICCP` (RGB profiles) | not stored | not stored | not stored | not stored |
| EXIF and orientation | `eXIf` | not stored (orientation must be `TopLeft`) | APP1 (at most 65,527 bytes) | `EXIF` | not stored (orientation must be `TopLeft`) | not stored (orientation must be `TopLeft`) | not stored (orientation must be `TopLeft`) | not stored (orientation must be `TopLeft`) |
| XMP | `iTXt` `XML:com.adobe.xmp` | not stored | APP1, standard XMP only (at most 65,504 bytes, no extended XMP) | `XMP ` | not stored | not stored | not stored | not stored |
| Text | `tEXt`/`zTXt`/`iTXt` | Comment extension (`Comment` entries, Latin-1) | COM (`Comment` entries, Latin-1, at most 65,533 characters) | not stored | not stored | not stored | not stored | not stored |
| Resolution | `pHYs` (pixels per meter) | not stored | JFIF density | not stored | not stored | `biXPelsPerMeter`/`biYPelsPerMeter` | not stored | not stored |
| Linear transfer function | not stored | not stored | not stored | not stored | header colorspace 1 | not stored | not stored | not stored |

TIFF stores the resolution, the orientation, an ICC profile and XMP (no EXIF block, no text entries); ICO stores no
metadata, CUR only the hotspot of each representation, and ANI the hotspot of each frame plus one `Title` and one `Author`
text entry (Latin-1). AVIF stores the ICC profile and the CICP color description (`colr` boxes), EXIF and XMP (items)
and, for a still image, the orientation (`irot` and `imir` properties, never an EXIF block created for it); an image
sequence stores no orientation, and AVIF has no text entry and no resolution. JPEG XL stores an ICC profile (in the
codestream), EXIF and XMP (in `Exif` and `xml ` boxes), the orientation (in the image header, not as an EXIF tag), the
linear transfer function and a CICP color description; it stores no resolution and no text entry. Unknown chunks,
segments and extensions are skipped on decode and not round-tripped.

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

### Color management

Color conversion between ICC profiles is always explicit. `image.ConvertColorProfile(profile)` converts every frame and
the poster from the color space of the image to the color space of `profile`, then labels the image with it:

- The source is `Metadata.IccProfile`. Without profile it is sRGB (`IccProfile.Srgb`), sGray for grayscale pixel formats
  (`IccProfile.SrgbGray`), or their linear-light counterparts when `Metadata.TransferFunction` is `Linear`. A profile
  together with the `Linear` label is ambiguous and rejected.
- The pixel format is kept, so the destination must be an RGB profile for color formats and a grayscale profile for
  `Gray8`/`Gray16`. Colors are converted at the storage precision (16-bit formats stay 16-bit); alpha is copied
  unchanged and the color of transparent pixels is converted like any other.
- The operation is transactional, like a geometry change: on failure or cancellation the image keeps its pixels and
  its profile, so pixels are never left with a profile that does not describe them. The old and new buffers are
  budgeted together.
- When both profiles have identical bytes nothing is converted: converting an untagged image to `IccProfile.Srgb` only
  labels it.

<!-- snippet: convert-to-srgb -->
```csharp
public static void ConvertToSrgb(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Decoders keep the embedded profile as a label; converting the colors is always explicit.
    // An image without profile is already sRGB (sGray for grayscale pixels): nothing is converted then.
    var grayscale = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
    image.ConvertColorProfile(grayscale ? IccProfile.SrgbGray : IccProfile.Srgb);

    // Untagged pixels are read as sRGB: the label can be removed for formats that cannot store a profile
    image.Metadata.IccProfile = null;
    image.Save(outputPath);
}
```

`IccColorTransform` is the underlying primitive: an immutable, thread-safe conversion between two profiles for
interleaved 8-bit, 16-bit or floating-point samples without alpha (in place when both profiles have the same number of
channels). Integer samples cover the whole range of their type, floating-point samples are in [0, 1], and colors
outside the destination gamut are clipped per channel.

<!-- snippet: convert-samples -->
```csharp
public static byte[] ConvertSamplesToSrgb(IccProfile sourceProfile, ReadOnlySpan<byte> samples)
{
    // A transform is immutable and thread-safe: create it once for a pair of profiles and reuse it
    var options = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual };
    var transform = IccColorTransform.Create(sourceProfile, IccProfile.Srgb, options);

    // Interleaved samples without alpha: 1 (gray), 3 (RGB) or 4 (CMYK) per source color, 3 per sRGB color
    var converted = new byte[samples.Length / transform.SourceChannelCount * transform.DestinationChannelCount];
    transform.Convert(samples, converted);
    return converted;
}
```

Supported profiles are ICC version 2 and version 4 input, display, output and color space profiles for grayscale, RGB
and CMYK data, with a CIEXYZ or CIELAB connection space:

- **Matrix-based profiles**: `rXYZ`/`gXYZ`/`bXYZ` with `rTRC`/`gTRC`/`bTRC`, or `kTRC` for grayscale; sampled, gamma
  and parametric curves.
- **Table-based profiles**: `A2B0`/`A2B1`/`A2B2` and `B2A0`/`B2A1`/`B2A2` tags of type `lut8Type`, `lut16Type`,
  `lutAToBType` or `lutBToAType`. They take precedence over the matrix and curves of the same profile.
  `IccColorTransformOptions.Intent` selects the table: perceptual (0), colorimetric (1) or saturation (2); a profile
  without the table of the intent uses its perceptual table, then its matrix and curves. CMYK samples are ink
  amounts: 0 is no ink.

**CMYK images.** No pixel format holds CMYK samples, so a JPEG file with four components (CMYK or YCCK inks) is only
loaded by converting its colors, and like every color conversion this one is explicit: set
`ImageDecodeOptions.CmykConversion` (or `ImageReaderOptions.CmykConversion`). Without it the load throws an
`UnsupportedImageFeatureException`; `Identify` always works and reports `ImageColorModel.Cmyk` or `Ycck` with the
embedded CMYK profile, so a caller can decide first. Other images are not affected by the setting.

<!-- snippet: load-cmyk-jpeg -->
```csharp
public static Image LoadCmykJpeg(string path)
{
    // Default options: converted to sRGB from the CMYK profile of the file, or from
    // IccProfile.Cgats001CompatibleCmyk when it has none. Images that are not CMYK are loaded as usual.
    var conversion = new CmykConversionOptions
    {
        Transform = new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual },
    };

    // The pixels are RGB, labeled with the destination profile (image.Metadata.IccProfile)
    return Image.Load(path, new ImageDecodeOptions { CmykConversion = conversion });
}
```

- `DestinationProfile` (default `IccProfile.Srgb`) is an RGB profile. The loaded image has `Rgb24` pixels labeled with
  it: the pixels and their profile change together, and the CMYK profile is not kept. A typed load then converts the
  layout as for any RGB image.
- The source is the CMYK profile embedded in the file. A file without one (or with a profile of another color space,
  which cannot describe inks) uses `AssumedSourceProfile`, by default `IccProfile.Cgats001CompatibleCmyk`; set it to
  another CMYK profile, or to `null` to reject such files. An embedded CMYK profile that is malformed or not supported
  is an error: it is never replaced by the assumed profile.
- `Transform` is the rendering intent and the black point compensation, as for `IccColorTransform`.
- `IccProfile.Cgats001CompatibleCmyk` is `CGATS001Compat-v2-micro.icc` of the
  [Compact ICC Profiles](https://github.com/saucecontrol/Compact-ICC-Profiles) collection by Clinton Ingram, released
  to the public domain (CC0-1.0) and stored unmodified: web offset printing on coated paper, from the CGATS TR 001
  characterization data. Its single table of 6 points per ink reproduces the overall appearance of the printing
  condition, not the exact colors of a full-size press profile, and it only converts from CMYK: it can be a source
  with any intent, never a destination.
- JPEG files store ink samples as complements (255 is no ink), as Adobe Photoshop writes them, and decoders read every
  four-component file that way, with or without the Adobe APP14 segment: so does the library. The segment says whether
  the first three components are coded as luma and chroma (transform 2, YCCK) or not (transform 0, or no segment:
  CMYK).
- Every pixel goes through the ICC conversion, which is evaluated exactly, one color at a time: decoding a CMYK image
  is much slower than decoding an RGB image of the same size.

`IccColorTransformOptions` selects how colors that differ between the two media are rendered:

- `Intent` is the rendering intent. `RelativeColorimetric` (the default) reproduces in-gamut colors relative to the
  white of each medium; `Perceptual` and `Saturation` use the tables the profile creator made for these purposes;
  `AbsoluteColorimetric` uses the colorimetric conversion and then rescales the colors by the media white points of the
  two profiles (`wtpt`; the illuminant for a display profile and for a profile without the tag), so that the white of
  the source medium is reproduced instead of becoming the white of the destination. Matrix-based profiles give the
  same result for the first three intents.
- `BlackPointCompensation` (enabled by default) maps the darkest color of the source to the darkest color of the
  destination by scaling the colors toward white, so that shadow detail is neither clipped nor lifted. It follows the
  algorithm published by Adobe ("Adobe Systems' Implementation of Black Point Compensation", standardized as
  ISO 18619): the black of the source is the color of its device black (for a CMYK output profile, of the inks its
  perceptual table gives for black); the black of a table-based destination is read from the round trip of a
  lightness ramp through the profile. It changes nothing when both blacks are black, as between two display
  profiles, and never applies to the absolute colorimetric intent.

A malformed profile is an `InvalidImageContentException`; a valid profile of another kind (version 5, device link,
abstract, named color, other data color spaces), or one that cannot be a destination (no table from the connection
space, a constant tone curve, colorants that do not span a color space), is an `UnsupportedImageFeatureException`.
Table sizes are validated against the profile before anything is read. Color lookup tables are read in place from the
profile; a transform only copies the one-dimensional curves, so its memory is bounded by a small multiple of the
profile size, and converting samples allocates nothing. The first curve of an 8-bit conversion is read from a
256-entry table per channel, with results identical to the evaluation of the curve.

The conversion is evaluated in double precision, one color at a time, from the formulas of the ICC specification:
results are the same on every platform up to the last bit of `Math.Pow` and `Math.Cbrt`, and integer samples are
rounded to nearest with ties upward. A destination tone curve is inverted exactly (the smallest input reaching the
value), so a round trip through a matrix-based profile and back changes a 16-bit sample only where the curve itself is
not invertible. The ICC specification does not define how tables are interpolated between grid points; the library
uses simplex interpolation along the main diagonal of the grid cell (tetrahedral interpolation) when the inputs are
RGB, gray or CIEXYZ, whose neutral axis is that diagonal, and multilinear interpolation when they are CIELAB or CMYK,
where it is not. Color management systems differ here: between the grid points of a coarse CMYK table they can
disagree by several levels, and they may also estimate black points differently.

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
encoded bytes, metadata bytes, live allocations, and for fonts the bytes of a font and the complexity of a glyph) are
safety bounds: exceeding one throws `ImageResourceLimitException`
(`Kind`, `Limit`, `Requested`) and is never turned into a successful truncation. `FrameLimit` (decode and reader options)
is deliberate prefix selection: decoding stops after that many displayed frames and the rest of the input is neither
examined nor validated. Limits are inclusive and enforced before allocating or consuming.

AVIF is detected from its file type box (12 bytes) and read forward only: the data of the image are captured as they go
by, so a file whose media data precede its index is buffered up to that index, bounded by `MaxEncodedBytes` and the live
allocation limit.

`MaxEncodedBytes` equal to the length of the input is always enough, and a smaller value fails the same way from a span,
a path or a stream. A stream only reports its end by returning no byte, so when the format needs the end of the input (the
TGA trailer, the last sample of a plain Netpbm raster, a TIFF, an icon or an animated cursor buffered whole, an input shorter than the detection
prefix) and every byte the limit allows was read, one more byte is read and discarded to tell an input that ends there from
a longer one. It is the only byte ever read past the limit, and it is never decoded.

TIFF, ICO, CUR and ANI are random-access containers: on a seekable source, only the bytes their structure points at are read
(the stream position afterward is unspecified), and a non-seekable or asynchronous load buffers the input, bounded by
`MaxEncodedBytes`.

### Streaming readers and writers

Readers and writers process long animations frame by frame with memory bounded by about one frame
of decoder or encoder state, plus the images the caller keeps.

**Streams.** Every stream API starts at the stream's current position and consumes what it reads (no rewinding).
Non-seekable streams are supported for input (bounded prefix replay) and output (no seeking, except for the WebP
animations, animated cursors and TIFF files described below). Caller streams are left open: eager APIs never close them, and
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
  canvas size, exactly one frame, no poster and no animation settings; its duration, hotspot and metadata are refreshed. It returns
  `false` at the clean end (destination unchanged); after a failure the destination may be partially updated but stays
  valid, and it stays charged to its own owner.
- `null`/`false` always means a clean end. Malformed data, limits, I/O errors and cancellation throw and fault the reader
  (later calls throw `InvalidOperationException`); argument errors leave it usable.
- TIFF, ICO and CUR are rejected explicitly: their entries are pages or representations, not a frame sequence (use
  `ImageCollection`). ANI is rejected too: its steps show stored frames in an order that is only known once the whole
  file is read (use `Image.Load`).

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
| JPEG XL | Optional; any count, without seeking (a frame is written when the next one, or the completion, tells whether it is the last) |
| ANI | Optional; any count, with a seekable destination (the counts and sizes are patched at completion) |
| AVIF | Required; an image sequence (any count other than 1) needs a seekable destination (its index is written at completion, in the room that the count reserves) |
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
`.bmp`/`.dib`, `.tga`/`.icb`/`.vda`/`.vst`, `.pnm`/`.pam`/`.ppm`/`.pgm`, `.pbm` (a portable bitmap, from black and white
pixels only), `.tif`/`.tiff`, `.ico`, `.cur`, `.ani`, `.avif`/`.avifs` (lossless by default) and `.jxl` (lossless); anything else throws `ArgumentException`. An eager save validates the whole image against the output (animation, poster,
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
| `UnsupportedImageFeatureException` (`Format`, `Feature`) | A valid but unsupported feature (arithmetic JPEG, a CMYK JPEG loaded without `CmykConversion`, GIF plain text...), or an operation that would lose animation, alpha, precision, metadata, a cursor hotspot, a poster or a profile without an explicit setting, or a duration/play count the output cannot represent. |
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
| Color models | Grayscale (`Gray8`), JFIF full-range YCbCr and RGB (Adobe transform 0 or `R`,`G`,`B` identifiers) to `Rgb24`; CMYK and YCCK (four components) to `Rgb24` through the ICC conversion of `CmykConversionOptions`, see [Color management](#color-management) | The layout follows the pixel format, never the data: gray formats → one grayscale component, color formats → JFIF full-range YCbCr (BT.601); JFIF APP0 always written, no Adobe APP14. Non-opaque pixels require `BackgroundColor`; `Rgba64`/`Gray16` require `AllowBitDepthReduction` |
| Chroma subsampling | 4:4:4, 4:2:2, 4:2:0, 4:4:0, 4:1:1 and any integral sampling ratio (libjpeg-turbo-compatible triangle filters for factor 2, replication otherwise) | `Auto` (= 4:2:0 for color), 4:4:4, 4:2:2, 4:2:0; box-filter downsampling, canvas padded to whole MCUs by edge replication; ignored for gray formats |
| Restart markers | Sequential and progressive scans, sequence checked | Not written |
| Metadata (EXIF, XMP, ICC, JFIF density, COM) | Bounded; ICC reassembled from APP2 chunks; segments after the scans included | APP1 EXIF (orientation and dimensions reconciled; at most 65,527 bytes), APP1 standard XMP (at most 65,504 bytes), APP2 ICC in up to 255 chunks, JFIF density, COM for `Comment` entries; anything else follows `MetadataHandling` (`Strip` writes only the JFIF segment) |
| Animated images, posters | — | Rejected with `UnsupportedImageFeatureException` before any output; export a frame with `CloneFrame` |
| CMYK/YCCK (four components) | Identified; loaded only with `CmykConversion`, otherwise `UnsupportedImageFeatureException` | Not supported |
| Arithmetic, lossless, hierarchical, 12/16-bit, non-integral sampling | Not supported (`UnsupportedImageFeatureException`, also from `Identify`) | Not supported |

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
    // the precision, so 16-bit images keep every bit. A portable bitmap (PBM) is only written on request, with
    // PnmPixelLayout.Bitmap, and only from black and white pixels.
    frame.Save(pnmPath, new PnmEncoder());
}
```

The BMP encoder never picks a lossy layout by itself: `BmpPixelLayout.Auto` is 24-bit or 32-bit. The indexed layouts
(`Indexed1`, `Indexed4`, `Indexed8`) store the colors of the image exactly when its palette can hold them, and reject
the image otherwise unless `AllowColorReduction` is set; `Indexed4` and `Indexed8` can be run-length encoded.

<!-- snippet: save-bmp-indexed -->
```csharp
public static void SaveIndexedBmp(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    using var frame = image.CloneFrame(0);

    // 8-bit indexes, run-length encoded (BI_RLE8). The palette holds the colors of the frame exactly when there are at
    // most 256 of them; AllowColorReduction accepts a deterministic palette for the other frames, here with dithering.
    // A BMP palette has no alpha, so non-opaque pixels need a background color.
    frame.Save(outputPath, new BmpEncoder
    {
        PixelLayout = BmpPixelLayout.Indexed8,
        Compression = BmpCompression.RunLength,
        AllowColorReduction = true,
        Dithering = BmpDithering.FloydSteinberg,
        BackgroundColor = new Rgba64(65535, 65535, 65535),
        AllowBitDepthReduction = true,
    });
}
```

| Feature | Decoding | Encoding |
| --- | --- | --- |
| DIB headers | `BITMAPINFOHEADER` and the V2, V3, V4 and V5 headers; the OS/2 1.x `BITMAPCOREHEADER` (12 bytes, 16-bit dimensions, three-byte palette entries, a palette that may end early at `bfOffBits`) and the OS/2 2.x `BITMAPCOREHEADER2` (16 to 64 bytes: the fields a short header leaves out are zero; the lengths 40, 52 and 56 are read as Windows headers). Other lengths and inconsistent headers are `InvalidImageContentException` | `BITMAPINFOHEADER`, or `BITMAPV4HEADER` (`LCS_sRGB`) for 32-bit output; OS/2 headers are never written |
| Layouts | 1/4/8-bit indexed (partial palettes, indexes outside the palette rejected), 16-bit (5-5-5), 24-bit BGR, 32-bit BGRX; 2-bit (Windows CE) is not supported | `BmpPixelLayout`: 24-bit `BI_RGB` or 32-bit `BI_BITFIELDS` by default (`Auto`); `Indexed1`, `Indexed4`, `Indexed8` (the palette stores only the colors used, `biClrUsed`); `Bgr565` (`BI_BITFIELDS`) and `Bgr555` (`BI_RGB`), which need `AllowBitDepthReduction` |
| Indexed output | | Exact when the image has at most `min(MaxColors, 2^bits)` colors; otherwise `UnsupportedImageFeatureException` unless `AllowColorReduction`, which builds a deterministic median-cut palette and maps each pixel to its nearest entry, with optional Floyd–Steinberg `Dithering` (from the top row down, whatever the row order). Needs one byte per pixel of working memory |
| Bit fields (`BI_BITFIELDS`, `BI_ALPHABITFIELDS`) | 16 and 32 bits, contiguous masks of 1 to 8 bits, expanded with `round(value * 255 / (2^n - 1))`; channels wider than 8 bits (10-10-10-2) are not supported | Explicit BGRA masks for 32-bit output, 5-6-5 masks after the header for `Bgr565`; samples are reduced with `round(value * (2^n - 1) / 255)` |
| Alpha | Only a declared alpha mask is transparency: the fourth byte of a 32-bit `BI_RGB` payload is unspecified padding, so such a file loads as opaque `Rgb24` | `BmpPixelLayout.Auto` writes 32-bit with an explicit alpha mask for pixel formats with alpha; every other layout discards alpha and needs `BackgroundColor` as soon as a pixel is not opaque |
| Rows | Padded to four bytes, bottom-up or top-down; bytes after the last row are not read | Padded to four bytes, bottom-up by default (read from the frame in reverse, no seeking) or top-down (`BmpRowOrder.TopDown`, a negative `biHeight`) |
| Compression | `BI_RLE8`, `BI_RLE4` and the OS/2 2.x `BCA_RLE24`: encoded runs, absolute runs, deltas, end of line and end of bitmap. The pixels a stream skips are palette index 0 (black for RLE24), never transparency. The stream is read strictly: a run past the end of its row, a delta outside the canvas, data after the last row, a missing end-of-bitmap marker, a bit count that does not match and top-down rows are `InvalidImageContentException`; bytes after the end-of-bitmap marker are not read. Embedded JPEG/PNG payloads and OS/2 Huffman 1D are `UnsupportedImageFeatureException` | `BmpCompression.RunLength`: `BI_RLE8` for `Indexed8`, `BI_RLE4` for `Indexed4` (bottom-up only; any other combination is an `ArgumentException`). Lossless; every pixel is stored (no delta), runs never cross a row, and the declared sizes are exact without seeking. It can be larger than uncompressed output |
| Metadata | Resolution (`biXPelsPerMeter`/`biYPelsPerMeter`, and the OS/2 2.x resolution when its unit is metric); V4/V5 color-space fields and embedded ICC profiles are not read | Resolution only; everything else follows `MetadataHandling` |

### TGA

TGA has no signature, so it is recognized from a strictly plausible 18-byte header
and checked last; give `DetectFormat` 18 bytes (`Image.FormatDetectionPrefixLength`).

The TGA 2.0 footer is the last 26 bytes of the file and locates the extension area, so a TGA is read to the end of its
input: a stream is consumed to its end (nothing can follow a TGA on the same stream), and the bytes after the image data
are buffered until then, bounded by `MaxEncodedBytes` and `MaxLiveAllocationBytes`. When the file ends with a footer, the
extension area it locates must lie between the image data and the footer and hold its 495 bytes
(`InvalidImageContentException` otherwise), and its attributes type is read: premultiplied alpha (type 4) for an image
with declared alpha bits is `UnsupportedImageFeatureException`, because pixels are stored with straight alpha and are never
divided by it. No other attributes type changes anything: the image descriptor alone decides the alpha channel, before
the first pixel is read. The developer area is application data and is not interpreted. `Identify` in `Header` mode stops
after the header and never sees the trailer.

A file without a footer is a TGA 1.0 file, whose trailing bytes mean nothing. A file cut anywhere after its image data
is therefore a valid TGA 1.0 file with the same pixels: truncation is only detected before the end of the image data.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Image types | Uncompressed and run-length true-color, grayscale and color-mapped | True-color and grayscale; color-mapped output is never written |
| Depths | 15/16-bit `A1 R5 G5 B5`, 24-bit BGR, 32-bit BGRA, 8-bit grayscale, 16-bit grayscale (an 8-bit gray sample and an 8-bit attribute, never a 16-bit luminance), 8-bit and 16-bit little-endian indexes with 15/16/24/32-bit map entries | 8-bit grayscale, 24-bit BGR, 32-bit BGRA; 15/16-bit output is never written |
| Color map | Up to 65,535 entries, charged to `MaxLiveAllocationBytes` while decoding; the first-entry offset is honored; an index outside the stored map is `InvalidImageContentException` | Never written |
| Run-length packets | Packets of 1 to 128 pixels, crossing scan lines accepted; a packet past the last pixel is `InvalidImageContentException` | `TgaCompression.RunLength`: runs of two or more identical neighbours, never crossing a scan line |
| Orientation | Both origin bits are resolved to the displayed image | Origin at the bottom left |
| Alpha | The descriptor's alpha-bit count is authoritative: a 32-bit payload with 0 alpha bits has an unspecified fourth byte, which is discarded, and so is the attribute byte of a 16-bit grayscale pixel (the image is `Gray8`); with 8 alpha bits, 16-bit grayscale is gray and alpha, decoded to `Rgba32` (`ImageColorModel.GrayscaleAlpha`); premultiplied alpha (attributes type 4 of the extension area) is rejected | 8 declared alpha bits for pixel formats with alpha, straight alpha, no extension area |
| Identification field, TGA 2.0 trailer | Skipped / the footer and the extension area are validated, the developer area is not interpreted | No identification field, a TGA 2.0 footer with zero offsets; at most 65,535 pixels per side, no metadata |

### Netpbm: PBM, PGM, PPM and PAM

`MAXVAL` up to 255 gives 8-bit samples, above it 16-bit, normalized with
`round(value * targetMax / MAXVAL)`. Only the first image of a concatenation is read, and it is never reported as an
animation.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Magic numbers | `P1` to `P6` (plain and binary PBM, PGM, PPM) and `P7` (PAM) | `P5`/`P6` (binary) or `P2`/`P3` (plain, `PnmEncoding.Plain`), and `P7` for pixel formats with alpha: the variant follows the pixel format. `P4`, or `P1` when plain, only with `PnmPixelLayout.Bitmap`. An encoder never looks at the extension; `Save(path)` without an encoder maps `.pbm` to `PnmPixelLayout.Bitmap` |
| Header | White space and `#` comments between tokens, exactly one white-space byte before a binary raster, `ENDHDR` for PAM; bounded at 65,536 bytes; illegal values are `InvalidImageContentException` | — |
| Tuple types | `BLACKANDWHITE`, `GRAYSCALE`, `RGB` and their `_ALPHA` forms, inferred from `DEPTH` when `TUPLTYPE` is absent; others are not supported | `RGB_ALPHA` for pixel formats with alpha |
| Samples | 8 or 16 bits (big-endian); a sample above `MAXVAL` is `InvalidImageContentException`; a set PBM bit is black, while a PAM `BLACKANDWHITE` sample is an intensity | `MAXVAL` 255 or 65,535, so the output is always lossless (no bit-depth setting). A portable bitmap is lossless too: one bit per pixel, a set bit is black, and a pixel that is not exactly black or white (once composited over `BackgroundColor`) is `UnsupportedImageFeatureException`; it is read back as `Gray8` with one bit per component |
| Plain rasters | Decimal samples with comments allowed; a prefix of a plain raster can be a complete raster with a smaller last sample, so truncation is not detectable there | Lines within 70 characters; PAM has no plain form, so alpha then needs `BackgroundColor`, as it does for a portable bitmap. A plain portable bitmap has one digit per pixel, without separators |
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
[Pages and representations](#pages-and-representations)); `Image.Load` reads the largest (then deepest) one, with the
hotspot of a cursor on its frame.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Directory | Checked payload offsets and lengths, reserved fields validated, count bounded by `MaxFrames` | One entry per representation; payloads are encoded in memory first, so no seeking is needed |
| Geometry | The payload is authoritative (a directory byte cannot express more than 256, and 0 means 256); a contradicting non-zero directory dimension is rejected | At most 256 pixels per side |
| PNG payloads | Decoded by the PNG codec; an animated PNG payload is rejected | A still PNG without metadata (`IconPayloadFormat.Png`, or `Auto` above 64 pixels per side or for 16-bit pixels) |
| DIB payloads | The icon rules, not the BMP ones: the stored height is doubled (color rows plus the 1-bit AND mask), bottom-up rows, 1/4/8/16/24/32-bit layouts, palettes, bit-field masks; Windows headers and uncompressed rows only (run-length encoding and OS/2 headers are rejected); every DIB-backed representation decodes to `Rgba32` | 32-bit `BI_RGB` with straight alpha and an all-zero AND mask (`IconPayloadFormat.Dib`, or `Auto` up to 64 pixels per side); 8-bit samples only, so `Dib` rejects `Rgba64` and `Gray16` pixels instead of narrowing them |
| AND mask and alpha | A 32-bit `BI_RGB` payload stores alpha in its fourth byte, unless that channel is entirely zero, in which case the AND mask decides transparency | The alpha channel carries the transparency |
| Cursor hotspots | Read from a `CUR` entry and validated against its own representation; exposed by `ImageCollectionEntry.Hotspot` and on the frame of the decoded image | `IcoEncoder.Kind = Cursor` writes the hotspot of every entry, or of the frame for `Image.Save` (top-left when there is none); an icon cannot store one and follows `MetadataHandling` |

Animated cursors are a different container: see [ANI](#ani).

### ANI

An animated cursor is an animation: `Image.Load` returns one frame per step, in playback order, each with the duration
of its step and the hotspot of its cursor image, and `Image.Save` writes one step per frame. The container is read by
offset, like the icon files it embeds, so it is neither streamed by `Image.OpenReader` nor an `ImageCollection`.
**`AniEncoder` needs a seekable destination**, because the counts and sizes stored at the start of the file are only
known once the last frame is written.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Container | RIFF `ACON`: chunks in any order (tables before or after the frames), unknown chunks skipped, word alignment, every size checked against its container, counts bounded by `MaxFrames`; bytes after the RIFF container are ignored | `anih`, the frame list, then `rate` and `seq ` when they are needed; the counts and sizes are patched at completion |
| Steps and sequence | The `seq ` chunk is expanded: a frame shown by several steps is read and decoded once and becomes as many independent frames, each counted against `MaxFrames` and `MaxTotalPixels`; frames that no step shows are not decoded | Frames whose encoded cursor image and hotspot are identical are stored once and replayed through `seq ` |
| Timing | Jiffies (`j/60` s) from `rate`, else the default rate of the header; kept exactly, zero included | `AniEncoder.DurationRounding` (default `RoundToNearest`, ties up); at most 2^32 - 1 jiffies; a `rate` chunk only when the durations differ |
| Play count | Always infinite (`TotalPlays = null`); a file is always an animation, even with a single step | A finite `TotalPlays` is rejected; a still image is written as one step |
| Frames | Icon or cursor files with PNG or DIB payloads (see [ICO and CUR](#ico-and-cur)). The first step fixes the canvas with the largest (then deepest) representation; every other frame shows its deepest representation of that size, and a frame without one is rejected | One representation per frame, written as a cursor file: `IconPayloadFormat.Dib`, `Png`, or `Auto` (a PNG above 64 pixels per side or for 16-bit pixels); at most 256 pixels per side |
| Pixel format | `Rgba32`, or `Rgba64` for the whole animation when a displayed PNG frame has 16-bit samples: nothing is narrowed | Any; 16-bit pixels need a PNG payload (`Dib` is rejected) |
| Hotspots | The hotspot of the displayed cursor representation, per frame (`null` for an icon-typed frame), validated against it | `Frames[i].Metadata.Hotspot`, per frame (top-left when there is none) |
| Metadata | `INFO` list: `INAM` as a `Title` text entry and `IART` as an `Author` one (Latin-1) | One `Title` and one `Author` text entry (Latin-1, no NUL), following `MetadataHandling` |
| Memory | The structure is validated without decoding a pixel; one embedded frame is decoded at a time | One encoded frame at a time, plus 8 bytes per step and a digest per distinct frame |

### AVIF

AVIF is decoded and encoded. The AV1 decoder and the AV1 encoder are fully managed and written from the AV1
specification. The decoder is exact: every plane it decodes equals the output of the reference decoders, film grain
included. The encoder reconstructs its frames with the reconstruction and the filters of the decoder, so a file it
writes decodes to the same samples in every conforming decoder.

<!-- snippet: load-avif -->
```csharp
public static string ConvertAvif(string avifPath, string pngPath)
{
    using var image = Image.Load(avifPath);

    // The color description of the file (primaries, transfer characteristics, matrix and range code points) is a
    // label, as an ICC profile is: the matrix and the range were applied to produce RGB, nothing else was converted.
    var description = $"{image.Width}x{image.Height} {image.PixelFormat}, frames: {image.Frames.Count}, color: {image.Metadata.Cicp}";

    // PNG keeps 16-bit samples, alpha and animations, without loss. It cannot store a color description
    // that is not sRGB, so a strict save of an HDR image fails instead of relabeling its samples.
    image.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
    return description;
}
```

| Feature | Decoding |
| --- | --- |
| Presentation | A file with the `avis` brand presents its image sequence (the `pict` track): an animation whose frames are full-canvas images. Any other file presents its primary item. Thumbnails, gain maps, depth maps and the other images of the file are not read |
| Bitstream | Every AV1 profile: 8, 10 and 12 bits, 4:4:4, 4:2:2, 4:2:0 and monochrome, intra and inter frames, intra block copy, super-resolution, reference scaling, film grain, temporal and spatial layers |
| Layered images | A layered (progressive) image holds a frame per spatial layer, each refining the previous ones. The image is the frame of the highest layer of the operating point, which is the image at its full quality; the lower layers are decoded on the way and never returned. The `a1op` property selects the operating point (the first one otherwise) and the `lsel` property a layer, whose frame is then the image and must have the size that the `ispe` property declares. In an image sequence, every sample gives the frame of its highest layer |
| Pixel formats | 8-bit files load as `Rgb24` (`Rgba32` with alpha, `Gray8` for monochrome without alpha); 10-bit and 12-bit files load as `Rgba64` (`Gray16` for monochrome without alpha), their samples scaled to 16 bits exactly |
| Color conversion | The matrix coefficients and the range of the `colr` box, or else of the bitstream, convert YCbCr to RGB with integer arithmetic and a documented rounding: identity (RGB, exact), BT.709, FCC, BT.470BG, BT.601, SMPTE 240M, YCgCo and BT.2020 non-constant luminance; unspecified coefficients are BT.601. Subsampled chroma is upsampled bilinearly at the sample position that the bitstream declares |
| Alpha | The alpha auxiliary image, or the alpha track of a sequence, of the size of the color image. Premultiplied alpha is rejected |
| Grids | Grid images, for the color image and its alpha plane. The transformations of a grid apply once, to the assembled image, whether or not the tiles repeat them |
| Transformations | `irot` and `imir` are composed and reported as `Metadata.Orientation`, never applied (`AutoOrient()` applies them); the EXIF orientation tag is ignored, as the format requires. `clap` crops the image when it selects whole samples. `pasp` is ignored |
| Metadata | ICC profile, EXIF, XMP and the CICP color description. A still image takes them from its primary item. An image sequence takes its ICC profile and its color description from its track, and its EXIF and XMP from the `meta` box of the track, or else from the `meta` box of the file when it precedes the `moov` box |
| Identification | `Header` mode reads the `meta` box (or the `moov` box of a sequence). When the file has no `colr` box, it reads on to the sequence header of the bitstream, which gives the color description and tells RGB samples from YCbCr ones, and stops there; if the bitstream precedes that box, it has already gone by and the color model is `Unknown`, never guessed. The EXIF and XMP items, which are data of the file, are returned by a full scan or a load. `FullScan` also walks every item and sample, and checks the sample format of the bitstream against its declaration |
| Animations | Exact frame durations (the sample durations over the time scale of the track); the number of plays comes from the edit list, and a sequence without edit list loops forever. `ImageReader` decodes a sequence frame by frame with a memory that does not depend on the number of frames |
| Limits | Every frame of the bitstream is checked against the canvas limits, and the frames that are decoded without being shown (hidden frames, and the lower layers of a layered image) count toward `MaxTotalPixels` |

Derived images other than grids (`iden`, `iovl`), fragmented files (`moof`), edit lists that do not play the whole
track, sequences whose images change size, large scale tile streams and matrix coefficients other than the ones above
are rejected with `UnsupportedImageFeatureException`.

<!-- snippet: save-avif -->
```csharp
public static void SaveAvif(string photoPath, string avifPath, string losslessPath, string animatedPath, string animatedAvifPath)
{
    using (var photo = Image.Load(photoPath))
    {
        // Lossy: Y'CbCr 4:2:0 by default. The alpha plane, when the image has one, is always stored losslessly.
        photo.Save(avifPath, new AvifEncoder
        {
            Compression = AvifCompression.Lossy,
            Quality = 80, // 0-100: size versus fidelity
            Effort = 6,   // 0-9: encoding time versus size
            AllowBitDepthReduction = true, // 16-bit sources are written with 12 bits
        });

        // The default is lossless: every sample of an 8-bit image is decoded back exactly. AVIF stores at most 12 bits:
        // a 16-bit image needs a bit depth, which is the consent to round its samples.
        photo.Save(losslessPath, new AvifEncoder { BitDepth = 12 });
    }

    // An animation is written as an image sequence, with its exact durations and its play count. It needs a seekable
    // destination (the index of the images is written last): a path, a FileStream or a MemoryStream.
    using var animation = Image.Load(animatedPath);
    animation.Save(animatedAvifPath, new AvifEncoder());
}
```

| Feature | Encoding |
| --- | --- |
| Compression | `AvifCompression.Lossless` (the default) codes the R'G'B' samples without color conversion (identity matrix, 4:4:4, full range), and gray pixel formats as a monochrome image: every coded sample is decoded back exactly. `AvifCompression.Lossy` codes Y'CbCr samples; `Quality` (0 to 100, 75 by default) selects the quantizer and `ChromaSubsampling` the chroma layout: 4:2:0 (the default), 4:2:2 or 4:4:4. Lossless files are large: select the lossy coding for photographs |
| Bit depth | 8-bit pixel formats are written with 8 bits, or with `BitDepth` 10 or 12 (scaled up exactly). AVIF stores at most 12 bits: `Rgba64` and `Gray16` need `BitDepth`, or `AllowBitDepthReduction` (12 bits), and are rounded to nearest. The samples of a 10-bit or 12-bit AVIF image, which load as 16-bit pixels, are coded back unchanged at their depth |
| Color conversion | Lossy output keeps the matrix coefficients and the range of `Metadata.Cicp` when they are ones that the decoder converts (BT.709, FCC, BT.470BG, BT.601, SMPTE 240M, YCgCo, BT.2020 non-constant luminance), and uses BT.601 in full range otherwise. The conversion is integer only and rounds each sample once; a subsampled chroma sample is the mean of the pixels it covers (centered samples) |
| Alpha | An auxiliary image at the bit depth of the color image, always lossless. A still image whose pixels are all opaque has none. `ClearTransparentColors` replaces the color of the fully transparent pixels with black, which makes the file smaller |
| Effort | `Effort` (0 to 9, 5 by default) widens the search: block sizes from 64x64 down to 4x4, rectangular partitions (and the partitions into three and four parts at effort 9), the number of intra modes that are tried, angle deltas, recursive filter modes, the prediction of chroma from luma, the number of transform types that are tried, the split of the transform blocks of intra blocks and the number of values that are tried for the in-loop filters, and for the images of a sequence the reach of the motion search, its refinement to a quarter of a sample and the number of predicted motion vectors that are tried. It never changes the decoded pixels of lossless output. Encoding is single-threaded and far slower than a native encoder: of the order of a megapixel per second at effort 0, and of a seventh of that at the default effort, for a lossy photograph on a recent laptop (the `avif-quality` command of the benchmarks measures sizes, qualities and times) |
| Animations | An image with several frames is written as an image sequence (`avis` brand): every frame is a full image, with its exact duration (the time scale of the track is the least common multiple of the denominators; `DurationRounding` rounds to microseconds when there is none of 32 bits) and the play count of the animation. A pixel format with alpha has an alpha track. The first image is also the primary item of the file, for the readers that do not play sequences. The destination must be seekable and the frame count known: the `moov` box, which indexes the images, is written at completion in the room reserved before the images. A sequence of images without duration that repeats forever has no edit list: an edit without duration cannot be repeated |
| Screen content | An image that looks like the content of a screen (a tenth of its 16x16 blocks have few colors, far from one another) is coded with palettes: a block whose samples have at most 8 values in a plane is predicted exactly, and a block with a few more values by 8 colors that stand for groups of close values. Text, interface elements and pixel art are several times smaller than without, lossless or not; a photograph is not affected |
| Inter prediction | The first image of a sequence is a key frame. Each other image is predicted from the image that precedes it: a block copies the samples that a motion vector points to (to a quarter of a sample), codes what differs, or is intra coded where that costs less, so the part of an image that does not change costs almost nothing. `KeyFrameInterval` (0 by default: no other key frame) is the number of images from a key frame to the next one; 1 makes every image a key frame. Key frames are the sync samples of both tracks: a reader that seeks decodes from the last one. Lossless output stays lossless |
| Metadata | A `colr` box of type `nclx` is always written: the primaries and the transfer characteristics of `Metadata.Cicp` (unspecified without one) and the matrix and the range that were used. The ICC profile, EXIF and XMP are stored; the orientation of a still image is stored as `irot` and `imir` properties (the pixels are written as they are). An image sequence stores no orientation: it is metadata the format cannot store |
| Memory | A still image is coded in memory before the file is written, so it goes to any stream. An image sequence keeps 24 bytes per frame; its coded frames are written as they are encoded |
| Limits | At most 65,536 pixels per side (65,535 for an image sequence) and 4 GiB for a still image |

The encoder does not use every coding tool of AV1: see [Limitations](#limitations).

### JPEG XL

JPEG XL files are decoded, lossy or lossless, and encoded losslessly. The decoder and the encoder are fully managed. The
lossless path is exact: a lossless file decodes to the samples that were encoded, and the files the encoder writes
decode to the same samples in libjxl and jxl-oxide. The lossy path (VarDCT) is defined in floating point: the decoded
samples are within a few sixteen-bit units of what those decoders give (they differ from each other by as much), not
bit-exact.

<!-- snippet: save-jpegxl -->
```csharp
public static string SaveJpegXl(string inputPath, string jxlPath, string animatedPath, string animatedJxlPath)
{
    // Every pixel format is stored as it is: no bit depth reduction, no flattening, and the colors of fully
    // transparent pixels are kept. The effort changes the size and the encoding time, never the pixels.
    using (var image = Image.Load(inputPath))
    {
        image.Save(jxlPath, new JpegXlEncoder { Effort = 7 });
    }

    // Frames are written as they come: the destination does not need to be seekable, whatever the frame count.
    using (var animation = Image.Load(animatedPath))
    {
        animation.Save(animatedJxlPath);
    }

    // Lossy (VarDCT) and lossless (Modular) files are decoded, whatever wrote them.
    var info = Image.Identify(jxlPath);
    return $"{info.Format} {info.Size.Width}x{info.Size.Height} {info.PixelFormat}, {info.BitsPerComponent} bits";
}
```

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Files | A bare codestream, or the container: `jxlc` or `jxlp` boxes (a codestream split anywhere), `Exif`, `xml ` and their Brotli-compressed form (`brob`), boxes of 64-bit size or that extend to the end of the file | A bare codestream; the container when the image has EXIF or XMP, or needs level 10 of the format (16-bit samples, more than 262,144 pixels per side or 268,435,456 pixels, an ICC profile above 4 MiB) |
| Coding | VarDCT (every transform, quantization table and coefficient order, chroma from luma, adaptive LF smoothing, LF frames, progressive passes) and Modular (every predictor, MA trees, RCT, palette and delta palette, squeeze), Gaborish and edge-preserving filters, upsampling by 2, 4 and 8, patches, splines, noise, groups in any order | Modular, lossless: groups of 256x256 samples, a reversible color transform and a predictor per image |
| Pixel formats | 8-bit files load as `Gray8`, `Rgb24` or `Rgba32`; deeper files as `Gray16` or `Rgba64`; grayscale with alpha as `Rgba32`/`Rgba64`. Depths other than 8 and 16 are scaled exactly; `ImageInfo.BitsPerComponent` reports the depth of the file | `Gray8`, `Gray16`, `Rgb24`, `Rgba32`, `Bgra32` and `Rgba64`, each at its own depth: nothing is reduced |
| Color | Samples stay in the color space the file declares; an XYB (lossy) image is converted to it. sRGB is untagged; linear sRGB sets `TransferFunction.Linear`; the other enumerated encodings with H.273 code points (Display P3, DCI-P3, BT.2020, BT.709, PQ, HLG) set `Metadata.Cicp`; custom primaries, a custom white point or a power function are described by an ICC profile written for them; an embedded ICC profile is kept byte for byte. HDR samples are returned as stored, never tone mapped | The ICC profile, byte for byte; otherwise `TransferFunction.Linear` and a `Metadata.Cicp` the format can enumerate as its color encoding; otherwise sRGB |
| JPEG recompression | A JPEG file recompressed by `cjxl` is decoded to pixels: YCbCr, subsampled chroma, the quantization tables of the file | Not written |
| Animations | Exact durations (ticks over the tick rate), play count; frames smaller or larger than the image, the blend modes (replace, add, blend, alpha-weighted add, multiply) and the four reference frames are composed. A frame without duration is a layer of the next one, not a frame of the animation | Full frames, 1000 ticks per second (`DurationRounding`), play count; a frame without duration is rejected |
| Orientation | The orientation of the image header is reported as `Metadata.Orientation`, never applied; the EXIF orientation tag is ignored, as the format requires | Stored in the image header |
| Effort | n/a | `Effort` 0 to 9 (default 5): 0 to 2 use prefix codes and fixed choices, 3 and above use ANS entropy coding and search the color transform and the predictor, 7 and above also try the weighted predictor. The pixels never depend on it |
| Limits | Every frame is checked against the frame limits, shown or not; the pixels that are decoded without being shown (reference frames, layers, LF frames, the part of a frame outside the image) count toward `MaxTotalPixels`; decoded planes are charged to the memory budget | At most 1,073,741,824 pixels per side |

`ImageReader` decodes an animation frame by frame; its memory does not depend on the number of frames. A frame is
decoded once all its bytes are available: the encoded size of a frame is buffered, not the file.

## Limitations

Everything below is rejected explicitly (never decoded or encoded approximately) or documented as lossy.

| Area | Behavior |
| --- | --- |
| JPEG decoding | Lossless (SOF3), hierarchical (SOF5–7, SOF13–15, DHP, EXP), arithmetic coding (SOF9–11, DAC), JPEG-LS, 12/16-bit precision, heights defined by DNL, other component counts than 1, 3 or 4, non-integral sampling ratios: `UnsupportedImageFeatureException` (also from `Identify`). CMYK/YCCK (four components): identified, but loaded only with `CmykConversion` (an ICC conversion to RGB; `UnsupportedImageFeatureException` otherwise, and for a file without CMYK profile when no profile is assumed). Scaled and region decoding: not available |
| JPEG encoding | Baseline 8-bit Huffman only: no progressive, arithmetic, lossless, 12-bit, RGB, CMYK, restart markers or optimized Huffman tables. One frame: animated images and posters are rejected (export one with `CloneFrame`); alpha requires `BackgroundColor`; `Rgba64`/`Gray16` require `AllowBitDepthReduction` |
| GIF decoding | Plain-text extensions (they require text rendering) are `UnsupportedImageFeatureException`; other unknown extensions (XMP and ICC application extensions included) are skipped; the background color index is never painted (cleared areas are transparent black) |
| GIF encoding | Lossy by design: at most 256 colors per frame (deterministic quantization, optional dithering), one transparent index (alpha thresholded or flattened, never partial); full-canvas frames (no delta rectangles); comments only (no ICC, EXIF, XMP or resolution); at most 65,535 pixels per side, 655.35 s per frame and 65,536 plays |
| WebP decoding | VP8 interframes and frames that are not shown (never produced by WebP encoders): `UnsupportedImageFeatureException`; the `ANIM` background color is a hint and is never painted (disposed areas are transparent black); metadata of simple-layout files and text/resolution do not exist in WebP |
| WebP encoding | 8-bit RGB(A) only (gray written as RGB, 16-bit after explicit reduction); lossy output is 4:2:0 and not pixel-exact; animations are full-canvas frames (no delta rectangles or blending) and need a seekable destination (no spooling to non-seekable streams); at most 16,383 (lossy) or 16,384 (lossless) pixels per side, 16,777,215 ms per frame, 65,535 plays and 4 GiB per file |
| QOI | 8-bit RGB(A) only (gray written as RGB, 16-bit after explicit reduction); one still image; no metadata; the colorspace field is a label (no linear-to-sRGB conversion exists) |
| BMP | Decoding: no embedded JPEG/PNG payloads, no OS/2 Huffman 1D, no 2-bit depth, no channel wider than 8 bits, no embedded ICC profile; OS/2 bitmap arrays (`BA`) are not recognized; a 40-byte header is always a Windows header (an OS/2 2.x header of that length with Huffman 1D or RLE24 is read as `BI_BITFIELDS` or `BI_JPEG`); run-length streams are read strictly (nothing is clamped, the end-of-bitmap marker is required). Encoding: one still image; Windows headers only; run-length output for 4-bit and 8-bit indexed layouts only, bottom-up; indexed output needs one byte per pixel of working memory; no alpha below 32 bits (no 1-5-5-5 output); resolution is the only metadata; the file must fit the 32-bit `bfSize` field |
| TGA | Decoding: 16-bit grayscale is always an 8-bit gray sample and an attribute byte, so a file that stores a 16-bit luminance is not read as one; premultiplied alpha (attributes type 4) is rejected and no other attributes type changes the decoded representation (an alpha channel the extension area calls absent or undefined is still decoded when the image descriptor declares it); the other fields of the extension area and the developer area are not interpreted; the input is read to its end and the bytes after the image data are buffered; truncation is only detected before the end of the image data. Encoding: true-color and grayscale only, bottom-up, one still image, at most 65,535 pixels per side; no color map, no 15/16-bit output, no metadata |
| Netpbm | Decoding: only the first image of a concatenation is read; tuple types other than the standard grayscale, RGB and alpha forms are rejected; the header is bounded at 65,536 bytes; truncation of a plain raster is not detectable. Encoding: one image, no metadata; the variant follows the pixel format, and a portable bitmap (`P1`/`P4`) is only written on request, from pixels that are exactly black or white (no threshold, no dithering); plain output and portable bitmaps have no alpha (PAM has no plain form) |
| TIFF | Decoding: LZW, PackBits, CCITT fax and JPEG compression, palette, CMYK, YCbCr and L\*a\*b\* photometric interpretations, planar storage, reversed fill order, the floating-point predictor, signed and floating-point samples, sample widths other than 8 and 16 bits, more than one extra sample, and associated (premultiplied) or unspecified extra samples are all rejected with `UnsupportedImageFeatureException`. No EXIF block, no sub-IFD, no unknown-tag round trip. Encoding: one page per `Image.Save` (use `ImageCollection.Save`), strips only, no predictor, no tiles, a seekable destination required |
| ICO/CUR | Decoding: a top-down DIB payload, a DIB whose stored height is not doubled, an animated PNG payload, a representation larger than 256 pixels per side and the DIB variants an icon payload cannot use (run-length encoding, embedded codecs, OS/2 headers) are rejected. Encoding: 32-bit DIB or still PNG payloads only (16-bit pixels need a PNG payload: `IconPayloadFormat.Dib` is rejected for them), at most 256 pixels per side, no metadata; an icon cannot store a hotspot |
| ANI | Decoding: raw bitmap frames (no icon flag), a frame without a representation of the canvas size and everything the ICO/CUR decoder rejects are `UnsupportedImageFeatureException`; the geometry fields and the sequence flag of the header are not used (the payloads and the `seq ` chunk are authoritative); an ICC profile or text inside a PNG frame is not retained; `Image.OpenReader` does not read it. Encoding: one representation per frame, at most 256 pixels per side, whole jiffies, an infinite play count only, a title and an author as the only metadata, a seekable destination, at most 4 GiB per file |
| PNG/APNG | No palette output (paletted inputs are re-encoded as RGB/RGBA), no RGB 16-bit or gray+alpha output layouts (16-bit color is written as RGBA 16), no APNG delta-rectangle optimization, unknown critical chunks rejected, unknown ancillary chunks not round-tripped |
| Metadata | Profiles are preserved and labeled, never applied implicitly (colors are converted only by `ConvertColorProfile` and `IccColorTransform`); EXIF orientation is reported, applied only by `AutoOrient`; extended XMP in JPEG is not supported; per-format storage limits are listed in [Metadata, color profiles and orientation](#metadata-color-profiles-and-orientation); unsupported items follow `MetadataHandling` (`Strict` throws by default) |
| Color management | ICC version 2 and 4 grayscale, RGB and CMYK profiles (matrix-based and table-based): no version 5 (iccMAX), device-link, abstract or named-color profile, no multi-process-element (`D2Bx`/`B2Dx`) tag, no other data color space; `ConvertColorProfile` keeps the pixel format (an RGB image cannot become grayscale or CMYK this way); no CMYK pixel format: a CMYK JPEG is converted to RGB when it is loaded (`CmykConversionOptions`, RGB destination only) and CMYK cannot be written; no gamut mapping beyond per-channel clipping |
| Drawing | No image or pattern brush, no transform, no clipping region other than the canvas, no dashed stroke, no elliptical-arc path element, no blend mode other than source-over; always antialiased; blended on the encoded sample values (no linear-light option); sequential; gradients have no focal point and no rotation of their own; coordinates are limited to ±16,777,216 |
| Processing | No HDR or custom processors; `LinearSrgb` resizing and convolution accept untagged or recognized sRGB/sGray profiles only; convolution matrices have odd dimensions, no bias or divisor, no ready-made matrices and no separable fast path |
| AVIF encoding | An image of a sequence is predicted from the previous image only, with one motion vector per block and the regular interpolation filter: no compound prediction (two references), global, warped or overlapped motion, switchable interpolation filter, temporal motion vector prediction, or scene change detection (an image that differs from the previous one is coded with intra blocks, not as a key frame). Not used: intra block copy (content that repeats in an image, such as the characters of a text, is not predicted from the image itself: a lossless screenshot can be larger than its PNG), the split of the transform blocks of an inter block (it has the largest transform of its size), quantizer deltas, quantization matrices, loop restoration, CDEF strengths per block (one pair per frame), super-resolution and film grain. No grid, layered (progressive) or premultiplied-alpha output; no `clap`, `pasp`, mastering display or content light level box; no orientation for an image sequence. An image sequence of one image cannot store animation settings (it is read back as a still image): a single frame with animation settings is rejected. An image sequence needs a seekable destination and its frame count. The level of the stream is the lowest one whose picture size holds the image; the other limits of a level are not checked |
| AVIF decoding | Rejected with `UnsupportedImageFeatureException`: premultiplied alpha, `iden`/`iovl` derived images, fragmented files, edit lists that do not play the whole track, image sequences whose images change size, clean apertures that do not select whole samples or that follow a rotation, grid tiles that declare other transformations than their grid, large scale tile streams, and the other matrix coefficients (constant luminance, ICtCp, SMPTE 2085, chromaticity-derived). Not read: gain maps, depth maps, thumbnails, the images that are not the primary item, the `tkhd` matrix and the other transformations of an image sequence (its images are returned as stored), mastering display and content light level boxes. A layered image is returned at its full quality only: its lower layers are not exposed as a progressive rendering. HDR samples are returned as stored: no tone mapping |
| JPEG XL | Decoding, rejected with `UnsupportedImageFeatureException` (also from `Identify` when the image header tells): floating-point samples and samples of more than 16 bits, premultiplied alpha, CMYK (a black channel) and spot colors, an XYB image whose ICC profile is not sRGB (it needs a color conversion to that profile), the PQ or HLG transfer function with primaries or a white point that CICP cannot express, a Modular frame stored as YCbCr with subsampled channels, restoration filters on a grayscale Modular frame, an upsampled frame with patches or splines whose extra channels have another resolution. Ignored: the preview frame, frame names, the intrinsic size, the JPEG reconstruction data (`jbrd`: a recompressed JPEG file is decoded to pixels, the JPEG file is not rebuilt), `jumb` and unknown boxes; depth, selection mask, thermal and the other extra channels than alpha are decoded and dropped. Samples of 1 to 16 bits other than 8 and 16 are scaled exactly to 8 or 16 bits. A lossy frame is decoded in floating point: the result is within a few sixteen-bit units of the reference decoders, not bit-exact. Encoding: lossless only (no lossy output), full frames that replace the image (no layers, blending, patches or cropped frames), frame durations in whole milliseconds, no frame of an animation without duration; an EXIF orientation is written to the image header |
| Text | Horizontal text only; no shaping beyond the lookups of the font (Arabic joining, Indic and Southeast Asian reordering) and no dictionary line breaking for the scripts that need one; no language-specific forms; greedy line breaking; no AAT (`morx`, `kerx`) shaping, no device adjustment of positions (hinting); no hinting and no subpixel (LCD) rendering; no system font: every font is given by the caller; along a path, glyphs are turned but not warped; see [Text](#text) |
| Fonts | No system font, no SVG glyph, no monochrome bitmap glyph, no Type 1 font, no hinting; variable fonts without version 2 of `avar`; see [Fonts](#fonts) |
| API | No public allocator, custom pixel type, codec plug-in or image-processor interface; implementation types are internal |
