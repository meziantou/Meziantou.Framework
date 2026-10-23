using System.Globalization;
using System.Text;
using System.Xml;

namespace Meziantou.Framework.Pdf.Metadata;

/// <summary>
/// Writes the XMP packet of a document (ISO 32000-2, 14.3.2; ISO 16684-1) from its information dictionary: the
/// Dublin Core, PDF and XMP basic schemas, and the extra properties of the conformance levels.
/// </summary>
internal static class XmpPacketWriter
{
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string PdfNamespace = "http://ns.adobe.com/pdf/1.3/";
    private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";

    public static byte[] Write(PdfDocumentInformation information, IReadOnlyList<XmpProperty> extraProperties)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = " ",
            NewLineChars = "\n",
            OmitXmlDeclaration = true,
        };

        using var output = new MemoryStream();
        output.Write("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n"u8);
        using (var writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement("x", "xmpmeta", "adobe:ns:meta/");
            writer.WriteStartElement("rdf", "RDF", RdfNamespace);
            writer.WriteStartElement("rdf", "Description", RdfNamespace);
            writer.WriteAttributeString("rdf", "about", RdfNamespace, "");
            writer.WriteAttributeString("xmlns", "dc", null, DublinCoreNamespace);
            writer.WriteAttributeString("xmlns", "pdf", null, PdfNamespace);
            writer.WriteAttributeString("xmlns", "xmp", null, XmpNamespace);
            foreach (var group in extraProperties.GroupBy(p => (p.Prefix, p.Namespace)))
            {
                writer.WriteAttributeString("xmlns", group.Key.Prefix, null, group.Key.Namespace);
            }

            writer.WriteElementString("dc", "format", DublinCoreNamespace, "application/pdf");
            if (information.Title is { } title)
            {
                WriteLanguageAlternative(writer, "title", title);
            }

            if (information.Author is { } author)
            {
                writer.WriteStartElement("dc", "creator", DublinCoreNamespace);
                writer.WriteStartElement("rdf", "Seq", RdfNamespace);
                writer.WriteElementString("rdf", "li", RdfNamespace, author);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            if (information.Subject is { } subject)
            {
                WriteLanguageAlternative(writer, "description", subject);
            }

            if (information.Keywords is { } keywords)
            {
                writer.WriteElementString("pdf", "Keywords", PdfNamespace, keywords);
            }

            if (information.Producer is { } producer)
            {
                writer.WriteElementString("pdf", "Producer", PdfNamespace, producer);
            }

            if (information.Creator is { } creator)
            {
                writer.WriteElementString("xmp", "CreatorTool", XmpNamespace, creator);
            }

            if (information.CreationDate is { } creationDate)
            {
                writer.WriteElementString("xmp", "CreateDate", XmpNamespace, FormatDate(creationDate));
            }

            if (information.ModificationDate is { } modificationDate)
            {
                writer.WriteElementString("xmp", "ModifyDate", XmpNamespace, FormatDate(modificationDate));
            }

            foreach (var property in extraProperties)
            {
                writer.WriteElementString(property.Prefix, property.Name, property.Namespace, property.Value);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        output.Write("\n<?xpacket end=\"w\"?>"u8);
        return output.ToArray();
    }

    public static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static void WriteLanguageAlternative(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement("dc", name, DublinCoreNamespace);
        writer.WriteStartElement("rdf", "Alt", RdfNamespace);
        writer.WriteStartElement("rdf", "li", RdfNamespace);
        writer.WriteAttributeString("xml", "lang", null, "x-default");
        writer.WriteString(value);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
