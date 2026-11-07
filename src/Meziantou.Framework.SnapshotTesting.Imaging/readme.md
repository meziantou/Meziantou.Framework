# Meziantou.Framework.SnapshotTesting.Imaging

`Meziantou.Framework.SnapshotTesting.Imaging` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with image snapshots based on [`Meziantou.Framework.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.Imaging), a fully managed image library with no native dependency. Image snapshots are compared by their pixels, exactly or by similarity, instead of by their bytes.

## Setup

Call `AddImaging()` on your `SnapshotSettings` to register the image serializer and comparer, for instance in a module
initializer (`ModuleInitializer` is in the `System.Runtime.CompilerServices` namespace, `SnapshotSettings` in
`Meziantou.Framework.SnapshotTesting` and `AddImaging` in `Meziantou.Framework.SnapshotTesting.Imaging`):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/SnapshotConfiguration.cs;region=SnapshotTestingImaging_Setup;format=md-fenced-code-block;dedent=true -->
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

Then validate images from your tests (`Image` is in the `Meziantou.Framework.Imaging` namespace):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/SampleTests.cs;region=SnapshotTestingImaging_Validate;format=md-fenced-code-block;dedent=true -->
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

`AddImaging()` registers:

- a serializer for `Meziantou.Framework.Imaging.Image` values, saved as `SnapshotType.Png`, `Jpeg`, `Bmp`, `Tiff`, `Webp`, `Gif`, `Ico`, `Avif`, `Heic`, `JpegXl` or `Jpeg2000`, or as `"cur"`, `"ani"`, `"qoi"`, `"tga"` or `"pnm"`. Every format but GIF and JPEG is written lossless and keeps the pixels exactly, GIF keeps at most 256 colors and stores transparency as on or off, and JPEG uses the highest quality. 16-bit samples are kept by PNG, TIFF, PNM, JPEG XL and JPEG 2000, reduced to 12 bits by AVIF and HEIC and to 8 bits by the other formats. Metadata the format cannot hold is dropped;
- the `ImageComparer` for every format `Meziantou.Framework.Imaging` decodes, under each of its file extensions: PNG and APNG, JPEG, GIF, WebP, BMP, TIFF, ICO, CUR, ANI, QOI, TGA, PNM (PBM, PGM, PPM and PAM), AVIF, HEIC, JPEG XL and JPEG 2000 (`SnapshotType.Avif`, `Heic`, `JpegXl` and `Jpeg2000`);
- a converter that writes `Rgba32` colors as `#RRGGBBAA` in human-readable snapshots.

To compare image snapshots by pixels without the serializer, register the comparer alone (on `SnapshotSettings.Default`
in the module initializer, or on a copy of the settings):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_AddImageComparer;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
settings.Comparers.AddImageComparer();
Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Png));
```
<!-- endref -->

> [!IMPORTANT]
> Register once for the whole test assembly, not inside a test method. `SnapshotSettings.Default` is
> shared by every test in the process and its collections are not safe for concurrent modification, so
> calling `AddImaging()` from a test races against any other test that is serializing a snapshot at
> the same time. Repeated calls also append a serializer and a converter every time.

## GIF and ICO frames

The bytes of a GIF or ICO file can be stored as one PNG snapshot per frame (GIF) or per representation (ICO), so that a change shows which frame differs:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_GifAndIcoFrames;format=md-fenced-code-block;dedent=true -->
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

Register a comparer for `SnapshotType.Png` (`AddImageComparer` or `AddImaging`) so that the frames are compared by pixels: the compressed bytes of a PNG file depend on the zlib of the runtime, so a runtime update could otherwise make every frame differ. Data that is not a GIF or ICO file is left to the next serializer.

## Image comparison

`ImageComparer` decodes both snapshots with `Meziantou.Framework.Imaging`, so every format the library reads is supported, CMYK JPEG and TIFF included. Every frame is compared: the frames of an animated GIF, APNG, WebP, ANI, AVIF, HEIC or JPEG XL file, the pages of a TIFF file and the representations of an ICO or CUR file. The images must have the same number of frames and the same size. Identical bytes always match, and a snapshot that cannot be decoded does not match.

By default, images are compared pixel by pixel (exact comparison). Fully transparent pixels are equal whatever color they hide. Images with samples of more than 8 bits, such as 16-bit PNG files, are compared on 16 bits: an 8-bit sample `v` equals the 16-bit sample `v × 257`.

To allow minor rendering differences, configure thresholds (on `SnapshotSettings.Default` in the module initializer
shown above):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_Thresholds;format=md-fenced-code-block;dedent=true -->
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

Perceptual metrics are closer to what a viewer sees, at a higher cost (about 0.3 s per megapixel for SSIMULACRA 2 and
0.75 s for Butteraugli):

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Imaging.Tests/ImageSnapshotTests.cs;region=SnapshotTestingImaging_PerceptualThresholds;format=md-fenced-code-block;dedent=true -->
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

Every configured check must pass for every frame, and the assertion message reports all the checks that failed. The measures are those of `ImageSimilarity` in `Meziantou.Framework.Imaging`; samples of more than 8 bits are rounded to 8 bits.

- **SSIM.** The [Structural Similarity Index](https://en.wikipedia.org/wiki/Structural_similarity_index_measure) must be greater than or equal to `SimilarityThreshold`. The score is the mean SSIM over every 7×7 window of the images (uniform weights, sample covariance, `K1 = 0.01`, `K2 = 0.03`), the value scikit-image's `structural_similarity(expected, actual, data_range=255, channel_axis=-1)` computes with its default parameters. The R, G and B channels are premultiplied by the alpha channel and averaged, and the score is the lower of that value and the SSIM of the alpha channel, so opacity differences are detected. Because the score is a mean, a localized difference lowers it in proportion to the area it covers: a difference confined to 1% of the image lowers it by about 0.01 at most.
- **PSNR.** The peak signal-to-noise ratio, in decibels, must be greater than or equal to `PsnrThreshold`. It is computed from the mean squared error of the R, G and B channels premultiplied by alpha, the value scikit-image's `peak_signal_noise_ratio(expected, actual, data_range=255)` computes for opaque images; the alpha channel gives its own ratio, and the lower one is used. Identical images have an infinite ratio.
- **SSIMULACRA 2.** The score of the `ssimulacra2` tool of libjxl must be greater than or equal to `Ssimulacra2Threshold`: 100 for identical images, about 90 when the difference is not visible, 70 for a high quality. It compares the images in the XYB color space at 6 scales, and penalizes new artifacts (ringing, banding, blockiness) and lost details (blurring). Images that are not fully opaque are composited over a dark and a bright gray, and the lower score is used. Images smaller than 8×8 pixels are too small for the metric: they pass this check only when their pixels are identical.
- **Butteraugli.** The largest Butteraugli distance of libjxl must be lower than or equal to `ButteraugliThreshold`: 0 for identical images, below about 1 when the difference is not visible. The distance of the worst pixel is used, so a difference confined to a small area weighs fully, unlike the SSIM and PSNR means. Images that are not fully opaque are composited over black and over white, and every pixel keeps the larger distance.
- **dHash and pHash.** The distance between the 64-bit difference hashes or perceptual hashes must be lower than or equal to the threshold. The distance is the Hamming distance between the hashes, plus the difference between the mean luminances (a solid black and a solid white image are 64 apart; each unit is about 4 luminance levels out of 255), capped at 64. Differences smaller than a luminance level never set a bit, so invisible noise in flat areas does not change the hashes. Images that are not fully opaque are compared over a black and over a white background, and the larger distance is used.

When images do not match, the assertion message says why under the changed file: a snapshot cannot be decoded, the images have a different number of frames, different sizes or different pixels, or a score is beyond its threshold. For images with several frames, the message names the first frame that does not match.
