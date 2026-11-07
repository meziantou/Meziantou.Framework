using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Meziantou.Framework.Pdf.Metadata;

/// <summary>
/// Writes the XMP packet of a document (ISO 32000-2, 14.3.2; ISO 16684-1) from its information dictionary: the
/// Dublin Core, PDF and XMP basic schemas, and the extra properties of the conformance levels. The other properties
/// of the existing packet, such as custom schemas and their PDF/A extension schemas, are kept.
/// </summary>
internal static class XmpPacketWriter
{
    private const string MetaNamespace = "adobe:ns:meta/";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string PdfNamespace = "http://ns.adobe.com/pdf/1.3/";
    private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";
    private const string PdfUAIdNamespace = "http://www.aiim.org/pdfua/ns/id/";
    private const string PdfAExtensionNamespace = "http://www.aiim.org/pdfa/ns/extension/";
    private const string PdfASchemaNamespace = "http://www.aiim.org/pdfa/ns/schema#";
    private const string PdfAPropertyNamespace = "http://www.aiim.org/pdfa/ns/property#";
    private const string PdfATypeNamespace = "http://www.aiim.org/pdfa/ns/type#";
    private const string PdfAFieldNamespace = "http://www.aiim.org/pdfa/ns/field#";

    private static readonly XNamespace Rdf = RdfNamespace;
    private static readonly XNamespace DublinCore = DublinCoreNamespace;
    private static readonly XNamespace Pdf = PdfNamespace;
    private static readonly XNamespace Xmp = XmpNamespace;
    private static readonly XNamespace PdfAExtension = PdfAExtensionNamespace;
    private static readonly XNamespace PdfASchema = PdfASchemaNamespace;
    private static readonly XNamespace PdfAProperty = PdfAPropertyNamespace;

    /// <summary>Writes the packet.</summary>
    /// <param name="information">The document information.</param>
    /// <param name="extraProperties">The properties of the conformance levels.</param>
    /// <param name="describePdfUA">
    /// Whether the PDF/UA identification is described by an extension schema (ISO 19005-2, 6.6.2.3): PDF/A-2 and PDF/A-3
    /// only know the schemas of XMP 2005.
    /// </param>
    /// <param name="existingPacket">
    /// The current packet of the document, or <see langword="null"/>: its properties that the information and the extra
    /// properties do not set are kept.
    /// </param>
    /// <param name="replacedNamespaces">The namespaces whose properties of the existing packet are removed, such as the PDF/A identification.</param>
    /// <param name="padding">The number of whitespace bytes before the end of the packet, which lets an editor update the packet in place.</param>
    /// <returns>The packet.</returns>
    public static byte[] Write(PdfDocumentInformation information, IReadOnlyList<XmpProperty> extraProperties, bool describePdfUA = false, byte[]? existingPacket = null, IReadOnlyCollection<string>? replacedNamespaces = null, int padding = 0)
    {
        var description = new XElement(Rdf + "Description", new XAttribute(Rdf + "about", ""));
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        void Declare(string prefix, string ns)
        {
            if (prefix.Length > 0 && prefixes.TryAdd(prefix, ns))
            {
                description.Add(new XAttribute(XNamespace.Xmlns + prefix, ns));
            }
        }

        Declare("dc", DublinCoreNamespace);
        Declare("pdf", PdfNamespace);
        Declare("xmp", XmpNamespace);
        foreach (var property in extraProperties)
        {
            Declare(property.Prefix, property.Namespace);
        }

        // The properties the information sets: the existing values are replaced, even when the information has none,
        // so that the packet and the information dictionary agree (ISO 19005-2, 6.6.2.1)
        var managed = new HashSet<XName>
        {
            DublinCore + "format",
            DublinCore + "title",
            DublinCore + "creator",
            DublinCore + "description",
            Pdf + "Keywords",
            Pdf + "Producer",
            Xmp + "CreatorTool",
            Xmp + "CreateDate",
            Xmp + "ModifyDate",
        };
        if (information.Trapped is not null)
        {
            managed.Add(Pdf + "Trapped");
        }

        var replaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in extraProperties)
        {
            replaced.Add(property.Namespace);
        }

        if (replacedNamespaces is not null)
        {
            replaced.UnionWith(replacedNamespaces);
        }

        bool IsKept(XName name) => !managed.Contains(name) && !replaced.Contains(name.NamespaceName);

        var keptProperties = new List<XElement>();
        var extensionSchemas = new List<XElement>();
        if (existingPacket is not null && Parse(existingPacket) is { } existing)
        {
            foreach (var existingDescription in existing.Descendants(Rdf + "Description").Where(element => element.Parent?.Name == Rdf + "RDF"))
            {
                foreach (var attribute in existingDescription.AncestorsAndSelf().SelectMany(element => element.Attributes()).Where(attribute => attribute.IsNamespaceDeclaration))
                {
                    if (attribute.Value is not (RdfNamespace or MetaNamespace))
                    {
                        Declare(attribute.Name.LocalName, attribute.Value);
                    }
                }

                // The abbreviated form: the simple properties are attributes of rdf:Description
                foreach (var attribute in existingDescription.Attributes())
                {
                    if (!attribute.IsNamespaceDeclaration && attribute.Name.Namespace != XNamespace.None && attribute.Name.Namespace != Rdf && attribute.Name.Namespace != XNamespace.Xml && IsKept(attribute.Name))
                    {
                        keptProperties.Add(new XElement(attribute.Name, attribute.Value));
                    }
                }

                foreach (var element in existingDescription.Elements())
                {
                    if (element.Name == PdfAExtension + "schemas")
                    {
                        // The extension schemas of the custom properties are merged with the one the packet describes
                        foreach (var schema in element.Elements(Rdf + "Bag").Elements(Rdf + "li"))
                        {
                            if (describePdfUA && (string?)schema.Element(PdfASchema + "namespaceURI") == PdfUAIdNamespace)
                                continue;

                            extensionSchemas.Add(new XElement(schema));
                        }
                    }
                    else if (IsKept(element.Name))
                    {
                        keptProperties.Add(new XElement(element));
                    }
                }
            }
        }

