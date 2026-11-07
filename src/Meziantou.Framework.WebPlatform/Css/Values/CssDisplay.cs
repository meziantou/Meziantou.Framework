namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>The values of the display property (https://drafts.csswg.org/css-display/#the-display-properties).</summary>
internal static class CssDisplay
{
    /// <summary>
    /// Serializes a display value (ASCII-lowercase keywords separated by spaces) in its shortest form
    /// (https://drafts.csswg.org/css-display/#display-value-summary): block flow is block, inline flow-root is inline-block,
    /// block flow-root list-item is flow-root list-item... The legacy -webkit-flex and -webkit-inline-flex are flex and
    /// inline-flex (https://compat.spec.whatwg.org/#display-property-mappings).
    /// </summary>
    public static string Canonicalize(string value)
    {
        // The layout() values are already canonical (see SerializeLayoutFunction): the name of the layout may contain any character
        if (IsLayoutFunction(value))
            return value;

        // Most values are a single keyword: they are not split
        if (value.Length > 0 && !value.Contains(' ', StringComparison.Ordinal))
            return CanonicalizeKeyword(value);

        var keywords = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (keywords.Length == 1)
            return CanonicalizeKeyword(keywords[0]);

        string? outer = null;
        string? inner = null;
        var listItem = false;
        foreach (var keyword in keywords)
        {
            switch (keyword)
            {
                case "block" or "inline" or "run-in":
                    outer = keyword;
                    break;
                case "list-item":
                    listItem = true;
                    break;
                default:
                    inner = keyword;
                    break;
            }
        }

        inner ??= "flow";
        outer ??= inner is "ruby" or "math" ? "inline" : "block";
        if (listItem)
        {
            List<string> parts = [];
            if (outer != "block")
            {
                parts.Add(outer);
            }

            if (inner != "flow")
            {
                parts.Add(inner);
            }

            parts.Add("list-item");
            return string.Join(' ', parts);
        }

        return (outer, inner) switch
        {
            (_, "flow") => outer,
            ("block", "flow-root") => "flow-root",
            ("inline", "flow-root") => "inline-block",
            ("block", "table" or "flex" or "grid" or "grid-lanes") => inner,
            ("inline", "table" or "flex" or "grid") => "inline-" + inner,
            ("inline", "ruby" or "math") => inner,
            _ => outer + " " + inner,
        };
    }

    private static string CanonicalizeKeyword(string keyword) => keyword switch
    {
        "flow" => "block",
        "-webkit-flex" => "flex",
        "-webkit-inline-flex" => "inline-flex",
        _ => keyword,
    };

    private const string LayoutFunctionPrefix = "layout(";
    private const string InlineLayoutFunctionPrefix = "inline layout(";

    /// <summary>
    /// Serializes the display of a layout API container (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers):
    /// <c>layout(name)</c>, or <c>inline layout(name)</c> for an inline-level one.
    /// </summary>
    public static string SerializeLayoutFunction(string name, bool isInline)
        => (isInline ? InlineLayoutFunctionPrefix : LayoutFunctionPrefix) + CssSerializer.SerializeIdentifier(name) + ")";

    /// <summary>Whether a canonical display value is <c>layout(name)</c> or <c>inline layout(name)</c>.</summary>
    public static bool IsLayoutFunction(string display)
        => display.StartsWith(LayoutFunctionPrefix, StringComparison.Ordinal) || display.StartsWith(InlineLayoutFunctionPrefix, StringComparison.Ordinal);

    /// <summary>Returns the name of the layout of a canonical display value that is <c>layout(name)</c> or <c>inline layout(name)</c>, else null.</summary>
    public static string? GetLayoutFunctionName(string display)
    {
        if (!IsLayoutFunction(display))
            return null;

        var start = display.IndexOf('(', StringComparison.Ordinal) + 1;
        return Syntax.CssParser.ParseListOfComponentValues(display[start..^1]) is [Syntax.CssPreservedToken { Type: Syntax.CssTokenType.Ident } name] ? name.Token.Value : null;
    }

    /// <summary>https://drafts.csswg.org/css-display/#blockify: the display of a canonical value is block-level.</summary>
    public static string Blockify(string display) => display switch
    {
        _ when display.StartsWith(InlineLayoutFunctionPrefix, StringComparison.Ordinal) => display["inline ".Length..],
        _ when display.StartsWith(LayoutFunctionPrefix, StringComparison.Ordinal) => display,
        // inline list-item, run-in flex...: the outer display type is block
        _ when display.Contains(' ', StringComparison.Ordinal) => Canonicalize(string.Join(' ', display.Split(' ').Select(keyword => keyword is "inline" or "run-in" ? "block" : keyword).Prepend("block").Distinct())),
        "inline" or "inline-block" or "run-in" => "block",
        "inline-table" => "table",
        "inline-flex" => "flex",
        "inline-grid" => "grid",
        "inline-grid-lanes" => "grid-lanes",
        "-webkit-inline-box" => "-webkit-box",
        "ruby" => "block ruby",
        "math" => "block math",
        "table-row-group" or "table-header-group" or "table-footer-group" or "table-row" or "table-cell" or "table-column-group" or "table-column" or "table-caption"
            or "ruby-base" or "ruby-text" or "ruby-base-container" or "ruby-text-container" => "block",
        _ => display,
    };
}
