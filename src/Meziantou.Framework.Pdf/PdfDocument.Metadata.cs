using Meziantou.Framework.Pdf.Metadata;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    private static readonly PdfName XmlName = PdfName.Create("XML");

    /// <summary>Gets the XMP metadata of the document (ISO 32000-2, 14.3.2): the <c>/Metadata</c> stream of the catalog.</summary>
    /// <returns>The XMP packet, or <see langword="null"/> when the document has none.</returns>
    public byte[]? GetXmpMetadata() => Catalog.Get<PdfStream>(PdfNames.Metadata)?.GetDecodedData();

    /// <summary>
    /// Sets the XMP metadata of the document (ISO 32000-2, 14.3.2): the <c>/Metadata</c> stream of the catalog, written
    /// without compression. The existing stream is updated, and left unchanged when it already has the packet, so that
    /// an incremental save writes no new object.
    /// </summary>
    /// <param name="packet">The XMP packet.</param>
    public void SetXmpMetadata(ReadOnlySpan<byte> packet)
    {
        if (Catalog[PdfNames.Metadata] is PdfReference && Catalog.Get<PdfStream>(PdfNames.Metadata) is { } existing)
        {
            if (HasMetadata(existing, packet))
                return;

            existing.Dictionary.Remove(PdfNames.Filter);
            existing.Dictionary.Remove(PdfNames.DecodeParms);
            existing.Dictionary.Remove(PdfNames.DL);
            existing.Dictionary[PdfNames.Type] = PdfNames.Metadata;
            existing.Dictionary[PdfNames.Subtype] = XmlName;
            existing.SetData(packet.ToArray(), compressible: false);
            return;
        }

        var stream = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Metadata,
            [PdfNames.Subtype] = XmlName,
        });
        stream.SetData(packet.ToArray(), compressible: false);
        Catalog[PdfNames.Metadata] = Objects.Add(stream);
    }

    /// <summary>Removes the XMP metadata of the document.</summary>
    public void RemoveXmpMetadata() => Catalog.Remove(PdfNames.Metadata);

    /// <summary>
    /// Updates the XMP metadata with the values of <see cref="Information"/>: title, author, subject, keywords,
    /// producer, creator tool, dates and trapping. The other properties of the existing packet, such as custom
    /// schemas, are kept.
    /// </summary>
    public void UpdateXmpMetadata() => UpdateXmpMetadata(padding: 0);

    /// <summary>
    /// Updates the XMP metadata with the values of <see cref="Information"/>, and reserves whitespace in the packet so
    /// that an editor can update it in place. The other properties of the existing packet, such as custom schemas, are kept.
    /// </summary>
    /// <param name="padding">The number of whitespace bytes before the end of the packet; 2048 to 4096 are common values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="padding"/> is negative.</exception>
    public void UpdateXmpMetadata(int padding)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        SetXmpMetadata(XmpPacketWriter.Write(Information, [], existingPacket: TryGetXmpMetadata(), padding: padding));
    }

    /// <summary>Gets the XMP metadata, or <see langword="null"/> when the document has none or its stream cannot be decoded.</summary>
    internal byte[]? TryGetXmpMetadata()
    {
        try
        {
            return GetXmpMetadata();
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private static bool HasMetadata(PdfStream stream, ReadOnlySpan<byte> packet)
    {
        if (stream.Dictionary.ContainsKey(PdfNames.Filter) || stream.Dictionary.GetName(PdfNames.Type) != PdfNames.Metadata || stream.Dictionary.GetName(PdfNames.Subtype) != XmlName)
            return false;

        try
        {
            return stream.GetDecodedData().AsSpan().SequenceEqual(packet);
        }
        catch (PdfException)
        {
            return false;
        }
    }
}
