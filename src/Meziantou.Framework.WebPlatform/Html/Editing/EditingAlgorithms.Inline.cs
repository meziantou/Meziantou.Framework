using System.Collections.Frozen;
using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using DomRange = Meziantou.Framework.WebPlatform.Dom.Range;

namespace Meziantou.Framework.WebPlatform.Html;

// The inline formatting algorithms: https://w3c.github.io/editing/docs/execCommand/#inline-formatting-command-definitions
internal sealed partial class EditingAlgorithms
{
    private static readonly string[] FontSizeKeywords = ["x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large"];

    /// <summary>The commands whose values are recorded and restored (https://w3c.github.io/editing/docs/execCommand/#record-the-values).</summary>
    private static readonly string[] RecordedCommands = ["subscript", "bold", "fontname", "fontsize", "forecolor", "hilitecolor", "italic", "strikethrough", "underline"];

    private static readonly FrozenSet<string> ModifiableElementNames = Names("b", "em", "i", "s", "span", "strike", "strong", "sub", "sup", "u");
    private static readonly FrozenSet<string> SimpleModifiableElementNames = Names("a", "b", "em", "font", "i", "s", "span", "strike", "strong", "sub", "sup", "u");

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#element-with-inline-contents</summary>
    private static readonly FrozenSet<string> NamesOfElementsWithInlineContents = Names(
        "a", "abbr", "b", "bdi", "bdo", "cite", "code", "dfn", "em", "h1", "h2", "h3", "h4", "h5", "h6", "i", "kbd", "mark", "p", "pre", "q",
        "rp", "rt", "ruby", "s", "samp", "small", "span", "strong", "sub", "sup", "u", "var", "acronym", "listing", "strike", "xmp", "big",
        "blink", "font", "marquee", "nobr", "tt");

    private static readonly FrozenSet<string> HeadingNames = Names("h1", "h2", "h3", "h4", "h5", "h6");
    private static readonly FrozenSet<string> TableStructureNames = Names("colgroup", "table", "tbody", "tfoot", "thead", "tr");
    private static readonly FrozenSet<string> RawTextNames = Names("script", "style", "plaintext", "xmp");
    private static readonly FrozenSet<string> TableChildNames = Names("caption", "col", "colgroup", "tbody", "td", "tfoot", "th", "thead", "tr");
    private static readonly FrozenSet<string> NeverAllowedChildNames = Names("body", "caption", "col", "colgroup", "frame", "frameset", "head", "html", "tbody", "td", "tfoot", "th", "thead", "tr");

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#relevant-css-property</summary>
    private static string? GetRelevantCssProperty(string command) => command switch
    {
        "backcolor" or "hilitecolor" => "background-color",
        "bold" => "font-weight",
        "fontname" => "font-family",
        "fontsize" => "font-size",
        "forecolor" => "color",
        "italic" => "font-style",
        _ => null,
    };

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#inline-command-activated-values</summary>
    private static string[]? GetInlineCommandActivatedValues(string command) => command switch
    {
        "bold" => ["bold", "600", "700", "800", "900"],
        "italic" => ["italic", "oblique"],
        "strikethrough" => ["line-through"],
        "subscript" => ["subscript"],
        "superscript" => ["superscript"],
        "underline" => ["underline"],
        _ => null,
    };

