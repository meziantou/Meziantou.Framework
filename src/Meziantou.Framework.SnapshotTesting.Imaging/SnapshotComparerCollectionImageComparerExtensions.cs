namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>Provides extension methods for <see cref="SnapshotComparerCollection"/> to compare image snapshots by their pixels.</summary>
public static class SnapshotComparerCollectionImageComparerExtensions
{
    extension(SnapshotComparerCollection comparers)
    {
        /// <summary>
        /// Registers the <see cref="ImageComparer"/> for BMP, PNG, JPEG, TIFF, WebP, GIF and ICO snapshots. The PNG comparer also
        /// compares the frames that <c>AddGifSerializer</c> and <c>AddIcoSerializer</c> write.
        /// </summary>
        /// <param name="settings">Optional image comparison settings. When <see langword="null"/>, exact pixel comparison is used.</param>
        public void AddImageComparer(ImageComparisonSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(comparers);

            var comparer = settings is null ? ImageComparer.Instance : new ImageComparer(settings);
            comparers.Set(SnapshotType.Bmp, comparer);
            comparers.Set(SnapshotType.Png, comparer);
            comparers.Set(SnapshotType.Jpeg, comparer);
            comparers.Set(SnapshotType.Tiff, comparer);
            comparers.Set(SnapshotType.Webp, comparer);
            comparers.Set(SnapshotType.Gif, comparer);
            comparers.Set(SnapshotType.Ico, comparer);
        }
    }
}
