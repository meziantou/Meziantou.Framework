---
title: JPEG XL
description: Read and write JPEG XL images and animations, lossless or lossy, progressive files, and recompress JPEG files without loss.
---

JPEG XL stores lossless and lossy images and animations, with integer or floating-point samples. The library reads every JPEG XL file
that does not use the features listed in [Limitations](../12-Limitations.md), and [JpegXlEncoder](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder) writes lossless files
by default. [JpegXlTranscoder](cref:Meziantou.Framework.Imaging.JpegXlTranscoder) stores a JPEG file in a smaller JPEG XL file without any loss, and rebuilds it.

Interoperability does not imply encoder parity with libjxl: the managed encoder makes its own coding decisions.
There is no general guarantee of comparable file size, encoding speed or perceptual quality, and equal quality/effort
numbers do not establish a matched comparison. Measure representative images at matched decoded quality for lossy output.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Coding | Lossy (VarDCT) and lossless (Modular) frames; recompressed JPEG files | Lossless (default) or lossy |
| Samples | Integers up to 24 bits and floating-point numbers, gray and color, alpha; CMYK, converted to RGB | Every pixel format at its own depth ([Gray8](cref:Meziantou.Framework.Imaging.Gray8), [Gray16](cref:Meziantou.Framework.Imaging.Gray16), [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24), [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32), [Bgra32](cref:Meziantou.Framework.Imaging.Bgra32), [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64)), lossless alpha |
| Animations | Composited into full frames with exact durations and the play count | Full frames, durations in milliseconds, play count |
| Progressive | Renderings of a partial file | Progressive files on request |
| Metadata | ICC profile, EXIF, XMP, enumerated color encodings, orientation | The same |

8-bit files load as [Gray8](cref:Meziantou.Framework.Imaging.Gray8), [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24) or [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32); files of up to 16 bits as [Gray16](cref:Meziantou.Framework.Imaging.Gray16), [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48) or [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64). The orientation of the
image header is reported in `Metadata.Orientation`, never applied.

## Read floating-point and CMYK files

JPEG XL also stores the samples of high dynamic range and print workflows. They load without losing anything, or not at
all:

- Floating-point samples load as [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) when every sample is a half-precision number, otherwise as
  [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat). Values above 1 (highlights) and below 0 are kept, in the color encoding the file declares
  (often linear sRGB): nothing is clamped or tone mapped.
- Integer samples of 17 to 24 bits load as [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat): each sample divided by the largest value, which gives the
  sample back when multiplied by it. Integers of more than 24 bits do not fit a single-precision number and throw an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
- A CMYK file (cyan, magenta and yellow in the color channels, black in an extra channel) loads only with
  [ImageDecodeOptions.CmykConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CmykConversion): its inks are converted from its CMYK profile to RGB, its layers composited first,
  and the alpha channel is kept. [Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) reports [ImageColorModel.Cmyk](cref:Meziantou.Framework.Imaging.ImageColorModel.Cmyk). See [Color management](../09-Color/03-ColorManagement.md).

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadJpegxl;format=md-fenced-code-block;dedent=true -->
<!-- endref -->

Layers (frames without duration) are composited with the frames they belong to, with the blend modes and the
reference frames of the file: an image holds what a viewer shows.

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
| [Compression](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder.Compression) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.JpegXlCompression.Lossless) or [Lossy](cref:Meziantou.Framework.Imaging.Formats.JpegXlCompression.Lossy) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.JpegXlCompression.Lossless) |
| [Quality](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder.Quality) | 0 (smallest) to 100 (best) for lossy output: 90 is barely distinguishable from the source, 100 is still lossy | 75 |
| [Effort](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder.Effort) | 0 (fastest, largest) to 9 (slowest, smallest): changes the decisions of the encoder, never lossless pixels nor the size [Quality](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder.Quality) aims at | 5 |
| [Progressive](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder.Progressive) | Writes a file that a reader can show at a lower resolution before it is complete; the file is larger | `false` |
| [DurationRounding](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder.DurationRounding) | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) rounds durations to the nearest millisecond; [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) throws | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata JPEG XL cannot store (text, resolution) | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

For lossless output, the effort sets how much the encoder learns about the image before coding it. On a 2048x1536
photograph, on one thread of a desktop x64 processor: efforts 0 to 2 take about 80 ms, effort 3 about 340 ms, effort 5
(the default) about 660 ms, effort 7 about 1.4 s and effort 9 about 2.5 s. The file of effort 3 is 23% smaller than the one
of efforts 0 to 2, effort 5 is 3% smaller than effort 3, and effort 9 another 2%. Screenshots and graphics gain more from
efforts 3 to 5, which code them with a palette of their colors and copy their repeated runs: a 2048x2048 screenshot is
about half as large at effort 5 as at effort 4.

For lossy output, from effort 3 the encoder also learns how to code the low-frequency image and the description of the
blocks, as it does for lossless output. On 1024x1024 photographs this makes the files 4% smaller at the same quality (7%
at quality 30, 2% at quality 90), and 1% on 356x356 ones, for about 15% more encoding time (25% on the small images).

## Show an image while it downloads

[ReadFrameRefinementInto](cref:Meziantou.Framework.Imaging.ImageReader{TPixel}.ReadFrameRefinementInto) renders a frame from the bytes received so far: coarse at first, then finer, then the frame
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

[JpegXlTranscoder.RecompressJpeg](cref:Meziantou.Framework.Imaging.JpegXlTranscoder.RecompressJpeg) stores the coefficients of a JPEG file in a JPEG XL file without decoding its pixels,
and [TryReconstructJpeg](cref:Meziantou.Framework.Imaging.JpegXlTranscoder.TryReconstructJpeg) gives the JPEG file back, byte for byte. This is not [Image.Save](cref:Meziantou.Framework.Imaging.Image.Save), which stores pixels:

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

- [HasJpegReconstructionData](cref:Meziantou.Framework.Imaging.JpegXlTranscoder.HasJpegReconstructionData) tells whether a JPEG XL file holds a JPEG file, without decoding it.
  [TryReconstructJpeg](cref:Meziantou.Framework.Imaging.JpegXlTranscoder.TryReconstructJpeg) returns `false` for a file made from pixels, and [ReconstructJpeg](cref:Meziantou.Framework.Imaging.JpegXlTranscoder.ReconstructJpeg) throws for it.
- Files that a JPEG XL frame cannot hold exactly are rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): CMYK and YCCK
  files, 4:1:1 sampling, arithmetic-coded, lossless, hierarchical and 12-bit files, among others. Nothing is
  approximated.
- The recompressed files are read by other JPEG XL decoders, and the library rebuilds the files that
  `cjxl --lossless_jpeg=1` writes.

## Pitfalls

- A frame of an animation without duration is rejected: JPEG XL defines it as a layer of the next frame.
- Lossy output converts the samples to the XYB color space, which needs an enumerated color encoding (sRGB, linear,
  BT.709 or DCI, with sRGB, Display P3 or BT.2020 primaries) or an sRGB ICC profile. Another ICC profile, or the
  PQ and HLG transfer functions, throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): write those images losslessly.
- The decoder rejects integer samples of more than 24 bits and premultiplied alpha. A CMYK image throws an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) unless the load sets [CmykConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CmykConversion).
- A floating-point image is not saved as JPEG XL as it is: the encoder takes integer pixel formats. Set
  [FloatingPointConversion](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.FloatingPointConversion) to reduce it to 16 bits, or save it as OpenEXR or TIFF.
- A lossy file with an ICC profile is decoded to the colors of that profile, through its colorants and tone curves, and
  keeps the profile. A file whose profile converts with lookup tables throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
