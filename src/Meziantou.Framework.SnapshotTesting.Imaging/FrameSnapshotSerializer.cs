using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>
/// Serializes the bytes of a GIF or ICO file as one PNG snapshot per frame (GIF) or per representation (ICO), so that a
/// change shows which frame differs and every frame can be reviewed as an image. Data that is not a decodable file of the
/// format is left to the next serializer.
/// </summary>
internal sealed class FrameSnapshotSerializer : ISnapshotSerializer
{
    // Only the pixels: a frame snapshot has no animation timing or metadata
    private static readonly PngEncoder Encoder = new() { MetadataHandling = MetadataHandling.Strip };

    private FrameSnapshotSerializer(SnapshotType type) => Type = type;

    public static FrameSnapshotSerializer Gif { get; } = new(SnapshotType.Gif);

    public static FrameSnapshotSerializer Ico { get; } = new(SnapshotType.Ico);

    public SnapshotType Type { get; }

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        result = null;
        if (type != Type || value is not byte[] data)
            return false;

        var expectedFormat = Type == SnapshotType.Gif ? ImageFormat.Gif : ImageFormat.Ico;
        if (Image.DetectFormat(data.AsSpan(0, Math.Min(data.Length, Image.FormatDetectionPrefixLength))) != expectedFormat)
            return false;

        using var decoded = TryLoad(data);
        if (decoded is null)
            return false;

        var snapshotData = new List<SnapshotData>(decoded.Frames.Count);
        foreach (var image in decoded.Images)
        {
            for (var i = 0; i < image.Frames.Count; i++)
            {
                snapshotData.Add(new SnapshotData(SnapshotType.Png.FileExtension, EncodeFrame(image, i)));
            }
        }

        result = new SerializedSnapshot(snapshotData);
        return true;
    }

    private static DecodedSnapshot? TryLoad(byte[] data)
    {
        try
        {
            return DecodedSnapshot.Load(data);
        }
        catch (Exception ex) when (ex is ImageException or NotSupportedException)
        {
            return null;
        }
    }

    private static byte[] EncodeFrame(Image image, int index)
    {
        using var frame = image.CloneFrame(index);
        using var stream = new MemoryStream();
        frame.Save(stream, Encoder);
        return stream.ToArray();
    }
}
