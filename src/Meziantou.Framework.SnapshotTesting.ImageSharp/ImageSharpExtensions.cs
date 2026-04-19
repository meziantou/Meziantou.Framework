using Meziantou.Framework.HumanReadable;
using Microsoft.VisualBasic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

public static class Extensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        public void AddImageSharp(ImageComparisonSettings? settings = null)
        {
            snapshotSettings.AddConverter(new Rgba32HumanReadableConverter());
            snapshotSettings.Serializers.Add(new ImageSnapshotSerializer());
        }
    }
}

public sealed class ImageComparisonSettings
{
    public float? SimilarityThreshold { get; set; }
}

internal sealed class ImageSharpSnapshotComparer(ImageComparisonSettings settings) : ISnapshotComparer
{
    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        var expectedImage = Image.Load(expected.Data);
        var actualImage = Image.Load(actual.Data);

        // TODO Check dimension
        // TODO Check similarity or exact image

    }
}

internal sealed class ImageSnapshotSerializer : ISnapshotSerializer
{
    public bool CanSerialize(SnapshotType type, object? value) => value is Image;
    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
    {
        if (value is not Image image)
            throw new ArgumentException("Value must be an Image.", nameof(value));

        using var ms = new MemoryStream();
        if (type == "png")
        {
            image.SaveAsPng(ms);
        }
        else if (type == "jpeg" || type == "jpg")
        {
            image.SaveAsJpeg(ms);
        }
        else if (type == "bmp")
        {
            image.SaveAsBmp(ms);
        }
        else if (type == "tiff")
        {
            image.SaveAsTiff(ms);
        }
        else if (type == "webp")
        {
            image.SaveAsWebp(ms);
        }

        return [new SnapshotData(type.FileExtension, ms.ToArray())];
    }
}


internal sealed class Rgba32HumanReadableConverter : HumanReadableConverter<Rgba32>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Rgba32 value, HumanReadableSerializerOptions options) => throw new NotImplementedException();
}