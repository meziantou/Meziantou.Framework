using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals;

/// <summary>Reads and writes the CSS declarations of the <c>style</c>, <c>classDef</c> and <c>linkStyle</c> statements.</summary>
internal static class MermaidStyle
{
    /// <summary>Parses <c>fill:#f9f,stroke:#333,stroke-width:4px</c>.</summary>
    public static DiagramStyle Parse(string text, ParseContext context, int offset)
    {
        var style = new DiagramStyle();
        foreach (var declaration in SplitDeclarations(text))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                if (!string.IsNullOrWhiteSpace(declaration))
                {
                    context.Warn(offset, $"The style '{declaration.Trim()}' has no value and is ignored.");
                }

                continue;
            }

            var name = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^"!important".Length].Trim();
            }

            switch (name)
            {
                case "fill" or "background" or "background-color":
                    if (TryParseColor(value, out var fill))
                    {
                        style = style with { Fill = fill };
                    }

                    break;
                case "stroke":
                    if (TryParseColor(value, out var stroke))
                    {
                        style = style with { Stroke = stroke };
                    }

                    break;
                case "color":
                    if (TryParseColor(value, out var color))
                    {
                        style = style with { TextColor = color };
                    }

                    break;
                case "stroke-width":
                    if (ParseLength(value) is >= 0 and <= 1000 and var width)
                    {
                        style = style with { StrokeWidth = width };
                    }

                    break;
                case "stroke-dasharray":
                    var dashes = ParseDashes(value);
                    if (dashes is not null)
                    {
                        style = style with { StrokeDashArray = dashes };
                    }

                    break;
                case "font-weight":
                    style = style with { Bold = value is "bold" or "bolder" || (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var weight) && weight >= 600) };
                    break;
                case "font-style":
                    style = style with { Italic = value is "italic" or "oblique" };
                    break;
                case "font-size":
                    if (ParseLength(value) is > 0 and <= 200 and var size)
                    {
                        style = style with { FontSize = size };
                    }

                    break;
                case "opacity":
                    if (ParseLength(value) is >= 0 and <= 1 and var opacity)
                    {
                        style = style with { Opacity = opacity };
                    }

                    break;
                case "fill-opacity":
                    if (ParseLength(value) is >= 0 and <= 1 and var fillOpacity)
                    {
                        style = style with { Fill = (style.Fill ?? SceneColor.White).WithOpacity(fillOpacity) };
                    }

                    break;
                default:
                    context.Warn(offset, $"The style property '{name}' is not supported.");
                    break;
            }
        }

        return style;
    }

    private static bool TryParseColor(string value, out SceneColor color) => SceneColor.TryParse(value, out color);

    private static double? ParseLength(string value)
    {
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : null;
    }

    private static double[]? ParseDashes(string value)
    {
        if (value is "none" or "0")
            return [];

        var parts = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 16)
            return null;

        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (ParseLength(parts[i]) is not (>= 0 and <= 1000 and var length))
                return null;

            result[i] = length;
        }

        return result;
    }

    /// <summary>Splits on the commas that are not between parentheses; a part without a colon belongs to the previous declaration, as <c>5,5</c> in <c>stroke-dasharray: 5,5</c>.</summary>
    private static List<string> SplitDeclarations(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                var c = text[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }

                if (c != ',' || depth > 0)
                    continue;
            }

            var part = text[start..i];
            if (parts.Count > 0 && !part.Contains(':', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(part))
            {
                parts[^1] += "," + part;
            }
            else
            {
                parts.Add(part);
            }

            start = i + 1;
        }

        return parts;
    }

    /// <summary>Writes a style as CSS declarations, or returns an empty text when it has none.</summary>
    public static string Format(DiagramStyle style)
    {
        var parts = new List<string>();
        if (style.Fill is { } fill)
        {
            parts.Add("fill:" + fill);
        }

        if (style.Stroke is { } stroke)
        {
            parts.Add("stroke:" + stroke);
        }

        if (style.StrokeWidth is { } width)
        {
            parts.Add("stroke-width:" + Number(width) + "px");
        }

        if (style.StrokeDashArray is { } dashes)
        {
            parts.Add("stroke-dasharray:" + (dashes.Count is 0 ? "none" : string.Join(' ', dashes.Select(Number))));
        }

        if (style.TextColor is { } color)
        {
            parts.Add("color:" + color);
        }

        if (style.Bold is { } bold)
        {
            parts.Add("font-weight:" + (bold ? "bold" : "normal"));
        }

        if (style.Italic is { } italic)
        {
            parts.Add("font-style:" + (italic ? "italic" : "normal"));
        }

        if (style.FontSize is { } size)
        {
            parts.Add("font-size:" + Number(size) + "px");
        }

        if (style.Opacity is { } opacity)
        {
            parts.Add("opacity:" + Number(opacity));
        }

        return string.Join(',', parts);
    }

    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static bool IsEmpty(DiagramStyle? style) => style is null || Format(style).Length is 0;

    public static StringBuilder AppendIndent(this StringBuilder builder, int depth) => builder.Append(' ', 4 * depth);
}
