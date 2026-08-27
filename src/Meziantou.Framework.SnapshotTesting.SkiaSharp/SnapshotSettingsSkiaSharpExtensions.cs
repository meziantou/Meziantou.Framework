namespace Meziantou.Framework.SnapshotTesting.SkiaSharp;

/// <summary>
/// Provides extension methods for <see cref="SnapshotSettings"/> to enable SkiaSharp-based image snapshot testing.
/// </summary>
public static class SnapshotSettingsSkiaSharpExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        /// <summary>
        /// Registers SkiaSharp serializers, comparers, and converters on the <see cref="SnapshotSettings"/>.
        /// Supports <see cref="global::SkiaSharp.SKImage"/>, <see cref="global::SkiaSharp.SKBitmap"/>,
        /// <see cref="global::SkiaSharp.SKPixmap"/>, and <see cref="global::SkiaSharp.SKSurface"/> values
        /// serialized as PNG, JPEG, and WebP snapshots.
        /// </summary>
        /// <param name="settings">Optional image comparison settings. When <see langword="null"/>, exact pixel comparison is used.</param>
        /// <remarks>
        /// The serializer writes PNG, JPEG and WebP. The comparer is registered for more formats than that on
        /// purpose: it decodes through <c>SKCodec</c>, so it can also compare BMP, GIF and ICO snapshots that
        /// reached disk as a <see cref="byte" /> array rather than through this serializer.
        /// </remarks>
        public void AddSkiaSharp(ImageComparisonSettings? settings = null)
        {
            snapshotSettings.AddConverter(new SKColorHumanReadableConverter());
            snapshotSettings.Serializers.Add(new SkiaSharpSnapshotSerializer());

            var comparer = new SkiaSharpSnapshotComparer(settings);
            snapshotSettings.Comparers.Set(SnapshotType.Bmp, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Png, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Jpeg, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Webp, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Gif, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Ico, comparer);
        }
    }
}
