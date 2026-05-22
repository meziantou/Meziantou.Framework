namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotComparerCollectionImageComparerExtensions
{
    extension(SnapshotComparerCollection comparers)
    {
        public void AddImageComparer(ImageComparisonSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(comparers);

            comparers.Set(SnapshotType.Bmp, settings is null ? ImageComparer.Instance : new ImageComparer(settings));
        }
    }
}
