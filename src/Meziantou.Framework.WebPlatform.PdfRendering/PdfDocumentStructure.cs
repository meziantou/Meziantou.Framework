using System.Text;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

/// <summary>
/// What the pages show of the elements of a document: where each element is painted first (the destinations of the links
/// and of the outline), the areas of the links, and the logical structure (ISO 32000-2, 14.7): a structure element for each
/// element that has a meaning (a heading, a paragraph, a list, a table, a figure, a link) and paints content.
/// </summary>
/// <remarks>
/// The pages are measured first (<see cref="Record"/>), so that the structure tree is built in the order of the document
/// (<see cref="BuildTree"/>) before the content refers to it. The drawings of the boxes (backgrounds, borders, decorations)
/// are artifacts, except in the figures (the shapes of an SVG image).
/// </remarks>
internal sealed class PdfDocumentStructure
{
    private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string MathMLNamespace = "http://www.w3.org/1998/Math/MathML";

    /// <summary>The role of an element that is not a structure element: its content belongs to its parent.</summary>
    private const string? Transparent = null;

    /// <summary>The role of an element whose content is an artifact (<c>aria-hidden</c>, a decorative image).</summary>
    private const string Hidden = "#hidden";

    /// <summary>The role of the root elements (<c>html</c>, <c>body</c>): the <c>Document</c> structure element.</summary>
    private const string Root = "#root";

    private readonly Document _document;
    private readonly Dictionary<Node, NodeInfo?> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Element, ElementRecord> _records = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Element, PdfStructureElement> _structureElements = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Element, PdfStructureElement> _contentElements = new(ReferenceEqualityComparer.Instance);
    private readonly List<Element> _links = [];
    private PdfStructureElement? _root;

    public PdfDocumentStructure(Document document, bool tagged)
    {
        _document = document;
        IsTagged = tagged;
    }

    /// <summary>Whether the document is a tagged PDF.</summary>
    public bool IsTagged { get; }

    /// <summary>Records an item of a page: the element it paints, its area on the page (in the CSS px of the page).</summary>
    public void Record(int pageIndex, DisplayItem item, PhysicalRect? bounds)
    {
        if (item.Source is not { } source || GetInfo(source) is not { } info)
            return;

        if (bounds is { IsEmpty: false } rect)
        {
            // The first area each element (and its ancestors) is painted in
            for (Element? element = info.Element; element is not null; element = element.ParentElement)
            {
                var record = GetRecord(element);
                if (record.FirstPage >= 0)
                    break;

                record.FirstPage = pageIndex;
                record.FirstBounds = rect;
            }

            if (info.Link is { } link && item is not DrawPaintItem)
            {
                var record = GetRecord(link);
                if (record.LinkAreas is null)
                {
                    record.LinkAreas = [];
                    _links.Add(link);
                }

                record.LinkAreas.Add((pageIndex, rect));
            }
        }

        if (info.StructureElement is { } structureElement && IsContent(item, info))
        {
            GetRecord(structureElement).HasContent = true;
        }
    }

    /// <summary>Builds the structure tree from the elements that paint content, in the order of the document.</summary>
    public void BuildTree(PdfDocument pdf)
    {
        if (!IsTagged || _document.DocumentElement is not { } documentElement)
            return;

        // An element is in the tree when it or one of its descendants paints content
        var needed = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        foreach (var (element, record) in _records)
        {
            if (!record.HasContent && record.LinkAreas is null)
                continue;

            for (Element? current = element; current is not null && needed.Add(current); current = current.ParentElement)
            {
            }
        }

        _root = pdf.StructureTree.AddElement("Document");
        Build(documentElement, _root, needed, inheritedLanguage: documentElement.GetAttribute("lang"));
    }

    /// <summary>
    /// Returns the structure element whose content an item is, or null when the item is an artifact (with the type of
    /// the artifact).
    /// </summary>
    public PdfStructureElement? GetContentElement(DisplayItem item, out PdfArtifactType artifactType)
    {
        artifactType = item is DrawGlyphsItem ? PdfArtifactType.Pagination : PdfArtifactType.Background;
        if (item.Source is not { } source || GetInfo(source) is not { } info)
            return null;

        artifactType = item is DrawLineItem ? PdfArtifactType.Layout : PdfArtifactType.Background;
        if (info.StructureElement is not { } element || !IsContent(item, info))
            return null;

        return _contentElements.GetValueOrDefault(element) ?? _structureElements.GetValueOrDefault(element) ?? _root;
    }

