---
title: Images
description: Validate images and compare image snapshots by their pixels, exactly or with similarity thresholds.
---

`Meziantou.Framework.SnapshotTesting.Imaging` compares image snapshots by their pixels instead of their bytes, so an image
encoded differently, by another version of a library or of the runtime, still matches when its pixels are the same. It
is based on [Meziantou.Framework.Imaging](../../Imaging/01-Introduction.md), a fully managed image library with no
native dependency.

<InstallNuGetPackage Id="Meziantou.Framework.SnapshotTesting.Imaging" />

## Set up

Call [AddImaging()](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotSettingsImagingExtensions.AddImaging) on [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) in a module initializer ([AddImaging](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotSettingsImagingExtensions.AddImaging) is in the
[Meziantou.Framework.SnapshotTesting.Imaging](cref:Meziantou.Framework.SnapshotTesting.Imaging) namespace):

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/SnapshotConfiguration.cs;region=SnapshotTestingImaging_Setup;format=md-fenced-code-block;dedent=true -->
```csharp
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddImaging();
    }
}
```
<!-- endref -->

Then validate images, as [Image](cref:Meziantou.Framework.Imaging.Image) objects or as the bytes of a file ([Image](cref:Meziantou.Framework.Imaging.Image) is in the [Meziantou.Framework.Imaging](cref:Meziantou.Framework.Imaging)
namespace):

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/SampleTests.cs;region=SnapshotTestingImaging_Validate;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class SampleTests
{
    [Fact]
    public void ValidateImage()
    {
        using var image = Image.Load("sample.png");
        Snapshot.Validate(image, SnapshotType.Png);
    }

    [Fact]
    public void ValidateImageBytes()
    {
        var bytes = File.ReadAllBytes("sample.jpg");
        Snapshot.Validate(bytes, SnapshotType.Jpeg);
    }
}
```
<!-- endref -->

[AddImaging()](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotSettingsImagingExtensions.AddImaging) registers:

- **A serializer for [Image](cref:Meziantou.Framework.Imaging.Image) values**, saved in the format of the snapshot type: [SnapshotType.Png](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Png), [Jpeg](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Jpeg), [Bmp](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Bmp),
  [Tiff](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Tiff), [Webp](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Webp), [Gif](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Gif), [Ico](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Ico), [Avif](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Avif), [Heic](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Heic), [JpegXl](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.JpegXl) or [Jpeg2000](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Jpeg2000), or `"cur"`, `"ani"`, `"qoi"`, `"tga"` or
  `"pnm"`. Every format but GIF and JPEG is written lossless and keeps the pixels exactly. GIF keeps at most 256 colors
  and stores transparency as on or off, and JPEG uses the highest quality. 16-bit samples are kept by PNG, TIFF, PNM,
  JPEG XL and JPEG 2000, reduced to 12 bits by AVIF and HEIC, and to 8 bits by the other formats. Metadata the format
  cannot hold is dropped.
- **The image comparer** for every format that Meziantou.Framework.Imaging decodes, under each of its file extensions:
  PNG and APNG, JPEG, GIF, WebP, BMP, TIFF, ICO, CUR, ANI, QOI, TGA, PNM (PBM, PGM, PPM and PAM), AVIF, HEIC, JPEG XL and
  JPEG 2000.
- **A converter** that writes [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) colors as `#RRGGBBAA` in text snapshots.

Prefer PNG for image snapshots: it is lossless, compact, and diff tools display it.

## Register the comparer only

To compare image files by their pixels without the serializer of [Image](cref:Meziantou.Framework.Imaging.Image) values, register the comparer alone:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_AddImageComparer;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
settings.Comparers.AddImageComparer();
Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Png));
```
<!-- endref -->

## How images are compared

The comparer decodes both snapshots, so every format the library reads is supported, CMYK JPEG and TIFF included.

- Every frame is compared: the frames of an animated GIF, APNG, WebP, ANI, AVIF, HEIC or JPEG XL file, the pages of a
  TIFF file, and the representations of an ICO or CUR file. The images must have the same number of frames and the same
  size.
- Identical bytes always match, and a snapshot that cannot be decoded does not match.
- By default, the pixels must be equal. Fully transparent pixels are equal whatever color they hide. Images with samples
  of more than 8 bits are compared on 16 bits: an 8-bit sample `v` equals the 16-bit sample `v × 257`.

When images do not match, the failure message says why under the changed file: a snapshot cannot be decoded, the images
have a different number of frames, a different size or different pixels, or a score is beyond its threshold. For images
with several frames, it names the first frame that does not match.

## Allow small differences

Rendering can differ slightly between platforms, for instance in antialiasing or font hinting. Pass
[ImageComparisonSettings](cref:Meziantou.Framework.SnapshotTesting.Imaging.ImageComparisonSettings) with thresholds to accept images that are close enough:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_Thresholds;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
settings.AddImaging(new ImageComparisonSettings
{
    SimilarityThreshold = 0.99f, // SSIM: 0.0 = completely different, 1.0 = identical
    DHashThreshold = 4,          // dHash distance: 0 = same structure, up to 64
    PHashThreshold = 4,          // pHash distance: 0 = same structure, up to 64
});
```
<!-- endref -->

