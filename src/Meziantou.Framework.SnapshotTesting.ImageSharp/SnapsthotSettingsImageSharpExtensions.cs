namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

public static class SnapsthotSettingsImageSharpExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
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
