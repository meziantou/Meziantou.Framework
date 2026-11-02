using System.Globalization;
using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// Generates the appearance of an annotation that has none (ISO 32000-2, 12.5.5): the markup annotations from their
/// geometry and colors (12.5.6), and the text, choice and check box fields from their value and default appearance
/// (12.7.4.3). The appearance is a form whose bounding box is the rectangle of the annotation, in default user space.
/// </summary>
internal static class AppearanceGenerator
{
    // The points of the paths of an annotation: /InkList, /Vertices, /QuadPoints
    private const int MaxPoints = 65536;

    // The flags of a field (12.7.4.1, 12.7.5.3)
    private const int Multiline = 1 << 12;
    private const int Password = 1 << 13;

    // The nesting of the fields whose entries a widget inherits
    private const int MaxFieldDepth = 32;

    /// <summary>Gets the appearance of an annotation that has none, or of a field whose form asks for new appearances (<c>/NeedAppearances</c>); <see langword="null"/> when none is generated.</summary>
    public static PdfStream? Generate(PdfPage page, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Top - rectangle.Bottom;
        if (!(width > 0 && height > 0))
            return null;

        var content = new StringBuilder();
        var resources = new PdfDictionary();
        var generated = annotation.GetName(PdfNames.Subtype)?.Value switch
        {
            "Square" => Square(content, annotation, rectangle),
            "Circle" => Circle(content, annotation, rectangle),
            "Line" => Line(content, annotation),
            "Polygon" => Vertices(content, annotation, close: true),
            "PolyLine" => Vertices(content, annotation, close: false),
            "Ink" => Ink(content, annotation),
            "Highlight" or "Underline" or "StrikeOut" or "Squiggly" => TextMarkup(content, resources, annotation, rectangle),
            "Widget" => Widget(page, content, resources, annotation, rectangle),
            _ => false,
        };
        if (!generated)
            return null;

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfNames.Form,
            [PdfNames.BBox] = PdfArray.FromNumbers(rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Top),
            [PdfNames.Resources] = resources,
        };
        return new PdfStream(dictionary, Encoding.Latin1.GetBytes(content.ToString()));
    }

    /// <summary>Indicates whether a widget annotation gets a new appearance even if it has one: the interactive form asks for it (12.7.3, <c>/NeedAppearances</c>), and it is a field whose appearance this class generates.</summary>
    public static bool NeedsAppearance(PdfPage page, PdfDictionary annotation)
    {
        if (annotation.GetName(PdfNames.Subtype)?.Value != "Widget" || page.Document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm)?.Get<PdfBoolean>(PdfName.Create("NeedAppearances"))?.Value is not true)
            return false;

        return Inherited(annotation, "FT") is PdfName { Value: "Tx" or "Ch" };
    }

    private static bool Square(StringBuilder content, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var width = BorderWidth(annotation);
        var inset = width / 2;
        var hasStroke = SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: true) && width > 0;
        var hasFill = SetColor(content, annotation.Get<PdfArray>(PdfName.Create("IC")), stroke: false);
        if (!hasStroke && !hasFill)
            return false;

        SetLine(content, annotation, width);
        Append(content, rectangle.Left + inset, rectangle.Bottom + inset, rectangle.Right - rectangle.Left - width, rectangle.Top - rectangle.Bottom - width).Append("re ");
        content.Append(Paint(hasStroke, hasFill));
        return true;
    }

    private static bool Circle(StringBuilder content, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var width = BorderWidth(annotation);
        var hasStroke = SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: true) && width > 0;
        var hasFill = SetColor(content, annotation.Get<PdfArray>(PdfName.Create("IC")), stroke: false);
        if (!hasStroke && !hasFill)
            return false;

        SetLine(content, annotation, width);

        // An ellipse inscribed in the rectangle, as four Bézier curves (the control points at 0.5523 of the radii)
        const double Kappa = 0.5523;
        var cx = (rectangle.Left + rectangle.Right) / 2;
        var cy = (rectangle.Bottom + rectangle.Top) / 2;
        var rx = Math.Max(0, ((rectangle.Right - rectangle.Left) / 2) - (width / 2));
        var ry = Math.Max(0, ((rectangle.Top - rectangle.Bottom) / 2) - (width / 2));
        Append(content, cx + rx, cy).Append("m ");
        Append(content, cx + rx, cy + (ry * Kappa), cx + (rx * Kappa), cy + ry, cx, cy + ry).Append("c ");
        Append(content, cx - (rx * Kappa), cy + ry, cx - rx, cy + (ry * Kappa), cx - rx, cy).Append("c ");
        Append(content, cx - rx, cy - (ry * Kappa), cx - (rx * Kappa), cy - ry, cx, cy - ry).Append("c ");
        Append(content, cx + (rx * Kappa), cy - ry, cx + rx, cy - (ry * Kappa), cx + rx, cy).Append("c h ");
        content.Append(Paint(hasStroke, hasFill));
        return true;
    }

    private static bool Line(StringBuilder content, PdfDictionary annotation)
    {
        var width = BorderWidth(annotation);
        if (annotation.Get<PdfArray>(PdfName.Create("L")) is not { Count: 4 } line || width <= 0 || !SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: true))
            return false;

        SetLine(content, annotation, width);
        Append(content, line.GetNumber(0), line.GetNumber(1)).Append("m ");
        Append(content, line.GetNumber(2), line.GetNumber(3)).Append("l S");
        return true;
    }

    private static bool Vertices(StringBuilder content, PdfDictionary annotation, bool close)
    {
        var width = BorderWidth(annotation);
        if (annotation.Get<PdfArray>(PdfName.Create("Vertices")) is not { Count: >= 4 } vertices)
            return false;

        var hasStroke = SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: true) && width > 0;
        var hasFill = close && SetColor(content, annotation.Get<PdfArray>(PdfName.Create("IC")), stroke: false);
        if (!hasStroke && !hasFill)
            return false;

        SetLine(content, annotation, width);
        AppendPolyline(content, vertices);
        content.Append(close ? "h " : "").Append(Paint(hasStroke, hasFill));
        return true;
    }

    private static bool Ink(StringBuilder content, PdfDictionary annotation)
    {
        var width = BorderWidth(annotation);
        if (annotation.Get<PdfArray>(PdfName.Create("InkList")) is not { Count: > 0 } paths || width <= 0 || !SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: true))
            return false;

        // The strokes of a pen: round caps and joins
        SetLine(content, annotation, width);
        content.Append("1 J 1 j ");
        var points = 0;
        foreach (var item in paths)
        {
            if (PdfReference.ResolveIfReference(item) is PdfArray { Count: >= 2 } path && (points += path.Count) <= MaxPoints)
            {
                AppendPolyline(content, path);
            }
        }

        content.Append('S');
        return true;
    }

    /// <summary>Generates the appearance of a text markup annotation (12.5.6.10): its quadrilaterals, or its rectangle, highlighted (multiplied), underlined, struck out or underlined with a wave.</summary>
    private static bool TextMarkup(StringBuilder content, PdfDictionary resources, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var subtype = annotation.GetName(PdfNames.Subtype)!.Value;
        if (!SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: subtype != "Highlight"))
            return false;

        // The quadrilaterals: four points each, the top edge (x1 y1, x2 y2), then the bottom edge (x3 y3, x4 y4), as
        // applications write them
        var quadrilaterals = new List<(double X1, double Y1, double X2, double Y2, double X3, double Y3, double X4, double Y4)>();
        if (annotation.Get<PdfArray>(PdfName.Create("QuadPoints")) is { } points)
        {
            for (var i = 0; i + 7 < points.Count && i < MaxPoints; i += 8)
            {
                quadrilaterals.Add((points.GetNumber(i), points.GetNumber(i + 1), points.GetNumber(i + 2), points.GetNumber(i + 3), points.GetNumber(i + 4), points.GetNumber(i + 5), points.GetNumber(i + 6), points.GetNumber(i + 7)));
            }
        }

        if (quadrilaterals.Count == 0)
        {
            quadrilaterals.Add((rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Top, rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Bottom));
        }

        if (subtype == "Highlight")
        {
            resources[PdfNames.ExtGState] = new PdfDictionary { [PdfName.Create("GS0")] = new PdfDictionary { [PdfName.Create("BM")] = PdfName.Create("Multiply") } };
            content.Append("/GS0 gs ");
            foreach (var (x1, y1, x2, y2, x3, y3, x4, y4) in quadrilaterals)
            {
                Append(content, x1, y1).Append("m ");
                Append(content, x2, y2).Append("l ");
                Append(content, x4, y4).Append("l ");
                Append(content, x3, y3).Append("l h ");
            }

            content.Append('f');
            return true;
        }

        foreach (var (x1, y1, x2, y2, x3, y3, x4, y4) in quadrilaterals)
        {
            // The height of the line of text, and the line drawn at its bottom or middle
            var height = Math.Sqrt(((x1 - x3) * (x1 - x3)) + ((y1 - y3) * (y1 - y3)));
            var lineWidth = Math.Max(height / 14, 0.5);
            var t = subtype == "StrikeOut" ? 0.5 : 1.0 / 7;
            var (startX, startY) = (x3 + ((x1 - x3) * t), y3 + ((y1 - y3) * t));
            var (endX, endY) = (x4 + ((x2 - x4) * t), y4 + ((y2 - y4) * t));
            Append(content, lineWidth).Append("w ");
            if (subtype == "Squiggly")
            {
                // A wave of the height of a seventh of the line, along the bottom edge
                var length = Math.Sqrt(((endX - startX) * (endX - startX)) + ((endY - startY) * (endY - startY)));
                var steps = (int)Math.Clamp(Math.Ceiling(length / Math.Max(height / 7, 1)), 1, 1024);
                var (normalX, normalY) = height > 0 ? ((x1 - x3) / height * height / 14, (y1 - y3) / height * height / 14) : (0, 0);
                for (var i = 0; i <= steps; i++)
                {
                    var side = i % 2 == 0 ? -1 : 1;
                    Append(content, startX + ((endX - startX) * i / steps) + (side * normalX), startY + ((endY - startY) * i / steps) + (side * normalY)).Append(i == 0 ? "m " : "l ");
                }
            }
            else
            {
                Append(content, startX, startY).Append("m ");
                Append(content, endX, endY).Append("l ");
            }

            content.Append("S ");
        }

        return true;
    }

    /// <summary>
    /// Generates the appearance of the widget of a field (12.7.4.3): its background and border (<c>/MK</c>), then the
    /// value of a text field or of a choice field with the default appearance (<c>/DA</c>), or the check mark of a check
    /// box that is on.
    /// </summary>
    private static bool Widget(PdfPage page, StringBuilder content, PdfDictionary resources, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var form = page.Document.Catalog.Get<PdfDictionary>(PdfNames.AcroForm);
        var type = (Inherited(annotation, "FT") as PdfName)?.Value;
        var flags = Inherited(annotation, "Ff") is PdfNumber number ? (long)number.DoubleValue : 0;
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Top - rectangle.Bottom;
        var characteristics = annotation.Get<PdfDictionary>(PdfName.Create("MK"));

        // The background and the border
        var hasBackground = SetColor(content, characteristics?.Get<PdfArray>(PdfName.Create("BG")), stroke: false);
        if (hasBackground)
        {
            Append(content, rectangle.Left, rectangle.Bottom, width, height).Append("re f ");
        }

        var borderWidth = BorderWidth(annotation);
        var hasBorder = borderWidth > 0 && SetColor(content, characteristics?.Get<PdfArray>(PdfName.Create("BC")), stroke: true);
        if (hasBorder)
        {
            Append(content, borderWidth).Append("w ");
            Append(content, rectangle.Left + (borderWidth / 2), rectangle.Bottom + (borderWidth / 2), width - borderWidth, height - borderWidth).Append("re S ");
        }

        var padding = (hasBorder ? borderWidth : 0) + 2;
        switch (type)
        {
            case "Tx" or "Ch" when (flags & Password) == 0 && Inherited(annotation, "V") is { } value:
                {
                    var text = value switch
                    {
                        PdfString single => single.ToText(),
                        PdfArray { Count: > 0 } selected when selected.GetResolved(0) is PdfString first => first.ToText(),
                        _ => null,
                    };
                    if (string.IsNullOrEmpty(text))
                        return hasBackground || hasBorder;

                    if (!SetDefaultAppearance(content, resources, annotation, form, height - (2 * padding), out var fontSize))
                        return hasBackground || hasBorder;

                    // The quadding (12.7.4.3): left, centered or right; the lines of a multiline field from the top
                    var alignment = Inherited(annotation, "Q") is PdfNumber q ? (int)q.DoubleValue : (form?.GetInteger(PdfName.Create("Q"), 0) ?? 0);
                    var lines = (flags & Multiline) != 0 ? text.Split('\n') : [text.ReplaceLineEndings(" ")];
                    content.Append("/Tx BMC q ");
                    Append(content, rectangle.Left + 1, rectangle.Bottom + 1, width - 2, height - 2).Append("re W n BT ");
                    var y = (flags & Multiline) != 0 ? rectangle.Top - padding - fontSize : rectangle.Bottom + ((height - fontSize) / 2) + (fontSize * 0.22);
                    foreach (var line in lines.Take(1024))
                    {
                        var estimated = line.Length * fontSize * 0.5;
                        var x = alignment switch
                        {
                            1 => rectangle.Left + ((width - estimated) / 2),
                            2 => rectangle.Right - padding - estimated,
                            _ => rectangle.Left + padding,
                        };
                        content.Append("1 0 0 1 ");
                        Append(content, x, y).Append("Tm ");
                        AppendString(content, line).Append(" Tj ");
                        y -= fontSize * 1.15;
                    }

                    content.Append("ET Q EMC");
                    return true;
                }

            case "Btn" when annotation.GetName(PdfName.Create("AS")) is { Value: not "Off" } || (Inherited(annotation, "V") is PdfName { Value: not "Off" } && annotation.GetName(PdfName.Create("AS")) is null):
                {
                    // A check box that is on: the check mark of ZapfDingbats (character 4), in the color of the text
                    resources[PdfNames.Font] = new PdfDictionary
                    {
                        [PdfName.Create("ZaDb")] = new PdfDictionary { [PdfNames.Type] = PdfNames.Font, [PdfNames.Subtype] = PdfName.Create("Type1"), [PdfNames.BaseFont] = PdfName.Create("ZapfDingbats") },
                    };
                    var size = Math.Max(1, Math.Min(width, height) - (2 * padding));
                    content.Append("q BT /ZaDb ");
                    Append(content, size).Append("Tf ");
                    Append(content, rectangle.Left + ((width - (size * 0.75)) / 2), rectangle.Bottom + ((height - (size * 0.7)) / 2)).Append("Td (4) Tj ET Q");
                    return true;
                }

            default:
                return hasBackground || hasBorder;
        }
    }

    /// <summary>Writes the default appearance of a field (12.7.4.3): its font and color, a size of 0 being the size that fits the height of the field, with the font of the resources of the form.</summary>
    private static bool SetDefaultAppearance(StringBuilder content, PdfDictionary resources, PdfDictionary annotation, PdfDictionary? form, double availableHeight, out double fontSize)
    {
        fontSize = 0;
        var defaultAppearance = (Inherited(annotation, "DA") as PdfString ?? form?.Get<PdfString>(PdfName.Create("DA")))?.ToText();
        var tokens = (defaultAppearance ?? "/Helv 0 Tf 0 g").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var fontIndex = Array.IndexOf(tokens, "Tf");
        if (fontIndex < 2 || tokens[fontIndex - 2] is not ['/', ..])
            return false;

        var fontName = tokens[fontIndex - 2][1..];
        if (!double.TryParse(tokens[fontIndex - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out fontSize) || !double.IsFinite(fontSize) || fontSize <= 0)
        {
            fontSize = Math.Clamp(availableHeight * 0.75, 4, 12);
        }

        // The font of the resources of the form, or Helvetica
        var font = form?.Get<PdfDictionary>(PdfName.Create("DR"))?.Get<PdfDictionary>(PdfNames.Font)?.GetResolved(PdfName.Create(fontName))
            ?? new PdfDictionary { [PdfNames.Type] = PdfNames.Font, [PdfNames.Subtype] = PdfName.Create("Type1"), [PdfNames.BaseFont] = PdfName.Create("Helvetica"), [PdfNames.Encoding] = PdfName.Create("WinAnsiEncoding") };
        resources[PdfNames.Font] = new PdfDictionary { [PdfName.Create("F0")] = font };

        // The operators of the default appearance, with the font replaced
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i == fontIndex - 2)
            {
                content.Append("/F0 ");
            }
            else if (i == fontIndex - 1)
            {
                Append(content, fontSize);
            }
            else if (tokens[i] is not ("BT" or "ET" or "Tj" or "TJ" or "'" or "\"" or "Do"))
            {
                content.Append(tokens[i]).Append(' ');
            }
        }

        return true;
    }

    /// <summary>Gets an entry of a field, inherited from its parents (12.7.4.1).</summary>
    private static PdfObject? Inherited(PdfDictionary field, string key)
    {
        var name = PdfName.Create(key);
        var current = field;
        for (var depth = 0; current is not null && depth < MaxFieldDepth; depth++)
        {
            if (current.GetResolved(name) is { } value)
                return value;

            current = current.Get<PdfDictionary>(PdfName.Create("Parent"));
        }

        return null;
    }

    /// <summary>Gets the width of the border of an annotation: <c>/BS /W</c>, the third number of <c>/Border</c>, 1 by default (12.5.4).</summary>
    private static double BorderWidth(PdfDictionary annotation)
    {
        var width = annotation.Get<PdfDictionary>(PdfName.Create("BS"))?.GetResolved(PdfName.Create("W")) is PdfNumber bs
            ? bs.DoubleValue
            : annotation.Get<PdfArray>(PdfName.Create("Border")) is { Count: >= 3 } border ? border.GetNumber(2, 1) : 1;
        return double.IsFinite(width) ? Math.Clamp(width, 0, 1000) : 1;
    }

    /// <summary>Writes the line width and the dashes of the border style (12.5.4).</summary>
    private static void SetLine(StringBuilder content, PdfDictionary annotation, double width)
    {
        Append(content, width).Append("w ");
        if (annotation.Get<PdfDictionary>(PdfName.Create("BS")) is { } style && style.GetName(PdfNames.S)?.Value == "D")
        {
            var dashes = style.Get<PdfArray>(PdfNames.D) is { Count: > 0 } array ? array : PdfArray.FromNumbers(3);
            content.Append('[');
            foreach (var item in dashes.Take(16))
            {
                if (PdfReference.ResolveIfReference(item) is PdfNumber dash && double.IsFinite(dash.DoubleValue))
                {
                    Append(content, Math.Abs(dash.DoubleValue));
                }
            }

            content.Append("] 0 d ");
        }
    }

    /// <summary>Writes the operator of a color of an annotation: no component is transparent, 1 gray, 3 RGB, 4 CMYK (12.5.2).</summary>
    private static bool SetColor(StringBuilder content, PdfArray? color, bool stroke)
    {
        var count = color?.Count ?? 0;
        if (count is not (1 or 3 or 4))
            return false;

        for (var i = 0; i < count; i++)
        {
            Append(content, Math.Clamp(color!.GetNumber(i), 0, 1));
        }

        var op = count switch { 1 => "g", 3 => "rg", _ => "k" };
        content.Append(stroke ? op.ToUpperInvariant() : op).Append(' ');
        return true;
    }

    private static string Paint(bool stroke, bool fill) => (stroke, fill) switch
    {
        (true, true) => "B",
        (true, false) => "S",
        _ => "f",
    };

    private static void AppendPolyline(StringBuilder content, PdfArray points)
    {
        for (var i = 0; i + 1 < points.Count && i < MaxPoints; i += 2)
        {
            Append(content, points.GetNumber(i), points.GetNumber(i + 1)).Append(i == 0 ? "m " : "l ");
        }
    }

    private static StringBuilder Append(StringBuilder content, params ReadOnlySpan<double> values)
    {
        foreach (var value in values)
        {
            content.Append((double.IsFinite(value) ? value : 0).ToString("0.####", CultureInfo.InvariantCulture)).Append(' ');
        }

        return content;
    }

    /// <summary>Writes a text as a literal string of the single-byte encoding of the font (Latin-1), the characters beyond it as question marks.</summary>
    private static StringBuilder AppendString(StringBuilder content, string text)
    {
        content.Append('(');
        foreach (var c in text)
        {
            switch (c)
            {
                case '(' or ')' or '\\':
                    content.Append('\\').Append(c);
                    break;
                case < ' ':
                    break;
                case > 'ÿ':
                    content.Append('?');
                    break;
                default:
                    content.Append(c);
                    break;
            }
        }

        return content.Append(')');
    }
}
