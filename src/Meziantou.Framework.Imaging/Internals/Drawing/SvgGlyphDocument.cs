using System.Xml;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The elements of an SVG document of a font (the <c>SVG </c> table), and the values of their attributes.</summary>
/// <remarks>
/// OpenType specification, "SVG - Scalable vector graphics table", "SVG documents": the document is XML, UTF-8, with SVG
/// as its default namespace; <c>xlink:href</c> is the only other attribute of a namespace. The document is untrusted:
/// no DTD is processed and nothing is resolved, and every element read is charged to the budget of the glyph. The
/// declarations of a <c>style</c> attribute replace the presentation attributes of their element; <c>style</c>
/// elements (stylesheets, optional in the specification) are not applied.
/// </remarks>
internal sealed class SvgGlyphDocument
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string XLinkNamespace = "http://www.w3.org/1999/xlink";

    private readonly Dictionary<string, SvgElement> _ids;

    private SvgGlyphDocument(SvgElement root, Dictionary<string, SvgElement> ids)
    {
        Root = root;
        _ids = ids;
    }

    /// <summary>Gets the root element.</summary>
    public SvgElement Root { get; }

    /// <summary>Reads a document.</summary>
    /// <returns><see langword="null"/> when the document is not well-formed XML or its root is not an SVG element.</returns>
    /// <exception cref="FontResourceLimitException">The document has more elements than the budget of the glyph.</exception>
    public static SvgGlyphDocument? Read(ReadOnlyMemory<byte> document, ref GlyphBudget budget)
    {
        var stream = new MemoryStream(document.ToArray(), writable: false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true,
        };

        var ids = new Dictionary<string, SvgElement>(StringComparer.Ordinal);
        SvgElement? root = null;
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            SvgElement? current = null;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        budget.Charge(1);

                        // An element of another namespace, and what it contains, is not SVG: it is read and left out.
                        var isSvg = reader.NamespaceURI is SvgNamespace or "" && current is not { IsForeign: true };
                        var element = new SvgElement(reader.LocalName, current, isForeign: !isSvg);
                        ReadAttributes(reader, element, ref budget);
                        if (current is null)
                        {
                            root = element;
                        }
                        else
                        {
                            current.Children.Add(element);
                        }

                        if (isSvg && element.GetAttribute("id") is { } id)
                        {
                            ids.TryAdd(id, element);
                        }

                        if (!reader.IsEmptyElement)
                        {
                            current = element;
                        }

                        break;

                    case XmlNodeType.EndElement:
                        current = current?.Parent;
                        break;
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        if (root is null || root.IsForeign || root.Name != "svg")
            return null;

        return new SvgGlyphDocument(root, ids);
    }

    private static void ReadAttributes(XmlReader reader, SvgElement element, ref GlyphBudget budget)
    {
        string? style = null;
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                budget.Charge(1);
                var ns = reader.NamespaceURI;
                if (ns.Length == 0)
                {
                    if (reader.LocalName == "style")
                    {
                        style = reader.Value;
                    }
                    else
                    {
                        element.SetAttribute(reader.LocalName, reader.Value);
                    }
                }
                else if (ns == XLinkNamespace && reader.LocalName == "href")
                {
                    // SVG 2 has href without namespace: the one without namespace wins when an element has both.
                    element.Attributes.TryAdd("href", reader.Value);
                }
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        // The declarations of the style attribute: "name: value", separated by semicolons; they override the attributes.
        if (style is not null)
        {
            foreach (var range in style.AsSpan().Split(';'))
            {
                var declaration = style.AsSpan(range);
                var colon = declaration.IndexOf(':');
                if (colon <= 0)
                    continue;

                var name = declaration[..colon].Trim();
                var value = declaration[(colon + 1)..].Trim();
                var important = value.LastIndexOf('!');
                if (important >= 0 && value[(important + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
                {
                    value = value[..important].TrimEnd();
                }

                if (name.Length > 0)
                {
                    budget.Charge(1);
                    element.SetAttribute(name.ToString(), value.ToString());
                }
            }
        }
    }

    /// <summary>Finds an element by its identifier.</summary>
    public SvgElement? GetElementById(string id) => _ids.GetValueOrDefault(id);

    /// <summary>Finds the element an IRI of the document refers to (<c>#id</c>); <see langword="null"/> for any other IRI.</summary>
    public SvgElement? GetElementByIri(string? iri)
    {
        if (iri is null)
            return null;

        var value = iri.AsSpan().Trim();
        return value is ['#', .. var id] ? GetElementById(id.ToString()) : null;
    }

    /// <summary>Finds the element of a <c>url(#id)</c> reference, and what follows it (the fallback of a paint).</summary>
    public SvgElement? GetElementByUrl(ReadOnlySpan<char> value, out ReadOnlySpan<char> fallback)
    {
        fallback = default;
        value = value.Trim();
        if (!value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return null;

        var end = value.IndexOf(')');
        if (end < 0)
            return null;

        var reference = value[4..end].Trim().Trim('"').Trim('\'').Trim();
        fallback = value[(end + 1)..].Trim();
        return reference is ['#', .. var id] ? GetElementById(id.ToString()) : null;
    }
}