    /// <summary>Adds the link annotations of the links of the document.</summary>
    public void AddLinks(IReadOnlyList<(PdfPage Page, PhysicalSize Size)> pages)
    {
        foreach (var link in _links)
        {
            var areas = _records[link].LinkAreas!;
            var href = link.GetAttribute("href");
            if (href is null || !TryGetLinkTarget(link, href, pages, out var uri, out var destination))
                continue;

            var contents = GetText(link);
            if (contents.Length == 0)
            {
                contents = href;
            }

            _structureElements.TryGetValue(link, out var structureElement);
            foreach (var (pageIndex, rect) in MergeLines(areas))
            {
                var (page, size) = pages[pageIndex];
                var rectangle = PdfGeometry.ToPdfRectangle(rect, size.Height);
                var annotation = uri is not null ? page.Annotations.AddLink(rectangle, uri) : page.Annotations.AddLink(rectangle, destination!);
                annotation.Contents = contents;
                if (IsTagged)
                {
                    (structureElement ?? _root)?.AddAnnotation(annotation);
                }
            }
        }
    }

    /// <summary>Adds the headings of the document to its outline, nested by level.</summary>
    public void AddOutlines(PdfDocument pdf, IReadOnlyList<(PdfPage Page, PhysicalSize Size)> pages)
    {
        if (_document.DocumentElement is not { } root)
            return;

        var stack = new List<(int Level, PdfOutlineItem Item)>();
        foreach (var element in Descendants(root))
        {
            if (!string.Equals(element.NamespaceUri, HtmlNamespace, StringComparison.Ordinal) || GetHeadingLevel(element.LocalName) is not { } level)
                continue;

            if (GetDestination(element, pages) is not { } destination)
                continue;

            var title = GetText(element);
            if (title.Length == 0)
                continue;

            while (stack.Count > 0 && stack[^1].Level >= level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var item = stack.Count == 0 ? pdf.Outlines.Add(title, destination) : stack[^1].Item.Children.Add(title, destination);
            stack.Add((level, item));
        }
    }

    private static int? GetHeadingLevel(string localName) => localName switch
    {
        "h1" => 1,
        "h2" => 2,
        "h3" => 3,
        "h4" => 4,
        "h5" => 5,
        "h6" => 6,
        _ => null,
    };

    private void Build(Element element, PdfStructureElement parent, HashSet<Element> needed, string? inheritedLanguage)
    {
        var role = GetRole(element);
        var childParent = parent;
        var language = element.GetAttribute("lang") is { Length: > 0 } lang ? lang : inheritedLanguage;
        if (role == Root)
        {
            _structureElements[element] = parent;
        }
        else if (role is not null and not Hidden && needed.Contains(element))
        {
            var structureElement = parent.AddElement(role);
            _structureElements[element] = structureElement;
            if (!string.Equals(language, inheritedLanguage, StringComparison.OrdinalIgnoreCase) && language is not null)
            {
                structureElement.Language = language;
            }

            SetAttributes(element, role, structureElement);
            childParent = structureElement;
            if (role == "LI")
            {
                // The content of a list item is its body (14.8.4.3.3)
                childParent = structureElement.AddElement("LBody");
                _contentElements[element] = childParent;
            }
        }
        else if (role == Hidden)
        {
            return;
        }

        foreach (var child in element.ChildNodes)
        {
            if (child is Element childElement)
            {
                Build(childElement, childParent, needed, language);
            }
        }
    }

    private void SetAttributes(Element element, string role, PdfStructureElement structureElement)
    {
        switch (role)
        {
            case "Figure" or "Formula":
            {
                var alternate = element.GetAttribute("alt") ?? element.GetAttribute("aria-label") ?? GetSvgTitle(element) ?? element.GetAttribute("title");
                if (!string.IsNullOrWhiteSpace(alternate))
                {
                    structureElement.AlternateDescription = alternate.Trim();
                }
                else if (role == "Formula" && GetText(element) is { Length: > 0 } text)
                {
                    structureElement.AlternateDescription = text;
                }

                break;
            }

            case "TH" or "TD":
            {
                var attributes = new PdfDictionary { [PdfName.Create("O")] = PdfName.Create("Table") };
                if (role == "TH")
                {
                    attributes[PdfName.Create("Scope")] = PdfName.Create(GetHeaderScope(element));
                }

                if (TryGetSpan(element, "rowspan", out var rowSpan))
                {
                    attributes[PdfName.Create("RowSpan")] = PdfInteger.Get(rowSpan);
                }

                if (TryGetSpan(element, "colspan", out var colSpan))
                {
                    attributes[PdfName.Create("ColSpan")] = PdfInteger.Get(colSpan);
                }

                structureElement.Dictionary[PdfName.Create("A")] = attributes;
                break;
            }
        }

        if (element.GetAttribute("aria-label") is { Length: > 0 } label && role is not ("Figure" or "Formula"))
        {
            structureElement.AlternateDescription = label;
        }
    }

    /// <summary>The scope of a header cell (14.8.5.7): its <c>scope</c> attribute, a column in the header of the table, a row when it starts a row of the body.</summary>
    private static string GetHeaderScope(Element element)
    {
        switch (element.GetAttribute("scope")?.ToLowerInvariant())
        {
            case "row" or "rowgroup":
                return "Row";

            case "col" or "colgroup":
                return "Column";
        }

        if (element.ParentElement?.ParentElement is { LocalName: "thead" })
            return "Column";

        return element.PreviousElementSibling is null && element.NextElementSibling is not null ? "Row" : "Column";
    }

    private static bool TryGetSpan(Element element, string name, out int value)
    {
        value = 0;
        return element.GetAttribute(name) is { } text && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 1;
    }

    private static string? GetSvgTitle(Element element)
    {
        if (!string.Equals(element.NamespaceUri, SvgNamespace, StringComparison.Ordinal))
            return null;

        foreach (var child in element.ChildNodes)
        {
            if (child is Element { LocalName: "title" } title)
                return title.TextContent;
        }

        return null;
    }

    /// <summary>
    /// Returns the standard structure type of an element (ISO 32000-1, 14.8.4), <see cref="Transparent"/> when its content
    /// belongs to its parent, <see cref="Hidden"/> when its content is an artifact, or <see cref="Root"/>.
    /// </summary>
    private static string? GetRole(Element element)
    {
        if (element.GetAttribute("aria-hidden") is { } ariaHidden && ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase))
            return Hidden;

        var ns = element.NamespaceUri;
        if (string.Equals(ns, SvgNamespace, StringComparison.Ordinal))
            return element.ParentElement is { } parent && string.Equals(parent.NamespaceUri, SvgNamespace, StringComparison.Ordinal) ? Transparent : "Figure";

        if (string.Equals(ns, MathMLNamespace, StringComparison.Ordinal))
            return element.ParentElement is { } parent && string.Equals(parent.NamespaceUri, MathMLNamespace, StringComparison.Ordinal) ? Transparent : "Formula";

        if (!string.Equals(ns, HtmlNamespace, StringComparison.Ordinal))
            return Transparent;

        if (element.GetAttribute("role") is "presentation" or "none")
            return Transparent;

        return element.LocalName switch
        {
            "html" or "body" => Root,
            "h1" => "H1",
            "h2" => "H2",
            "h3" => "H3",
            "h4" => "H4",
            "h5" => "H5",
            "h6" => "H6",
            "p" or "pre" or "address" or "dt" or "dd" => "P",
            "ul" or "ol" or "menu" => "L",
            "li" => "LI",
            "table" => "Table",
            "caption" or "figcaption" => "Caption",
            "thead" => "THead",
            "tbody" => "TBody",
            "tfoot" => "TFoot",
            "tr" => "TR",
            "th" => "TH",
            "td" => "TD",
            "img" when element.GetAttribute("alt") is "" => Hidden,
            "img" or "canvas" or "video" or "object" or "embed" => "Figure",
            "blockquote" => "BlockQuote",
            "q" => "Quote",
            "code" or "kbd" or "samp" => "Code",
            "a" when element.HasAttribute("href") => "Link",
            "article" => "Art",
            "section" or "nav" or "aside" or "header" or "footer" or "main" => "Sect",
            "div" or "figure" or "form" or "fieldset" or "dl" or "details" or "summary" => "Div",
            "hr" => Hidden,
            _ => Transparent,
        };
    }

