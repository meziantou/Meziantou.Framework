using Meziantou.Framework.Pdf.Metadata;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    /// <summary>Gets the XMP metadata of the document (ISO 32000-2, 14.3.2): the <c>/Metadata</c> stream of the catalog.</summary>
    /// <returns>The XMP packet, or <see langword="null"/> when the document has none.</returns>
    public byte[]? GetXmpMetadata() => Catalog.Get<PdfStream>(PdfNames.Metadata)?.GetDecodedData();

    /// <summary>Sets the XMP metadata of the document (ISO 32000-2, 14.3.2): the <c>/Metadata</c> stream of the catalog, written without compression.</summary>
    /// <param name="packet">The XMP packet.</param>
    public void SetXmpMetadata(ReadOnlySpan<byte> packet)
    {
        var stream = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Metadata,
            [PdfNames.Subtype] = PdfName.Create("XML"),
        });
        stream.SetData(packet.ToArray(), compressible: false);
        Catalog[PdfNames.Metadata] = Objects.Add(stream);
    }

    /// <summary>Removes the XMP metadata of the document.</summary>
    public void RemoveXmpMetadata() => Catalog.Remove(PdfNames.Metadata);

    /// <summary>Replaces the XMP metadata with a packet built from <see cref="Information"/>: title, author, subject, keywords, producer, creator tool and dates.</summary>
    public void UpdateXmpMetadata() => SetXmpMetadata(XmpPacketWriter.Write(Information, GetConformanceXmpProperties()));

    private static IReadOnlyList<XmpProperty> GetConformanceXmpProperties() => [];
}