    private static bool IsActivatedValue(string command, string? value)
        => value is not null && GetInlineCommandActivatedValues(command) is { } values && Array.IndexOf(values, value) >= 0;

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#wrap: wraps a list of consecutive sibling nodes in a sibling that
    /// matches the criteria, or in the new parent given by the instructions.
    /// </summary>
    private Element? Wrap(List<Node> nodeList, Func<Node, bool>? siblingCriteria = null, Func<Element?>? newParentInstructions = null)
    {
        if (nodeList.Count == 0)
            return null;

        if (nodeList.TrueForAll(IsInvisible) && !nodeList.Exists(static node => IsHtmlElement(node, "br")))
            return null;

        if (nodeList[0].ParentNode is null)
            return null;

        if (IsInlineNode(nodeList[^1]) && !IsHtmlElement(nodeList[^1], "br") && IsHtmlElement(nodeList[^1].NextSibling, "br"))
        {
            nodeList.Add(nodeList[^1].NextSibling!);
        }

        while (IsInvisible(nodeList[0].PreviousSibling))
        {
            nodeList.Insert(0, nodeList[0].PreviousSibling!);
        }

        while (IsInvisible(nodeList[^1].NextSibling))
        {
            nodeList.Add(nodeList[^1].NextSibling!);
        }

        Element? newParent;
        if (IsEditable(nodeList[0].PreviousSibling) && siblingCriteria is not null && siblingCriteria(nodeList[0].PreviousSibling!))
        {
            newParent = (Element)nodeList[0].PreviousSibling!;
        }
        else if (IsEditable(nodeList[^1].NextSibling) && siblingCriteria is not null && siblingCriteria(nodeList[^1].NextSibling!))
        {
            newParent = (Element)nodeList[^1].NextSibling!;
        }
        else
        {
            newParent = newParentInstructions?.Invoke();
        }

        if (newParent is null)
            return null;

        if (newParent.ParentNode is null)
        {
            nodeList[0].ParentNode!.InsertBefore(newParent, nodeList[0]);

            // A boundary point of the active range that was before the nodes is now before the new parent: it stays with the nodes
            var range = ActiveRange;
            if (ReferenceEquals(range.StartContainer, newParent.ParentNode) && range.StartOffset == NodeIndex(newParent))
            {
                range.SetStart(range.StartContainer, range.StartOffset + 1);
            }

            if (ReferenceEquals(range.EndContainer, newParent.ParentNode) && range.EndOffset == NodeIndex(newParent))
            {
                range.SetEnd(range.EndContainer, range.EndOffset + 1);
            }
        }

        var originalParent = nodeList[0].ParentNode!;
        if (IsBefore(newParent, nodeList[0]))
        {
            if (!IsInlineNode(newParent) && IsInlineNode(LastVisibleChild(newParent)) && IsInlineNode(nodeList.Find(IsVisible)) && !IsHtmlElement(newParent.LastChild, "br"))
            {
                newParent.AppendChild(CreateElement("br"));
            }

            foreach (var node in nodeList)
            {
                MovePreservingRanges(node, newParent, -1);
            }
        }
        else
        {
            if (!IsInlineNode(newParent) && IsInlineNode(FirstVisibleChild(newParent)) && IsInlineNode(nodeList.FindLast(IsVisible)) && !IsHtmlElement(nodeList[^1], "br"))
            {
                newParent.InsertBefore(CreateElement("br"), newParent.FirstChild);
            }

            for (var i = nodeList.Count - 1; i >= 0; i--)
            {
                MovePreservingRanges(nodeList[i], newParent, 0);
            }
        }

        if (IsEditable(originalParent) && !originalParent.HasChildNodes())
        {
            RemoveNode(originalParent);
        }

        if (IsEditable(newParent.NextSibling) && siblingCriteria is not null && siblingCriteria(newParent.NextSibling!))
        {
            var next = newParent.NextSibling!;
            if (!IsInlineNode(newParent) && IsInlineNode(newParent.LastChild) && IsInlineNode(next.FirstChild) && !IsHtmlElement(newParent.LastChild, "br"))
            {
                newParent.AppendChild(CreateElement("br"));
            }

            while (next.FirstChild is { } child)
            {
                MovePreservingRanges(child, newParent, -1);
            }

            RemoveNode(next);
        }

        RemoveExtraneousLineBreaksFrom(newParent);
        return newParent;
    }

