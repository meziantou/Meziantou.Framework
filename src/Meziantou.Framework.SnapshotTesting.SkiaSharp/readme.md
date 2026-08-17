# Meziantou.Framework.SnapshotTesting.SkiaSharp

`Meziantou.Framework.SnapshotTesting.SkiaSharp` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with support for [SkiaSharp](https://github.com/mono/SkiaSharp) images, enabling snapshot validation of `SKImage`, `SKBitmap`, `SKPixmap`, and `SKSurface` objects stored as PNG, JPEG, or WebP files.

## Setup

Call `AddSkiaSharp()` on your `SnapshotSettings` to register the SkiaSharp serializer and comparer:

```csharp
public sealed class SampleTests
{
    [Fact]
    public void ValidateImage()
    {
        SnapshotSettings.Default.AddSkiaSharp();

        using var bitmap = SKBitmap.Decode("sample.png");
        Snapshot.Validate(bitmap, SnapshotType.Png);
    }
}
```

Note that SkiaSharp requires the native libraries to be available at runtime. They are included in the `SkiaSharp` package for Windows and macOS. On Linux, add a reference to [`SkiaSharp.NativeAssets.Linux`](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux).

## Image comparison

By default, images are compared pixel-by-pixel (exact comparison). To allow minor rendering differences, configure a [Structural Similarity Index (SSIM)](https://en.wikipedia.org/wiki/Structural_similarity_index_measure) threshold:

```csharp
SnapshotSettings.Default.AddSkiaSharp(new ImageComparisonSettings
{
    SimilarityThreshold = 0.99f, // 0.0 = completely different, 1.0 = identical
});
```

When `SimilarityThreshold` is set, the mean SSIM across the R, G, and B channels is computed and must be greater than or equal to the threshold for the images to be considered equal.
