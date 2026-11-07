using System.Globalization;
using System.Xml;

namespace Meziantou.Framework.Pdf.Metadata;

/// <summary>Reads the PDF/A identification of an XMP packet (ISO 19005-2, 6.6.4): the <c>pdfaid:part</c> property.</summary>
internal static class XmpIdentification
{
    private const string PdfAIdNamespace = "http://www.aiim.org/pdfa/ns/id/";

    /// <summary>Gets the part of ISO 19005 a packet claims, or <see langword="null"/> when it claims none or is not well-formed.</summary>
    public static int? ReadPdfAPart(byte[] packet)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };
        try
        {
            using var stream = new MemoryStream(packet, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                // The property is an attribute of rdf:Description (the abbreviated form) or an element
                if (reader.LocalName == "part" && reader.NamespaceURI == PdfAIdNamespace)
                    return Parse(reader.ReadElementContentAsString());

                if (reader.HasAttributes && reader.GetAttribute("part", PdfAIdNamespace) is { } attribute)
                    return Parse(attribute);
            }
        }
        catch (XmlException)
        {
        }

        return null;
    }

    private static int? Parse(string text) => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var part) ? part : null;
}
