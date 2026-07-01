using System.Xml;
using System.Xml.Linq;

namespace Meziantou.Framework.Language.Xml;

/// <summary>
/// Formats XML syntax trees/documents using <see cref="XmlFormattingOptions"/>.
/// </summary>
/// <example>
/// <code>
/// var tree = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;item/&gt;&lt;/root&gt;");
/// var formatted = Formatter.Format(tree);
/// </code>
/// </example>
public static class Formatter
{
    public static XmlSyntaxAnnotation Annotation { get; } = new("Formatter");

    public static XmlDocumentSyntax Format(XmlSyntaxTree syntaxTree)
    {
        ArgumentNullException.ThrowIfNull(syntaxTree);
        return Format(syntaxTree.Root, XmlFormattingOptions.Default);
    }

    public static XmlDocumentSyntax Format(XmlDocumentSyntax document)
    {
        return Format(document, XmlFormattingOptions.Default);
    }

    public static XmlDocumentSyntax Format(XmlDocumentSyntax document, XmlFormattingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= XmlFormattingOptions.Default;
        var text = document.ToFullString();
        if (text.Length == 0)
            return document;

        try
        {
            var source = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = source.Declaration is null,
                Indent = true,
                NewLineChars = options.NewLine,
                NewLineHandling = NewLineHandling.None,
                Encoding = Encoding.UTF8,
                IndentChars = options.UseTabs ? "\t" : new string(' ', Math.Max(0, options.IndentationSize)),
            };

            using var writer = new Utf8StringWriter();
            using var xmlWriter = XmlWriter.Create(writer, settings);
            source.Save(xmlWriter);
            xmlWriter.Flush();
            return XmlSyntaxTree.ParseText(writer.ToString()).Root;
        }
        catch (XmlException)
        {
            return document;
        }
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public Utf8StringWriter()
            : base(CultureInfo.InvariantCulture)
        {
        }
    }
}
