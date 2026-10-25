using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.Sanitizers;

// The tree builder of the HTML parser never puts some elements in some others: a start tag closes an open element
// (a button closes the button in scope, a div closes the p in button scope, an li closes the open li, ...), is ignored
// (a form in a form, a tr outside of a table), or moves the content (the text of a table is foster parented). The parser
// can still build such a tree when an element separates them, such as a template or an object, and unwrapping that
// element, or an element the parser inserted (tbody), puts them together. A browser would read the output back with
// another structure, so these elements are unwrapped too.
// https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody
public sealed partial class HtmlSanitizer
{
    /// <summary>The open elements of the parser that change how the start tag of a child is handled.</summary>
    [Flags]
    private enum OpenElements
    {
        None = 0,

        // An a element, with no marker of the list of active formatting elements between
        Link = 1 << 0,

        // A form element, with no template between
        Form = 1 << 1,

        // A button, nobr, select or ruby element in scope
        Button = 1 << 2,
        Nobr = 1 << 3,
        Select = 1 << 4,
        Ruby = 1 << 5,

        // A p element in button scope
        Paragraph = 1 << 6,

        // An li (or a dd or dt) element, with only address, div, p or elements that are not special between
        ListItem = 1 << 7,
        DefinitionItem = 1 << 8,

        // The parent is the content of a template that only contains parts of a table of one kind, which the parser
        // accepts there (see HasOnlyOneKindOfTableParts)
        TemplateContent = 1 << 9,
    }

    private const OpenElements InScope = OpenElements.Button | OpenElements.Nobr | OpenElements.Select | OpenElements.Ruby | OpenElements.Paragraph;

    // The element is a kept element, so an element of the HTML namespace
    private OpenElements GetChildrenState(Element element, OpenElements state)
    {
        var name = element.LocalName;
        state &= ~OpenElements.TemplateContent;
        if (IsFormattingScope(element))
        {
            state &= ~OpenElements.Link;
        }

        if (name is "applet" or "caption" or "html" or "table" or "td" or "th" or "marquee" or "object" or "template")
        {
            state &= ~InScope;
        }

        if (IsSpecial(name) && name is not ("address" or "div" or "p"))
        {
            state &= ~(OpenElements.ListItem | OpenElements.DefinitionItem);
        }

        return name switch
        {
            "a" => state | OpenElements.Link,
            "form" => state | OpenElements.Form,
            "button" => (state | OpenElements.Button) & ~OpenElements.Paragraph,
            "nobr" => state | OpenElements.Nobr,
            "select" => state | OpenElements.Select,
            "ruby" => state | OpenElements.Ruby,
            "p" => state | OpenElements.Paragraph,
            "li" => state | OpenElements.ListItem,
            "dd" or "dt" => state | OpenElements.DefinitionItem,
            "template" when HasOnlyOneKindOfTableParts(((HtmlTemplateElement)element).Content) => (state & ~OpenElements.Form) | OpenElements.TemplateContent,
            "template" => state & ~OpenElements.Form,
            _ => state,
        };
    }

    // The element is allowed, and its parent is a kept element or the root of a fragment
    private bool IsMisnested(Element element, Node parent, OpenElements state)
    {
        if (element.NamespaceUri != HtmlNamespace)
            return false;

        var name = element.LocalName;
        if (state.HasFlag(OpenElements.Paragraph) && ClosesParagraph(name))
            return true;

        var parentName = parent is Element parentElement && parentElement.NamespaceUri == HtmlNamespace ? parentElement.LocalName : null;
        var isInTemplateContent = state.HasFlag(OpenElements.TemplateContent);
        var isMisnested = name switch
        {
            "a" => state.HasFlag(OpenElements.Link),
            "form" => state.HasFlag(OpenElements.Form),
            "button" => state.HasFlag(OpenElements.Button),
            "nobr" => state.HasFlag(OpenElements.Nobr),
            "select" or "input" => state.HasFlag(OpenElements.Select),
            "li" => state.HasFlag(OpenElements.ListItem),
            "dd" or "dt" => state.HasFlag(OpenElements.DefinitionItem),
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => parentName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6",

            // These start tags generate the implied end tags, which close the parent
            "option" => state.HasFlag(OpenElements.Select) ? parentName is not "optgroup" && HasImpliedEndTag(parentName) : parentName is "option",
            "optgroup" => state.HasFlag(OpenElements.Select) ? HasImpliedEndTag(parentName) : parentName is "option",
            "hr" => state.HasFlag(OpenElements.Select) && HasImpliedEndTag(parentName),
            "rb" or "rtc" => state.HasFlag(OpenElements.Ruby) && HasImpliedEndTag(parentName),
            "rp" or "rt" => state.HasFlag(OpenElements.Ruby) && parentName is not "rtc" && HasImpliedEndTag(parentName),

            // The parser ignores the elements of a table outside of their parent (and inserts a tbody element
            // between a table and a tr element, or a colgroup between a table and a col element)
            "caption" or "colgroup" or "tbody" or "thead" or "tfoot" => !isInTemplateContent && parentName is not "table",
            "col" => !isInTemplateContent && parentName is not "colgroup",
            "tr" => !isInTemplateContent && parentName is not ("tbody" or "thead" or "tfoot"),
            "td" or "th" => !isInTemplateContent && parentName is not "tr",
            _ => false,
        };

        if (isMisnested)
            return true;

        return name is "table" or "tbody" or "thead" or "tfoot" or "tr" or "colgroup" && HasMisplacedTableContent(element);
    }

