# Meziantou.Framework.SnapshotTesting.ImageSharp

`Meziantou.Framework.SnapshotTesting.ImageSharp` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with support for [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) images, enabling snapshot validation of `Image` objects stored as PNG, JPEG, BMP, TIFF, or WebP files.

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

When `SimilarityThreshold` is set, the mean SSIM across the R, G, and B channels is computed and must be greater than or equal to the threshold for the images to be considered equal.