        if (describePdfUA)
        {
            extensionSchemas.Add(CreatePdfUAExtensionSchema());
        }

        if (extensionSchemas.Count > 0)
        {
            // ISO 19005-2, 6.6.2.3.3: the prefixes of the extension schema container schema are required
            Declare("pdfaExtension", PdfAExtensionNamespace);
            Declare("pdfaSchema", PdfASchemaNamespace);
            Declare("pdfaProperty", PdfAPropertyNamespace);
            Declare("pdfaType", PdfATypeNamespace);
            Declare("pdfaField", PdfAFieldNamespace);
        }

        description.Add(new XElement(DublinCore + "format", "application/pdf"));
        if (information.Title is { } title)
        {
            description.Add(CreateLanguageAlternative("title", title));
        }

        if (information.Author is { } author)
        {
            description.Add(new XElement(DublinCore + "creator", new XElement(Rdf + "Seq", new XElement(Rdf + "li", Sanitize(author)))));
        }

        if (information.Subject is { } subject)
        {
            description.Add(CreateLanguageAlternative("description", subject));
        }

        if (information.Keywords is { } keywords)
        {
            description.Add(new XElement(Pdf + "Keywords", Sanitize(keywords)));
        }

        if (information.Producer is { } producer)
        {
            description.Add(new XElement(Pdf + "Producer", Sanitize(producer)));
        }

        if (information.Trapped is { } trapped)
        {
            description.Add(new XElement(Pdf + "Trapped", PdfDocumentInformation.GetTrappedName(trapped)));
        }

        if (information.Creator is { } creator)
        {
            description.Add(new XElement(Xmp + "CreatorTool", Sanitize(creator)));
        }

        if (information.CreationDate is { } creationDate)
        {
            description.Add(new XElement(Xmp + "CreateDate", FormatDate(creationDate)));
        }

        if (information.ModificationDate is { } modificationDate)
        {
            description.Add(new XElement(Xmp + "ModifyDate", FormatDate(modificationDate)));
        }

        foreach (var property in extraProperties)
        {
            description.Add(new XElement(XNamespace.Get(property.Namespace) + property.Name, Sanitize(property.Value)));
        }

        description.Add(keptProperties);
        if (extensionSchemas.Count > 0)
        {
            description.Add(new XElement(PdfAExtension + "schemas", new XElement(Rdf + "Bag", extensionSchemas)));
        }

        var root = new XElement(
            XNamespace.Get(MetaNamespace) + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", MetaNamespace),
            new XElement(Rdf + "RDF", new XAttribute(XNamespace.Xmlns + "rdf", RdfNamespace), description));

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
            root.WriteTo(writer);
        }

        output.Write("\n"u8);
        WritePadding(output, padding);
        output.Write("<?xpacket end=\"w\"?>"u8);
        return output.ToArray();
    }

    public static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>
    /// Replaces the characters XML 1.0 does not allow (the control characters other than tab, line feed and carriage
    /// return, the lone surrogates, U+FFFE and U+FFFF) by U+FFFD, the replacement character.
    /// </summary>
    internal static string Sanitize(string value)
    {
        var index = IndexOfInvalidCharacter(value, 0);
        if (index < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        var start = 0;
        while (index >= 0)
        {
            builder.Append(value, start, index - start).Append('�');
            start = index + 1;
            index = IndexOfInvalidCharacter(value, start);
        }

        builder.Append(value, start, value.Length - start);
        return builder.ToString();
    }

    private static int IndexOfInvalidCharacter(string value, int start)
    {
        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (XmlConvert.IsXmlChar(c))
                continue;

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    /// <summary>Writes the padding (ISO 16684-1, 7.3.2): spaces, with a line feed every 100 bytes.</summary>
    private static void WritePadding(MemoryStream output, int padding)
    {
        for (var i = 0; i < padding; i++)
        {
            output.WriteByte(i % 100 == 99 ? (byte)'\n' : (byte)' ');
        }

        if (padding > 0)
        {
            output.WriteByte((byte)'\n');
        }
    }

    private static XDocument? Parse(byte[] packet)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
        };
        try
        {
            using var stream = new MemoryStream(packet, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement CreatePdfUAExtensionSchema()
    {
        return new XElement(
            Rdf + "li",
            new XAttribute(Rdf + "parseType", "Resource"),
            new XElement(PdfASchema + "schema", "PDF/UA Universal Accessibility Schema"),
            new XElement(PdfASchema + "namespaceURI", PdfUAIdNamespace),
            new XElement(PdfASchema + "prefix", "pdfuaid"),
            new XElement(
                PdfASchema + "property",
                new XElement(
                    Rdf + "Seq",
                    new XElement(
                        Rdf + "li",
                        new XAttribute(Rdf + "parseType", "Resource"),
                        new XElement(PdfAProperty + "name", "part"),
                        new XElement(PdfAProperty + "valueType", "Integer"),
                        new XElement(PdfAProperty + "category", "internal"),
                        new XElement(PdfAProperty + "description", "Indicates which part of ISO 14289 standard is followed")))));
    }

    private static XElement CreateLanguageAlternative(string name, string value)
    {
        return new XElement(
            DublinCore + name,
            new XElement(Rdf + "Alt", new XElement(Rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), Sanitize(value))));
    }
}
