---
title: Convert between formats
description: Convert a file or a whole folder to another format, and decide what happens to what the target cannot store.
---

Converting an image means loading it and saving it in another format. The library never guesses what to do with what
the target format cannot store, such as the transparency of a PNG file saved as JPEG: you say it once, with the
settings of the encoder, and the conversion is exact everywhere else.

## Convert a file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_ConvertFile;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConvertFile(string inputPath, string outputPath)
{
    // The format of the input comes from its content, the format of the output from the extension of outputPath
    using var image = Image.Load(inputPath);

    // Throws UnsupportedImageFeatureException, before writing anything, when the output cannot hold the image:
    // an animation, transparency, 16-bit samples or metadata the format does not store
    image.Save(outputPath);
}
```
<!-- endref -->

- `Image.Load` detects the format of the input from its content: a PNG file named `photo.jpg` loads as a PNG image.
- `Save(path)` chooses the encoder from the extension of the output, with its default settings: `.png`, `.apng`,
  `.gif`, `.jpg`, `.webp`, `.avif`, `.heic`, `.jxl`, `.tif`, `.bmp` and the others listed in
  [Saving images](../05-LoadingAndSaving/04-SavingImages.md). An unknown extension throws an `ArgumentException`.
- The image is checked against the format before anything is written. When the output would lose something, `Save`
  throws an `UnsupportedImageFeatureException` and no file is created.

## Choose the encoder and its settings

Pass an encoder to `Save` to choose the format and its settings, whatever the extension. The encoders are in the
`Meziantou.Framework.Imaging.Formats` namespace: `JpegEncoder`, `PngEncoder`, `WebPEncoder`, `GifEncoder`,
`AvifEncoder`, `HeicEncoder` and one per format. An encoder is also the only way to save to a stream, which has no
extension.

The default encoder of an extension is not always what you want:

| Extension | Default | You may want |
| --- | --- | --- |
| `.jpg` | Quality 90, baseline | A lower `Quality` for smaller files, `Process = JpegEncodingProcess.Progressive` for the web |
| `.webp`, `.avif`, `.heic`, `.jxl` | Lossless | `Compression` set to lossy, and a `Quality`, for photos |
| `.png` | Lossless, APNG when the image is animated | `PngEncoder { AnimationMode = PngAnimationMode.Animated }` (or `.apng`) to always write APNG |

## Decide what the target cannot store

The target format may lack something that the image has. Each case has its own setting, so that a loss is always a
choice:

| The image has | Formats that cannot store it | Setting |
| --- | --- | --- |
| Transparency | JPEG | `BackgroundColor`: the opaque color that transparent pixels are blended on |
| 16-bit samples | JPEG, GIF, WebP, QOI, AVIF and HEIC (12 bits at most), and others | `AllowBitDepthReduction` on the encoders that have it (JPEG, WebP, AVIF, HEIC, QOI...); GIF reduces the colors to a palette anyway |
| Several frames (an animation) | JPEG, BMP, QOI, static PNG... | Export one frame with `CloneFrame` |
| Metadata (EXIF, XMP, ICC profile, text) | GIF, BMP, QOI, TGA, Netpbm, and partly the others | `MetadataHandling = MetadataHandling.DiscardUnsupported` drops what the format cannot store; `Strip` writes none |
| Floating-point samples (OpenEXR) | Every format but OpenEXR and TIFF | `FloatingPointConversion`: how the samples are reduced to integers |

For example, to export one frame of an animation as a JPEG file:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ExportJpegFrame;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

`MetadataHandling.DiscardUnsupported` and `Strip` may drop the orientation and the color profile, which change how the
pixels look: a photo can turn sideways or lose its colors. Apply them to the pixels before saving with `AutoOrient` and
`ConvertColorProfile` (see [Strip or edit metadata](08-StripOrEditMetadata.md)). The metadata that each format stores is
listed in [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).

## Change the pixel format

Some conversions change the pixels themselves, not only the file: a grayscale copy, or the removal of the alpha channel.
`CloneAs<TPixel>` converts to another pixel format, and its `PixelConversionOptions` say how to remove alpha and what to
do with a color profile that cannot describe the new pixels:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertPrecision;format=md-fenced-code-block;dedent=true -->
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

`Gray16` keeps the 16-bit precision of the source, and PNG stores it as it is. See
[Pixel formats](../04-Concepts/05-PixelFormats.md).

## Convert a whole folder

A batch conversion reuses one encoder for every file, and decides what to do with the files it cannot convert:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_ConvertFolder;format=md-fenced-code-block;dedent=true -->
```csharp
public static IReadOnlyList<string> ConvertFolderToJpeg(string inputDirectory, string outputDirectory)
{
    // JPEG stores one picture: decoding stops after the first frame of an animation. A CMYK JPEG is converted to
    // RGB; the other images are loaded as usual
    var decodeOptions = new ImageDecodeOptions { FrameLimit = 1, CmykConversion = CmykConversionOptions.Default };

    // Encoders are immutable: one instance serves every file. Each setting allows a loss that is otherwise rejected
    var encoder = new JpegEncoder
    {
        Quality = 85,
        BackgroundColor = new Rgba32(255, 255, 255),           // transparency
        AllowBitDepthReduction = true,                         // 16-bit samples
        MetadataHandling = MetadataHandling.DiscardUnsupported, // metadata JPEG cannot store
    };

    Directory.CreateDirectory(outputDirectory);
    var skipped = new List<string>();
    foreach (var inputPath in Directory.EnumerateFiles(inputDirectory))
    {
        try
        {
            using var image = Image.Load(inputPath, decodeOptions);

            // The first frame of an animation keeps the animation settings (and an APNG its poster): make it a
            // still image
            image.RemovePosterFrame();
            image.Animation = null;

            image.Save(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".jpg"), encoder);
        }
        catch (ImageException)
        {
            // Not an image, a corrupted file, or a feature that cannot be converted: nothing was written
            skipped.Add(inputPath);
        }
    }

    return skipped;
}
```
<!-- endref -->

- `FrameLimit = 1` stops decoding after the first frame of an animation: the rest of the file is not read. The image
  keeps its animation settings, so `Animation = null` makes it a still image, after `RemovePosterFrame` removes the
  default image of an APNG file, if any.
- `CmykConversionOptions.Default` converts the CMYK JPEG files of print workflows to sRGB. Without it, they throw an
  `UnsupportedImageFeatureException`. The other images are not affected.
- `ImageException` is the base class of the content errors: an unknown format, a corrupted file, an unsupported feature
  and an exceeded resource limit. I/O errors are not caught: they are not a problem of the file.
- A failed save leaves nothing behind: a path output is written to a temporary file, renamed only when it is complete.

The files are converted one after the other. Images are independent, so you can convert several in parallel, one image
per thread.
