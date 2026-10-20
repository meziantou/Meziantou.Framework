using System.Collections.Frozen;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The computed role (https://w3c.github.io/core-aam/#roleMappingComputedRole) and the accessible name
/// (https://w3c.github.io/accname/) of the elements, with the mappings of HTML-AAM (https://w3c.github.io/html-aam/) and
/// SVG-AAM (https://w3c.github.io/svg-aam/). The hidden elements are determined by the computed style when the document has a
/// browsing context, and by the hidden and aria-hidden attributes otherwise.
/// </summary>
internal static class Accessibility
{
    // https://w3c.github.io/aria/#role_definitions (non-abstract roles), https://w3c.github.io/dpub-aria/, https://w3c.github.io/graphics-aria/
    private static readonly FrozenSet<string> Roles = FrozenSet.Create(StringComparer.Ordinal,
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox", "code", "columnheader",
        "combobox", "comment", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory", "document", "emphasis", "feed",
        "figure", "form", "generic", "grid", "gridcell", "group", "heading", "image", "img", "insertion", "link", "list", "listbox", "listitem",
        "log", "main", "mark", "marquee", "math", "menu", "menubar", "menuitem", "menuitemcheckbox", "menuitemradio", "meter", "navigation", "none",
        "note", "option", "paragraph", "presentation", "progressbar", "radio", "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar",
        "search", "searchbox", "sectionfooter", "sectionheader", "separator", "slider", "spinbutton", "status", "strong", "subscript", "suggestion",
        "superscript", "switch", "tab", "table", "tablist", "tabpanel", "term", "textbox", "time", "timer", "toolbar", "tooltip", "tree", "treegrid",
        "treeitem",
        "doc-abstract", "doc-acknowledgments", "doc-afterword", "doc-appendix", "doc-backlink", "doc-biblioentry", "doc-bibliography",
        "doc-biblioref", "doc-chapter", "doc-colophon", "doc-conclusion", "doc-cover", "doc-credit", "doc-credits", "doc-dedication",
        "doc-endnote", "doc-endnotes", "doc-epigraph", "doc-epilogue", "doc-errata", "doc-example", "doc-footnote", "doc-foreword",
        "doc-glossary", "doc-glossref", "doc-index", "doc-introduction", "doc-noteref", "doc-notice", "doc-pagebreak", "doc-pagefooter",
        "doc-pageheader", "doc-pagelist", "doc-part", "doc-preface", "doc-prologue", "doc-pullquote", "doc-qna", "doc-subtitle", "doc-tip",
        "doc-toc", "graphics-document", "graphics-object", "graphics-symbol");

    // https://w3c.github.io/aria/#namefromcontent
    private static readonly FrozenSet<string> NameFromContentRoles = FrozenSet.Create(StringComparer.Ordinal,
        "button", "cell", "checkbox", "columnheader", "comment", "gridcell", "heading", "link", "menuitem", "menuitemcheckbox", "menuitemradio",
        "option", "radio", "row", "rowheader", "sectionfooter", "sectionheader", "switch", "tab", "tooltip", "treeitem", "doc-backlink",
        "doc-biblioref", "doc-glossref", "doc-noteref");

    // https://w3c.github.io/aria/#global_states: the global ARIA attributes that prevent the presentational role (https://w3c.github.io/aria/#conflict_resolution_presentation_none)
    private static readonly string[] GlobalAriaAttributes =
    [
        "aria-atomic", "aria-braillelabel", "aria-brailleroledescription", "aria-busy", "aria-controls", "aria-describedby", "aria-description",
        "aria-details", "aria-dropeffect", "aria-flowto", "aria-grabbed", "aria-keyshortcuts", "aria-label", "aria-labelledby", "aria-live",
        "aria-owns", "aria-relevant", "aria-roledescription",
    ];

    // ----------------------------------------------------------------------------------------------------------------
    // Role
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the computed role: the first valid token of the role attribute (or of ElementInternals.role), the implicit role
    /// of the host language otherwise; the presentational roles are ignored on the focusable elements and the elements with
    /// global ARIA attributes. Returns the empty string when the element has no role.
    /// </summary>
    public static string GetRole(Element element)
    {
        if (GetExplicitRole(element, element.GetAttributeValue("role")) is { } explicitRole)
        {
            if (explicitRole != "none" || !IsPresentationalConflict(element))
                return explicitRole;
        }
        else if (GetInternals(element)?.Role is { } internalsRole && GetExplicitRole(element, internalsRole, requireLandmarkName: false) is { } defaultRole)
        {
            return defaultRole;
        }

        return GetImplicitRole(element);
    }

    /// <summary>The first valid role of the role attribute (https://w3c.github.io/aria/#document-handling_author-errors_roles), normalized (img is image, presentation is none, directory is list).</summary>
    private static string? GetExplicitRole(Element element, string? value, bool requireLandmarkName = true)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        foreach (var token in value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var role = InfraAscii.ToAsciiLowercase(token);
            if (!Roles.Contains(role))
                continue;

            // A region or a form without accessible name is not a landmark: the next role is used
            if (requireLandmarkName && role is "region" or "form" && !HasAuthorProvidedName(element))
                continue;

            return role switch
            {
                "img" => "image",
                "presentation" => "none",
                "directory" => "list",
                _ => role,
            };
        }

        return null;
    }

    private static bool HasAuthorProvidedName(Element element)
    {
        return !string.IsNullOrWhiteSpace(element.GetAttributeValue("aria-label"))
            || GetLabelledByElements(element).Count > 0
            || !string.IsNullOrWhiteSpace(element.GetAttributeValue("title"));
    }

    /// <summary>https://w3c.github.io/aria/#conflict_resolution_presentation_none: a focusable element or an element with a global ARIA attribute keeps its implicit role.</summary>
    private static bool IsPresentationalConflict(Element element)
    {
        if (Array.Exists(GlobalAriaAttributes, element.HasAttribute))
            return true;

        return IsFocusable(element);
    }

    private static bool IsFocusable(Element element)
    {
        if (element.HasAttribute("tabindex"))
            return true;

        if (element is not HtmlElement || element.HasAttribute("disabled"))
            return false;

        return element.TagId switch
        {
            TagId.A or TagId.Area => element.HasAttribute("href"),
            TagId.Button or TagId.Select or TagId.Textarea or TagId.Iframe => true,
            TagId.Input => element.GetAttributeValue("type") is not { } type || !type.Equals("hidden", StringComparison.OrdinalIgnoreCase),
            _ => element.HasAttribute("contenteditable"),
        };
    }

    /// <summary>The implicit role of the element (https://w3c.github.io/html-aam/#html-element-role-mappings, https://w3c.github.io/svg-aam/#mapping_role_table).</summary>
    private static string GetImplicitRole(Element element)
    {
        if (ReferenceEquals(element.NamespaceUri, Namespaces.Svg))
            return GetSvgRole(element);

        if (ReferenceEquals(element.NamespaceUri, Namespaces.MathML))
            return element.LocalName == "math" ? "math" : "";

        if (!element.IsHtmlNamespace)
            return "";

        switch (element.TagId)
        {
            case TagId.A or TagId.Area:
                return element.HasAttribute("href") ? "link" : "generic";
            case TagId.Abbr or TagId.B or TagId.Bdi or TagId.Bdo or TagId.Data or TagId.Div or TagId.I or TagId.Pre or TagId.Q or TagId.Samp or TagId.Small or TagId.Span or TagId.U or TagId.Body:
                return "generic";
            case TagId.Address or TagId.Details or TagId.Fieldset or TagId.Hgroup or TagId.Optgroup:
                return "group";
            case TagId.Article:
                return "article";
            case TagId.Aside:
                return IsScopedToSectioningContent(element, includeMain: false) && !HasAuthorProvidedName(element) ? "generic" : "complementary";
            case TagId.Blockquote:
                return "blockquote";
            case TagId.Button:
                return "button";
            case TagId.Caption:
                return "caption";
            case TagId.Code:
                return "code";
            case TagId.Datalist:
                return "listbox";
            case TagId.Dd:
                return "definition";
            case TagId.Del or TagId.S:
                return "deletion";
            case TagId.Dfn or TagId.Dt:
                return "term";
            case TagId.Dialog:
                return "dialog";
            case TagId.Dir or TagId.Menu or TagId.Ol or TagId.Ul:
                return "list";
            case TagId.Em:
                return "emphasis";
            case TagId.Figure:
                return "figure";
            case TagId.Footer:
                return IsScopedToSectioningContent(element, includeMain: true) ? "sectionfooter" : "contentinfo";
            case TagId.Form:
                return "form";
            case TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6:
                return "heading";
            case TagId.Header:
                return IsScopedToSectioningContent(element, includeMain: true) ? "sectionheader" : "banner";
            case TagId.Hr:
                return "separator";
            case TagId.Img:
                return element.GetAttributeValue("alt") is { Length: 0 } && string.IsNullOrWhiteSpace(element.GetAttributeValue("aria-label")) && GetLabelledByElements(element).Count == 0 ? "none" : "image";
            case TagId.Input:
                return GetInputRole(element);
            case TagId.Ins:
                return "insertion";
            case TagId.Li:
                return GetListItemRole(element);
            case TagId.Main:
                return "main";
            case TagId.Mark:
                return "mark";
            case TagId.Meter:
                return "meter";
            case TagId.Nav:
                return "navigation";
            case TagId.Option:
                return "option";
            case TagId.Output:
                return "status";
            case TagId.P:
                return "paragraph";
            case TagId.Progress:
                return "progressbar";
            case TagId.Search:
                return "search";
            case TagId.Section:
                return HasAuthorProvidedName(element) ? "region" : "generic";
            case TagId.Select:
                return element.HasAttribute("multiple") || (int.TryParse(element.GetAttributeValue("size"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 1) ? "listbox" : "combobox";
            case TagId.Strong:
                return "strong";
            case TagId.Sub:
                return "subscript";
            case TagId.Sup:
                return "superscript";
            case TagId.Table:
                return "table";
            case TagId.Tbody or TagId.Thead or TagId.Tfoot:
                return "rowgroup";
            case TagId.Td:
                return GetTableRole(element) is "grid" or "treegrid" ? "gridcell" : "cell";
            case TagId.Th:
                return GetHeaderCellRole(element);
            case TagId.Textarea:
                return "textbox";
            case TagId.Time:
                return "time";
            case TagId.Tr:
                return "row";
            default:
                // An autonomous custom element without default role, and the elements without corresponding role
                return element.CustomElementState == CustomElementState.Custom ? "generic" : "";
        }
    }

    private static string GetSvgRole(Element element) => element.LocalName switch
    {
        "svg" => element.ParentNode is SvgElement ? "group" : "graphics-document",
        "a" => element.HasAttribute("href") || element.HasAttribute("xlink:href") ? "link" : "group",
        "g" => HasAuthorProvidedName(element) || HasSvgTitle(element) ? "group" : "generic",
        "circle" or "ellipse" or "line" or "path" or "polygon" or "polyline" or "rect" or "use" => HasAuthorProvidedName(element) || HasSvgTitle(element) ? "graphics-symbol" : "",
        "image" => HasAuthorProvidedName(element) || HasSvgTitle(element) ? "image" : "",
        "text" => "group",
        "textPath" or "tspan" => "generic",
        "foreignObject" => "generic",
        _ => "",
    };

    private static bool HasSvgTitle(Element element)
    {
        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (ReferenceEquals(child.NamespaceUri, Namespaces.Svg) && child.LocalName == "title" && !string.IsNullOrWhiteSpace(child.TextContent))
                return true;
        }

        return false;
    }

    private static string GetInputRole(Element element)
    {
        var type = InfraAscii.ToAsciiLowercase(element.GetAttributeValue("type") ?? "text");
        var hasList = element.HasAttribute("list");
        return type switch
        {
            "button" or "image" or "reset" or "submit" => "button",
            "checkbox" => "checkbox",
            "radio" => "radio",
            "range" => "slider",
            "number" => "spinbutton",
            "search" => hasList ? "combobox" : "searchbox",
            "email" or "tel" or "url" => hasList ? "combobox" : "textbox",
            "color" or "date" or "datetime-local" or "file" or "hidden" or "month" or "password" or "time" or "week" => "",
            _ => hasList ? "combobox" : "textbox",
        };
    }

    /// <summary>https://w3c.github.io/html-aam/#el-li: a listitem in a list, generic otherwise (a presentational list makes its items presentational).</summary>
    private static string GetListItemRole(Element element)
    {
        if (element.ParentElement is not { } parent || !parent.IsHtmlNamespace || parent.TagId is not (TagId.Ol or TagId.Ul or TagId.Menu))
            return "generic";

        return GetExplicitRole(parent, parent.GetAttributeValue("role")) == "none" && !IsPresentationalConflict(parent) ? "none" : "listitem";
    }

    private static string? GetTableRole(Element cell)
    {
        for (var ancestor = cell.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.IsHtml(TagId.Table))
                return GetRole(ancestor);
        }

        return null;
    }

    /// <summary>https://w3c.github.io/html-aam/#el-th: the scope attribute, then the position of the header cell.</summary>
    private static string GetHeaderCellRole(Element element)
    {
        switch (InfraAscii.ToAsciiLowercase(element.GetAttributeValue("scope") ?? ""))
        {
            case "row" or "rowgroup":
                return "rowheader";
            case "col" or "colgroup":
                return "columnheader";
        }

        if (element.ParentElement is { } row && row.ParentElement is { } group && group.IsHtml(TagId.Thead))
            return "columnheader";

        // A header cell in a row of data cells is a row header
        for (var cell = element.ParentElement?.FirstElementChild; cell is not null; cell = cell.NextElementSibling)
        {
            if (cell.IsHtml(TagId.Td))
                return "rowheader";
        }

        return "columnheader";
    }

    /// <summary>Whether the element is a descendant of sectioning content (article, aside, nav, section) or main (https://w3c.github.io/html-aam/#el-header).</summary>
    private static bool IsScopedToSectioningContent(Element element, bool includeMain)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.IsHtmlNamespace && (ancestor.TagId is TagId.Article or TagId.Aside or TagId.Nav or TagId.Section || (includeMain && ancestor.TagId == TagId.Main)))
                return true;

            if (ancestor.GetAttributeValue("role") is { } role && GetExplicitRole(ancestor, role) is "article" or "complementary" or "navigation" or "region" || (includeMain && GetExplicitRole(ancestor, ancestor.GetAttributeValue("role")) == "main"))
                return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Accessible name (https://w3c.github.io/accname/#computation-steps)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the accessible name of the element, with its white space collapsed and trimmed.</summary>
    public static string GetName(Element element)
    {
        var computation = new NameComputation(element);
        return Flatten(computation.Compute(element, new Traversal()));
    }

    private static string Flatten(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static List<Element> GetLabelledByElements(Element element)
    {
        if (element.AriaLabelledByElements is { Count: > 0 } elements)
            return [.. elements];

        if (GetInternals(element)?.AriaLabelledByElements is { Count: > 0 } internalsElements && !element.HasAttribute("aria-labelledby"))
            return [.. internalsElements];

        return [];
    }

    /// <summary>The state of a step of the name computation.</summary>
    /// <param name="InContent">Whether the node is a descendant reached by the name from content (the element whose name is computed is not part of its own name).</param>
    private readonly record struct Traversal(bool InLabelledBy = false, bool Recursion = false, bool InHiddenRoot = false, bool InNativeLabel = false, bool InContent = false);

    private sealed class NameComputation(Element root)
    {
        private int _depth;

        // The elements already part of the name: a node referenced twice (e.g. by aria-labelledby, then as content) is used once
        private readonly HashSet<Element> _visited = [];

        private CssCounters? _counters;
        private AriaOwns? _ariaOwns;

        private AriaOwns Owns => _ariaOwns ??= AriaOwns.Compute(root);

        public string Compute(Node node, Traversal traversal)
        {
            // The relations (aria-labelledby, labels) are not followed recursively, so the depth is the depth of the tree
            if (_depth > 256)
                return "";

            _depth++;
            try
            {
                return ComputeCore(node, traversal);
            }
            finally
            {
                _depth--;
            }
        }

        private string ComputeCore(Node node, Traversal traversal)
        {
            if (node is Text text)
                return IsHiddenInTree(text.ParentElement) && !traversal.InHiddenRoot ? "" : TransformText(text);

            if (node is not Element element)
                return "";

            if (traversal.InContent && ReferenceEquals(element, root))
                return "";

            if (!ReferenceEquals(element, root) && !_visited.Add(element))
                return "";

            // 2A: the hidden nodes are ignored, except in a traversal whose root is hidden (the name of the element itself is
            // computed even when it is hidden). The visible descendants of an element with visibility: hidden are not hidden.
            if (!ReferenceEquals(element, root) && !traversal.InHiddenRoot && IsHiddenInTree(element))
                return traversal.Recursion && IsHiddenByVisibilityOnly(element) ? GetNameFromContent(element, traversal) : "";

            var role = GetRole(element);

            // 2B: aria-labelledby
            if (!traversal.InLabelledBy && GetLabelledByElements(element) is { Count: > 0 } labelledBy)
            {
                var parts = new List<string>();
                foreach (var target in labelledBy)
                {
                    parts.Add(Flatten(Compute(target, new Traversal(InLabelledBy: true, Recursion: true, InHiddenRoot: IsHiddenInTree(target)))));
                }

                var result = string.Join(' ', parts.Where(p => p.Length > 0));
                if (result.Length > 0)
                    return result;
            }

            // 2C: an embedded control is its value (when the traversal is not at the root)
            var isEmbeddedControl = traversal.Recursion && !ReferenceEquals(element, root) && GetEmbeddedControlValue(element, role) is not null;
            if (isEmbeddedControl && !ReferenceEquals(element, root))
                return GetEmbeddedControlValue(element, role)!;

            // A slot has no box: its name is its flat tree contents
            if (element is HtmlSlotElement)
                return GetNameFromContent(element, traversal with { Recursion = true });

            // 2D: aria-label
            var ariaLabel = element.GetAttributeValue("aria-label") ?? GetInternals(element)?.AriaLabel;
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                return ariaLabel;

            // An empty alt attribute is an empty name (https://w3c.github.io/html-aam/#img-element-accessible-name-computation)
            if (element.IsHtmlNamespace && element.TagId is TagId.Img or TagId.Area or TagId.Input && element.GetAttributeValue("alt") is { Length: 0 }
                && (element.TagId != TagId.Input || InfraAscii.EqualsIgnoreAsciiCase(element.GetAttributeValue("type") ?? "", "image")))
                return "";

            // 2E: the native text alternative of the host language
            if (role != "none" || IsPresentationalConflict(element))
            {
                if (GetNativeName(element, traversal) is { Length: > 0 } native)
                    return native;
            }

            // 2F: name from content
            if (traversal.Recursion || NameFromContentRoles.Contains(role) || role is "" && ReferenceEquals(element, root) is false)
            {
                var content = GetNameFromContent(element, traversal with { Recursion = true });
                if (traversal.Recursion || content.Trim().Length > 0)
                    return content;
            }

            // The name from the first descendant heading (tentative, https://github.com/w3c/aria/pull/1860)
            if (!traversal.Recursion && role is "alertdialog" or "article" or "dialog" && FindFirstHeading(element) is { } heading)
            {
                var headingName = Flatten(Compute(heading, traversal with { Recursion = true, InContent = true }));
                if (headingName.Length > 0)
                    return headingName;
            }

            // 2I: the tooltip
            if (element.GetAttributeValue("title") is { } title && !string.IsNullOrWhiteSpace(title))
                return title;

            if (ReferenceEquals(element.NamespaceUri, Namespaces.Svg) && GetSvgTitle(element) is { } svgTitle)
                return svgTitle;

            return "";
        }

        /// <summary>https://w3c.github.io/accname/#comp_embedded_control</summary>
        private static string? GetEmbeddedControlValue(Element element, string role)
        {
            switch (role)
            {
                case "textbox" or "searchbox":
                    return element switch
                    {
                        HtmlInputElement input => input.Value,
                        HtmlTextAreaElement textarea => textarea.Value,
                        _ => element.TextContent ?? "",
                    };

                case "combobox" or "listbox":
                    if (element is HtmlSelectElement select)
                        return string.Join(' ', select.SelectedOptions.OfType<HtmlOptionElement>().Select(o => o.Text));

                    if (element is HtmlInputElement comboInput)
                        return comboInput.Value;

                    var selected = string.Join(' ', element.QuerySelectorAll("[aria-selected=true]").OfType<Element>().Select(e => e.TextContent));

                    // A combobox that is not a text field (e.g. a span) displays its value as its content
                    return selected.Length > 0 || role == "listbox" ? selected : element.TextContent ?? "";

                case "slider" or "spinbutton" or "progressbar" or "scrollbar" or "meter":
                    if (element.GetAttributeValue("aria-valuetext") is { } valueText)
                        return valueText;

                    if (element.GetAttributeValue("aria-valuenow") is { } valueNow)
                        return valueNow;

                    return element is HtmlInputElement rangeInput ? rangeInput.Value : "";

                default:
                    return null;
            }
        }

        /// <summary>The text alternative of the host language (https://w3c.github.io/html-aam/#accessible-name-computations-by-html-element).</summary>
        private string? GetNativeName(Element element, Traversal traversal)
        {
            if (ReferenceEquals(element.NamespaceUri, Namespaces.Svg))
                return GetSvgTitle(element);

            if (!element.IsHtmlNamespace)
                return null;

            // The labels of the labelable elements
            if (element is HtmlElement htmlElement && htmlElement.GetLabels() is { Length: > 0 } labels && !traversal.InNativeLabel)
            {
                var parts = labels.OfType<Element>().Where(label => traversal.InHiddenRoot || !IsHidden(label)).Select(label => Flatten(Compute(label, traversal with { Recursion = true, InNativeLabel = true }))).Where(p => p.Length > 0).ToList();
                if (parts.Count > 0)
                    return string.Join(' ', parts);
            }

            switch (element.TagId)
            {
                case TagId.Input:
                {
                    var type = InfraAscii.ToAsciiLowercase(element.GetAttributeValue("type") ?? "text");
                    switch (type)
                    {
                        case "button":
                            return element.GetAttributeValue("value");
                        case "submit" or "reset":
                            return element.GetAttributeValue("value") ?? (type == "submit" ? "Submit" : "Reset");
                        case "image":
                            return element.GetAttributeValue("alt") is { Length: > 0 } alt ? alt : element.GetAttributeValue("value") is { Length: > 0 } value ? value : element.GetAttributeValue("title") ?? "Submit";
                    }

                    if (element.GetAttributeValue("title") is { Length: > 0 } inputTitle)
                        return inputTitle;

                    return element.GetAttributeValue("placeholder") ?? element.GetAttributeValue("aria-placeholder");
                }

                case TagId.Textarea:
                    return element.GetAttributeValue("title") is { Length: > 0 } textareaTitle ? textareaTitle : element.GetAttributeValue("placeholder") ?? element.GetAttributeValue("aria-placeholder");

                case TagId.Img or TagId.Area:
                    return element.GetAttributeValue("alt");

                case TagId.Fieldset:
                    return FirstVisibleChild(element, TagId.Legend, traversal) is { } legend ? Compute(legend, traversal with { Recursion = true }) : null;

                case TagId.Figure:
                    return FirstVisibleChild(element, TagId.Figcaption, traversal) is { } figcaption ? Compute(figcaption, traversal with { Recursion = true }) : null;

                case TagId.Table:
                    return FirstVisibleChild(element, TagId.Caption, traversal) is { } caption ? Compute(caption, traversal with { Recursion = true }) : null;

                case TagId.Summary:
                    return GetNameFromContent(element, traversal with { Recursion = true });

                case TagId.Details:
                    return null;

                case TagId.Optgroup:
                    return element.GetAttributeValue("label");

                case TagId.Option:
                    return element.GetAttributeValue("label") is { Length: > 0 } optionLabel ? optionLabel : null;

                default:
                    return null;
            }
        }

        /// <summary>https://w3c.github.io/accname/#comp_name_from_content: the ::before content, the names of the children, the ::after content.</summary>
        private string GetNameFromContent(Element element, Traversal traversal)
        {
            var builder = new StringBuilder();
            builder.Append(GetMarkerContent(element));
            builder.Append(GetPseudoContent(element, "before"));
            foreach (var child in GetChildren(element))
            {
                var value = Compute(child, traversal with { InContent = true });
                if (value.Length > 0 && child is Element childElement && !IsInline(childElement))
                {
                    builder.Append(' ').Append(value).Append(' ');
                }
                else
                {
                    builder.Append(value);
                }
            }

            builder.Append(GetPseudoContent(element, "after"));
            return builder.ToString();
        }

        /// <summary>
        /// The text of the content property of a pseudo-element (https://w3c.github.io/accname/#comp_name_from_content_pseudo_element):
        /// the strings and the counters, or the alternative text after the slash.
        /// </summary>
        private string GetPseudoContent(Element element, string pseudoElement)
        {
            if (!HasStyle(element))
                return "";

            var style = element.NodeDocument.GetStyleEngine().GetStyle(element, pseudoElement);
            var content = style.GetComputedValue("content");
            if (content is "none" or "normal" or "")
                return "";

            return GetContentText(style, element, pseudoElement, content);
        }

        private string GetContentText(ComputedStyle style, Element element, string pseudoElement, string content)
        {
            var builder = new StringBuilder();
            foreach (var value in Css.Values.ValueMatcher.RemoveWhitespace(Css.Syntax.CssParser.ParseListOfComponentValues(content)))
            {
                if (value is Css.Syntax.CssPreservedToken { Type: Css.Syntax.CssTokenType.String } stringToken)
                {
                    builder.Append(stringToken.Token.Value);
                }
                else if (value is Css.Syntax.CssPreservedToken slash && slash.Token.IsDelim('/'))
                {
                    // The alternative text of the content replaces it
                    builder.Clear();
                }
                else if (value is Css.Syntax.CssFunction function && GetCounterText(style, element, pseudoElement, function) is { } counter)
                {
                    // The browsers expose the value of a counter as a separate text, joined with spaces
                    builder.Append(' ').Append(counter).Append(' ');
                }
            }

            return builder.ToString();
        }

        /// <summary>The value of a counter() or counters() function (https://drafts.csswg.org/css-lists-3/#counter-functions).</summary>
        private string? GetCounterText(ComputedStyle style, Element element, string pseudoElement, Css.Syntax.CssFunction function)
        {
            var isCounters = InfraAscii.EqualsIgnoreAsciiCase(function.Name, "counters");
            if (!isCounters && !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "counter"))
                return null;

            var arguments = Css.Values.ValueMatcher.SplitAtCommas(function.Value).Select(a => Css.Values.ValueMatcher.TrimWhitespace(a)).ToList();
            if (arguments.Count == 0 || arguments[0] is not [Css.Syntax.CssPreservedToken { Type: Css.Syntax.CssTokenType.Ident } name])
                return null;

            var separator = isCounters && arguments.Count > 1 && arguments[1] is [Css.Syntax.CssPreservedToken { Type: Css.Syntax.CssTokenType.String } separatorToken] ? separatorToken.Token.Value : null;
            if (isCounters && separator is null)
                return null;

            _counters ??= CssCounters.Compute(root);
            var styleIndex = isCounters ? 2 : 1;
            var counterStyle = (arguments.Count > styleIndex ? _counters.ResolveCounterStyle(arguments[styleIndex], style, "content") : null) ?? CounterStyle.Decimal;
            return isCounters
                ? _counters.GetCounters(element, pseudoElement, name.Token.Value, separator!, counterStyle, style)
                : _counters.GetCounter(element, pseudoElement, name.Token.Value, counterStyle, style);
        }

        /// <summary>
        /// The text of the ::marker of a list item (https://w3c.github.io/accname/#comp_name_from_content_pseudo_element_marker),
        /// separated from the content: its content, or the bullet or the number of its list-style-type followed by its suffix.
        /// The disclosure triangle of a summary is not part of its name.
        /// </summary>
        private string GetMarkerContent(Element element)
        {
            var marker = GetMarkerText(element);
            return marker.Length > 0 ? marker + " " : "";
        }

        private string GetMarkerText(Element element)
        {
            if (!HasStyle(element))
                return "";

            var engine = element.NodeDocument.GetStyleEngine();
            if (!engine.GetStyle(element).GetComputedValue("display").Contains("list-item", StringComparison.Ordinal))
                return "";

            var style = engine.GetStyle(element, "marker");
            var content = style.GetComputedValue("content");
            if (content is "none")
                return "";

            if (content is not ("normal" or ""))
                return GetContentText(style, element, "marker", content);

            var listStyleType = style.GetComputedValue("list-style-type");
            if (listStyleType is "none" or "" or "disclosure-open" or "disclosure-closed")
                return "";

            // The marker string: the space after a bullet is not part of the name (the marker is separated from the content)
            _counters ??= CssCounters.Compute(root);
            var marker = _counters.GetMarkerString(element, style) ?? "";
            return listStyleType is "disc" or "circle" or "square" ? marker.TrimEnd(' ') : marker;
        }

        /// <summary>The first heading descendant, in depth-first order, that is not hidden.</summary>
        private Element? FindFirstHeading(Element element)
        {
            foreach (var child in GetChildren(element))
            {
                if (child is not Element childElement || IsHiddenInTree(childElement))
                    continue;

                if (GetRole(childElement) == "heading")
                    return childElement;

                if (FindFirstHeading(childElement) is { } heading)
                    return heading;
            }

            return null;
        }

        private static bool IsInline(Element element)
        {
            if (!HasStyle(element))
                return !element.IsHtmlNamespace || element.TagId is not (TagId.Div or TagId.P or TagId.Li or TagId.Ul or TagId.Ol or TagId.Section or TagId.Article or TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6 or TagId.Table or TagId.Tr or TagId.Td or TagId.Th or TagId.Br);

            return element.NodeDocument.GetStyleEngine().GetStyle(element).GetComputedValue("display") is "inline" or "contents";
        }

        private static string? GetSvgTitle(Element element)
        {
            for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                if (ReferenceEquals(child.NamespaceUri, Namespaces.Svg) && child.LocalName == "title")
                    return child.TextContent;
            }

            return element.LocalName == "a" ? element.GetAttribute("xlink:title") : null;
        }

        private static Element? FirstVisibleChild(Element element, TagId tagId, Traversal traversal)
        {
            for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                if (child.IsHtml(tagId) && (traversal.InHiddenRoot || !IsHidden(child)))
                    return child;
            }

            return null;
        }

        /// <summary>The text of a text node, with the text-transform of its parent (https://w3c.github.io/accname/#comp_text_node).</summary>
        private static string TransformText(Text text)
        {
            if (text.ParentElement is not { } parent || !HasStyle(parent))
                return text.Data;

            var transform = parent.NodeDocument.GetStyleEngine().GetStyle(parent).GetComputedValue("text-transform");
            if (transform.Contains("uppercase", StringComparison.Ordinal))
                return text.Data.ToUpperInvariant();

            if (transform.Contains("lowercase", StringComparison.Ordinal))
                return text.Data.ToLowerInvariant();

            if (!transform.Contains("capitalize", StringComparison.Ordinal))
                return text.Data;

            var builder = new StringBuilder(text.Data);
            for (var i = 0; i < builder.Length; i++)
            {
                if (char.IsLetter(builder[i]) && (i == 0 || !char.IsLetterOrDigit(builder[i - 1])))
                {
                    builder[i] = char.ToUpperInvariant(builder[i]);
                }
            }

            return builder.ToString();
        }

        /// <summary>The children of the element in the accessibility tree: its flat tree children, without the elements owned by another element, then the elements it owns (aria-owns).</summary>
        private AccessibleChildren GetChildren(Element element) => new(element, HtmlElement.GetFlatTreeChildren(element), Owns.HasOwnedElements ? Owns : null);

        /// <summary>Whether the element is hidden, its parent being its owner (aria-owns) when it is owned: an owned element is not hidden by the aria-hidden of its DOM ancestors.</summary>
        private bool IsHiddenInTree(Element? element)
        {
            if (element is null)
                return false;

            if (!Owns.HasOwnedElements)
                return IsHidden(element);

            for (var ancestor = element; ancestor is not null; ancestor = Owns.GetOwner(ancestor) ?? HtmlElement.GetFlatTreeParent(ancestor) as Element)
            {
                if (IsAriaHidden(ancestor))
                    return true;
            }

            return IsNotRendered(element);
        }

    }

    private static bool HasStyle(Element element) => ComputedStyleDeclarations.IsRendered(element);

    /// <summary>The ElementInternals of a custom element, whose ARIA properties are the default semantics of the element.</summary>
    private static ElementInternals? GetInternals(Element element) => element.RareDataOrNull is ElementRareData { AttachedInternals: { } internals } ? internals : null;

    /// <summary>Whether the element is only hidden by visibility (its visible descendants are rendered).</summary>
    private static bool IsHiddenByVisibilityOnly(Element element)
    {
        if (!HasStyle(element))
            return false;

        for (Node? ancestor = element; ancestor is Element ancestorElement; ancestor = HtmlElement.GetFlatTreeParent(ancestor))
        {
            if (ancestorElement.GetAttributeValue("aria-hidden") is { } ariaHidden && ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var style = element.NodeDocument.GetStyleEngine().GetStyle(element);
        for (var current = style; current is not null; current = current.Parent)
        {
            if (current.GetComputedValue("display") == "none")
                return false;
        }

        return style.GetComputedValue("visibility") is "hidden" or "collapse";
    }

    /// <summary>
    /// Whether the element is hidden (https://w3c.github.io/accname/#dfn-hidden): it is not rendered (display: none, or
    /// visibility: hidden or collapse), or it or one of its ancestors has aria-hidden="true".
    /// </summary>
    internal static bool IsHidden(Element? element)
    {
        if (element is null)
            return false;

        for (Node? ancestor = element; ancestor is Element ancestorElement; ancestor = HtmlElement.GetFlatTreeParent(ancestor))
        {
            if (IsAriaHidden(ancestorElement))
                return true;
        }

        return IsNotRendered(element);
    }

    private static bool IsAriaHidden(Element element) => element.GetAttributeValue("aria-hidden") is { } ariaHidden && ariaHidden.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the element is hidden from all users: display: none (on it or an ancestor), or visibility: hidden or collapse.</summary>
    private static bool IsNotRendered(Element element)
    {
        // The areas of an image map are not rendered as boxes (display: none), but they are rendered by the image
        if (element.IsHtmlNamespace && element.TagId is TagId.Area or TagId.Map)
            return false;

        if (HasStyle(element))
        {
            var style = element.NodeDocument.GetStyleEngine().GetStyle(element);
            if (style.GetComputedValue("visibility") is "hidden" or "collapse")
                return true;

            for (var current = style; current is not null; current = current.Parent)
            {
                if (current.GetComputedValue("display") == "none")
                    return true;
            }

            return false;
        }

        if (!element.IsConnected)
            return false;

        for (var ancestor = element; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.HasAttribute("hidden") || (ancestor.IsHtmlNamespace && ancestor.TagId is TagId.Head or TagId.Script or TagId.Style or TagId.Template))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The relocations of aria-owns (https://w3c.github.io/aria/#aria-owns): an element is owned by the first element that
    /// references it. aria-owns is ignored on a hidden element, and when it references an element hidden from all users,
    /// the element itself or one of its ancestors (https://github.com/w3c/aria/pull/1839).
    /// </summary>
    private sealed class AriaOwns
    {
        private readonly Dictionary<Element, Element> _owners = [];
        private readonly Dictionary<Element, List<Element>> _owned = [];

        public bool HasOwnedElements => _owners.Count > 0;

        public static AriaOwns Compute(Element element)
        {
            var result = new AriaOwns();
            var stack = new Stack<Node>();
            stack.Push(element.GetShadowIncludingRoot());
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is Element owner)
                {
                    result.Add(owner);
                    if (owner.AttachedShadowRoot is { } shadowRoot)
                    {
                        stack.Push(shadowRoot);
                    }
                }

                for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
                {
                    stack.Push(child);
                }
            }

            return result;
        }

        public Element? GetOwner(Element element) => _owners.GetValueOrDefault(element);

        public List<Element>? GetOwnedElements(Element element) => _owned.GetValueOrDefault(element);

        private void Add(Element owner)
        {
            var targets = owner.AriaOwnsElements;
            if (targets is not { Count: > 0 })
            {
                if (owner.HasAttribute("aria-owns") || GetInternals(owner)?.AriaOwnsElements is not { Count: > 0 } internalsTargets)
                    return;

                targets = internalsTargets;
            }

            if (IsHidden(owner))
                return;

            foreach (var target in targets)
            {
                if (_owners.ContainsKey(target) || IsNotRendered(target) || IsFlatTreeInclusiveAncestor(target, owner))
                    continue;

                _owners[target] = owner;
                if (!_owned.TryGetValue(owner, out var owned))
                {
                    owned = [];
                    _owned[owner] = owned;
                }

                owned.Add(target);
            }
        }

        private static bool IsFlatTreeInclusiveAncestor(Element ancestor, Element element)
        {
            for (Node? current = element; current is not null; current = HtmlElement.GetFlatTreeParent(current))
            {
                if (ReferenceEquals(current, ancestor))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The children of an element in the accessibility tree: its flat tree children, without the elements owned by another
    /// element, then the elements it owns (aria-owns), enumerated without allocation.
    /// </summary>
    private readonly struct AccessibleChildren(Element element, NodeEnumerable children, AriaOwns? owns)
    {
        public Enumerator GetEnumerator() => new(element, children.GetEnumerator(), owns, owns?.GetOwnedElements(element));

        public struct Enumerator(Element element, NodeEnumerable.Enumerator children, AriaOwns? owns, List<Element>? owned)
        {
            private NodeEnumerable.Enumerator _children = children;

            /// <summary>The index of the next owned element, or -1 while the flat tree children are enumerated.</summary>
            private int _ownedIndex = -1;

            public Node Current { get; private set; } = null!;

            public bool MoveNext()
            {
                if (_ownedIndex < 0)
                {
                    while (_children.MoveNext())
                    {
                        var child = _children.Current;
                        if (owns is not null && child is Element childElement && ((owns.GetOwner(childElement) is { } owner && !ReferenceEquals(owner, element)) || owned?.Contains(childElement) == true))
                            continue;

                        Current = child;
                        return true;
                    }

                    _ownedIndex = 0;
                }

                if (owned is null || _ownedIndex >= owned.Count)
                    return false;

                Current = owned[_ownedIndex++];
                return true;
            }
        }
    }
}
