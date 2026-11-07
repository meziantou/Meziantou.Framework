using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>A page of the paged media, as the page selectors see it (https://drafts.csswg.org/css-page-3/#page-selectors).</summary>
/// <param name="Name">The page name (the used value of the <c>page</c> property of the content of the page), or the empty string.</param>
/// <param name="IsFirst">Whether the page is the first page of the document (<c>:first</c>).</param>
/// <param name="IsLeft">Whether the page is a left page (<c>:left</c>), else a right page (<c>:right</c>).</param>
/// <param name="IsBlank">Whether the page is a blank page inserted by a forced break (<c>:blank</c>).</param>
internal readonly record struct PageSelection(string Name, bool IsFirst, bool IsLeft, bool IsBlank);

// The styles of the page contexts of the paged media (https://drafts.csswg.org/css-page-3/#page-properties): the declarations
// of the @page rules that match a page, cascaded by importance, cascade layer, specificity and order of appearance
// (https://drafts.csswg.org/css-page-3/#cascading-and-page-context). The page context inherits from the root element.
internal sealed partial class StyleEngine
{
    /// <summary>The name of the pseudo-element of the style of a page context (no style rule matches it).</summary>
    internal const string PageContextPseudoElement = "-internal-page";

    /// <summary>
    /// Returns the style of the page context of a page: the declarations of the matching @page rules of the document (of the
    /// properties that apply to the page context, see <see cref="Om.CssStyleDeclaration.AppliesToPageContext"/>). The
    /// descriptors that are not properties are returned apart: <c>size</c> (see <see cref="GetPageSizeLengths"/>),
    /// <c>page-orientation</c> and <c>page-margin-safety</c>.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="defaultMargins">The margins of the page when no @page rule sets them (top, right, bottom, left, in CSS px).</param>
    /// <param name="size">Receives the value of the <c>size</c> descriptor (<c>auto</c> when it is not declared).</param>
    /// <param name="orientation">Receives the value of the <c>page-orientation</c> descriptor (<c>upright</c> when it is not declared).</param>
    /// <param name="marginSafety">Receives the value of the <c>page-margin-safety</c> descriptor (<c>auto</c> when it is not declared).</param>
    /// <returns>The style, or null when the document has no root element.</returns>
    public ComputedStyle? GetPageStyle(PageSelection page, ReadOnlySpan<double> defaultMargins, out string size, out string orientation, out string marginSafety)
    {
        size = "auto";
        orientation = "upright";
        marginSafety = "auto";
        if (_document.DocumentElement is not { } root)
            return null;

        var rootStyle = GetStyle(root);
        var matched = GetPageDeclarations(page, marginName: null);
        var cascaded = new Dictionary<string, CascadedValue>(StringComparer.Ordinal);

        // The margins of the page are set by the user agent (the options of the printing) when no rule sets them
        string[] marginNames = ["margin-top", "margin-right", "margin-bottom", "margin-left"];
        for (var i = 0; i < marginNames.Length && i < defaultMargins.Length; i++)
        {
            cascaded[marginNames[i]] = new CascadedValue(new CssLonghandValue(marginNames[i], CssSerializer.SerializeNumber(defaultMargins[i]) + "px"), 0, source: null);
        }

        var rank = 1L;
        foreach (var (declaration, _, baseUrl) in matched)
        {
            switch (declaration.Name)
            {
                case "size":
                    size = declaration.Value.Value;
                    continue;
                case "page-orientation":
                    orientation = declaration.Value.Value;
                    continue;
                case "page-margin-safety":
                    marginSafety = declaration.Value.Value;
                    continue;
                case "marks" or "bleed":
                    continue;
            }

            if (!Om.CssStyleDeclaration.AppliesToPageContext(declaration.Name))
                continue;

            cascaded[declaration.Name] = new CascadedValue(declaration.Value, rank++, CascadedValueSource.Create(baseUrl, _document, mixin: null));
        }

        return new ComputedStyle(this, root, PageContextPseudoElement, rootStyle, cascaded, animated: false) { IncludeTransitions = false };
    }

    /// <summary>
    /// Returns the lengths of the <c>size</c> descriptor of a page (its width and height in CSS px, one length is a square),
    /// computed as the lengths of its page context (e.g. <c>em</c>, the viewport-relative lengths), or null when the value is
    /// not lengths (<c>auto</c>, a page size keyword, an orientation).
    /// </summary>
    /// <param name="pageStyle">The style of the page context.</param>
    /// <param name="size">The value of the <c>size</c> descriptor.</param>
    public (double Width, double Height)? GetPageSizeLengths(ComputedStyle pageStyle, string size)
    {
        var lengths = size.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (lengths.Length == 0 || !lengths.All(static value => value.Length > 0 && (char.IsAsciiDigit(value[0]) || value[0] is '.' or '+' || value.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))))
            return null;

        // (the lengths are the width and height of a style that inherits from the page context: its font and viewport)
        var cascaded = new Dictionary<string, CascadedValue>(StringComparer.Ordinal)
        {
            ["width"] = new CascadedValue(new CssLonghandValue("width", lengths[0]), 0, source: null),
            ["height"] = new CascadedValue(new CssLonghandValue("height", lengths[^1]), 1, source: null),
        };
        var style = new ComputedStyle(this, pageStyle.Element, PageContextPseudoElement, pageStyle, cascaded, animated: false) { IncludeTransitions = false };
        return ParsePixels(style.GetComputedValue("width")) is { } width && ParsePixels(style.GetComputedValue("height")) is { } height ? (width, height) : null;

        static double? ParsePixels(string value)
            => value.EndsWith("px", StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    /// <summary>
    /// Returns the style of a page-margin box of a page (https://drafts.csswg.org/css-page-3/#margin-boxes): the declarations of
    /// the margin rules of the matching @page rules, cascaded as the page context, over the default alignments of the box
    /// (https://drafts.csswg.org/css-page-3/#margin-text-alignment). The box inherits from the page context.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="pageStyle">The style of the page context of the page.</param>
    /// <param name="marginName">The name of the margin box (e.g. <c>top-left</c>).</param>
    /// <param name="marginSafety">Receives the value of the <c>page-margin-safety</c> descriptor of the margin rules (<c>auto</c> when it is not declared).</param>
    public ComputedStyle GetPageMarginStyle(PageSelection page, ComputedStyle pageStyle, string marginName, out string marginSafety)
    {
        marginSafety = "auto";
        var cascaded = new Dictionary<string, CascadedValue>(StringComparer.Ordinal);
        var (textAlign, verticalAlign) = marginName switch
        {
            "top-left-corner" or "bottom-left-corner" => ("right", "middle"),
            "top-right-corner" or "bottom-right-corner" => ("left", "middle"),
            "top-left" or "bottom-left" => ("left", "middle"),
            "top-right" or "bottom-right" => ("right", "middle"),
            "left-top" or "right-top" => ("center", "top"),
            "left-bottom" or "right-bottom" => ("center", "bottom"),
            _ => ("center", "middle"),
        };
        cascaded["text-align-all"] = new CascadedValue(new CssLonghandValue("text-align-all", textAlign), 0, source: null);
        if (verticalAlign == "middle")
        {
            cascaded["alignment-baseline"] = new CascadedValue(new CssLonghandValue("alignment-baseline", "middle"), 0, source: null);
        }
        else
        {
            cascaded["baseline-shift"] = new CascadedValue(new CssLonghandValue("baseline-shift", verticalAlign), 0, source: null);
        }

        var rank = 1L;
        foreach (var (declaration, _, baseUrl) in GetPageDeclarations(page, marginName))
        {
            if (declaration.Name == "page-margin-safety")
            {
                marginSafety = declaration.Value.Value;
                continue;
            }

            if (!Om.CssStyleDeclaration.AppliesToMarginBox(declaration.Name))
                continue;

            cascaded[declaration.Name] = new CascadedValue(declaration.Value, rank++, CascadedValueSource.Create(baseUrl, _document, mixin: null));
        }

        return new ComputedStyle(this, pageStyle.Element, PageContextPseudoElement + "-" + marginName, pageStyle, cascaded, animated: false) { IncludeTransitions = false };
    }

    /// <summary>
    /// Returns the declarations of the @page rules that match a page (or of their margin rules of a name), from the lowest
    /// precedence to the highest (https://drafts.csswg.org/css-cascade-5/#cascade-sort: the important declarations win, and their
    /// cascade layers are in the reverse order; then the specificity of the page selector and the order of appearance).
    /// </summary>
    private List<(CssDeclarationEntry Declaration, long Precedence, string? BaseUrl)> GetPageDeclarations(PageSelection page, string? marginName)
    {
        var rules = GetTreeStyles(_document).PageRules;
        var matched = new List<(CssDeclarationEntry Declaration, long Precedence, string? BaseUrl)>();
        for (var order = 0; order < rules.Count; order++)
        {
            var (rule, layer, baseUrl) = rules[order];
            if (GetPageSpecificity(rule.SelectorText, page) is not { } specificity)
                continue;

            if (marginName is null)
            {
                Add(rule.Style.Declarations, layer, specificity, order, baseUrl);
                continue;
            }

            foreach (var child in rule.ChildRules)
            {
                if (child is CssMarginRule marginRule && marginRule.Name == marginName)
                {
                    Add(marginRule.Style.Declarations, layer, specificity, order, baseUrl);
                }
            }
        }

        // (the declarations of a rule keep their order: the sort is stable)
        return [.. matched.OrderBy(entry => entry.Precedence)];

        void Add(List<CssDeclarationEntry> declarations, int layer, int specificity, int order, string? baseUrl)
        {
            foreach (var declaration in declarations)
            {
                var layerKey = declaration.Important ? 0xFFFF - Math.Clamp(layer, 0, 0xFFFF) : Math.Clamp(layer, 0, 0xFFFF);
                var precedence = ((declaration.Important ? 1L : 0L) << 62) | ((long)layerKey << 40) | ((long)specificity << 20) | (long)Math.Min(order, 0xFFFFF);
                matched.Add((declaration, precedence, baseUrl));
            }
        }
    }

    /// <summary>
    /// Returns the specificity of the most specific selector of a page selector list that matches a page
    /// (https://drafts.csswg.org/css-page-3/#cascading-and-page-context: a page name, then the number of <c>:first</c> and
    /// <c>:blank</c>, then the number of <c>:left</c> and <c>:right</c>), or null when none matches. The empty selector
    /// matches all the pages.
    /// </summary>
    internal static int? GetPageSpecificity(string selectorText, PageSelection page)
    {
        if (selectorText.Length == 0)
            return 0;

        int? result = null;
        foreach (var selector in selectorText.Split(','))
        {
            var text = selector.Trim();
            var colon = text.IndexOf(':', StringComparison.Ordinal);
            var name = colon < 0 ? text : text[..colon];
            var matches = name.Length == 0 || string.Equals(name, CssSerializer.SerializeIdentifier(page.Name), StringComparison.Ordinal);
            var specificity = name.Length == 0 ? 0 : 1 << 16;
            if (colon >= 0)
            {
                foreach (var pseudoClass in text[(colon + 1)..].Split(':'))
                {
                    switch (InfraAscii.ToAsciiLowercase(pseudoClass))
                    {
                        case "first":
                            matches &= page.IsFirst;
                            specificity += 1 << 8;
                            break;
                        case "blank":
                            matches &= page.IsBlank;
                            specificity += 1 << 8;
                            break;
                        case "left":
                            matches &= page.IsLeft;
                            specificity++;
                            break;
                        case "right":
                            matches &= !page.IsLeft;
                            specificity++;
                            break;
                        default:
                            matches = false;
                            break;
                    }
                }
            }

            if (matches)
            {
                result = Math.Max(result ?? 0, specificity);
            }
        }

        return result;
    }
}
