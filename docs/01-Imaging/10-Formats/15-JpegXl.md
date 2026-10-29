---
title: JPEG XL
description: Read and write JPEG XL images and animations, lossless or lossy, progressive files, and recompress JPEG files without loss.
---

JPEG XL stores lossless and lossy images and animations, up to 16 bits per sample. The library reads every JPEG XL file
that does not use the features listed in [Limitations](../12-Limitations.md), and `JpegXlEncoder` writes lossless files
by default. `JpegXlTranscoder` stores a JPEG file in a smaller JPEG XL file without any loss, and rebuilds it.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Coding | Lossy (VarDCT) and lossless (Modular) frames; recompressed JPEG files | Lossless (default) or lossy |
| Samples | Up to 16 bits, gray and color, alpha | Every pixel format at its own depth (`Gray8`, `Gray16`, `Rgb24`, `Rgba32`, `Bgra32`, `Rgba64`), lossless alpha |
| Animations | Composited into full frames with exact durations and the play count | Full frames, durations in milliseconds, play count |
| Progressive | Renderings of a partial file | Progressive files on request |
| Metadata | ICC profile, EXIF, XMP, enumerated color encodings, orientation | The same |

8-bit files load as `Gray8`, `Rgb24` or `Rgba32`; deeper files as `Gray16`, `Rgb48` or `Rgba64`. The orientation of the
image header is reported in `Metadata.Orientation`, never applied.

## Save a JPEG XL file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveJpegxl;format=md-fenced-code-block;dedent=true -->
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

Lossless output keeps every sample, 16-bit samples and the colors of fully transparent pixels included. Lossy output
keeps the alpha channel lossless. Unlike WebP, AVIF and HEIC, an animation does not need a seekable destination: frames
are written as they come, whatever their number.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| `Compression` | `Lossless` or `Lossy` | `Lossless` |
| `Quality` | 0 (smallest) to 100 (best) for lossy output: 90 is barely distinguishable from the source, 100 is still lossy | 75 |
| `Effort` | 0 (fastest, largest) to 9 (slowest, smallest): changes the decisions of the encoder, never lossless pixels nor the size `Quality` aims at | 5 |
| `Progressive` | Writes a file that a reader can show at a lower resolution before it is complete; the file is larger | `false` |
| `DurationRounding` | `RoundToNearest` rounds durations to the nearest millisecond; `RequireExact` throws | `RoundToNearest` |
| `MetadataHandling` | What to do with metadata JPEG XL cannot store (text, resolution) | `Strict` |

## Show an image while it downloads

`ReadFrameRefinementInto` renders a frame from the bytes received so far: coarse at first, then finer, then the frame
itself. A file that is not progressive, or another format, gives the frame at once:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ProgressiveRead;format=md-fenced-code-block;dedent=true -->
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

See [Show an image while it downloads](../05-LoadingAndSaving/06-ShowImagesWhileTheyDownload.md) for the other
formats and for partial downloads.

## Recompress JPEG files without loss

`JpegXlTranscoder.RecompressJpeg` stores the coefficients of a JPEG file in a JPEG XL file without decoding its pixels,
and `TryReconstructJpeg` gives the JPEG file back, byte for byte. This is not `Image.Save`, which stores pixels:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TranscodeJpeg;format=md-fenced-code-block;dedent=true -->
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

- `HasJpegReconstructionData` tells whether a JPEG XL file holds a JPEG file, without decoding it.
  `TryReconstructJpeg` returns `false` for a file made from pixels, and `ReconstructJpeg` throws for it.
- Files that a JPEG XL frame cannot hold exactly are rejected with an `UnsupportedImageFeatureException`: CMYK and YCCK
  files, 4:1:1 sampling, arithmetic-coded, lossless, hierarchical and 12-bit files, among others. Nothing is
  approximated.
- The recompressed files are read by other JPEG XL decoders, and the library rebuilds the files that
  `cjxl --lossless_jpeg=1` writes.

## Pitfalls

- A frame of an animation without duration is rejected: JPEG XL defines it as a layer of the next frame.
- Lossy output converts the samples to the XYB color space, which needs an enumerated color encoding (sRGB, linear,
  BT.709 or DCI, with sRGB, Display P3 or BT.2020 primaries) or an sRGB ICC profile. Another ICC profile, or the
  PQ and HLG transfer functions, throws an `UnsupportedImageFeatureException`: write those images losslessly.
- The decoder rejects floating-point samples, samples of more than 16 bits, premultiplied alpha and CMYK images.