    /// <summary>Whether an item is content of its structure element: the text and the images, and every drawing of a figure.</summary>
    private static bool IsContent(DisplayItem item, NodeInfo info) => item is DrawGlyphsItem or DrawImageItem || info.IsFigure;

    private ElementRecord GetRecord(Element element)
    {
        if (!_records.TryGetValue(element, out var record))
        {
            record = new ElementRecord();
            _records.Add(element, record);
        }

        return record;
    }

    /// <summary>Returns what a node painted by an item belongs to, or null when the node is not in the document.</summary>
    private NodeInfo? GetInfo(Node node)
    {
        if (_nodes.TryGetValue(node, out var info))
            return info;

        var element = node as Element ?? node.ParentElement;
        if (element is null || element.OwnerDocument != _document)
        {
            info = null;
        }
        else
        {
            Element? structureElement = null;
            Element? link = null;
            var hidden = false;
            var isFigure = false;
            for (var current = element; current is not null; current = current.ParentElement)
            {
                var role = GetRole(current);
                if (role == Hidden)
                {
                    hidden = true;
                    break;
                }

                if (structureElement is null && role is not null)
                {
                    structureElement = current;
                    isFigure = role is "Figure" or "Formula";
                }

                if (link is null && string.Equals(current.LocalName, "a", StringComparison.Ordinal) && current.HasAttribute("href") && string.Equals(current.NamespaceUri, HtmlNamespace, StringComparison.Ordinal))
                {
                    link = current;
                }
            }

            info = new NodeInfo(element, hidden ? null : structureElement, link, isFigure);
        }

        _nodes.Add(node, info);
        return info;
    }

