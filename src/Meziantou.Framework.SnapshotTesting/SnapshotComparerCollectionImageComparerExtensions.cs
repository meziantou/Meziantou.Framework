namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotComparerCollectionImageComparerExtensions
{
    extension(SnapshotComparerCollection comparers)
    {
        /// <summary>
        /// Registers the built-in image comparer for the formats it can decode: BMP, PNG, JPEG and TIFF.
        /// </summary>
        /// <remarks>
        /// Alternate spellings need no registration of their own: <see cref="SnapshotType.Create(string)" />
        /// resolves <c>jpg</c> to <see cref="SnapshotType.Jpeg" /> and <c>tif</c> to
        /// <see cref="SnapshotType.Tiff" />, and snapshot types compare by their canonical name.
        /// <para>
        /// GIF and ICO are absent on purpose. Their serializers store PNG frames, and comparers are resolved
        /// by the format a snapshot is stored in, so those snapshots already use the PNG registration.
        /// </para>
        /// </remarks>
        public void AddImageComparer(ImageComparisonSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(comparers);

            var comparer = settings is null ? ImageComparer.Instance : new ImageComparer(settings);
            comparers.Set(SnapshotType.Bmp, comparer);
            comparers.Set(SnapshotType.Png, comparer);
            comparers.Set(SnapshotType.Jpeg, comparer);
            comparers.Set(SnapshotType.Tiff, comparer);
        }
    }
}