Perceptual metrics are closer to what a viewer sees, but are slower to compute:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_PerceptualThresholds;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
settings.AddImaging(new ImageComparisonSettings
{
    PsnrThreshold = 40,        // PSNR in decibels: higher is closer, infinite for identical images
    Ssimulacra2Threshold = 90, // SSIMULACRA 2: 100 = identical, about 90 = not visible
    ButteraugliThreshold = 1,  // Butteraugli distance: 0 = identical, below about 1 = not visible
});
```
<!-- endref -->

Every configured check must pass for every frame, and the failure message reports all the checks that failed. The
measures are those of [ImageSimilarity](cref:Meziantou.Framework.Imaging.ImageSimilarity) in Meziantou.Framework.Imaging (see
[Comparing images](../../Imaging/06-Processing/10-ComparingImages.md)); samples of more than 8 bits are rounded to 8
bits.

| Setting | Measure | Passes when |
| --- | --- | --- |
| [SimilarityThreshold](cref:Meziantou.Framework.SnapshotTesting.Imaging.ImageComparisonSettings.SimilarityThreshold) | SSIM, the mean structural similarity over 7×7 windows, from 0 (different) to 1 (identical) | The score is at least the threshold |
| [PsnrThreshold](cref:Meziantou.Framework.SnapshotTesting.Imaging.ImageComparisonSettings.PsnrThreshold) | The peak signal-to-noise ratio in decibels, infinite for identical images | The ratio is at least the threshold |
| [Ssimulacra2Threshold](cref:Meziantou.Framework.SnapshotTesting.Imaging.ImageComparisonSettings.Ssimulacra2Threshold) | The SSIMULACRA 2 score of libjxl: 100 for identical images, about 90 when the difference is not visible | The score is at least the threshold |
| [ButteraugliThreshold](cref:Meziantou.Framework.SnapshotTesting.Imaging.ImageComparisonSettings.ButteraugliThreshold) | The largest Butteraugli distance of libjxl: 0 for identical images, below about 1 when the difference is not visible | The distance is at most the threshold |
| [DHashThreshold](cref:Meziantou.Framework.SnapshotTesting.Imaging.ImageComparisonSettings.DHashThreshold) | The distance between the 64-bit difference hashes, from 0 to 64 | The distance is at most the threshold |
| [PHashThreshold](cref:Meziantou.Framework.SnapshotTesting.Imaging.ImageComparisonSettings.PHashThreshold) | The distance between the 64-bit perceptual hashes, from 0 to 64 | The distance is at most the threshold |

Choose the measure by the kind of difference you accept:

- **SSIM and PSNR** are means over the image: a difference confined to a small area lowers them in proportion to the
  area it covers. A difference confined to 1% of the image lowers SSIM by about 0.01 at most.
- **Butteraugli** keeps the distance of the worst pixel, so a small but visible difference weighs fully.
- **SSIMULACRA 2** penalizes new artifacts (ringing, banding, blockiness) and lost details (blurring). Images smaller
  than 8×8 pixels are too small for it: they pass this check only when their pixels are identical.
- **dHash and pHash** compare the structure of the image and ignore invisible noise in flat areas. Their distance also
  counts the difference between the mean luminances: a solid black and a solid white image are 64 apart.

Transparency is taken into account by every measure: SSIM and PSNR also compare the alpha channel, and the other measures
compare images that are not fully opaque over two backgrounds and keep the worse result.

## Store GIF and ICO files as PNG frames

The bytes of a GIF or ICO file can be stored as one PNG snapshot per frame (GIF) or per representation (ICO), so that a
change shows which frame differs:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_GifAndIcoFrames;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = SnapshotSettings.Default with { };
settings.Serializers.AddGifSerializer();
settings.Serializers.AddIcoSerializer();
settings.Comparers.AddImageComparer();

var gifBytes = File.ReadAllBytes("animation.gif");
var icoBytes = File.ReadAllBytes("favicon.ico");
Snapshot.Validate(gifBytes, SnapshotType.Gif, settings);
Snapshot.Validate(icoBytes, SnapshotType.Ico, settings);
```
<!-- endref -->

Register a comparer for [SnapshotType.Png](cref:Meziantou.Framework.SnapshotTesting.SnapshotType.Png) too ([AddImageComparer](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotComparerCollectionImageComparerExtensions.AddImageComparer) or [AddImaging](cref:Meziantou.Framework.SnapshotTesting.Imaging.SnapshotSettingsImagingExtensions.AddImaging)), so that the frames are compared by
their pixels: the compressed bytes of a PNG file depend on the zlib of the runtime, and a runtime update could otherwise
make every frame differ. Data that is not a GIF or ICO file is left to the next serializer.
