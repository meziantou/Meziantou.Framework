namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>Provides extension methods for <see cref="SnapshotSerializerCollection"/> to snapshot GIF and ICO files frame by frame.</summary>
public static class SnapshotSerializerCollectionImagingExtensions
{
    extension(SnapshotSerializerCollection serializers)
    {
        /// <summary>
        /// Registers a serializer that writes the bytes of a GIF file, validated as <see cref="SnapshotType.Gif"/>, as one PNG
        /// snapshot per frame. Register a comparer for <see cref="SnapshotType.Png"/>, such as
        /// <see cref="SnapshotComparerCollectionImageComparerExtensions.AddImageComparer"/>, to compare the frames by pixels.
        /// </summary>
        public void AddGifSerializer() => Add(serializers, FrameSnapshotSerializer.Gif);

        /// <summary>
        /// Registers a serializer that writes the bytes of an ICO file, validated as <see cref="SnapshotType.Ico"/>, as one PNG
        /// snapshot per representation. Register a comparer for <see cref="SnapshotType.Png"/>, such as
        /// <see cref="SnapshotComparerCollectionImageComparerExtensions.AddImageComparer"/>, to compare the representations by pixels.
        /// </summary>
        public void AddIcoSerializer() => Add(serializers, FrameSnapshotSerializer.Ico);
    }

    private static void Add(SnapshotSerializerCollection serializers, FrameSnapshotSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializers);

        if (serializers.Any(existing => existing == serializer))
            return;

        serializers.Add(serializer);
    }
}