    private Node? FirstVisibleChild(Node node)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (IsVisible(child))
                return child;
        }

        return null;
    }

    private Node? LastVisibleChild(Node node)
    {
        for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
        {
            if (IsVisible(child))
                return child;
        }

        return null;
    }

    private static bool IsAllowedChild(Node child, Node parent) => IsAllowedChild(child, childName: null, parent, parentName: null);

    private static bool IsAllowedChild(string child, Node parent) => IsAllowedChild(childNode: null, child, parent, parentName: null);

    private static bool IsAllowedChild(Node child, string parent) => IsAllowedChild(child, childName: null, parentNode: null, parent);

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#allowed-child: whether a node, or an HTML element of a given name, is
    /// an allowed child of a node, or of an HTML element of a given name.
    /// </summary>
    private static bool IsAllowedChild(Node? childNode, string? childName, Node? parentNode, string? parentName)
    {
        var parentIsElementName = parentName is not null ? parentName : IsHtmlElement(parentNode) ? ((Element)parentNode).LocalName : null;
        if (parentIsElementName is not null && TableStructureNames.Contains(parentIsElementName) && childNode is Text text && text.Data.AsSpan().IndexOfAnyExcept(" \t\n\f\r") >= 0)
            return false;

        if (parentIsElementName is not null && RawTextNames.Contains(parentIsElementName) && childNode is not Text)
            return false;

        if (childNode is Document or DocumentFragment or DocumentType)
            return false;

        if (IsHtmlElement(childNode))
        {
            childName = ((Element)childNode).LocalName;
        }

        if (childName is null)
            return true;

        if (IsHtmlElement(parentNode))
        {
            for (Node? ancestor = parentNode; ancestor is not null; ancestor = ancestor.ParentNode)
            {
                if (childName == "a" && IsHtmlElement(ancestor, "a"))
                    return false;

                if (ProhibitedParagraphChildNames.Contains(childName) && IsHtmlElement(ancestor, NamesOfElementsWithInlineContents))
                    return false;

                if (HeadingNames.Contains(childName) && IsHtmlElement(ancestor, HeadingNames))
                    return false;
            }

            parentName = ((Element)parentNode).LocalName;
        }

        if (parentName is null)
            return parentNode is Element or DocumentFragment;

        switch (parentName)
        {
            case "colgroup":
                return childName == "col";
            case "table":
                return TableChildNames.Contains(childName);
            case "tbody" or "thead" or "tfoot":
                return childName is "td" or "th" or "tr";
            case "tr":
                return childName is "td" or "th";
            case "dl":
                return childName is "dt" or "dd";
            case "dir" or "ol" or "ul":
                return childName is "dir" or "li" or "ol" or "ul";
            case "hgroup":
                return HeadingNames.Contains(childName);
        }

        if (NeverAllowedChildNames.Contains(childName))
            return false;

        if (childName is "dd" or "dt" && parentName != "dl")
            return false;

        if (childName == "li" && parentName != "ol" && parentName != "ul")
            return false;

        return !(parentName == "a" && childName == "a")
            && !(parentName is "dd" or "dt" && childName is "dd" or "dt")
            && !(HeadingNames.Contains(parentName) && HeadingNames.Contains(childName))
            && !(parentName == "li" && childName == "li")
            && !(parentName == "nobr" && childName == "nobr")
            && !(NamesOfElementsWithInlineContents.Contains(parentName) && ProhibitedParagraphChildNames.Contains(childName))
            && !(parentName is "td" or "th" && TableChildNames.Contains(childName));
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#effectively-contained</summary>
    private static bool IsEffectivelyContained(Node? node, DomRange range)
    {
        if (node is null || range.Collapsed)
            return false;

        if (IsContained(node, range))
            return true;

        if (ReferenceEquals(node, range.StartContainer) && node is Text && NodeLength(node) != range.StartOffset)
            return true;

        if (ReferenceEquals(node, range.EndContainer) && node is Text && range.EndOffset != 0)
            return true;

        if (!node.HasChildNodes())
            return false;

        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (!IsEffectivelyContained(child, range))
                return false;
        }

        return (!IsDescendant(range.StartContainer, node) || range.StartContainer is not Text || range.StartOffset == 0)
            && (!IsDescendant(range.EndContainer, node) || range.EndContainer is not Text || range.EndOffset == NodeLength(range.EndContainer));
    }

    /// <summary>
    /// The nodes effectively contained in a range that match a condition, in tree order. Unless <paramref name="all"/> is
    /// set, the descendants of the nodes that are returned are omitted.
    /// </summary>
    private static List<Node> GetEffectivelyContainedNodes(DomRange range, Func<Node, bool>? condition = null, bool all = false)
    {
        var node = range.StartContainer;
        while (IsEffectivelyContained(node.ParentNode, range))
        {
            node = node.ParentNode!;
        }

        var stop = NextNodeDescendants(range.EndContainer);
        var nodes = new List<Node>();
        Node? current = node;
        while (current is not null && IsBefore(current, stop))
        {
            if (IsEffectivelyContained(current, range) && (condition is null || condition(current)))
            {
                nodes.Add(current);
                if (!all)
                {
                    current = NextNodeDescendants(current);
                    continue;
                }
            }

            current = NextNode(current);
        }

        return nodes;
    }

    private static List<Node> GetAllEffectivelyContainedNodes(DomRange range, Func<Node, bool>? condition = null) => GetEffectivelyContainedNodes(range, condition, all: true);

    /// <summary>
    /// The names of the properties of the declarations of the style attribute of an element, as they are written (a
    /// shorthand is one property, and the invalid and unrecognized properties are included).
    /// </summary>
    private static List<string> GetStylePropertyNames(Element element)
    {
        var names = new List<string>();
        if (element.GetAttributeValue("style") is not { } style)
            return names;

        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var i = 0; i <= style.Length; i++)
        {
            var c = i < style.Length ? style[i] : ';';
            if (i < style.Length)
            {
                if (quote != '\0')
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (c is '"' or '\'')
                {
                    quote = c;
                    continue;
                }

                if (c is '(' or '[' or '{')
                {
                    depth++;
                    continue;
                }

                if (c is ')' or ']' or '}')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (c != ';' || depth > 0)
                    continue;
            }

            var declaration = style.AsSpan(start, i - start);
            var colon = declaration.IndexOf(':');
            if (colon > 0 && declaration[..colon].Trim().Length > 0)
            {
                names.Add(Infra.InfraAscii.ToAsciiLowercase(declaration[..colon].Trim().ToString()));
            }

            start = i + 1;
        }

        return names;
    }

    private static string GetInlineStyleValue(Element element, string property)
        => element.HasAttributeWithLocalName("style") ? element.GetInlineStyle().GetPropertyValue(property) : "";

    /// <summary>The value of text-decoration (or of text-decoration-line) of the style attribute of the element.</summary>
    private static string GetInlineTextDecoration(Element element)
    {
        var value = GetInlineStyleValue(element, "text-decoration");
        return value.Length > 0 ? value : GetInlineStyleValue(element, "text-decoration-line");
    }

    private static void RemoveEmptyStyleAttribute(Element element)
    {
        if (element.GetAttributeValue("style") is { } style && string.IsNullOrWhiteSpace(style))
        {
            element.RemoveAttribute("style");
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#modifiable-element</summary>
    private static bool IsModifiableElement(Node? node)
    {
        if (node is not Element { IsHtmlNamespace: true } element)
            return false;

        var count = element.AttributeCount;
        if (ModifiableElementNames.Contains(element.LocalName))
            return count == 0 || (count == 1 && element.HasAttributeWithLocalName("style"));

        if (element.LocalName is "font" or "a")
        {
            if (element.HasAttributeWithLocalName("style"))
            {
                count--;
            }

            if (element.LocalName == "font")
            {
                foreach (var name in (ReadOnlySpan<string>)["color", "face", "size"])
                {
                    if (element.HasAttributeWithLocalName(name))
                    {
                        count--;
                    }
                }
            }
            else if (element.HasAttributeWithLocalName("href"))
            {
                count--;
            }

            return count == 0;
        }

        return false;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#simple-modifiable-element</summary>
    private static bool IsSimpleModifiableElement(Node? node)
    {
        if (node is not Element { IsHtmlNamespace: true } element || !SimpleModifiableElementNames.Contains(element.LocalName))
            return false;

        if (element.AttributeCount == 0)
            return true;

        if (element.AttributeCount > 1)
            return false;

        var name = element.LocalName;
        if (name == "a" && element.HasAttributeWithLocalName("href"))
            return true;

        if (name == "font" && (element.HasAttributeWithLocalName("color") || element.HasAttributeWithLocalName("face") || element.HasAttributeWithLocalName("size")))
            return true;

        if (!element.HasAttributeWithLocalName("style"))
            return false;

        var properties = GetStylePropertyNames(element);
        if (properties.Count == 0)
            return true;

        if (properties.Count != 1)
            return false;

        var property = properties[0];
        if (name is "b" or "strong" && property == "font-weight")
            return true;

        if (name is "i" or "em" && property == "font-style")
            return true;

        if (name is "a" or "font" or "span" && property != "text-decoration")
            return true;

        return name is "a" or "font" or "s" or "span" or "strike" or "u" && property == "text-decoration"
            && GetInlineTextDecoration(element) is "line-through" or "underline" or "overline" or "none";
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#formattable-node</summary>
    private bool IsFormattableNode(Node node)
        => IsEditable(node) && IsVisible(node) && (node is Text || IsHtmlElement(node, "img") || IsHtmlElement(node, "br"));

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#equivalent-values</summary>
    private bool AreEquivalentValues(string command, string? value1, string? value2)
    {
        if (value1 is null || value2 is null)
            return value1 is null && value2 is null;

        switch (command)
        {
            case "bold":
                return value1 == value2 || (value1 == "bold" && value2 == "700") || (value1 == "700" && value2 == "bold") || (value1 == "normal" && value2 == "400") || (value1 == "400" && value2 == "normal");

            case "backcolor" or "forecolor" or "hilitecolor":
                return NormalizeColor(value1) == NormalizeColor(value2);

            default:
                return value1 == value2;
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#loosely-equivalent-values</summary>
    private bool AreLooselyEquivalentValues(string command, string? value1, string? value2)
    {
        if (AreEquivalentValues(command, value1, value2))
            return true;

        if (command != "fontsize" || value1 is null || value2 is null)
            return false;

        // One of the values is a keyword and the other one is the computed size of a font element of this size
        return GetFontSizeKeywordPixels(value2) == value1 || GetFontSizeKeywordPixels(value1) == value2;
    }

    private string? GetFontSizeKeywordPixels(string keyword)
    {
        var index = Array.IndexOf(FontSizeKeywords, keyword);
        if (index < 0)
            return null;

        _fontSizes ??= new string?[FontSizeKeywords.Length];
        return _fontSizes[index] ??= GetProbeValue("font", element => element.SetAttribute("size", (index + 1).ToString(CultureInfo.InvariantCulture)), "font-size");
    }

    private string?[]? _fontSizes;

    /// <summary>The resolved value of a property of an element that is not in the document (the initial values are inherited).</summary>
    private string GetProbeValue(string localName, Action<Element> initialize, string property)
    {
        var element = CreateElement(localName);
        initialize(element);
        return GetStyle(element).GetResolvedValue(property);
    }

    /// <summary>
    /// The computed value of a color, <c>rgb(r, g, b)</c> or <c>rgba(r, g, b, a)</c>, or null when the value is
    /// <c>currentcolor</c>. As in the reference implementation of the specification, whose results the tests expect, a
    /// value that is not a valid color is black (the color of an element with this value in the reference implementation).
    /// </summary>
    private string? NormalizeColor(string color)
    {
        if (Infra.InfraAscii.EqualsIgnoreAsciiCase(color, "currentcolor"))
            return null;

        _normalizedColors ??= new(StringComparer.Ordinal);
        if (_normalizedColors.TryGetValue(color, out var cached))
            return cached;

        var result = "rgb(0, 0, 0)";
        var probe = CreateElement("span");
        var style = probe.GetInlineStyle();
        style.SetProperty("color", color);
        if (style.GetPropertyValue("color").Length > 0)
        {
            result = GetStyle(probe).GetResolvedValue("color");
            if (result.StartsWith("rgba(", StringComparison.Ordinal) && result.EndsWith(", 1)", StringComparison.Ordinal))
            {
                result = string.Concat("rgb(", result.AsSpan(5, result.Length - 9), ")");
            }
            else if (result == "transparent")
            {
                result = "rgba(0, 0, 0, 0)";
            }
        }

        _normalizedColors[color] = result;
        return result;
    }

    /// <summary>The color as <c>#rrggbb</c> when it is a valid opaque color in the sRGB color space, else null.</summary>
    private string? ParseSimpleColor(string color)
    {
        if (NormalizeColor(color) is not { } normalized || !normalized.StartsWith("rgb(", StringComparison.Ordinal))
            return null;

        var parts = normalized[4..^1].Split(", ");
        if (parts.Length != 3 || !byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var red) || !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var green) || !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var blue))
            return null;

        return string.Create(CultureInfo.InvariantCulture, $"#{red:x2}{green:x2}{blue:x2}");
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#effective-command-value</summary>
    private string? GetEffectiveCommandValue(Node? node, string command)
    {
        if (node is null)
            return null;

        if (node is not Element)
        {
            node = node.ParentNode;
            if (node is not Element)
                return null;
        }

        var element = (Element)node;
        switch (command)
        {
            case "createlink" or "unlink":
                for (Node? current = element; current is not null; current = current.ParentNode)
                {
                    if (IsHtmlElement(current, "a") && ((Element)current).GetAttributeValue("href") is { } href)
                        return href;
                }

                return null;

            case "backcolor" or "hilitecolor":
                while (GetStyle(element).GetResolvedValue("background-color") is "rgba(0, 0, 0, 0)" or "" or "transparent" && element.ParentNode is Element parent)
                {
                    element = parent;
                }

                return GetStyle(element).GetResolvedValue("background-color");

            case "subscript" or "superscript":
            {
                var affectedBySubscript = false;
                var affectedBySuperscript = false;
                for (Node? current = element; IsInlineNode(current); current = current.ParentNode)
                {
                    affectedBySubscript |= IsHtmlElement(current, "sub");
                    affectedBySuperscript |= IsHtmlElement(current, "sup");
                }

                return affectedBySubscript && affectedBySuperscript ? "mixed" : affectedBySubscript ? "subscript" : affectedBySuperscript ? "superscript" : null;
            }

            case "strikethrough" or "underline":
            {
                var keyword = command == "strikethrough" ? "line-through" : "underline";
                for (Node? current = element; current is Element ancestor; current = current.ParentNode)
                {
                    if (GetStyle(ancestor).GetComputedValue("text-decoration-line").Contains(keyword, StringComparison.Ordinal))
                        return keyword;
                }

                return null;
            }
        }

        return GetStyle(element).GetResolvedValue(GetRelevantCssProperty(command)!);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#specified-command-value</summary>
    private string? GetSpecifiedCommandValue(Element element, string command)
    {
        switch (command)
        {
            case "backcolor" or "hilitecolor" when !IsDisplayInline(element):
                return null;

            case "createlink" or "unlink":
                return IsHtmlElement(element, "a") ? element.GetAttributeValue("href") : null;

            case "subscript" or "superscript":
                return IsHtmlElement(element, "sup") ? "superscript" : IsHtmlElement(element, "sub") ? "subscript" : null;

            case "strikethrough" or "underline":
            {
                var keyword = command == "strikethrough" ? "line-through" : "underline";
                if (GetInlineTextDecoration(element) is { Length: > 0 } decoration)
                    return decoration.Contains(keyword, StringComparison.Ordinal) ? keyword : null;

                if (command == "strikethrough" ? (IsHtmlElement(element, "s") || IsHtmlElement(element, "strike")) : IsHtmlElement(element, "u"))
                    return keyword;

                return null;
            }
        }

        var property = GetRelevantCssProperty(command);
        if (property is null)
            return null;

        if (GetInlineStyleValue(element, property) is { Length: > 0 } value)
            return value;

        if (!element.IsHtmlNamespace)
            return null;

        if (element.LocalName == "font")
        {
            if (property == "color" && element.GetAttributeValue("color") is { } color)
                return color;

            if (property == "font-family" && element.GetAttributeValue("face") is { } face)
                return face;

            if (property == "font-size" && element.GetAttributeValue("size") is { } size)
            {
                // https://html.spec.whatwg.org/multipage/rendering.html#rules-for-parsing-a-legacy-font-size
                var span = size.AsSpan().TrimStart(" \t\n\f\r");
                var relative = span.Length > 0 && span[0] is '+' or '-';
                var end = relative ? 1 : 0;
                while (end < span.Length && char.IsAsciiDigit(span[end]))
                {
                    end++;
                }

                if (!int.TryParse(span[(relative ? 1 : 0)..end], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    return null;

                if (relative)
                {
                    number = span[0] == '+' ? 3 + number : 3 - number;
                }

                return FontSizeKeywords[Math.Clamp(number, 1, 7) - 1];
            }
        }

        if (property == "font-weight" && element.LocalName is "b" or "strong")
            return "bold";

        if (property == "font-style" && element.LocalName is "i" or "em")
            return "italic";

        return null;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#reorder-modifiable-descendants</summary>
    private void ReorderModifiableDescendants(Node? node, string command, string newValue)
    {
        if (node is null)
            return;

        var candidate = node;
        while (IsModifiableElement(candidate) && candidate.ChildCount == 1 && IsModifiableElement(candidate.FirstChild)
            && (!IsSimpleModifiableElement(candidate) || !AreEquivalentValues(command, GetSpecifiedCommandValue((Element)candidate, command), newValue)))
        {
            candidate = candidate.FirstChild!;
        }

        if (ReferenceEquals(candidate, node) || !IsSimpleModifiableElement(candidate)
            || !AreEquivalentValues(command, GetSpecifiedCommandValue((Element)candidate, command), newValue)
            || !AreLooselyEquivalentValues(command, GetEffectiveCommandValue(candidate, command), newValue))
        {
            return;
        }

        while (candidate.FirstChild is { } child)
        {
            MovePreservingRanges(child, candidate.ParentNode!, NodeIndex(candidate));
        }

        node.ParentNode!.InsertBefore(candidate, node.NextSibling);
        MovePreservingRanges(node, candidate, -1);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#record-the-values</summary>
    private List<(Node Node, string Command, string? Value)> RecordValues(List<Node> nodeList)
    {
        var values = new List<(Node, string, string?)>();
        foreach (var node in nodeList)
        {
            foreach (var command in RecordedCommands)
            {
                var ancestor = node as Element ?? node.ParentNode as Element;
                string? value = null;
                while (ancestor is not null && (value = GetSpecifiedCommandValue(ancestor, command)) is null)
                {
                    ancestor = ancestor.ParentNode as Element;
                }

                values.Add((node, command, value));
            }
        }

        return values;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#restore-the-values</summary>
    private void RestoreValues(List<(Node Node, string Command, string? Value)>? values)
    {
        if (values is null)
            return;

        foreach (var (node, command, value) in values)
        {
            var ancestor = node as Element ?? node.ParentNode as Element;
            string? specified = null;
            while (ancestor is not null && (specified = GetSpecifiedCommandValue(ancestor, command)) is null)
            {
                ancestor = ancestor.ParentNode as Element;
            }

            if (value is null && ancestor is not null)
            {
                PushDownValues(node, command, newValue: null);
            }
            else if ((ancestor is not null && !AreEquivalentValues(command, specified, value)) || (ancestor is null && value is not null))
            {
                ForceValue(node, command, value);
            }
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#clear-the-value</summary>
    private List<Node> ClearValue(Element element, string command)
    {
        if (!IsEditable(element) || GetSpecifiedCommandValue(element, command) is null)
            return [];

        if (IsSimpleModifiableElement(element))
        {
            var children = GetChildren(element);
            foreach (var child in children)
            {
                MovePreservingRanges(child, element.ParentNode!, NodeIndex(element));
            }

            RemoveNode(element);
            return children;
        }

        if (command is "strikethrough" or "underline")
        {
            var keyword = command == "strikethrough" ? "line-through" : "underline";
            var decoration = GetInlineTextDecoration(element);
            if (decoration.Contains(keyword, StringComparison.Ordinal))
            {
                var remaining = decoration.Replace(keyword, "", StringComparison.Ordinal).Trim();
                element.GetInlineStyle().RemoveProperty("text-decoration");
                element.GetInlineStyle().RemoveProperty("text-decoration-line");
                if (remaining.Length > 0)
                {
                    element.GetInlineStyle().SetProperty("text-decoration", remaining);
                }

                RemoveEmptyStyleAttribute(element);
            }
        }

        if (GetRelevantCssProperty(command) is { } property && element.HasAttributeWithLocalName("style"))
        {
            element.GetInlineStyle().RemoveProperty(property);
            RemoveEmptyStyleAttribute(element);
        }

        if (IsHtmlElement(element, "font"))
        {
            switch (command)
            {
                case "forecolor":
                    element.RemoveAttribute("color");
                    break;
                case "fontname":
                    element.RemoveAttribute("face");
                    break;
                case "fontsize":
                    element.RemoveAttribute("size");
                    break;
            }
        }

        if (IsHtmlElement(element, "a") && command is "createlink" or "unlink")
        {
            element.RemoveAttribute("href");
        }

        if (GetSpecifiedCommandValue(element, command) is null)
            return [];

        return [SetTagName(element, "span")];
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#push-down-values</summary>
    private void PushDownValues(Node node, string command, string? newValue)
    {
        if (node.ParentNode is not Element)
            return;

        if (AreLooselyEquivalentValues(command, GetEffectiveCommandValue(node, command), newValue))
            return;

        var currentAncestor = node.ParentNode;
        var ancestorList = new List<Element>();
        while (IsEditable(currentAncestor) && currentAncestor is Element element && !AreLooselyEquivalentValues(command, GetEffectiveCommandValue(element, command), newValue))
        {
            ancestorList.Add(element);
            currentAncestor = element.ParentNode;
        }

        if (ancestorList.Count == 0)
            return;

        var propagatedValue = GetSpecifiedCommandValue(ancestorList[^1], command);
        if (propagatedValue is null && newValue is not null)
            return;

        if (newValue is not null && !AreLooselyEquivalentValues(command, GetEffectiveCommandValue(ancestorList[^1].ParentNode, command), newValue))
            return;

        while (ancestorList.Count > 0)
        {
            var ancestor = ancestorList[^1];
            ancestorList.RemoveAt(ancestorList.Count - 1);
            var specified = GetSpecifiedCommandValue(ancestor, command);
            if (specified is not null)
            {
                propagatedValue = specified;
            }

            var children = GetChildren(ancestor);

            // The value of an s or strike element is pushed down to elements of the same name
            var lineThroughElementName = _lineThroughElementName;
            if (command == "strikethrough" && ancestor.IsHtmlNamespace && ancestor.LocalName is "s" or "strike")
            {
                _lineThroughElementName = ancestor.LocalName;
            }

            if (specified is not null)
            {
                ClearValue(ancestor, command);
            }

            foreach (var child in children)
            {
                if (ReferenceEquals(child, node))
                    continue;

                if (child is Element childElement && GetSpecifiedCommandValue(childElement, command) is { } childValue && !AreEquivalentValues(command, propagatedValue, childValue))
                    continue;

                if (ancestorList.Count > 0 && ReferenceEquals(child, ancestorList[^1]))
                    continue;

                ForceValue(child, command, propagatedValue);
            }

            _lineThroughElementName = lineThroughElementName;
        }
    }

    /// <summary>
    /// The local name of the element created for the line-through value of strikethrough when the CSS styling flag is not
    /// set. The specification creates an s element. As in the browsers (and as the tests expect), the strikethrough command
    /// creates a strike element, and the value of a strike element is pushed down to strike elements.
    /// </summary>
    private string _lineThroughElementName = "s";

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#force-the-value</summary>
    private void ForceValue(Node node, string command, string? newValue)
    {
        if (node.ParentNode is null || newValue is null)
            return;

        if (IsAllowedChild(node, "span"))
        {
            ReorderModifiableDescendants(node.PreviousSibling, command, newValue);
            ReorderModifiableDescendants(node.NextSibling, command, newValue);

            Wrap(
                [node],
                sibling => IsSimpleModifiableElement(sibling)
                    && AreEquivalentValues(command, GetSpecifiedCommandValue((Element)sibling, command), newValue)
                    && AreLooselyEquivalentValues(command, GetEffectiveCommandValue(sibling, command), newValue));
        }

        if (IsInvisible(node))
            return;

        if (AreLooselyEquivalentValues(command, GetEffectiveCommandValue(node, command), newValue))
            return;

        if (!IsAllowedChild(node, "span"))
        {
            ForceValueOfChildren(node, command, newValue);
            return;
        }

        if (AreLooselyEquivalentValues(command, GetEffectiveCommandValue(node, command), newValue))
            return;

        Element? newParent = null;
        if (!_state.CssStyling)
        {
            switch (command)
            {
                case "bold" when newValue is "bold" or "700":
                    newParent = CreateElement("b");
                    break;
                case "italic" when newValue == "italic":
                    newParent = CreateElement("i");
                    break;
                case "strikethrough" when newValue == "line-through":
                    newParent = CreateElement(_lineThroughElementName);
                    break;
                case "underline" when newValue == "underline":
                    newParent = CreateElement("u");
                    break;
                case "forecolor" when ParseSimpleColor(newValue) is { } color:
                    newParent = CreateElement("font");
                    newParent.SetAttribute("color", color);
                    break;
                case "fontname":
                    newParent = CreateElement("font");
                    newParent.SetAttribute("face", newValue);
                    break;
            }
        }

        if (command is "createlink" or "unlink")
        {
            newParent = CreateElement("a");
            newParent.SetAttribute("href", newValue);
            for (var ancestor = node.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
            {
                if (IsHtmlElement(ancestor, "a"))
                {
                    ancestor = SetTagName((Element)ancestor, "span");
                }
            }
        }

        if (command == "fontsize" && Array.IndexOf(FontSizeKeywords, newValue) is >= 0 and var sizeIndex && (!_state.CssStyling || newValue == "xxx-large"))
        {
            newParent = CreateElement("font");
            newParent.SetAttribute("size", (sizeIndex + 1).ToString(CultureInfo.InvariantCulture));
        }

        if (command is "subscript" or "superscript" && newValue is "subscript" or "superscript")
        {
            newParent = CreateElement(newValue == "subscript" ? "sub" : "sup");
        }

        newParent ??= CreateElement("span");
        node.ParentNode.InsertBefore(newParent, node);
        if (GetRelevantCssProperty(command) is { } property && !AreLooselyEquivalentValues(command, GetEffectiveCommandValue(newParent, command), newValue))
        {
            newParent.GetInlineStyle().SetProperty(property, newValue);
        }

        if (command is "strikethrough" or "underline" && newValue == (command == "strikethrough" ? "line-through" : "underline") && GetEffectiveCommandValue(newParent, command) != newValue)
        {
            newParent.GetInlineStyle().SetProperty("text-decoration", newValue);
        }

        // The specification compares the font sizes strictly: the keyword of the new value is never the pixel size of
        // the node, so that the font element is always moved in the element (<b><font size="5">). As in the browsers, the
        // font element stays around the elements that have contents (<font size="5"><b>)
        MovePreservingRanges(node, newParent, -1);
        if (node is Element && !(command == "fontsize" && node.HasChildNodes() ? AreLooselyEquivalentValues(command, GetEffectiveCommandValue(node, command), newValue) : AreEquivalentValues(command, GetEffectiveCommandValue(node, command), newValue)))
        {
            MovePreservingRanges(node, newParent.ParentNode!, NodeIndex(newParent));
            RemoveNode(newParent);
            ForceValueOfChildren(node, command, newValue);
        }
    }

    /// <summary>Forces the value of the children of a node, except the elements that specify another value.</summary>
    private void ForceValueOfChildren(Node node, string command, string newValue)
    {
        var children = new List<Node>();
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element element && GetSpecifiedCommandValue(element, command) is { } specified && !AreEquivalentValues(command, newValue, specified))
                continue;

            children.Add(child);
        }

        foreach (var child in children)
        {
            ForceValue(child, command, newValue);
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#set-the-selection's-value</summary>
    private void SetSelectionValue(string command, string? newValue)
    {
        var range = ActiveRange;
        if (!GetAllEffectivelyContainedNodes(range).Exists(IsFormattableNode))
        {
            if (GetInlineCommandActivatedValues(command) is { } activatedValues)
            {
                _state.SetStateOverride(range, command, newValue is not null && Array.IndexOf(activatedValues, newValue) >= 0);
            }

            if (command == "subscript")
            {
                _state.UnsetStateOverride(range, "superscript");
            }
            else if (command == "superscript")
            {
                _state.UnsetStateOverride(range, "subscript");
            }

            if (newValue is null)
            {
                _state.UnsetValueOverride(range, command);
            }
            else if (command == "createlink" || HasValue(command))
            {
                _state.SetValueOverride(range, command, newValue);
            }

            return;
        }

        SplitTextAtBoundaryPoints();

        // The specification forces the value of the outermost nodes that are effectively contained: bold in <i>[foo]</i>
        // gives <b><i>foo</i></b>. As in the browsers (and as the tests expect), when the range is in one inline element
        // and contains all its contents, the value is forced on the contents of this element: <i><b>foo</b></i>. The font
        // elements (fontName, fontSize, foreColor) stay around the other inline elements, and a link that is replaced
        // stays around its contents.
        var wholeInlineElements = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        if (newValue is not null && command is not ("fontname" or "fontsize" or "forecolor"))
        {
            var outermost = GetEffectivelyContainedNodes(range, static node => IsEditable(node));
            while (outermost is [Element element] && element.HasChildNodes())
            {
                // The value is never forced on the blocks (it is forced on their children)
                if (IsAllowedChild(element, "span"))
                {
                    if (!IsDisplayInline(element) || IsAtomicElement(element) || element.HasAttributeWithLocalName("style")
                        || !(element.IsInclusiveAncestorOf(range.StartContainer) || element.IsInclusiveAncestorOf(range.EndContainer))
                        || (command is "createlink" or "unlink" && IsHtmlElement(element, "a")))
                    {
                        break;
                    }

                    wholeInlineElements.Add(element);
                }

                outermost = GetChildren(element).FindAll(static child => IsEditable(child));
            }
        }

        foreach (var element in GetAllEffectivelyContainedNodes(range, static node => node is Element && IsEditable(node)))
        {
            ClearValue((Element)element, command);
        }

        foreach (var node in GetAllEffectivelyContainedNodes(range, static node => IsEditable(node)))
        {
            if (newValue is not null)
            {
                SplitAncestorsWithAnotherValue(node, command, newValue);
            }

            PushDownValues(node, command, newValue);
            if (IsAllowedChild(node, "span") && !wholeInlineElements.Contains(node))
            {
                ForceValue(node, command, newValue);
            }
        }
    }

    /// <summary>
    /// Moves a node out of its inline ancestors that are modifiable elements with another value of the fontName, fontSize or
    /// foreColor command (e.g. a font element of another size), by splitting them. The specification nests the element of
    /// the new value in them; the browsers split them for these commands, which the tests expect. The values of the other
    /// commands are preserved.
    /// </summary>
    private void SplitAncestorsWithAnotherValue(Node node, string command, string newValue)
    {
        if (command is not ("fontname" or "fontsize" or "forecolor") || !IsAllowedChild(node, "span"))
            return;

        while (true)
        {
            Element? ancestorWithValue = null;
            for (var ancestor = node.ParentNode; IsEditable(ancestor) && ancestor is Element element && IsInlineNode(element) && InSameEditingHost(element, node); ancestor = element.ParentNode)
            {
                if (IsModifiableElement(element) && GetSpecifiedCommandValue(element, command) is { } value && !AreEquivalentValues(command, value, newValue))
                {
                    ancestorWithValue = element;
                }
            }

            if (ancestorWithValue is null || !IsEditableOrEditingHost(ancestorWithValue.ParentNode))
                return;

            // When the parent of the ancestor has the new value, the value of the ancestor is pushed down instead
            if (AreLooselyEquivalentValues(command, GetEffectiveCommandValue(ancestorWithValue.ParentNode, command), newValue))
                return;

            var values = RecordValues([node]);
            values.RemoveAll(value => value.Command == command);
            while (IsDescendant(node, ancestorWithValue))
            {
                var parent = node.ParentNode;
                SplitParent([node]);
                if (ReferenceEquals(node.ParentNode, parent))
                    return;
            }

            RestoreValues(values);
        }
    }

    /// <summary>
    /// Splits the Text nodes of the boundary points of the active range that are in the middle of an editable Text node, so
    /// that the active range contains whole Text nodes.
    /// </summary>
    private void SplitTextAtBoundaryPoints()
    {
        var range = ActiveRange;
        if (IsEditable(range.StartContainer) && range.StartContainer is Text startText && range.StartOffset != 0 && range.StartOffset != startText.Length)
        {
            var endsInSameNode = ReferenceEquals(range.StartContainer, range.EndContainer);
            var newEndOffset = range.EndOffset - range.StartOffset;
            var newNode = startText.SplitText(range.StartOffset);
            if (endsInSameNode)
            {
                range.SetEnd(newNode, newEndOffset);
            }

            range.SetStart(newNode, 0);
        }

        if (IsEditable(range.EndContainer) && range.EndContainer is Text endText && range.EndOffset != 0 && range.EndOffset != endText.Length)
        {
            var (startNode, startOffset, endNode, endOffset) = (range.StartContainer, range.StartOffset, range.EndContainer, range.EndOffset);
            endText.SplitText(endOffset);
            range.SetStart(startNode, startOffset);
            range.SetEnd(endNode, endOffset);
        }
    }
}
