namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotComparerCollectionImageComparerExtensions
{
    extension(SnapshotComparerCollection comparers)
    {
        public void AddImageComparer(ImageComparisonSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(comparers);

            var comparer = settings is null ? ImageComparer.Instance : new ImageComparer(settings);
            comparers.Set(SnapshotType.Bmp, comparer);
            comparers.Set(SnapshotType.Png, comparer);
            comparers.Set(SnapshotType.Jpeg, comparer);
            comparers.Set(SnapshotType.Create("jpg"), comparer);
        }
    }
}