    /// <summary>
    /// Determines whether a table, a table section, a row or a column group would contain, once sanitized, content the
    /// parser moves (foster parenting) or that closes it: text that is not white space, or an element that is not
    /// one of its parts. The parser never builds such a table, but unwrapping an element it contains can.
    /// </summary>
    private bool HasMisplacedTableContent(Element container)
    {
        var containerName = container.LocalName;
        var pendingNodes = new Stack<Node>();
        pendingNodes.Push(container);
        while (pendingNodes.TryPop(out var parent))
        {
            for (var node = parent.FirstChild; node is not null; node = node.NextSibling)
            {
                switch (node)
                {
                    case Text text when text.Data.AsSpan().IndexOfAnyExcept("\t\n\f\r ") >= 0:
                        return true;

                    case Element element when BlockedElements.Contains(element.LocalName) || HasRawTextContent(element):
                        break;

                    // A section or a row with misplaced content is unwrapped too, which puts its content in the
                    // container. The recursion is bounded: a table part only contains the next kind of parts.
                    case Element element when IsValidElement(element) && IsTablePart(containerName, element.LocalName):
                        if (element.LocalName is "tbody" or "thead" or "tfoot" or "tr" or "colgroup" && HasMisplacedTableContent(element))
                        {
                            pendingNodes.Push(element);
                        }

                        break;

                    // An empty form element, and a hidden input element, are kept in a table (except in a column group)
                    case Element { LocalName: "form", FirstChild: null } when containerName is not "colgroup":
                        break;

                    case Element { LocalName: "input" } element when containerName is not "colgroup" && (!IsValidElement(element) || (IsValidAttribute("type") && string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase))):
                        break;

                    // An element that is unwrapped puts its content in the container
                    case Element element when !IsValidElement(element) || IsAnyTablePart(element.LocalName):
                        pendingNodes.Push(element is HtmlTemplateElement template ? template.Content : element);
                        break;

                    case Element:
                        return true;
                }
            }
        }

        return false;

        static bool IsAnyTablePart(string name) => name is "caption" or "colgroup" or "col" or "tbody" or "thead" or "tfoot" or "tr" or "td" or "th";
    }

    /// <summary>
    /// Determines whether the content of a template, once sanitized, is only made of parts of a table of one kind
    /// (captions, column groups and sections; rows; cells; or columns) and of white space. The first start tag of the
    /// content decides how the parser reads the next ones: after a div, it ignores a tr, and after a tr, a tbody.
    /// </summary>
    private bool HasOnlyOneKindOfTableParts(Node content)
    {
        var kind = 0;
        var pendingNodes = new Stack<Node>();
        pendingNodes.Push(content);
        while (pendingNodes.TryPop(out var parent))
        {
            for (var node = parent.FirstChild; node is not null; node = node.NextSibling)
            {
                switch (node)
                {
                    case Text text when text.Data.AsSpan().IndexOfAnyExcept("\t\n\f\r ") >= 0:
                        return false;

                    case Element element when BlockedElements.Contains(element.LocalName) || HasRawTextContent(element):
                        break;

                    case Element element when !IsValidElement(element) || (element.LocalName is "tbody" or "thead" or "tfoot" or "tr" or "colgroup" && HasMisplacedTableContent(element)):
                        pendingNodes.Push(element is HtmlTemplateElement template ? template.Content : element);
                        break;

                    case Element { LocalName: "template" }:
                        break;

                    case Element element:
                        var elementKind = element.LocalName switch
                        {
                            "caption" or "colgroup" or "tbody" or "thead" or "tfoot" => 1,
                            "tr" => 2,
                            "td" or "th" => 3,
                            "col" => 4,
                            _ => -1,
                        };

                        if (elementKind < 0 || (kind != 0 && kind != elementKind))
                            return false;

                        kind = elementKind;
                        break;
                }
            }
        }

        return true;
    }

    private static bool IsTablePart(string containerName, string name) => name is "template" || containerName switch
    {
        "table" => name is "caption" or "colgroup" or "tbody" or "thead" or "tfoot",
        "tbody" or "thead" or "tfoot" => name is "tr",
        "tr" => name is "td" or "th",
        "colgroup" => name is "col",
        _ => false,
    };

    // https://html.spec.whatwg.org/multipage/parsing.html#generate-implied-end-tags
    private static bool HasImpliedEndTag(string? name) => name is "dd" or "dt" or "li" or "optgroup" or "option" or "p" or "rb" or "rp" or "rt" or "rtc";

    // The start tags that close a p element in button scope (a table only in a document that is not in quirks mode)
    private static bool ClosesParagraph(string name) => name is "address" or "article" or "aside" or "blockquote" or "center" or "details" or "dialog" or "dir" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "header" or "hgroup" or "main" or "menu" or "nav" or "ol" or "p" or "search" or "section" or "summary" or "ul" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "pre" or "listing" or "form" or "li" or "dd" or "dt" or "plaintext" or "xmp" or "table" or "hr";

    // https://html.spec.whatwg.org/multipage/parsing.html#special
    private static bool IsSpecial(string name) => name is "address" or "applet" or "area" or "article" or "aside" or "base" or "basefont" or "bgsound" or "blockquote" or "body" or "br" or "button" or "caption" or "center" or "col" or "colgroup" or "dd" or "details" or "dir" or "div" or "dl" or "dt" or "embed" or "fieldset" or "figcaption" or "figure" or "footer" or "form" or "frame" or "frameset" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "head" or "header" or "hgroup" or "hr" or "html" or "iframe" or "img" or "input" or "keygen" or "li" or "link" or "listing" or "main" or "marquee" or "menu" or "meta" or "nav" or "noembed" or "noframes" or "noscript" or "object" or "ol" or "p" or "param" or "plaintext" or "pre" or "script" or "search" or "section" or "select" or "source" or "style" or "summary" or "table" or "tbody" or "td" or "template" or "textarea" or "tfoot" or "th" or "thead" or "title" or "tr" or "track" or "ul" or "wbr" or "xmp";
}