    /// <summary>Returns the destination of the first area an element is painted in, or null when it is not painted.</summary>
    private PdfDestination? GetDestination(Element element, IReadOnlyList<(PdfPage Page, PhysicalSize Size)> pages)
    {
        if (!_records.TryGetValue(element, out var record) || record.FirstPage < 0)
            return null;

        var (page, size) = pages[record.FirstPage];
        var rect = PdfGeometry.ToPdfRectangle(record.FirstBounds, size.Height);
        return PdfDestination.Xyz(page, rect.Left, rect.Top);
    }

    private bool TryGetLinkTarget(Element link, string href, IReadOnlyList<(PdfPage Page, PhysicalSize Size)> pages, out Uri? uri, out PdfDestination? destination)
    {
        uri = null;
        destination = null;
        if (!Uri.TryCreate(link.BaseUri, UriKind.Absolute, out var baseUri) || !Uri.TryCreate(baseUri, href.Trim(), out var target))
            return false;

        if (target.Scheme is "javascript" or "data")
            return false;

        // A fragment of the document: a destination in the PDF document
        if (target.Fragment.Length > 1 && Uri.TryCreate(_document.DocumentUri, UriKind.Absolute, out var documentUri) && Uri.Compare(target, documentUri, UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.Unescaped, StringComparison.Ordinal) == 0)
        {
            var id = Uri.UnescapeDataString(target.Fragment[1..]);
            var element = _document.GetElementById(id) ?? _document.QuerySelector("a[name=\"" + id.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"]");
            destination = element is null ? null : GetDestination(element, pages);
            return destination is not null;
        }

        uri = target;
        return true;
    }

    /// <summary>Merges the areas of a link that are on the same line of a page: one link annotation per line.</summary>
    private static List<(int Page, PhysicalRect Rect)> MergeLines(List<(int Page, PhysicalRect Rect)> areas)
    {
        var result = new List<(int Page, PhysicalRect Rect)>();
        foreach (var (page, rect) in areas)
        {
            var merged = false;
            for (var i = 0; i < result.Count; i++)
            {
                var (otherPage, other) = result[i];
                if (otherPage != page)
                    continue;

                // On the same line: the vertical ranges overlap by half of the smaller one
                var overlap = Math.Min(rect.Bottom, other.Bottom) - Math.Max(rect.Top, other.Top);
                if (overlap >= Math.Min(rect.Height, other.Height) / 2)
                {
                    result[i] = (page, rect.Union(other));
                    merged = true;
                    break;
                }
            }

            if (!merged)
            {
                result.Add((page, rect));
            }
        }

        return result;
    }

    /// <summary>Returns the text of an element, with its white space collapsed.</summary>
    private static string GetText(Element element)
    {
        var text = element.TextContent ?? "";
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        var stack = new Stack<Element>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            yield return element;
            for (var child = element.LastElementChild; child is not null; child = child.PreviousElementSibling)
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>What a node belongs to: its element, the element of its structure element (null for an artifact), its link.</summary>
    private sealed record NodeInfo(Element Element, Element? StructureElement, Element? Link, bool IsFigure);

    private sealed class ElementRecord
    {
        public int FirstPage { get; set; } = -1;

        public PhysicalRect FirstBounds { get; set; }

        public bool HasContent { get; set; }

        public List<(int Page, PhysicalRect Rect)>? LinkAreas { get; set; }
    }
}
