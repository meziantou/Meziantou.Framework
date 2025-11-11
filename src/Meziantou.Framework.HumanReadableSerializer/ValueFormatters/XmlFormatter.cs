using System.Xml.Linq;
using System.Xml;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Formats XML values for human-readable output.</summary>
public sealed class XmlFormatter : ValueFormatter
{
    private readonly XmlFormatterOptions _options;

    /// <summary>Initializes a new instance of the <see cref="XmlFormatter"/> class with default options.</summary>
    public XmlFormatter()
        : this(options: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XmlFormatter"/> class with the specified options.</summary>
    /// <param name="options">The formatting options.</param>
    public XmlFormatter(XmlFormatterOptions? options)
    {
        _options = options ?? new();
    }

    public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
    {
        try
        {
            var document = XDocument.Parse(value, _options.WriteIndented ? LoadOptions.None : LoadOptions.PreserveWhitespace);
            if (_options.OrderAttributes)
            {
                foreach (var element in document.Descendants())
                {
                    var sortedAttributes = element.Attributes()
                        .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
                        .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal)
                        .ToArray();

                    element.RemoveAttributes();
                    element.Add(sortedAttributes);
                }
            }

            var sb = new StringBuilder(capacity: value.Length);
            var settings = new XmlWriterSettings
            {
                Indent = _options.WriteIndented,
                IndentChars = "  ",
                NamespaceHandling = NamespaceHandling.OmitDuplicates,
                Async = false,
                CheckCharacters = false,
                NewLineHandling = NewLineHandling.None,
                NewLineOnAttributes = false,
                OmitXmlDeclaration = true,
            };

            using (var xmlWriter = XmlWriter.Create(sb, settings))
            {
                document.WriteTo(xmlWriter);
            }

            writer.WriteValue(sb.ToString());
        }
        catch
        {
            writer.WriteValue(value);
        }
    }
}
