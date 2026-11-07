namespace Meziantou.Framework.Sanitizers;

// The HTML attributes that only describe the content: whatever their value, they cannot run script or load a resource.
// Attributes that hold a URL are not part of them, as their value must be validated. This file is also compiled into
// Meziantou.Framework.Markdown, whose generic attributes only allow these attributes by default.
internal static class DescriptiveHtmlAttributes
{
    public static string[] Names { get; } =
    [
        "abbr",
        "align",
        "alt",
        "axis",
        "bgcolor",
        "border",
        "cellpadding",
        "cellspacing",
        "clear",
        "color",
        "cols",
        "colspan",
        "compact",
        "coords",
        "datetime",
        "decoding",
        "dir",
        "face",
        "headers",
        "height",
        "hidden",
        "hreflang",
        "hspace",
        "ismap",
        "lang",
        "language",
        "loading",
        "nohref",
        "nowrap",
        "open",
        "rel",
        "rev",
        "reversed",
        "role",
        "rows",
        "rowspan",
        "rules",
        "scope",
        "scrolling",
        "shape",
        "size",
        "span",
        "start",
        "summary",
        "tabindex",
        "target",
        "title",
        "translate",
        "type",
        "valign",
        "value",
        "vspace",
        "width",
    ];

    // The attributes whose name starts with one of these prefixes are descriptive too
    public static string[] Prefixes { get; } = ["aria-"];
}
