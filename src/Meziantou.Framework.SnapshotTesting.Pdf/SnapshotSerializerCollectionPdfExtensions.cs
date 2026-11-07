namespace Meziantou.Framework.SnapshotTesting.Pdf;

/// <summary>Provides extension methods for <see cref="SnapshotSerializerCollection"/> to snapshot PDF documents page by page.</summary>
public static class SnapshotSerializerCollectionPdfExtensions
{
    extension(SnapshotSerializerCollection serializers)
    {
        /// <summary>
        /// Registers a serializer that writes a PDF document, a <see cref="Meziantou.Framework.Pdf.PdfDocument"/> or the bytes
        /// of a file (<see cref="byte"/>[] or <see cref="Stream"/>) validated as <see cref="SnapshotType.Pdf"/>, as a JSON file
        /// of its metadata followed by one PNG snapshot per page. Register a comparer for <see cref="SnapshotType.Png"/>,
        /// such as <see cref="Imaging.SnapshotComparerCollectionImageComparerExtensions.AddImageComparer"/>, to compare the
        /// pages by pixels.
        /// </summary>
        /// <param name="settings">The rendering and metadata settings. When <see langword="null"/>, the default settings are used.</param>
        public void AddPdfSerializer(PdfSnapshotSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(serializers);

            serializers.Add(new PdfSnapshotSerializer(settings));
        }
    }
}
