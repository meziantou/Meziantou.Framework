# Meziantou.Framework.Imaging

A fully managed, performance-oriented image library for .NET 10 and .NET 11: PNG, animated PNG (APNG), GIF, JPEG, WebP
(still and animated, lossless and lossy), QOI, BMP, TGA, Netpbm (PBM/PGM/PPM/PAM), TIFF/BigTIFF, ICO/CUR and animated
cursor (ANI), AVIF and HEIC (still and animated, lossless and lossy), JPEG XL (still and animated, lossless and
lossy, progressive), JPEG 2000 (lossless and lossy) and OpenEXR (high dynamic range, floating-point samples) decoding
and encoding, lossless JPEG recompression, animation-aware processing, vector drawing, ICC
color conversion, and bounded-memory streaming.

- Static PNG, animated PNG (APNG), GIF, baseline/progressive/lossless JPEG, WebP (still and animated, lossless and lossy), QOI,
  BMP, TGA, Netpbm (PBM/PGM/PPM/PAM), TIFF/BigTIFF, ICO/CUR and ANI decoding; PNG, APNG, GIF, baseline and progressive
  JPEG, WebP (lossless and lossy, still and animated), QOI, BMP, TGA, Netpbm, TIFF/BigTIFF, ICO/CUR and ANI encoding (unsupported
  modes, such as hierarchical JPEG, embedded JPEG or PNG in BMP, premultiplied-alpha TGA or planar TIFF, are rejected
  explicitly; a CMYK JPEG or TIFF is loaded with an explicit color conversion)
- JPEG XL decoding and encoding, fully managed: lossy (VarDCT) and lossless (Modular) files, alpha, animations and
  layers; lossless output of every pixel format at its own depth, lossy output controlled by a quality, progressive
  output, and progressive reading (the renderings of a frame before it is complete)
- JPEG files recompressed as JPEG XL files without decoding their pixels, about a fifth smaller, and rebuilt byte for
  byte (`JpegXlTranscoder`)
- JPEG 2000 decoding and encoding with a fully managed codec: JP2 files and bare codestreams, the reversible (5-3) and
  irreversible (9-7) wavelet transformations, every coding tool of Rec. ITU-T T.800 | ISO/IEC 15444-1, up to 16 bits,
  opacity, palettes, sYCC; the high-throughput block coder (HTJ2K, Rec. ITU-T T.814 | ISO/IEC 15444-15) is decoded;
  lossless and lossy encoding of every pixel format
- OpenEXR decoding and encoding with a fully managed codec: scan-line and tiled files, HALF, FLOAT and UINT samples,
  the NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44 and B44A compressions, loaded as `RgbaHalf` or `RgbaFloat` pixels without
  any conversion; lossless output of half and float samples (NONE, RLE, ZIPS, ZIP, PIZ)
- AVIF decoding and encoding with a fully managed AV1 decoder and a fully managed AV1 encoder: still images and
  animations, lossless and lossy, 8, 10 and 12 bits, alpha; grids and film grain are decoded
- HEIC decoding and encoding with a fully managed HEVC (H.265) decoder and a fully managed HEVC encoder: the photos of
  phones and cameras (grids, rotation, alpha, 10 bits), image sequences with reordered pictures, lossless and lossy
  output, 8 to 12 bits
- `ImageCollection` for the images that are not animation frames: TIFF document pages and icon/cursor representations,
  read and written one entry at a time
- Animation-aware model: every frame is a full-canvas displayed image with exact rational timing and, for cursors, a
  hotspot that follows its pixel through geometry operations
- Fourteen integer working pixel formats (`Rgba32`, `Bgra32`, `Rgb24`, `Rgba64`, `Gray8`, `Gray16`, the gray-with-alpha
  `La16` and `La32`, the 16-bit `Rgb48`, the `A8` alpha mask, and `Argb32`, `Abgr32`, `Bgr565` and `Rgba1010102` to
  exchange buffers with GPU and display APIs) with 16-bit precision preserved, and two floating-point ones (`RgbaFloat`,
  `RgbaHalf`) for scene-referred and high dynamic range data
- Crop, auto-crop (background detection), resize (alpha-aware, Contain/Cover/Stretch/Pad/BoxPad), padding and canvas
  extension, rotate (quarter turns or any angle), skew, affine and perspective transformations, auto-orient, flip, grayscale, Gaussian, box and median blurs, unsharp masking, convolution matrices (ready-made sharpen, emboss and edge matrices, a bias for signed results, separable matrices in two passes) and edge detection (Sobel, Scharr, Prewitt, Kirsch, Robinson, Laplacians) applied to all frames
- Antialiased vector drawing on a frame or on every frame: filled and stroked shapes and paths with Bézier curves,
  arcs and curves through points, dashed strokes, strokes on one side of their path and arrow heads, solid colors,
  linear, radial and sweep gradients, images (also in perspective), patterns, clips, masks, layers, shadows, blurs,
  blend modes, compositing operators and transformations of the canvas, with exact pixel coverage; hard edges and
  compositing in linear light on request
- Paths that are read from and written to SVG path data, combined (union, intersection, difference), simplified,
  reversed, cut and measured
- Text drawn from font files with the same rasterizer, measured by the layout that is drawn, with the positions of
  its characters (hit testing, carets, selections)
- Font files read without any system library: TrueType and OpenType (TrueType and CFF outlines), font collections,
  WOFF and WOFF2, with their names, metrics, character map and glyph outlines
- ICC color management on request: convert an image to another color profile, or convert sample buffers between two
  profiles: matrix- and table-based grayscale, RGB and CMYK profiles of versions 2 and 4, the four rendering intents
  and black point compensation (profiles are never applied implicitly)
- Bounded-memory sequential readers and writers for long animations
- Explicit policies for alpha, precision, metadata and color-profile losses; configurable resource limits

This document is the user guide: it describes what the library guarantees to its callers. Every C# example below is
copied from code that compiles, and most of them also run in the test suite.

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
  - [Comparing images](#comparing-images)
  - [Drawing](#drawing)
  - [Fonts](#fonts)
  - [Text](#text)
  - [Metadata, color profiles and orientation](#metadata-color-profiles-and-orientation)
  - [Color management](#color-management)
  - [Color values](#color-values)
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
  - [HEIC](#heic)
  - [ANI](#ani)
  - [JPEG XL](#jpeg-xl)
  - [JPEG 2000](#jpeg-2000)
  - [OpenEXR](#openexr)
- [Limitations](#limitations)

## Quick start

Namespaces: `Meziantou.Framework.Imaging` (images, I/O, processing, configuration), `Meziantou.Framework.Imaging.Formats`
(encoders and their settings), `Meziantou.Framework.Imaging.Metadata`, `Meziantou.Framework.Imaging.Drawing` (brushes,
pens and paths), `Meziantou.Framework.Imaging.Typography` (fonts). The examples of this README omit the `using` directives: the first one only needs
`using Meziantou.Framework.Imaging;`.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/PackageReadmeSnippets.cs;region=Imaging_QuickStart;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = Image.Load("animation.gif");
image.Resize(new ResizeOptions(320, 240));
image.Save("thumbnail.gif");
```
<!-- endref -->

The output format follows the extension, and the timing and play count of the animation are kept. An explicit encoder
chooses the format and its settings (the encoders are in `Meziantou.Framework.Imaging.Formats`); formats that store one
still image require the frame to be exported explicitly:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/ReadmeSnippets.cs;region=Imaging_QuickStartWithEncoders;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = Image.Load("animation.gif");          // every displayed frame, exact timing
image.Resize(new ResizeOptions(320, 240));               // all frames, atomically
image.Save("thumbnail.png", new PngEncoder());           // APNG, because the image is animated

image.Frames[0].Save("first.jpg",                         // JPEG stores one frame: export it explicitly
    new JpegEncoder { Quality = 85, BackgroundColor = new Rgba32(255, 255, 255) });
```
<!-- endref -->

## Design highlights

- **Full displayed frames.** An image is one or more full-canvas frames sharing a size and pixel type; encoded delta
  rectangles, blending and disposal are codec internals. APNG poster frames are kept separately.
- **Pages and representations are not frames.** The pages of a TIFF document and the sizes inside an icon live in a
  separate `ImageCollection`, whose entries may differ in size and pixel format and never carry timing or looping.
- **Explicit ownership.** `Image`/`Image<TPixel>` are disposable owners; frames are borrowed views; clones, extracted
  frames and reader results are independent owned images. Eager loads never retain their input.
- **Precision and alpha.** Sixteen working pixel formats (`Rgba32`, `Bgra32`, `Rgb24`, `Rgba64`, `Gray8`, `Gray16`,
  `La16`, `La32`, `Rgb48`, `A8`, `Argb32`, `Abgr32`, `Bgr565`, `Rgba1010102`, and the floating-point `RgbaFloat` and
  `RgbaHalf`), 16-bit precision preserved, straight alpha, alpha-aware resampling, and no
  silent alpha/precision/range/metadata/animation loss.
- **Exact timing.** Rational `FrameDuration` preserves GIF, APNG, WebP and ANI delays exactly; `TotalPlays` counts total plays.
- **Atomic geometry.** Crop, auto-crop, resize, pad, extend canvas, rotate, skew, transform and auto-orient apply to every frame (and the
  poster) transactionally.
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
| Dependencies | Two packages of the same repository: `Meziantou.Framework.Unicode` (the Unicode algorithms of text layout, fully managed, no ICU) and `Meziantou.Framework.Fonts` (the font reader and the shaper, shared with `Meziantou.Framework.WebPlatform`); no native dependency |
| Trimming / NativeAOT | Supported: the library is marked trimmable and AOT-compatible (analyzers on every build), and a NativeAOT smoke app (every format, streaming, processing, errors; trim/AOT warnings as errors) is published and run for both frameworks |

## Supported formats

Detection is content-based, never by extension. "Not supported" features are recognized and rejected explicitly, never
decoded or encoded approximately; see [Format reference](#format-reference) and [Limitations](#limitations).

| Format | Decoding | Encoding | `Save(path)` extensions |
| --- | --- | --- | --- |
| PNG / APNG | Every color type and bit depth, palettes, Adam7, APNG animations and poster frames, Apple CgBI (iOS-optimized) files | Gray, gray+alpha, RGB and RGBA at 8/16 bits, exact palettes at 1/2/4/8 bits, Adam7, APNG with full-canvas frames and posters; sub-byte grayscale and APNG delta rectangles with `PngOptimization.MinimumSize` | `.png` (APNG when animated), `.apng` |
| GIF | GIF87a/89a, animations, interlacing, transparency, comments | GIF89a, deterministic quantization per frame or to a shared palette, optional dithering, animations with unknown frame counts | `.gif` |
| JPEG | Baseline, extended sequential and progressive, Huffman or arithmetic coded, 8-bit or 12-bit samples; lossless (Huffman coded), 2-bit to 16-bit samples; gray, YCbCr, RGB, and CMYK/YCCK with a color conversion; any integral sampling | Baseline or progressive 8-bit Huffman, gray or YCbCr, 4:4:4/4:2:2/4:2:0 | `.jpg`, `.jpeg` |
| WebP | Lossless, lossy, alpha, animations, ICC/EXIF/XMP | Lossless and lossy, alpha, animations (seekable destination) | `.webp` (lossless by default) |
| QOI | Every chunk form, 3 or 4 channels, colorspace label | Lossless 8-bit RGB/RGBA | `.qoi` |
| BMP | Windows headers up to V5 and OS/2 1.x/2.x headers, 1/4/8/16/24/32-bit, bit fields, RLE4/RLE8/RLE24 | 24-bit or 32-bit with an alpha mask by default; 1/4/8-bit indexed, RLE4/RLE8, 16-bit and top-down on request | `.bmp`, `.dib` |
| TGA | True-color, grayscale and color-mapped, uncompressed or run-length | True-color and grayscale, uncompressed or run-length | `.tga`, `.icb`, `.vda`, `.vst` |
| Netpbm | `P1` to `P7` (PBM, PGM, PPM, PAM), 8 and 16 bits | `P1` to `P7`, lossless (PBM on request, from black and white pixels) | `.pnm`, `.pam`, `.ppm`, `.pgm`, `.pbm` |
| TIFF / BigTIFF | Both byte orders and offset sizes; bilevel, 2/4/8/16-bit gray, gray+alpha, RGB, RGBA, palette, YCbCr, and CMYK with a color conversion; signed 8/16-bit and floating-point 16/24/32-bit gray and RGB(A) samples; strips and tiles; Deflate, LZW, PackBits, CCITT (modified Huffman, Group 3, Group 4), JPEG and JPEG 2000; horizontal and floating-point predictors; multi-page documents | Strips; uncompressed, Deflate, LZW, PackBits, CCITT (bilevel) or JPEG; gray, RGB, RGBA, bilevel or palette; 32-bit floating-point gray and RGB(A) with the floating-point predictor; classic or BigTIFF; multi-page documents (seekable destination) | `.tif`, `.tiff` |
| ICO / CUR | PNG and DIB representations, cursor hotspots | PNG or 32-bit DIB representations, cursor hotspots | `.ico`, `.cur` |
| AVIF | Still images and image sequences (animations); 8, 10 and 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome; alpha, straight or premultiplied; grids; film grain; clean aperture and orientation; ICC/EXIF/XMP, CICP color descriptions, light levels, mastering display and pixel aspect ratio | Lossless and lossy still images and image sequences (seekable destination); 8, 10 and 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome; lossless alpha, straight or premultiplied; grids; clean aperture and orientation; ICC/EXIF/XMP, CICP color descriptions, light levels, mastering display and pixel aspect ratio | `.avif`, `.avifs` (lossless by default) |
| HEIC | Still images and image sequences (animations); the HEVC version 1 and range extensions profiles: 8 to 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome; intra and inter pictures (P and B, reordered); alpha; grids; ICC/EXIF/XMP and CICP color descriptions | Lossless and lossy still images and image sequences (seekable destination); 8, 10 and 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome; lossless alpha; grids; ICC/EXIF/XMP, CICP color descriptions and orientation | `.heic`, `.heif`, `.heics`, `.heifs` (lossless by default) |
| JPEG XL | Bare codestreams and containers; lossy (VarDCT) and lossless (Modular) frames; up to 16 bits; alpha; animations and layers; patches, splines, noise; recompressed JPEG files; ICC/EXIF/XMP and enumerated color encodings; progressive renderings | Lossless (default) or lossy, every pixel format at its own depth, lossless alpha, animations with any frame count, progressive output, ICC/EXIF/XMP, orientation; JPEG files recompressed without loss (`JpegXlTranscoder`) | `.jxl` |
| JPEG 2000 | JP2 files (also JPX and JPH files of one codestream) and bare codestreams; reversible and irreversible wavelet transformations; tiles, layers, every progression order and code-block style, regions of interest; the high-throughput block coder (HTJ2K); 1 to 16 bits; opacity, palettes, sYCC, CMYK with a color conversion; ICC/EXIF/XMP and resolution | Lossless and lossy, every pixel format at its own depth, lossless opacity, a JP2 file or a codestream, ICC/EXIF/XMP and resolution | `.jp2` (lossless), `.j2k`, `.j2c`, `.jpc` (a codestream) |
| OpenEXR | Single-part scan-line and tiled files (the full-resolution level of mipmaps and ripmaps); HALF, FLOAT and UINT samples; R, G, B, A and Y (luminance) channels; NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44 and B44A; data and display windows; chromaticities | Single-part scan-line files of half or float samples, lossless: NONE, RLE, ZIPS, ZIP (default) or PIZ (seekable destination when compressed); chromaticities | `.exr` |
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
  saving the first frame; save a frame explicitly with `Frames[i].Save` (or `PosterFrame.Save`), or extract it with
  `CloneFrame` to edit it apart from the animation.

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

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_BuildAnimation;format=md-fenced-code-block;dedent=true -->
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
    animation.Save(gifPath, new GifEncoder { Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } });
}
```
<!-- endref -->

**Animated cursors**. An ANI file is an animation whose frames are cursor images: each frame has
its own hotspot, durations are whole sixtieths of a second, and the animation always loops.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_AnimatedCursor;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

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
  the frame of that image. The entries of a loaded collection can be decoded from several threads at once: a file or
  in-memory collection reads at an offset, and a stream collection serializes its reads of the stream. Disposing the
  collection during a decode waits for the read in progress, and the decode then completes or throws
  `ObjectDisposedException`. Editing and saving a collection are not thread-safe.
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

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TiffPages;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_IcoRepresentations;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

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

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TypedRows;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TypedRowsState;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_RawPixels;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

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
by default (`ImageConfiguration.MaxDegreeOfParallelism = 1`); with more workers, only large resizes, transformations,
convolutions, blurs, medians, unsharp masks, the `Pixelate`, `OilPaint` and `Flatten` effects and the `ImageSimilarity`
comparisons (SSIM, PSNR, SSIMULACRA 2, Butteraugli) are split into row bands, the tiles of large HEIC and AVIF grids and
the post-processing filters of large AVIF images (by row bands) are decoded concurrently, the tiles of large HEIC and
AVIF grids (`HeicEncoder.GridTileSize`, `AvifEncoder.GridTileSize`) are encoded concurrently, and the groups of large
JPEG XL frames are encoded and decoded concurrently, with results identical to the sequential ones (the same pixels, the
same encoded bytes). A worker writes only its own rows, and the floating-point sums of the comparisons are made by bands
of 64 rows whatever the number of workers, added in order.

### Failure atomicity



- **Geometry is atomic.** `Crop`, `AutoCrop`, `Resize`, `Pad`, `ExtendCanvas`, `Rotate`, `Skew`, `Transform` and `AutoOrient` apply to every
  frame and the poster in one transaction: replacements are allocated (and charged) while the originals are live, and any failure before the commit
  (limit, allocation failure, cancellation) leaves dimensions, pixels, frame identities and order, cursor hotspots and
  metadata unchanged.
- **Pixel mutations may be partial.** `Flip`, `Grayscale`, `Convolve` (and the blurs and the sharpening), `Equalize`,
  `AutoLevels`, `Draw`, row callbacks and `ReadFrameInto` write in place: a failure or a cancellation can leave some rows
  or frames updated, but the image stays structurally valid and disposable. `Convolve`, `GaussianBlur`, `BoxBlur`,
  `MedianBlur`, `Sharpen`, `Equalize` and `AutoLevels` rent their working memory first: exceeding the allocation limit
  leaves the image unchanged. `Flip`
  moves the hotspot of a frame once that frame is completely mirrored. In a `Draw` callback, the shapes already drawn
  stay; each shape rents its working storage before it changes a pixel, and a canceled shape leaves the same rows
  painted on every frame.
- Failed operations release every resource they acquired; nothing is leaked or returned to a pool while still visible.

### Pixel formats, precision and alpha

**Working formats**. Sixteen pixel types: `Rgba32`, `Bgra32`, `Rgb24`, `Rgba64`, `Gray8`,
`Gray16`, `La16`, `La32`, `Rgb48`, `A8`, `Argb32`, `Abgr32`, `Bgr565`, `Rgba1010102`, `RgbaFloat` and `RgbaHalf`
(sequential `Pack = 1` layouts, straight alpha, 16-bit components and packed values in native endianness; `PixelFormats` describes them, `PixelFormats.IsFloatingPoint` tells the floating-point ones and
`PixelFormats.HasColor` the alpha mask). Untyped `Load` picks the smallest format that preserves the source: for example 16-bit PNGs load as `Gray16`/`Rgb48`/`Rgba64`,
gray with alpha (or with a `tRNS` key) as `La16`/`La32`, palettes and color transparency as `Rgba32`, JPEG as `Gray8`/`Rgb24` (`Gray16`/`Rgb48` for samples that do not have 8 bits), GIF as `Rgba32` (`ImageInfo.PixelFormat` tells it before
decoding). Typed `Load<TPixel>` and `CloneAs<TPixel>` convert explicitly between any two formats: 8 to 16 bits is exact
(`v * 257`), 16 to 8 bits rounds to nearest, color to gray uses Rec. 709 luma on the encoded values. A 16-bit path never
goes through 8 bits.

**Compact formats**. `La16` (an 8-bit gray level and alpha, 2 bytes per pixel) and `La32` (16-bit, 4 bytes) hold gray
images with transparency in half the memory of `Rgba32`/`Rgba64`, `Rgb48` (three 16-bit samples, 6 bytes) holds 16-bit
color without alpha in three quarters of the memory of `Rgba64`, and `A8` (1 byte) is an alpha mask: it has no color,
reads as black with its alpha, and a conversion to it keeps the alpha of the source and drops its color (and its ICC
profile, which describes no sample of a mask). Converting gray pixels to RGBA and back, opaque `Rgb48` pixels to `Rgba64`
and back, and alpha masks to any format with alpha and back are exact.

> **Behavior change.** Before these formats existed, gray images with alpha (PNG color type 4 and gray with a `tRNS`
> key, TIFF, Netpbm, TGA, JPEG XL, JPEG 2000, AVIF and HEIC gray with alpha) loaded as `Rgba32`/`Rgba64`, and 16-bit
> (or 10-bit and 12-bit) color without alpha as opaque `Rgba64`. Untyped `Load` and `ImageInfo.PixelFormat` now give
> `La16`/`La32` and `Rgb48`; the pixels are the same. Code that casts an untyped image to `Image<Rgba32>` or
> `Image<Rgba64>` should load with `Load<Rgba32>`/`Load<Rgba64>` instead. A gray ICC profile of such a file is now kept,
> where it could not label the RGBA pixels of before. Animations keep `Rgba32`/`Rgba64`.

Every processing operation and the drawing canvas accept the compact formats, except `Flatten` on an `A8` mask, which
has no color to hold the result (`NotSupportedException`). `Rgb48` has kernels of its own; the filtering operations
(resize, transform, convolution, blurs, median) and the `Pixelate`, `OilPaint` and `Flatten` effects process `La16`,
`La32` and `A8` through a temporary `Rgba32`/`Rgba64` copy of each frame, charged to the image, which gives back the
same gray levels and alpha (a flattened gray with alpha gets the luma of its flattened colors, as its conversion to
`Gray8`/`Gray16` does); drawing composites them the same way, a run of pixels at a time. `PngEncoder` and `TiffEncoder` store `La16`, `La32` and
`Rgb48` as they are (gray with alpha, 16-bit RGB) and an alpha mask as black gray with alpha; the other encoders get a
copy of the frames widened without loss to `Rgba32` or `Rgba64`, charged to the writer, and follow their own rules for
those formats (for example `JpegEncoder` needs a `BackgroundColor` for pixels that are not opaque).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_CompactFormats;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<A8> ExtractAlphaMask(string inputPath, string maskPath)
{
    // Untyped loads pick the smallest lossless format: La16 for a gray image with alpha (2 bytes per pixel instead of
    // the 4 of Rgba32), Rgb48 for 16-bit color without alpha
    using var image = Image.Load(inputPath);

    // An alpha mask keeps the alpha of the image in 1 byte per pixel; its pixels read as black
    var mask = image.CloneAs<A8>();

    // PNG stores the mask as gray with alpha, which loads back as La16
    mask.Save(maskPath, new PngEncoder());
    return mask;
}
```
<!-- endref -->

**Interoperability formats**. `Argb32` and `Abgr32` store the 8-bit components of `Rgba32` in the byte orders that
some APIs expect (alpha first), `Bgr565` packs an opaque color in 16 bits (5-bit red and blue, 6-bit green, blue in the
low bits: `DXGI_FORMAT_B5G6R5_UNORM`, Android `RGB_565`) and `Rgba1010102` packs 10-bit colors and a 2-bit alpha in 32
bits (red in the low bits: `DXGI_FORMAT_R10G10B10A2_UNORM`, Android `RGBA_1010102`), so that such buffers can be
imported and exported without a copy in another layout. A packed component is the fraction of its largest value:
conversions round it to the nearest sample of the destination and back to the nearest component, so `Bgr565` goes to
any format of 8 bits or more and back exactly, `Rgba1010102` to any 16-bit format and back exactly, and a conversion
to them reduces the precision (`PixelFormats.GetBitsPerComponent` gives their largest component: 6 and 10). Every
processing operation, the drawing canvas and every encoder accept them: the library works on their working format
(`Rgba32` for the byte orders, `Rgb24` for `Bgr565`, `Rgba64` for `Rgba1010102`), in a temporary copy of each frame
charged to the image, and stores the result back, so `Argb32` and `Abgr32` give the results of `Rgba32` and a packed
format those of its working format rounded once to its components (an image of these formats drawn on another one is
read like any other: a packed component as its nearest 16-bit sample). Encoders get a copy widened to that format
without loss.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_InteropFormats;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgba1010102> ConvertDisplayBuffer(ReadOnlySpan<Bgr565> frameBuffer, int width, int height)
{
    // Bgr565 is the 16-bit layout of display buffers (blue in the low bits): the pixels are copied as they are
    using var image = Image.ImportPixelData<Bgr565>(frameBuffer, width, height);

    // Every operation works on it like on Rgb24 and stores the result back in 5, 6 and 5 bits
    image.Flip(FlipMode.Horizontal);

    // The components convert to the nearest samples of any other format, here 10-bit colors and a 2-bit alpha
    return image.CloneAs<Rgba1010102>();
}
```
<!-- endref -->

**Floating-point formats**. `RgbaFloat` (four `float`, 16 bytes per pixel) and `RgbaHalf` (four `Half`, 8 bytes) hold
linear light where 1 is the reference white of a standard dynamic range: a color component may be above 1 (a highlight)
or negative (outside the gamut of the primaries), and alpha is a coverage from 0 to 1. A new image of these types is
labeled `ColorTransferFunction.Linear`; a conversion never converts colors, so `CloneAs` and typed loads keep the label of
their source (an sRGB `Rgba64` image becomes sRGB-encoded floats). OpenEXR files and the floating-point pages of TIFF
decode to them: OpenEXR files load as `RgbaHalf` or `RgbaFloat` in linear light, and untyped `Load` of a TIFF page picks
`RgbaHalf` for half-precision samples and `RgbaFloat` for the others, labeled linear light unless an ICC profile
describes them. `OpenExrEncoder` stores them as they are, and so does `TiffEncoder` (as 32-bit floats); for every other
file, `Load<RgbaFloat>` converts the decoded samples.

| Conversion | Rule |
| --- | --- |
| Integer to `RgbaFloat` | `v / max`, one correctly rounded `float` division: exact, and back to the same integer for every sample |
| Integer to `RgbaHalf` | The nearest `Half` of `v / max`: exact back for 8-bit samples, not for 16-bit ones (11 significant bits) |
| `RgbaHalf` to `RgbaFloat` | Exact, infinities and samples that are not numbers included |
| `RgbaFloat` to `RgbaHalf` | Nearest (ties to even); infinities and samples that are not numbers are kept; a finite sample that rounds beyond ±65,504 (the largest half) is rejected unless `OutOfRangeSamples` is `Clip`, which gives it ±65,504 |
| Floating point to integer | A sample that is not a number is always rejected. The colors are tone mapped when `PixelConversionOptions.ToneMapping` is set; a sample (alpha included) still outside [0, 1] is rejected unless `OutOfRangeSamples` is `Clip`, which clips each one (infinities included). Then alpha is removed as for integer pixels (an alpha of exactly 1 is opaque), gray gets the Rec. 709 luma `0.2126 R + 0.7152 G + 0.0722 B`, and each sample is `floor(v max + 1/2)`, in double precision |

Every check runs on every frame and the poster before anything is allocated. Tone mapping treats the color components as
linear light in units of the white: the curve of `HdrToneMappingOptions.Operator` is applied to the largest of the three
and they are scaled together, which keeps the hue; `TargetWhiteLuminance` is the luminance of a component of 1, and the
peak of the source is `SourcePeakLuminance` over that white, or else the largest finite color component of the image
(at least 1). The label of the image does not change.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_FloatingPoint;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgba64> ToneMapHighlights(Image<RgbaFloat> hdr, string pngPath)
{
    // Integer pixels cannot store the highlights: compress them with a tone mapping, or clip them
    var toneMapped = hdr.CloneAs<Rgba64>(new PixelConversionOptions
    {
        ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Reinhard },
    });

    // Encoders store integers too: the encoder says how to reduce the samples
    hdr.Save(pngPath, new PngEncoder
    {
        FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip },
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
    return toneMapped;
}
```
<!-- endref -->

**Colors**. `Colors` has the named colors of CSS as `Rgba32` values (`Colors.CornflowerBlue`, `Colors.Transparent`), and
`Rgba32.ParseHex` / `TryParseHex` read the hexadecimal notations of CSS (`#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`, with or
without the `#`), which `ToHexString` writes back. An `Rgba32` converts implicitly to an `Rgba64`.

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
| 16-bit precision, when encoding AVIF or HEIC (at most 12 bits) | `BitDepth` (10 or 12) of `AvifEncoder` or `HeicEncoder`, or `AllowBitDepthReduction` (12 bits) |
| 8-bit precision, when encoding 16-bit BMP (`Bgr565`, `Bgr555`: 5 or 6 bits per channel) | `BmpEncoder.AllowBitDepthReduction` |
| 16-bit precision, when loading or converting | Request the narrower type explicitly (`Load<Rgba32>`, `CloneAs<Gray8>`) |
| Floating-point samples outside the range of the destination (outside [0, 1] for integers, beyond ±65,504 for `RgbaHalf`) | `PixelConversionOptions.OutOfRangeSamples` (`Clip`), or `PixelConversionOptions.ToneMapping` for the highlights; samples that are not numbers are always rejected |
| Floating-point samples, when encoding a format that does not store them (all but OpenEXR and TIFF, and the TIFF palette, bilevel and JPEG settings) | `ImageEncoder.FloatingPointConversion`: the options of the reduction to `Rgba64`, after which the rules of the encoder apply (`AllowBitDepthReduction` for an 8-bit format) |
| Float and 16-bit precision, when encoding OpenEXR half samples | `OpenExrEncoder.AllowBitDepthReduction` (with `PixelType = Half`); samples beyond ±65,504 are rejected, after the multiplication of the colors by alpha |
| sRGB-encoded samples, an ICC profile or a non-linear CICP description, when encoding OpenEXR (it stores linear light only) | `ImageEncoder.MetadataHandling` (the samples are written as they are), or convert the image to linear light first |
| Colors beyond 256 per frame, when encoding GIF | Lossy by design: deterministic quantization (`GifEncoder.Quantization`: `MaxColors`, a fixed `Palette`, `Dithering`) |
| Colors beyond the palette, when encoding indexed BMP (2, 16 or 256 entries, `MaxColors`, or a fixed palette) | `BmpEncoder.AllowColorReduction` (the same deterministic quantization, `BmpEncoder.Quantization`); an image whose colors fit is always stored exactly |
| Colors beyond 256 (or the fixed palette), or 16-bit samples that 8 bits cannot represent, when encoding indexed PNG (`PngPixelLayout.Indexed`) | `PngEncoder.AllowColorReduction` (the same deterministic quantization, `PngEncoder.Quantization`); without it the image is rejected, never quantized, dithered or rounded |
| Colors beyond a palette, on request | `Quantize` (see [Processing](#processing)) |
| A color profile that cannot label the converted pixels | `PixelConversionOptions.DiscardIncompatibleColorProfile` |
| Metadata the output format cannot store | `ImageEncoder.MetadataHandling` (see [Metadata](#metadata-color-profiles-and-orientation)) |

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertPrecision;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ExportJpegFrame;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
{
    using var animation = Image.Load(animatedPath);

    // The frame is saved as a still image, without copying its pixels
    animation.Frames[frameIndex].Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        BackgroundColor = new Rgba32(255, 255, 255),
        AllowBitDepthReduction = true,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

### Processing

In-place extension methods (`ImageProcessingExtensions`), applied to every frame and the poster:
`Crop` (an in-canvas rectangle, never clamped), `AutoCrop` (the detected content), `Resize`, `Pad` (a border of a given
width on each side), `ExtendCanvas` (a larger canvas, with the image at one of nine anchors), `Rotate` (exact 90/180/270
permutations, or any angle), `Skew` and `Transform` (an affine or a perspective transformation), `Flip`, `AutoOrient` (all eight EXIF orientations), `Grayscale` (Rec. 709 at storage precision, ties upward; keeps the storage format and
alpha, and rejects incompatible ICC profiles; use `CloneAs<Gray8>` to change the storage), `ApplyColorMatrix` (a new
color for each pixel from its own one: brightness, contrast, saturation, a tint, photo presets, a color blindness
preview), `ReplaceColor` (a new color for the pixels of one color), `Vignette`, `Glow`, `Pixelate`, `OilPaint` and
`Flatten` (see [Effects](#effects)), `Convolve` (a convolution
matrix), `GaussianBlur`, `BoxBlur`, `MedianBlur`, `Sharpen` (an unsharp mask), `DetectEdges`, `Equalize` and `AutoLevels` (histogram
equalization, CLAHE and contrast stretch, see [Histograms, equalization and auto-levels](#histograms-equalization-and-auto-levels)),
`Quantize` (a reduction to a palette, see [Color reduction](#color-reduction)) and `Threshold` (black and white, with a
global or an adaptive threshold, see [Binarization](#binarization)). `Flip`, `Grayscale`, `ApplyColorMatrix`,
`ReplaceColor`, the effects, `Convolve`, the blurs, `Sharpen`, `DetectEdges`, `Equalize`, `AutoLevels`, `Quantize` and `Threshold` also exist for one frame. Geometry changes preserve 16-bit precision and alpha, and reconcile the EXIF dimensions,
orientation and thumbnail. `ConvertColorProfile` converts the colors to another ICC profile
(see [Color management](#color-management)).

**Floating-point pixels**. `RgbaFloat` and `RgbaHalf` images accept every processing operation. Crops, rotations, flips,
auto-orient and nearest-neighbor resizes and transformations move the stored bits unchanged, NaN payloads included.
Resize, transformations, convolution and blur use the double-precision filters of the integer formats, with the sample values as working values premultiplied by
alpha: colors are rounded to the nearest float or half but never clamped, so highlights above 1 and negative lobes
survive (a half beyond 65,504 becomes infinite); alpha is clamped to [0, 1], and samples that are not numbers spread to
the pixels whose filters read them. In the `LinearSrgb` working spaces, an image labeled as linear light (the label of a
new floating-point image) is filtered as stored; with another label, every sample must be a number in [0, 1], the
domain of its transfer function, or the operation fails before anything changes, and the filtered light is clipped to
[0, 1] before it is encoded again, as for the integer formats (the ringing of a sharp filter beyond the range is lost).
`Grayscale` stores the Rec. 709 luma of
the stored values, unclamped. `ConvertColorProfile` converts the samples without reducing them to 16 bits, and rejects
colors outside [0, 1] or not numbers, since a profile describes only that range. The auto-crop analysis compares the
values as stored and rejects non-finite samples; the typed `BackgroundColor` of its result is exact, the `Rgba64` one is
clipped. Image comparisons round samples in [0, 1] to 8 bits and reject any other value instead of clipping it.

**Cursor hotspots**. A hotspot (`Frames[i].Metadata.Hotspot`) follows the pixel it designates:
`Crop`, `AutoCrop`, `Pad` and `ExtendCanvas` translate it, `Rotate`, `Flip` and `AutoOrient` permute it exactly, and `Resize` moves it to
`floor(x * newWidth / width)` (and likewise vertically, through the kept region for `Cover`, then by the padding for
`Pad` and `BoxPad`), which is how Windows
scales a cursor: a hotspot in the top-left corner stays there. A rotation by any angle, a skew and a transformation move it
to the pixel that contains the image of the center of its pixel. When that pixel is not part of the result (a crop, an
auto-crop, a `Cover` resize or a transformation that removes it), the operation throws `UnsupportedImageFeatureException` and leaves the image unchanged;
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

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_AutoCrop;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

**Resizing**. `ResizeOptions` sets the target box and `Mode`: `Contain` (default: fit inside,
never padded), `Cover` (fill exactly, cropped around `Anchor`, one of nine positions), `Stretch`, `Pad` (fit inside like
`Contain`, then pad to exactly the target size with `BackgroundColor`, the image placed at `Anchor`: letterboxing) or
`BoxPad` (like `Pad`, but an image that already fits is only placed on the canvas, never enlarged).
`AllowUpscaling = false` keeps `Contain`, `Pad` and `BoxPad` within the original size ("max") and rejects `Cover`/`Stretch`
enlargements; `AllowDownscaling = false` keeps `Contain` at or above the original size ("min") and rejects the reductions
of the other modes. The padding defaults to transparent black: a format without alpha (`Rgb24`, `Gray8`, `Gray16`)
requires an opaque `BackgroundColor` (`UnsupportedImageFeatureException` otherwise), as do `Pad` and `ExtendCanvas`. A
centered image gets the extra pixel of an odd padding on its right or bottom side. The padding never blends into the
image: its edges are clamped as for any resize, and an image that is not resampled is copied unchanged.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Letterbox;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Letterbox(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Fits inside 320x240, then pads to exactly 320x240 with black bars, the image centered (ResizeOptions.Anchor).
    // The padding is transparent by default, which a pixel format without alpha (an RGB JPEG) cannot store
    image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Pad, BackgroundColor = new Rgba64(0, 0, 0) });

    // A 4-pixel white frame on every side: 328x248
    image.Pad(4, 4, 4, 4, new Rgba64(65535, 65535, 65535));
    image.Save(outputPath);
}
```
<!-- endref -->

Target sizes are computed with exact rational arithmetic, rounded to nearest (ties up), and are at least 1 pixel. Filters, sampled at
pixel centers with clamped edges, normalized weights, and a support widened when downsampling:

| Filter | Kernel | Radius | Interpolating |
| --- | --- | --- | --- |
| `NearestNeighbor` | the pixel containing the center | - | yes |
| `Box` | average of the pixels whose centers lie in the footprint of the output pixel (exact for integer downscales) | 1/2 | yes |
| `Bilinear` | triangle | 1 | yes |
| `Hermite` | cubic B = 0, C = 0 | 1 | yes |
| `Bicubic` (default) | Catmull-Rom, cubic B = 0, C = 1/2 | 2 | yes |
| `MitchellNetravali` | cubic B = C = 1/3 | 2 | no |
| `Robidoux` | Keys cubic, B = 12 / (19 + 9 sqrt(2)) | 2 | no |
| `RobidouxSharp` | Keys cubic, B = 6 / (13 + 7 sqrt(2)) | 2 | no |
| `Spline` | cubic B-spline, B = 1, C = 0 | 2 | no |
| `Lanczos2`, `Lanczos3`, `Lanczos5`, `Lanczos8` | sinc windowed by sinc, with 2, 3, 5 or 8 lobes | 2, 3, 5, 8 | yes |

The cubics are those of Mitchell and Netravali (SIGGRAPH 1988). A non-interpolating filter also smooths an axis that
keeps its size, for example the width of an image whose height only changes. Away from the edges, every filter gave the
same result as ImageMagick 7.1.2 (`-filter Box`, `Triangle`, `Hermite`, `Catrom`, `Mitchell`, `Robidoux`, `RobidouxSharp`,
`Spline`, `Lanczos2`, `Lanczos` and `Lanczos -define filter:lobes=5` or `8`), within one unit of 16-bit samples; at the
edges, ImageMagick renormalizes the weights of the pixels inside the image instead of repeating the edge pixel.

Colors are filtered premultiplied by alpha, so hidden colors of transparent pixels never bleed (zero alpha becomes
transparent black), and 16-bit images are filtered with 16-bit precision. A frame whose pixels read by the filter are all
opaque is filtered as its colors alone and keeps the maximum alpha: premultiplying by a constant alpha would only add
roundings.

- `ResizeWorkingSpace.Encoded` (default) filters the stored sample values directly, like most image tools.
- `ResizeWorkingSpace.LinearSrgb` converts the samples to linear light before filtering and back afterward (more
  physically correct averages, for example for thin bright lines), with the transfer function of the image: sRGB
  (IEC 61966-2-1) for an untagged image and for an ICC profile recognized as sRGB or sGray; any other profile is
  rejected rather than misinterpreted. No ICC transform is applied in either space: convert such an image first with
  `ConvertColorProfile(IccProfile.Srgb)`. An image with a CICP color description and no ICC profile (an AVIF or JPEG XL
  image, for example) is decoded with the function of its transfer characteristics, whatever its primaries, which do
  not change how light adds up: BT.709, gamma 2.2, PQ, HLG and the other functions of Rec. ITU-T H.273. PQ and HLG
  samples are filtered in the light their function decodes to, relative to its nominal peak, and stay PQ or HLG
  samples. Unspecified transfer characteristics are read as sRGB, linear ones are filtered as stored, and reserved
  ones are rejected. The name of the value is historical.

A resize to the current size leaves the image unchanged. Sums are computed in double precision, every term added with a
fused multiply-add (one rounding per term), and samples are correctly rounded, except for 8-bit images filtered in
`Encoded` space whose pixels read are all opaque (or that have no alpha): those are filtered in fixed point, with
weights of 14 fractional bits that add up to exactly 1, 16-bit intermediate rows and 32-bit sums, which is faster.
About 0.2% of their samples are then one unit off the correctly rounded value, when the exact value is within 0.02 of a
rounding boundary; none is off by more, and the headroom of the 16-bit rows is checked for the weights of every resize,
which keeps double precision when it does not fit. Results do not depend on the worker count or on SIMD support: the
vectorized and parallel kernels are bit-identical to the scalar reference.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ResizeAnimation;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ResizeAnimation(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Contain, Filter = ResamplingFilter.Lanczos3 });

    // Same format as the extension; APNG/GIF timing and play count come from the decoded image
    image.Save(outputPath);
}
```
<!-- endref -->

**Transformations**. `Rotate(degrees)` turns the image by any angle around its center, `Skew(degreesX, degreesY)` leans its vertical and
horizontal lines, and `Transform` applies an `AffineTransform` (any combination of translations, scalings, rotations and
skews) or a `ProjectiveTransform` (the image seen in perspective), in stored-pixel coordinates: the image covers
`[0, width] x [0, height]`. `TransformOptions` chooses:

- `CanvasMode`. `Expand` (default) makes the smallest canvas that holds the whole transformed image and centers the image
  in it (a size within 1e-6 of an integer is that integer); `Keep` keeps the size of the image, and `Explicit` takes
  `CanvasSize`. With `Keep` and `Explicit`, `Transform` applies the transformation as given (what lands outside the canvas
  is cut), and `Rotate` and `Skew` put the center of the image at the center of the canvas.
- `Filter`, the kernels of resizing (bicubic Catmull-Rom by default). Every output pixel takes the source at the point
  its center comes from. Where the transformation shrinks the image along one of its axes, the kernel of that axis is
  widened by the same factor, as when resizing; a rotation or a skew widens nothing, and a perspective widens its kernels
  by 16 at most, near its horizon. Colors are filtered premultiplied by alpha in double precision and rounded once to the
  storage precision, with the rules of resizing (16-bit precision, floating-point samples unclamped). Nearest neighbor
  copies the pixel that contains the point, and the box filter averages the pixels whose centers lie in the footprint
  of the point (the pixel that contains it, when nothing shrinks).
- `BackgroundColor` (transparent black by default): the pixels beyond the edges of the image, filtered like the others,
  so that the edges blend into it, and the canvas the image does not cover. A pixel format without alpha (`Rgb24`,
  `Gray8`, `Gray16`) needs an opaque color when the result shows it; an `UnsupportedImageFeatureException` says so.
- `WorkingSpace`, `Encoded` (default) or `LinearSrgb`, as for resizing.

A transformation that moves every pixel onto a pixel of a canvas it fills exactly, such as `Rotate(90)` or a mirror, is
the exact pixel permutation of `Rotate(RotateMode)` and `Flip`, whatever the filter; one that moves nothing leaves the
image unchanged. The canvas is checked against the resource limits before anything is allocated, and a transformation
that flattens the image, or a perspective with an `Expand` canvas whose horizon crosses the image, throws an
`ArgumentException`. The pixel aspect ratio is kept, except by the quarter turns that swap the axes. Results do not
depend on the worker count.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Straighten;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Straighten(string inputPath, string outputPath, double degrees)
{
    using var image = Image.Load(inputPath);

    // The canvas grows to hold the whole turned image. A JPEG image has no alpha: the corners need an opaque color
    image.Rotate(degrees, new TransformOptions { Filter = ResamplingFilter.Lanczos3, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) });
    image.Save(outputPath);
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_FlattenPage;format=md-fenced-code-block;dedent=true -->
```csharp
public static void FlattenPage(string inputPath, string outputPath, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft, Size pageSize)
{
    using var image = Image.Load(inputPath);

    // The page seen in perspective, inverted: from the photo to the page
    var page = new RectangleD(0, 0, pageSize.Width, pageSize.Height);
    var pageToPhoto = ProjectiveTransform.CreateFromQuadrilateral(page, topLeft, topRight, bottomRight, bottomLeft);
    if (!pageToPhoto.TryInvert(out var photoToPage))
        throw new ArgumentException("The corners do not delimit a page.", nameof(topLeft));

    // The canvas is the page: what the photo shows around it is cut
    image.Transform(photoToPage, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = pageSize, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) });
    image.Save(outputPath);
}
```
<!-- endref -->

**Convolution matrices**. `Convolve` replaces every sample by the weighted sum of the same sample
of the neighboring pixels: sharpening, blurring, embossing and edge detection are all matrices. A `ConvolutionKernel` has
odd dimensions, so its middle weight is the one of the pixel itself, and holds its weights row by row. It is applied
*as written* (the top-left weight multiplies the top-left neighbor; it is not flipped) and the weights are used as given:
divide them by their sum to keep the overall brightness. Ready-made matrices hold the published coefficients:
`SobelX`/`SobelY`, `ScharrX`/`ScharrY` and `PrewittX`/`PrewittY` (horizontal and vertical gradients, the kernels of
OpenCV's `cv2.Sobel` and `cv2.Scharr`), `Laplacian3x3` and `Laplacian5x5` (those of `cv2.Laplacian` with `ksize` 1 and 5),
`LaplacianOfGaussian` (the classic 5x5 approximation, with the sign of the Laplacian), `Emboss` and `Sharpen3x3`.
`ConvolutionOptions` then chooses:

- `EdgeMode`, the pixels read outside the image: `Clamp` (default, the nearest edge pixel), `Mirror` (the image reflected
  about its border), `Wrap` (the image tiled) or `Zero` (nothing: transparent black).
- `PreserveAlpha`. By default the filter is alpha-aware, like resizing: colors are filtered premultiplied by alpha, alpha
  is filtered too, and a pixel whose alpha becomes zero is transparent black, so hidden colors never bleed. A matrix
  whose weights add up to zero (edge detection) therefore turns every pixel transparent: set `PreserveAlpha = true` to
  filter the colors as stored and leave alpha untouched. Pixel formats without alpha are not concerned.
- `WorkingSpace`: `Encoded` (default) or `LinearSrgb`, with the same meaning and the same ICC profile rule as
  `ResizeWorkingSpace`.
- `Bias`, a value added to every color sum, as a fraction of the range of a color (0 by default; 0.5 adds 127.5 to
  8-bit sums, half of the light in `LinearSrgb`, and 0.5 to floating-point samples). It makes the negative sums of a
  gradient or an emboss visible, darker than mid-gray, instead of clamping them to 0. Alpha never gets it; with the
  alpha-aware filter it is added to the colors once they are divided by the filtered alpha.

Sums are computed in double precision with fused multiply-adds, at the storage precision (16-bit images are filtered
with 16-bit precision), plus the bias, rounded to nearest (ties up) and clamped to the sample range: negative sums become 0. The image
is rewritten in place, with a scratch of a few rows per worker proportional to the matrix height, charged to the
allocation limit; the pixel format, the size, the frames and the metadata are kept (the EXIF thumbnail, now stale, is
removed). As for resizing, results do not depend on the worker count or on SIMD support. The cost grows with the number
of non-zero weights, except for separable matrices. `ConvolutionKernel.CreateSeparable(rowWeights, columnWeights)`
makes the matrix whose weight at `(x, y)` is `columnWeights[y] * rowWeights[x]`, and a matrix given weight by weight is
recognized as one when every weight is exactly such a product (a box, a binomial, Sobel: `IsSeparable` tells). A
separable matrix is applied in two passes, every row filtered by the row weights and the filtered rows combined by the
column weights, in double precision and rounded once, so that the time grows with the sum of its dimensions instead of
their product: a 15x15 box is several times faster as factors than as a matrix.

`GaussianBlur(sigma)` blurs an image or a frame with a bell curve of standard deviation `sigma` (the radius CSS
gives to a blur is twice the deviation): one separable convolution, each row filtered by a row of `2 ceil(3 sigma) + 1`
weights and the filtered rows by the same weights as a column, so that the time grows with the deviation and not with
its square. Nothing is rounded between the two directions: the result is the one of the matrix of the products of the
weights, rounded once. It takes the edge mode and the working space of a convolution, and follows its rules; an opaque
frame is blurred as its colors alone and keeps the maximum alpha, except with the `Zero` edge mode, which reads
transparent pixels beyond the image. The weights `exp(-i^2 / (2 sigma^2))`, divided by their sum, are computed without
any function of the platform.


<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Convolve;format=md-fenced-code-block;dedent=true -->
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

    // Weights that add up to 0 would also filter alpha down to 0 (transparent black): keep the alpha instead, and
    // add half of the range so that negative sums show as darker than mid-gray instead of black
    var options = new ConvolutionOptions(ConvolutionKernel.Laplacian3x3) { PreserveAlpha = true, EdgeMode = ConvolutionEdgeMode.Mirror, Bias = 0.5 };
    image.Convolve(options);
    image.Save(edgesPath);
}
```
<!-- endref -->

**Edge detection**. `DetectEdges(detector, grayscale = true, edgeMode = Clamp)` replaces every frame by its edges, bright
where the colors change and black where they are flat. Every color sample becomes the strength of the edge at its
pixel, computed from the same sample of its neighbors with the ready-made matrices: the magnitude `sqrt(gx² + gy²)` of
the horizontal and vertical gradients (`Sobel`, `Scharr`, `Prewitt`), the largest response of the eight directions of a
compass (`Kirsch`, from `[5, 5, 5; -3, 0, -3; -3, -3, -3]` and its rotations, `Robinson`, from the Sobel matrix and its
rotations), or the absolute value of a Laplacian (`Laplacian3x3`, `Laplacian5x5`, `LaplacianOfGaussian`). The sums and
their combination are computed in double precision and rounded once, so 16-bit samples keep their precision; with the
`Clamp` edge mode, the results are those of OpenCV with `BORDER_REPLICATE` (`cv2.magnitude` of two `cv2.Sobel` or
`cv2.Scharr` gradients, the absolute value of `cv2.Laplacian`), rounded. `grayscale` converts every frame to grayscale
first, as `Grayscale` does, which gives white edges on black; without it, every color has its own edges. Colors are
filtered as stored and alpha is kept, as with `PreserveAlpha`. Floating-point samples are not clamped. The edge mode
`Clamp` finds no edge along the border of the image; `Zero` reads black beyond it.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DetectEdges;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DetectEdges(string inputPath, string edgesPath)
{
    using var image = Image.Load(inputPath);

    // White edges on black, computed on the brightness (grayscale: false keeps the edges of every color); alpha is kept
    image.DetectEdges(EdgeDetector.Sobel);
    image.Save(edgesPath);
}
```
<!-- endref -->

`BoxBlur(radius)` replaces every sample by the mean of the same sample of the `(2 radius + 1)^2` pixels of the square
around it, in a time per pixel that does not depend on the radius: the sums of the rows, then of the columns, are
computed by blocks of `2 radius + 1` values at fixed positions, three additions per sample and no subtraction (a sample
that is not a number or an infinity only reaches the squares that contain it), and divided once. It takes the edge mode
and the working space of a convolution and follows its rules, like `GaussianBlur`; the scratch holds about three times
the radius rows of double-precision samples per worker.

`Sharpen(sigma, amount = 1, threshold = 0)` is an unsharp mask: every sample `o` whose Gaussian blur of deviation
`sigma` is `b` (the blur of `GaussianBlur`) becomes `o + amount (o - b)`, which raises the contrast of the details
smaller than the blur, unless `|o - b|` is less than `threshold` times the range of the sample, so that flat areas and
their noise stay as they are. The samples are those of the blur (premultiplied colors and alpha, or linear light),
rounded once and clamped (ringing beyond black and white is clipped, except for the colors of floating-point samples);
an opaque frame keeps its alpha.

`MedianBlur(radius)` replaces every sample by the median of the same sample of the square of side `2 radius + 1` around
it: isolated specks disappear and edges stay sharp. Every sample, alpha included, is filtered on its own as stored;
nothing is computed or rounded, and there is no working space (a transfer function keeps the order of the samples).
Floating-point samples are ordered as numbers, `-0` below `+0`, and every sample that is not a number above the
infinities (it comes back with a positive sign). The samples are counted in histograms, and the time per pixel grows
with the radius. For `RgbaFloat` samples, a second histogram counts the samples whose 16 high bits are those of the
median: it is rebuilt from the whole window, a cost that grows with the square of the radius, each time the median
moves to other high bits (for samples between 0.5 and 1, these groups span 1/256). With a radius of 1, the 9
`RgbaFloat` samples of each window are sorted.

The box blur and the median give the samples of `cv2.blur` and `cv2.medianBlur` of OpenCV for the same edges, and the
unsharp mask those of the same formula over `cv2.GaussianBlur` (the tests keep some of them). ImageMagick 7.1 agrees
within a few units: its `-statistic Mean` is one unit off for about half of the samples, and its `-unsharp` samples
the Gaussian in another way, a difference that the amount magnifies (up to 3 units for a deviation of 1 and an amount
of 1).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_BlurAndSharpen;format=md-fenced-code-block;dedent=true -->
```csharp
public static void CleanUpScan(string inputPath, string cleanedPath, string backgroundPath)
{
    using var image = Image.Load(inputPath);

    // The median of every 3x3 square removes isolated specks and keeps edges. The unsharp mask then adds half the
    // difference from a Gaussian blur of deviation 1.5, except where it is under 2% of the range: flat paper stays flat
    image.MedianBlur(1);
    image.Sharpen(1.5, amount: 0.5, threshold: 0.02);
    image.Save(cleanedPath);

    // A box blur costs the same whatever its radius; a separable matrix is applied to the rows, then to the columns
    image.BoxBlur(25, ConvolutionEdgeMode.Mirror);
    image.Convolve(new ConvolutionOptions(ConvolutionKernel.CreateSeparable([0.25, 0.5, 0.25], [0.25, 0.5, 0.25])));
    image.Save(backgroundPath);
}
```
<!-- endref -->

**Replacing a color**. `ReplaceColor(color, replacement, tolerance)` gives `replacement` to every pixel whose color is
`color`, or within `tolerance` of it (a fraction of the range of a sample, from 0 to 1, rounded to 16-bit steps): a
plain background made transparent, a color of a logo changed. Each pixel is read as a 16-bit color (8-bit and 16-bit
samples exactly, a gray level as its three colors, an opaque alpha for a format without alpha), and each of its four
samples, alpha included, must be within the tolerance; `1 / 255` accepts one 8-bit level on each side. The
replacement is converted to the pixel format as a drawn color is. The comparison is made on integers, the same on
every platform, and no color profile is applied.

**Color matrices**. `ApplyColorMatrix` gives each pixel a color computed from its own one, by a `ColorMatrix`: four
rows, for the red, the green, the blue and the alpha of the result, of five coefficients, for the red, the green, the
blue and the alpha of the pixel and for a constant (`R' = m00 R + m01 G + m02 B + m03 A + m04`, with the samples as
fractions of one and straight alpha). This is the matrix of the `feColorMatrix` filter of SVG. The ready-made ones are
the filter functions of CSS, with the matrices of the W3C: `CreateBrightness`, `CreateContrast`, `CreateSaturation`,
`CreateHueRotation`, `CreateGrayscale`, `CreateSepia`, `CreateInvert` and `CreateOpacity`; `CreateTint` gives every
pixel one color and keeps its alpha (an icon drawn in another color), and `CreateDuotone` the color between two inks
that its gray level says. `Then` makes one matrix of two: the colors are computed once, and are not rounded or clamped
in between.

The matrix is applied to the stored sample values, at their precision: a sample of the result is
`floor(m0 R + m1 G + m2 B + m3 A + m4 M + 1/2 + 2^-20)` with `M` the largest sample value, the terms added in that
order in double precision, kept between 0 and `M`. Only multiplications and additions are involved, so the result is
the same on every platform, and `CreateGrayscale()` gives the pixels of `Grayscale`, bit for bit. A pixel format
without alpha has an alpha of `M` and gets the color of the result over black; a grayscale format has its gray level
as red, green and blue and gets the Rec. 709 luma of the result. No color profile is applied. In a drawing,
`canvas.Filter` applies a matrix to what a callback draws and `canvas.FilterBackdrop` to a part of what is already
drawn (see [Drawing](#drawing)).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ColorMatrix;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ApplyColorFilters(string photoPath, string iconPath, string outputPath)
{
    using var photo = Image.Load(photoPath);
    using var icon = Image.Load(iconPath);

    // Matrices are immutable: create them once. Two matrices become one, which is applied once per pixel
    var warm = ColorMatrix.CreateSaturation(0.6).Then(ColorMatrix.CreateSepia(0.4)).Then(ColorMatrix.CreateContrast(1.1));
    var dim = ColorMatrix.CreateBrightness(0.5);
    var accent = ColorMatrix.CreateTint(Colors.Gold);

    // The whole image, every frame of it
    photo.ApplyColorMatrix(warm);

    photo.Draw(canvas =>
    {
        // What is already drawn, inside a clip: the lower third of the photo is darker
        var band = new Rectangle(0, canvas.Height * 2 / 3, canvas.Width, canvas.Height - (canvas.Height * 2 / 3));
        canvas.Clip(band, clipped => clipped.FilterBackdrop(dim));

        // What a callback draws: the icon keeps its shape and its transparency, and gets one color
        canvas.Filter(accent, tinted => tinted.DrawImage(icon.Frames[0], new PointD(8, band.Y + 8)));
    });

    photo.Save(outputPath);
}
```
<!-- endref -->

#### Effects

The effects are of two kinds. The **color effects** are color matrices (`ColorMatrix`, applied by `ApplyColorMatrix`):
each pixel gets a color computed from its own one only, with the arithmetic of **Color matrices** above, and they
combine with `Then` and with the other matrices.

| Color effect (matrix) | What it does | Source of the coefficients |
| --- | --- | --- |
| `ColorMatrix.Polaroid` | The cool, saturated look of an instant photograph | `polaroid()` of the `ColorMatrixFilter` of PixiJS 8.6.6 |
| `ColorMatrix.Kodachrome` | The warm contrast of a Kodachrome slide | `kodachrome()` of the `ColorMatrixFilter` of PixiJS 8.6.6 (constants divided by 255) |
| `ColorMatrix.Lomograph` | The strong, shifted tones of a Lomography camera | `Lomograph` of ImageProcessor |
| `ColorMatrix.BlackWhite` | High-contrast black and white: `1.5 (R + G + B) - 1` | `BlackWhite` of ImageProcessor |
| `ColorMatrix.CreateLightness(amount)` | Adds `amount` (-1 to 1) to every color | The `linear` function of `feComponentTransfer` (SVG), slope 1 |
| `ColorMatrix.CreateColorBlindness(type)` | The colors as seen with protanopia, deuteranopia, tritanopia or the anomalies of the same cones (severity 0.5, or any severity with the second argument) | Table 1 of Machado, Oliveira and Fernandes, IEEE TVCG 15(6), 2009, interpolated between its severities |

The color blindness matrices are defined on linear RGB: they are exact on a linear-light `RgbaFloat` image, and an
approximation on sRGB-encoded samples, which is how most simulators apply them.

The **spatial effects** are operations of their own, which read the neighbors of a pixel or its position:

| Spatial effect | What it does |
| --- | --- |
| `Vignette(color, radiusX, radiusY, rectangle)` | An elliptic radial gradient, transparent at the center of the rectangle (the canvas by default) and `color` on the ellipse of half-axes `radiusX` and `radiusY` and beyond |
| `Glow(color, radiusX, radiusY, rectangle)` | The opposite gradient: `color` at the center, transparent on the ellipse and beyond |
| `Pixelate(size)` | Every block of `size` by `size` pixels from the top-left corner gets the mean of its pixels (smaller blocks on the right and bottom edges) |
| `OilPaint(levels, radius)` | Every pixel gets the mean of the pixels of the square of side `2 radius + 1` around it whose intensity level (the Rec. 709 luma over black, cut into `levels` levels) is the most frequent there |
| `Flatten(backgroundColor)` | Every pixel is composited over an opaque color and becomes opaque; the pixel format is kept |

The vignette and the glow are drawn on the image with `CompositeMode.SourceAtop`: the alpha of every pixel is kept,
and a transparent pixel stays transparent (draw a `RadialGradientBrush` with the canvas to paint those too). Their
arithmetic is the one of the drawings. The means of `Pixelate` and `OilPaint` are alpha-aware: colors are averaged
premultiplied by their alpha, so the hidden color of a transparent pixel takes no part, and they are computed on the
stored samples in double precision and rounded once, as the blurs do (floating-point samples are not clamped). The
oil painting moves its window by one column at a time for the integer formats, whose sums are exact, so its time per
pixel grows with the radius; for `RgbaFloat` and `RgbaHalf` it grows with its square. `Flatten` uses the formulas of a
conversion to a format without alpha with `PixelConversionOptions.BackgroundColor`: a flattened `Rgba32` image converted
to `Rgb24` is that conversion, bit for bit, and a format without alpha is left as it is. `Pixelate`, `OilPaint` and
`Flatten` split the rows of a frame of at least 65,536 pixels between up to `ImageConfiguration.MaxDegreeOfParallelism`
workers, with identical results: a band of the pixelation starts on a row of blocks, and a band of the oil painting is
at least as tall as its window and reads the rows around it from a copy taken before any row changes. With 8 workers,
`OilPaint(levels: 20, radius: 5)` on a 3840x2160 photograph is 5 times faster (350 ms to 70 ms, Apple M5 Pro).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Effects;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ApplyPhotoEffects(string photoPath, string outputDirectory)
{
    using var photo = Image.Load(photoPath);

    // A preset matrix, then the dark corners of an old lens: with the half-diagonal as radius, the corners get the
    // whole color and the center none of it
    using (var lomo = photo.Clone())
    {
        lomo.ApplyColorMatrix(ColorMatrix.Lomograph);
        var halfDiagonal = Math.Sqrt((lomo.Width * lomo.Width) + (lomo.Height * lomo.Height)) / 2;
        lomo.Vignette(Colors.Black, halfDiagonal, halfDiagonal);
        lomo.Save(Path.Combine(outputDirectory, "lomo.png"));
    }

    // Spatial effects: blocks of 8x8 pixels, and brush strokes of 7x7 pixels with 20 levels of intensity
    using (var mosaic = photo.Clone())
    {
        mosaic.Pixelate(8);
        mosaic.Save(Path.Combine(outputDirectory, "mosaic.png"));
    }

    using (var painting = photo.Clone())
    {
        painting.OilPaint(levels: 20, radius: 3);
        painting.Save(Path.Combine(outputDirectory, "painting.png"));
    }

    // The colors as a person without green cones sees them
    using (var preview = photo.Clone())
    {
        preview.ApplyColorMatrix(ColorMatrix.CreateColorBlindness(ColorBlindnessType.Deuteranopia));
        preview.Save(Path.Combine(outputDirectory, "deuteranopia.png"));
    }

    // Transparent pixels composited onto white: every pixel is opaque, and the image keeps its pixel format
    photo.Flatten(Colors.White);
    photo.Save(Path.Combine(outputDirectory, "flattened.png"));
}
```
<!-- endref -->

#### Histograms, equalization and auto-levels

`ComputeHistogram` counts the pixels of every level of a channel (`HistogramChannel`: the Rec. 709 luminance that
`Grayscale` stores, red, green, blue or alpha), over every frame and the poster or over one frame, without changing the
image. `Equalize` and `AutoLevels` remap the levels from such histograms, in place:

- **Global equalization** (`HistogramEqualizationMode.Global`, the default) is the textbook mapping of the cumulative
  histogram: a level `v` becomes `round((cdf(v) - cdf_min) * max / (N - cdf_min))`. On 8-bit samples it gives the
  pixels of OpenCV's `equalizeHist`.
- **Adaptive equalization** (`HistogramEqualizationMode.Adaptive`, CLAHE) equalizes each tile of a grid
  (`TileCountX` and `TileCountY`, 8 by default) with its histogram clipped at `ClipLimit` times the average bin height
  (2 by default; the excess is spread over every bin, as Zuiderveld does), and blends the mappings of the four nearest
  tiles bilinearly at each pixel center. The histogram is clipped in 256 bins whatever the depth, as MATLAB and
  scikit-image do, so the clip limit of a 16-bit frame acts as on its 8-bit version: a bin of 16-bit samples covers 256
  levels, whose mapping follows the histogram of these levels. On 8-bit frames, the tables are those of OpenCV's
  `createCLAHE` with the same settings; OpenCV interpolates them at the corner of each pixel, which moves its results by a
  few levels. OpenCV clips 16-bit histograms in 65,536 bins, where the limit of the tiles of fewer than 65,536 /
  `ClipLimit` samples is one sample per level, the same for most clip limits.
- **Auto-levels** (`AutoLevels`) stretches the levels between two percentiles of the histogram (`LowPercentile` and
  `HighPercentile`, 0 and 100 by default: the darkest and the brightest levels present) linearly to the whole range.

`HistogramColorMode.Luminance`, the default, remaps the luma and adds its change to the red, green and blue samples,
which keeps the chroma and so the hues (except where a sample is clamped); `PerChannel` remaps each channel from its
own histogram, which also neutralizes a color cast. Every computation is on the stored samples at the storage
precision, with one bin per level (256, or 65,536 for 16-bit formats; 256 bins for the clip of CLAHE), in exact integer
arithmetic except the bilinear blend of CLAHE (IEEE doubles): the results are the same on every platform. Fully
transparent pixels are not counted, but they are remapped. Floating-point formats have no discrete levels and are rejected
(`UnsupportedImageFeatureException`): convert them to `Rgba64` first. Each frame is processed alone by default;
`ShareHistogramAcrossFrames` gives every frame of an animation the mapping of their combined histograms, so that it does
not flicker. The tables (up to 25 MiB for 8 x 8 tiles of 16-bit samples remapped per channel) are charged to the image.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Histogram;format=md-fenced-code-block;dedent=true -->
```csharp
public static ImageHistogram EnhanceContrast(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Stretch the luminance between its 0.5th and 99.5th percentiles: a few very dark or very bright pixels do not
    // limit the stretch. The hues are kept.
    image.AutoLevels(new AutoLevelsOptions { LowPercentile = 0.5, HighPercentile = 99.5 });

    // Raise the local contrast: each of 8 x 8 tiles is equalized with a contrast limit, and the tiles are blended
    // without seams. The frames of an animation share their histograms, so that they do not flicker.
    image.Equalize(new HistogramEqualizationOptions
    {
        Mode = HistogramEqualizationMode.Adaptive,
        ClipLimit = 2,
        ShareHistogramAcrossFrames = true,
    });

    image.Save(outputPath);

    // The number of pixels at each luminance level, over every frame
    return image.ComputeHistogram(HistogramChannel.Luminance);
}
```
<!-- endref -->

#### Color reduction

`Quantize` reduces the colors of every frame to a palette, in place, so that each frame has at most
`QuantizeOptions.MaxColors` distinct colors (2 to 256, the transparent entry included), or only the colors of
`QuantizeOptions.Palette`. The pixel format is kept; the colors are read and written as 8-bit samples (a 16-bit or
floating-point sample gets the exact value of an 8-bit one), and every fully transparent pixel becomes transparent
black.

- **Palette.** A frame whose colors fit keeps them exactly. Otherwise the palette is a variance-based median cut of a
  histogram of fixed size (5 bits per channel for the opaque colors, 4 bits per channel and 3 bits of alpha for the
  translucent ones), refined by k-means (up to eight passes on exact integer sums). Alpha is reduced with the colors:
  translucent colors are compared premultiplied by their alpha and get entries of their own, and an opaque pixel always
  gets an opaque entry, so opaque areas never become translucent.
- **Shared palettes.** `PaletteBuilder` computes one `ImagePalette` from several frames or images (the frames of an
  animation, a set of icons), which `QuantizeOptions.Palette` then applies to each of them. A palette can also be listed
  color by color. A gray pixel format uses the gray entries of a fixed palette only, and a format without alpha the
  opaque ones.
- **Dithering.** `QuantizeOptions.Dithering` selects error diffusion (`FloydSteinberg`, `Atkinson`, `Burkes`,
  `JarvisJudiceNinke`, `Sierra`, `SierraTwoRow`, `SierraLite`, `Stucki`; left to right on every row, from the top row
  down, on the opaque pixels) or ordered dithering (`Bayer2x2`, `Bayer4x4`, `Bayer8x8`, spread over the mean distance
  between the palette colors), and `DitherStrength` (0 to 1) scales the error or the pattern. A frame whose colors are
  all palette entries is never dithered.
- **Determinism.** Every decision uses integer arithmetic (the split criterion of the median cut and the spacing of
  the ordered pattern use IEEE doubles, which are the same on every platform): the same pixels and options give the
  same output everywhere.
- **Encoders.** `GifEncoder.Quantization`, `BmpEncoder.Quantization` and `PngEncoder.Quantization` take the same
  options, with the same quantizer; each format first applies its own alpha rules (GIF thresholds or flattens alpha,
  BMP composites it over its background color, an indexed PNG keeps translucent entries).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Quantize;format=md-fenced-code-block;dedent=true -->
```csharp
public static void QuantizePhoto(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Each frame gets its own palette of at most 64 colors (its exact colors when they fit), and Atkinson error
    // diffusion hides the bands of smooth gradients. The pixel format is kept: the image can be saved in any format.
    image.Quantize(new QuantizeOptions { MaxColors = 64, Dithering = DitheringMode.Atkinson });
    image.Save(outputPath);
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SharedPalette;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveGifWithSharedPalette(string inputPath, string outputPath)
{
    using var animation = Image.Load(inputPath);

    // The palette of the pixels of every frame and of the poster: 255 colors leave room for the transparent entry
    ImagePalette palette;
    using (var builder = new PaletteBuilder())
    {
        builder.Add(animation);
        palette = builder.Build(maxColors: 255);
    }

    // Every frame maps to the same colors, so they do not flicker from one frame to the next
    animation.Save(outputPath, new GifEncoder
    {
        Quantization = new QuantizeOptions { Palette = palette, Dithering = DitheringMode.FloydSteinberg },
    });
}
```
<!-- endref -->

**Quality.** Measured against pngquant 3.0.3 (libimagequant, default speed, `--nofs` without dithering) with the
`ImageSimilarity` ports of SSIMULACRA 2 (higher is better, 100 is identical) and Butteraugli (3-norm, lower is better),
as means over seven photographs and wallpapers of 354×354 to 1024×576 pixels (2026-10-26):

| Colors | Quantizer | Dithering | SSIMULACRA 2 | Butteraugli | PSNR (dB) |
| ---: | --- | --- | ---: | ---: | ---: |
| 256 | `Quantize` | `None` | 77.5 | 1.22 | 40.8 |
| 256 | `Quantize` | `FloydSteinberg` | 77.6 | 1.46 | 37.4 |
| 256 | `Quantize` | `Atkinson` | 82.0 | 1.10 | 40.1 |
| 256 | `Quantize` | `Bayer8x8` | 77.7 | 1.60 | 37.4 |
| 256 | pngquant | none | 80.6 | 1.84 | 36.2 |
| 256 | pngquant | Floyd–Steinberg | 82.0 | 1.78 | 36.0 |
| 64 | `Quantize` | `None` | 56.8 | 2.40 | 36.3 |
| 64 | `Quantize` | `FloydSteinberg` | 58.5 | 2.64 | 32.8 |
| 64 | `Quantize` | `Atkinson` | 66.1 | 2.09 | 35.5 |
| 64 | `Quantize` | `Bayer8x8` | 60.3 | 2.72 | 32.9 |
| 64 | pngquant | none | 63.9 | 2.49 | 33.5 |
| 64 | pngquant | Floyd–Steinberg | 70.5 | 2.31 | 33.1 |
| 16 | `Quantize` | `None` | 10.3 | 5.54 | 30.5 |
| 16 | `Quantize` | `FloydSteinberg` | 20.8 | 5.56 | 27.3 |
| 16 | `Quantize` | `Atkinson` | 29.3 | 4.42 | 29.5 |
| 16 | `Quantize` | `Bayer8x8` | 24.3 | 5.28 | 27.0 |
| 16 | pngquant | none | 20.9 | 5.45 | 29.3 |
| 16 | pngquant | Floyd–Steinberg | 39.3 | 4.50 | 28.7 |

The palette is closer to the pixels (a higher PSNR and, from 64 colors up, a lower Butteraugli distance), while
pngquant, which weighs the colors perceptually and adapts its dithering to the content, scores higher on SSIMULACRA 2,
which penalizes banding most. `Atkinson` is the best choice for photographs: it matches pngquant at 256 colors. With
few colors, the error diffusion of pngquant remains ahead.

#### Binarization

`Threshold` makes every frame black and white, in place: a pixel whose luminance (the Rec. 709 luma that `Grayscale`
stores, at the storage precision) is greater than its threshold becomes `ThresholdOptions.UpperColor` (white by
default), and any other pixel `LowerColor` (black). The pixel format is kept. `ThresholdOptions.Method` chooses the
thresholds:

- **Otsu** (`ThresholdMethod.Otsu`, the default) gives the frame the level that maximizes the variance between the two
  classes of its luminance histogram. `ImageHistogram.GetOtsuThreshold` returns that level for any histogram; on the
  same integer samples it is the level of scikit-image's `threshold_otsu`. `ShareHistogramAcrossFrames` gives every
  frame of an animation the level of their combined histograms.
- **Fixed** (`ThresholdMethod.Fixed`) uses `Level`, a fraction of the sample range (0.5 by default: 128 and above
  become white on 8-bit samples).
- **Mean** (`ThresholdMethod.Mean`) compares each pixel with the mean of the `WindowSize` square around it (15 by
  default) minus `Offset`, as scikit-image's `threshold_local` with `method='mean'` does: it follows uneven lighting.
- **Sauvola** (`ThresholdMethod.Sauvola`), for documents, uses `m * (1 + k * (s / R - 1))`, where `m` and `s` are the
  mean and the standard deviation of the window, `k` is `SauvolaK` (0.2) and `R` is `SauvolaR` (0.5 of the range, the
  127.5 of the paper), the formula of scikit-image's `threshold_sauvola`: a blank background stays blank.

The windows read the pixels outside the frame as `EdgeMode` says (`Mirror` by default, scipy's `reflect`). Their sums
are exact integers and their means and deviations IEEE doubles, so the results are the same on every platform; on the
same samples, they are the masks of scikit-image 0.25.2 with every edge mode. The working memory of the adaptive
methods (2 bytes per pixel of a frame) is charged to the image. Alpha is ignored and every pixel gets one of the two
colors, alpha included, which the pixel format must store exactly (`ArgumentException` otherwise). Floating-point
formats have no discrete levels and are rejected (`UnsupportedImageFeatureException`): convert them to `Rgba64` first.

The result is what a bilevel TIFF page (`TiffPixelLayout.Bilevel`, required by the CCITT compressions) and a portable
bitmap (`PnmPixelLayout.Bitmap`) store, since these encoders never threshold: they accept only exactly black and white
pixels. To dither to black and white instead, `Quantize` with an `ImagePalette` of black and white and a `Dithering`
mode.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Threshold;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveScanAsBilevel(string inputPath, string tiffPath, string pbmPath)
{
    using var page = Image.Load(inputPath);

    // Sauvola's threshold follows uneven lighting: each pixel is compared with the mean and the contrast of the
    // 31 x 31 pixels around it, so the text becomes black and the paper white, even in a shadow. The pixels are now
    // exactly black or white, in the same pixel format.
    page.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 31 });

    // A bilevel page with Group 4 compression, the usual format of scanned documents
    page.Save(tiffPath, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel, Compression = TiffCompression.CcittGroup4 });

    // A portable bitmap: one bit per pixel
    page.Save(pbmPath, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap });
}
```
<!-- endref -->

There is no HDR processing or custom processor.

### Comparing images

`ImageSimilarity` measures how similar two frames look, for visual regression tests, deduplication or quality checks.
It never changes the frames.

- `ComputeSsim` returns the mean Structural Similarity Index of two frames of the same size, from -1 to 1 (identical
  pixels). It is computed in every 7x7 window, moved one pixel at a time, with the parameters of scikit-image's
  `structural_similarity(data_range=255, channel_axis=-1)`: for opaque frames, the score is the mean over the three
  color channels of what scikit-image computes. Color channels are premultiplied by alpha, so a fully transparent pixel
  scores the same whatever color it hides. Alpha is scored on its own, and the result is the lower of the two scores. A
  localized difference lowers the score in proportion to its area.
- `ComputePsnr` returns the peak signal-to-noise ratio in decibels, from the mean squared error of the color channels
  premultiplied by alpha (scikit-image's `peak_signal_noise_ratio(data_range=255)` for opaque frames); alpha gives its
  own ratio and the lower one is kept. Identical frames have an infinite ratio.
- `ComputeSsimulacra2` returns the [SSIMULACRA 2](https://github.com/cloudinary/ssimulacra2) score of two frames of at
  least 8x8 pixels, a perceptual metric tuned on subjective scores: 100 for identical pixels, about 90 when the
  difference is not visible, 70 for a high quality, 50 for a medium quality, negative for very different frames. It is a
  port of the `ssimulacra2` tool of libjxl 0.12.0, whose arithmetic it reproduces: the scores agree with the 8 decimals
  the tool prints. Frames that are not fully opaque are composited over a dark and a bright gray, and the lower score is
  kept.
- `ComputeButteraugliDistance` returns the [Butteraugli](https://github.com/google/butteraugli) distance of libjxl
  0.12.0, a psychovisual model with a distance for every pixel, with the parameters of `butteraugli_main` for SDR images
  (80 nits): `MaxDistance` (0 for identical pixels, below about 1 when the difference is not visible) and `PNorm3`, a
  summary of the whole frame. The values are within about 0.01% of the tool. Frames that are not fully opaque are
  composited over black and over white, and every pixel keeps the larger distance; frames smaller than 8x8 are extended
  by repeating their edges. It is the slowest member (about 0.75 s per megapixel) and needs about 120 bytes per pixel.
- `ComputeDifferenceHash` (dHash) and `ComputePerceptualHash` (pHash) return a 64-bit hash of the structure of a
  frame, computed from an exact, area-weighted luminance thumbnail. Every pixel counts, and differences smaller than a
  luminance level never set a bit.
- `ComputeDifferenceHashDistance` and `ComputePerceptualHashDistance` compare two frames of the same size, from 0 to
  `ImageSimilarity.MaxHashDistance` (64). The distance is the number of hash bits that differ, plus the difference
  between the mean luminances (about 4 levels out of 255 per unit), since hashes ignore brightness. Frames that are not
  fully opaque are compared over a black and over a white background, and the larger distance is kept.

Every pixel format is read as 8-bit RGBA, with the conversion of `CloneAs<Rgba32>` (16-bit samples are rounded), so
frames of different pixel formats can be compared. The results depend only on the pixels, not on the hardware; the last
digits of PSNR, SSIMULACRA 2 and Butteraugli can differ between platforms, as they use the logarithm and power functions
of the platform. The members compare frames: compare an animation frame by frame.

SSIM, PSNR, SSIMULACRA 2 and Butteraugli use up to `ImageConfiguration.MaxDegreeOfParallelism` workers (the
configuration of the image of the expected frame) for frames of at least 65,536 pixels, with identical results: their
floating-point sums are made by bands of 64 rows, added in order, whatever the number of workers. With 8 workers on a
3840x2160 photograph (Release, net11.0, Apple M5 Pro), SSIMULACRA 2 is 5.5 times faster (2.0 s to
0.37 s) and Butteraugli 6.7 times faster (6.3 s to 0.94 s).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ImageSimilarity;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool LookTheSame(string expectedPath, string actualPath)
{
    using var expected = Image.Load(expectedPath);
    using var actual = Image.Load(actualPath);
    if (expected.Size != actual.Size || expected.Frames.Count != actual.Frames.Count)
        return false;

    for (var i = 0; i < expected.Frames.Count; i++)
    {
        // 1 for identical pixels; small differences, such as anti-aliasing, keep the score close to 1
        if (ImageSimilarity.ComputeSsim(expected.Frames[i], actual.Frames[i]) < 0.99)
            return false;

        // 0 for the same structure and brightness, up to 64; the pHash is the most tolerant to small changes
        if (ImageSimilarity.ComputePerceptualHashDistance(expected.Frames[i], actual.Frames[i]) > 4)
            return false;

        // Perceptual metrics: 100 for identical pixels, about 90 when the difference is not visible
        if (ImageSimilarity.ComputeSsimulacra2(expected.Frames[i], actual.Frames[i]) < 90)
            return false;

        // 0 for identical pixels, below about 1 when the difference is not visible, even in a small area
        if (ImageSimilarity.ComputeButteraugliDistance(expected.Frames[i], actual.Frames[i]).MaxDistance > 1)
            return false;
    }

    return true;
}
```
<!-- endref -->

### Drawing

`ImageFrame.Draw` hands a `DrawingCanvas` to a callback, for one frame; `Image.Draw` does the same for a whole image and
paints every frame and the poster identically. The callback runs once, while the pixels are leased: the canvas is only
valid inside it, and other pixel access or geometry changes on the same image throw `InvalidOperationException` until it
returns. The drawing types are in the `Meziantou.Framework.Imaging.Drawing` namespace.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawShapes;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

**Shapes**. A canvas fills (`Fill`, `FillRectangle`, `FillRoundedRectangle`, `FillEllipse`, `FillCircle`, `FillPie`,
`FillPolygon`, `FillClosedCurve`) and strokes (`Stroke`, `StrokeRectangle`, `StrokeRoundedRectangle`, `StrokeEllipse`,
`StrokeCircle`, `StrokeArc`, `StrokeLine`, `StrokePolyline`, `StrokePolygon`, `StrokeBezier`, `StrokeCurve`,
`StrokeClosedCurve`). Anything else is a `VectorPath`, built once with a `VectorPathBuilder` (`MoveTo`, `LineTo`,
`QuadraticBezierTo`, `CubicBezierTo`, `ArcTo`, `CurveTo`, `Close`, `AddRectangle`, `AddRoundedRectangle`,
`AddEllipse`, `AddCircle`, `AddArc`, `AddPie`, `AddPolygon`, `AddCurve`, `AddClosedCurve`) and drawn as many times as
needed. A path holds any number of contours; an open
contour is closed by a straight segment when it is filled, and gets the caps of the pen when it is stroked. `FillRule`
decides what is inside when contours overlap: `NonZero` (default) fills everything the contours wind around, `EvenOdd`
makes a hole wherever an even number of them overlap.

**Arcs**. `ArcTo` continues a contour with an arc of an ellipse, in two forms: the arc of SVG paths (the radii and the
rotation of the ellipse, the large-arc and direction flags, the end point; radii too small to reach the end point are
enlarged), and the arc around a center (the radii, the angle of its first point and its sweep), which a straight
segment joins to the current point. `AddArc` starts a contour with an arc around a center, `AddPie` adds the closed
slice between an arc and its center, and `AddRoundedRectangle` a rectangle whose corners are quarters of an ellipse
(radii are limited to half the sides). With a `CornerRadii`, each corner is a quarter of a circle of its own radius (a
tab, a speech bubble; zero for a corner that is not rounded): when the radii of the two corners of a side add up to
more than that side, every radius is reduced by the same factor, as CSS does. Angles are in degrees from the positive x axis; a positive angle turns clockwise
on the image, and a sweep beyond 360 degrees is the whole ellipse. An arc is an element of the path like a curve: it
ends exactly on its end point, and it is transformed as an arc.

**Curves through points**. `StrokeCurve` draws the smooth curve that goes through a list of points, the line of a
chart; `StrokeClosedCurve` and `FillClosedCurve` go back from the last point to the first one, and `AddCurve`,
`AddClosedCurve` and `CurveTo` (from the current point of a contour) add the same curves to a path. The curve is a
cubic Bézier curve between each point and the next one, which has at each point the direction of the line between
the points around it. Its `tension` says how far it keeps that direction: 0.5 (the default) is the Catmull-Rom
spline, 0 gives the straight segments between the points, and 1 the roundest curve. From `P1` to `P2`, the control
points are `P1 + tension (P2 - P0) / 3` and `P2 - tension (P3 - P1) / 3`; the first point of an open curve is its own
previous point, and its last point its own next point. A curve can go beyond the points it goes through where they
turn sharply.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawChart;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawChart(string outputPath)
{
    using var image = new Image<Rgba32>(240, 140, Colors.White);
    PointD[] values = [new(30, 100), new(70, 60), new(110, 80), new(150, 30), new(190, 50)];

    // Hatching: a tile of 6 by 6 with a stripe, drawn once, repeated, and turned by the transformation of the brush
    var hatch = new PatternBrush(new Rectangle(0, 0, 6, 6), tile => tile.FillRectangle(Colors.LightSteelBlue, new Rectangle(0, 0, 3, 6)))
    {
        Transform = AffineTransform.CreateRotation(45),
    };
    var axis = new Pen(Colors.DimGray, 2) { EndMarker = LineMarker.Arrow };
    var line = new Pen(Colors.Crimson, 3) { StartMarker = LineMarker.Circle, EndMarker = LineMarker.Circle };
    var border = new Pen(Colors.SteelBlue, 2) { Alignment = PenAlignment.Inside };

    // The area under the curve: the curve through the values, closed by the axis
    var area = new VectorPathBuilder().MoveTo(values[0]).CurveTo(values.AsSpan(1)).LineTo(190, 120).LineTo(30, 120).Close().Build();

    image.Draw(canvas =>
    {
        // The outline is inside the area: it does not go over the axis
        canvas.Fill(hatch, area);
        canvas.Stroke(border, area);

        // The head of each arrow has its tip at the end of the line
        canvas.StrokeLine(axis, new PointD(20, 120), new PointD(225, 120));
        canvas.StrokeLine(axis, new PointD(20, 120), new PointD(20, 12));

        // The curve goes smoothly through every value, with a disk at both ends
        canvas.StrokeCurve(line, values);
    });

    image.Save(outputPath);
}
```
<!-- endref -->

**Transforms**. An `AffineTransform` is a combination of translations, scalings, rotations and skews
(`CreateTranslation`, `CreateScale`, `CreateRotation`, `CreateSkew`, combined with `Then` or `*`; the first one is
applied first), and `VectorPath.Transform` returns the path it gives. The result is exact: curves are transformed as
curves and an ellipse stays an ellipse, so a path that is enlarged stays smooth. Angles are in degrees, and a positive
angle turns clockwise on the image (Y grows downward); sines and cosines do not come from the platform, and a rotation
by a multiple of 90 degrees is exact. `TransformBounds(rectangle)` gives the box a transformed rectangle lands in: where
a turned label or image ends up, to size the image that holds it.

`canvas.Transform(transform, callback)` transforms everything a callback draws: shapes, strokes (the pen is
transformed with the path it strokes, so a scaling makes it wider, unless `Pen.ScalesWithTransform` is off: see Pens
below), the gradients and the images of the brushes,
images, texts with the paints of their spans and the glyphs a font draws with its own colors, clips, and the offset
and the blur of a shadow. A drawing is flattened for the size the transformation gives it: curves stay smooth however
they are enlarged, and straight sides are placed exactly where `VectorPath.Transform` places them. Transformations
nest (the inner one applies first), with clips and layers in any order. A drawing that the transformation sends beyond
±16,777,216 (the box of a page of 33 million pixels moved by a few pixels, a shape scaled a million times) is drawn
where the canvas shows it: shapes, strokes, clip paths, rectangles, the destinations of images and glyphs are cut by
that range instead of being rejected.

**Perspective and three dimensions**. A `ProjectiveTransform` keeps straight lines straight, but not parallel lines
parallel: it draws a plane seen in perspective. A point `(x, y)` becomes `(X / W, Y / W)`, with
`X = x M11 + y M21 + M31`, `Y = x M12 + y M22 + M32` and `W = x M13 + y M23 + M33`; an `AffineTransform` converts to
the one whose `W` is 1, and `ProjectiveTransform.CreateFromQuadrilateral` gives the one that maps a rectangle to four
points. A `Transform3D` is a 4x4 matrix as the 3D transforms of CSS: `CreateTranslation`, `CreateScale`,
`CreateRotationX`, `CreateRotationY`, `CreateRotationZ`, `CreateRotation` around an axis, `CreatePerspective` (the
distance from the viewer to the plane), combined with `Then`; the drawing is its plane `z = 0`, and
`ToProjectiveTransform` gives what the matrix does to it.

`canvas.Transform` takes both: everything the callback draws is drawn in perspective, the paints of the brushes
included. What is on the other side of the horizon of the plane (where `W` is not positive: behind the viewer) has no
image: a drawing that crosses the horizon is cut there, and nothing is rejected. Transformations of space started
inside each other are combined in three dimensions, so that a rotation inside a perspective shows the side that comes
closer larger, as the transforms of nested elements of CSS do. The blur and the offset of a shadow, a blur and the
glyphs a font draws with its own colors are drawn in the plane, on an image of it, then that image is drawn in
perspective. `DrawImage` takes a `ProjectiveTransform` too.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Draw3d;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawTiltedCard(string outputPath)
{
    using var image = new Image<Rgba32>(240, 160, Colors.WhiteSmoke);
    var card = new RectangleD(60, 30, 120, 100);
    var face = new LinearGradientBrush(new PointD(60, 0), new PointD(180, 0), [new GradientStop(0, Colors.SteelBlue), new GradientStop(1, Colors.MidnightBlue)]);

    // "perspective: 300px" and "transform: rotateY(40deg)", both around the center of the image: the turn of the
    // card applies first, then the perspective, combined in three dimensions
    var toCenter = Transform3D.CreateTranslation(-120, -80, 0);
    var fromCenter = Transform3D.CreateTranslation(120, 80, 0);
    var perspective = toCenter.Then(Transform3D.CreatePerspective(300)).Then(fromCenter);
    var turn = toCenter.Then(Transform3D.CreateRotationY(40)).Then(fromCenter);

    image.Draw(canvas => canvas.Transform(perspective, viewer => viewer.Transform(turn, plane =>
    {
        // Everything is drawn in the plane of the card: its shadow, its gradient, its border
        plane.Shadow(new Rgba32(0, 0, 0, 90), new PointD(0, 6), 6, shadowed => shadowed.FillRoundedRectangle(face, card, 12));
        plane.StrokeRoundedRectangle(new Pen(Colors.White, 3), card.Inflate(-10, -10), 6);
    })));

    image.Save(outputPath);
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawWatermark;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawWatermark(string fontPath, string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    var layout = TextLayout.Create("DRAFT", Font.Load(fontPath), size: image.Width / 6.0);
    var ink = new SolidBrush(new Rgba32(255, 255, 255, 110));
    var center = new PointD(image.Width / 2.0, image.Height / 2.0);

    image.Draw(canvas =>
    {
        // Everything the callback draws is turned around the center of the image: the text is drawn as usual
        canvas.Transform(AffineTransform.CreateRotation(-30, center), turned =>
        {
            for (var row = -1; row <= 1; row++)
            {
                var middle = new PointD(center.X, center.Y + (row * layout.Height * 1.5));
                turned.FillText(ink, layout, layout.GetOrigin(middle, TextHorizontalAnchor.Center, TextVerticalAnchor.Center));
            }
        });
    });

    image.Save(outputPath);
}
```
<!-- endref -->

**Clipping**. `canvas.Clip` runs a callback whose drawings only change the inside of a path or of a rectangle: the clip
lasts as long as the callback, and a clip started inside another one keeps what both let through. The edge of a clip
is antialiased like the edge of a shape: each pixel has the area of it that is inside the clip, and the coverage of
whatever is drawn is multiplied by it. A rectangle on whole pixels is exact and free; any other clip keeps one value
per pixel of its bounds, rented when it starts.

**Measuring a path**. A `VectorPath` answers the questions a drawing asks about a shape: `Contains` tells whether a
point is inside (what a fill paints, with either fill rule), `GetLength` gives its length and `TryGetPointAtDistance`
the point at a distance along it with the direction of the path there (to place a marker, an arrow head or a label),
`TryGetClosestPoint` gives the point of the path that is the nearest to a position, with its distance along the path
(what a pointer snaps to, or where a click falls on a line), `Bounds` is the box of its points and control points, and
`GetTightBounds` the box it really reaches. `GetBounds(pen)` is a box that contains what a pen paints, without
computing the stroke: the tight bounds spread by half the width of the pen, its miters and its square caps (on the
side of the path its `Alignment` puts it), so it is larger than the stroke at a corner that is not mitered. `Widen` returns
the path a pen draws when it strokes the path, so that all of these also answer for a stroke: the exact bounds of a stroke
are the tight bounds of the widened path, and a click is on a line when the widened path contains it. A widened path
is the pieces the library fills (one per segment, join and cap, overlapping): fill it with the non-zero rule; it is
not a single outline (see `Simplify` below). Lengths and containment are those of the path as it is drawn, with its curves replaced by their
chords.

**Reading and cutting a path**. `EnumerateSegments` gives the elements of a path, without allocating: each
`PathSegment` is what a method of the builder adds (`MoveTo`, `LineTo`, `QuadraticBezierTo`, `CubicBezierTo`, `ArcTo`,
`Close`), so a path can be written in another format or built again from them. An arc comes as the arc of SVG paths, in
pieces of at most a quarter turn, and a whole ellipse as two arcs of half a turn. `Reverse` returns the path followed
from its end (what the dashes of a pen, a text on the path and the points at a distance start from), and
`Trim(start, end)` the part of it between two distances from its start: the stroke of a path that is drawn
progressively, or the arc of a gauge. A part of a path is made of the chords the path is drawn with: transform a path
before cutting it, not after.

**Combining paths**. `Combine` returns the outline of what two paths fill, combined by a `PathCombineMode`: `Union`
(what one of them fills), `Intersect` (what both fill), `Exclude` (what the first one fills and the second one does
not) or `Xor` (what only one fills). `Simplify` returns the outline of what one path fills: a path that crosses itself,
or whose contours overlap, becomes the boundary of its area, and the pieces of `Widen` become the outline of the
stroke, which can be stroked or combined in turn. The result is closed contours that do not cross each other or
themselves: the outer ones turn clockwise on the image and the holes the other way, so both fill rules paint the same
pixels. It is made of straight segments (the chords the paths are drawn with, their vertices moved to the nearest
1/1024 of a pixel, where every decision is exact): combine paths at the size they are drawn at. The time grows with the
number of segments and of their crossings, and with the square of the number of segments for shapes stacked on each
other: pass a `CancellationToken` for untrusted paths.

**Growing, shrinking and rounding a shape**. `Offset(distance)` returns the outline of what a path fills, moved
outward by a distance or, when it is negative, inward: a margin around a shape, an outline at a distance of a text,
a shape that fits inside another one. Every side moves by the distance, a hole gets smaller when the shape grows, and
what is narrower than twice the distance disappears when it shrinks. The `LineJoin` is the shape of the corners the
sides move away from (`Miter` by default, with a limit as for a pen; with `Round`, the result is what is within the
distance of the shape), and a curve stays round whatever the join. What moves is the boundary of the area, as
`Simplify` gives it: contours that overlap are moved as the one shape they fill. The result is made as `Combine`
makes it, of straight segments, for the size the path has.

`RoundCorners(radius)` replaces each corner between two straight segments of a path by the arc of the circle of that
radius that touches both: a polygon, a star or an arrow with soft corners. The circle touches each segment before its
middle, or else is the smaller one that touches both at half the length of the shorter one, so a rectangle gets the
corners of `AddRoundedRectangle`. The corners where a curve or an arc starts or ends are kept. The result has arcs:
it stays smooth when it is enlarged.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawSticker;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawSticker(string outputPath)
{
    // A star: a point above the center, at the radius of a tip or of a notch, turned by a tenth of a turn each time
    var center = new PointD(60, 62);
    var points = new PointD[10];
    for (var i = 0; i < points.Length; i++)
    {
        points[i] = AffineTransform.CreateRotation(i * 36, center).Transform(new PointD(center.X, center.Y - (i % 2 == 0 ? 44 : 20)));
    }

    // Its corners become arcs of a circle of radius 4
    var star = new VectorPathBuilder().AddPolygon(points).Build().RoundCorners(4);

    // The outline of the star 6 pixels further out, with round corners: the white border of a sticker
    var border = star.Offset(6, LineJoin.Round);

    // And 5 pixels further in: a line that follows the shape, inside it
    var seam = new Pen(new Rgba32(255, 255, 255, 170), 1.5) { DashPattern = [4, 3] };
    var inner = star.Offset(-5, LineJoin.Round);

    using var image = new Image<Rgba32>(120, 120);
    image.Draw(canvas =>
    {
        canvas.Shadow(new Rgba32(0, 0, 0, 90), new PointD(0, 2), 3, shadowed => shadowed.Fill(Colors.White, border));
        canvas.Fill(Colors.Goldenrod, star);
        canvas.Stroke(seam, inner);
    });

    image.Save(outputPath);
}
```
<!-- endref -->

**SVG path data**. `VectorPath.ParseSvgPathData` reads the `d` attribute of an SVG `path` element, the format icons
come in: every command (`M L H V C S Q T A Z`, absolute and relative) with the grammar of SVG, in the coordinates of
the canvas (Y grows downward, as in SVG). The whole text must be valid: an error is a `FormatException` that gives its
position (or `false` from `TryParseSvgPathData`), where a browser draws what comes before it. `ToSvgPathData` writes a
path the same way, with absolute coordinates and numbers that read back as the same values: lines and curves are read
back exactly, an arc to a few units of the last place.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawIcon;format=md-fenced-code-block;dedent=true -->
```csharp
public static string DrawIcon(string outputPath)
{
    // The path data of an icon of 24 by 24 pixels, as the "d" attribute of an SVG file has it
    var heart = VectorPath.ParseSvgPathData("M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z");

    // A path is scaled before it is drawn or cut: its curves stay curves
    var icon = heart.Transform(AffineTransform.CreateScale(4, 4));
    var outline = new Pen(Colors.Crimson, 3) { LineCap = LineCap.Round, LineJoin = LineJoin.Round };

    using var image = new Image<Rgba32>(96, 96, Colors.White);
    image.Draw(canvas =>
    {
        canvas.Fill(Colors.MistyRose, icon);

        // The first three quarters of the outline
        canvas.Stroke(outline, icon.Trim(0, icon.GetLength() * 0.75));
    });

    image.Save(outputPath);
    return icon.ToSvgPathData();
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawRoute;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawRoute(string outputPath)
{
    using var image = new Image<Rgba32>(200, 120, Colors.White);
    var route = new VectorPathBuilder().MoveTo(20, 100).CubicBezierTo(60, 10, 120, 110, 176, 34).Build();

    // An arrow head that points to the right, with its tip at the origin
    var arrowHead = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(-14, -6), new PointD(-14, 6)]).Build();

    // The end of the route and its direction there. The transformation turns the x axis to that direction, then
    // moves the origin to the end of the route.
    var length = route.GetLength();
    _ = route.TryGetPointAtDistance(length, out var tip, out var direction);
    var head = arrowHead.Transform(new AffineTransform(direction.X, direction.Y, -direction.Y, direction.X, tip.X, tip.Y));

    var line = new Pen(Colors.SteelBlue, 3) { DashPattern = [8, 5], LineCap = LineCap.Round };
    image.Draw(canvas =>
    {
        canvas.Stroke(line, route);
        canvas.Fill(Colors.SteelBlue, head);

        // A milestone at each quarter of the way
        for (var i = 1; i <= 3; i++)
        {
            _ = route.TryGetPointAtDistance(length * i / 4, out var milestone, out _);
            canvas.FillCircle(Colors.Crimson, milestone, 4);
        }
    });

    image.Save(outputPath);
}
```
<!-- endref -->

**Coordinates**. Positions are `double` values in pixels (`PointD`, `RectangleD`; the integer `Point` and `Rectangle`
convert implicitly). The origin is the top-left corner, and integer coordinates are pixel boundaries: the pixel
`(x, y)` covers the square from `(x, y)` to `(x + 1, y + 1)`. A rectangle on integer coordinates is filled exactly, and
a one-pixel horizontal line is crisp when it runs through the middle of a row (`y + 0.5`). Shapes are clipped to the
canvas. A coordinate must be finite (`ArgumentException`) and no larger than 16,777,216 in magnitude
(`ArgumentOutOfRangeException`). Once a transformation of the canvas (or the place of a layer) is applied, a drawing
may go beyond that range: as browsers draw what is visible, what goes beyond it is cut by it (the canvas, with a margin
of millions of pixels around it), and only a coordinate that is no longer a finite number is rejected. `PointD` has the usual arithmetic (`+`, `-`, `*`, `/`, `DistanceTo`) and `RectangleD`
the usual helpers (`Center`, `Contains`, `Intersect`, `Union`, `Offset`, `Inflate`).

**Brushes**. A brush is a `SolidBrush`, a gradient, a `PathGradientBrush`, an `ImageBrush` or a `PatternBrush` (see
Images and Patterns below). The `Opacity` of a brush, as `globalAlpha` of the HTML canvas, multiplies the alpha of its paint at each pixel
(at the precision of the canvas, `floor(a opacity + 1/2 + 2^-20)`), before its operator and its blend mode combine it
with the pixels: a paint at half its opacity that replaces the pixels (`CompositeMode.Source`) leaves them at half
their opacity. A `SolidBrush` paints one
color (an `Rgba32` or `Rgba64` converts implicitly to a brush). A `LinearGradientBrush` (between two points), a
`RadialGradientBrush` (around a center, up to a radius) and a `SweepGradientBrush` (around a center, by the angle) paint
the color of their `GradientStop`s at the center of each pixel; `SpreadMode` gives the colors beyond the stops: `Pad`
(default), `Repeat`, `Reflect`, or `Transparent` (nothing is painted where the parameter is outside the range from 0
to 1: a gradient that ends at its first and its last offsets). Gradients are positioned in canvas coordinates, whatever the shape they fill. Two
stops with the same offset make a hard edge, and stops are interpolated on alpha-premultiplied values, so a transparent
stop does not tint its neighbors.

`ColorSpace` says in which space the colors of two stops are mixed. `Encoded` (the default, as in CSS and SVG) mixes
the stored sample values: half way from red to green is a dark olive, (128, 128, 0). `LinearSrgb` mixes light, as
paints of light do: the yellow (188, 188, 0). `Oklab` mixes in a space made for it, where the lightness and the hue
change evenly: (208, 168, 0), with no dull band between two saturated colors. `Oklch` mixes the lightness, the chroma
and the hue of the same space, the hue along the shorter arc as CSS does for `in oklch`: an orange half way, and from
blue to yellow a teal where `Oklab` goes through a gray; the hue of a gray stop is the hue of the other stop. The stops
are taken as sRGB colors; at a stop, the color is the color of the stop in every space.

A radial gradient can spread from a `FocalPoint` that is not its center (a highlight on a sphere). With a
`FocalRadius`, it starts on a circle around that point instead of at the point: the gradient between two circles of
SVG 2 (`fr`) and of the HTML canvas (`createRadialGradient`). Inside the first circle, the color is the first stop (or
the gradient goes on, with the other spread modes). The circles can be anywhere: when the first one is not inside
the second one, the circles of the gradient form a cone, a position has the color of the largest parameter whose
circle goes through it (its radius not negative), as the HTML canvas defines, and what is outside the cone is not
painted. Two circles of the same center and the same radius paint the first stop inside and the last one outside
with `Pad`, and nothing with the other spread modes. A
sweep gradient goes around its center clockwise on the image, from `StartAngle` to
`EndAngle` (a full turn from the right of the center by default); its colors change abruptly at its start angle unless
it ends on the color it starts with, and that edge is not antialiased. Every gradient has a `Transform` that places it
on the canvas: the points of the gradient are given before it applies, so a scaling that is not the same on both axes
makes an elliptical radial gradient, and a rotation turns a gradient without recomputing its points. The constructor
of `RadialGradientBrush` with two half-axes (`radiusX`, `radiusY`) writes that ellipse directly: its circles are
stretched along y around the center (`RadiusY`), the focal point stays where it is given, and `Transform` can still
turn the ellipse.

A `PathGradientBrush` paints the colors of the vertices of a polygon (`Points`, `PointColors`) and of a `Center`
(`CenterColor`; the mean of the points by default), mixed across the triangles from the center to each edge (the
Gouraud shading of a fan of triangles): each pixel has the color at its center, interpolated from the three
corners of its triangle on alpha-premultiplied values, and a point has exactly its color. A position takes the color
of the first triangle that contains it, and nothing is painted outside the triangles: when the center sees every
edge (a convex polygon around it, a star), the triangles are the polygon. Each pixel tests the triangles in turn, so
the cost grows with the number of points.

**Images**. `canvas.DrawImage` draws a frame of another image: at a position (its pixels as they are), stretched to a
rectangle, a region of it stretched to a rectangle (a sprite of a sheet), or placed by an `AffineTransform` (turned,
skewed, mirrored: its edges are antialiased, and a quarter turn or a mirror keeps its pixels exactly), with an optional
opacity. An `ImageBrush`
paints any shape or text with an image: `Transform` places the image on the canvas, `WrapMode` gives the colors around
it (`Transparent` by default, `Pad`, `Repeat` for tiles, `Reflect`), or `WrapModeX` and `WrapModeY` for each direction
(a row of tiles, as `background-repeat: repeat-x` of CSS), `Sampling` chooses how the image is read, and
`Opacity` fades it as the opacity of any brush does. An image drawn on whole pixels without scaling keeps its pixels exactly; its pixels are converted
to the pixel format of the canvas like any brush color, and no color profile is applied.

**Images in perspective**. `DrawImage` with four points draws the image on a quadrilateral, as a plane seen in perspective: a
screenshot on the screen of a photograph, a label on a wall. The four corners of the image go to the four points
(top-left, top-right, bottom-right, bottom-left), straight lines stay straight, and what is further away is smaller.
The points must be the corners of a convex quadrilateral, in order around it (the other way around mirrors the
image); points that are all on a line draw nothing, and a parallelogram is an affine transformation, with the same
pixels. With `ImageSampling.Smooth`, each pixel of the canvas reads what it covers of the image, which changes from
the near side to the far side.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawPerspective;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawOnScreen(string photoPath, string screenshotPath, string outputPath)
{
    using var photo = Image.Load(photoPath);
    using var screenshot = Image.Load(screenshotPath);

    // Where the four corners of the screenshot go on the photo, from its top-left corner, clockwise. The
    // screenshot is larger than the screen: smooth sampling reads every pixel of it that a pixel of the photo covers.
    photo.Draw(canvas => canvas.DrawImage(
        screenshot.Frames[0],
        new PointD(40, 30),
        new PointD(180, 44),
        new PointD(176, 132),
        new PointD(36, 150),
        ImageSampling.Smooth));

    photo.Save(outputPath);
}
```
<!-- endref -->

**Patterns**. A `PatternBrush` paints a drawing that repeats: a callback draws a tile (a rectangle of the coordinates
of the pattern), and the tile is laid side by side in both directions, placed on the canvas by the `Transform` of the
brush. When a shape is painted, the tile is drawn once, on an image of the size it has on the canvas (under the
transformation of the brush, then the one of the canvas), then repeated like the image of an `ImageBrush`: a pattern
that is enlarged stays as sharp as its drawing, and a tile of a whole number of pixels placed on whole pixels keeps
the pixels of its drawing exactly. What the callback draws outside the tile is not painted. The callback must draw
the same thing every time (it runs again when the size of the tile changes, or after another pattern was painted),
and can use any brush but, directly or not, its own pattern. `WrapModeX` and `WrapModeY` say how the tile is laid in
each direction (`Repeat` by default; `Transparent` paints it once in that direction), and `SpacingX` and `SpacingY`
leave a gap after each tile, where nothing is painted: the spacing `background-repeat: space` of CSS computes. A
tile whose image would be much larger than what the canvas shows of it (more than four times the pixels of the bounds
of the clip and 65,536, or beyond the limits of the image) is drawn where the canvas shows it: along each axis where the
canvas shows less than a tile, the callback draws the tiles it shows (two at most, a mirrored one for `Reflect`), each
clipped to its tile, on an image of that part, at the same scale, so that the pixels are those of the whole tile. In
perspective, its image is drawn at the resolution those limits allow instead. The image of the tile has the
precision of the canvas (16-bit samples on a 16-bit canvas); `TilePrecision = PatternTilePrecision.EightBit` draws it
with 8-bit samples on every canvas, as browsers rasterize the drawing of a canvas (a CSS paint worklet) into an 8-bit
bitmap.

A `PatternBrush` built from a `HatchStyle` and two colors repeats a tile of 8 by 8 pixels: lines (`Horizontal`,
`Vertical`, `Cross`, `ForwardDiagonal`, `BackwardDiagonal`, `DiagonalCross`) or a fraction of the pixels
(`Percent10` to `Percent90`, the pixels of lowest rank of the 8 by 8 ordered dither matrix, so that each percentage
lights the pixels of the lower ones). The tile starts at the origin of the canvas, so that shapes painted with the
same hatch line up, and on whole pixels each pixel has exactly the color of its pixel of the tile; a transparent
background leaves the pixels under it unchanged.

A callback is also what replays a drawing: keep a `DrawingAction`, and call it inside `canvas.Transform` wherever the
drawing is needed, at any size.

An `ImageSampling` is `Bilinear` (the default: the four pixels around a position, interpolated), `Nearest` (the pixel
that contains the position: an enlarged image keeps its pixels as squares), `Bicubic` (the sixteen pixels around a
position with the cubic filter of Mitchell and Netravali, `B = C = 1/3`, as browsers draw a scaled image with high
quality: the alpha clamped to the range from 0 to 1 and each color, multiplied by the alpha, to the alpha), `Smooth` or
`Box`.
A rectangle of an image that is not made of whole pixels, as the source rectangle of `drawImage` of the HTML canvas,
is drawn by `DrawImage(frame, RectangleD sourceRegion, destination, sampling)`: the position each pixel reads is brought
into it, to `[left + 1/2, right - 1/2]` when the image is interpolated (the middle when it is narrower than a pixel) and
to `[left, right)` for the nearest pixel, so that the pixels around it weigh as little as they can.
With the first three samplings, an image
that is drawn much smaller than it is skips pixels. `Smooth` reads every pixel of the image a pixel of the canvas
covers, with the weights of the interpolation stretched over them, so that a photograph drawn as a thumbnail has no
aliasing; an image that is not reduced is interpolated as with `Bilinear`, bit for bit. `Box` is the same, but a pixel
of the canvas is the average of what it covers of the image (each pixel of the image weighs the part of it inside the
columns and the rows covered), as the box-filtered mipmaps of browsers approximate it: the filter of `Smooth` is twice
as wide, and blends the edges of stripes that a reduction by a whole factor keeps aligned, which `Box` keeps sharp.
A position on the edge between two pixels takes half of each with the interpolation (a quarter of each of four on
their corner: the pixel of a canvas whose center comes from the corner of a black square in a blue image is three
quarters blue), and the pixel after the edge with `Nearest`. `DrawImage` takes the sampling
as an argument. Painting a pixel then costs as many pixels of the image as it covers: `Resize` an image that is drawn
many times, which also has sharper filters. The frame is read while it is drawn: it cannot be one of the frames the canvas draws on, but
it can be another frame of the same image when the drawing is on one frame (`ImageFrame.Draw`).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawCard;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ComposeCard(string photoPath, string outputPath)
{
    using var photo = Image.Load(photoPath);
    using var card = new Image<Rgba32>(320, 120);

    var shade = new LinearGradientBrush(new PointD(0, 0), new PointD(0, 120), [new GradientStop(0, Rgba32.ParseHex("#2B5876")), new GradientStop(1, Rgba32.ParseHex("#4E4376"))]);
    var portrait = new VectorPathBuilder().AddCircle(new PointD(60, 60), 40).Build();
    var ring = new Pen(Colors.White, 3);
    var rule = new Pen(new Rgba32(255, 255, 255, 160), 2) { DashPattern = [6, 4] };
    var gauge = new Pen(Colors.Gold, 6) { LineCap = LineCap.Round };

    card.Frames[0].Draw(canvas =>
    {
        // The pixels outside the rounded corners stay transparent
        canvas.FillRoundedRectangle(shade, new Rectangle(0, 0, 320, 120), 16);

        // The photo is stretched to the square around the circle, and only changes the inside of the circle
        canvas.Clip(portrait, clipped => clipped.DrawImage(photo.Frames[0], new Rectangle(20, 20, 80, 80)));
        canvas.Stroke(ring, portrait);

        // Dashes of 6 pixels and gaps of 4, then three quarters of a circle from its top, clockwise
        canvas.StrokeLine(rule, new PointD(120, 60), new PointD(300, 60));
        canvas.StrokeArc(gauge, new PointD(270, 30), 14, -90, 270);
    });

    card.Save(outputPath);
}
```
<!-- endref -->

**Layers**. `canvas.Layer(opacity, callback)` runs a callback whose drawings are made on a transparent image of the
size of the canvas, which is then drawn on the canvas as a whole. Shapes that overlap, drawn one after the other with a
translucent paint, show through each other; in a layer they are drawn as they are, then the whole is faded. A layer can
also be combined with the canvas by a compositing operator (see below): `CompositeMode.DestinationIn` makes a mask of
any drawing, a text included, and `DestinationOut` an eraser. A layer starts transparent and has the coordinates of
the canvas; layers and clips nest, and a clip that is active when a layer starts applies to the layer as a whole. The
image of a layer comes from the allocation scope of the image (4 bytes per pixel on an 8-bit color canvas, 8 bytes
otherwise) and is kept while the canvas lasts; a layer costs the clearing and the drawing of the pixels the clip can
show, so start a layer inside `Clip` when it covers a small part of a large canvas. `canvas.Layer(bounds, opacity, callback)` makes a
layer that holds the pixels of a rectangle only (the box of the rectangle on the canvas that the clip can show): its
image is rented when it starts and released when it ends, and what the callback draws outside it is not kept. Its
drawings are made by a canvas of their own, with the coordinates of the canvas, and give the pixels of a layer of the
whole canvas: an element of a page with an opacity costs the pixels of its box, however large the page.

The opacity of a layer is a coverage of what its operator makes, as the edge of a shape is: a pixel gets
`o R + (1 - o) D`, where `R` is the layer combined with the canvas `D` by the operator and the blend mode. With
source-over, this is the layer faded, then drawn. With the other operators it is not the group opacity of CSS, which
fades the layer, then combines it: with `CompositeMode.Plus`, opaque white at an opacity of 0.6 over an alpha of 0.6
gives an alpha of 0.84, where CSS gives 1, and a mask (`DestinationIn`) at half its opacity keeps half of what it does
not cover. For the result of CSS (group compositing), pass `LayerOpacity.Group`:
`canvas.Layer(0.6, draw, CompositeMode.Plus, BlendMode.Normal, LayerOpacity.Group)` multiplies the alpha of every pixel
of the layer by the opacity, as the opacity of a brush does, then combines the layer: 1 in the example, and a mask at
half its opacity keeps half of what it covers and nothing else. No other layer is made. `Layer(bounds, ...)` and
`BackdropLayer` take it too; with source-over, both modes draw the same pixels.

**Groups**. `canvas.Group(options, content, mask)` composites its drawings as a whole, as the transparency groups of
PDF (ISO 32000-2, 11.4) do. An isolated group (`GroupOptions.IsIsolated`, the default) starts transparent, as a layer.
A non-isolated group starts with the pixels under it, which its blend modes mix with: green multiplied in a group over
red is black, where an isolated group keeps green; when it ends, the pixels it started with are removed from its result
(the group alone is tracked on a plane of its own), so that they are not composited twice. In a knockout group
(`GroupOptions.IsKnockout`), each element drawn with `KnockoutElement(opacity, content, mask)` on the canvas of the group
is composited with the initial backdrop of the group, and replaces the elements before it in proportion to its shape:
two overlapping translucent squares show the second one only where they overlap. The callback of an element draws it
with an opacity of 1, the opacity and the mask of the element are applied when it ends, and the alpha of what it draws
counts as its shape; outside a knockout group, an element is drawn as if the callback drew with its opacity. The mask
of a group or of an element multiplies its alpha, by its alpha or its luminance (`GroupOptions.MaskMode`); then the
group is drawn with its opacity and blend mode. A group is a bounded layer of 16-bit samples, with one more plane when
it is not isolated, and an element of a knockout group is another; under a projective transformation, a group is drawn
as an isolated layer.

**Shadows**. `canvas.Shadow(color, offset, blur, callback)` draws what the callback draws with its shadow under it: a
drop shadow, or a glow when there is no offset. The drawings are made on a layer; the shadow is the alpha of that layer
(the shape of everything the callback drew, as a whole), blurred by a Gaussian blur of the given standard deviation,
moved by the offset and painted with the color, whose alpha makes the shadow translucent. A drawing that goes beyond
an edge of the canvas is taken to continue there, so that its shadow does not fade along that edge. The cost grows
with the pixels the clip can show and with the blur: draw the shadow of a small shape inside `Clip`, with room for the
shadow.

**Inner shadows**. `canvas.InnerShadow(color, offset, blur, callback)` draws what the callback draws with a shadow
inside it: the shadow cast on the drawing by what is around it, for a shape that looks cut out or pressed in. It is
computed as a shadow is, from what the layer lacks of opacity, and painted on the drawing only: the alpha of the
drawing does not change. A translucent drawing lacks opacity everywhere and is shadowed all over.

**Blurs**. `canvas.Blur(blur, callback)` draws what the callback draws out of focus: the drawings are made on a
layer, which is blurred as a whole (colors and alpha together, a transparent pixel not darkening its neighbors) by a
Gaussian blur of the given standard deviation, then drawn. `canvas.BlurBackdrop(blur)` blurs what is already drawn
instead, where the clip lets a drawing through: inside `Clip`, it is the frosted glass behind a panel, with the
antialiased edge of the clip. Both read what is around the pixels they change, and take a drawing that reaches an
edge of the canvas to continue beyond it.

**Color filters**. `canvas.Filter(matrix, callback)` gives new colors to what the callback draws, as a whole: the
drawings are made on a layer, each pixel of the layer gets the color a `ColorMatrix` makes of its own one (see
[Processing](#processing): gray levels, a tint, brightness, contrast, a negative), then the layer is drawn. A
photograph drawn in gray levels, an icon drawn in the color of a theme and a disabled button are one call each.
`canvas.FilterBackdrop(matrix)` gives new colors to what is already drawn instead, where the clip lets a drawing
through: the page behind a dialog that is dimmed, a region shown in gray levels, with the antialiased edge of the
clip.

**Filters and backdrops**. A `Filter` is what the `filter` and `backdrop-filter` properties of CSS apply: a
`BlurFilter` (a Gaussian blur, with one standard deviation or one per axis, and an `EdgeMode` for what it reads beyond
the edges of the canvas: by default what is drawn there and transparent beyond it, or the edges of the canvas extended,
wrapped or mirrored), a `ColorMatrixFilter` (the
filter functions that change colors), a `DropShadowFilter` (a shadow of the alpha, moved, blurred and colored, under
the drawing, or alone with `ShadowOnly`, as the shadows of the HTML canvas), and a `FilterChain` of them, applied one
after the other. `canvas.Filter(filter, callback)` draws the callback on a layer that holds what the clip can show and
what the filter moves into it, then filters the layer and draws it. That includes what is drawn beyond the canvas
within the reach of the filter (its blurs, three deviations, and its offsets): a shape out of view casts its shadow on
the canvas, and a shape that goes beyond an edge stays opaque there when it is blurred, as the element of a page that
is scrolled does. The layer is then an image of its own, of the bounds of the clip with a margin of that reach (at most
the largest side of the canvas, and while it has at most four times the pixels of the bounds of the clip and 65,536,
within the limits of the image); filling the whole canvas in it fills the canvas, which fades at its edges.
A `DropShadowFilter` whose offset goes further than that margin is cast from a window of its own: the pixels the clip
shows, moved back by the offset, with the reach of the blur around them. The callback then draws twice (on the layer
and on that window; once, on the window, for a shadow alone), and the cost depends on what the clip shows and on the
blur, not on the length of the offset: a shape 1000 pixels beyond an edge of a small canvas casts its shadow on it.
`canvas.BackdropLayer(filter, bounds, opacity,
callback)` starts a layer with what is already drawn behind a rectangle, filtered (its pixels mirrored beyond its edges,
so that a blurred backdrop does not fade there): the callback draws on it, then it is drawn with its opacity, and the
clip around it gives it its shape. The lengths of a filter are lengths of the drawing: a transformation of the canvas
that enlarges the drawing enlarges its blurs and its offsets. `filter.TryGetBounds(bounds, out result)` gives the
rectangle a filter paints in when what it filters stays in a rectangle (a blur spreads by three deviations, a shadow
moves by its offset), to size an image or a layer that keeps the whole shadow; it returns `false` for a filter that can
paint anywhere (a color matrix that adds to the alpha, a blur that reads the edges of the canvas, a `FilterGraph`
without a region).

A `FilterGraph` is the `filter` element of SVG: primitives computed in order, each one reading the drawing
(`FilterInput.SourceGraphic`), its alpha (`FilterInput.SourceAlpha`), the result of the primitive before it (the
default) or of any earlier one (`FilterInput.FromResult(index)`), in its `Subregion` of the drawing and within the
`Region` of the filter: the canvas by default, or a rectangle that can go beyond it, within the reach of the filter. A primitive computes in linear light by default, as `color-interpolation-filters` of SVG, or in
sRGB with `ColorSpace`. The primitives are `FloodPrimitive`, `OffsetPrimitive` (between pixels too), `MergePrimitive`,
`TilePrimitive`, `ColorMatrixPrimitive` (the types `saturate`, `hueRotate` and `luminanceToAlpha` of SVG are the
matrices `ColorMatrix.CreateSaturation`, `ColorMatrix.CreateHueRotation` and `ColorMatrix.LuminanceToAlpha`),
`ComponentTransferPrimitive` (with a `TransferFunction` per component: table, discrete, linear or gamma),
`CompositePrimitive` (the operators of Porter and Duff, `Lighter` and `Arithmetic`), `BlendPrimitive`,
`GaussianBlurPrimitive` (a deviation per axis, and an `EdgeMode` relative to the subregion of its input),
`DropShadowPrimitive`, `MorphologyPrimitive` (erode or dilate), `ConvolveMatrixPrimitive` (a divisor, a bias added to
every component, `PreserveAlpha`, and an edge mode relative to its own subregion, in which an input is transparent
outside its subregion: the bias colors what is around a smaller input, as browsers do), `DisplacementMapPrimitive`, `TurbulencePrimitive` (the
noise of the reference code of SVG, with `StitchTiles`), and `DiffuseLightingPrimitive` and `SpecularLightingPrimitive`
with a `DistantLight`, a `PointLight` or a `SpotLight`, and `ImagePrimitive`, whose result is what a callback draws
(an image, or an element that `feImage` refers to).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawFilterGraph;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawGlowingRing(string outputPath)
{
    using var image = new Image<Rgba32>(160, 160, new Rgba32(16, 20, 40));

    // <feMorphology in="SourceAlpha" operator="dilate" radius="3"/> <feGaussianBlur stdDeviation="6"/>
    // <feFlood flood-color="#33ccff"/> <feComposite operator="in" in2="(the blur)"/>
    // <feMerge> <feMergeNode in="(the glow)"/> <feMergeNode in="SourceGraphic"/> </feMerge>
    var glow = new FilterGraph(
        new MorphologyPrimitive(FilterMorphologyOperator.Dilate, 3, 3) { Input = FilterInput.SourceAlpha },
        new GaussianBlurPrimitive(6),
        new FloodPrimitive(new Rgba32(51, 204, 255)),
        new CompositePrimitive(FilterCompositeOperator.In, FilterInput.FromResult(1)),
        new MergePrimitive(FilterInput.FromResult(3), FilterInput.SourceGraphic));

    image.Draw(canvas => canvas.Filter(glow, ring => ring.StrokeCircle(new Pen(new Rgba32(255, 255, 255), 6), new PointD(80, 80), 40)));
    image.Save(outputPath);
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawFilters;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawFrostedCard(string photoPath, string outputPath)
{
    using var image = Image.Load(photoPath);
    var card = new RectangleD(24, 24, image.Width - 48, 80);
    var shape = new VectorPathBuilder().AddRoundedRectangle(card, 16).Build();

    // backdrop-filter: blur(16px) saturate(1.4), and filter: drop-shadow(0 4px 12px rgb(0 0 0 / 47%))
    var glass = new FilterChain(new BlurFilter(8), new ColorMatrixFilter(ColorMatrix.CreateSaturation(1.4)));
    var lift = new DropShadowFilter(new Rgba32(0, 0, 0, 120), new PointD(0, 4), 6);

    image.Draw(canvas =>
    {
        // The card starts with what is behind it, blurred, inside its rounded corners, then is lightened
        canvas.Clip(shape, clipped => clipped.BackdropLayer(glass, card, 1, panel => panel.Fill(new Rgba32(255, 255, 255, 60))));

        // The badge and its shadow, as a whole
        canvas.Filter(lift, badge => badge.FillCircle(Colors.Gold, new PointD(card.Right - 30, card.Bottom - 30), 14));
    });

    image.Save(outputPath);
}
```
<!-- endref -->

**Masks**. `canvas.Mask(mask, content, mode)` keeps what one callback draws only where another one draws: the content
is drawn on a layer, the mask on a layer of its own, and each pixel of the content is kept in proportion to the mask
there. With `MaskMode.Alpha` (the default), the mask is the alpha of its drawings, whatever their colors: a shape, a
text, a gradient to a transparent color. With `MaskMode.Luminance`, as in the masks of SVG, it is the luminance of
their colors multiplied by their alpha: white keeps, black removes, and a gradient from white to black fades.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawBadge;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawBadge(string outputPath)
{
    using var image = new Image<Rgba32>(220, 120, Colors.WhiteSmoke);
    var card = new RectangleD(30, 24, 160, 64);
    var face = new LinearGradientBrush(new PointD(0, 24), new PointD(0, 88), [new GradientStop(0, Rgba32.ParseHex("#4F8EF7")), new GradientStop(1, Rgba32.ParseHex("#2456C8"))]) { ColorSpace = GradientColorSpace.Oklab };
    var shine = new SolidBrush(new Rgba32(255, 255, 255, 90)) { BlendMode = BlendMode.SoftLight };
    var mark = new SolidBrush(Colors.White);

    image.Draw(canvas =>
    {
        // The card and its shadow: black at a third of its opacity, 6 pixels down, blurred
        canvas.Shadow(new Rgba32(0, 0, 0, 85), new PointD(0, 6), 5, shadowed =>
            shadowed.FillRoundedRectangle(face, card, new CornerRadii(18, 18, 4, 18)));

        // A light that lightens the top of the card without hiding it, only where the card is
        canvas.Clip(new VectorPathBuilder().AddRoundedRectangle(card, new CornerRadii(18, 18, 4, 18)).Build(), clipped =>
            clipped.FillEllipse(shine, new PointD(110, 20), 120, 34));

        // Two disks that overlap, faded together: they do not show through each other
        canvas.Layer(0.6, layer =>
        {
            layer.FillCircle(mark, new PointD(96, 56), 16);
            layer.FillCircle(mark, new PointD(118, 56), 16);
        });
    });

    image.Save(outputPath);
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawGlass;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawGlassPanel(string photoPath, string outputPath)
{
    using var image = Image.Load(photoPath);
    var bounds = new RectangleD(16, 16, image.Width - 32, 72);
    var panel = new VectorPathBuilder().AddRoundedRectangle(bounds, 14).Build();
    var track = new RectangleD(bounds.X + 16, bounds.Bottom - 26, bounds.Width - 32, 12);
    var fade = new LinearGradientBrush(new PointD(track.X, 0), new PointD(track.Right, 0), [new GradientStop(0, Colors.White), new GradientStop(1, Colors.Black)]);

    image.Draw(canvas =>
    {
        // Frosted glass: what is behind the panel is blurred, then lightened, inside the panel only
        canvas.Clip(panel, glass =>
        {
            glass.BlurBackdrop(6);
            glass.Fill(new Rgba32(255, 255, 255, 70));
        });

        // A light on the panel, out of focus
        canvas.Blur(5, glow => glow.FillCircle(Colors.Gold, new PointD(bounds.X + 30, bounds.Y + 24), 8));

        // The track of a gauge, pressed into the panel: the shadow of its edge falls inside it, from the top
        canvas.InnerShadow(new Rgba32(0, 0, 0, 150), new PointD(0, 2), 1.5, well =>
        {
            well.FillRoundedRectangle(Colors.WhiteSmoke, track, 6);

            // Its level fades out to the right: it is kept by the luminance of a gradient from white to black
            well.Mask(
                mask => mask.Fill(fade),
                level => level.FillRoundedRectangle(Colors.SeaGreen, track, 6),
                MaskMode.Luminance);
        });
    });

    image.Save(outputPath);
}
```
<!-- endref -->

**Operators**. A brush is painted over the pixels (`CompositeMode.SourceOver`) unless its `CompositeMode` says
otherwise. The modes are the compositing operators of Porter and Duff: `Source` replaces the pixels, alpha included (a
transparent color erases), `DestinationOut` erases them where the paint is opaque (an eraser, with any shape and any
gradient), `DestinationOver` paints behind them, `DestinationIn` keeps them where the paint is opaque (a mask),
`SourceIn`, `SourceOut`, `SourceAtop`, `DestinationAtop` and `Xor` combine both, `Clear` erases whatever the paint
is, and `Plus` adds the paint to the pixels, light to light, up to white (`plus-lighter` of CSS, `lighter` of the
HTML canvas). An operator gives the result where a shape covers a pixel entirely; a pixel covered in part is that result for the
part that is covered and what it was for the rest, so a pixel the shape does not touch never changes. `canvas.Clear`
replaces every pixel by a color (`Colors.Transparent` erases the canvas).

**Blend modes**. `Brush.BlendMode` mixes the color of a paint with the color of the pixels before the operator
combines them: `Multiply` darkens (a shadow, a texture, a tint over a photograph), `Screen` lightens (a glow),
`Overlay`, `HardLight` and `SoftLight` do one or the other by the lightness of the pixel or of the paint, `Darken` and
`Lighten` keep the darker or the lighter color, `ColorDodge` and `ColorBurn` brighten or darken the pixel to reflect
the paint, `Difference` and `Exclusion` invert, and `Hue`, `Saturation`, `Color` and `Luminosity` take some of the
three from the paint and the rest from the pixel (`Color` tints a gray image). These are the blend modes of CSS and
of image editors, with the formulas of the W3C. A paint keeps its own color on a transparent pixel, since there is
nothing to mix it with. `canvas.Layer` takes a blend mode too, and mixes a whole drawing with the canvas at once.

**Pens**. A `Pen` is a brush and a width, centered on the path, with a `LineJoin` for the corners (`Miter` by default,
beveled beyond `MiterLimit`; `Round`; `Bevel`) and a `LineCap` for the ends of open contours (`Butt` by default; `Round`;
`Square`). The parts of a stroke that overlap are painted once, also with a translucent paint. A stroke is what the
segment of the width of the pen perpendicular to the path sweeps along it, as the HTML canvas defines it: along a
curve, the ends of the stroke, its caps and the joins with what follows are perpendicular to the curve (not to its
last chord), and a pen wider than the diameter of an arc reaches beyond its center, up to half the width minus the
radius, without painting the rest of the disk around the center.

`Pen.Alignment` puts the stroke on one side of its path: `Inside` what the path fills (an outline that does not make
a shape larger, or go over what is next to it) or `Outside` it (an outline that does not cover the shape: the outline
of a text). Such a stroke is the stroke of a pen twice as wide, of which a clip keeps one side.

`Pen.StartMarker` and `Pen.EndMarker` draw a shape at the ends of the open contours: a `LineMarker`, which is
`Arrow` (the head of an arrow, whose tip is exactly the end of the path), `Circle`, `Square`, `Diamond`, or any
`VectorPath` given in units of the width of the pen, with the end of the contour at its origin and the direction of
the contour along its x axis. A marker is scaled by the width of the pen, turned to the direction of the contour at
that end, and painted with the stroke as one shape; its `Inset` shortens the stroke so that it does not show beyond a
marker that narrows to a point. Closed contours have no marker; with dashes, the markers are at the ends of the
contour.

`Pen.ScalesWithTransform = false` keeps a pen as it is under a transformation of the canvas: the path is transformed,
then stroked, so the width, the dashes and the markers of the pen stay in pixels. A chart, a map or an icon that is
drawn in coordinates of its own, inside `canvas.Transform`, then has lines of the same width at every scale, where
the default pen is enlarged with the drawing (and is wider in one direction when the scaling is not the same along
both axes). A pen of width 0 is a hairline, as in Skia: one pixel of the canvas wide whatever the transformation,
the pen of width 1 that the transformation does not apply to. A dash of length 0 is drawn by its caps alone, turned
to the direction of the path at its point: a dot for round caps, a square for square caps, nothing for butt caps.

**Dashes**. `Pen.DashPattern` cuts a stroke into dashes: a list of lengths in pixels, alternately of a dash and of a
gap, repeated along each contour (`[6, 3]` is dashes of 6 and gaps of 3; a pattern of an odd number of lengths is used
twice, so `[4]` is dashes of 4 and gaps of 4). `DashOffset` is where a contour starts in the pattern. Each dash is
stroked as an open contour: it gets the caps of the pen at both ends and its joins at the vertices it goes through, and
a dash of length zero is drawn by its caps alone (`[0, 5]` with round caps is a dotted line). The last dash of a closed
contour continues with the first one, as one dash, when the contour ends in the middle of a dash. Lengths are measured
along the path as it is drawn, and are not rounded; a pattern must be at least 1/1024 of a pixel long.

**Precision**. On a canvas of 8-bit samples, every draw call rounds its result to 8 bits, and many translucent
drawings on top of each other, nested layers and filters accumulate those roundings. `canvas.HighPrecision(callback)`
draws the callback on a copy of the pixels the clip can show with 16-bit samples and alpha (layers started in it have
16-bit samples too), which starts with the pixels of the canvas and replaces them when the callback returns, as
`CompositeMode.Source` does through the clip: each color is rounded once. It costs 8 bytes per pixel the clip shows,
for each frame, while the callback runs; on a canvas of 16-bit samples, the callback draws on the canvas itself.

**Antialiasing and linear light**. `canvas.Antialiasing(false, callback)` draws with hard edges, as browsers do: a
pixel is painted entirely when its center is inside the shape, and not at all otherwise. An opaque paint then writes its color
and nothing else, which pixel art, masks of whole pixels and images that keep the colors of a palette need.
`canvas.Compositing(CompositingSpace.LinearSrgb, callback)` combines the paints with the pixels in linear light
instead of on the stored sample values: the samples are decoded with the transfer function of the image, combined
and encoded again. The edge of a shape, a translucent paint and a shadow then mix light as light mixes: with sRGB
samples, half of white over black is the gray 188 instead of 128, and a thin light line on a dark background keeps
its weight. The function is sRGB for an untagged image or a profile recognized as sRGB, and the transfer
characteristics of a CICP description otherwise, as for `ResizeWorkingSpace.LinearSrgb`. With another ICC profile,
each channel is decoded and encoded by its tone curve in the profile (Display P3, Adobe RGB and the other
matrix-based profiles, and monochrome ones); a profile that only has lookup tables is rejected. `Blur` and
`BlurBackdrop` then blur light too: red next to green blurs to a yellow, not to a dark olive.

**Colors of a profile**. The color of a paint is sample values of the image, whatever its color space: the red
(255, 0, 0) is the most saturated red an image can hold, which is not the same color on an sRGB image and on a
Display P3 one. `canvas.PaintProfile(profile, callback)` says what the colors of the paints are instead: inside the
callback, a color is a color of an ICC profile (`IccProfile.Srgb` for the colors of CSS, of a design tool or of
`Colors`), and it is converted to the color space of the image, the one of its ICC profile, else of its CICP color
description, else sRGB. The red of sRGB then is (234, 51, 35) on a Display P3 image, and looks the same on both. The
colors that are converted are those the caller gives: solid brushes, the stops of gradients (a gradient that mixes
light or Oklab values mixes the colors as given, then converts each color it obtains), the color of a shadow and of
`Clear`, the brushes of a text. What is samples already is not: images that are drawn (convert them with
`ConvertColorProfile` first), the glyphs a font draws with its own colors, the constants of a color matrix. On a
grayscale image, a color becomes the gray that has its luminance. A color the image cannot hold is clipped,
component by component, and an image of PQ or HLG samples is rejected, since the light of the white of a paint among
them is a choice.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawColorManaged;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawBrandColors(string inputPath, string outputPath)
{
    // A photograph whose ICC profile is not the one of sRGB (Display P3, Adobe RGB): its samples are not sRGB colors
    using var image = Image.Load(inputPath);
    var brand = Rgba32.ParseHex("#E63946");
    var label = new RectangleD(8, 8, 96, 24);
    var outline = new Pen(Colors.White, 2);

    image.Draw(canvas =>
    {
        // The colors of the callback are colors of sRGB: they are converted to the color space of the image, so
        // that the banner has the color its hexadecimal value means, whatever the profile of the photograph
        canvas.PaintProfile(IccProfile.Srgb, managed =>
        {
            managed.FillRoundedRectangle(brand, label, 6);
            managed.StrokeRoundedRectangle(outline, label, 6);
        });
    });

    // The profile of the photograph is kept: the file still says what its samples are
    image.Save(outputPath);
}
```
<!-- endref -->


<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawRendering;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawSprite(string outputPath)
{
    using var image = new Image<Rgba32>(32, 32, Colors.Black);
    var thin = new Pen(Colors.White, 1);

    image.Draw(canvas =>
    {
        // Without antialiasing, every pixel is the paint or what was there: the colors stay those of the palette
        canvas.Antialiasing(enabled: false, sprite =>
        {
            sprite.FillCircle(Colors.Gold, new PointD(16, 18), 9);
            sprite.FillRectangle(Colors.Crimson, new RectangleD(11.5, 14.5, 9, 3));
        });

        // In linear light, the edge of a thin light line on a dark background is not darker than the line
        canvas.Compositing(CompositingSpace.LinearSrgb, light => light.StrokeLine(thin, new PointD(2, 2.3), new PointD(30, 5.7)));
    });

    image.Save(outputPath);
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawOnAnimation;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

**Rendering**. The result is defined exactly and does not depend on the platform:

- **Coverage.** Each pixel is painted in proportion to the area of it that is inside the shape, rounded to the nearest
  1/65536 (ties upward). The area is exact, including where the contours of a shape overlap or intersect themselves: two
  contours over the same half of a pixel cover half of it, not all of it. A pixel that is not covered is left
  untouched, bit for bit.
- **Curves.** Curves, ellipses, arcs, round joins and round caps are replaced by chords that stay within 1/16 of a pixel of
  them, or of the tolerance `canvas.Flattening(tolerance, callback)` sets for what the callback draws (from 1/1024 to
  1/16): shapes drawn as different curves in a page and in its reference then differ less on their edges, at the cost
  of about 1.4 times the chords each time the tolerance is halved. The queries of a path keep 1/16. A part of a round
  join or cap whose box is outside the canvas may be replaced by a single chord: what is between them is not seen. The
  vertices of the chords are not part of the contract.
- **Strokes.** A segment is a rectangle; a chord next to a vertex of a curve is the area between the perpendiculars to
  the curve at its ends (the bisector of the normals of the chords at a vertex, and at an end of the curve the normal
  of the end chord turned away from the next one by half the angle between them), split into two triangles where they
  cross within the pen, with the half of its rectangle on the outer side of the turns. Round joins fill the outer side
  of the vertices of a curve. The pieces are filled as one shape: their overlaps are covered once. `Offset` and a
  synthetic bold, which need what is within a distance of an outline rather than what a pen sweeps, keep a rectangle
  for every chord and the directions of the chords.
- **Colors.** Brush colors are sample values in the color space of the image: no color profile is applied, unless
  `PaintProfile` gives one. A color of a profile is its 16-bit red, green and blue values converted as
  `IccColorTransform` converts them, from the profile to the ICC profile of the image, or to its CICP color space (the
  connection space, the inverse of the matrix of the primaries adapted to D50 by the linear Bradford transform, then
  the function of the transfer characteristics, which clips), and rounded to 16-bit samples; a grayscale image gets
  the one sample of the conversion as red, green and blue. A color is
  then converted to the pixel format of the frame with the rules of `CloneAs` (Rec. 709 luma on grayscale frames,
  rounding to 8 bits on 8-bit frames); its alpha stays the opacity of the paint, also on frames without alpha.
- **Compositing.** The paint is composited over the pixels (source-over) on the stored sample values, with straight
  alpha and exact integer arithmetic, rounded to nearest with ties upward. A full coverage with an opaque color writes
  that color exactly; frames without alpha stay opaque; a pixel that would stay fully transparent is not written and
  keeps its hidden color. A gradient whose stops have the same color paints exactly what a solid brush paints. A
  gradient that mixes its colors in linear light, in Oklab or in Oklch computes them at 16 bits with the power and cube root
  functions of the platform, as `ResizeWorkingSpace.LinearSrgb` does: a sample within about 1e-12 of a rounding
  boundary can differ by one from a platform to another.
- **Operators.** The other compositing operators use the same exact arithmetic and the same rounding. A pixel whose
  alpha becomes zero becomes transparent black. A frame without alpha stays opaque: its pixels get the color the result
  would have over black, so an eraser darkens them. `Plus` clips its sums to one: the alpha of the result is
  `min(as + ad, 1)`, and each of its colors multiplied by its alpha `min(as sc + ad dc, 1)`.
- **Blend modes.** With the colors as fractions of one, a paint `s` on a pixel `d` of alpha `ad` takes the color
  `(1 - ad) s + ad B(s, d)`, where `B` is the function of the mode in "Compositing and Blending Level 1" of the W3C. It
  is computed in double precision with additions, multiplications, divisions and square roots only, rounded to the
  nearest sample value (ties upward), then composited with the exact arithmetic above. The modes that take a color as
  a whole use the luminosity `0.3 R + 0.59 G + 0.11 B`; a gray level is a color without saturation.
- **Images.** The color an image gives a pixel is the color it has at the position the center of the pixel comes from:
  the four pixels around that position, weighted by their distance to it, interpolated at 16 bits on alpha-premultiplied
  values and rounded to nearest (or the pixel that contains the position, with nearest sampling). At the center of a
  pixel of the image, it is that pixel exactly. With smooth sampling, a pixel of the canvas that covers `sx` columns
  and `sy` rows of the image (the lengths of the rows of the inverse of the transformation, at least 1, at most the
  size of the region) reads the pixels whose centers are less than `sx` and `sy` away from that position, with the
  weights `(1 - dx / sx) (1 - dy / sy)`; the weights of the pixels that are outside a transparent image count for the
  alpha. With box sampling, each pixel of the image has the weight of the length of its part inside `[u - sx / 2, u + sx / 2]`
  times the one inside `[v - sy / 2, v + sy / 2]`, in the same order. The opacity of an image multiplies the coverage,
  as a clip does.
- **Floating-point canvases.** On an `RgbaFloat` or `RgbaHalf` frame, paints keep their 16-bit colors and are combined
  with the stored values in double precision, with the formulas of the operators and of the blend modes on fractions of
  one, without rounding to a grid and without clipping the colors; each result is stored to the nearest value of the
  format, and a full coverage with an opaque paint writes `v / 65535`. `Plus` adds light: its colors are not clipped
  (alpha still is). A color matrix (`Filter`, `FilterBackdrop`, `ApplyColorMatrix`) is applied to the values in place,
  colors unclipped and alpha kept in [0, 1]. Linear light uses the values of an image labeled linear as they are; other
  labels decode only samples in [0, 1], and reject the others (and the tone curves of an ICC profile). What reads the
  pixels of a frame back as 16-bit colors (drawing an image or a layer, patterns, filter graphs, shadows, blurs, groups,
  backdrops) rejects samples outside [0, 1] and works on their 16-bit colors.
- **Layers.** A layer is an image with colors and alpha (`Rgba32` on an `Rgba32`, `Bgra32` or `Rgb24` canvas,
  `Rgba64` otherwise) on which the drawings follow the rules of any frame. When it ends, it is drawn as an image is on
  whole pixels: its pixels are converted to the pixel format of the canvas like brush colors, and its opacity
  multiplies the coverage (of what the operator makes, not the alpha of the layer). With `LayerOpacity.Group` and an
  operator other than source-over, the opacity `o` multiplies the alpha `a` of each converted pixel instead, as the
  opacity of a brush does (`floor(a o + 1/2 + 2^-20)`), and the coverage is the one of the clip. A drawing of opaque colors on whole pixels is the same in a layer as on the canvas;
  elsewhere the alpha of the layer is rounded to its samples before it is drawn, which can change a sample by one.
- **Shadows.** With the alpha of the pixels of the layer as fractions of one, extended beyond the layer by its pixels
  on the edges, the shadow of a pixel is the alpha blurred by the weights of a Gaussian blur (see `GaussianBlur`) at
  the position the offset moves it to, interpolated between the four pixels around it, and rounded to a coverage
  (`floor(65536 s + 1/2)`); the color of the shadow is composited with that coverage as a shape is.
- **Transformations.** Under a transformation of the canvas, a shape is flattened as it is given, by chords that
  stay within 1/16 of a pixel of its curves divided by the largest factor the transformation enlarges a length by,
  then its vertices are transformed: on the canvas the chords stay within 1/16 of a pixel. A stroke is the stroke of
  the path as it is given, transformed the same way; with a pen that does not scale with the transformation, it is
  the stroke of the path flattened that way, its vertices transformed first. A brush is placed by its own transformation, then by the one of
  the canvas. A shape with straight sides is, bit for bit, the transformed path. A dashed stroke is cut into the
  dashes that can change a pixel of the canvas: the part of the drawing the canvas shows is the intersection of four
  half-planes of the drawing, around which the dashes are cut.
- **Range of the coordinates.** A vertex is checked once it is transformed, where it is on the canvas (a layer of a
  rectangle is where the canvas has it). From the first vertex of a fill (a shape, a clip path, the rectangle of an
  image, the outline of a glyph) whose magnitude exceeds 16777216, its contours are cut one side after the other by
  the square `|x| <= 16777216`, `|y| <= 16777216` (Sutherland and Hodgman): the points where a contour crosses a side
  are on it exactly and on the segment up to the rounding of one product and one sum, and the parts outside are
  replaced by segments along the sides, which keeps the winding number of every point of the square. The same cut
  applies to each piece of a stroke that goes beyond the square, and to the pieces of a stroke whose pen does not
  scale once its path does (its dashes and joins are those of the whole path). A drawing that stays in the square is
  drawn as it was, bit for bit. A Bézier curve is cut into at most 2^24 chords. A coordinate that is not a finite
  number once transformed throws `ArgumentOutOfRangeException`; the offset of a shadow is limited to four times the
  range.
- **Projective transformations.** A projective transformation whose `W` is the same positive number everywhere is the
  affine transformation it divides to, bit for bit. Otherwise, each contour of a shape, and each piece of a stroke,
  is cut in the coordinates of the drawing by the four half-planes where `|X| <= 16777216 W` and
  `|Y| <= 16777216 W`, then its vertices are divided by `W`: a polygon is, bit for bit, the polygon of the images of
  its vertices. A Bézier curve is halved until each part needs at most 8 chords, cut at uniform parameters, for a
  bound of the factor the transformation enlarges a length by on the part of the rectangle of its control points
  that is seen (`(|A| + D |c|) / W`, `A` the four coefficients of x and y, `c = (M13, M23)`, `D` the distance from
  the origin to a corner of the canvas, `W` its smallest value there); an arc is halved while its chord is further
  from it than 1/16 of a pixel divided by that bound on the triangle that contains it. The bound is limited to
  65536, and a part that is not seen gets 1/1024. The color of a brush is its color at the point the center of the
  pixel comes from through the inverse of the transformation; where that point is beyond the horizon, the pixel has
  no color. A shadow, a blur and a color glyph are drawn on an image of a rectangle of the plane (the part of it the
  clip shows, with the reach of the effect around it, or the box of the glyph), scaled by the largest singular value
  of the four coefficients of x and y divided by `|M33|` (between 1/1024 and 1024, and smaller when the image would
  have more than 4,194,304 pixels, or more than 16 times the pixels of the bounds of the clip), then that image is drawn as an image is, placed by the transformation of its
  pixels, with smooth sampling and nothing around it; the tile of a pattern has the size that scaling gives it. A
  transformation whose determinant is at most 2^-40 of the product of the lengths of its rows shows the plane from
  its edge, and draws nothing.
- **Clips.** The coverage of a pixel under a clip is the product of the coverage of the shape and of the coverage of
  the clip, rounded to the nearest 1/65536 (ties upward). It is the area of what the shape and the clip have in common
  wherever one of them covers the pixel entirely; on a pixel that both only cover in part, it is the product of the two
  areas.
- **Perspective.** An image on a quadrilateral is placed by the projective transformation that maps the corners of
  the image to the four points: the color of a pixel is the color the image has at the position its center comes
  from, by the rules of any image. With smooth sampling, `sx` and `sy` are the lengths of the derivatives of that
  position at the center of the pixel.
- **Patterns.** With `w` and `h` the width and the height the tile and its spacing have on the canvas, its image has
  `max(1, ceil(w - 1/1024))` columns and as many rows computed from `h`, with colors and alpha (8-bit samples on an
  8-bit color canvas, 16-bit samples otherwise, or 8-bit samples with `PatternTilePrecision.EightBit`). The callback draws on it through a transformation that maps the
  tile and its spacing to the image, clipped to the tile when there is a spacing, and the shape is painted with that
  image, laid by the wrap modes of the brush, with bilinear interpolation.
- **Masks.** A mask is a layer combined with the layer of its content by `DestinationIn`. A luminance mask first
  replaces the alpha `a` of each of its pixels by `round(a Y / M)`, where `Y` is the Rec. 709 luma of the pixel on
  its stored samples (the gray level of `Grayscale`) and `M` the largest sample value.
- **Blurs.** With the pixels read as 16-bit colors, their alpha and each color multiplied by its alpha are blurred by
  the weights of a Gaussian blur (see `GaussianBlur`), rows then columns, the pixels beyond those of the canvas being
  the nearest ones; the result is the alpha `floor(A + 1/2 + 2^-20)` and the colors `floor(65535 P / A + 1/2 + 2^-20)`,
  converted to the pixel format like brush colors. Intermediate values are kept in single precision. The blur of the
  backdrop replaces a pixel in proportion to the coverage of the clip. In linear light, each color is decoded
  before it is multiplied by its alpha, and the colors of the result are `floor(65535 encode(P / A) + 1/2 + 2^-20)`.
- **Color filters.** The matrix is applied to the stored samples of the pixels of the layer, or of the canvas, with
  the arithmetic of `ApplyColorMatrix` (see **Color matrices**): 8-bit samples on a layer of an 8-bit color canvas,
  16-bit samples otherwise. A pixel the clip covers entirely gets the samples of the result; a pixel on the edge of
  the clip is replaced in proportion to the coverage of the clip, as a paint of that color drawn with
  `CompositeMode.Source` replaces it.
- **Filters.** The pixels of a filtered layer, or of a backdrop, are read as 16-bit colors, each multiplied by its
  alpha, as fractions of one in single precision. A blur of a deviation `s` under 2 pixels adds the pixels with the
  weights of `GaussianKernel` in double precision; from 2 pixels, it is three box blurs of `d = floor(3 sqrt(2 pi) s / 4 + 1/2)`
  pixels (centered when `d` is odd; otherwise ending on the pixel, starting on it, then of `d + 1` pixels centered on
  it), as SVG defines it; the rows then the columns, the pixels beyond the canvas (or the rectangle of a backdrop) given by the edge
  mode and those beyond the layer being transparent. The transparent edge mode reads the pixels the layer holds beyond
  the canvas; the drop shadow too. A color matrix is applied to the colors without alpha, clipped to
  the range from 0 to 1. A drop shadow is the alpha at the position the offset moves each pixel from (the four pixels
  around it, interpolated), blurred with transparent edges, multiplied by the color of the shadow, under the pixels
  unless the shadow is alone. A drop shadow cast from beyond the margin of the layer is computed on a window of its
  own: the bounds of the clip moved back by the whole part `D` of the offset on the canvas, with `3s + 2` pixels
  around them (fewer when the window would have more than four times the pixels of the bounds of the clip and 65,536,
  or exceed the limits of the image), where the rest of the offset (from 0 to 1) moves the alpha; the
  shadow of a pixel `p` is the one of `p - D` there, and the pixels are those of a layer that holds everything but for
  the sums of the box blurs, which start elsewhere (below a deviation of 2 pixels, they are the same bit for bit). A
  shadow cast from more than four times the range of the coordinates is transparent. The result is rounded to the samples of the layer: the alpha as
  `floor(M a + 1/2 + 2^-20)` and the colors as `floor(M p / a + 1/2 + 2^-20)`. Under a transformation that scales the
  axes, the lengths of a filter of a layer are multiplied by the scale of their axis and its offsets by the
  transformation; under one that turns, skews or is projective, the layer is drawn and filtered on an image of the plane
  of the drawing, which is then drawn transformed. The lengths of a filter of a backdrop go through the linear part of
  the transformation.
- **Filter graphs.** Every primitive of a `FilterGraph` has a result of the size of the filtered surface, in single
  precision, transparent outside its subregion: the pixels whose center is in the box of the image of its rectangle on
  the canvas, within the region of the filter. A primitive that computes in linear light reads each input converted
  once (each color divided by the alpha, decoded by the transfer function of sRGB, multiplied by the alpha again), and
  its result is encoded the same way when a primitive in sRGB reads it, or at the end; an offset, a tile and a flood
  are not converted. An offset interpolates the four pixels around the position it reads; a
  composite, a merge and a blend work on colors multiplied by their alpha (a blend with the straight colors of
  `BlendMode`); a color matrix and the transfer functions work on straight colors and are clamped to the range from 0
  to 1, and an arithmetic composite is clamped and each of its colors limited to its alpha. Sums are made in double
  precision and kept in single precision; the result is rounded as the result of any filter. A morphology works on
  colors multiplied by their alpha, with radii rounded to whole pixels of the canvas; the weights of a convolution
  matrix are one pixel of the canvas apart; a displacement map takes the nearest pixel, and reads its map without the
  alpha of the colors, in the colors of the primitive, and its input in its own colors. A turbulence is the reference
  code of "Filter Effects Module Level 1" transcribed (additions, multiplications, divisions and square roots only), at
  the point of the drawing of the top left corner of each pixel, at most 32 octaves. A lighting takes the normals of
  the surface from the Sobel kernels of the specification (with those of its edges and corners at the edges of the
  region of the filter), the pixels at the top left corner of their pixel and at the height `surfaceScale A`, and the
  height of a light multiplied by the square root of the product of the scales of the axes; its powers use `Math.Pow`.
  The transfer function of sRGB of the filter graphs uses no function of the platform: its power is a polynomial of
  degree 5 on each of 256 segments of every power of two, within a few units in the last place of double precision of
  the exact function, so the colors of the primitives are the same on every platform.
- **Inner shadows.** The shadow is the one of **Shadows** computed from one minus the alpha of the layer; its color
  is combined with the pixels of the layer by `SourceAtop`, with the coverage of the shadow.
- **Aligned strokes and markers.** A stroke on one side of its path is the stroke of a pen twice as wide under a clip
  made of what the path fills with the non-zero rule, or of what it does not fill: the rules of **Clips** apply. A
  point `(x, y)` of a marker is drawn at `P + w (x d + y n)`, with `P` the end of the contour, `w` the width of the
  pen, `d` the unit vector of the direction of the contour there and `n = (-d.y, d.x)`; the stroke goes from the
  distance `w i0` to the distance `L - w i1` along a contour of length `L`, for the insets `i0` and `i1`.
- **Hard edges.** Without antialiasing, a pixel is covered entirely when its center is inside the shape (with its fill
  rule), and not at all otherwise, before the clip and the opacity of a paint apply. On the row `Y`, the edges whose
  range of heights contains `Y + 1/2`, the top excluded and the bottom included, are crossed at that height, and the
  pixels from `floor(xa + 1/2)` to `floor(xb + 1/2)` excluded are covered between a crossing `xa` where the shape starts
  and one `xb` where it ends: a center on the top or the left side of a shape is outside, one on its bottom or its
  right side inside, as when positions are rounded half up. Clips made without antialiasing follow the same rule.
- **Linear light.** Every color sample of the paint and of the pixel is decoded by the transfer function of the
  image (sRGB when it declares none), or by the tone curve of its channel in an ICC profile that is not the one of
  sRGB (evaluated as a color conversion evaluates it, and inverted the same way), the blend mode and the
  operator are applied with the formulas of the stored values in double precision, and the result is encoded and
  rounded as `floor(M v + 1/2 + 2^-20)`; the alpha is combined as it is. A pixel that would not be written on the
  stored values is not written, and an opaque paint that replaces a pixel entirely writes its color exactly. The
  power function of the platform is used: a sample within about 1e-12 of a rounding boundary can differ by one from a
  platform to another.
- **Metadata.** The first shape that changes a pixel removes the EXIF thumbnail, which no longer matches. Cursor
  hotspots and everything else are kept.

**Cost**. Drawing is sequential. A shape rents working storage from the allocation scope of the image (proportional to
the width and height of the canvas, and to the number of edges of the shape), and each `Draw` call allocates a few small
objects; the shapes themselves allocate nothing once that storage has grown to the largest of them, so reuse brushes,
pens and paths and use the overloads with a state and a static callback. A pattern brush is the exception: drawing
its tile allocates a few objects each time the tile is drawn. So do a shadow, a blur and a glyph a font draws with
its own colors under a projective transformation: each is drawn on an image of the plane of its own, rented when it
starts and released when it ends. The time to draw a shape grows with the number
of its edges and with the number of points where they cross each other, which the exact area depends on. One family of
shapes has millions of crossings: a pen far wider than the bends of a long curve it follows. A dashed stroke has the
edges of every dash the canvas shows: what cannot change a pixel of the canvas is not cut into dashes, whatever the
length of the path and the width of the pen, but a very short pattern on a large canvas is millions of dashes. Pass a `CancellationToken`
when shapes come from untrusted input; it is checked by every shape, every 64 rows, every few thousand crossings and
every few hundred dashes.

### Fonts

`Font` and `FontCollection` (namespace `Meziantou.Framework.Imaging.Typography`) read font files. Nothing comes from the
operating system by itself: a font is a file, a stream or bytes given by the caller, or an installed font the caller
asks for by its family.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadFont;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

`Font.LoadInstalled("Arial", weight: 700)` loads the installed face of a family that is the closest to a style, with
the font matching of CSS, and returns `null` when the family is not installed: another family is never used instead.
The installed fonts are the ones `SystemFontCollection.Default` of `Meziantou.Framework.Fonts`, a dependency of the
library, finds on Windows, macOS and Linux; `LoadInstalled` also takes another `SystemFontCollection`, such as the
fonts of chosen directories (`SystemFontCollection.Create`), and `Font.Load(face)` loads one of its faces (a
`SystemFontDescriptor`).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadInstalledFont;format=md-fenced-code-block;dedent=true -->
```csharp
public static string? DescribeInstalledFont(string familyName)
{
    // The installed face of the family closest to the style: a bold face, or the bold instance of a variable font.
    // Null when the family is not installed: another family is never used instead
    var font = Font.LoadInstalled(familyName, weight: 700);
    if (font is null)
        return null;

    return string.Create(CultureInfo.InvariantCulture, $"{font.FamilyName} {font.SubfamilyName}, weight {font.Weight}");
}
```
<!-- endref -->

- **The instance of the style.** When the face is a variable font, its `wght` axis is set to the weight, and for an
  italic its `ital` axis, or without one its `slnt` axis to an oblique of 14 degrees, each value brought into the range
  of its axis: `LoadInstalled("Inter", 700)` gives a bold instance. A face that is not variable is used as it is.
- **A file is read once** while one of its fonts is in use: the faces of a font collection (`.ttc`) share its bytes,
  and a face loaded again with the same options is the same font. Once none of its fonts is referenced, the file is
  read again the next time.
- **Machines differ.** The fonts of a machine, and so the font `LoadInstalled` returns, are not those of another one.

| | Supported |
| --- | --- |
| Containers | TrueType and OpenType files (`.ttf`, `.otf`), font collections (`.ttc`, `.otc`), WOFF 1.0 and WOFF 2.0 (with the transformed glyph, location and horizontal metrics tables, and collections); detected from the content, never from the extension |
| Outlines | TrueType (`glyf`: simple and composite glyphs, scaled, rotated and placed by matching points), CFF version 1 (`CFF `: Type 2 charstrings, subroutines, CID-keyed fonts) and CFF version 2 (`CFF2`) |
| Variable fonts | The axes and the named instances (`fvar`), the mapping of the axes (`avar` versions 1 and 2), and what an instance varies: TrueType outlines (`gvar`), CFF2 outlines (the `blend` operator), advances (`HVAR`, or the phantom points of the glyphs), metrics (`MVAR`), the positions and anchors of `GPOS` (the variation store of `GDEF`) and the features a font replaces in a part of its variation space (feature variations, `rvrn`) |
| Color glyphs | The layers of version 0 of the `COLR` table and the paints of its version 1 (solid colors, linear, radial and sweep gradients, transformations, clips, clip boxes, the 28 composition modes, and their variations in a variable font), with the palettes of the `CPAL` table; glyphs that are images, in the `sbix` table (PNG, JPEG and TIFF) and in the `CBLC` and `CBDT` tables (PNG; index subtables of formats 1 to 5, images of formats 17, 18 and 19) |
| Bitmaps without colors | On request (`FontLoadOptions.UseEmbeddedBitmaps`): the strikes of one bit per pixel of the `EBLC` and `EBDT` tables (index subtables of formats 1 to 5, images of formats 1, 2, 5, 6 and 7), drawn at the size they are made for |
| Character maps | Unicode subtables of formats 0, 4, 6, 12 and 13, variation sequences (format 14), and the Macintosh Roman subtable for ASCII when a font has no Unicode one |
| Names | The typographic family and subfamily when the font has them, the legacy ones otherwise; Windows and Unicode platform names, then Macintosh Roman |
| Metrics | In font units (`Font.Metrics`) or scaled to a size in pixels (`Font.GetMetrics`): ascent, descent, line gap, cap height, x-height, underline and strikethrough. The ascent, descent and line gap come from the table the font designates (`hhea`, or the typographic values of `OS/2` when the font sets `USE_TYPO_METRICS` or has an empty `hhea`). For vertical lines, the advance height and the vertical origin of each glyph (`vhea`, `vmtx`, `VORG`, and their variation in `VVAR`), or, in a font without them, an advance of the ascent and the descent with the glyph centered in it. The baselines of each script (`BASE`, with the variations of its version 1.1), which place the text that is on its side in a vertical line |

A variable font is one file for a whole family: its axes (`Font.VariationAxes`) say what varies, such as the weight or
the width, and `Font.WithVariations` gives the font of an instance.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_VariableFont;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

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
  - The alpha of a solid brush and the opacity of any brush fade the whole glyph, as browsers draw an emoji in a
    translucent color: the glyph is drawn with the color of the text opaque, then its alpha is multiplied. A gradient
    gives its colors to the parts of the glyph that take the color of the text, alpha included, and fades nothing
    else: a gradient of one translucent color draws what font renderers such as HarfBuzz draw with that text color.
  - A color glyph is drawn after the glyphs that are not, in the order of the text. `StrokeText` and
    `TextLayout.ToPath` use the outline the font gives to the glyph itself, which can be empty.
  - A glyph the font gives as an image (`sbix`, `CBDT`) is taken from the smallest strike that is at least as large
    as the size of the text, or from the largest one. Its image is decoded when the layout is created, with the
    decoder of its format (PNG, or in an `sbix` table JPEG or TIFF) and the limits of the library; data that is not
    in the format the font gives for it is an error. The image is scaled down to the size of the text with the default filter of
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
- **Axes that depend on each other.** Version 2 of the `avar` table adds to the value of an axis a delta that depends
  on the values of all the axes: a font can keep its bold condensed style at the weight a designer chose for it, or
  drive several axes from one. The deltas are applied after the mapping of each axis (version 1), all from the same
  values, and the result stays in the range of the axis. `Font.Variations` still gives the values that were asked for.
- **Bitmaps without colors.** Some fonts, mostly fonts of Chinese, Japanese and Korean made for screens of low
  resolution, have for a few sizes a drawing of each glyph made pixel by pixel (the `EBLC` and `EBDT` tables). They
  are not used by default: a glyph is drawn from its outline at every size. A font loaded with
  `FontLoadOptions.UseEmbeddedBitmaps` uses them for a text whose size, in pixels per em, is exactly the size of a
  strike of one bit per pixel. Each pixel of a bitmap is then a square of the outline of the glyph: the text is
  filled, stroked, measured and placed on a path like any other, with any brush, and a glyph drawn at a whole
  position has exactly the pixels of its bitmap. At another size, and for a glyph the strike does not have, the
  outline is drawn. The advances stay those of the outlines. Strikes of several bits per pixel, and glyphs made of
  other glyphs (image formats 8 and 9), are not used.
- **Not supported**: the variation of the hinting values (`cvar`), glyphs in SVG (`SVG `: their outlines are drawn), fonts
  without outlines (bitmap-only fonts), PostScript Type 1 fonts, Type 1
  charstrings and the deprecated `seac` form of `endchar` in a CFF table, the font matrix of a CFF table (the units of
  the `head` table are used), and the hints of CFF outlines (TrueType instructions are run with
  `TextLayoutOptions.Hinting = Full`, see [Text](#text)). A WOFF or WOFF2 file is expanded in
  memory; its metadata and private blocks are not read. Table checksums are not verified.

### Text

A `TextLayout` is a text laid out: its lines, its glyphs and their positions. It measures the text (`Width`,
`Height`, `Baseline`, `Lines`, `InkBounds`) and is what `DrawingCanvas.FillText` and `StrokeText` draw, so that what is
measured is what is drawn. The text types are in the `Meziantou.Framework.Imaging.Typography` namespace.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawText;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

`GetOrigin` turns those measures into the origin to draw a layout at: the one that puts a point of its box (a corner,
the middle of a side, its center, or a point of its first baseline) on an anchor, or that aligns its box in a
rectangle. The box is the one of the layout by default, so that texts line up whatever their letters, or what is drawn
(`TextBounds.Ink`), to center a text by its visible shape:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_PlaceText;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawPayButton(string fontPath, string outputPath)
{
    var font = Font.Load(fontPath);
    var label = TextLayout.Create("Pay now", font, size: 20);
    var caption = TextLayout.Create("Total", font, size: 16);
    var amount = TextLayout.Create("42.00", font, size: 24);

    using var image = new Image<Rgba32>(240, 120, new Rgba32(255, 255, 255));
    image.Draw(canvas =>
    {
        // GetOrigin gives the top-left corner to draw a layout at so that a point of its box lands on an anchor.
        // The ink box centers what is drawn, the tail of the y included; the layout box would center the line
        var button = new RectangleD(20, 64, 200, 40);
        canvas.FillRoundedRectangle(new Rgba32(30, 60, 160), button, radius: 8);
        canvas.FillText(new Rgba32(255, 255, 255), label, label.GetOrigin(button, TextHorizontalAnchor.Center, TextVerticalAnchor.Center, TextBounds.Ink));

        // Two sizes on one baseline: the caption from the left, the amount aligned to the right
        var baseline = 44;
        canvas.FillText(new Rgba32(90, 90, 90), caption, caption.GetOrigin(new PointD(20, baseline), TextHorizontalAnchor.Left, TextVerticalAnchor.Baseline));
        canvas.FillText(new Rgba32(0, 0, 0), amount, amount.GetOrigin(new PointD(220, baseline), TextHorizontalAnchor.Right, TextVerticalAnchor.Baseline));
    });

    image.Save(outputPath);
}
```
<!-- endref -->

A text of one font is `TextLayout.Create(text, font, size)`. A `TextStyle` holds everything else about how a text is
set, a `RichText` gives each part of a text its own style, and `TextLayoutOptions` say how the text is wrapped and
aligned:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawParagraph;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

A text can be made to fit a box, have a background and a contour on some of its words, and leave room in its lines
for what is not text:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawFittedText;format=md-fenced-code-block;dedent=true -->
```csharp
public static double DrawTitleCard(string fontPath, string iconPath, string outputPath)
{
    var font = Font.Load(fontPath);
    using var icon = Image.Load(iconPath);
    using var image = new Image<Rgba32>(320, 120, Rgba32.ParseHex("#14213D"));

    // The styles: a word on a background, a word with a contour around its letters, and the rest
    var title = new TextStyle(font, 64) { Brush = new SolidBrush(Colors.White) };
    var highlight = new TextStyle(font, 64) { Brush = new SolidBrush(Rgba32.ParseHex("#14213D")), BackgroundBrush = new SolidBrush(Rgba32.ParseHex("#FCA311")) };
    var contour = new TextStyle(font, 64) { Brush = new SolidBrush(Colors.White), Outline = new Pen(Rgba32.ParseHex("#FCA311"), 2) { Alignment = PenAlignment.Outside, LineJoin = LineJoin.Round } };

    // Room for the icon in the line, as tall as a capital letter and standing on the baseline
    var capHeight = font.GetMetrics(64).CapHeight;
    var text = new RichTextBuilder()
        .AppendPlaceholder(capHeight, capHeight, capHeight, title)
        .Append(" Draw ", title)
        .Append("more", highlight)
        .Append(" with ", title)
        .Append("less", contour)
        .Build();

    // The largest size at which the text fits the card, less its margins: every length of the styles has it
    var box = new RectangleD(16, 12, image.Width - 32, image.Height - 24);
    var layout = TextLayout.CreateToFit(text, box.Width, box.Height, new TextLayoutOptions { Alignment = TextAlignment.Center });
    var origin = new PointD(box.X, box.Y + ((box.Height - layout.Height) / 2));

    image.Draw(canvas =>
    {
        canvas.FillText(Colors.White, layout, origin);

        // What the placeholder made room for, where the layout put it
        var place = layout.Placeholders[0];
        canvas.DrawImage(icon.Frames[0], new RectangleD(origin.X + place.X, origin.Y + place.Y, place.Width, place.Height), ImageSampling.Smooth);
    });

    image.Save(outputPath);
    return layout.Scale;
}
```
<!-- endref -->

A text can be set in vertical lines, as Chinese, Japanese and Korean often are: `TextLayoutOptions.WritingMode` turns
the lines, and each character is upright or on its side as its script asks:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawVerticalText;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawVerticalText(string fontPath, string outputPath)
{
    var style = new TextStyle(Font.Load(fontPath), 24);

    // Lines go down and follow each other to the left, and are wrapped at the height of the layout. The ideographs
    // and the kana are upright, with the vertical forms the font has for the brackets and the punctuation; the
    // Latin letters are on their side.
    var layout = TextLayout.Create(
        "「日本語」のテキスト。ABC 東京、日本。",
        style,
        new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, MaxHeight = 160 });

    // The layout is as wide as its columns: the first one is on its right
    using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + 20, 180, new Rgba32(255, 255, 255));
    image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
    image.Save(outputPath);
}
```
<!-- endref -->

A layout can also follow a path: `FillText` and `StrokeText` take a `VectorPath` and `TextPathOptions` instead of an
origin.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawTextOnPath;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

The fonts of a text are the ones its `FontStack` is given, so that a text is drawn the same way on every machine.
`FontStack.WithInstalledFallback()` also looks for a character none of them has (an emoji, an ideograph, a symbol)
among the installed fonts:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_InstalledFallback;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawWithInstalledFallback(string fontPath, string outputPath)
{
    // Without fallback, a stack only has the fonts it is given, and a text is drawn the same way on every machine.
    // With it, a character none of them has (an ideograph, an emoji) is drawn with an installed font that has it
    var fonts = new FontStack(Font.Load(fontPath)).WithInstalledFallback();
    var layout = TextLayout.Create("Hello, \u4E16\u754C \U0001F600", new TextStyle(fonts, 32));

    using var image = new Image<Rgba32>((int)Math.Ceiling(layout.Width) + 20, (int)Math.Ceiling(layout.Height) + 20, new Rgba32(255, 255, 255));
    image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(10, 10)));
    image.Save(outputPath);
}
```
<!-- endref -->

| | Supported |
| --- | --- |
| Text analysis | Paragraphs, grapheme clusters, the places a line can end at (UAX #14), bidirectional text (UAX #9), script runs (UAX #24), soft hyphens and hyphenation patterns given by the caller: the algorithms of `Meziantou.Framework.Unicode` |
| Vertical text | Lines set from the top to the bottom that follow each other to the left or to the right (`TextLayoutOptions.WritingMode`), with each character upright or on its side as the Unicode Standard says (UAX #50) or as `TextLayoutOptions.Orientation` says; the vertical forms (`vert`), the rotated forms (`vrt2`), the forms of turned glyphs (`vrtr`), the advance heights, the vertical origins and the baselines (`BASE`) of the font; a few characters set horizontally in the room of one (tate-chu-yoko) |
| OpenType Layout | The `GDEF` table, every type and format of `GSUB` lookup (1 to 8) and of `GPOS` lookup (1 to 9), feature variations, the `kern` table |
| Scripts whose fonts do all the work | Latin, Greek, Cyrillic, Armenian, Georgian, Han, Hiragana, Katakana, Bopomofo, Ethiopic, Thaana, Cherokee and the other scripts that need no analysis of the text: the default features of the font are applied |
| Scripts that join | Arabic (with Persian, Urdu and the other languages written with it) and Syriac; in the universal model, N'Ko, Mongolian, Adlam, Mandaic, Manichaean, Psalter Pahlavi, Hanifi Rohingya, Sogdian, Old Uyghur, Chorasmian and Phags-pa |
| Scripts with a model of their own | Hebrew; Thai and Lao; Hangul; Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada and Malayalam; Khmer; Myanmar |
| Scripts of the universal model | The scripts registered for the Universal Shaping Engine: Tibetan, Sinhala, Javanese, Balinese, Sundanese, Batak, Buginese, Cham, Tai Tham, Tai Le, Tai Viet, Lepcha, Limbu, Meetei Mayek, Newa, Kaithi, Sharada, Grantha, Chakma, Brahmi, Kharoshthi, Tifinagh, Duployan, Egyptian Hieroglyphs and the others |
| Drawing | Composited on the encoded sample values unless linear light is asked for; sequential; an image that is reduced is read with four pixels per pixel of the canvas unless smooth or box sampling is asked for, whose filters are the one of the interpolation and the average of what a pixel covers, not mipmaps (`Resize` has sharper ones), and an image or a pattern gives black as the color of the text to the glyphs a font draws with its own colors; a clip multiplies coverages (on a pixel that the shape and the clip both cover in part, that is not the area they have in common); the path of a stroke (`Widen`) is the pieces of the stroke, not a single outline (`Simplify` makes it one); combined and simplified paths are made of segments, on a grid of 1/1024 of a pixel; coordinates are limited to ±16,777,216 (what a transformation sends beyond is cut by that range) |
| Fonts made before OpenType Layout | The marks of a font without positioning table are placed from the shapes of the glyphs; the letters of an Arabic font without substitution table take the presentation forms the font has as characters, with the ligatures of lam and alef; the marks of a Thai font without substitution table take the forms the font has in the private use area |

The glyphs, the clusters and the positions are compared with those of a reference shaper (HarfBuzz) for fonts of
every script that has a model here, and of some eighty scripts of the universal model: the texts of the test corpus,
and several hundred thousand generated sequences of the characters of each font while the models were written. The
scripts added to the universal model by the latest versions of Unicode (Beria Erfe, Garay, Gurung Khema, Kirat Rai,
Ol Onal, Sidetic, Sunuwar, Tai Yo, Todhri, Tolong Siki, Tulu Tigalari and a few others) are shaped from the properties
of their characters and were not compared with a font.

- **Paragraphs and lines.** A paragraph ends at a line break of the text (line feed, carriage return with or without
  a line feed, vertical tab, form feed, U+0085, U+2028, U+2029); a line break at the end starts an empty line. With
  `MaxWidth`, the lines of a paragraph are wrapped where the Unicode line breaking algorithm (UAX #14) allows it;
  `WordBreak` and `LineBreak` select its variants, and `OverflowWrap` lets a word that is wider than the layout be cut
  between two characters. The white space that ends a wrapped line hangs: it does not count in the width of the line.
  A line is as tall as its fonts ask (the largest ascent and descent, each with half of the line gap), or as
  `LineHeight` says.
- **Scripts without spaces.** Thai, Lao, Khmer and Burmese are written without spaces between words, and the Unicode
  algorithm leaves their lines to a dictionary: without one, such text is only broken at a space, or anywhere with
  `OverflowWrap`. `TextLayoutOptions.LineBreakDictionary` is a `LineBreakDictionary` of
  `Meziantou.Framework.Unicode`, loaded from a list of words: a line can then also end before each word the
  dictionary finds (the text is cut into the fewest words, the characters of no word staying with the word before
  them), never inside a grapheme cluster. One dictionary can hold the words of several languages. No word list is
  provided with the library; the ICU project publishes some.
- **Hyphenation.** A word is broken at a soft hyphen (U+00AD), and the line then ends with a hyphen. With
  `Hyphenation = Automatic`, a word is also broken where the patterns of its style allow it: `TextStyle.Hyphenator` is
  a `Hyphenator` of `Meziantou.Framework.Unicode`, loaded from TeX hyphenation patterns. No pattern is provided with
  the library.
- **Direction.** Each paragraph is ordered by the Unicode Bidirectional Algorithm (UAX #9): left-to-right and
  right-to-left text can be mixed, mirrored characters are mirrored, and each line is put in display order.
  `Direction` imposes the base direction of the paragraphs; by default it is the one of their first strong character.
  `Alignment` is relative to it: `Start` is the left of a left-to-right paragraph and the right of a right-to-left one.
- **Vertical text.** With `WritingMode = VerticalRightToLeft`, lines go from the top to the bottom and follow each
  other to the left, as Chinese, Japanese and Korean are set; with `VerticalLeftToRight` they follow each other to the
  right, as Mongolian is. Everything that is said of the width of a horizontal line is then said of the height of a
  vertical one: `MaxHeight` is where lines are wrapped and what they are aligned in, `MaxWidth` is the room the lines
  must fit, `LineHeight` is the width of a line, `Alignment` places a line from its top, and `FirstLineIndent` moves
  the first line of a paragraph down. `Width` and `Height` stay the size of the layout on the image.
  - `Orientation` says which characters are upright. By default (`Mixed`), each character is set as the Unicode
    Standard says (the Vertical_Orientation property, UAX #50): ideographs, kana, Hangul syllables, emoji and most
    symbols are upright, one under the other; the letters of the scripts that are written in horizontal lines
    (Latin, Cyrillic, Greek, Arabic, Hebrew, Devanagari) are on their side, turned a quarter of a turn clockwise,
    as a horizontal line read from the top. A character the standard sets upright only with a glyph of its own (a
    bracket, a long vowel mark) is upright when the font has a vertical form for it (`vert`), and on its side
    otherwise. `Upright` sets every character upright, and `Sideways` every character on its side.
  - A style that turns `vrt2` on uses the rotated glyphs of its font: a character the font has a rotated glyph for
    is upright with that glyph, which is already turned, in the place of the vertical forms, and the characters it
    has none for are turned as they are without it.
  - An upright glyph advances by its advance height and is placed by its vertical origin, with the `vhea`, `vmtx`
    and `VORG` tables of the font (and `VVAR` for an instance of a variable font). A font without these tables,
    which is every font that was not made for vertical text, advances by its ascent and its descent, and each glyph
    is centered in that advance and on the line.
  - A line has a central baseline, in the middle of its width: upright glyphs are centered on it, and the text that
    is on its side has the middle of its font on it: the middle of the ideographic em box the `BASE` table of the
    font gives for its script (with the variations of the table in an instance of a variable font), or else the
    middle of its ascent and its descent. The line is as wide as its fonts are tall (ascent, descent and line gap). With `Sideways`, the line is a horizontal line turned a quarter of a turn
    clockwise, with its baseline, its alignment of fonts of several sizes, and its placeholders turned with it.
  - Upright runs are shaped from the top to the bottom with the vertical forms of the font (`vert`) and with the
    features that do not depend on the direction (`ccmp`, `locl`, `rlig`, `mark`, `mkmk`, `abvm`, `blwm`): the
    ligatures, the kerning and the contextual forms a font has for horizontal text are not applied, and Arabic or
    Syriac letters set upright do not join. `TextStyle.Features` asks for the other vertical features of a font
    (`vkrn`, `vpal`, `vhal`, `valt`), as the reference shaper does. The features a style turns on for one direction
    apply to the other one where the font has their counterpart: `kern`, `palt`, `halt`, `chws` and `hkna` are
    applied to upright text as `vkrn`, `vpal`, `vhal`, `vchw` and `vkna`, and the vertical ones to the text that is
    on its side as the horizontal ones. The text that is on its side is shaped as a horizontal line, with the forms
    its font has for glyphs that are turned (`vrtr`). The forms of a vertical line (`vert`, `vrt2`, `vrtr`) are
    looked for in the whole font when the script of a run does not list them. Right-to-left text that is on its side
    reads from the bottom of the line, as the bidirectional algorithm orders it; upright, it reads from the top.
  - A few characters can be set horizontally in the room of one upright character (horizontal-in-vertical,
    tate-chu-yoko): the text of a style whose `IsCombinedUpright` is set, and the runs of digits that are not longer
    than `TextLayoutOptions.CombineUprightDigits` (2, 3 or 4; 0, the default, for none), such as a date or a number
    in a column of Japanese text. Such a text is one unit of its line: it is shaped as a horizontal line of one font,
    centered across the line, and advances by one em down the line, with the middle of its font in the middle of
    that em. When it is wider than one em, it takes the forms its font has for half, a third or a quarter of an em
    (`hwid`, `twid`, `qwid`) for two, three or four characters, then is narrowed to one em. A line never ends inside
    it, and the letter spacing, the word spacing and justification leave it as it is. It is the
    `text-combine-upright` property of CSS; it changes nothing with `Sideways`.
  - An underline is on the left of the line, where it is under the text that is on its side, and an overline on its
    right; the strikethrough of upright text is in the middle of the line. A placeholder takes its height down the
    line and is centered on it.
- **Alignment.** Lines are aligned in `MaxWidth`, or in the width of the widest line without it. `Justify` stretches
  each line to that width by widening U+0020 spaces and U+00A0 no-break spaces, except the last line of a paragraph.
  A line without these spaces stays start-aligned: there is no inter-character justification for CJK text.
  `FirstLineIndent` indents the first line of each paragraph, and `TabSize` sets the tabulation stops.
- **Fitting.** `MaxLines` and `MaxHeight` cut the text (the first line is always kept); `Trimming` ends the last line
  with an ellipsis after the last character or the last word that leaves room for it, and also trims a line that is
  wider than `MaxWidth` when lines are not wrapped. `IsTruncated` says that a part of the text is not in the layout.
- **Styles.** A `TextStyle` has a `FontStack` (a font, then the fonts used for the characters the ones before lack; a
  `Font` converts to a stack of one font; an instance of a variable font is a font) and a size, and optionally a brush, OpenType features to turn on or off
  (`new FontFeature("liga", 0)`, `new FontFeature("smcp")`), letter and word spacing, decorations (underline,
  strikethrough, overline, drawn with the metrics of the font), a language, a synthesis and hyphenation patterns.
  Spans that only differ by what is painted are shaped together: letters of two colors still kern and join.
- **Backgrounds and contours.** `TextStyle.BackgroundBrush` paints behind the text of a style: a highlight, the
  background of a label. On each line it is one rectangle, as wide as the advances of the text of the style (the
  white space between its words included, not the one that hangs at the end of a wrapped line) and as tall as the
  primary font of the style, from its ascent to its descent. `TextStyle.Outline` is a pen that strokes the outlines
  of the glyphs of a style: centered on the outline, or around the glyphs with `PenAlignment.Outside`, which leaves
  them as they are filled. `FillText` paints the backgrounds of the whole layout, then its text, then the strokes:
  a background never hides a glyph, and a stroke is over the text. They are not outlines of the text: `StrokeText`
  and `ToPath` leave the backgrounds out, and the pen of `StrokeText` strokes every glyph.
- **Fitting a box.** `TextLayout.CreateToFit(text, style, width, height)` lays out a text at the largest size, up to
  the size of its style, at which it fits a box: a title that fills a card, a label that never overflows its button.
  Every length of the styles is multiplied by one factor, which `TextLayout.Scale` gives (the sizes of every span, the
  spacings, the indent, a fixed line height, the placeholders). Lines are wrapped at the width of the box as the
  options say (the columns of a vertical text at its height), and the text fits when none of it is cut, no line is
  wider than the box and its lines are not taller.
  The factor is the one the box is to the text when its lines stay the same, or else the largest of those found by
  halving the range twelve times, each with a whole layout: creating such a layout costs up to fifteen. A text that
  does not fit at `minimumScale` (0.1 by default) is laid out at it, and cut as the options say.
- **Placeholders.** `RichTextBuilder.AppendPlaceholder(width, height, baseline, style)` leaves room in a line for what
  the text does not draw: an icon, a badge, an image. The placeholder is one character of the text (U+FFFC): it
  moves with the words around it when lines are wrapped and aligned, a line can end before or after it, and its line
  is at least as tall as it needs (`baseline` above the baseline of the line, the rest of `height` below).
  `TextLayout.Placeholders` gives the rectangle of each one, in the order of the text, for the caller to draw in.
- **Fonts of a character.** A character and the marks and selectors that follow it are drawn with the first font of
  the stack that has all of them. An emoji is first looked for in the fonts of its presentation (Unicode Technical
  Standard #51): the fonts that have color glyphs for a character that is an emoji by default (U+1F600), that is
  followed by the emoji presentation selector (U+FE0F), or that starts an emoji sequence (a skin tone, characters
  joined by a zero width joiner); the fonts without color glyphs for a character followed by the text presentation
  selector (U+FE0E). A character that is text by default and has no selector, such as a digit or the copyright
  sign, is looked for in the order of the stack like any character. The presentation is a preference: when no font
  of that kind has the character, the stack is searched in its order, and a color font is not drawn without its
  colors.
- **Installed fonts as fallback.** A stack made with `FontStack.WithInstalledFallback(fonts, options)` looks for a
  character, with its marks and selectors, that none of its fonts has among the installed fonts
  (`SystemFontCollection.Default`, or the collection it is given), in this order: the families of
  `InstalledFontFallbackOptions.PreferredFamilies`; for an emoji presented with colors, the color emoji font of the
  platforms (Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji); the families the platforms usually use for the
  script of the character (Microsoft YaHei, PingFang SC or Noto Sans CJK SC for the Han ideographs, the Noto family
  named after a script, the fonts of symbols), those of the platform the code runs on first; then every family that
  has the character, those whose closest face has the weight and the italic of the primary font first, by family
  name and by file. In a family, the face is the one closest to the style of the primary font, at the instance of that
  style when it is a variable font. The characters of an installed face are read from its file when the fallback
  first needs them (`SystemFontDescriptor.Covers`), within a bounded amount of memory, and a file that cannot be read
  or loaded is skipped. The font found for a character is kept by the stack, which its layouts share: each font is
  loaded once. A stack without fallback never uses an installed font; with fallback, the text depends on the fonts of
  the machine. Fallback does not use `TextStyle.Language`: Han ideographs use the simplified-Chinese family list
  regardless of the language. Put the desired Japanese, Korean or traditional-Chinese fonts in the stack or in
  `PreferredFamilies` when their regional forms matter.
- **Synthetic bold and oblique.** `TextStyle.Synthesis` makes a replacement for a bold or an italic font the caller
  does not have, by geometry alone. `Oblique` slants every outline to the right around the baseline, by a quarter
  of the height (about 14 degrees); marks follow their letter, advances do not change and decorations stay straight.
  `Bold` thickens the glyphs of a filled text: each glyph is filled together with a stroke of its outline, 1/24 of
  the size wide, whose corners stay sharp unless they are sharper than 60 degrees, so every edge moves out by 1/48
  of the size; each glyph that takes room takes 1/24 of the size more and is drawn in the middle of it. The bold is a
  way to fill: `StrokeText` and `TextLayout.ToPath` use the outlines of the font, slanted but not thickened, and
  `InkBounds` contains the thickened glyphs. Glyphs a font draws with its own colors are drawn as they are. A bold
  or italic font, or an instance of a variable font, is always a better choice when there is one.
- **Language.** `TextStyle.Language` is the language of the text, as a BCP 47 language tag (`"sr"`, `"tr"`,
  `"zh-Hant"`). A font can draw the same characters differently for different languages (the Cyrillic letters of
  Serbian and Bulgarian, the ideographs of Chinese, Japanese and Korean, the ligatures around the dotless i of
  Turkish), with a language system of its OpenType Layout tables for each one. The language selects that language
  system: the first one the font has among the tags the registry of OpenType gives for the language, or else the
  default language system of the font, as without a language.
  - A tag of the registry is for one or several ISO 639 codes, and a code can have several tags: the tag of fewer
    languages is tried first (`ga` is Irish, then Irish Traditional). A language of a macrolanguage also has the
    tags of the macrolanguage (`arb`, Standard Arabic, is drawn as `ar`), and a deprecated subtag those of the subtag
    that replaces it (`iw` is `he`).
  - The script, the region and the variants of the tag count where the registry has a tag for a written form:
    Chinese (`zh` and `zh-Hans` are simplified, `zh-Hant` and `zh-TW` traditional, `zh-HK` and `zh-MO` the forms of
    Hong Kong and Macao, then traditional; `yue` is `zh-HK` and `lzh` is traditional unless the tag says otherwise;
    `zh-Bopo` and `zh-Latn` are phonetic), polytonic Greek (`el-polyton`), the forms of Syriac (`syr-Syre`,
    `syr-Syrj`, `syr-Syrn`), Khutsuri Georgian (`ka-Geok`), phonetic transcriptions (the variants `fonipa`,
    `fonnapa` and `fonupa`), and the Romanian of Moldova (`ro-MD`).
  - The tag is not checked against the registry of language subtags: a language the registry of OpenType has no tag
    for changes nothing. That is the case of a macrolanguage without tag of its own (`sh`; `no` is taken as Bokmål
    and `qu` as the Quechua of Cusco), of the tags kept from older standards (`i-klingon`) and of private use
    subtags.
  - The language only selects the language system: it does not choose fonts, and does not change where lines
    break.
- **Shaping.** Each run of one script, direction and font is shaped with the OpenType Layout tables of the font:
  - the characters are put in the form the font has glyphs for: a character the font lacks is decomposed into parts
    it has, and a character and its marks use the composed glyph when the font has it, so `é` typed as one character
    or as `e` and a combining accent gives the same result; a variation sequence selects its glyph. The marks of a
    character are put in the order they are drawn in, which for Arabic and Hebrew is not the canonical order of
    Unicode;
  - the `GSUB` table substitutes glyphs (ligatures, contextual and localized forms, composition) and the `GPOS` table
    positions them (kerning, marks on their base or on a component of a ligature, marks on marks, cursive
    connection), with the features a client applies by default to horizontal text: `ccmp`, `locl`, `rlig`, `calt`,
    `clig`, `liga`, `rclt`, the direction features (`ltra`, `ltrm`, `rtla`, `rtlm`), `mark`, `mkmk`, `abvm`, `blwm`,
    `curs`, `dist`, `kern`, and `frac`, `numr`, `dnom` around a fraction slash (`1⁄2`);
  - a font without `GPOS` kerning is kerned by its `kern` table; a mark does not move the pen;
  - a variation sequence the font has (a character and a variation selector, with a glyph of its own or with the
    glyph of the character) is one character: its selector leaves no glyph;
  - the marks of a font that has no positioning table at all are placed from the shapes of the glyphs, as no
    specification describes and as the reference shaper does: a mark goes where its Unicode combining class says
    (above, below, attached, on a side; for the Hebrew, Arabic, Syriac, Lao and Tibetan classes, where the mark
    goes in its script), centered in the advance of its letter or aligned with an end of it, a sixteenth of the em
    from its ink; the marks of one class stack, and the marks on a ligature are placed on the part of the component
    they follow. A mark below that the font draws lower stays there, and a mark above that the font draws higher
    is brought down by half of the distance. The fonts of the scripts that have a model of their own are not
    concerned, nor those of Thai and Lao;
  - spaces of a fixed width and the non-breaking hyphen that a font lacks are made from its space and its hyphen;
  - the scripts whose letters join, **Arabic** (with Persian, Urdu and the other languages written with it) and
    **Syriac**, are shaped as their fonts are made for: each letter takes the isolated, initial, medial or final form
    its neighbors ask for (`isol`, `init`, `medi`, `fina`, and the forms of the Syriac alaph, `med2`, `fin2`,
    `fin3`), marks and format characters do not interrupt a word, a tatweel or a zero width joiner joins and a zero
    width non-joiner separates, and the features are applied in the stages of the Arabic and Syriac specifications
    (composition, forms, required ligatures such as lam-alef, alternates, then the other ligatures). A zero width
    joiner between two letters keeps them from forming a ligature. The hanging style (nastaliq) is drawn with the
    cursive attachments of its fonts. The Syriac abbreviation mark stretches over the word that follows it (`stch`).
    An Arabic font without substitution for the script, as fonts were made before OpenType Layout, has the forms
    of its letters as characters (the Arabic Presentation Forms blocks): each letter is drawn with the character
    of its form when the font has it, and a lam with the alef that follows it with their ligature;
  - **Hebrew** points and cantillation marks are placed by the font; a font that has no feature to place them (or a
    caller that turns `mark` off) gets the precomposed letters of the Alphabetic Presentation Forms block instead;
  - **Thai** and **Lao**: the sara am is drawn as its two parts, a ring above the consonant and a vowel after it, and
    the ring goes under the tone mark or the vowel that is already above the consonant; the marks below a consonant
    are in the order they are stacked in. A Thai font without substitution for the script, as fonts were made before
    OpenType Layout, has other forms of its marks as characters of the private use area (at the code points of the
    fonts of Windows or of Mac OS): a tone mark without vowel above is drawn with its lower form, a mark above a
    consonant with an ascender (po pla, fo fa, fo fan) with its form on the left, a vowel below do chada or to patak
    with its lower form, and yo ying and tho than lose their descender before a vowel below, when the font has
    these characters;
  - **Hangul**: letters (jamo) that make a syllable the font has are drawn with the glyph of the syllable, and a
    syllable followed by a trailing consonant with the glyph of both; the other blocks (Old Hangul, or a font without
    the syllable) are drawn with their letters, shaped by position (`ljmo`, `vjmo`, `tjmo`). A tone mark of Middle
    Korean is drawn before its syllable, or on a dotted circle when it has none;
  - the **Indic scripts** (Devanagari, Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada, Malayalam) are
    shaped syllable by syllable, as the specifications of Microsoft for these scripts describe: the base consonant
    of a syllable is found from what the font does with its consonants (half forms, forms below and after the
    base); a vowel sign that is drawn before the syllable is moved there, and a two-part vowel sign is drawn with
    its parts; a ra and a halant at the start of a syllable become a reph, placed where the script puts it; the
    features of the font are applied in the order of the specifications (`nukt`, `akhn`, `rphf`, `rkrf`, `pref`,
    `blwf`, `abvf`, `half`, `pstf`, `vatu`, `cjct`, then `init`, `pres`, `abvs`, `blws`, `psts`, `haln`), inside each
    syllable. A zero width joiner after a halant asks for a half form and a zero width non-joiner for a visible
    halant. A vowel sign or a mark without a consonant is drawn on a dotted circle, and so is a vowel sign after a
    vowel letter it would be mistaken for another letter with. A font made for the first version of the
    specifications (the script tags without a digit) is shaped as that version asks. `liga` is not applied to these
    scripts unless the caller asks for it;
  - **Khmer**: a syllable is a consonant, the letters written under it (each with the coeng) and its vowel and signs.
    A vowel drawn before the syllable is moved there, a vowel with two parts is drawn with both, and the subscript
    ro, which is drawn on the left of the syllable, goes to its start. The forms of the subscripts (`pref`, `blwf`,
    `abvf`, `pstf`, `cfar`) are applied together inside each syllable, then the presentation forms (`pres`, `abvs`,
    `blws`, `psts`, `clig`). A vowel or a sign without a consonant is drawn on a dotted circle. `liga` is not
    applied unless the caller asks for it;
  - **Myanmar** (Burmese and the other languages of the script, with the extended blocks): the kinzi, written before
    the consonant it is drawn above, is moved after it; the vowel e and the medial ra are moved to the left of the
    syllable; an anusvara written after a vowel below is put before it. The features of the font are applied in the
    order of the specification (`rphf`, `pref`, `blwf`, `pstf`, then `pres`, `abvs`, `blws`, `psts`), inside each
    syllable. A font that only has the script under the tag of the first version of the specification (`mymr`) is
    not reordered: its features are applied as those of a simple script;
  - the scripts of the **universal shaping model** (Tibetan, Sinhala, Javanese, Balinese, Tai Tham, Cham, Lepcha,
    Newa, Kaithi, Mongolian, N'Ko, Adlam, Duployan and some eighty others, the scripts registered for the
    "Universal Shaping Engine" of Microsoft): the class of each character comes from its Unicode properties, and the
    text is cut into clusters of a base with its stacked or subjoined consonants, its vowels, their modifiers and
    its final consonants, each in the order above, below, after. The features of the font are applied in the order
    of the specification (`locl`, `ccmp`, `nukt`, `akhn`; `rphf`; `pref`; `rkrf`, `abvf`, `blwf`, `half`, `pstf`,
    `vatu`, `cjct`), inside each cluster; then the reph goes after the base and what is drawn before the base (a
    vowel, the glyph `pref` made) to the start of the cluster, or after the last halant that is still there. The
    letters of the scripts that join (Mongolian, N'Ko, Adlam, Mandaic...) take their form (`isol`, `init`, `medi`,
    `fina`) from their neighbors as Arabic letters do, and a free variation selector of Mongolian chooses the form
    of its letter; in the other scripts the form is the place of the cluster among the clusters around it. Then
    come `abvs`, `blws`, `haln`, `pres` and `psts`. A cluster without base is drawn on a dotted circle. An Indic font
    made for this model (the script tags that end with 3) is shaped with it.

  A font that has no feature for the script of a text, only for a default script (`DFLT`, `latn`), was made for no
  model: its features are applied as those of a simple script. A font without substitution table at all is shaped
  with the model of the script.

  Control characters are not drawn and format characters (joiners, variation selectors, the soft hyphen where the line
  is not broken) are invisible. A character no font of the stack has is drawn with the missing glyph of the primary
  font, and `HasMissingGlyphs` says so: nothing throws.
- **Rendering.** The outlines are scaled to the size and placed at fractions of a pixel, then filled or stroked like any
  path: exact coverage, the same compositing, the same flattening tolerance. By default nothing is snapped to the
  pixel grid and the hinting of the font is not applied (see **Hinting** below); the same text gives the same pixels
  on every platform, with or without hinting. The glyphs and the
  decorations of one paint are one shape: where glyphs overlap, the brush is applied once. The brush of `FillText`
  paints the spans that have none; `StrokeText` strokes every outline with its pen. `TextLayout.ToPath` gives the
  outlines as a `VectorPath`; filling or stroking it changes the same pixels to the same values as a layout of one
  paint.
- **Hinting** (`TextLayoutOptions.Hinting`, `None` by default). Small text (9 to 16 pixels) is blurry when its edges
  fall across pixels; hinting fits the outlines to the pixel grid of the layout:
  - `Light`: the outlines are moved along the vertical axis only, without the instructions of the font. The
    alignment zones of the font (baseline, x-height, cap height, ascenders, descenders, measured on reference letters,
    with the overshoot of the round letters kept from half a pixel) go to whole pixels, horizontal stems become a
    whole number of pixels thick, and the rest of the outline follows; a gap of three quarters of a pixel keeps a
    pixel (the dot of an i). The advances and the horizontal positions do not change. Every outline format.
  - `Full`: the TrueType instructions of the font (`fpgm`, `prep`, `cvt `, the instructions of each glyph) fit the
    outlines along both axes, with the version 40 interpreter of FreeType for black and white pixels
    (`FT_LOAD_TARGET_MONO`) as the reference: on the fonts of the corpus (Droid Sans, Open Sans and Roboto, hinted by
    hand), every point of every glyph at sizes 9 to 16, 20 and 24 is where FreeType puts it. The advances are those of
    the hinted glyphs, whole pixels, plus what the positioning of the text adds, rounded; each glyph starts on a whole
    pixel. A font without instructions (CFF outlines, or neither a font program nor a control value program), an
    instance of a variable font (the variations of the control values, `cvar`, are not applied) and a font whose
    programs break a rule of the specification are hinted as with `Light`; a glyph whose instructions break one is
    drawn unhinted. The programs are untrusted: every instruction is charged to `MaxGlyphComplexity` (a program that
    loops forever is an `ImageResourceLimitException`), the calls are nested at most 64 deep, and the stack, the
    storage, the functions and the twilight zone are sized from `maxp` and charged to `MaxLiveAllocationBytes`.

  The baseline of each hinted glyph is on a whole pixel of the layout, and what the layout measures is what it draws.
  The grid is the pixel grid of the layout: draw it at a whole-pixel origin and without a transform that scales or
  rotates the canvas. A vertical layout is not hinted. The contrast across the horizontal edges (the sum of the
  differences between vertically neighboring pixels divided by the coverage, at 10 to 16 pixels, averaged) goes up
  with hinting:

  | Font | None | Light | Full |
  | --- | --- | --- | --- |
  | Droid Sans | 0.730 | 0.807 (+10.6 %) | 0.784 (+7.5 %) |
  | Open Sans | 0.792 | 0.876 (+10.5 %) | 0.864 (+9.1 %) |
  | Roboto | 0.729 | 0.798 (+9.5 %) | 0.778 (+6.8 %) |

  Across the vertical edges, `Full` raises the contrast of Droid Sans by 11.8 %, and changes that of Open Sans and
  Roboto by -3.0 % and +1.9 %: their instructions mostly fit the vertical axis (`TextLayoutTests.HintingMakesSmallTextSharper`
  measures it).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawHintedText;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawSmallLabel(string fontPath, string outputPath)
{
    // The instructions of the font fit the glyphs to the pixel grid: sharper text at small sizes, with advances
    // of whole pixels. TextHinting.Light fits them vertically only, without the instructions, for any font.
    var options = new TextLayoutOptions { Hinting = TextHinting.Full };
    var layout = TextLayout.Create("Settings and preferences", new TextStyle(Font.Load(fontPath), size: 12), options);

    using var image = new Image<Rgba32>((int)layout.Width + 8, (int)Math.Ceiling(layout.Height) + 8, new Rgba32(255, 255, 255));

    // The grid is the pixel grid of the layout: draw it at a whole-pixel origin, without a transform that scales it
    image.Draw(canvas => canvas.FillText(new Rgba32(0, 0, 0), layout, new PointD(4, 4)));
    image.Save(outputPath);
}
```
<!-- endref -->

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
  tight curve, neighboring characters overlap or part; give letter spacing to the style when that matters. A
  vertical layout follows a path as it follows its lines: from the top of the line, with the central baseline of the
  first line on the path, so that down a vertical path the text is the one drawn at a point, and along a path that
  goes to the right it is that text turned a quarter of a turn. The lines that follow are on the right of the
  direction of the path, whatever side the lines of the layout go to.
- **Measuring.** `Width` and `Height` are the size of the text, `Lines` gives each line (its part of the text, its
  position, its width, its baseline), and `InkBounds` contains everything that is drawn; `GetInkBounds(pen)` adds the
  reach of a pen that strokes the text. `GetOrigin(anchor, horizontal, vertical)` gives the origin that puts a point
  of the box of the layout, or of its ink, on an anchor, and `GetOrigin(rectangle, horizontal, vertical)` the one that
  aligns it in a rectangle. The anchors are the sides of the image, whatever the direction of the text; a vertical
  layout has no baseline along y. The line of a vertical
  layout is a column: its rectangle is the one it has on the image, its `Baseline` is the distance from its left to
  its central baseline, and the `Baseline` of the layout is the one of its first column, from the left of the layout.
- **Positions of the text.** `HitTest(point)` gives the character at a point of the layout (the point of the canvas
  less the origin the layout is drawn at), or the nearest one, and which half of it the point is on: its `CaretIndex`
  is where a click puts a caret. `GetCaretRectangle(index)` is where that caret is drawn, and
  `GetSelectionRectangles(start, length)` the rectangles that highlight a range of the text, line by line. A
  character is a grapheme cluster (a letter with its marks, an emoji sequence), and takes the space of its advance, not
  of its ink; the characters a font draws as one glyph share its width. A right-to-left character starts on its
  right: in a text that mixes directions, a caret jumps where the direction changes, and a range of the text can be
  several rectangles on one line. In a vertical layout, a character is found down its column, a caret lies across
  its column (a rectangle without height), and a selection covers the characters that follow each other down it.
- **Cost.** Creating a layout allocates (its glyphs and positions) and decodes each distinct glyph once, which validates
  the font data the text uses: drawing a layout then only fails on a limit of the image or on cancellation, and
  allocates nothing. The time to create a layout grows with the length of the text and with the number of lookups
  of the font: every lookup of a feature is tried at every glyph, and a font of a complex script can have thousands
  (on a recent laptop, a paragraph of 500 characters takes under a millisecond with a Latin or an Arabic font, and
  ten to twenty times more with a Devanagari or a Tibetan font). `FillText(brush, text, font, size, origin)` creates a layout on each call; create the layout
  yourself to measure a text or to draw it more than once. Layouts, styles, options and rich texts are immutable and
  thread-safe.
- **Limits.** `MaxTextLength` (UTF-16 code units), `MaxTextGlyphs`, `MaxTextLines` and `MaxGlyphComplexity`, from the
  configuration the primary font of the first span was loaded with.

**Glyph runs**. A caller that shapes text itself, a browser engine for example, draws the glyphs it placed with
`canvas.FillGlyphs(brush, font, size, glyphs, options)`: each `PositionedGlyph` is a glyph of the font and its origin
on the baseline, in the coordinates of the drawing. Each glyph is filled on its own, over the ones before it, as
browsers draw a text. Under a transformation that keeps the axes (or swaps them), the origin of a glyph on the canvas
is moved to the nearest quarter of a pixel along the line and to the nearest pixel across it (to the nearest pixel
without antialiasing), and the glyph is painted from its coverage in the pixels around the pixel of its origin; under
a transformation that turns, skews or is projective, and for a glyph larger than 128 by 128 pixels, it is filled as a
shape where it is. `GlyphRunOptions` gives a synthetic bold and oblique (with the width and the shear browsers use when
they differ from those of `FontSynthesis`), the palette of the glyphs a font draws with its own colors (`COLR`,
`sbix`, `CBDT`, which are drawn as a text draws them), and a `GlyphCache`. A cache, owned by the caller and bounded in
bytes, keeps the coverage of a glyph for each font object, size, transformation, position in a pixel, antialiasing and
bold, and the display lists of the glyphs of colors and of images: the pixels are the same with or without it, and a
run whose glyphs are in the cache allocates nothing. `canvas.StrokeGlyphs(pen, font, size, glyphs, options)` strokes
the outlines of a run as one shape, as `StrokeText` does. A variable font draws the glyphs of its instance.

What text layout does not do yet: letter spacing is added between the letters of
every script, including those that join. Justification does not lengthen Arabic words (kashida). The advances of a
bitmap strike are not used. Lines are broken one after the other, each as full as
possible, not by optimizing the whole paragraph. In vertical lines, there is no ruby, the punctuation is not compressed
nor hung, the side of an underline is not a choice, a line cannot be read from the bottom, and the marks of a font
that has no positioning table are not placed on upright letters. Along a path, the glyphs are placed and turned,
not bent to the curve, and the rectangles of the placeholders are those of a text drawn at a point. A placeholder is
room, not a drawing: the layout holds no image.

The glyphs and positions are checked against the Annotated OpenType Specification tests of Adobe for every format of
the lookups, and against the OpenType shaper of HarfBuzz for texts set in the fonts of the test corpus; line breaking
and bidirectional ordering are the algorithms of `Meziantou.Framework.Unicode`, which pass the conformance tests of
the Unicode Consortium.

### Metadata, color profiles and orientation

`Image.Metadata` holds the source format (informational only), the EXIF orientation, the
resolution, the pixel aspect ratio, ICC/EXIF/XMP profiles (byte-for-byte payloads), the CICP color description, the
content light level, the mastering display color volume and text entries.

- **Color profiles are preserved, never applied implicitly.** A profile labels the pixels; untagged pixels are assumed
  sRGB. Decoders keep it, encoders write it (for example PNG `iCCP`, JPEG APP2), conversions keep it only when it can
  still label the converted pixels (gray profile for gray formats, RGB profile for color formats). Colors are converted
  only on request (see [Color management](#color-management)). A CMYK image (a JPEG file, a TIFF page or a JPEG 2000
  file) is the one image that cannot be loaded as it is, since no pixel format holds inks: it is rejected unless the load asks for its
  conversion (`CmykConversionOptions`).
- **Transfer functions are labels too.** `Metadata.TransferFunction` is `Srgb` (the default: sRGB samples, or as described
  by the ICC profile) or `Linear` (linear-light samples, declared by a QOI colorspace of 1 or by a JPEG XL color
  encoding with the sRGB primaries and a linear transfer function). Only `ConvertColorProfile` converts the pixels (and
  resets the label); linear-light resizing filters `Linear` samples directly. Only QOI and JPEG XL store the label: the
  other encoders treat `Linear` as metadata they cannot store, so the samples are never silently relabeled as sRGB.
- **CICP color descriptions are labels too.** `Metadata.Cicp` holds the code points of ITU-T H.273 that an AVIF, HEIC or
  PNG (`cICP`) file declares, or that are equivalent to the enumerated color encoding of a JPEG XL file: color primaries, transfer
  characteristics, matrix coefficients and range. The decoder applies the matrix and the range to produce RGB; the
  primaries and the transfer characteristics are not applied when a file is read or written (the samples of an HDR
  image are returned as they are stored). `ConvertColorProfile` converts the pixels from the color space they
  describe, and linear-light processing decodes the samples with their transfer function. The AVIF and HEIC encoders
  store the description (with the matrix and the range that it used), the PNG encoder stores its primaries and its
  transfer characteristics when no ICC profile is written (a `cICP` chunk takes precedence over `iCCP`, so the PNG
  decoder does not adopt the profile of a file that has both), and the JPEG XL encoder stores it when the
  format can enumerate it (full-range RGB with the sRGB, Display P3, DCI-P3 or BT.2020 primaries). For the other
  encoders, a description that is not equivalent to sRGB is metadata the format cannot store, unless an ICC profile is
  written.
- **Light levels, the mastering display and the shape of the pixels are labels too.** `Metadata.ContentLightLevel`
  (MaxCLL and MaxPALL, in cd/m2) and `Metadata.MasteringDisplay` (the primaries, the white point and the luminance
  range of the display, as the integers of SMPTE ST 2086) describe high dynamic range samples as they were mastered;
  nothing computes them from the pixels. `Metadata.PixelAspectRatio` is the relative width and height of a pixel:
  nothing resamples the pixels to make them square, and an operation that swaps the axes of the image (a quarter turn,
  `AutoOrient()` for the orientations that transpose) swaps its terms. AVIF stores the three of them; PNG stores the
  light levels (`cLLI`, in whole cd/m2 once read) and the mastering display (`mDCV`, only with the `cICP` chunk it
  completes); for the other encoders they are metadata the format cannot store (a ratio of square pixels excepted).
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
| CICP color description, light levels, mastering display | `cICP` (without ICC profile), `cLLI`, `mDCV` (with `cICP`) | not stored | not stored | not stored | not stored | not stored | not stored | not stored |
| Resolution | `pHYs` (pixels per meter) | not stored | JFIF density | not stored | not stored | `biXPelsPerMeter`/`biYPelsPerMeter` | not stored | not stored |
| Linear transfer function | not stored | not stored | not stored | not stored | header colorspace 1 | not stored | not stored | not stored |

TIFF stores the resolution, the orientation, an ICC profile and XMP (no EXIF block, no text entries); ICO stores no
metadata, CUR only the hotspot of each representation, and ANI the hotspot of each frame plus one `Title` and one `Author`
text entry (Latin-1). AVIF and HEIC store the ICC profile and the CICP color description (`colr` boxes), EXIF and XMP
(items) and the orientation of a still image (`irot` and `imir` properties, never an EXIF block created for it); AVIF
also stores the content light level, the mastering display color volume and the pixel aspect ratio (`clli`, `mdcv`
and `pasp` boxes) and the orientation of an image sequence (the matrix of its track headers), which HEIC does not
store yet. Neither format has a text entry or a resolution. JPEG XL stores an ICC profile (in the
codestream), EXIF and XMP (in `Exif` and `xml ` boxes), the orientation (in the image header, not as an EXIF tag), the
linear transfer function and a CICP color description; it stores no resolution and no text entry. A JP2 file stores an
ICC profile (in its color specification box: a grayscale profile or a matrix-based RGB profile, the only ones the
format allows there), the resolution (capture resolution box), EXIF, which carries the orientation, and XMP (UUID
boxes); it stores no text entry, no CICP color description and no linear transfer function, and a JPEG 2000 codestream
written without a file stores no metadata at all. Unknown chunks, segments, boxes and extensions are skipped on decode
and not round-tripped.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_EditMetadata;format=md-fenced-code-block;dedent=true -->
```csharp
public static void EditMetadata(string path)
{
    using var image = Image.Load(path);
    image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Sunset"));
    image.Metadata.Resolution = new ImageResolution(300, 300);
    image.Save(path, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
}
```
<!-- endref -->

### Color management

Color conversion between ICC profiles is always explicit. `image.ConvertColorProfile(profile)` converts every frame and
the poster from the color space of the image to the color space of `profile`, then labels the image with it:

- The source is `Metadata.IccProfile`. Without profile it is the color space of the CICP color description
  (`Metadata.Cicp`): the function of its transfer characteristics, then its color primaries, adapted to the white of
  the ICC connection space with the linear Bradford transform. Unspecified code points are those of sRGB, the matrix
  coefficients and the range play no part (they describe how a file stored the samples), and reserved primaries or
  transfer characteristics are rejected. Without profile and without description, or with a description that is
  equivalent to sRGB, the source is sRGB (`IccProfile.Srgb`), sGray for grayscale pixel formats
  (`IccProfile.SrgbGray`), or their linear-light counterparts when `Metadata.TransferFunction` is `Linear`. A profile
  together with the `Linear` label is ambiguous and rejected.
- After the conversion the profile describes the pixels: a color description that is not equivalent to sRGB, the
  content light level and the mastering display are removed with the colors they described.
- The pixel format is kept, so the destination must be an RGB profile for color formats and a grayscale profile for
  `Gray8`/`Gray16`. Colors are converted at the storage precision (16-bit formats stay 16-bit); alpha is copied
  unchanged and the color of transparent pixels is converted like any other.
- The operation is transactional, like a geometry change: on failure or cancellation the image keeps its pixels and
  its profile, so pixels are never left with a profile that does not describe them. The old and new buffers are
  budgeted together.
- When both profiles have identical bytes nothing is converted: converting an untagged image to `IccProfile.Srgb` only
  labels it.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertToSrgb;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

High dynamic range samples (the PQ and HLG transfer characteristics, 16 and 18) are not converted implicitly: an ICC
profile describes a standard dynamic range, and compressing highlights into it changes the look of the image.
`ConvertColorProfile` rejects them unless `IccColorTransformOptions.ToneMapping` says how. They are the samples of an
image whose CICP color description says so and that has no ICC profile, or of an image whose ICC profile has a `cicp`
tag (ICC.1:2022, version 4.4) that says so (`IccProfile.Cicp`): the curves of such a profile only approximate the
functions, and the samples are converted from the color space of the tag.

- PQ samples are the light of a display, up to 10,000 cd/m2. HLG samples are the light of a scene, turned into the
  light of a display by the opto-optical transfer function of Rec. ITU-R BT.2100 for the peak of the source.
- `TargetWhiteLuminance` (203 cd/m2 by default, the reference white of Report ITU-R BT.2408) becomes the white of the
  result. `SourcePeakLuminance` is the brightest light of the source, which the curve maps to that white: when it is
  not set, it is the maximum content light level of the image (PQ), else the maximum luminance of its mastering
  display, else 10,000 cd/m2 (PQ) or 1,000 cd/m2 (HLG).
- `Operator` is the curve: `Bt2390` (the default: the EETF of Report ITU-R BT.2390, which keeps the light below a knee
  and rolls off above), `Clip`, `Reinhard`, `Hable` or `Mobius`. Every curve is applied to the largest component of a
  color and the three components are scaled together, so highlights keep their hue. The result is linear light in
  the primaries of the source, which is then converted to the destination profile like any other color; a source
  whose peak is below the white is neither compressed nor brightened.
- Samples are 8-bit or 16-bit integers from end to end: there is no floating-point pixel format, and the converted
  image is a standard dynamic range one.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertHdrToSrgb;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConvertHdrToSrgb(string avifPath, string pngPath)
{
    using var image = Image.Load(avifPath);

    // The image has no ICC profile: its color description (BT.2020 primaries with PQ samples, for
    // example) is what the colors are converted from. PQ and HLG samples are only converted when
    // the options say how the light above the white of the result is compressed.
    var options = new IccColorTransformOptions
    {
        ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Bt2390 },
    };
    var grayscale = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
    image.ConvertColorProfile(grayscale ? IccProfile.SrgbGray : IccProfile.Srgb, options);

    // The pixels are sRGB ones: the color description and the light levels went with the conversion
    image.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
}
```
<!-- endref -->

`IccColorTransform` is the underlying primitive: an immutable, thread-safe conversion between two profiles for
interleaved 8-bit, 16-bit or floating-point samples without alpha (in place when both profiles have the same number of
channels). Integer samples cover the whole range of their type, floating-point samples are in [0, 1], and colors
outside the destination gamut are clipped per channel.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertSamples;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

Supported profiles are ICC version 2 and version 4 input, display, output and color space profiles for grayscale, RGB
and CMYK data, with a CIEXYZ or CIELAB connection space:

- **Matrix-based profiles**: `rXYZ`/`gXYZ`/`bXYZ` with `rTRC`/`gTRC`/`bTRC`, or `kTRC` for grayscale; sampled, gamma
  and parametric curves.
- **Table-based profiles**: `A2B0`/`A2B1`/`A2B2` and `B2A0`/`B2A1`/`B2A2` tags of type `lut8Type`, `lut16Type`,
  `lutAToBType` or `lutBToAType`. They take precedence over the matrix and curves of the same profile.
  `IccColorTransformOptions.Intent` selects the table: perceptual (0), colorimetric (1) or saturation (2); a profile
  without the table of the intent uses its perceptual table, then its matrix and curves. CMYK samples are ink
  amounts: 0 is no ink.

**CMYK images.** No pixel format holds CMYK samples, so a JPEG file with four components (CMYK or YCCK inks) and a
TIFF page of separated data (`PhotometricInterpretation` 5) are only loaded by converting their colors, and like every
color conversion this one is explicit: set `ImageDecodeOptions.CmykConversion` (or `ImageReaderOptions.CmykConversion`;
the pages of a TIFF document take the options of `ImageCollectionEntry.Decode`). Without it the load throws an
`UnsupportedImageFeatureException`; `Identify` always works and reports `ImageColorModel.Cmyk` or `Ycck` with the
embedded CMYK profile, so a caller can decide first. Other images are not affected by the setting.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadCmykJpeg;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

- `DestinationProfile` (default `IccProfile.Srgb`) is an RGB profile. The loaded image has `Rgb24` pixels labeled with
  it: the pixels and their profile change together, and the CMYK profile is not kept. A typed load then converts the
  layout as for any RGB image. A TIFF page keeps what JPEG does not have: an unassociated alpha sample is copied
  (`Rgba32`), and 16-bit inks are converted with their precision (`Rgba64`).
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
  CMYK). TIFF pages store ink amounts (0 is no ink), whatever their compression: a JPEG datastream inside a TIFF page
  is not complemented.
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

### Color values

Single colors have value types of their own, immutable `readonly struct`s of `double` components, in the namespace
`Meziantou.Framework.Imaging`: `CieXyz`, `CieLab` and its cylindrical form `CieLch`, `CieLuv`, `Oklab` and `Oklch`, and
the cylindrical forms of RGB values `Hsl`, `Hsv` and `Hwb`, and the device `Cmyk`. They convert from and to `Rgba32` and
`Rgba64`, measure how different two colors look (`ColorDifference`), mix colors (`Oklab.Lerp`, `Oklch.Lerp`), and read
and write the functional notations of CSS Color 4.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ColorValues;format=md-fenced-code-block;dedent=true -->
```csharp
public static (string Lab, double Difference, Rgba32[] Palette, Rgba32 Tint, Rgba32 InDisplayP3) DescribeColors()
{
    // Colors of CSS, as sRGB colors: hexadecimal, rgb(), hsl(), hwb(), lab(), lch(), oklab() and oklch()
    var brand = Rgba32.ParseCss("oklch(62% 0.2 29)");
    var accent = Rgba32.ParseCss("#2A9D8F");

    // CIELAB, relative to D50 as in CSS, and its CSS notation
    var lab = CieLab.FromRgb(brand);
    var labText = lab.ToCssString();

    // How different two colors look: about 1 is a just noticeable difference
    var difference = ColorDifference.DeltaE2000(lab, CieLab.FromRgb(accent));

    // Five colors evenly spaced in Oklch, from one color to the other along the shorter arc of hues
    var from = Oklch.FromRgb(brand);
    var to = Oklch.FromRgb(accent);
    var palette = new Rgba32[5];
    for (var i = 0; i < palette.Length; i++)
    {
        palette[i] = Oklch.Lerp(from, to, i / 4.0).ToRgba32();
    }

    // A lighter tint of the same hue and saturation
    var hsl = Hsl.FromRgb(brand);
    var tint = new Hsl(hsl.H, hsl.S, Math.Min(1, hsl.L + 0.2)).ToRgba32();

    // The same color in the samples of a Display P3 image, which its CICP description gives
    var displayP3 = RgbColorSpace.FromCicp(new CicpColorDescription(12, 13, 0, fullRange: true));
    var inDisplayP3 = lab.ToRgba32(displayP3);
    return (labText, difference, palette, tint, inDisplayP3);
}
```
<!-- endref -->

- **RGB values have a color space.** `FromRgb` and `ToRgba32`/`ToRgba64` take an `RgbColorSpace`: `RgbColorSpace.Srgb`
  (the default), `RgbColorSpace.FromIccProfile(profile)` for an RGB ICC profile (the relative colorimetric intent), or
  `RgbColorSpace.FromCicp(description)` for a CICP color description (PQ and HLG are rejected: their light needs tone
  mapping). Create a space once and reuse it. Alpha is not a color coordinate: it is ignored by `FromRgb`, and the
  colors `ToRgba32`/`ToRgba64` give are opaque. A color outside the gamut of the space is clipped component by
  component (CSS gamut-maps instead). `Hsl`, `Hsv`, `Hwb` and `Cmyk` describe the stored RGB values as they are, of
  whatever color space: they take no space.
- **The white is D50.** `CieXyz` is relative to `CieXyz.D50` in every conversion, as the `lab()` colors of CSS and the
  connection space of ICC profiles are; `CieLab.FromXyz` and `CieLuv.FromXyz` take another white, and `CieXyz.Adapt`
  moves a color from a white to another with the linear Bradford transform. `CieXyz.D50` and `CieXyz.D65` are those of
  CSS Color 4, from the four-figure chromaticities of CIE.
- **The numbers are those of CSS Color 4.** sRGB uses the rational matrix and the transfer function of the CSS sample
  code, the adaptation between D65 and D50 its 64-bit Bradford matrices, and Oklab its matrices recomputed for that D65
  (they differ from the first ones Björn Ottosson published by up to 1.3e-4). The tests compare every conversion with
  the sample code of CSS Color 4 and with colour-science, to 1e-9. An ICC profile converts with its s15Fixed16 numbers,
  so `RgbColorSpace.FromIccProfile(IccProfile.Srgb)` differs from `RgbColorSpace.Srgb` by about 1e-5 in CIEXYZ: the
  connection space of ICC (D50 rounded to 0.9642, 1, 0.8249) is adapted to `CieXyz.D50` with the Bradford transform.
- **Color differences.** `ColorDifference.DeltaE76` is the distance in CIELAB, `DeltaE94` the CIE 1994 formula (graphic
  arts or textiles weights; not symmetric, the reference color weights it), `DeltaE2000` CIEDE2000 with optional
  parametric factors (it passes the test data of Sharma, Wu and Dalal), and `DeltaEOK` the distance in Oklab of CSS.
- **Hues** are in degrees. Conversions give them in [0, 360); a gray has no hue, which is then the angle of its tiny
  components, or 0. `Oklch.Lerp` mixes the hues along the arc a `HueInterpolation` selects (`Shorter` by default, as
  CSS), and takes the hue of the other color when one is gray (a chroma of at most 0.000004).
- **CSS notations.** `Parse` and `TryParse` of `CieLab` (`lab()`), `CieLch` (`lch()`), `Oklab` (`oklab()`), `Oklch`
  (`oklch()`), `Hsl` (`hsl()`, `hsla()`) and `Hwb` (`hwb()`) read the modern syntax (white space between the
  components, `/ alpha`, `none`) and, for `hsl()`, the legacy syntax with commas; the alpha is returned apart.
  Percentages, angles (`deg`, `grad`, `rad`, `turn`) and the clamping of CSS are applied as the specification resolves
  them at parse time, and hues are brought to [0, 360). `ToCssString` writes the modern syntax with the shortest numbers
  that read back the same values. `Rgba32.ParseCss` reads any of them, `rgb()`/`rgba()` and the hexadecimal notation
  too, and converts the color to sRGB. Named colors, `calc()`, relative colors and `color()` are not supported.
- The operations of the value types allocate nothing, except the formatting, which returns a string.

### Loading, identification and limits

Detection is content-based (never by extension); `Image.DetectFormat` needs at most
`Image.FormatDetectionPrefixLength` (18) bytes, because TGA has no signature and is recognized from a strictly plausible
18-byte header, consulted last. `Load` exists in synchronous and asynchronous, path, stream and span, typed and untyped
forms, with identical results and error categories. `Identify` returns an `ImageInfo` without decoding pixels: `Header`
mode reads up to the first pixel data, `FullScan` walks the whole structure (chunks, blocks, markers, CRCs, rows). Unknown
values stay `null` and are never guessed (a GIF header never yields a frame count).

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Identify;format=md-fenced-code-block;dedent=true -->
```csharp
public static string Describe(Stream stream)
{
    var info = Image.Identify(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
    var frames = info.FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    return $"{info.Format} {info.Size} {info.PixelFormat}, frames: {frames}, orientation: {info.Metadata.Orientation}";
}
```
<!-- endref -->

**Limits versus prefix selection.** `ImageResourceLimits` (canvas width/height, pixels per frame, frames, total pixels,
encoded bytes, metadata bytes, live allocations, and for fonts the bytes of a font and the complexity of a glyph) are
safety bounds: exceeding one throws `ImageResourceLimitException`
(`Kind`, `Limit`, `Requested`) and is never turned into a successful truncation. `FrameLimit` (decode and reader options)
is deliberate prefix selection: decoding stops after that many displayed frames and the rest of the input is neither
examined nor validated. Limits are inclusive and enforced before allocating or consuming.

AVIF and HEIC are detected from the brands of their file type box and read forward only: the data of the image are captured as they go
by, so a file whose media data precede its index is buffered up to that index, bounded by `MaxEncodedBytes` and the live
allocation limit.

For forward-only parsing, `MaxEncodedBytes` bounds the prefix examined; the input length is enough for that limit.
When parsing stops early (`Identify` in header mode or `FrameLimit`), the remaining bytes need not fit the limit.
A stream only reports its end by returning no byte, so when the format needs the end of the input (the
TGA trailer, the last sample of a plain Netpbm raster, a TIFF, an icon or an animated cursor buffered whole, an input shorter than the detection
prefix) and every byte the limit allows was read, one more byte is read and discarded to tell an input that ends there from
a longer one. It is the only byte ever read past the limit, and it is never decoded.

TIFF, ICO, CUR and ANI are random-access containers: on a seekable source, only the bytes their structure points at are read
(the stream position afterward is unspecified). Synchronous path and seekable-stream loads charge the cumulative bytes
read to `MaxEncodedBytes`, including repeated reads of the same range: they may succeed with a limit below the input
length, or need more than that length when ranges are read repeatedly. Span, non-seekable and asynchronous loads instead
parse the whole input in memory and charge its length once, so the input length is exactly enough for this limit.

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
- `ReadFrameRefinementInto(destination)` writes the next rendering of the current frame to a caller-owned image and tells
  what it is (`FrameRefinement`: `IsFinal`, `Downsampling`, `CompletedPasses`). A format that stores a frame at increasing
  levels of detail gives intermediate renderings before the frame itself, each a full image at the size of the frame: a
  progressive [JPEG XL](#jpeg-xl) frame, a progressive [JPEG](#jpeg) frame (after each scan, once every component has its
  DC coefficients) and an interlaced [PNG](#png-and-apng) image (the Adam7 passes completed so far, every other pixel
  taking the value of the pixel of its block in them). Any other input gives the frame at once, with `IsFinal` set. After
  a final rendering, the next read starts the next frame; `ReadFrame` and `ReadFrameInto` can be called at any time and
  read the frame itself, and the destination may change between two reads. The number of intermediate renderings and
  their pixels are not part of the contract; the renderings of passes and scans count toward `MaxTotalPixels`.
- An input that ends before the frame is complete is malformed. With `ReadFrameRefinementInto`, the read that reaches its
  end first returns a rendering of what was decoded when the format allows it, as a browser shows a partial download, and
  the next read throws `InvalidImageContentException`: the rows decoded so far of a GIF image (over what the previous
  frames left), of a PNG image that is not interlaced, of a JPEG frame coded in one scan and of a still lossy WebP image
  (the other rows are zero), the passes of an interlaced PNG image, and the coefficients of a progressive JPEG frame
  decoded so far. The other reads throw at once.
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
| APNG | Required (the count precedes the image data and seeking is never used). With `PngPixelLayout.Indexed`, the palette also precedes the image data: it holds the colors of the first image written (the poster, or the first frame), and every later frame must use them. With `PngOptimization.MinimumSize`, the layout comes from the first image written in the same way, and each frame is written when the next one (or the completion) is given |
| GIF | Optional: unknown counts are supported with fixed memory (local palettes, no global prepass) |
| WebP | Optional; an animation (any count other than 1) needs a seekable destination |
| JPEG XL | Optional; any count, without seeking (a frame is written when the next one, or the completion, tells whether it is the last) |
| ANI | Optional; any count, with a seekable destination (the counts and sizes are patched at completion) |
| AVIF, HEIC | Required; an image sequence (any count other than 1) needs a seekable destination (its index is written at completion, in the room that the count reserves) |
| JPEG, JPEG 2000, QOI, BMP, TGA, Netpbm, TIFF, ICO/CUR | Optional; exactly one frame |

`Complete` checks that at least one frame was written and that the count matches. Errors detected before any work
(state, order, frame size or pixel type, a frame duration the format cannot store) leave the writer usable; a failure once
encoding started faults it.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_StreamingTransform;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ReadFrameInto;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

### Saving files

`Save(stream, encoder)` requires an encoder. `Save(path)` infers it from the extension: `.png`
(APNG when the image is animated), `.apng` (always APNG), `.gif`, `.jpg`/`.jpeg`, `.webp` (lossless by default), `.qoi`,
`.bmp`/`.dib`, `.tga`/`.icb`/`.vda`/`.vst`, `.pnm`/`.pam`/`.ppm`/`.pgm`, `.pbm` (a portable bitmap, from black and white
pixels only), `.tif`/`.tiff`, `.ico`, `.cur`, `.ani`, `.avif`/`.avifs` (lossless by default), `.heic`/`.heif`/`.heics`/`.heifs` (lossless by default), `.jxl` (lossless by default), `.jp2` (a JP2 file,
lossless by default), `.j2k`/`.j2c`/`.jpc` (a JPEG 2000 codestream, lossless by default) and `.exr` (OpenEXR, ZIP);
anything else throws
`ArgumentException`, the `.jpx`, `.jpf`, `.jph` and `.jhc` extensions included (these files are read, not written). An eager save validates the whole image against the output (animation, poster,
durations, alpha, precision, metadata) before writing anything.

`ImageFrame.Save` and `SaveAsync` (to a path or a stream) save one frame, a displayed frame or the poster frame, as a
still image without copying its pixels: the output is the one of `CloneFrame(i).Save` (or `ClonePosterFrame().Save`),
with the metadata of the frame (duration, cursor hotspot) and of its image, and no animation settings or poster. The
same rules apply, before any output: alpha to JPEG needs `BackgroundColor`, 16 bits to an 8-bit output need
`AllowBitDepthReduction`, and metadata the output cannot store follows `MetadataHandling`. A frame that is leased throws
`InvalidOperationException`, and one that was removed or replaced, or whose image is disposed, throws
`ObjectDisposedException`.

OpenEXR and TIFF store floating-point samples: `OpenExrEncoder` writes `RgbaFloat` and `RgbaHalf` images using the sample
type and alpha conversion described in [OpenEXR](#openexr), and
`TiffEncoder` writes them as 32-bit floats (see [TIFF and BigTIFF](#tiff-and-bigtiff)) with `TiffPixelLayout.Auto` and a
compression that is not `TiffCompression.Jpeg`. Elsewhere an `RgbaFloat` or `RgbaHalf` image, writer or collection entry
is rejected before anything is written, unless `ImageEncoder.FloatingPointConversion` gives the options of its reduction
to `Rgba64` (`CloneAs<Rgba64>` with those options). The reduced frames are then encoded like any `Rgba64` frame, and the
label `ColorTransferFunction.Linear` of a new floating-point image follows `MetadataHandling` (only QOI, JPEG XL and the
floating-point pages of TIFF store it). The peak of a tone mapping is the one of every frame of an eager save, and of each frame for a
sequential writer, unless the options give it. The reduced copies are charged to the writer: an eager save holds those of
all of its frames.

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
| `UnsupportedImageFeatureException` (`Format`, `Feature`) | A valid but unsupported feature (hierarchical JPEG, a CMYK JPEG loaded without `CmykConversion`, GIF plain text...), or an operation that would lose animation, alpha, precision, metadata, a cursor hotspot, a poster or a profile without an explicit setting, or a duration/play count the output cannot represent. |
| `ImageResourceLimitException` (`Kind`, `Limit`, `Requested`) | A configured limit was exceeded. |

Other failures use the standard exceptions: `ArgumentException` (invalid arguments, mismatched frames),
`NotSupportedException` (an unsupported `TPixel`, before any I/O), `InvalidOperationException` (invalid state, lease
conflicts, overlapping or faulted reader/writer calls), `ObjectDisposedException`, `OperationCanceledException`, and
`IOException` from the stream, propagated unchanged. Malformed data and resource failures are never reported as a clean
end of input.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ErrorHandling;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

## Format reference

Every codec works eagerly (`Load`, `Save`) and sequentially (readers, writers) with the same results, and streams with
bounded memory unless stated otherwise.

### PNG and APNG

`PngEncoder.AnimationMode`: `Auto` writes an APNG for animated images (several frames,
a poster or animation settings), `Animated` and the `.apng` extension always do; the static mode rejects animated images.

`PngEncoder.PixelLayout`: `Auto` (the default) follows the pixel format and never writes a palette. `Indexed` writes
indexed color: a palette of the distinct colors of the file, each with its own alpha, and indexes at the smallest bit depth
that addresses it (1, 2, 4 or 8 bits). It is exact or it fails: an image with more than 256 distinct combinations of color
and alpha (fully transparent pixels keep their color, so they count too), or with 16-bit samples that are not exactly
8-bit values, is rejected before anything is written, unless `PngEncoder.AllowColorReduction` is set. The frames and
the poster of an animation share the palette: `Save` collects the colors of all of them first, while a sequential writer
takes the palette from the first image it is given. The palette order is deterministic (alpha, then red, green and
blue), and an indexed file loads as `Rgba32`.

`PngEncoder.AllowColorReduction` lets `Indexed` reduce the images that do not fit, with the quantizer of
[`Quantize`](#color-reduction) and the options of `PngEncoder.Quantization`: a file has one palette, computed from every
frame and the poster for `Save` (or `QuantizeOptions.Palette`), and from the first image for a sequential writer, whose
later frames are mapped to it. Each image is reduced into an 8-bit RGBA copy charged to the writer. An image that fits is
written exactly as without the setting.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SavePngColorReduction;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePalettePng(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // At most 256 colors, each with its own alpha (tRNS), in one palette for every frame. An image whose colors fit
    // is stored exactly; the others are reduced and dithered.
    image.Save(outputPath, new PngEncoder
    {
        PixelLayout = PngPixelLayout.Indexed,
        AllowColorReduction = true,
        Quantization = new QuantizeOptions { Dithering = DitheringMode.Atkinson },
    });
}
```
<!-- endref -->

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SavePngIndexed;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool SaveIndexedPng(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    try
    {
        // One palette (PLTE, plus tRNS when a color is not opaque) for the colors of every frame, and 1-, 2-, 4- or
        // 8-bit indexes. The pixels are stored exactly, alpha included: nothing is ever reduced.
        image.Save(outputPath, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
        return true;
    }
    catch (UnsupportedImageFeatureException exception) when (exception.Feature is "Color reduction" or "Bit depth reduction")
    {
        // More than 256 colors, or 16-bit samples that 8 bits cannot represent: the save failed before it wrote
        // anything. The default layout follows the pixel format and stores every image.
        image.Save(outputPath, new PngEncoder());
        return false;
    }
}
```
<!-- endref -->

`PngEncoder.Optimization`: `None` (the default) follows the settings. `MinimumSize` searches the lossless options and
writes the smallest file it finds, several times slower; the pixels (alpha and the color of fully transparent pixels
included), the frames, their durations, the poster and the metadata stay exact:

- **Layout.** The color type and bit depth the pixels need: grayscale at 1, 2, 4, 8 or 16 bits, grayscale with alpha, RGB
  or RGBA at 8 or 16 bits (16-bit samples are written at 8 bits only when both of their bytes are equal), a `tRNS` color
  key when every transparent pixel has one color that no opaque pixel has, or a palette. The candidates are compressed
  and the smallest total wins; `PngPixelLayout.Indexed` restricts them to palettes. An ICC profile keeps its color space
  (an RGB profile never gets a grayscale layout).
- **Filters and zlib.** Every filter strategy (or only the fixed one `Filter` selects) at `SmallestSize`, `Optimal` and
  `Fastest`; `CompressionLevel` is ignored, `Interlaced` is respected.
- **Animation frames.** Each frame stores the rectangle that differs from the canvas it is drawn on, with `SOURCE` or
  `OVER` blending (only where the result is exact: opaque pixels, or visible pixels on a transparent canvas, the
  unchanged ones transparent) and the disposal of the previous frame (`NONE`, `BACKGROUND` or `PREVIOUS`) that give the
  smallest data. For an animation saved at once, the layout trial compresses the frames as the regions that changed, and
  also tries an unused `tRNS` color key (or a transparent palette entry) that lets opaque frames skip their unchanged
  pixels: it is kept only when it saves more than it costs. The samples and palette indexes of an animation have 8 bits
  at least: some decoders (FFmpeg among them) misplace the packed pixels of a frame region.

A file has one layout, written before its first image: `Save` chooses it from every frame and the poster, a sequential
writer from the first image, and it rejects a later frame that the layout cannot store (`UnsupportedImageFeatureException`
before anything of the frame is written). A sequential writer also writes each frame when it is given the next one (or
when it completes), because a frame's control data holds the disposal chosen for the next one. The file may load with
another pixel format than the one it was written from (`Rgb24` for an opaque `Rgba32` image, `Rgba32` for a color key or
a palette), with the same pixel values.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SavePngMinimumSize;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveSmallestPng(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // The layout the pixels need (grayscale, a color key, a palette, 8 instead of 16 bits...), the best filters and
    // zlib level, and for an animation the frame regions that changed. The pixels, alpha values, frames, durations and
    // metadata are stored exactly; only the encoding changes, and it takes several times longer.
    image.Save(outputPath, new PngEncoder { Optimization = PngOptimization.MinimumSize });
}
```
<!-- endref -->

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Grayscale 1/2/4/8/16-bit | Sub-byte samples scaled exactly to `Gray8`; 16-bit kept in `Gray16`; a `tRNS` key gives `La16`/`La32` | `Gray8` → gray 8, `Gray16` → gray 16 (all 16 bits kept), `La16`/`La32` → gray with alpha 8/16, `Rgb48` → RGB 16; `PngOptimization.MinimumSize`: every bit depth, and a `tRNS` key, for the gray pixels of any pixel format |
| RGB / RGBA 8/16-bit, gray+alpha | 16-bit precision preserved, gray+alpha expanded to RGBA | `Rgb24` → RGB 8, `Rgba32`/`Bgra32` → RGBA 8, `Rgba64` → RGBA 16; fixed mapping, no data-dependent reduction; `PngOptimization.MinimumSize` chooses the layout from the pixels instead (RGB 16, gray+alpha 8/16, 8-bit samples of 16-bit pixel formats, RGB with a `tRNS` key), exactly |
| Palette + `tRNS` | 1/2/4/8-bit, partial `tRNS`, out-of-range indices rejected | `PngPixelLayout.Indexed`, from any pixel format: exact palette of at most 256 colors with per-entry alpha (`tRNS` only for the entries that are not opaque), smallest bit depth, one palette per file; reducing colors only with `AllowColorReduction` (`Quantization`), and selected automatically only by `PngOptimization.MinimumSize` (which keeps 8-bit indexes when they compress smaller, and in an animation) |
| Adam7 interlacing | Empty passes, pass rows scattered directly into the frame (no intermediate image); with `ReadFrameRefinementInto`, a rendering of the passes completed by each piece of the data, every other pixel taking the value of the top left pixel of its block in them (the passes are then decoded into an image of the decoder, from which the renderings and the frame are written) | Option; pass rows gathered from the frame storage, empty passes have no scanline |
| Filters | All five; invalid types rejected | Adaptive minimum-sum-of-absolute-differences heuristic, or one fixed type for every scanline; indexed output uses type 0 (None) instead of the heuristic, as the specification recommends; `PngOptimization.MinimumSize` compresses every strategy and keeps the smallest |
| zlib datastream | Truncation, extra data, Adler-32 and expansion (bounded by the image size) validated; streamed with bounded memory | BCL zlib at the four `CompressionLevel` values (`PngOptimization.MinimumSize` tries `Fastest`, `Optimal` and `SmallestSize`); `IDAT` chunks of at most 32 KiB, streamed in bounded bands |
| Metadata (`iCCP`, `cICP`, `mDCV`, `cLLI`, `eXIf`, XMP `iTXt`, `tEXt`/`zTXt`/`iTXt`, `pHYs`) | Bounded; metadata after `IDAT` included; a chunk that is read (`iCCP`, `eXIf`, `tEXt`, `zTXt`, `iTXt`) and whose data is larger than what is left of `MaxMetadataBytes` fails with `ImageResourceLimitException` before it is buffered, even when it would decompress to fewer bytes or be skipped as invalid; a `cICP` chunk takes precedence over `iCCP` (the profile is not adopted, nor buffered when it follows `cICP`), one whose matrix coefficients are not 0 is skipped, and narrow-range samples are rejected with `UnsupportedImageFeatureException` | Written before `IDAT`; `cICP` (matrix coefficients 0, full range) when no ICC profile is written; typed orientation and canvas reconciled into EXIF; incompatible ICC profiles and invalid text entries follow `MetadataHandling`; long texts compressed |
| APNG animations | Full-canvas displayed frames from one compositor (SOURCE/OVER blending with an exact rounding contract at 8 and 16 bits, NONE/BACKGROUND/PREVIOUS disposal; BACKGROUND clears to transparent black), every color type and bit depth, Adam7 frame regions, partial rectangles, split `fdAT` chunks, exact rational delays, total plays; control-data errors are never reported as a clean end or a still image | Every displayed frame written as a full-canvas `SOURCE` frame (the changed region with `PngOptimization.MinimumSize`) with freshly computed control data, so decoded delta/disposal/blend data never survives an edit; exact delays (`RequireExact`, default) or `RoundToNearest` (nearest 16-bit fraction, ties to the longer delay); writers require `ExpectedFrameCount` and stream to non-seekable outputs |
| APNG poster frame | `PosterFrame`, excluded from `Frames.Count`; a default image with an `fcTL` is frame zero | The `IDAT` image without a preceding `fcTL`, written once before frame zero, not counted in `num_frames` |
| APNG delta-rectangle optimization | — | `PngOptimization.MinimumSize`: the region that changed, `SOURCE` or exact `OVER` blending, and the disposal of the previous frame, chosen per frame by compressing the candidates |
| Apple CgBI (iOS-optimized PNG, as `xcrun pngcrush -iphone` writes them) | Decoded by default: a `CgBI` chunk before `IHDR` marks 8-bit BGR or premultiplied BGRA samples in a raw deflate datastream (no zlib header or Adler-32), whose end is still validated; the channels are swapped and the colors un-premultiplied to `round(c × 255 / a)`, the values Apple ImageIO gives. Un-premultiplying is lossy for low alpha values: a color with an alpha of `a` was stored with `a + 1` levels, so an alpha of 1 only leaves 0 and 255. `iCCP` and `zTXt` chunks may be raw deflate too. A premultiplied color above its alpha, a truncated datastream, another `CgBI` chunk, or samples that are not 8-bit RGB or RGBA are `InvalidImageContentException`; an animated CgBI file or one with a `tRNS` key is `UnsupportedImageFeatureException`. A `CgBI` chunk after `IHDR` is an unknown critical chunk | Never written |
| Unknown critical chunks | Not supported (`UnsupportedImageFeatureException`) | — |
| Unknown ancillary chunks | Skipped, not round-tripped | Not written |

### GIF



| Feature | Decoding | Encoding |
| --- | --- | --- |
| GIF87a / GIF89a, LZW | Minimum code sizes 1-11, codes up to 12 bits, clear codes anywhere, full tables without clear; a missing end code after a complete image, data after the end code and any data after the last pixel (more indices, a string running past the image, a reset stream that goes on, as browsers decode it) are ignored; invalid codes, short data, out-of-table indices and images without a color table are `InvalidImageContentException` | GIF89a, no global color table, one full-canvas image per frame, LZW with a clear code on a full table, 255-byte sub-blocks |
| Palettes | Global and local, per image; output always `Rgba32` | One local palette per frame of at most `Quantization.MaxColors` entries (2–256, the transparent entry included); exact palette when the frame's colors fit, otherwise the deterministic median cut of [`Quantize`](#color-reduction), or `Quantization.Palette` for every frame (its entries get the alpha rule of the pixels: dropped below the threshold, or flattened); nearest-color mapping. Lossy by design |
| Interlacing | Four passes, any height | `GifEncoder.Interlaced` |
| Transparency and disposal | Full-canvas displayed frames from one compositor (partial rectangles clipped to the screen, the transparent index keeps the canvas); the canvas and disposal 2 are transparent black (the background color index is never painted); disposal 3 restores (first image: clears); undefined 4 is "restore previous", 5–7 "none" | Recomputed from the displayed frames: every frame covers the canvas with disposal 2, so pixels that become transparent never show the previous frame; alpha thresholded (default 128) or flattened onto an explicit background, never partial |
| Dithering | — | `Quantization.Dithering`: every `DitheringMode` (error diffusion and ordered), deterministic integer arithmetic, quantized frames only. `GifEncoder.Dithering` and `GifDithering` are obsolete aliases |
| Loop extension (NETSCAPE2.0, ANIMEXTS1.0) | `L` repetitions → `TotalPlays = L + 1`, `0` → infinite, none → one play; the last extension wins; exact hundredth-second delays | NETSCAPE2.0 with `TotalPlays - 1` repetitions (none for one play, at most 65,536 plays); delays to the nearest hundredth (ties up) or exact (`RequireExact`), beyond 655.35 s rejected, never clamped |
| Comment extension | `Comment` text entries, Latin-1 (readers see those before the first image) | `Comment` entries only, Latin-1, any length; ICC, EXIF, XMP and resolution follow `MetadataHandling` |
| Plain-text extension | Not supported (`UnsupportedImageFeatureException`) | — |
| Other application extensions (XMP, ICC...) | Skipped | — |

### JPEG



| Feature | Decoding | Encoding |
| --- | --- | --- |
| Sequential Huffman | Baseline (SOF0) and extended sequential (SOF1): 8/16-bit DQT, DHT identifiers 0-3, tables redefined between scans, interleaved or non-interleaved scans, double-precision IDCT with documented rounding; streamed with three MCU rows of decoder state (full planes for multi-scan frames) | Baseline (SOF0), the default `JpegEncoder.Process`: one interleaved scan, Annex K typical Huffman tables (no optimized tables), Annex K quantization tables scaled by `Quality` 1-100 (default 90), double-precision forward DCT with documented rounding, no restart markers; streamed by MCU rows |
| Progressive Huffman (SOF2) | DC and AC first and refinement scans, spectral selection, EOB runs, successive approximation, restart intervals, partial progressions, every sampling layout and color model of the sequential decoder; scan parameters validated from the scan headers (also by a full `Identify`). Not row-streamed: the 16-bit coefficients of the whole image (about 2 bytes per sample and component) are kept until the end of the image. `ReadFrameRefinementInto` renders the coefficients decoded so far after each scan, once every component has its DC coefficients (`Downsampling` is 8 until an AC scan) | `JpegEncodingProcess.Progressive`: the quantized coefficients of the baseline output (so the same decoded pixels), in ten scans for a color image and six for a grayscale one: the DC coefficients without their lowest bit, the AC coefficients 1-5 of the luminance without their two lowest bits, the chrominance (Cr, then Cb) without its lowest bit, the AC coefficients 6-63 of the luminance, then the missing bits (luminance bit 1, DC bit 0, chrominance bit 0, luminance bit 0). The Huffman tables of each scan are computed from its own symbols (each scan is coded twice), which the end-of-band runs require and which usually makes the file smaller than the baseline one; no restart markers. Not streamed by MCU rows: the 16-bit coefficients of the whole image are kept until the last scan and charged to the writer |
| 12-bit samples | Extended sequential and progressive frames with a sample precision of 12 bits (a baseline frame that declares 12 bits is read as an extended sequential one, as common decoders do): DC differences of up to 15 bits and AC coefficients of up to 14 bits, the same inverse DCT, upsampling filters and color conversion at the precision of the samples (level shift and chroma center 2048, range 0-4095), then each sample `s` scaled to 16 bits as `(s * 65535 + 2047) / 4095`, so that 0 and 4095 are exact. `ImageInfo.BitsPerComponent` is 12; grayscale loads as `Gray16`, the other color models as `Rgb48`; CMYK and YCCK inks are scaled, then converted by the 16-bit ICC conversion. Decodes like libjpeg-turbo within three units of a 12-bit sample | Not supported |
| Lossless (SOF3) | The lossless process of T.81 Annex H, Huffman coded: samples of any precision from 2 to 16 bits, the seven predictors, the point transform (the output is multiplied back by `2^Pt`, so its low bits are zero), restart intervals, interleaved and non-interleaved scans, subsampled components (the samples that complete an MCU are decoded and removed). Every sample is decoded exactly. 8-bit samples load as `Gray8`/`Rgb24`; any other precision `P` loads as `Gray16`, or `Rgb48`, each sample `s` scaled to 16 bits as `(s * 65535 + (2^P - 1) / 2) / (2^P - 1)`, which keeps every sample distinct, 0 and the largest one exact, and a 16-bit sample unchanged; `ImageInfo.BitsPerComponent` is `P`. The components mean what they mean in the other frames: three components are YCbCr (converted to RGB, with subsampled chroma upsampled by the same filters) unless the file says RGB, which is what lossless encoders write. A predictor outside 1-7, a point transform that leaves no bit, a restart interval that is not a whole number of MCU rows, a difference category above 16 and a sample that does not fit in its `P - Pt` bits are `InvalidImageContentException`. Streamed with three MCU rows of decoder state, an MCU row being one to four lines. The lossless process with arithmetic coding (SOF11) is `UnsupportedImageFeatureException` | Not supported |
| Arithmetic coding | Sequential (SOF9) and progressive (SOF10) frames, of 8-bit or 12-bit samples and every color model, sampling layout and scan structure of the Huffman-coded frames (they hold the same coefficients, so they decode to the same pixels): DC and AC conditioning tables (DAC: bounds `0 <= L <= U <= 15`, threshold `1 <= Kx <= 63`, the defaults `L = 0`, `U = 1`, `Kx = 5` otherwise), statistics that start again at each restart marker; a scan uses the conditioning defined when it starts, and a DAC segment that defines no table is read (the JPEG reference software writes one before the scans that use no conditioning). Invalid conditioning tables, magnitudes beyond those of the sample precision and runs of zeros past the end of a block or of a band are `InvalidImageContentException`. Entropy-coded data that ends early is not detected: an encoder may omit the final zero bytes of a segment, so the missing bytes read as zeros (T.81 D.2.6) and decode to something, as in other decoders; a file that is cut is still invalid (no EOI marker). The decoder buffers up to 4 KiB per block of an MCU before decoding it, since arithmetic coding has no small bound on the size of a block | Not supported |
| Color models | Grayscale (`Gray8`), JFIF full-range YCbCr and RGB (Adobe transform 0 or `R`,`G`,`B` identifiers) to `Rgb24`; CMYK and YCCK (four components) to `Rgb24` through the ICC conversion of `CmykConversionOptions`, see [Color management](#color-management) | The layout follows the pixel format, never the data: gray formats → one grayscale component, color formats → JFIF full-range YCbCr (BT.601); JFIF APP0 always written, no Adobe APP14. Non-opaque pixels require `BackgroundColor`; `Rgba64`/`Gray16` require `AllowBitDepthReduction` |
| Chroma subsampling | 4:4:4, 4:2:2, 4:2:0, 4:4:0, 4:1:1 and any integral sampling ratio (libjpeg-turbo-compatible triangle filters for factor 2, replication otherwise) | `Auto` (= 4:2:0 for color), 4:4:4, 4:2:2, 4:2:0; box-filter downsampling, canvas padded to whole MCUs by edge replication; ignored for gray formats |
| Restart markers | Sequential and progressive scans, sequence checked | Not written |
| Metadata (EXIF, XMP, ICC, JFIF density, COM) | Bounded; ICC reassembled from APP2 chunks; segments after the scans included | APP1 EXIF (orientation and dimensions reconciled; at most 65,527 bytes), APP1 standard XMP (at most 65,504 bytes), APP2 ICC in up to 255 chunks, JFIF density, COM for `Comment` entries; anything else follows `MetadataHandling` (`Strip` writes only the JFIF segment) |
| Animated images, posters | — | Rejected with `UnsupportedImageFeatureException` before any output; save a frame with `Frames[i].Save` |
| CMYK/YCCK (four components) | Identified; loaded only with `CmykConversion`, otherwise `UnsupportedImageFeatureException` | Not supported |
| Lossless with arithmetic coding (SOF11), hierarchical, sample precisions other than 8 and 12 bits in a frame that is not lossless, non-integral sampling | Not supported (`UnsupportedImageFeatureException`, also from `Identify`) | Not supported |

A progressive file holds the same picture as a baseline one in several scans of increasing precision, so a viewer can
show the whole image before the end of a download:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveProgressiveJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveProgressiveJpeg(string sourcePath, string jpegPath)
{
    using var image = Image.Load<Rgb24>(sourcePath);
    image.Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        Process = JpegEncodingProcess.Progressive,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

### WebP

`WebPEncoder.Compression` chooses the bitstream for every frame: `Lossless` (the
default) keeps every 8-bit sample, including the colors of fully transparent pixels (`ClearTransparentColors` opts into
transparent black); `Lossy` is VP8 with 4:2:0 chroma, controlled by `Quality` (0-100), and keeps alpha losslessly.
`Effort` (0-9, default 5) trades encoding time for size and never changes lossless pixels. Gray images are stored as RGB;
16-bit images need `AllowBitDepthReduction`.

Lossy output first tries 4x4 luma prediction from effort 2. If the modes exceed the VP8 first-partition limit
(524,287 bytes), it retries with 16x16 prediction only, at the same quantizer. This can change the decoded quality and
file size; `Quality` is not a target distortion guarantee. If that partition still does not fit, encoding throws
`UnsupportedImageFeatureException`: use lossless output or reduce the image dimensions.

The managed encoder makes its own prediction and entropy-coding decisions: lossless output uses one prefix-code group
(no spatial entropy image), and lossy output uses one quantizer for the frame and one token partition. It is not libwebp. No general file-size,
speed or perceptual-quality comparison with libwebp is guaranteed, and quality/effort numbers are not interchangeable
between encoders. Compare representative images at matched decoded quality for a lossy size comparison.

Lossless encoding works on one frame at a time. Its working memory counts against `MaxLiveAllocationBytes`: a copy of
the frame (4 bytes per pixel), the hash chains of the LZ77 parse (4 bytes per pixel, released before the output is
written), the recorded parse (at most 2 bytes per pixel) and the compressed stream, whose buffer doubles as it grows.
That is about 10 bytes per pixel, more for images that hardly compress. Effort 9 adds a second copy of the frame and
keeps two streams to pick the smaller one. A 16,384 x 16,384 image needs more than the default 512 MiB: raise the limit
to encode images of that size.

A still image streams to any destination. An animation (several frames, animation settings, or a writer without
`ExpectedFrameCount = 1`) declares its size before its frames, so it needs a **seekable** destination: frames are written
as they come and the RIFF size and alpha flag are patched at the end. Non-seekable streams are rejected with an
`ArgumentException` before anything is written (there is no spooling); path outputs are always seekable and atomic.
Durations are rounded to the nearest millisecond by default (`DurationRounding`), and the loop count holds 1 to 65,535
plays or infinite.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveWebp;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

Decoding: lossless images decode exactly; lossy images decode to the exact VP8 reconstruction (RFC 6386) converted to RGB
with a documented BT.601 contract and centered bilinear chroma upsampling (within one unit of libwebp). Still images
without alpha load as `Rgb24`, others and every animation as `Rgba32`. Animation frames are full-canvas displayed images
(alpha blending with an exact OVER contract, mixed lossy and lossless frames, exact millisecond durations); disposed areas
become transparent black and the `ANIM` background color is never painted. The container walker buffers one image's
payloads at most, skips unknown chunks, and charges the canvas to the limits before any image. ICC (RGB profiles), EXIF
and XMP are read from and written to extended files; text and resolution have no WebP chunk. A metadata chunk whose
payload is larger than what is left of `MaxMetadataBytes` fails with `ImageResourceLimitException` before it is buffered,
even when it would be skipped as invalid; repeated chunks are skipped without being buffered. VP8 interframes and hidden
frames are not supported.

### QOI

QOI is a small lossless format for one 8-bit RGB or RGBA still image. `QoiEncoder`
keeps every 8-bit sample, including the colors of fully transparent pixels; pixel formats with alpha are written with 4
channels, the others with 3 (gray as RGB), and 16-bit images need `AllowBitDepthReduction`. Its output is byte-identical to
the qoi.h reference encoder. QOI stores no metadata: ICC, EXIF, XMP, orientation, resolution and text follow
`MetadataHandling` (rejected by default). The header colorspace is written from `Metadata.TransferFunction` (`Strip`
writes 0) and decoded back into it. Encoding and decoding stream: the decoder keeps one row and consumes the input as it
arrives, the encoder writes bounded bands to any destination.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveQoi;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveQoi(string inputPath, string qoiPath, string linearQoiPath, string pngPath)
{
    using var image = Image.Load(inputPath);
    var frame = image.Frames[0]; // QOI stores one still image
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
<!-- endref -->

Decoding: 3-channel files load as `Rgb24` (opaque, whatever alpha values the chunks carry), 4-channel files as `Rgba32`.
Every chunk form is supported, runs may cross rows, and bytes after the end marker are not read. Zero dimensions, other
channel counts or colorspaces, a run past the last pixel, a missing end marker and truncation are
`InvalidImageContentException`; dimensions above 2^31 - 1 or the configured limits are `ImageResourceLimitException`
before any allocation.

### BMP

BMP, TGA and Netpbm are uncompressed (or run-length) formats for desktop interchange
(BMP), asset pipelines (TGA) and tooling fixtures (Netpbm). Each stores one still image, and each encoder makes its
format-specific choices explicit.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveAssetFormats;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveAssetFormats(string inputPath, string bmpPath, string tgaPath, string pnmPath)
{
    using var image = Image.Load(inputPath);
    var frame = image.Frames[0]; // BMP, TGA and Netpbm store one still image

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
<!-- endref -->

The BMP encoder never picks a lossy layout by itself: `BmpPixelLayout.Auto` is 24-bit or 32-bit. The indexed layouts
(`Indexed1`, `Indexed4`, `Indexed8`) store the colors of the image exactly when its palette can hold them, and reject
the image otherwise unless `AllowColorReduction` is set, which reduces the colors as `BmpEncoder.Quantization` says (the
quantizer of [`Quantize`](#color-reduction), or a fixed palette); `Indexed4` and `Indexed8` can be run-length encoded.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveBmpIndexed;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveIndexedBmp(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    var frame = image.Frames[0];

    // 8-bit indexes, run-length encoded (BI_RLE8). The palette holds the colors of the frame exactly when there are at
    // most 256 of them; AllowColorReduction accepts a deterministic palette for the other frames, here with dithering.
    // A BMP palette has no alpha, so non-opaque pixels need a background color.
    frame.Save(outputPath, new BmpEncoder
    {
        PixelLayout = BmpPixelLayout.Indexed8,
        Compression = BmpCompression.RunLength,
        AllowColorReduction = true,
        Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg },
        BackgroundColor = new Rgba64(65535, 65535, 65535),
        AllowBitDepthReduction = true,
    });
}
```
<!-- endref -->

| Feature | Decoding | Encoding |
| --- | --- | --- |
| DIB headers | `BITMAPINFOHEADER` and the V2, V3, V4 and V5 headers; the OS/2 1.x `BITMAPCOREHEADER` (12 bytes, 16-bit dimensions, three-byte palette entries, a palette that may end early at `bfOffBits`) and the OS/2 2.x `BITMAPCOREHEADER2` (16 to 64 bytes: the fields a short header leaves out are zero; the lengths 40, 52 and 56 are read as Windows headers). Other lengths and inconsistent headers are `InvalidImageContentException` | `BITMAPINFOHEADER`, or `BITMAPV4HEADER` (`LCS_sRGB`) for 32-bit output; OS/2 headers are never written |
| Layouts | 1/4/8-bit indexed (partial palettes, indexes outside the palette rejected), 16-bit (5-5-5), 24-bit BGR, 32-bit BGRX; 2-bit (Windows CE) is not supported | `BmpPixelLayout`: 24-bit `BI_RGB` or 32-bit `BI_BITFIELDS` by default (`Auto`); `Indexed1`, `Indexed4`, `Indexed8` (the palette stores only the colors used, `biClrUsed`); `Bgr565` (`BI_BITFIELDS`) and `Bgr555` (`BI_RGB`), which need `AllowBitDepthReduction` |
| Indexed output | | Exact when the image has at most `min(Quantization.MaxColors, 2^bits)` colors, or only colors of `Quantization.Palette` (whose entries that are not opaque are composited over `BackgroundColor`); otherwise `UnsupportedImageFeatureException` unless `AllowColorReduction`, which builds a deterministic median-cut palette (or takes the fixed one) and maps each pixel to its nearest entry, with the optional `Quantization.Dithering` (from the top row down, whatever the row order). `BmpEncoder.MaxColors`, `BmpEncoder.Dithering` and `BmpDithering` are obsolete aliases. Needs one byte per pixel of working memory |
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
| Alpha | The descriptor's alpha-bit count is authoritative: a 32-bit payload with 0 alpha bits has an unspecified fourth byte, which is discarded, and so is the attribute byte of a 16-bit grayscale pixel (the image is `Gray8`); with 8 alpha bits, 16-bit grayscale is gray and alpha, decoded to `La16` (`ImageColorModel.GrayscaleAlpha`); premultiplied alpha (attributes type 4 of the extension area) is rejected | 8 declared alpha bits for pixel formats with alpha, straight alpha, no extension area |
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
| Samples | 8 or 16 bits (big-endian); a sample above `MAXVAL` is `InvalidImageContentException`; a set PBM bit is black, while a PAM `BLACKANDWHITE` sample is an intensity | `MAXVAL` 255 or 65,535, so the output is always lossless (no bit-depth setting). A portable bitmap is lossless too: one bit per pixel, a set bit is black, and a pixel that is not exactly black or white (once composited over `BackgroundColor`) is `UnsupportedImageFeatureException` (binarize the image first with `Threshold`, see [Binarization](#binarization)); it is read back as `Gray8` with one bit per component |
| Plain rasters | Decimal samples with comments allowed; a prefix of a plain raster can be a complete raster with a smaller last sample, so truncation is not detectable there | Lines within 70 characters; PAM has no plain form, so alpha then needs `BackgroundColor`, as it does for a portable bitmap. A plain portable bitmap has one digit per pixel, without separators |
| Metadata | Netpbm stores none | Follows `MetadataHandling` |

### TIFF and BigTIFF

Use `ImageCollection` for multi-page documents (see
[Pages and representations](#pages-and-representations)); `Image.Load` reads the first page and `Image.Save` writes one.
By default `TiffEncoder` writes a sample layout that follows the pixel format losslessly (floating-point pixels as 32-bit
floating-point samples), and its bilevel and palette layouts store a page exactly or reject it, which is why it has
neither a background color, nor a bit-depth switch, nor a color reduction. Only `TiffCompression.Jpeg` is lossy. **It needs a seekable destination**, because a directory stores
the offsets of the strips it describes.

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Container | Classic TIFF and BigTIFF (64-bit offsets), both byte orders, IFD chains with checked offsets, cycle and overlap rejection, page count bounded by `MaxFrames` | Classic or BigTIFF (`TiffEncoder.BigTiff`), little- or big-endian (`BigEndian`) |
| Samples | Unsigned integers: grayscale of 1, 2, 4, 8 or 16 bits and RGB of 8 or 16 bits, with an optional alpha sample at 8 and 16 bits → `Gray8`/`Gray16`/`La16`/`La32`/`Rgb24`/`Rgb48`/`Rgba32`/`Rgba64` (grayscale samples of 1, 2 and 4 bits are scaled exactly to `Gray8`) | `TiffPixelLayout.Auto` (the default) follows the pixel format losslessly: gray formats → gray, `La16`/`La32` (and `A8`, as black) → gray with alpha, `Rgb24`/`Rgb48` → RGB 8/16, the other formats with alpha → RGBA; nothing is quantized and alpha is never dropped |
| Signed samples (`SampleFormat` 2) | Grayscale (`BlackIsZero` or `WhiteIsZero`) and RGB of 8 or 16 bits, with an optional alpha sample, loaded as the unsigned samples of the same width that are larger by half of the range: `-2^(n-1)` → 0, 0 → `2^(n-1)`, `2^(n-1) - 1` → `2^n - 1` (a signed 16-bit sample `v` is the `Gray16` value `v + 32768`), exactly, the alpha sample too; the page is then read like an unsigned one (`ImageInfo` does not tell them apart) | Not written |
| Floating-point samples (`SampleFormat` 3) | `BlackIsZero` grayscale and RGB of 16 (half precision), 24 and 32 bits, with an optional alpha sample → `RgbaHalf` (16 bits) or `RgbaFloat` (24 and 32 bits), every value kept (signed zeros, subnormal numbers, infinities, not-a-number values and their payloads); a gray sample is repeated in the three colors, and a page without alpha is opaque. A 24-bit number has 1 sign bit, 7 exponent bits (bias 63) and 16 fraction bits, which a `float` holds exactly. The samples are linear light (`ColorTransferFunction.Linear`) unless an ICC profile describes them; `ImageInfo.BitsPerComponent` is 16, 24 or 32 | `RgbaFloat` and `RgbaHalf` pixels with `TiffPixelLayout.Auto` and any compression but JPEG: 32-bit floats, exactly, in the fewest samples that hold the page (one gray sample when the three colors of every pixel are the same number and no ICC profile is written, no alpha sample when every alpha is exactly 1). The pixels must be labeled linear light (the label of a new floating-point image) or described by an ICC profile: other pixels follow `MetadataHandling`, since a floating-point page without profile is read as linear light |
| Photometric interpretation | `WhiteIsZero`, `BlackIsZero`, `RGB`, palette color, CMYK (separated) and YCbCr | `BlackIsZero` and `RGB`; `WhiteIsZero` for the bilevel layout, palette color for the palette layout, YCbCr for JPEG compression |
| Bilevel | One bit per pixel, most or least significant bit first (`FillOrder` 1 or 2) → `Gray8`, reported with 1 bit per component | `TiffPixelLayout.Bilevel`: every pixel must be opaque and exactly black or exactly white, whatever the pixel format (never thresholded nor dithered: binarize the page first with `Threshold`, see [Binarization](#binarization)) |
| Palette | Indexes of 1, 2, 4 or 8 bits → `Rgb24`. The entries of a `ColorMap` are 16-bit values: they are 8-bit colors when they are all multiples of 257 or all multiples of 256 (the two ways writers store an 8-bit color); otherwise the page is `Rgba64`, with the entries as they are | `TiffPixelLayout.Indexed`: 8-bit indexes into a sorted `ColorMap` of the colors of the page, at most 256 (never reduced); every pixel must be opaque; the colors of a 16-bit pixel format keep their 16 bits |
| CMYK | 8 or 16 bits, with an optional alpha sample: loaded only with `CmykConversion`, by an ICC conversion to RGB (see [Color management](#color-management)); `Identify` reports `ImageColorModel.Cmyk` and the CMYK profile | Not written |
| YCbCr | 8 bits, one chroma sample for 1, 2 or 4 pixels in each direction, `YCbCrCoefficients` and `ReferenceBlackWhite` applied → `Rgb24`; 8 to 16 bits with JPEG compression | Only as the samples of JPEG compression |
| Alpha | Only unassociated (straight) extra samples are alpha | Always unassociated |
| Compression | None, Deflate, LZW, PackBits, CCITT modified Huffman, Group 3 (one and two dimensions, with or without fill bits) and Group 4, JPEG (TIFF Technical Note 2, with or without `JPEGTables`: DCT-based datastreams of 8-bit or 12-bit samples, lossless ones of 8-bit to 16-bit samples), and JPEG 2000 (compression 34712: a codestream or a JP2 file in each strip or tile, samples of 8 or 16 bits, the high-throughput block coder included) | `TiffCompression.None`, `Deflate` (the default), `Lzw`, `PackBits`, `CcittModifiedHuffman`, `CcittGroup3` and `CcittGroup4` (bilevel layout only), `Jpeg` (lossy: `JpegQuality`, `JpegChromaSubsampling`) |
| Predictor | None, horizontal differencing (integer samples of 8 and 16 bits) and the floating-point predictor of TIFF Technical Note 3 (floating-point samples) | The floating-point predictor for floating-point samples compressed with Deflate or LZW; no predictor otherwise |
| Layout | Strips and tiles, chunky samples only | Strips only (`TiffEncoder.RowsPerStrip`, or about 64 KiB of samples per strip); a JPEG page is one strip |
| Metadata | Resolution, orientation (reported, never applied), ICC profile, XMP | The same four, following `MetadataHandling`; EXIF and text entries are not written |
| Memory | One strip or one row of tiles at a time (a JPEG strip is decoded row by row, like a JPEG file); one page of a large document reads only the bytes its structure points at | One strip at a time, whatever the size of the page or the number of pages; a JPEG page costs what a JPEG file costs |

- **Compressed data is never repaired.** The decompressed size of a strip or a tile is known before it is read: a block
  that produces fewer bytes, an LZW code the table does not hold, a fax code that does not exist, a fax row that is too
  long or too short, and a JPEG datastream whose frame is not the one of its strip are `InvalidImageContentException`,
  never a page completed with white or with garbage. The bytes a block produces beyond its size are not read, and no
  more compressed bytes are read than a block of its size can need (16 times its size, and room for the tables of a
  JPEG datastream), whatever its byte count declares: many blocks that point at the same bytes cost no more than
  their pixels.
- **CCITT.** A white run is the sample 0, and `PhotometricInterpretation` says what 0 displays as, as for any bilevel
  data. The encoder writes `WhiteIsZero` pages; Group 3 is written in one dimension, with an end-of-line code before
  each row and no fill bits, and Group 4 strips end with the end-of-facsimile-block sequence.
- **JPEG.** A strip or a tile is a JPEG datastream, decoded by the JPEG decoder of the library with everything it
  supports ([JPEG](#jpeg): baseline, extended sequential and progressive frames, Huffman or
  arithmetic coded, lossless frames, any integral sampling) and written by
  its encoder. `BitsPerSample` is the precision of the datastreams: 8 bits, 12 bits, or any number of bits from 8 to 16
  when they are lossless; a datastream of another precision than its page is `InvalidImageContentException`. Samples
  of more than 8 bits are scaled to 16 bits like the ones of a JPEG file, and the page loads as `Gray16` or `Rgb48`
  (`ImageInfo.BitsPerComponent` reports the precision); the `ReferenceBlackWhite` values of a YCbCr page are then the
  full range of its precision (0, 4095, 2048, 4095, 2048, 4095 for 12 bits, which is also the default). The photometric interpretation of the page says what the components are, whatever the datastream would
  mean as a JPEG file: YCbCr components are converted to RGB as in a JFIF file (the page must use the full-range ITU-R
  BT.601 samples of JFIF: other `YCbCrCoefficients` or `ReferenceBlackWhite` values are rejected), and grayscale, RGB and
  CMYK components are the stored samples. The frame of a datastream must be as wide as its strip or tile, hold at
  least the rows of the page it covers and at most the rows of a strip or tile. An encoded JPEG page is exactly what `JpegEncoder` writes with the same quality
  and subsampling and the baseline process, in one strip: at most 65,535 pixels per side, 8-bit samples, no alpha (a pixel that is not opaque is
  rejected).
- **YCbCr without JPEG** (uncompressed, Deflate, LZW or PackBits data units). With the luma coefficients of
  `YCbCrCoefficients` (ITU-R BT.601 by default) and the black and white pairs of `ReferenceBlackWhite`:
  `Y' = (Y - black) * 255 / (white - black)` and `C' = (C - black) * 127 / (white - black)` for each chroma sample, then
  `R = Y' + Cr' * (2 - 2 * LumaRed)`, `B = Y' + Cb' * (2 - 2 * LumaBlue)` and
  `G = (Y' - LumaBlue * B - LumaRed * R) / LumaGreen`, each rounded to the nearest integer (ties upward) and clamped, in
  double precision. `ReferenceBlackWhite` defaults to full range with chroma centered on 128 (0, 255, 128, 255, 128,
  255), the values writers of YCbCr data mean. Subsampled chroma is replicated, not interpolated: the specification
  defines no reconstruction filter. Other readers round the intermediate values differently and can differ by a few
  levels.

Old-style JPEG compression (`Compression` 6), L\*a\*b\* and transparency-mask data, planar storage of several samples,
the reversed fill order for other data than bilevel, other sample widths (signed samples of 32 bits, 64-bit
floating-point samples), signed or floating-point palette, CMYK and YCbCr data, floating-point `WhiteIsZero` data, signed
or floating-point samples compressed with JPEG or JPEG 2000, horizontal differencing of floating-point samples and the
floating-point predictor of integer samples, samples of different formats in one pixel, more than one extra sample,
premultiplied or unspecified extra samples, alpha next to palette, YCbCr or packed grayscale samples, the uncompressed
mode of the CCITT compressions and the LZW bit order of TIFF 5.0 drafts are recognized and rejected with
`UnsupportedImageFeatureException`.

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

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadAvif;format=md-fenced-code-block;dedent=true -->
```csharp
public static string ConvertAvif(string avifPath, string pngPath)
{
    using var image = Image.Load(avifPath);

    // The color description of the file (primaries, transfer characteristics, matrix and range code points) is a
    // label, as an ICC profile is: the matrix and the range were applied to produce RGB, nothing else was converted.
    var description = $"{image.Width}x{image.Height} {image.PixelFormat}, frames: {image.Frames.Count}, color: {image.Metadata.Cicp}";

    // PNG keeps 16-bit samples, alpha and animations, without loss, and the primaries and the transfer
    // characteristics of the description (cICP chunk), so the samples of an HDR image keep their meaning.
    image.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
    return description;
}
```
<!-- endref -->

| Feature | Decoding |
| --- | --- |
| Presentation | A file with the `avis` brand presents its image sequence (the `pict` track): an animation whose frames are full-canvas images. Any other file presents its primary item. Thumbnails, gain maps, depth maps and the other images of the file are not read |
| Derived images | A grid (`grid`) of AV1 images, and identity derivations (`iden`): an `iden` item is its input image, with its own `clap`, `irot` and `imir` applied after those of the input. Up to eight nested derivations are followed. The pixels are those of the coded image: the transformations of the whole chain select the part of it that is presented and give `Metadata.Orientation`. The color description, the ICC profile and the other descriptive properties are those of the primary item, or else of the image it is derived from. The alpha plane of a derived image is the auxiliary image of that item (not the one of its input), and must be presented as the colors are: readers disagree on an untransformed alpha plane under a transformed derivation, which is rejected |
| Images built from other images | An overlay (`iovl`), a grid whose tiles are derived images, and the identity derivations of those, nested up to eight deep. Each input image is decoded and converted as when it is presented alone, moved by its own `clap`, `irot` and `imir`, then composited: an overlay fills its canvas with its fill color and draws its input images in their order, each at its offset (which may be negative or exceed the canvas), with the source-over operator on straight alpha and exact rounding; a grid places its tiles side by side. The image is always `Rgba32`, or `Rgba64` when a coded image has more than 8 bits (an 8-bit input is then scaled exactly), with straight alpha. The transformations of the primary item apply to the image that is built like those of a coded image: `clap` selects a part of it, `irot` and `imir` are `Metadata.Orientation`. Nothing converts colors while an image is built, so every description of the tree must be the same one: the color primaries and the transfer characteristics of the `colr` boxes (or of the bitstream of a coded image that has none), and the bytes of the ICC profiles. An image without description takes the one of the image it is a part of. The description of the image is the first one in the order of the tree, starting at the primary item; when no box has one, it is the one of the bitstream of the first coded image. The fill color of an overlay is an sRGB color: one that is neither transparent nor black needs an image that is sRGB. The other descriptive properties (`clli`, `mdcv`, `pasp`) and the EXIF and XMP items are those of the primary item. Every coded image counts toward `MaxTotalPixels`, like a frame that is decoded without being shown, and each canvas of the tree is checked against the canvas limits |
| Bitstream | Every AV1 profile: 8, 10 and 12 bits, 4:4:4, 4:2:2, 4:2:0 and monochrome, intra and inter frames, intra block copy, super-resolution, reference scaling, film grain, temporal and spatial layers |
| Layered images | A layered (progressive) image holds a frame per spatial layer, each refining the previous ones. The image is the frame of the highest layer of the operating point, which is the image at its full quality; the lower layers are decoded on the way and never returned. The `a1op` property selects the operating point (the first one otherwise) and the `lsel` property a layer, whose frame is then the image and must have the size that the `ispe` property declares. In an image sequence, every sample gives the frame of its highest layer |
| Pixel formats | 8-bit files load as `Rgb24` (`Rgba32` with alpha, `Gray8` for monochrome without alpha); 10-bit and 12-bit files load as `Rgb48` (`Rgba64` with alpha, `Gray16`/`La32` for monochrome), their samples scaled to 16 bits exactly; monochrome 8-bit files with alpha load as `La16` |
| Color conversion | The matrix coefficients and the range of the `colr` box, or else of the bitstream, convert YCbCr to RGB with integer arithmetic and a documented rounding: identity (RGB, exact), BT.709, FCC, BT.470BG, BT.601, SMPTE 240M, YCgCo, BT.2020 non-constant luminance, SMPTE ST 2085 (Y'D'zD'x) and the chromaticity-derived non-constant luminance coefficients, whose weights are the exact luminance of the color primaries that the file declares (primaries without chromaticities are rejected); unspecified coefficients are BT.601. The BT.2020 and chromaticity-derived constant luminance coefficients and ICtCp (with PQ or HLG) are not a matrix of the samples: they go through the transfer characteristics that the file declares, in double precision, and give the nearest sample to the exact value (a value within about a billionth of a rounding boundary may round either way, depending on the platform). The extents of the chroma of constant luminance are the ones the equations of ITU-T H.273 give, which differ in the fourth decimal from the constants printed in Rec. ITU-R BT.2020. The samples stay encoded with the transfer characteristics of the file. Subsampled chroma is upsampled bilinearly at the sample position that the bitstream declares |
| Alpha | The alpha auxiliary image, or the alpha track of a sequence, of the size of the color image. Premultiplied colors (a `prem` reference from the color image, or from the color track, to its alpha plane) are divided by the alpha of their pixel once they are converted, with an integer division rounded to nearest: pixels always have straight alpha. A color above its alpha is clipped, and nothing is left of the color of a fully transparent pixel, which is black |
| Grids | Grid images, for the color image and its alpha plane. The transformations of a grid apply once, to the assembled image, whether or not the tiles repeat them. With `ImageConfiguration.MaxDegreeOfParallelism` above 1, the tiles of a grid of at least 65,536 samples are decoded by up to that many workers (the pixels and the errors are those of one worker; each worker holds the decoder and the frame of its tile, charged to `MaxLiveAllocationBytes`): about 4 times faster with 8 workers for a 3840x2160 photo in tiles of 512x512 on an Apple M5 Pro |
| Parallelism | With `ImageConfiguration.MaxDegreeOfParallelism` above 1, the post-processing filters of a frame of at least 65,536 pixels (the loop filter, CDEF, the super-resolution upscaling and the loop restoration) run by bands of rows, with the pixels of one worker; each worker needs its own rows, charged to `MaxLiveAllocationBytes`. On an Apple M5 Pro, the loop filter and CDEF are 25% (default quality) to 39% (quality 40) of the decoding time of a 3840x2160 image, which decodes 1.27 to 1.45 times faster with 8 workers. The tiles of a grid that several workers decode filter their frames on their own worker. The rest of the decoding (the symbols, the reconstruction of the blocks, film grain and the color conversion) runs on the calling thread |
| Transformations | `irot` and `imir` are composed and reported as `Metadata.Orientation`, never applied (`AutoOrient()` applies them); the EXIF orientation tag is ignored, as the format requires. `clap` crops the image when it selects whole samples. The properties apply in the order of their association: a `clap` that follows a rotation or a mirroring selects a part of the image as they present it, and the pixels are still returned as they are coded. `pasp` is reported as `Metadata.PixelAspectRatio`, never applied. An image sequence takes its orientation from the matrix of the track header of its color track, when the matrix turns the images by quarter turns or mirrors them (any other matrix places the track in the presentation of a player and is ignored), and its clean aperture from the `clap` box of its sample entry; its alpha track has the same ones or none |
| Metadata | ICC profile, EXIF, XMP, the CICP color description, the content light level (`clli`), the mastering display color volume (`mdcv`) and the pixel aspect ratio (`pasp`). A still image takes them from its primary item. An image sequence takes its ICC profile, its color description and the `clli`, `mdcv` and `pasp` boxes from the sample entry of its track, and its EXIF and XMP from the `meta` box of the track, or else from the `meta` box of the file when it precedes the `moov` box. The light levels and the mastering display that the bitstream itself carries (AV1 metadata units) and the content color volume (`cclv`) are not read |
| Identification | `Header` mode reads the `meta` box (or the `moov` box of a sequence). When the file has no `colr` box, it reads on to the sequence header of the bitstream, which gives the color description and tells RGB samples from YCbCr ones, and stops there; if the bitstream precedes that box, it has already gone by and the color model is `Unknown`, never guessed. The EXIF and XMP items, which are data of the file, are returned by a full scan or a load. `FullScan` also walks every item and sample, and checks the sample format of the bitstream against its declaration |
| Animations | Exact frame durations (the sample durations over the time scale of the track); the number of plays comes from the edit list, and a sequence without edit list loops forever. An edit list that selects parts of the media is applied: each edit plays the media from its media time for its duration, an image is shown for the exact time that the edits cover of it (a fraction of a second, whatever the time scales of the movie and of the media), and an image that no edit covers is not a frame of the result. It is still decoded, since the images that follow may refer to it: it counts toward `MaxTotalPixels` like a frame of the bitstream that is not shown, and not toward the frame limit. The usual single edit that starts with the media and reaches its last image shows every image for its whole duration: its own duration, which writers round to the time scale of the movie, is only used for the number of plays. An alpha track has the edit list of its color track, or none. A fragmented sequence (a `moov` box with an `mvex` box, followed by `moof` and `mdat` boxes) is read fragment by fragment: the images of a fragment follow the ones that are known, the tables of a fragment are released once its images are read, and the sequence ends with the file, so a header identification does not report a frame count (`FrameCount` is `null`) and a file that is cut after one of its fragments is a shorter sequence. The data offsets of the runs follow the three base rules of ISO/IEC 14496-12 (an explicit offset, the `moof` box, or the end of the previous track fragment), and the durations and sizes come from the run, the track fragment or the `trex` defaults. `ImageReader` decodes a sequence frame by frame with a memory that does not depend on the number of frames |
| Limits | Every frame of the bitstream is checked against the canvas limits, and the frames that are decoded without being shown (hidden frames, and the lower layers of a layered image) count toward `MaxTotalPixels` |

More than eight nested derivations, an image built from images that do not have the same color description (color primaries, transfer characteristics or ICC profile), an overlay whose fill color shows on an image that is not an sRGB one, an alpha plane that is an auxiliary image of an overlay or of a grid of derived images (the alpha of such an image is the one of its input images), movie fragments that do not follow each other (a `tfdt` time that is not the end of the previous samples), that have composition time offsets, whose samples precede them in the file or that precede the `moov` box, a fragmented sequence whose edit list selects a part of its media, edit lists that do not play the media forward at its rate (empty edits, a media rate other than 1, edits that are out of order or that overlap), an alpha track with another edit list than its color track,
sequences whose images change size, large scale tile streams, reserved matrix coefficients, and matrix
coefficients without the transfer characteristics they are defined with (constant luminance with unspecified or
reserved transfer characteristics, ICtCp with other ones than PQ and HLG) are rejected with
`UnsupportedImageFeatureException`.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveAvif;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

| Feature | Encoding |
| --- | --- |
| Compression | `AvifCompression.Lossless` (the default) codes the R'G'B' samples without color conversion (identity matrix, 4:4:4, full range), and gray pixel formats as a monochrome image: every coded sample is decoded back exactly. `AvifCompression.Lossy` codes Y'CbCr samples; `Quality` (0 to 100, 75 by default) selects the quantizer and `ChromaSubsampling` the chroma layout: 4:2:0 (the default), 4:2:2 or 4:4:4. Lossless files are large: select the lossy coding for photographs |
| Bit depth | 8-bit pixel formats are written with 8 bits, or with `BitDepth` 10 or 12 (scaled up exactly). AVIF stores at most 12 bits: `Rgba64` and `Gray16` need `BitDepth`, or `AllowBitDepthReduction` (12 bits), and are rounded to nearest. The samples of a 10-bit or 12-bit AVIF image, which load as 16-bit pixels, are coded back unchanged at their depth |
| Color conversion | Lossy output keeps the matrix coefficients and the range of `Metadata.Cicp` when they are ones that the decoder converts (BT.709, FCC, BT.470BG, BT.601, SMPTE 240M, YCgCo, BT.2020 non-constant luminance), and uses BT.601 in full range otherwise (the SMPTE ST 2085, chromaticity-derived, constant luminance and ICtCp coefficients are read, not written). The conversion is integer only and rounds each sample once; a subsampled chroma sample is the mean of the pixels it covers (centered samples) |
| Alpha | An auxiliary image at the bit depth of the color image, always lossless. A still image whose pixels are all opaque has none. `ClearTransparentColors` replaces the color of the fully transparent pixels with black, which makes the file smaller. `PremultiplyAlpha` stores the colors multiplied by the alpha of their pixel (rounded to nearest, then coded like any color) and declares it with a `prem` reference: it is the consent to that rounding, since a reader divides the colors by the alpha, which does not give the original color back for the pixels that are nearly transparent |
| Effort | `Effort` (0 to 9, 5 by default) widens the search: block sizes from 64x64 down to 4x4, rectangular partitions (and the partitions into three and four parts at effort 9), the number of intra modes that are tried, angle deltas, recursive filter modes, the prediction of chroma from luma, the number of transform types that are tried, the split of the transform blocks of intra blocks, the number of values that are tried for the in-loop filters and the copy of repeated blocks of lossless screen content (not at effort 0), and for the images of a sequence the reach of the motion search, its refinement to a quarter of a sample and the number of predicted motion vectors that are tried. The efforts 0 to 2 of lossy output also choose a Wiener filter for each unit of 256x256 samples of each plane (loop restoration), which recovers a part of what their narrow search leaves: 0.1 to 0.2 dB for 1% of the bytes on the photograph of the `avif-quality` report; from effort 3 the search leaves nothing that the filter recovers there, and it is not used. It never changes the decoded pixels of lossless output. Encoding is single-threaded and far slower than a native encoder: of the order of a megapixel per second at effort 0, and of a seventh of that at the default effort, for a lossy photograph on a recent laptop (the `avif-quality` command of the benchmarks measures sizes, qualities and times) |
| Animations | An image with several frames is written as an image sequence (`avis` brand): every frame is a full image, with its exact duration (the time scale of the track is the least common multiple of the denominators; `DurationRounding` rounds to microseconds when there is none of 32 bits) and the play count of the animation. A pixel format with alpha has an alpha track. The first image is also the primary item of the file, for the readers that do not play sequences. The destination must be seekable and the frame count known: the `moov` box, which indexes the images, is written at completion in the room reserved before the images. A sequence of images without duration that repeats forever has no edit list: an edit without duration cannot be repeated |
| Screen content | An image that looks like the content of a screen (a tenth of its 16x16 blocks have few colors, far from one another) is coded with palettes: a block whose samples have at most 8 values in a plane is predicted exactly, and a block with a few more values by 8 colors that stand for groups of close values. Text, interface elements and pixel art are several times smaller than without, lossless or not; a photograph is not affected. A lossless image of that kind also copies what repeats (intra block copy, from effort 1): a block of 8x8 samples or more that is identical, in every plane, to a part of the image that is already coded is stored as the position of that part. AV1 only lets a block copy samples that are at least five superblocks of 64 samples before it, so an image of fewer than 16 superblocks (256x256 pixels, for instance) does not look for copies, and the characters of a line of text are copied from lines above rather than from the same line: measured with the `avif-quality` report on 768x576 screens, a page of text is 31% smaller and flat graphics 41% smaller. Looking for the copies takes several times the encoding time of such an image without them. Lossy output does not copy blocks: a frame that does has no in-loop filter |
| Inter prediction | The first image of a sequence is a key frame. Each other image is predicted from the image that precedes it: a block copies the samples that a motion vector points to (to a quarter of a sample), codes what differs, or is intra coded where that costs less, so the part of an image that does not change costs almost nothing. `KeyFrameInterval` (0 by default: no other key frame) is the number of images from a key frame to the next one; 1 makes every image a key frame. Key frames are the sync samples of both tracks: a reader that seeks decodes from the last one. Lossless output stays lossless |
| Metadata | A `colr` box of type `nclx` is always written: the primaries and the transfer characteristics of `Metadata.Cicp` (unspecified without one) and the matrix and the range that were used. The ICC profile, EXIF and XMP are stored; the orientation of a still image is stored as `irot` and `imir` properties (the pixels are written as they are). The orientation of an image sequence is the matrix of the headers of its tracks, and its clean aperture a `clap` box of their sample entries; the headers keep the size of the coded images, which libavif and FFmpeg take for the size that the aperture applies to. The content light level, the mastering display color volume and the pixel aspect ratio are stored in `clli`, `mdcv` and `pasp` boxes: item properties of the image, and boxes of the sample entries of a sequence (the alpha plane only has the pixel aspect ratio). `CleanAperture` stores a `clap` property on a still image and on its alpha plane: every pixel is coded, and readers show the rectangle (inside the image; on an even column with subsampled chroma, and on an even row with 4:2:0) |
| Grid | `GridTileSize` cuts a still image that is larger than a tile into tiles of that size, each one coded on its own, which a `grid` item assembles (the alpha plane is a grid of the same tiles; an image that one tile holds is coded whole, and an image sequence is never a grid). It bounds what a decoder decodes at once and the memory of the encoder, and it is what the profiles of the format expect of large images. Each side of a tile is between 64 and 65,536 pixels, a grid has at most 256 columns and 256 rows, and with subsampled chroma the widths of the tiles and of the image are even (their heights too for 4:2:0). The tiles of the right column and of the bottom row extend past the image, whose last column and last row they repeat. A tile is coded without its neighbors: lossy output can show the edges of the tiles and is larger than the image coded whole; lossless output is exact. With `ImageConfiguration.MaxDegreeOfParallelism` above 1 (the configuration of the saved image), the tiles of a grid of at least 65,536 samples are coded by up to that many workers, each with encoders of its own charged to `MaxLiveAllocationBytes`, and the file is byte-identical to the one of one worker. With 8 workers on an Apple M5 Pro, a 3840x2160 photo in tiles of 512x512 is coded about 6 times faster in lossy mode (effort 0: 1.1 s to 0.19 s; effort 5: 21.4 s to 3.7 s), and 4 to 6 times faster losslessly (effort 0: 4.9 s to 1.2 s; effort 5: 67 s to 10.6 s) |
| Memory | A still image is coded in memory before the file is written, so it goes to any stream; a lossless image of screen content adds 4 bytes per pixel for the table of the blocks that can be copied, and lossy output at the efforts 0 to 2 keeps 4 bytes per syntax element of the frame (about as many as its bits) until the frame is filtered, since the filters of the loop restoration are chosen last and coded with the blocks; a grid is coded one tile at a time, with the planes and the search state of one tile. An image sequence keeps 24 bytes per frame; its coded frames are written as they are encoded |
| Limits | At most 65,536 pixels per side (65,535 for an image sequence) and 4 GiB for a still image |

The encoder does not use every coding tool of AV1: see [Limitations](#limitations).

### HEIC

HEIC is decoded and encoded: HEVC (H.265) images in the HEIF container that AVIF also uses, which is the format of the
photos of Apple devices and of many cameras. The HEVC decoder is fully managed and written from Rec. ITU-T H.265. It is
exact: every plane it decodes equals the output of the reference decoders (FFmpeg, libde265 and the HM reference
software), and the picture hashes that a bitstream carries are checked by the tests.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadHeic;format=md-fenced-code-block;dedent=true -->
```csharp
public static string ConvertHeic(string heicPath, string jpegPath)
{
    using var image = Image.Load(heicPath);
    var description = $"{image.Width}x{image.Height} {image.PixelFormat}, orientation: {image.Metadata.Orientation}";

    // A camera stores the image as its sensor reads it, and the rotation to apply as a property of the file: the
    // decoder reports it as the orientation of the image and moves no pixel. AutoOrient applies it, so that the
    // JPEG file needs no orientation.
    image.AutoOrient();

    // The ICC profile of the photo (Display P3 for a recent phone) and its EXIF block are stored in the JPEG file
    image.Save(jpegPath, new JpegEncoder { Quality = 90 });
    return description;
}
```
<!-- endref -->

| Feature | Decoding |
| --- | --- |
| Detection | The brands of the file type box: `heic`, `heix`, `heim`, `heis` (images) and `hevc`, `hevx`, `hevm`, `hevs` (image sequences). A file with a generic major brand (`mif1`, `msf1`, `miaf`) is HEIC or AVIF according to its compatible brands, and a file that has both according to the codec of its primary item, or of the track of its sequence. `Image.DetectFormat`, which sees the first bytes only, answers from the brands that these bytes hold |
| Presentation | A file with a sequence brand presents its image sequence (the `pict` track): an animation whose frames are full-canvas images. Any other file presents its primary item. Thumbnails, gain maps, depth maps and the other images of the file (the other photos of a burst, for instance) are not read |
| Bitstream | The profiles of version 1 (Main, Main 10, Main Still Picture) and of the range extensions (Monochrome, 4:2:2 and 4:4:4 profiles): 8 to 12 bits, 4:4:4, 4:2:2, 4:2:0 and monochrome; intra pictures and inter pictures (P and B, with two reference lists, weighted prediction, temporal motion vector prediction and long-term reference pictures); tiles, wavefront streams and dependent slice segments (decoded sequentially); scaling lists, PCM and lossless (transform bypass) blocks; the tools of the range extensions (cross-component prediction, residual DPCM, transform skip of large blocks, chroma QP offset lists, persistent Rice adaptation). Only the base layer of a layered stream is decoded |
| Configuration | The parameter sets of the `hvcC` configuration record and the ones of the bitstream (`hev1` tracks). The sample format of the bitstream must be the one that the record declares, and the size of a picture the one of its `ispe` property |
| Pixel formats | 8-bit files load as `Rgb24` (`Rgba32` with alpha, `Gray8` for monochrome without alpha); 9-bit to 12-bit files load as `Rgb48` (`Rgba64` with alpha, `Gray16`/`La32` for monochrome), their samples scaled to 16 bits exactly; monochrome 8-bit files with alpha load as `La16` |
| Color conversion | As for AVIF: the matrix coefficients and the range of the `colr` box, or else of the video usability information of the bitstream, convert YCbCr to RGB with integer arithmetic; unspecified coefficients are BT.601. A bitstream without video signal description uses the limited range, which is what H.265 infers and how libheif and Apple ImageIO read such a file. The chroma samples of a 4:2:0 picture are at the position that the bitstream declares (types 0, 1 and 2 of H.265: at the left of the luma samples by default), and the ones of a 4:2:2 picture at the position of the luma samples of even columns |
| Alpha | The alpha auxiliary image (`urn:mpeg:hevc:2015:auxid:1` or the URN of MPEG-B), or the alpha track of a sequence, of the size of the color image. An alpha plane uses the full range unless its bitstream states the limited range. Premultiplied alpha is rejected |
| Grids | Grid images, for the color image and its alpha plane: the layout of the photos of a phone (tiles of 512x512 samples). A grid without alpha plane is converted one row of tiles at a time, so its decoded planes hold one row of tiles, not the whole image. With `ImageConfiguration.MaxDegreeOfParallelism` above 1, the tiles of a grid of at least 65,536 samples are decoded by up to that many workers (the pixels and the errors are those of one worker; each worker holds the decoder and the picture of its tile, charged to `MaxLiveAllocationBytes`): about 5 times faster with 8 workers for a 3840x2160 photo on an Apple M5 Pro |
| Transformations | `irot` and `imir` are composed and reported as `Metadata.Orientation`, never applied (`AutoOrient()` applies them); the EXIF orientation tag is ignored, as the format requires. `clap` crops the image when it selects whole samples, as does the `clap` box of the sample entry of a sequence. The properties apply in the order of their association: a `clap` that follows a rotation or a mirroring selects a part of the image as they present it, and the pixels are still returned as they are coded. `pasp` is validated and not reported, and the matrix of the track header of a sequence is ignored |
| Metadata | ICC profile, EXIF, XMP and the CICP color description, as for AVIF |
| Identification | `Header` mode reads the `meta` box (or the `moov` box of a sequence), whose configuration record gives the sample format and, when the file has no `colr` box, the color description of the bitstream; when the record holds no sequence parameter set (an `hev1` track), it reads on to the one of the bitstream. `FullScan` also walks every item and sample |
| Animations | Exact frame durations, in presentation order: the images of a sequence are stored in decoding order, and the composition offsets of the track (`ctts`) give the order and the durations of the images that are shown. The number of plays comes from the edit list, and a sequence without edit list loops forever. `ImageReader` decodes a sequence frame by frame with a memory that does not depend on the number of frames: the reference pictures of the stream, and the samples of at most 16 images that are decoded before the image that is shown |
| Limits | Every picture of the bitstream is checked against the canvas limits, at the size that the decoder outputs: the conformance window of a picture, which removes its padding to a whole number of coding blocks (a picture of 1080 rows is coded with 1088), does not count, unless it removes more than that padding. As for AVIF, a clean aperture does not reduce what is checked: the picture is decoded whole |
| Errors | No concealment: a missing reference picture, an incomplete picture or a value outside its range is `InvalidImageContentException` |

The screen content coding, multilayer and 3D extensions, bit depths above 12 bits, luma and chroma samples of different
bit depths, separate color planes, field sequences, extended precision processing, and the chroma sample positions of
types 3 to 5 are rejected with `UnsupportedImageFeatureException`, as are the container features that the
AVIF decoder rejects.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveHeic;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveHeic(string photoPath, string heicPath, string losslessPath, string animatedPath, string animatedHeicPath)
{
    using (var photo = Image.Load(photoPath))
    {
        // Lossy 4:2:0 with 8 bits is the Main Still Picture profile of HEVC, which every HEIC reader decodes.
        // The alpha plane, when the image has one, is always stored losslessly.
        photo.Save(heicPath, new HeicEncoder
        {
            Compression = HeicCompression.Lossy,
            Quality = 80, // 0-100: size versus fidelity
            Effort = 4,   // 0-9: encoding time versus size
            AllowBitDepthReduction = true, // 16-bit sources are written with 12 bits
        });

        // The default is lossless: every sample of an 8-bit image is decoded back exactly. The R'G'B' samples are
        // coded as they are (4:4:4), which is a profile of the range extensions of HEVC: fewer readers decode it.
        photo.Save(losslessPath, new HeicEncoder { AllowBitDepthReduction = true });
    }

    // An animation is written as an image sequence, with its exact durations and its play count. It needs a seekable
    // destination (the index of the images is written last): a path, a FileStream or a MemoryStream.
    using var animation = Image.Load(animatedPath);
    animation.Save(animatedHeicPath, new HeicEncoder { Compression = HeicCompression.Lossy });
}
```
<!-- endref -->

| Feature | Encoding |
| --- | --- |
| Compression | `HeicCompression.Lossless` (the default) codes the R'G'B' samples without color conversion (identity matrix, 4:4:4, full range), and gray pixel formats as a monochrome image, with the transform and the quantization bypassed: every coded sample is decoded back exactly. `HeicCompression.Lossy` codes Y'CbCr samples; `Quality` (0 to 100, 75 by default) selects the quantizer and `ChromaSubsampling` the chroma layout: 4:2:0 (the default), 4:2:2 or 4:4:4. The quantization parameter of HEVC is linear in the quality (51 at 0, 26 at 50, 13 at 75, 0 at 100), which is not the scale of the AVIF encoder: at the default quality, a photograph is closer to its source and several times larger than the AVIF file of the same quality number (the synthetic photograph of the `heic-quality` command of the benchmarks, which measures sizes, qualities and times, takes 0.4 bit per pixel at quality 50 and 2.7 at quality 75, where the AVIF encoder takes 0.2 and 0.4). Lossless files are large: select the lossy coding for photographs |
| Profiles and readers | The stream declares the narrowest profile that holds it, and the brand of the file follows the profile of the color images. Lossy 4:2:0 output with 8 bits is Main Still Picture (Main for a sequence) in a `heic` file (`hevc` for a sequence), and with 10 bits Main 10 in a `heix` file: the output that the readers of phones, operating systems and browsers open. Everything else is a profile of the range extensions in a `heix` (`hevx`) file: lossless output, 4:4:4 and 4:2:2, 12 bits, gray images. libheif, FFmpeg and Apple ImageIO open them at 8 and 10 bits (the interoperability tests decode every combination with them); Apple ImageIO does not open 12-bit 4:2:2 or 4:4:4 images, and many other readers open none of them |
| Bit depth | 8-bit pixel formats are written with 8 bits, or with `BitDepth` 10 or 12 (scaled up exactly). HEIC stores at most 12 bits: `Rgba64` and `Gray16` need `BitDepth`, or `AllowBitDepthReduction` (12 bits), and are rounded to nearest. The samples of a 10-bit or 12-bit image, which load as 16-bit pixels, are coded back unchanged at their depth |
| Color conversion | Lossy output keeps the matrix coefficients and the range of `Metadata.Cicp` when they are ones that the decoder converts, and uses BT.601 in full range otherwise, as for AVIF. The video usability information of the stream always states the range, the color description and the position of the chroma samples. A chroma sample of 4:2:0 or 4:2:2 planes is at the position of the luma sample of an even column, where HEVC readers expect it: the chroma of that column counts twice and the one of each neighbor once (and the two rows of 4:2:0 samples are averaged) |
| Sizes | The pictures of a stream with subsampled chroma have an even number of columns and of rows that are subsampled. An image of an odd size is coded with one more column or row, which repeats the last one, and the file states the size of the image: a `clap` property for a still image, a `clap` box in the sample entry of a sequence |
| Alpha | An auxiliary image at the bit depth of the color image, monochrome and always lossless. A still image whose pixels are all opaque has none. `ClearTransparentColors` replaces the color of the fully transparent pixels with black, which makes the file smaller |
| Effort | `Effort` (0 to 9, 5 by default) widens the search: coding blocks from 32x32 down to 8x8 (64x64 for the predicted images of a sequence), 4x4 prediction blocks from effort 4, the number of intra modes that are tried at their real cost (all 35 at effort 9), the depth of the transform tree, sign data hiding (from effort 4), transform skip (from effort 6), the sample adaptive offset (edge offsets from effort 3, band offsets from effort 5), and for the images of a sequence the reach of the motion search, its refinement to a half then a quarter of a sample, the number of merge candidates, rectangular and asymmetric partitions and temporal motion vector prediction. It never changes the decoded pixels of lossless output. Encoding is single-threaded and far slower than a native encoder: of the order of a megapixel per second at effort 4 for a lossy photograph on a recent laptop, ten times more at effort 0 and ten times less at effort 9 |
| Animations | An image with several frames is written as an image sequence (`hevc` or `hevx` brand): every frame is a full image, with its exact duration and the play count of the animation, as for AVIF. A pixel format with alpha has an alpha track. The first image is also the primary item of the file, for the readers that do not play sequences. The destination must be seekable and the frame count known |
| Inter prediction | The first image of a sequence is a key frame (an IDR picture). Each other image is a P picture predicted from the image that precedes it: a block copies the samples that a motion vector points to (to a quarter of a sample), merges with the motion of a neighbor, codes what differs, or is intra coded where that costs less. `KeyFrameInterval` (0 by default: no other key frame) is the number of images from a key frame to the next one; 1 makes every image a key frame. The images are stored in the order they are shown. Lossless output stays lossless |
| Metadata | A `colr` box of type `nclx` is always written; the ICC profile, EXIF and XMP are stored; the orientation of a still image is stored as `irot` and `imir` properties (the pixels are written as they are). An image sequence stores no orientation |
| Grid | `GridTileSize` (not set by default) cuts a still image that is larger than a tile into tiles of that size, each one an HEVC still picture of its own, which a `grid` item assembles: the layout of the photos of cameras (tiles of 512x512). The alpha plane is a grid of the same tiles; an image that one tile holds is coded whole, and an image sequence is never a grid. Each side of a tile is between 64 and 65,528 pixels, a grid has at most 256 columns and 256 rows, and with subsampled chroma the widths of the tiles and of the image are even (their heights too for 4:2:0), so that the conformance window of each picture crops its coding blocks to the tile. The tiles of the right column and of the bottom row extend past the image, whose last column and last row they repeat. A tile is coded without its neighbors: lossy output can show the edges of the tiles and is larger than the image coded whole; lossless output is exact. With `ImageConfiguration.MaxDegreeOfParallelism` above 1 (the configuration of the saved image), the tiles of a grid of at least 65,536 samples are coded by up to that many workers, each with encoders of its own charged to `MaxLiveAllocationBytes`, and the file is byte-identical to the one of one worker. With 8 workers on an Apple M5 Pro, a 3840x2160 photo in tiles of 512x512 is coded about 6 times faster in lossy mode (effort 0: 0.53 s to 0.09 s; effort 5: 7.2 s to 1.3 s) and about 6.5 times faster losslessly (effort 0: 1.56 s to 0.23 s; effort 5: 8.8 s to 1.34 s). The tiles cost little on that photo: the files are 0.8% larger in lossy mode (default quality, same PSNR) and 0.2% to 0.3% larger losslessly, and one worker takes 1% to 14% longer than for the image coded whole (lossy effort 5: 6.9 s) |
| Memory | A still image is coded in memory before the file is written, so it goes to any stream. An image sequence keeps 24 bytes per frame; its coded frames are written as they are encoded |
| Limits | At most 65,528 pixels per side and 4 GiB for a still image. The level of the stream is the lowest one whose picture size holds the image, and level 8.5 (no limit) for a picture that is larger than the ones of level 6.2 |

The encoder does not use every coding tool of HEVC: see [Limitations](#limitations). HEVC is covered by patents in many
countries: check the licensing terms that apply to what you distribute.

### JPEG XL

JPEG XL files are decoded and encoded, lossy or lossless; the decoder and the encoder are fully managed. The lossless
path is exact: a lossless file decodes to the samples that were encoded, and the files the encoder writes decode to the
same samples in libjxl and jxl-oxide. The lossy path (VarDCT) is defined in floating point: the decoded samples are
within a few sixteen-bit units of what those decoders give (they differ from each other by as much), not bit-exact.

Interoperability does not imply encoder parity with libjxl: the managed encoder makes its own coding decisions.
There is no general guarantee of comparable file size, encoding speed or perceptual quality, and equal quality/effort
numbers do not establish a matched comparison. Measure representative images at matched decoded quality for lossy output.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveJpegxl;format=md-fenced-code-block;dedent=true -->
```csharp
public static string SaveJpegXl(string inputPath, string jxlPath, string lossyPath, string animatedPath, string animatedJxlPath)
{
    using (var image = Image.Load(inputPath))
    {
        // Lossless by default: every pixel format is stored as it is (no bit depth reduction, no flattening, and the
        // colors of fully transparent pixels are kept). The effort changes the size and the encoding time, never the pixels.
        image.Save(jxlPath, new JpegXlEncoder { Effort = 7 });

        // Lossy on request: the quality sets the quantization, and the alpha channel stays exact. A progressive file
        // can be shown before it is complete.
        image.Save(lossyPath, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 90, Progressive = true });
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
<!-- endref -->

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Files | A bare codestream, or the container: `jxlc` or `jxlp` boxes (a codestream split anywhere), `Exif`, `xml ` and their Brotli-compressed form (`brob`), boxes of 64-bit size or that extend to the end of the file | A bare codestream; the container when the image has EXIF or XMP, or needs level 10 of the format (16-bit samples, more than 262,144 pixels per side or 268,435,456 pixels, an ICC profile above 4 MiB) |
| Coding | VarDCT (every transform, quantization table and coefficient order, chroma from luma, adaptive LF smoothing, LF frames, progressive passes) and Modular (every predictor, MA trees, RCT, palette and delta palette, squeeze), Gaborish and edge-preserving filters, upsampling by 2, 4 and 8, patches, splines, noise, groups in any order | `Compression`: `Lossless` (the default; Modular, groups of 256x256 samples, a reversible color transform and a predictor per image) or `Lossy` (VarDCT in the XYB color space: DCTs of 8x8 to 64x64 samples chosen for each area, the default quantization tables scaled by `Quality` and, from the effort 3, by a factor per block that follows the content (finer where the image is flat and next to an edge, coarser in texture), chroma from luma per tile, the Gabor-like filter up to quality 90 and the edge-preserving filter for coarse quantization) |
| Quality | n/a | `Quality` 0 to 100 (default 75) for lossy output: 90 is a quality at which the difference is barely visible, 100 is the finest quantization, still lossy. Ignored by lossless output |
| Pixel formats | 8-bit files load as `Gray8`, `Rgb24` or `Rgba32`; deeper files as `Gray16`, `Rgb48` or `Rgba64`; grayscale with alpha as `La16`/`La32`. Depths other than 8 and 16 are scaled exactly; `ImageInfo.BitsPerComponent` reports the depth of the file | `Gray8`, `Gray16`, `Rgb24`, `Rgba32`, `Bgra32` and `Rgba64`, each at its own depth: nothing is reduced. The alpha channel is lossless in lossy output too |
| Color | Samples stay in the color space the file declares; an XYB (lossy) image is converted to it. sRGB is untagged; linear sRGB sets `TransferFunction.Linear`; the other enumerated encodings with H.273 code points (Display P3, DCI-P3, BT.2020, BT.709, PQ, HLG) set `Metadata.Cicp`; custom primaries, a custom white point or a power function are described by an ICC profile written for them; an embedded ICC profile is kept byte for byte. HDR samples are returned as stored, never tone mapped | The ICC profile, byte for byte; otherwise `TransferFunction.Linear` and a `Metadata.Cicp` the format can enumerate as its color encoding; otherwise sRGB. Lossy output converts the samples to XYB, which a decoder converts back: it needs an enumerated encoding (sRGB, linear, BT.709, DCI, with sRGB, Display P3 or BT.2020 primaries) or an sRGB profile |
| Progressive | `ImageReader.ReadFrameRefinementInto` renders a frame from the sections received so far: the LF image (one value per 8x8 block), then each complete pass, then the frame. A Modular frame is rendered early when it is squeezed. Every rendering has the size of the frame | `Progressive`: lossy output in three passes (the lowest quarter of the frequencies, the lowest half, the rest), lossless output with the squeeze transform (each resolution in the sections of its pass). The decoded pixels do not change; the file is larger |
| JPEG recompression | A JPEG file recompressed by `cjxl` or by `JpegXlTranscoder` is decoded to pixels: YCbCr or RGB, subsampled chroma, the quantization tables of the file. `JpegXlTranscoder.ReconstructJpeg` rebuilds the JPEG file instead | `JpegXlTranscoder.RecompressJpeg` (not `Image.Save`: an image holds pixels) |
| Animations | Exact durations (ticks over the tick rate), play count; frames smaller or larger than the image, the blend modes (replace, add, blend, alpha-weighted add, multiply) and the four reference frames are composed. A frame without duration is a layer of the next one, not a frame of the animation | Full frames, 1000 ticks per second (`DurationRounding`), play count; a frame without duration is rejected |
| Orientation | The orientation of the image header is reported as `Metadata.Orientation`, never applied; the EXIF orientation tag is ignored, as the format requires | Stored in the image header |
| Parallelism | With `ImageConfiguration.MaxDegreeOfParallelism` above 1, the groups of a frame of at least 65,536 pixels (256x256 pixels each) are decoded by up to that many workers, and the restoration filters and the color conversion of a frame run by row bands; the pixels and the errors are those of one worker. With 8 workers instead of one, a 3840x2160 lossy file decodes about 3.7 times faster and a lossless one about 5.5 times, on an Apple M5 Pro | The groups of a frame of at least 65,536 pixels are encoded by up to that many workers (lossy and lossless output, and `JpegXlTranscoder`); the bytes are those of one worker: with 8 workers instead of one, a 3840x2160 photo encodes about 4 times faster lossy (effort 5) and 6 times lossless, on an Apple M5 Pro |
| Effort | n/a | `Effort` 0 to 9 (default 5). Lossless: 0 to 2 use a fixed color transform and predictor, 3 and above search them, 7 and above also try the weighted predictor; at every effort the samples are written with prefix codes or with ANS entropy coding, whichever is smaller; the pixels never depend on it. Lossy: 0 to 2 only use the 8x8 DCT and one quantization factor for the frame; 3 and above give each block its own quantization factor, choose between the 8x8 and the 16x16 DCT for each area, fit the chroma from luma factors of each tile and search how the alpha channel is predicted; 6 and above give the contexts of the coefficients more distributions; 7 and above also try the 16x8 and 8x16 DCTs; 8 and above the DCTs of up to 64x64 samples. It changes decisions, not the file size that `Quality` aims at |
| Limits | Every frame is checked against the frame limits, shown or not; the pixels that are decoded without being shown (reference frames, layers, LF frames, the part of a frame outside the image, intermediate renderings) count toward `MaxTotalPixels`; decoded planes are charged to the memory budget | At most 1,073,741,824 pixels per side. Lossy output keeps 12 bytes per pixel of the frame it compresses, progressive lossless output 4 bytes per sample |

`ImageReader` decodes an animation frame by frame; its memory does not depend on the number of frames. A frame is
complete once all its bytes are available (the encoded size of a frame is buffered, not the file), and
`ReadFrameRefinementInto` renders it before that:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ProgressiveRead;format=md-fenced-code-block;dedent=true -->
```csharp
public static int ShowWhileReading(Stream input, Action<Image<Rgba32>, FrameRefinement> show)
{
    using var reader = Image.OpenReader<Rgba32>(input);
    using var image = new Image<Rgba32>(reader.Info.Width, reader.Info.Height);
    var renderings = 0;

    // Each call writes a full image: coarse at first (Downsampling 8, then 4, 2), then the frame itself (IsFinal).
    // A file that is not progressive, or another format, gives its frame at once. An input that ends early gives the
    // renderings it has, then throws: truncation is never a clean end.
    while (reader.ReadFrameRefinementInto(image) is { } refinement)
    {
        show(image, refinement);
        renderings++;
        if (refinement.IsFinal)
            break;
    }

    return renderings;
}
```
<!-- endref -->

A JPEG file can be stored in a JPEG XL file without decoding it: the frame holds its quantized coefficients, coded more
efficiently, and the container holds what rebuilds the file. Nothing is lost, in either direction:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TranscodeJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool RecompressJpeg(string jpegPath, string jxlPath, string rebuiltPath)
{
    // No pixel is decoded: the JPEG XL file holds the coefficients of the JPEG file. A file that cannot be stored
    // exactly (four components, a sampling factor of 4, arithmetic coding) throws; nothing is approximated.
    JpegXlTranscoder.RecompressJpeg(jpegPath, jxlPath);

    // The file is an image like any other...
    using (var image = Image.Load(jxlPath))
    {
        _ = image.PixelFormat;
    }

    // ...and gives the JPEG file back, byte for byte. A JPEG XL file made from pixels returns false.
    return JpegXlTranscoder.TryReconstructJpeg(jxlPath, rebuiltPath)
        && File.ReadAllBytes(jpegPath).AsSpan().SequenceEqual(File.ReadAllBytes(rebuiltPath));
}
```
<!-- endref -->

`JpegXlTranscoder` also rebuilds the files `cjxl --lossless_jpeg=1` writes, and libjxl and jxl-oxide rebuild the files it
writes. `HasJpegReconstructionData` tells whether a file holds a JPEG file, without decoding it.

### JPEG 2000

JPEG 2000 files and codestreams are decoded and encoded, without loss or with loss. The codec is fully managed and
implements Rec. ITU-T T.800 | ISO/IEC 15444-1; the decoder also reads the high-throughput block coder of Rec. ITU-T
T.814 | ISO/IEC 15444-15 (HTJ2K). The reversible path (the 5-3 wavelet transformation) is exact: a lossless file decodes
to the samples that were encoded, and the files the encoder writes decode to the same samples in OpenJPEG and FFmpeg.
The irreversible path (the 9-7 wavelet transformation) is defined on real numbers: the decoded samples are within one
8-bit level of what those decoders give (they differ from each other by as much), not bit-exact.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveJpeg2000;format=md-fenced-code-block;dedent=true -->
```csharp
public static string SaveJpeg2000(string inputPath, string jp2Path, string codestreamPath)
{
    using var image = Image.Load(inputPath);

    // Lossless by default: every pixel format is stored as it is, 16-bit samples and the colors of fully
    // transparent pixels included. A JP2 file stores the ICC profile, the resolution, EXIF and XMP.
    image.Save(jp2Path);

    // Lossy: the quality sets the quantization, on the scale of the JPEG encoder; the opacity stays exact.
    // A codestream alone (.j2k, .j2c, .jpc) stores no metadata: a strict save rejects an image that has some.
    image.Save(codestreamPath, new Jpeg2000Encoder
    {
        Compression = Jpeg2000Compression.Lossy,
        Quality = 80,
        Container = Jpeg2000Container.Codestream,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });

    // Files, codestreams and the high-throughput block coder (HTJ2K) are decoded, whatever wrote them.
    var info = Image.Identify(jp2Path);
    return $"{info.Format} {info.Size.Width}x{info.Size.Height} {info.PixelFormat}, {info.BitsPerComponent} bits";
}
```
<!-- endref -->

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Files | A bare codestream (`.j2k`, `.j2c`, `.jpc`, `.jhc`), or a file of the JP2 family whose brand or a compatible brand is `jp2 `, `jpx ` or `jph ` and that holds one codestream; boxes of 64-bit length, a last box that extends to the end of the file. The boxes that follow the codestream are read (metadata) and are not needed: a file that ends after its codestream is complete | A JP2 file (`Container = Jp2`, the default) or a codestream (`Container = Codestream`) |
| Coding | The reversible 5-3 and irreversible 9-7 wavelet transformations, 0 to 32 decomposition levels; the reversible and irreversible component transformations; quantization without step, with derived and with expounded steps; tiles and tile-parts in any legal order, image and tile grid offsets; the five progression orders and progression order changes (POC); layers; precincts; code-blocks of 4 to 1024 samples per side; every style of the coding passes (selective bypass, reset, termination on each pass, vertically causal contexts, predictable termination, segmentation symbols); regions of interest (RGN); SOP and EPH markers; packet headers in the main header (PPM) or in tile-part headers (PPT); PLT, TLM and COM marker segments (not used); coding styles and quantizations per component (COC, QCC) and per tile. The coding passes that a codestream leaves out (a rate, a layer that was cut) are not an error: the missing bits of a coefficient are replaced by the middle of their range | Lossless: the 5-3 transformation and the reversible component transformation, without quantization. Lossy: the 9-7 transformation, the irreversible component transformation and scalar quantization; the opacity is always coded without loss. One tile, one layer, the LRCP progression, code-blocks of 64x64, up to five decomposition levels, no rate control |
| HTJ2K | Code-blocks of the high-throughput block coder: the HT cleanup, HT SigProp and HT MagRef passes, placeholder passes, the vertically causal contexts; in a codestream or in a JPH file | Not written |
| Pixel formats | Files of at most 8 bits load as `Gray8`, `Rgb24` or `Rgba32`; deeper files as `Gray16`, `Rgb48` or `Rgba64`; grayscale with opacity as `La16`/`La32`. Depths other than 8 and 16 are scaled exactly; `ImageInfo.BitsPerComponent` reports the depth of the file (of its palette for a paletted file) | `Gray8`, `Gray16`, `Rgb24`, `Rgba32`, `Bgra32` and `Rgba64`, each at its own depth: nothing is reduced, the colors of fully transparent pixels are kept |
| Channels | The channel definition box orders the colors and names the opacity; without one, the components are the colors in order and one more component is the opacity. A palette (with or without an opacity column) is expanded. A codestream alone has one to four components: gray, gray and opacity, RGB, RGB and opacity | Gray, gray and opacity, RGB, or RGB and opacity; a channel definition box names the opacity |
| Color | sRGB and grayscale as they are; sYCC components (sub-sampled chrominances included, each used for the pixels it covers) are converted to RGB, exactly rounded; a codestream of three components whose last two are sub-sampled alike is read as sYCC; an ICC profile, restricted or not, is kept byte for byte and never applied. CMYK inks (the enumerated color space, or a CMYK ICC profile) are identified, and loaded only with `CmykConversion` | sRGB or grayscale, or the ICC profile when it is one a JP2 file allows: a grayscale profile or a matrix-based RGB profile. Another profile is metadata the format cannot store (`MetadataHandling`) |
| Metadata | The capture resolution box (the default display resolution box when there is none), and the EXIF profile (its orientation is reported, never applied) and the XMP packet of UUID boxes, before or after the codestream | In a JP2 file: the resolution, EXIF (with the orientation) and XMP. A codestream stores none: any metadata follows `MetadataHandling` |
| Quality | n/a | `Quality` 1 to 100 (default 90), for `Compression = Lossy`: it sets the quantization steps of the sub-bands, and it is calibrated on `JpegEncoder.Quality`: on photographs, the same value gives the structural similarity (SSIM) of the JPEG file, in about two thirds of its size. The two chrominances are quantized twice as coarsely as the luminance. It is not a rate: the size follows from the image |
| TIFF | Strips and tiles compressed with JPEG 2000 (compression 34712), as a codestream or as a JP2 file | Not written |
| Limits | The size and the depth are checked on the main header; a tile is decoded as a whole: its coded bytes, its coefficients (four bytes per sample) and the decoded image are charged to the memory budget, so that a very large image of one tile can exceed the default 512 MiB | At most 16 bits per sample; one frame |

`Image.Identify` reads the main header of the codestream: its `Header` mode stops at the first tile-part, and `FullScan`
follows the chain of the tile-parts to the end of the codestream without decoding any packet. A codestream ends with
its EOC marker: a file that is cut is `InvalidImageContentException`, never a shorter image.

### OpenEXR

OpenEXR files hold scene-linear, high dynamic range images of 16-bit (half) or 32-bit floating-point samples, as used in
visual effects and rendering. The codec is fully managed. Its decoder reads the files OpenEXR 3.5 writes bit for bit,
lossy compressions included: what PXR24, B44 and B44A lost was lost when the file was written, and decoding them is
exact for the stored samples. Its encoder uses lossless compression, but the pixel-to-sample conversion is not a
general bit-exact round-trip guarantee: OpenEXR stores premultiplied colors, whereas the library holds straight colors.
The encoder multiplies the colors by the alpha it stores (rounded to the sample type) and the decoder divides them by
it; rounding and underflow can change them, except for `RgbaHalf` pixels written as float samples (the product of two
halves is exact in a float), which come back exactly. A stored alpha of 0 keeps its colors without multiplication or division.
A finite color that the multiplication takes beyond the range of the sample type (±65,504 for half samples), or that an
alpha that is not finite would make infinite or not a number, is rejected with `UnsupportedImageFeatureException`
before anything is written: alpha is not limited to [0, 1], so a half color of 60,000 with an alpha of 2 is rejected.
Integer pixels never are: their colors and alpha are within [0, 1]. Integer normalization and half-sample rounding are
separate from compression.

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExr;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgba32> ConvertOpenExr(string exrPath, string pizPath)
{
    // Half-only files without alpha load as RgbaHalf; other OpenEXR files load as RgbaFloat, in linear light
    using var hdr = Image.Load(exrPath);

    // PIZ compresses the stored samples losslessly, values above 1 included; alpha conversion can round colors
    hdr.Save(pizPath, new OpenExrEncoder { Compression = OpenExrCompression.Piz });

    // Integer pixels cannot hold the highlights: a typed load compresses them with a tone mapping
    return Image.Load<Rgba32>(exrPath, new ImageDecodeOptions
    {
        Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() },
    });
}
```
<!-- endref -->

| Feature | Decoding | Encoding |
| --- | --- | --- |
| Files | Single-part files (version 2) of scan lines or tiles; the chunks may be in any order in the file (decreasing lines, random tiles) and are read forward only. A tiled file with mipmap or ripmap levels gives its full-resolution level; the other levels are walked and skipped | A single-part file of scan lines in increasing order, with the attributes the specification requires |
| Compression | NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44 and B44A; a chunk stored uncompressed whatever the compression of the file | NONE, RLE, ZIPS, ZIP (the default) or PIZ, all lossless; a chunk that does not shrink is stored uncompressed |
| Samples | HALF, FLOAT and UINT channels: an image of HALF channels without an alpha channel loads as `RgbaHalf`, any other as `RgbaFloat` (the extra precision is used when dividing colors by alpha; a UINT sample must have an exact float value: up to 2^24, or a larger value with at most 24 significant bits). `ImageInfo.BitsPerComponent` is 16 or 32. Infinities and samples that are not numbers are kept, subject to the alpha conversion | By default, `RgbaHalf` and 8-bit pixels as HALF samples, `RgbaFloat` and 16-bit pixels as FLOAT samples; colors are premultiplied by the stored alpha when it is not 0 and rounded to the sample type, so lossless compression does not guarantee exact source pixels; a finite color whose product is beyond the range of the sample type is rejected. `PixelType` chooses the type, and half samples of wider pixels need `AllowBitDepthReduction` (round to nearest, ties to even; beyond ±65,504 rejected) |
| Channels | `R`, `G`, `B` and `A`, any of them (a missing color is 0, a missing alpha is 1), or `Y` (luminance, with or without `A`), which gives the three colors; the channels are not sub-sampled | `R`, `G`, `B`, and `A` for the pixel formats with alpha; `Y` for gray pixel formats |
| Windows | The display window is the image; the data window is placed in it, and the pixels it does not cover are transparent black | Both windows are the image, from (0, 0) |
| Color | Linear light: the `chromaticities` attribute (BT.709 and D65 without it) gives `ColorTransferFunction.Linear` for BT.709, a CICP description with the linear transfer characteristics (8) for BT.2020, DCI-P3 and Display P3, and an ICC profile with linear curves for the others. Never applied | A `chromaticities` attribute for a linear CICP description of BT.2020, DCI-P3 or Display P3. The samples must be linear light: an image labeled sRGB (the default of integer pixels), with an ICC profile or another CICP description follows `MetadataHandling` |
| Metadata | The other attributes are skipped, but counted against `MaxMetadataBytes` | None other than the chromaticities |
| Limits | The header is validated before the offset table: its attributes count as metadata, the display window against the size limits; the offset table and the largest chunk are charged to the memory budget | A seekable destination when the chunks are compressed (the offset table precedes them) |

`Image.Identify` reads the header: its `Header` mode stops before the offset table, and `FullScan` walks every chunk (its
coordinates and size) without decompressing it. Every chunk is read, so a file that is cut is
`InvalidImageContentException`; a chunk that is not at the offset the table gives, that carries the coordinates of
another chunk, or that overlaps the next one is an error too. A typed load into an integer pixel format converts the
whole image like `CloneAs` does: samples outside [0, 1] are rejected unless the conversion clips or tone maps them, and
the peak of a tone mapping is the one of the image.

## Limitations

Everything below is rejected explicitly (never decoded or encoded approximately) or documented as lossy.

| Area | Behavior |
| --- | --- |
| JPEG decoding | Lossless with arithmetic coding (SOF11), hierarchical (SOF5–7, SOF13–15, DHP, EXP), JPEG-LS, sample precisions other than 8 and 12 bits in a frame that is not lossless, heights defined by DNL, other component counts than 1, 3 or 4, non-integral sampling ratios: `UnsupportedImageFeatureException` (also from `Identify`). CMYK/YCCK (four components): identified, but loaded only with `CmykConversion` (an ICC conversion to RGB; `UnsupportedImageFeatureException` otherwise, and for a file without CMYK profile when no profile is assumed). Scaled and region decoding: not available |
| JPEG encoding | Baseline or progressive 8-bit Huffman only: no arithmetic, lossless, 12-bit, RGB, CMYK or restart markers; the progression of the progressive process is fixed, and the baseline process has no optimized Huffman tables. One frame: animated images and posters are rejected (save one with `Frames[i].Save`); alpha requires `BackgroundColor`; `Rgba64`/`Gray16` require `AllowBitDepthReduction` |
| GIF decoding | Plain-text extensions (they require text rendering) are `UnsupportedImageFeatureException`; other unknown extensions (XMP and ICC application extensions included) are skipped; the background color index is never painted (cleared areas are transparent black) |
| GIF encoding | Lossy by design: at most 256 colors per frame (deterministic quantization, optional dithering), one transparent index (alpha thresholded or flattened, never partial); a local color table per frame, also with a shared palette (no global color table); full-canvas frames (no delta rectangles); comments only (no ICC, EXIF, XMP or resolution); at most 65,535 pixels per side, 655.35 s per frame and 65,536 plays |
| WebP decoding | VP8 interframes and frames that are not shown (never produced by WebP encoders): `UnsupportedImageFeatureException`; the `ANIM` background color is a hint and is never painted (disposed areas are transparent black); metadata of simple-layout files and text/resolution do not exist in WebP |
| WebP encoding | 8-bit RGB(A) only (gray written as RGB, 16-bit after explicit reduction); lossy output is 4:2:0 and not pixel-exact; animations are full-canvas frames (no delta rectangles or blending) and need a seekable destination (no spooling to non-seekable streams); at most 16,383 (lossy) or 16,384 (lossless) pixels per side, 16,777,215 ms per frame, 65,535 plays and 4 GiB per file |
| QOI | 8-bit RGB(A) only (gray written as RGB, 16-bit after explicit reduction); one still image; no metadata; the colorspace field is a label (no linear-to-sRGB conversion exists) |
| BMP | Decoding: no embedded JPEG/PNG payloads, no OS/2 Huffman 1D, no 2-bit depth, no channel wider than 8 bits, no embedded ICC profile; OS/2 bitmap arrays (`BA`) are not recognized; a 40-byte header is always a Windows header (an OS/2 2.x header of that length with Huffman 1D or RLE24 is read as `BI_BITFIELDS` or `BI_JPEG`); run-length streams are read strictly (nothing is clamped, the end-of-bitmap marker is required). Encoding: one still image; Windows headers only; run-length output for 4-bit and 8-bit indexed layouts only, bottom-up; indexed output needs one byte per pixel of working memory; no alpha below 32 bits (no 1-5-5-5 output); resolution is the only metadata; the file must fit the 32-bit `bfSize` field |
| TGA | Decoding: 16-bit grayscale is always an 8-bit gray sample and an attribute byte, so a file that stores a 16-bit luminance is not read as one; premultiplied alpha (attributes type 4) is rejected and no other attributes type changes the decoded representation (an alpha channel the extension area calls absent or undefined is still decoded when the image descriptor declares it); the other fields of the extension area and the developer area are not interpreted; the input is read to its end and the bytes after the image data are buffered; truncation is only detected before the end of the image data. Encoding: true-color and grayscale only, bottom-up, one still image, at most 65,535 pixels per side; no color map, no 15/16-bit output, no metadata |
| Netpbm | Decoding: only the first image of a concatenation is read; tuple types other than the standard grayscale, RGB and alpha forms are rejected; the header is bounded at 65,536 bytes; truncation of a plain raster is not detectable. Encoding: one image, no metadata; the variant follows the pixel format, and a portable bitmap (`P1`/`P4`) is only written on request, from pixels that are exactly black or white (no threshold, no dithering); plain output and portable bitmaps have no alpha (PAM has no plain form) |
| TIFF | Decoding: old-style JPEG compression (6), L\*a\*b\* and transparency-mask photometric interpretations, planar storage of several samples, the reversed fill order for other data than bilevel, sample widths other than 1, 2, 4, 8 and 16 bits (8 and 16 for RGB and CMYK, 8 for YCbCr, at most 8 for palette indexes; with JPEG compression, any width from 8 to 16 bits; signed samples of 8 or 16 bits and floating-point samples of 16, 24 or 32 bits, gray and RGB only, without JPEG or JPEG 2000 compression; no 64-bit floating-point samples), JPEG compression of samples of less than 8 bits, more than one extra sample, associated (premultiplied) or unspecified extra samples, alpha next to palette, YCbCr or packed grayscale samples, the uncompressed mode of the CCITT compressions, the LZW bit order of TIFF 5.0 drafts, and JPEG-compressed YCbCr data that is not the full-range BT.601 data of JFIF, and JPEG 2000 compression of samples that are not 8 or 16 bits wide, of palette indexes, of YCbCr data or with a predictor are all rejected with `UnsupportedImageFeatureException`; CMYK pages are loaded only with `CmykConversion`. Compressed data is never repaired (a damaged fax row is an error, not a white row). Subsampled chroma of pages that are not JPEG-compressed is replicated. No EXIF block, no sub-IFD, no unknown-tag round trip. Encoding: one page per `Image.Save` (use `ImageCollection.Save`), strips only, no predictor but the floating-point one, no tiles, no CMYK, no signed samples, no half-precision or 24-bit floating-point samples (`RgbaHalf` pixels are written as 32-bit floats), no thresholding and no color reduction (the bilevel and palette layouts store a page exactly or reject it; 16-bit palette colors that are all multiples of 256 without being all multiples of 257 are rejected, since readers take them for 8-bit colors), Group 3 in one dimension only, one strip per JPEG page (at most 65,535 pixels per side, 8-bit samples, opaque pixels), a seekable destination required |
| ICO/CUR | Decoding: a top-down DIB payload, a DIB whose stored height is not doubled, an animated PNG payload, a representation larger than 256 pixels per side and the DIB variants an icon payload cannot use (run-length encoding, embedded codecs, OS/2 headers) are rejected. Encoding: 32-bit DIB or still PNG payloads only (16-bit pixels need a PNG payload: `IconPayloadFormat.Dib` is rejected for them), at most 256 pixels per side, no metadata; an icon cannot store a hotspot |
| ANI | Decoding: raw bitmap frames (no icon flag), a frame without a representation of the canvas size and everything the ICO/CUR decoder rejects are `UnsupportedImageFeatureException`; the geometry fields and the sequence flag of the header are not used (the payloads and the `seq ` chunk are authoritative); an ICC profile or text inside a PNG frame is not retained; `Image.OpenReader` does not read it. Encoding: one representation per frame, at most 256 pixels per side, whole jiffies, an infinite play count only, a title and an author as the only metadata, a seekable destination, at most 4 GiB per file |
| PNG/APNG | Default output follows the pixel format, including RGB 16-bit and gray+alpha 8/16-bit; grayscale below 8 bits and APNG delta rectangles require `PngOptimization.MinimumSize`. Palettes are selected by `PngPixelLayout.Indexed` or `MinimumSize`, exact unless `AllowColorReduction` (a sequential writer fixes its palette from the first image; later frames must fit it, or are mapped to it with `AllowColorReduction`). Unknown critical chunks are rejected, unknown ancillary chunks are not round-tripped; Apple CgBI files are decoded with the precision their premultiplied colors keep (lossy for low alpha values) and never written |
| Metadata | Profiles are preserved and labeled, never applied implicitly (colors are converted only by `ConvertColorProfile` and `IccColorTransform`); EXIF orientation is reported, applied only by `AutoOrient`; extended XMP in JPEG is not supported; per-format storage limits are listed in [Metadata, color profiles and orientation](#metadata-color-profiles-and-orientation); unsupported items follow `MetadataHandling` (`Strict` throws by default) |
| Color management | ICC version 2 and 4 grayscale, RGB and CMYK profiles (matrix-based and table-based): no version 5 (iccMAX), device-link, abstract or named-color profile, no multi-process-element (`D2Bx`/`B2Dx`) tag, no other data color space; `ConvertColorProfile` keeps the pixel format (an RGB image cannot become grayscale or CMYK this way); no CMYK pixel format: a CMYK JPEG file or TIFF page is converted to RGB when it is loaded (`CmykConversionOptions`, RGB destination only) and CMYK cannot be written; no gamut mapping beyond per-channel clipping |
| Drawing | Sequential; under a projective transformation of the canvas (or, for filters, one that turns or skews), shadows, blurs, filters and the glyphs a font draws with its own colors are drawn on an image of the plane at one resolution (the scale of the transformation at its origin, at most 4,194,304 pixels), curves are cut no finer than for a factor of 65536 near the horizon, and a pen that does not scale strokes the parts of a closed contour that crosses the horizon as open contours; a filter graph has no input `BackgroundImage`, `BackgroundAlpha`, `FillPaint` or `StrokePaint`, and its region defaults to the canvas (not to the box of the drawing); no mesh gradient of patches (a `PathGradientBrush` is a fan of triangles from one center); a marker has no hole, and a stroke on one side of its path costs a clip; linear light is the light of the transfer function the image declares (sRGB when it declares none) or of the tone curves of its ICC profile, and a profile without tone curves has none; a color matrix is applied to the encoded sample values, also in linear light; the colors of a profile (`PaintProfile`) are those of the paints only (an image that is drawn is not converted), an RGB profile only, and never on PQ or HLG samples; without antialiasing, the glyphs a font draws with its own colors stay antialiased; a clip multiplies coverages; combined and simplified paths are made of segments; coordinates are limited to ±16,777,216 (what a transformation sends beyond is cut by that range); on a floating-point canvas, paints have 16-bit colors in [0, 1], and an image, a layer, a pattern, a filter, a shadow, a blur, a group or a backdrop is combined through 16-bit colors, so its samples must be in [0, 1] (others are rejected, never clipped) and are quantized to 1/65535 |
| Floating-point pixels | `RgbaFloat` and `RgbaHalf` only (no floating-point grayscale format); OpenEXR and TIFF are the formats that decode to them and store them (the other encoders reduce them to `Rgba64` with `ImageEncoder.FloatingPointConversion`); HDR samples of the integer formats are 16-bit integers, converted to a standard dynamic range by an explicit tone mapping |
| Processing | No custom processors; `LinearSrgb` resizing and convolution accept untagged images, recognized sRGB/sGray profiles and CICP transfer characteristics that have a function; convolution matrices have odd dimensions and no divisor (divide the weights); edge detection has no non-maximum suppression or thresholds (no Canny detector); histograms, equalization and auto-levels work on the encoded samples of integer pixel formats only (floating-point pixels are rejected), and CLAHE does not pad a frame whose size is not a multiple of the tile grid (its tiles divide it as evenly as possible) |
| AVIF encoding | An image of a sequence is predicted from the previous image only, with one motion vector per block and the regular interpolation filter: no compound prediction (two references), global, warped or overlapped motion, or temporal motion vector prediction. Three tools were implemented, measured with the `avif-quality` report and left out because they did not make its sequences smaller: key frames at scene changes (the key frame of an image that follows a cut is 0.3 to 1.7% larger than the inter frame whose blocks are all intra, since it gives up the probabilities that the previous images adapted; `KeyFrameInterval` is how a sequence gets seek points), the split of the transform blocks of an inter block (0.01% in total: it keeps the largest transform of its size) and an interpolation filter per block (the same size at equal quality, for several times the encoding time). Intra block copy is for lossless images only (a frame that copies blocks has no in-loop filter), and its delay of five superblocks leaves small images out. The loop restoration is the Wiener filter, in units of 256x256 samples, at the efforts 0 to 2 only (it recovered nothing above them in the report), and never the self-guided filter. Not used: quantizer deltas, quantization matrices, CDEF strengths per block (one pair per frame), super-resolution and film grain. No layered (progressive) output, and no grid for an image sequence. An image sequence of one image cannot store animation settings (it is read back as a still image): a single frame with animation settings is rejected. An image sequence needs a seekable destination and its frame count. The level of the stream is the lowest one whose picture size holds the image; the other limits of a level are not checked |
| AVIF decoding | Rejected with `UnsupportedImageFeatureException`: more than eight nested derivations, an image built from images of several color descriptions, an overlay whose sRGB fill color shows on an image that is not an sRGB one, an alpha plane of an overlay or of a grid of derived images, a derived image with more than 1,024 input images, an image built from more than 16,384 images (an image counts once for each derivation that uses it, since it is decoded for each), an alpha plane that is not presented as its derived color image, movie fragments that do not follow each other, have composition time offsets, precede their `moov` box or follow their samples, an edit list that selects a part of a fragmented sequence, edit lists that do not play the media forward at its rate (empty edits, a media rate other than 1, edits out of order or overlapping), an alpha track with another edit list than its color track, image sequences whose images change size, clean apertures that do not select whole samples, grid tiles that declare other transformations than their grid, large scale tile streams, reserved matrix coefficients, and matrix coefficients without the transfer characteristics or the primaries they are defined with (constant luminance without a transfer function, ICtCp with other transfer characteristics than PQ and HLG, chromaticity-derived coefficients without chromaticities). Not read: gain maps, depth maps, thumbnails, the images that are not the primary item, a `tkhd` matrix that is not a quarter turn or a mirror (the images are returned as stored), the content color volume (`cclv`) and the light levels and the mastering display of AV1 metadata units. A layered image is returned at its full quality only: its lower layers are not exposed as a progressive rendering. HDR samples are returned as stored: tone mapping is an explicit `ConvertColorProfile` with `ToneMapping` options |
| HEIC encoding | One slice per picture, without tiles or wavefront streams: a picture is coded by one worker, and only the pictures of the tiles of a grid (`GridTileSize`) are coded concurrently. An image of a sequence is a P picture predicted from the previous image only: no B pictures, no second reference, no bi-prediction, no weighted prediction, no long-term reference, no scene change detection. Not used: intra coding blocks of 64x64 samples, PCM blocks, scaling lists, quantizer changes inside a picture (no adaptive quantization, no rate control: `Quality` is a quantizer, not a size), rate-distortion optimized quantization, searched deblocking offsets, and the tools of the range extensions (lossless output is the plain transform bypass of version 1, which every HEVC decoder reads: residual DPCM and cross-component prediction would make it smaller). No screen content tools: a lossless screenshot is larger than its PNG. An image is one picture unless `GridTileSize` asks for a grid. No premultiplied alpha, no `pasp`, mastering display or content light level box, no orientation for an image sequence, and the decoded picture hash is not written. An image of an odd size with subsampled chroma relies on the clean aperture of the file: libheif ignores the one of a sequence and shows one more column or row, which repeats the last one, and FFmpeg reports the even size of the coded pictures (its command-line tool then crops them to an even size). The readers upsample subsampled chroma each in their own way: the edges of a 4:2:0 or 4:2:2 image differ visibly from one reader to another, as they do for the files of any HEVC encoder. FFmpeg 9 decodes 12-bit lossy output of the coarsest quantizers (`Quality` 0 to 2) to other pictures than libde265, the HM reference decoder and this library, which agree. libheif reports the edit list of a sequence that plays once as unsupported, and plays it once. An image sequence of one image cannot store animation settings. An image sequence needs a seekable destination and its frame count |
| HEIC decoding | The container limitations of AVIF decoding apply (the two formats share their container), and the container features that only AVIF reads so far are rejected: premultiplied alpha, derived images (`iden`, `iovl`, grids of derived images), fragmented files, edit lists that do not play the whole track, and the matrix coefficients that are not a linear matrix of the coded samples (SMPTE ST 2085, chromaticity-derived, constant luminance, ICtCp). The `clli`, `mdcv` and `pasp` boxes are not reported either (a `pasp` box is only validated). Rejected with `UnsupportedImageFeatureException`: the screen content coding, multilayer and 3D extensions of HEVC, bit depths above 12 bits, luma and chroma samples of different bit depths, separate color planes, field sequences, extended precision processing, the chroma sample positions of types 3 to 5, and an image sequence whose pictures change size. Only the base layer of a layered stream is decoded, and a layer selector (`lsel`) is rejected. Not read: the other images of a file (bursts, thumbnails, depth and gain maps, the video of a live photo, which is another file), the supplemental information of the bitstream other than its picture hashes (mastering display, content light level), and the `tkhd` matrix of an image sequence. A file whose bitstream has no video signal description and no `colr` box is read in limited range, as H.265 infers, with the BT.601 matrix: Apple ImageIO reads the same file with the BT.709 matrix, and libheif does not expand the samples of a monochrome image. The decoder is sequential: the tiles and the wavefront rows of a picture are decoded one after the other |
| JPEG XL | Decoding, rejected with `UnsupportedImageFeatureException` (also from `Identify` when the image header tells): floating-point samples and samples of more than 16 bits, premultiplied alpha, CMYK (a black channel) and spot colors, an XYB image whose ICC profile is not sRGB (it needs a color conversion to that profile), the PQ or HLG transfer function with primaries or a white point that CICP cannot express, a Modular frame stored as YCbCr with subsampled channels, restoration filters on a grayscale Modular frame, an upsampled frame with patches or splines whose extra channels have another resolution. Ignored: the preview frame, frame names, the intrinsic size, the JPEG reconstruction data (`jbrd`: `Image.Load` decodes a recompressed JPEG file to pixels, `JpegXlTranscoder` rebuilds the JPEG file), `jumb` and unknown boxes; depth, selection mask, thermal and the other extra channels than alpha are decoded and dropped. Samples of 1 to 16 bits other than 8 and 16 are scaled exactly to 8 or 16 bits. A lossy frame is decoded in floating point: the result is within a few sixteen-bit units of the reference decoders, not bit-exact. Progressive reading: whole stages only (the data of a section that is not complete are not shown), none for a frame of one section, for a Modular frame that is not squeezed, or for the frames that are not shown; an alpha channel that is not squeezed is opaque until it is decoded; the pixels of an intermediate rendering are not part of the contract. Encoding: full frames that replace the image (no layers, blending, patches or cropped frames), frame durations in whole milliseconds, no frame of an animation without duration; an EXIF orientation is written to the image header. Lossy encoding: DCTs of 8x8 to 64x64 samples only (no smaller transform, no identity or AFV transform), a quantization factor per block that is computed from the image once (the encoder does not decode its output to refine it), no patches, splines or noise; an ICC profile that is not sRGB, and the PQ and HLG transfer functions, are rejected (use lossless output, or convert the image) |
| JPEG recompression | `JpegXlTranscoder.RecompressJpeg` rejects with `UnsupportedImageFeatureException` what a JPEG XL frame cannot hold: four components (CMYK, YCCK), a sampling factor other than 1 or 2 (4:1:1), subsampled RGB components, arithmetic, lossless, hierarchical and 12-bit files, restart intervals of several lengths (the description of the file has one), a quantization table that no component uses unless it repeats the table defined before it, a Huffman table that uses the code of all ones, more than 65,535 bytes between two markers or 4,260,096 bytes after the end of image, and entropy-coded data that would not be rebuilt byte for byte (the output is always checked). The whole file is held in memory, with two bytes per coefficient. The reconstruction data (`jbrd`) are read wherever the container has them: every encoder writes them before the codestream; when they follow it, the frame is held as it is coded until they are read, and `HasJpegReconstructionData` reads the file to its end |
| JPEG 2000 | Decoding, rejected with `UnsupportedImageFeatureException` (also from `Identify` when the headers tell): signed samples and samples of more than 16 bits, components of different depths, a sub-sampled first component and sub-sampled components that are not the chrominances of an image of three components, more than four components without color specification, a premultiplied opacity, several opacity channels, an opacity of one color, channels that are not defined, the extensions of ISO/IEC 15444-2 (other wavelet filters, other component transformations, other regions of interest), files of the `jpm ` and `mjp2` brands, files of several codestreams or of a fragmented codestream, more than 30 bit-planes in a sub-band; for the high-throughput block coder: tile-components that mix both block coders, a code-block with several HT sets, and placeholder passes in another packet than the HT cleanup pass. CMYK is loaded only with `CmykConversion`. Not read: the intellectual property and XML boxes, the comments of the codestream. Not available: decoding a lower resolution, a region or fewer layers. Encoding: no rate control (a quality, not a size), one tile and one layer (the whole image is transformed at once, in memory), no HTJ2K, no palette, no sub-sampling, no CMYK, no animation; a codestream stores no metadata |
| OpenEXR | Decoding, rejected with `UnsupportedImageFeatureException` (also from `Identify`): multi-part files, deep data, the DWAA, DWAB, HTJ2K and ZSTD compressions, channels other than R, G, B, A and Y (layers, depth, `RY`/`BY` chroma, extra channels: they would be lost), sub-sampled channels, a data window that leaves the display window (its pixels would be lost), UINT samples that a float cannot hold exactly, and versions other than 2. Not decoded: the lower levels of mipmaps and ripmaps, the preview image and the attributes other than the windows and the chromaticities. Encoding: one still image, scan lines only (no tiles), half or float samples (no UINT), no lossy compression (PXR24, B44, DWA), no attributes other than the required ones and the chromaticities, linear light only
| Text | In vertical lines, no ruby and no compression of punctuation; a synthetic bold is not in the path of a layout nor in a stroked text; the pen of a style (`TextStyle.Outline`) strokes glyphs, not decorations, and has a width in pixels that a text fitted to a box does not scale; a text is fitted to a box by trying sizes, each with a whole layout; no word list is provided for the scripts that need a dictionary to break their lines; greedy line breaking; no AAT (`morx`, `kerx`) shaping, no device adjustment of positions; hinting is opt-in (`TextLayoutOptions.Hinting`), only for horizontal text, without the variations of the control values (`cvar`) and without the hints of CFF outlines; no subpixel (LCD) rendering; an installed font is never used by itself: it is loaded with `Font.LoadInstalled`, or used for the characters the fonts of a stack lack with `FontStack.WithInstalledFallback`, whose choice depends on the fonts of the machine; along a path, glyphs are turned but not warped; see [Text](#text) |
| Fonts | No font of the system unless one is asked for (`Font.LoadInstalled`, `FontStack.WithInstalledFallback`, `SystemFontCollection`), no SVG glyph, no font without outlines, no bitmap glyph of several bits per pixel without colors, no Type 1 font, no hints of CFF outlines; see [Fonts](#fonts) |
| API | No public allocator, custom pixel type, codec plug-in or image-processor interface; implementation types are internal |
