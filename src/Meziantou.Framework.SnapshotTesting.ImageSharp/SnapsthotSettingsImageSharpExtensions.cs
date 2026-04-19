namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

/// <summary>
/// Provides extension methods for <see cref="SnapshotSettings"/> to enable ImageSharp-based image snapshot testing.
/// </summary>
public static class SnapsthotSettingsImageSharpExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        /// <summary>
        /// Registers ImageSharp serializers, comparers, and converters on the <see cref="SnapshotSettings"/>.
        /// Supports <see cref="SixLabors.ImageSharp.Image"/> values serialized as PNG, JPEG, BMP, TIFF, and WebP snapshots.
        /// </summary>
        /// <param name="settings">Optional image comparison settings. When <see langword="null"/>, exact pixel comparison is used.</param>
        public void AddImageSharp(ImageComparisonSettings? settings = null)
        {
            snapshotSettings.AddConverter(new Rgba32HumanReadableConverter());
            snapshotSettings.Serializers.Add(new ImageSnapshotSerializer());

            var comparer = new ImageSharpSnapshotComparer(settings);
            snapshotSettings.Comparers.Set(SnapshotType.Bmp, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Png, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Jpeg, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Tiff, comparer);
            snapshotSettings.Comparers.Set(SnapshotType.Webp, comparer);
        }
    }
}
