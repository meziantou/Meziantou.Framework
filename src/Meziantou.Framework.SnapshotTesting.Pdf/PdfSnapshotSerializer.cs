using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.SnapshotTesting.Pdf;

/// <summary>
/// Serializes a PDF document, as a <see cref="PdfDocument"/> or as the bytes of a file (<see cref="byte"/>[] or
/// <see cref="Stream"/>) validated as <see cref="SnapshotType.Pdf"/>, to a JSON file of its metadata followed by one PNG
/// snapshot per page. Data that is not a PDF document is stored as is.
/// </summary>
internal sealed class PdfSnapshotSerializer(PdfSnapshotSettings? settings) : ISnapshotSerializer
{
    // Only the pixels: the PNG files have no metadata that could change between runs
    private static readonly PngEncoder Encoder = new() { MetadataHandling = MetadataHandling.Strip };

    private readonly PdfSnapshotSettings _settings = settings ?? new PdfSnapshotSettings();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        result = null;
        if (type != SnapshotType.Pdf)
            return false;

        switch (value)
        {
            case PdfDocument document:
                result = Serialize(document);
                return true;

            case byte[] data:
                result = Serialize(data);
                return true;

            case Stream stream:
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    result = Serialize(buffer.ToArray());
                }

                return true;

            default:
                return false;
        }
    }

    private SerializedSnapshot Serialize(byte[] data)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(data, new PdfReadOptions { Password = _settings.Password });
        }
        catch (InvalidPdfContentException)
        {
            // Not a PDF document: the snapshot is the data, as without this serializer
            return new SerializedSnapshot([new SnapshotData(SnapshotType.Pdf.Type, data)]);
        }

        return Serialize(document);
    }

    private SerializedSnapshot Serialize(PdfDocument document)
    {
        var renderOptions = new PdfRenderOptions
        {
            Dpi = _settings.Dpi,
            RenderAnnotations = _settings.RenderAnnotations,
            UseSystemFonts = _settings.UseSystemFonts,
        };

        // The metadata comes first, so the file of page n has the index n
        var snapshots = new List<SnapshotData>(document.Pages.Count + 1)
        {
            new("json", PdfMetadataWriter.Write(document, _settings.Metadata)),
        };

        foreach (var page in document.Pages)
        {
            using var image = PdfRenderer.RenderPage(page, renderOptions);
            using var stream = new MemoryStream();
            image.Save(stream, Encoder);
            snapshots.Add(new SnapshotData(SnapshotType.Png.Type, stream.ToArray()));
        }

        return new SerializedSnapshot(snapshots);
    }
}
