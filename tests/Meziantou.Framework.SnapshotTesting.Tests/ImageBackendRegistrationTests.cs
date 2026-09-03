using System.Reflection;
using Meziantou.Framework.SnapshotTesting.ImageSharp;
using Meziantou.Framework.SnapshotTesting.SkiaSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using ImageSharpImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace Meziantou.Framework.SnapshotTesting.Tests;

/// <summary>
/// Pins the precedence between the two image backends when both are registered on the same settings.
/// The tests only inspect the registrations, so they do not need the SkiaSharp native libraries.
/// </summary>
public sealed class ImageBackendRegistrationTests
{
    private static readonly Assembly ImageSharpBackend = typeof(SnapsthotSettingsImageSharpExtensions).Assembly;
    private static readonly Assembly SkiaSharpBackend = typeof(SnapshotSettingsSkiaSharpExtensions).Assembly;

    [Fact]
    public void AddSkiaSharpAfterImageSharp_SkiaSharpSerializerIsTriedFirst()
    {
        var settings = new SnapshotSettings();
        settings.AddImageSharp();
        settings.AddSkiaSharp();

        // Serialize walks the collection from the last serializer to the first, so being registered last wins
        Assert.Equal<Assembly>([ImageSharpBackend, SkiaSharpBackend], GetBackendSerializers(settings));
    }

    [Fact]
    public void AddImageSharpAfterSkiaSharp_ImageSharpSerializerIsTriedFirst()
    {
        var settings = new SnapshotSettings();
        settings.AddSkiaSharp();
        settings.AddImageSharp();

        Assert.Equal<Assembly>([SkiaSharpBackend, ImageSharpBackend], GetBackendSerializers(settings));
    }

    [Fact]
    public void AddSkiaSharpAfterImageSharp_SkiaSharpComparerWinsForTheSharedFormats()
    {
        var settings = new SnapshotSettings();
        settings.AddImageSharp();
        settings.AddSkiaSharp();

        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Bmp)));
        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Png)));
        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Jpeg)));
        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Webp)));

        // Only SkiaSharp registers a comparer for these formats
        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Gif)));
        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Ico)));

        // SKCodec cannot decode TIFF, so AddSkiaSharp does not register a comparer for it and the ImageSharp one is kept
        Assert.Equal(ImageSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Tiff)));
    }

    [Fact]
    public void AddImageSharpAfterSkiaSharp_ImageSharpComparerWinsForTheSharedFormats()
    {
        var settings = new SnapshotSettings();
        settings.AddSkiaSharp();
        settings.AddImageSharp();

        Assert.Equal(ImageSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Bmp)));
        Assert.Equal(ImageSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Png)));
        Assert.Equal(ImageSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Jpeg)));
        Assert.Equal(ImageSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Webp)));
        Assert.Equal(ImageSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Tiff)));

        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Gif)));
        Assert.Equal(SkiaSharpBackend, GetBackend(settings.Comparers.Get(SnapshotType.Ico)));
    }

    [Fact]
    public void AddSkiaSharpAfterImageSharp_ImageSharpValuesAreStillSerialized()
    {
        var settings = new SnapshotSettings();
        settings.AddImageSharp();
        settings.AddSkiaSharp();

        // The SkiaSharp serializer does not claim ImageSharp values, so the ImageSharp one still handles them
        using var image = new ImageSharpImage(2, 2);
        var snapshot = settings.Serializers.Serialize(SnapshotType.Png, image);

        var data = Assert.Single(snapshot.Data);
        Assert.Equal(SnapshotType.Png.FileExtension, data.Extension);
        Assert.Equal<byte>([0x89, (byte)'P', (byte)'N', (byte)'G'], data.Data.AsSpan(0, 4));
    }

    [Fact]
    public void AddSkiaSharpAfterImageSharp_BothColorConvertersAreUsable()
    {
        var settings = new SnapshotSettings();
        settings.AddImageSharp();
        settings.AddSkiaSharp();

        var value = new { ImageSharpColor = new Rgba32(0x01, 0x02, 0x03, 0x04), SkiaSharpColor = new SKColor(0x11, 0x22, 0x33, 0x44) };
        var snapshot = settings.Serializers.Serialize(SnapshotType.Default, value);
        var text = Encoding.UTF8.GetString(Assert.Single(snapshot.Data).Data);

        // Converters resolve first-registered-wins, but the two backends convert different types so neither shadows the other
        Assert.Contains("#01020304", text);
        Assert.Contains("#11223344", text);
    }

    private static Assembly[] GetBackendSerializers(SnapshotSettings settings)
        => [.. settings.Serializers.Select(GetBackend).Where(backend => backend == ImageSharpBackend || backend == SkiaSharpBackend)];

    private static Assembly GetBackend(object instance) => instance.GetType().Assembly;
}
