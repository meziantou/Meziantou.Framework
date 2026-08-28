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
public sealed class SampleTests
{
    [Fact]
    public void ValidateImage()
    {
        SnapshotSettings.Default.AddImageSharp();

        using var image = Image.Load("sample.png");
        Snapshot.Validate(image, SnapshotType.Png);
    }
}
```

## Image comparison

By default, images are compared pixel-by-pixel (exact comparison). To allow minor rendering differences, configure a [Structural Similarity Index (SSIM)](https://en.wikipedia.org/wiki/Structural_similarity_index_measure) threshold:

```csharp
SnapshotSettings.Default.AddImageSharp(new ImageComparisonSettings
{
    SimilarityThreshold = 0.99f, // 0.0 = completely different, 1.0 = identical
});
```

When `SimilarityThreshold` is set, the mean SSIM across the R, G, and B channels is computed and must be greater than or equal to the threshold for the images to be considered equal.
