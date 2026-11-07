using System.Text.Encodings.Web;
using System.Text.Json;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.SnapshotTesting.Pdf;

/// <summary>Writes the metadata of a PDF document as indented JSON, with a fixed property order so the file is stable.</summary>
internal static class PdfMetadataWriter
{
    // The file is read by people reviewing a snapshot, not embedded in HTML: accented letters stay readable
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] Write(PdfDocument document, PdfSnapshotMetadata metadata)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("version", document.Version.ToString());
            writer.WriteNumber("pageCount", document.Pages.Count);

            if (metadata.HasFlag(PdfSnapshotMetadata.DocumentInformation))
            {
                WriteDocumentInformation(writer, document);
            }

            if (metadata.HasFlag(PdfSnapshotMetadata.Dates))
            {
                WriteDate(writer, "creationDate", document.Information.CreationDate);
                WriteDate(writer, "modificationDate", document.Information.ModificationDate);
            }

            if (metadata.HasFlag(PdfSnapshotMetadata.Outlines))
            {
                writer.WritePropertyName("outlines");
                WriteOutlines(writer, document.Outlines);
            }

            writer.WriteStartArray("pages");
            for (var i = 0; i < document.Pages.Count; i++)
            {
                WritePage(writer, document.Pages[i], i + 1, metadata);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        // A text file ends with a new line
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteDocumentInformation(Utf8JsonWriter writer, PdfDocument document)
    {
        var information = document.Information;
        writer.WriteStartObject("information");
        WriteString(writer, "title", information.Title);
        WriteString(writer, "author", information.Author);
        WriteString(writer, "subject", information.Subject);
        WriteString(writer, "keywords", information.Keywords);
        WriteString(writer, "creator", information.Creator);
        WriteString(writer, "producer", information.Producer);
        WriteString(writer, "language", document.Language);
        if (document.PdfAConformance is not PdfAConformance.None)
        {
            writer.WriteString("pdfAConformance", document.PdfAConformance.ToString());
        }

        if (document.PdfUAConformance is not PdfUAConformance.None)
        {
            writer.WriteString("pdfUAConformance", document.PdfUAConformance.ToString());
        }

        writer.WriteBoolean("isEncrypted", document.IsEncrypted);
        writer.WriteEndObject();
    }

    private static void WriteOutlines(Utf8JsonWriter writer, PdfOutlineCollection outlines)
    {
        writer.WriteStartArray();
        foreach (var item in outlines)
        {
            writer.WriteStartObject();
            writer.WriteString("title", item.Title);
            if (item.Children.Count > 0)
            {
                writer.WritePropertyName("children");
                WriteOutlines(writer, item.Children);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePage(Utf8JsonWriter writer, PdfPage page, int number, PdfSnapshotMetadata metadata)
    {
        writer.WriteStartObject();
        writer.WriteNumber("number", number);

        // The crop box is the area that is rendered
        var cropBox = page.CropBox;
        writer.WriteNumber("width", Round(cropBox.Width));
        writer.WriteNumber("height", Round(cropBox.Height));
        writer.WriteNumber("rotation", page.Rotation);
        if (page.MediaBox != cropBox)
        {
            writer.WritePropertyName("mediaBox");
            WriteRectangle(writer, page.MediaBox);
        }

        if (metadata.HasFlag(PdfSnapshotMetadata.Text))
        {
            // One entry per line, so a change shows as a changed line in the diff
            writer.WriteStartArray("text");
            var text = page.ExtractText().ReplaceLineEndings("\n").TrimEnd('\n');
            if (text.Length > 0)
            {
                foreach (var line in text.Split('\n'))
                {
                    writer.WriteStringValue(line);
                }
            }

            writer.WriteEndArray();
        }

        if (metadata.HasFlag(PdfSnapshotMetadata.Annotations))
        {
            writer.WriteStartArray("annotations");
            foreach (var annotation in page.Annotations)
            {
                writer.WriteStartObject();
                writer.WriteString("subtype", annotation.Subtype);
                writer.WritePropertyName("rectangle");
                WriteRectangle(writer, annotation.Rectangle);
                WriteString(writer, "contents", annotation.Contents);
                if (annotation is PdfLinkAnnotation { Uri: { } uri })
                {
                    writer.WriteString("uri", uri.OriginalString);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteRectangle(Utf8JsonWriter writer, PdfRectangle rectangle)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Round(rectangle.Left));
        writer.WriteNumberValue(Round(rectangle.Bottom));
        writer.WriteNumberValue(Round(rectangle.Right));
        writer.WriteNumberValue(Round(rectangle.Top));
        writer.WriteEndArray();
    }

    private static void WriteString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteDate(Utf8JsonWriter writer, string propertyName, DateTimeOffset? value)
    {
        if (value is { } date)
        {
            writer.WriteString(propertyName, date);
        }
    }

    // Points are 1/72 inch: a thousandth is far below a pixel, and hides the rounding of the millimeter page sizes
    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
