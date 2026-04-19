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
            snapshotSettings.SetSnapshotComparer(SnapshotType.Bmp, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Png, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Jpeg, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Tiff, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Webp, comparer);
        }
    }
}
