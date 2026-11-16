using System.Globalization;
using System.Xml;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// Reads a rich text string (ISO 32000-2, 12.7.4.3, table 228): the XHTML elements <c>body</c>, <c>p</c>, <c>span</c>,
/// <c>b</c>, <c>i</c> and <c>br</c>, and the CSS properties <c>font-size</c>, <c>font-weight</c>, <c>font-style</c>,
/// <c>font-family</c>, <c>font</c>, <c>color</c> and <c>text-align</c> of their <c>style</c> attributes. The paragraphs
/// are lines; the text is not wrapped.
/// </summary>
internal static class RichText
{
    private const int MaxRuns = 4096;
    private const int MaxDepth = 64;

    public static List<RichTextParagraph>? TryParse(string xml, string? defaultStyle, double defaultSize)
    {
        var baseStyle = Apply(new RichTextStyle(defaultSize, Bold: false, Italic: false, Family: RichTextFamily.SansSerif, Color: null, Alignment: 0), defaultStyle);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = 1024 * 1024,
        };

        var paragraphs = new List<RichTextParagraph>();
        var current = new RichTextParagraph(baseStyle.Alignment);
        var styles = new Stack<RichTextStyle>();
        styles.Push(baseStyle);
        var runs = 0;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        var style = styles.Peek();
                        style = reader.LocalName switch
                        {
                            "b" => style with { Bold = true },
                            "i" => style with { Italic = true },
                            _ => style,
                        };
                        style = Apply(style, reader.GetAttribute("style"));
                        if (reader.LocalName is "p" or "div")
                        {
                            if (current.Runs.Count > 0)
                            {
                                paragraphs.Add(current);
                            }

                            current = new RichTextParagraph(style.Alignment);
                        }
                        else if (reader.LocalName == "br")
                        {
                            paragraphs.Add(current);
                            current = new RichTextParagraph(style.Alignment);
                        }

                        if (!reader.IsEmptyElement)
                        {
                            if (styles.Count > MaxDepth)
                                return null;

                            styles.Push(style);
                        }

                        break;

                    case XmlNodeType.EndElement:
                        if (styles.Count > 1)
                        {
                            styles.Pop();
                        }

                        if (reader.LocalName is "p" or "div")
                        {
                            paragraphs.Add(current);
                            current = new RichTextParagraph(styles.Peek().Alignment);
                        }

                        break;

                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.CDATA:
                        var lines = reader.Value.ReplaceLineEndings("\n").Split('\n');
                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (i > 0)
                            {
                                paragraphs.Add(current);
                                current = new RichTextParagraph(styles.Peek().Alignment);
                            }

                            if (lines[i].Length > 0 && ++runs < MaxRuns)
                            {
                                current.Runs.Add(new RichTextRun(lines[i], styles.Peek()));
                            }
                        }

                        break;
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        if (current.Runs.Count > 0)
        {
            paragraphs.Add(current);
        }

        return paragraphs;
    }

    /// <summary>Applies the declarations of a CSS style attribute.</summary>
    private static RichTextStyle Apply(RichTextStyle style, string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return style;

        foreach (var declaration in css.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
                continue;

            var property = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            style = property switch
            {
                "font-size" when TryParseSize(value, out var size) => style with { Size = size },
                "font-weight" => style with { Bold = value.Equals("bold", StringComparison.OrdinalIgnoreCase) || (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight >= 600) },
                "font-style" => style with { Italic = value.Equals("italic", StringComparison.OrdinalIgnoreCase) || value.Equals("oblique", StringComparison.OrdinalIgnoreCase) },
                "font-family" => style with { Family = ParseFamily(value) },
                "font" => ApplyFont(style, value),
                "color" when TryParseColor(value, out var color) => style with { Color = color },
                "text-align" => style with { Alignment = value.ToLowerInvariant() switch { "center" => 1, "right" => 2, _ => 0 } },
                _ => style,
            };
        }

        return style;
    }

    /// <summary>The <c>font</c> shorthand: the style and weight keywords, the size, then the family.</summary>
    private static RichTextStyle ApplyFont(RichTextStyle style, string value)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Equals("bold", StringComparison.OrdinalIgnoreCase))
            {
                style = style with { Bold = true };
            }
            else if (token.Equals("italic", StringComparison.OrdinalIgnoreCase) || token.Equals("oblique", StringComparison.OrdinalIgnoreCase))
            {
                style = style with { Italic = true };
            }
            else if (TryParseSize(token.Split('/')[0], out var size))
            {
                style = style with { Size = size };
                if (i + 1 < tokens.Length)
                {
                    style = style with { Family = ParseFamily(string.Join(' ', tokens[(i + 1)..])) };
                }

                break;
            }
        }

        return style;
    }

    private static RichTextFamily ParseFamily(string value)
    {
        var family = value.ToLowerInvariant();
        if (family.Contains("courier", StringComparison.Ordinal) || family.Contains("mono", StringComparison.Ordinal))
            return RichTextFamily.Monospace;

        if (family.Contains("times", StringComparison.Ordinal) || (family.Contains("serif", StringComparison.Ordinal) && !family.Contains("sans", StringComparison.Ordinal)))
            return RichTextFamily.Serif;

        return RichTextFamily.SansSerif;
    }

    private static bool TryParseSize(string value, out double size)
    {
        var number = value.Trim().ToLowerInvariant();
        if (number.EndsWith("pt", StringComparison.Ordinal) || number.EndsWith("px", StringComparison.Ordinal))
        {
            number = number[..^2];
        }

        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out size) && double.IsFinite(size) && size > 0 && size <= 1000;
    }

    private static bool TryParseColor(string value, out (double R, double G, double B) color)
    {
        color = default;
        var text = value.Trim();
        if (text.StartsWith("#", StringComparison.Ordinal) && text.Length is 4 or 7 && int.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = text.Length == 7
                ? (((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0)
                : (((rgb >> 8) & 0xF) / 15.0, ((rgb >> 4) & 0xF) / 15.0, (rgb & 0xF) / 15.0);
            return true;
        }

        if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
        {
            var parts = text[4..^1].Split(',');
            if (parts.Length == 3
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                color = (Math.Clamp(r / 255, 0, 1), Math.Clamp(g / 255, 0, 1), Math.Clamp(b / 255, 0, 1));
                return true;
            }
        }

        return false;
    }
}
