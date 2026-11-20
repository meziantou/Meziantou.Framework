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
    private const int Comb = 1 << 24;
    private const int RichTextFlag = 1 << 25;

    // The nesting of the fields whose entries a widget inherits
    private const int MaxFieldDepth = 32;

    // The standard fonts by their PostScript names (9.6.2.2)
    private static readonly Dictionary<string, PdfStandardFont> StandardFonts = new(StringComparer.Ordinal)
    {
        ["Times-Roman"] = PdfStandardFont.TimesRoman,
        ["Times-Bold"] = PdfStandardFont.TimesBold,
        ["Times-Italic"] = PdfStandardFont.TimesItalic,
        ["Times-BoldItalic"] = PdfStandardFont.TimesBoldItalic,
        ["Helvetica"] = PdfStandardFont.Helvetica,
        ["Helvetica-Bold"] = PdfStandardFont.HelveticaBold,
        ["Helvetica-Oblique"] = PdfStandardFont.HelveticaOblique,
        ["Helvetica-BoldOblique"] = PdfStandardFont.HelveticaBoldOblique,
        ["Courier"] = PdfStandardFont.Courier,
        ["Courier-Bold"] = PdfStandardFont.CourierBold,
        ["Courier-Oblique"] = PdfStandardFont.CourierOblique,
        ["Courier-BoldOblique"] = PdfStandardFont.CourierBoldOblique,
        ["Symbol"] = PdfStandardFont.Symbol,
        ["ZapfDingbats"] = PdfStandardFont.ZapfDingbats,
    };

    /// <summary>Gets the appearance of an annotation that has none, or of a field whose form asks for new appearances (<c>/NeedAppearances</c>); <see langword="null"/> when none is generated.</summary>
    /// <param name="page">The page of the annotation.</param>
    /// <param name="annotation">The annotation dictionary.</param>
    /// <param name="rectangle">The rectangle of the annotation.</param>
    /// <param name="includeIcons">
    /// Whether the annotations a viewer draws with its own icons or text layout also get an appearance: the notes, the
    /// free texts, the stamps, the carets and the redactions. The renderer leaves them out, <see cref="PdfAnnotation.UpdateAppearance"/>
    /// draws them.
    /// </param>
    public static PdfStream? Generate(PdfPage page, PdfDictionary annotation, PdfRectangle rectangle, bool includeIcons = false)
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
            "Text" when includeIcons => Note(content, annotation, rectangle),
            "FreeText" when includeIcons => FreeText(content, resources, annotation, rectangle),
            "Stamp" when includeIcons => Stamp(content, resources, annotation, rectangle),
            "Caret" when includeIcons => Caret(content, resources, annotation, rectangle),
            "Redact" when includeIcons => Redact(content, annotation, rectangle),
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
        var (x1, y1, x2, y2) = (line.GetNumber(0), line.GetNumber(1), line.GetNumber(2), line.GetNumber(3));
        Append(content, x1, y1).Append("m ");
        Append(content, x2, y2).Append("l S ");
        AppendLineEndings(content, annotation, width, (x2, y2, x1, y1), (x1, y1, x2, y2));
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
        content.Append(close ? "h " : "").Append(Paint(hasStroke, hasFill)).Append(' ');
        if (!close && hasStroke)
        {
            // The endings of a polyline are at its first and last vertices (12.5.6.9)
            var count = Math.Min(vertices.Count, MaxPoints) / 2 * 2;
            var start = (vertices.GetNumber(2), vertices.GetNumber(3), vertices.GetNumber(0), vertices.GetNumber(1));
            var end = (vertices.GetNumber(count - 4), vertices.GetNumber(count - 3), vertices.GetNumber(count - 2), vertices.GetNumber(count - 1));
            AppendLineEndings(content, annotation, width, start, end);
        }

        return true;
    }

    /// <summary>
    /// Draws the line endings of a line or a polyline (<c>/LE</c>, 12.5.6.7, table 179): each segment goes from the point
    /// before the end to the end. The closed shapes are filled with the interior color (<c>/IC</c>) when the annotation has
    /// one. The size of an ending grows with the width of the line.
    /// </summary>
    private static void AppendLineEndings(StringBuilder content, PdfDictionary annotation, double width, (double FromX, double FromY, double X, double Y) start, (double FromX, double FromY, double X, double Y) end)
    {
        if (annotation.Get<PdfArray>(PdfName.Create("LE")) is not { Count: 2 } endings)
            return;

        var fill = new StringBuilder();
        var hasFill = SetColor(fill, annotation.Get<PdfArray>(PdfName.Create("IC")), stroke: false);
        var size = (width * 3) + 4;
        AppendLineEnding(content, (endings.GetResolved(0) as PdfName)?.Value, start, size, hasFill ? fill.ToString() : null);
        AppendLineEnding(content, (endings.GetResolved(1) as PdfName)?.Value, end, size, hasFill ? fill.ToString() : null);
    }

    private static void AppendLineEnding(StringBuilder content, string? style, (double FromX, double FromY, double X, double Y) segment, double size, string? fill)
    {
        var (fromX, fromY, x, y) = segment;
        var length = Math.Sqrt(((x - fromX) * (x - fromX)) + ((y - fromY) * (y - fromY)));
        if (style is null or "None" || !(length > 0) || !double.IsFinite(length))
            return;

        // The direction of the segment at its end, and the normal to it
        var (dx, dy) = ((x - fromX) / length, (y - fromY) / length);
        var (nx, ny) = (-dy, dx);
        (double X, double Y) Point(double along, double across) => (x + (dx * along) + (nx * across), y + (dy * along) + (ny * across));

        // The arrows open at 30 degrees on each side of the line
        var back = size * Math.Cos(Math.PI / 6);
        var side = size * Math.Sin(Math.PI / 6);
        var half = size / 2;
        var closed = true;
        var path = new StringBuilder();
        switch (style)
        {
            case "OpenArrow" or "ClosedArrow":
                MoveTo(Point(-back, side));
                LineTo(Point(0, 0));
                LineTo(Point(-back, -side));
                closed = style == "ClosedArrow";
                break;

            case "ROpenArrow" or "RClosedArrow":
                MoveTo(Point(back, side));
                LineTo(Point(0, 0));
                LineTo(Point(back, -side));
                closed = style == "RClosedArrow";
                break;

            case "Butt":
                MoveTo(Point(0, half));
                LineTo(Point(0, -half));
                closed = false;
                break;

            case "Slash":
                // 30 degrees clockwise from the normal
                var (sx, sy) = (half * Math.Sin(Math.PI / 6), half * Math.Cos(Math.PI / 6));
                MoveTo(Point(sx, sy));
                LineTo(Point(-sx, -sy));
                closed = false;
                break;

            case "Square":
                MoveTo(Point(-half, -half));
                LineTo(Point(half, -half));
                LineTo(Point(half, half));
                LineTo(Point(-half, half));
                break;

            case "Diamond":
                MoveTo(Point(-half, 0));
                LineTo(Point(0, -half));
                LineTo(Point(half, 0));
                LineTo(Point(0, half));
                break;

            case "Circle":
                const double Kappa = 0.5523;
                MoveTo(Point(half, 0));
                CurveTo(Point(half, half * Kappa), Point(half * Kappa, half), Point(0, half));
                CurveTo(Point(-half * Kappa, half), Point(-half, half * Kappa), Point(-half, 0));
                CurveTo(Point(-half, -half * Kappa), Point(-half * Kappa, -half), Point(0, -half));
                CurveTo(Point(half * Kappa, -half), Point(half, -half * Kappa), Point(half, 0));
                break;

            default:
                return;
        }

        // The color is set outside of the path object (8.5.1)
        if (closed && fill is not null)
        {
            content.Append("q ").Append(fill).Append(path).Append("h B Q ");
        }
        else
        {
            content.Append(path).Append(closed ? "h S " : "S ");
        }

        void MoveTo((double X, double Y) point) => Append(path, point.X, point.Y).Append("m ");
        void LineTo((double X, double Y) point) => Append(path, point.X, point.Y).Append("l ");
        void CurveTo((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) => Append(path, a.X, a.Y, b.X, b.Y, c.X, c.Y).Append("c ");
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

    /// <summary>Generates the icon of a note (12.5.6.4): a speech bubble filled with the color of the annotation, yellow by default, with two lines of text.</summary>
    private static bool Note(StringBuilder content, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var (x, y) = (rectangle.Left, rectangle.Bottom);
        var size = Math.Min(rectangle.Right - rectangle.Left, rectangle.Top - rectangle.Bottom);
        double S(double value) => value * size / 20;
        if (!SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: false))
        {
            content.Append("1 0.85 0.2 rg ");
        }

        content.Append("0.2 G 1 j ");
        Append(content, S(1)).Append("w ");
        Append(content, x + S(2), y + S(6)).Append("m ");
        Append(content, x + S(2), y + S(18)).Append("l ");
        Append(content, x + S(18), y + S(18)).Append("l ");
        Append(content, x + S(18), y + S(6)).Append("l ");
        Append(content, x + S(9), y + S(6)).Append("l ");
        Append(content, x + S(5), y + S(2)).Append("l ");
        Append(content, x + S(5), y + S(6)).Append("l h B ");
        Append(content, x + S(5), y + S(14.5)).Append("m ");
        Append(content, x + S(15), y + S(14.5)).Append("l ");
        Append(content, x + S(5), y + S(10.5)).Append("m ");
        Append(content, x + S(13), y + S(10.5)).Append("l S");
        return true;
    }

    /// <summary>
    /// Generates the appearance of a free text (12.5.6.6): its border, in the color of the text, and its text broken into
    /// lines that fit the rectangle, with the font, size and color of its default appearance (<c>/DA</c>) and its
    /// alignment (<c>/Q</c>). A font that is not a standard font is replaced with Helvetica.
    /// </summary>
    private static bool FreeText(StringBuilder content, PdfDictionary resources, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Top - rectangle.Bottom;
        var text = annotation.GetText(PdfNames.Contents) ?? "";
        var defaultAppearance = new StringBuilder();
        if (!SetDefaultAppearance(defaultAppearance, resources, annotation, form: null, height, out var fontSize))
            return false;

        var borderWidth = annotation.Get<PdfDictionary>(PdfName.Create("BS")) is null && annotation.Get<PdfArray>(PdfNames.Border) is null ? 0 : BorderWidth(annotation);
        content.Append("q ").Append(defaultAppearance);
        if (borderWidth > 0)
        {
            // The border is stroked in the color of the text: the default appearance sets the color of the fill
            content.Append("q ");
            content.Append(StrokeColorOf(defaultAppearance.ToString()));
            SetLine(content, annotation, borderWidth);
            Append(content, rectangle.Left + (borderWidth / 2), rectangle.Bottom + (borderWidth / 2), width - borderWidth, height - borderWidth).Append("re S Q ");
        }

        var padding = borderWidth + 2;
        var font = StandardFont(resources);
        var lines = WrapText(text, font, fontSize, width - (2 * padding), height - (2 * padding));
        var alignment = annotation.GetInteger(PdfName.Create("Q"), 0);
        Append(content, rectangle.Left + borderWidth, rectangle.Bottom + borderWidth, Math.Max(width - (2 * borderWidth), 0), Math.Max(height - (2 * borderWidth), 0)).Append("re W n BT ");
        var y = rectangle.Top - padding - (fontSize * 0.8);
        foreach (var line in lines.Take(1024))
        {
            if (y < rectangle.Bottom - fontSize)
                break;

            var lineWidth = MeasureText(font, line, fontSize);
            var x = alignment switch
            {
                1 => rectangle.Left + ((width - lineWidth) / 2),
                2 => rectangle.Right - padding - lineWidth,
                _ => rectangle.Left + padding,
            };
            content.Append("1 0 0 1 ");
            Append(content, x, y).Append("Tm ");
            AppendString(content, line).Append(" Tj ");
            y -= fontSize * 1.2;
        }

        content.Append("ET Q");
        return true;
    }

    /// <summary>Generates the appearance of a rubber stamp (12.5.6.12): its name, such as <c>NOT APPROVED</c>, in a frame with rounded corners, in the color of the annotation, red by default.</summary>
    private static bool Stamp(StringBuilder content, PdfDictionary resources, PdfDictionary annotation, PdfRectangle rectangle)
    {
        var name = annotation.GetName(PdfNames.Name)?.Value ?? "Draft";
        var text = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            // A word starts at an uppercase letter after a lowercase one, in the name: NotApproved is NOT APPROVED
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
            {
                text.Append(' ');
            }

            text.Append(char.ToUpperInvariant(name[i]));
        }

        var colors = new StringBuilder();
        if (!SetColor(colors, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: false))
        {
            colors.Append("0.8 0.1 0.1 rg ");
        }

        var fill = colors.ToString();
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Top - rectangle.Bottom;
        var line = Math.Max(Math.Min(width, height) * 0.06, 0.5);
        var font = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
        var label = text.ToString();
        var fontSize = Math.Max(Math.Min((height - (4 * line)) * 0.7, (width - (6 * line)) / Math.Max(MeasureText(font, label, 1), 0.001)), 1);
        var radius = Math.Min(width, height) * 0.2;
        var k = radius * 0.5523;
        var (l, b, r, t) = (rectangle.Left + line, rectangle.Bottom + line, rectangle.Right - line, rectangle.Top - line);
        content.Append(fill).Append(fill.Replace(" rg ", " RG ", StringComparison.Ordinal).Replace(" g ", " G ", StringComparison.Ordinal).Replace(" k ", " K ", StringComparison.Ordinal));
        Append(content, line).Append("w ");
        Append(content, l + radius, b).Append("m ");
        Append(content, r - radius, b).Append("l ");
        Append(content, r - radius + k, b, r, b + radius - k, r, b + radius).Append("c ");
        Append(content, r, t - radius).Append("l ");
        Append(content, r, t - radius + k, r - radius + k, t, r - radius, t).Append("c ");
        Append(content, l + radius, t).Append("l ");
        Append(content, l + radius - k, t, l, t - radius + k, l, t - radius).Append("c ");
        Append(content, l, b + radius).Append("l ");
        Append(content, l, b + radius - k, l + radius - k, b, l + radius, b).Append("c h S ");
        resources[PdfNames.Font] = new PdfDictionary { [PdfName.Create("F0")] = StandardFontDictionary("Helvetica-Bold") };
        content.Append("BT /F0 ");
        Append(content, fontSize).Append("Tf ");
        Append(content, rectangle.Left + ((width - MeasureText(font, label, fontSize)) / 2), rectangle.Bottom + ((height - (font.CapHeight * fontSize / 1000)) / 2)).Append("Td ");
        AppendString(content, label).Append(" Tj ET");
        return true;
    }

    /// <summary>Generates the appearance of a caret (12.5.6.11): a caret filled with the color of the annotation, black by default, or a paragraph symbol (<c>/Sy /P</c>).</summary>
    private static bool Caret(StringBuilder content, PdfDictionary resources, PdfDictionary annotation, PdfRectangle rectangle)
    {
        if (!SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: false))
        {
            content.Append("0 g ");
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Top - rectangle.Bottom;
        if (annotation.GetName(PdfName.Create("Sy"))?.Value == "P")
        {
            resources[PdfNames.Font] = new PdfDictionary { [PdfName.Create("F0")] = StandardFontDictionary("Helvetica") };
            var size = Math.Min(height, width * 1.6);
            content.Append("BT /F0 ");
            Append(content, size).Append("Tf ");
            Append(content, rectangle.Left + ((width - (size * 0.537)) / 2), rectangle.Bottom + (height * 0.15)).Append(@"Td (\266) Tj ET");
            return true;
        }

        // A caret: two curved strokes that meet at the top
        var (x, y) = (rectangle.Left, rectangle.Bottom);
        Append(content, x, y).Append("m ");
        Append(content, x + (width * 0.4), y + (height * 0.3), x + (width * 0.45), y + (height * 0.7), x + (width * 0.5), y + height).Append("c ");
        Append(content, x + (width * 0.55), y + (height * 0.7), x + (width * 0.6), y + (height * 0.3), x + width, y).Append("c ");
        Append(content, x + (width * 0.6), y + (height * 0.12), x + (width * 0.4), y + (height * 0.12), x, y).Append("c f");
        return true;
    }

    /// <summary>Generates the appearance of a redaction annotation before it is applied (12.5.6.23): the outline of its regions, in the color of the annotation, red by default.</summary>
    private static bool Redact(StringBuilder content, PdfDictionary annotation, PdfRectangle rectangle)
    {
        if (!SetColor(content, annotation.Get<PdfArray>(PdfName.Create("C")), stroke: true))
        {
            content.Append("1 0 0 RG ");
        }

        content.Append("1 w ");
        var regions = PdfAnnotation.ReadQuadrilaterals(annotation.Get<PdfArray>(PdfName.Create("QuadPoints")));
        if (regions.Count == 0)
        {
            regions.Add(rectangle);
        }

        foreach (var region in regions)
        {
            Append(content, region.Left + 0.5, region.Bottom + 0.5, Math.Max(region.Right - region.Left - 1, 0), Math.Max(region.Top - region.Bottom - 1, 0)).Append("re ");
        }

        content.Append('S');
        return true;
    }

    /// <summary>Gets the stroking operator of the color the default appearance sets for the fill (<c>g</c>, <c>rg</c> or <c>k</c>), so that a border has the color of the text.</summary>
    private static string StrokeColorOf(string defaultAppearance)
    {
        var tokens = defaultAppearance.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            var count = tokens[i] switch { "g" => 1, "rg" => 3, "k" => 4, _ => 0 };
            if (count > 0 && i >= count)
                return string.Join(' ', tokens[(i - count)..i]) + " " + tokens[i].ToUpperInvariant() + " ";
        }

        return "0 G ";
    }

    /// <summary>Gets the standard font of the font resource <c>F0</c> the default appearance set: Helvetica when it is not one of the standard fonts.</summary>
    private static PdfFont StandardFont(PdfDictionary resources)
    {
        var baseFont = resources.Get<PdfDictionary>(PdfNames.Font)?.Get<PdfDictionary>(PdfName.Create("F0"))?.GetName(PdfNames.BaseFont)?.Value;
        return StandardFonts.TryGetValue(baseFont ?? "", out var font) ? PdfFont.GetStandardFont(font) : PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
    }

    private static PdfDictionary StandardFontDictionary(string baseFont) => new()
    {
        [PdfNames.Type] = PdfNames.Font,
        [PdfNames.Subtype] = PdfName.Create("Type1"),
        [PdfNames.BaseFont] = PdfName.Create(baseFont),
        [PdfNames.Encoding] = PdfName.Create("WinAnsiEncoding"),
    };

    /// <summary>Breaks a text into lines that fit a width with the metrics of a standard font; the characters the font cannot show are measured as question marks.</summary>
    private static List<string> WrapText(string text, PdfFont font, double fontSize, double width, double height)
    {
        // Only the text that can show in the box is wrapped: the lines that fit its height, of at most the characters of
        // the narrowest glyphs (a fifth of the font size) that fit its width. The text of a file is not bounded, and
        // wrapping a long word measures its rest for each line
        var maxLines = (int)Math.Clamp(Math.Ceiling(height / (fontSize * 1.2)) + 1, 1, 1024);
        var maxCharacters = (int)Math.Clamp(Math.Ceiling(Math.Max(width, fontSize) / (fontSize * 0.2)) + 1, 1, 4096);
        // A run of spaces longer than a line is a line break, as the wrapper drops the spaces at the end and at the start of
        // its lines: it is shortened to one line, so that the spaces do not take the budget of the visible text
        var budget = maxLines * maxCharacters;
        var bounded = new StringBuilder(Math.Min(text.Length, budget));
        var spaces = 0;
        foreach (var c in text)
        {
            if (bounded.Length >= budget)
                break;

            spaces = c is ' ' or '\t' ? spaces + 1 : 0;
            if (spaces <= maxCharacters)
            {
                bounded.Append(c);
            }
        }

        var printable = ToLatin1(bounded.ToString());
        try
        {
            return [.. PdfTextWrapper.Wrap(printable, font, fontSize, Math.Max(width, fontSize)).Select(line => line.Text)];
        }
        catch (ArgumentException)
        {
            return [.. printable.Split('\n')];
        }
    }

    private static double MeasureText(PdfFont font, string text, double fontSize)
    {
        try
        {
            return font.GetTextWidth(ToLatin1(text), fontSize);
        }
        catch (ArgumentException)
        {
            return text.Length * fontSize * 0.5;
        }
    }

    private static string ToLatin1(string text) => string.Create(text.Length, text, (span, value) =>
    {
        for (var i = 0; i < value.Length; i++)
        {
            span[i] = value[i] is > '\u00FF' or (< ' ' and not '\n') ? '?' : value[i];
        }
    });

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
                    if (type == "Tx" && (flags & RichTextFlag) != 0 && RichTextValue(annotation) is { } richText
                        && RichText.TryParse(richText, (Inherited(annotation, "DS") as PdfString)?.ToText(), fontSize) is { Count: > 0 } paragraphs)
                    {
                        AppendRichText(content, resources, paragraphs, rectangle, padding, (int)alignment);
                        return true;
                    }

                    if (type == "Tx" && (flags & (Comb | Multiline | Password)) == Comb && Inherited(annotation, "MaxLen") is PdfNumber { DoubleValue: >= 1 and <= 10_000 } maxLength)
                    {
                        // A comb field (12.7.5.3): the width is divided into as many cells as the maximum length, one
                        // character centered in each
                        var cells = (int)maxLength.DoubleValue;
                        var cellWidth = width / cells;
                        var baseline = rectangle.Bottom + ((height - fontSize) / 2) + (fontSize * 0.22);
                        content.Append("/Tx BMC q ");
                        Append(content, rectangle.Left + 1, rectangle.Bottom + 1, width - 2, height - 2).Append("re W n BT ");
                        var index = 0;
                        foreach (var rune in text.ReplaceLineEndings(" ").EnumerateRunes())
                        {
                            if (index >= cells)
                                break;

                            var character = rune.ToString();
                            content.Append("1 0 0 1 ");
                            Append(content, rectangle.Left + (index * cellWidth) + ((cellWidth - (fontSize * 0.5)) / 2), baseline).Append("Tm ");
                            AppendString(content, character).Append(" Tj ");
                            index++;
                        }

                        content.Append("ET Q EMC");
                        return true;
                    }

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

    /// <summary>Gets the rich text value of a field (<c>/RV</c>, 12.7.4.3): a text string or a stream.</summary>
    private static string? RichTextValue(PdfDictionary annotation)
    {
        try
        {
            return Inherited(annotation, "RV") switch
            {
                PdfString text => text.ToText(),
                PdfStream stream => System.Text.Encoding.UTF8.GetString(stream.GetDecodedData()),
                _ => null,
            };
        }
        catch (PdfException)
        {
            return null;
        }
    }

    /// <summary>
    /// Draws the paragraphs of a rich text value from the top of the field, each run in the standard 14 font of its style,
    /// its size and its color. The runs of a line follow each other: the text position advances with their glyphs.
    /// </summary>
    private static void AppendRichText(StringBuilder content, PdfDictionary resources, List<RichTextParagraph> paragraphs, PdfRectangle rectangle, double padding, int defaultAlignment)
    {
        var fonts = new PdfDictionary();
        resources[PdfNames.Font] = fonts;
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Top - rectangle.Bottom;
        content.Append("/Tx BMC q ");
        Append(content, rectangle.Left + 1, rectangle.Bottom + 1, width - 2, height - 2).Append("re W n BT ");
        var y = rectangle.Top - padding;
        foreach (var paragraph in paragraphs.Take(1024))
        {
            var lineHeight = paragraph.Runs.Count > 0 ? paragraph.Runs.Max(run => run.Style.Size) : 12;
            y -= lineHeight;
            var estimated = paragraph.Runs.Sum(run => run.Text.Length * run.Style.Size * 0.5);
            var alignment = paragraph.Alignment != 0 ? paragraph.Alignment : defaultAlignment;
            var x = alignment switch
            {
                1 => rectangle.Left + ((width - estimated) / 2),
                2 => rectangle.Right - padding - estimated,
                _ => rectangle.Left + padding,
            };
            content.Append("1 0 0 1 ");
            Append(content, x, y).Append("Tm ");
            foreach (var run in paragraph.Runs)
            {
                var baseFont = run.Style.BaseFont;
                if (!names.TryGetValue(baseFont, out var name))
                {
                    name = "R" + names.Count.ToString(CultureInfo.InvariantCulture);
                    names.Add(baseFont, name);
                    fonts[PdfName.Create(name)] = new PdfDictionary { [PdfNames.Type] = PdfNames.Font, [PdfNames.Subtype] = PdfName.Create("Type1"), [PdfNames.BaseFont] = PdfName.Create(baseFont), [PdfNames.Encoding] = PdfName.Create("WinAnsiEncoding") };
                }

                var (red, green, blue) = run.Style.Color ?? (0, 0, 0);
                Append(content, red, green, blue).Append("rg /").Append(name).Append(' ');
                Append(content, run.Style.Size).Append("Tf ");
                AppendString(content, run.Text).Append(" Tj ");
            }

            y -= lineHeight * 0.15;
        }

        content.Append("ET Q EMC");
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

        // The font of the resources of the form, the standard font the name designates, or Helvetica
        var font = form?.Get<PdfDictionary>(PdfName.Create("DR"))?.Get<PdfDictionary>(PdfNames.Font)?.GetResolved(PdfName.Create(fontName))
            ?? StandardFontDictionary(StandardFonts.TryGetValue(fontName, out var standard) && standard is not (PdfStandardFont.Symbol or PdfStandardFont.ZapfDingbats) ? fontName : "Helvetica");
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
