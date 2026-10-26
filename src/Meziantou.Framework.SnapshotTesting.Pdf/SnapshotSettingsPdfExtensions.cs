using Meziantou.Framework.SnapshotTesting.Imaging;

namespace Meziantou.Framework.SnapshotTesting.Pdf;

/// <summary>
/// Provides extension methods for <see cref="SnapshotSettings"/> to enable PDF snapshot testing based on
/// <c>Meziantou.Framework.Pdf</c>.
/// </summary>
public static class SnapshotSettingsPdfExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        /// <summary>
        /// Registers the serializer of PDF documents (see <see cref="SnapshotSerializerCollectionPdfExtensions.AddPdfSerializer"/>)
        /// and an <see cref="ImageComparer"/> for <see cref="SnapshotType.Png"/>, so the pages are compared by pixels.
        /// </summary>
        /// <param name="settings">The rendering and metadata settings. When <see langword="null"/>, the default settings are used.</param>
        /// <param name="imageComparisonSettings">
        /// The comparison settings of the pages. When <see langword="null"/>, a comparer already registered for
        /// <see cref="SnapshotType.Png"/>, for instance by <c>AddImaging</c>, is kept, and exact pixel comparison is used otherwise.
        /// </param>
        public void AddPdf(PdfSnapshotSettings? settings = null, ImageComparisonSettings? imageComparisonSettings = null)
        {
            ArgumentNullException.ThrowIfNull(snapshotSettings);

            snapshotSettings.Serializers.AddPdfSerializer(settings);
            if (imageComparisonSettings is not null || !snapshotSettings.Comparers.Any(entry => entry.Key == SnapshotType.Png))
            {
                snapshotSettings.Comparers.Set(SnapshotType.Png, new ImageComparer(imageComparisonSettings));
            }
        }
    }
}
