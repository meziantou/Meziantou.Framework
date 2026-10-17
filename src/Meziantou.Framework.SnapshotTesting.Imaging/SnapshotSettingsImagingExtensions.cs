namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>
/// Provides extension methods for <see cref="SnapshotSettings"/> to enable image snapshot testing based on
/// <c>Meziantou.Framework.Imaging</c>.
/// </summary>
public static class SnapshotSettingsImagingExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        /// <summary>
        /// Registers the serializer of <see cref="Meziantou.Framework.Imaging.Image"/> values (PNG, JPEG, BMP, TIFF, WebP, GIF,
        /// ICO, CUR, ANI, QOI, TGA, PNM, AVIF, HEIC, JPEG XL and JPEG 2000 snapshots), the <see cref="ImageComparer"/> for every format the library decodes (see
        /// <see cref="SnapshotComparerCollectionImageComparerExtensions.AddImageComparer"/>), and a converter that writes
        /// <see cref="Meziantou.Framework.Imaging.Rgba32"/> colors as <c>#RRGGBBAA</c> in human-readable snapshots.
        /// </summary>
        /// <param name="settings">Optional image comparison settings. When <see langword="null"/>, exact pixel comparison is used.</param>
        public void AddImaging(ImageComparisonSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(snapshotSettings);

            snapshotSettings.AddConverter(new Rgba32HumanReadableConverter());
            snapshotSettings.Serializers.Add(new ImageSnapshotSerializer());
            snapshotSettings.Comparers.AddImageComparer(settings);
        }
    }
}
