using System.Globalization;
using System.Text;
using System.Xml;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The values of the fields of a form, apart from the document: read from and written to FDF (Forms Data Format, ISO
/// 32000-2, 12.7.8) and XFDF (XML Forms Data Format, ISO 19444-1). <see cref="PdfInteractiveForm.ExportData"/> exports
/// the values of a form, and <see cref="PdfInteractiveForm.ImportData(PdfFormData)"/> fills a form with them.
/// </summary>
/// <remarks>
/// Only the values of the fields are read and written: the annotations, the appearances, the JavaScript and the other
/// entries of FDF and XFDF are ignored. The names of the fields are fully qualified (<c>address.city</c>); the files
/// store them as a hierarchy of partial names.
/// </remarks>
public sealed class PdfFormData
{
    private const int MaxDepth = 64;
    private const int MaxFields = 100_000;

    private static readonly PdfName FdfName = PdfName.Create("FDF");
    private static readonly PdfName FieldsName = PdfName.Create("Fields");
    private static readonly PdfName KidsName = PdfName.Create("Kids");
    private static readonly PdfName TName = PdfName.Create("T");
    private static readonly PdfName VName = PdfName.Create("V");
    private static readonly PdfName FName = PdfName.Create("F");
    private static readonly PdfName UFName = PdfName.Create("UF");
    private static readonly PdfName RootName = PdfName.Create("Root");

    /// <summary>The namespace of XFDF.</summary>
    private const string XfdfNamespace = "http://ns.adobe.com/xfdf/";

    /// <summary>Gets or sets the file the data belongs to: <c>/F</c> of the FDF dictionary, <c>href</c> of the <c>f</c> element of XFDF.</summary>
    public string? SourceFile { get; set; }

    /// <summary>Gets the values of the fields.</summary>
    public IList<PdfFormDataField> Fields { get; } = [];

    /// <summary>Reads FDF data (ISO 32000-2, 12.7.8): the <c>/Fields</c> of the FDF dictionary.</summary>
    /// <param name="data">The FDF file.</param>
    /// <param name="options">The options of the reader, or <see langword="null"/> for the default options.</param>
    /// <returns>The data.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not an FDF file.</exception>
    /// <exception cref="PdfException">The file cannot be read.</exception>
    public static PdfFormData ParseFdf(ReadOnlySpan<byte> data, PdfReadOptions? options = null)
    {
        // FDF has the syntax of PDF (12.7.8.1), with the %FDF- header
        var bytes = data.ToArray();
        var header = bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%FDF-"u8);
        if (header >= 0)
        {
            "%PDF-"u8.CopyTo(bytes.AsSpan(header));
        }

        var document = PdfDocument.Open(bytes, options);
        if (document.Trailer.GetResolved(RootName) is not PdfDictionary catalog || catalog.GetResolved(FdfName) is not PdfDictionary fdf)
            throw new InvalidPdfContentException("The file has no FDF dictionary");

        var result = new PdfFormData { SourceFile = ReadFileSpecification(fdf.GetResolved(FName)) };
        if (fdf.GetResolved(FieldsName) is PdfArray fields)
        {
            var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
            ReadFdfFields(fields, prefix: null, result, visited, depth: 0);
        }

        return result;
    }

    /// <summary>Reads XFDF data (ISO 19444-1): the <c>field</c> and <c>value</c> elements of the <c>fields</c> element.</summary>
    /// <param name="xml">The XFDF document.</param>
    /// <returns>The data.</returns>
    /// <exception cref="InvalidPdfContentException">The text is not an XFDF document.</exception>
    public static PdfFormData ParseXfdf(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using var reader = new StringReader(xml);
        return ParseXfdf(reader);
    }

    /// <summary>Reads XFDF data (ISO 19444-1) from a stream: the <c>field</c> and <c>value</c> elements of the <c>fields</c> element.</summary>
    /// <param name="stream">The XFDF document.</param>
    /// <returns>The data.</returns>
    /// <exception cref="InvalidPdfContentException">The stream is not an XFDF document.</exception>
    public static PdfFormData ParseXfdf(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return ParseXfdf(reader);
    }

    /// <summary>Writes the data as FDF (ISO 32000-2, 12.7.8): a field hierarchy of partial names in the FDF dictionary.</summary>
    /// <returns>The FDF file.</returns>
    /// <remarks>A name given several times is written once, with its last value.</remarks>
    public byte[] ToFdf()
    {
        var root = BuildTree();
        var fdf = new PdfDictionary();
        if (SourceFile is not null)
        {
            fdf[FName] = PdfString.FromText(SourceFile);
        }

        fdf[FieldsName] = ToFdfFields(root);
        var catalog = new PdfDictionary { [FdfName] = fdf };

        var writer = new PdfByteWriter();
        writer.WriteAscii("%FDF-1.2\n");

        // A comment of bytes above 127 tells that the file is binary (7.5.2)
        writer.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
        var offset = writer.Position;
        writer.WriteAscii("1 0 obj\n");
        PdfObjectSerializer.Write(writer, catalog, ReferenceWriter.Identity);
        writer.WriteAscii("\nendobj\n");
        var xref = writer.Position;
        writer.WriteAscii("xref\n0 2\n0000000000 65535 f \n");
        writer.WriteAscii(offset.ToString("D10", CultureInfo.InvariantCulture));
        writer.WriteAscii(" 00000 n \ntrailer\n<</Root 1 0 R>>\nstartxref\n");
        writer.WriteAscii(xref.ToString(CultureInfo.InvariantCulture));
        writer.WriteAscii("\n%%EOF\n");
        return writer.ToArray();
    }

    /// <summary>Writes the data as XFDF (ISO 19444-1): nested <c>field</c> elements with partial names, and their <c>value</c> elements.</summary>
    /// <returns>The XFDF document.</returns>
    /// <remarks>
    /// A name given several times is written once, with its last value. The characters that XML cannot hold (control
    /// characters, unpaired surrogates) are written as U+FFFD.
    /// </remarks>
    public string ToXfdf()
    {
        var root = BuildTree();
        var output = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, NewLineChars = "\n" };
        using (var writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement("xfdf", XfdfNamespace);
            writer.WriteAttributeString("xml", "space", ns: null, "preserve");
            if (SourceFile is not null)
            {
                writer.WriteStartElement("f", XfdfNamespace);
                writer.WriteAttributeString("href", ToXmlText(SourceFile));
                writer.WriteEndElement();
            }

            writer.WriteStartElement("fields", XfdfNamespace);
            WriteXfdfFields(writer, root);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + output.ToString() + "\n";
    }

    private static PdfFormData ParseXfdf(TextReader text)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = 256L * 1024 * 1024,
        };

        var result = new PdfFormData();
        try
        {
            using var reader = XmlReader.Create(text, settings);
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "xfdf")
                throw new InvalidPdfContentException("The document is not an XFDF document: its root element is not 'xfdf'");

            if (reader.IsEmptyElement)
                return result;

            reader.Read();
            while (!reader.EOF && reader.Depth > 0)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                {
                    if (reader.LocalName == "f")
                    {
                        result.SourceFile = reader.GetAttribute("href");
                    }
                    else if (reader.LocalName == "fields")
                    {
                        ReadXfdfFields(reader, prefix: null, values: null, result, depth: 0);
                        reader.Read();
                        continue;
                    }

                    reader.Skip();
                    continue;
                }

                reader.Read();
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidPdfContentException("The document is not an XFDF document: " + ex.Message, ex);
        }

        return result;
    }

    /// <summary>
    /// Reads the children of the element the reader is on: its <c>value</c> elements into <paramref name="values"/>, and
    /// its <c>field</c> elements as fields of their own. The reader is left on the end of the element.
    /// </summary>
    private static void ReadXfdfFields(XmlReader reader, string? prefix, List<string>? values, PdfFormData result, int depth)
    {
        if (reader.IsEmptyElement)
            return;

        using var subtree = reader.ReadSubtree();
        subtree.Read();
        subtree.Read();
        while (!subtree.EOF)
        {
            if (subtree.NodeType != XmlNodeType.Element || subtree.Depth != 1)
            {
                subtree.Read();
                continue;
            }

            if (values is not null && subtree.LocalName == "value")
            {
                values.Add(subtree.ReadElementContentAsString());
            }
            else if (subtree.LocalName == "field" && depth < MaxDepth)
            {
                var name = Combine(prefix, subtree.GetAttribute("name") ?? "");
                var fieldValues = new List<string>();
                ReadXfdfFields(subtree, name, fieldValues, result, depth + 1);
                if (fieldValues.Count > 0 && result.Fields.Count < MaxFields)
                {
                    result.Fields.Add(new PdfFormDataField(name, fieldValues));
                }

                subtree.Read();
            }
            else
            {
                subtree.Skip();
            }
        }
    }

    private static void ReadFdfFields(PdfArray fields, string? prefix, PdfFormData result, HashSet<PdfObject> visited, int depth)
    {
        if (depth > MaxDepth)
            return;

        for (var i = 0; i < fields.Count && result.Fields.Count < MaxFields; i++)
        {
            if (!fields.TryGetResolved<PdfDictionary>(i, out var field) || !visited.Add(field))
                continue;

            var name = Combine(prefix, field.GetText(TName) ?? "");
            switch (field.GetResolved(VName))
            {
                case PdfString text:
                    result.Fields.Add(new PdfFormDataField(name, text.ToText()));
                    break;
                case PdfName state:
                    result.Fields.Add(new PdfFormDataField(name, state.Value) { IsName = true });
                    break;
                case PdfArray array:
                    var values = new List<string>();
                    for (var j = 0; j < array.Count; j++)
                    {
                        switch (array.GetResolved(j))
                        {
                            case PdfString item:
                                values.Add(item.ToText());
                                break;
                            case PdfName item:
                                values.Add(item.Value);
                                break;
                        }
                    }

                    result.Fields.Add(new PdfFormDataField(name, values));
                    break;
            }

            if (field.GetResolved(KidsName) is PdfArray kids)
            {
                ReadFdfFields(kids, name, result, visited, depth + 1);
            }
        }
    }

    private static string? ReadFileSpecification(PdfObject? value) => value switch
    {
        PdfString text => text.ToText(),
        PdfDictionary specification => specification.GetText(UFName) ?? specification.GetText(FName),
        _ => null,
    };

    private static string Combine(string? prefix, string name) => prefix is null ? name : prefix + "." + name;

    /// <summary>
    /// Builds the hierarchy of partial names; a name given several times keeps its last value. The hierarchy is at most
    /// <see cref="MaxDepth"/> levels deep, the depth the readers read: the last partial name keeps the dots of the parts
    /// beyond it.
    /// </summary>
    private Node BuildTree()
    {
        var root = new Node("");
        foreach (var field in Fields)
        {
            var node = root;
            foreach (var part in field.Name.Split('.', MaxDepth))
            {
                node = node.GetChild(part);
            }

            node.Field = field;
        }

        return root;
    }

    private static PdfArray ToFdfFields(Node parent)
    {
        var array = new PdfArray();
        foreach (var node in parent.Children)
        {
            var dictionary = new PdfDictionary { [TName] = PdfString.FromText(node.Name) };
            if (node.Field is { } field)
            {
                dictionary[VName] = field.Values.Count switch
                {
                    0 => null,
                    1 when field.IsName => PdfName.Create(field.Values[0]),
                    1 => PdfString.FromText(field.Values[0]),
                    _ => new PdfArray(field.Values.Select(value => (PdfObject)PdfString.FromText(value))),
                };
            }

            if (node.Children.Count > 0)
            {
                dictionary[KidsName] = ToFdfFields(node);
            }

            array.Add(dictionary);
        }

        return array;
    }

    private static void WriteXfdfFields(XmlWriter writer, Node parent)
    {
        foreach (var node in parent.Children)
        {
            writer.WriteStartElement("field", XfdfNamespace);
            writer.WriteAttributeString("name", ToXmlText(node.Name));
            if (node.Field is { } field)
            {
                foreach (var value in field.Values)
                {
                    writer.WriteElementString("value", XfdfNamespace, ToXmlText(value));
                }
            }

            WriteXfdfFields(writer, node);
            writer.WriteEndElement();
        }
    }

    /// <summary>Replaces the characters XML 1.0 cannot hold with U+FFFD.</summary>
    private static string ToXmlText(string value)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var valid = true;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    builder?.Append(c).Append(value[i + 1]);
                    i++;
                    continue;
                }

                valid = false;
            }
            else if (char.IsLowSurrogate(c) || !XmlConvert.IsXmlChar(c))
            {
                valid = false;
            }

            if (!valid)
            {
                builder ??= new StringBuilder(value, 0, i, value.Length);
                builder.Append('\uFFFD');
            }
            else
            {
                builder?.Append(c);
            }
        }

        return builder?.ToString() ?? value;
    }

    private sealed class Node(string name)
    {
        private Dictionary<string, Node>? _index;

        public string Name { get; } = name;

        public List<Node> Children { get; } = [];

        public PdfFormDataField? Field { get; set; }

        public Node GetChild(string name)
        {
            _index ??= new Dictionary<string, Node>(StringComparer.Ordinal);
            if (!_index.TryGetValue(name, out var child))
            {
                child = new Node(name);
                _index.Add(name, child);
                Children.Add(child);
            }

            return child;
        }
    }
}
