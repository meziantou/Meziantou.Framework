# Meziantou.Framework.SnapshotTesting.ImageSharp

`Meziantou.Framework.SnapshotTesting.ImageSharp` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with support for [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) images, enabling snapshot validation of `Image` objects stored as PNG, JPEG, BMP, TIFF, or WebP files.

## Licensing

This package is MIT-licensed, but it takes a hard dependency on
[SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp), which since v3 is published under the
[Six Labors Split License](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE): AGPL-3.0 unless you
hold a commercial licence. Installing this package therefore brings that obligation with it, and your build
will emit a "No Six Labors license found" warning until you set `SixLaborsLicenseKey`, set
`SixLaborsLicenseFile`, or add a `sixlabors.lic` file.

If that does not suit your project,
[`Meziantou.Framework.SnapshotTesting.SkiaSharp`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting.SkiaSharp)
offers the same image snapshot support on top of SkiaSharp, which is MIT-licensed.

## Setup

Call `AddImageSharp()` on your `SnapshotSettings` to register the ImageSharp serializer and comparer:

```csharp
using System.Runtime.CompilerServices;
using Meziantou.Framework.SnapshotTesting;
using Meziantou.Framework.SnapshotTesting.ImageSharp;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddImageSharp();
    }
}
```

Then validate images from your tests:

```csharp
public sealed class SampleTests
{
    [Fact]
    public void ValidateImage()
    {
        using var image = Image.Load("sample.png");
        Snapshot.Validate(image, SnapshotType.Png);
    }
}
```

> [!IMPORTANT]
> Register once for the whole test assembly, not inside a test method. `SnapshotSettings.Default` is
> shared by every test in the process and its collections are not safe for concurrent modification, so
> calling `AddImageSharp()` from a test races against any other test that is serializing a snapshot at
> the same time. Repeated calls also append a serializer and a converter every time.

## Image comparison

By default, images are compared pixel-by-pixel (exact comparison). To allow minor rendering differences, configure a [Structural Similarity Index (SSIM)](https://en.wikipedia.org/wiki/Structural_similarity_index_measure) threshold:

```csharp
// In the module initializer shown above
SnapshotSettings.Default.AddImageSharp(new ImageComparisonSettings
{
    SimilarityThreshold = 0.99f, // 0.0 = completely different, 1.0 = identical
});
```

When `SimilarityThreshold` is set, the images must have the same dimensions and their score must be greater than or equal to the threshold. The score is the mean SSIM over every 7×7 window of the images (uniform weights, sample covariance, `K1 = 0.01`, `K2 = 0.03`), the value scikit-image's `structural_similarity(expected, actual, data_range=255, channel_axis=-1)` computes with its default parameters. The R, G, and B channels are premultiplied by the alpha channel and averaged, and the score is the lower of that value and the SSIM of the alpha channel, so opacity differences are detected. Because the score is a mean, a localized difference lowers it in proportion to the area it covers: a difference confined to 1% of the image lowers it by about 0.01 at most. Fully transparent pixels are equal whatever color they hide, with both the exact and the SSIM comparison, and a snapshot that cannot be decoded does not match.

Images with samples of more than 8 bits, such as 16-bit PNGs, follow the same rule as the built-in `ImageComparer` and the SkiaSharp package: the exact comparison sees every bit of every sample (an 8-bit sample `v` equals the 16-bit sample `v × 257`), and the SSIM reduces each sample to 8 bits by keeping its high byte.

When images do not match, the assertion message says why under the changed file: an image cannot be decoded, the images have different sizes or different pixels, or the SSIM score is below the threshold.
