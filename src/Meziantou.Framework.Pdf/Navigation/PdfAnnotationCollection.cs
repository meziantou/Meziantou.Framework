using System.Collections;
using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The annotations of a page: the <c>/Annots</c> array (ISO 32000-2, 12.5).</summary>
/// <remarks>
/// The annotations are read once and kept until the <c>/Annots</c> array changes, through the collection or the object
/// model: <see cref="Count"/> and the indexer do not read the array again, and an annotation is the same instance until
/// then.
/// </remarks>
public sealed class PdfAnnotationCollection : IReadOnlyList<PdfAnnotation>
{
    private readonly PdfPage _page;
    private List<PdfAnnotation>? _items;
    private PdfArray? _itemsArray;
    private int _itemsVersion;

    internal PdfAnnotationCollection(PdfPage page)
    {
        _page = page;
    }

    /// <summary>Gets the number of annotations.</summary>
    public int Count => GetItems().Count;

    /// <summary>Gets an annotation.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The annotation.</returns>
    public PdfAnnotation this[int index] => GetItems()[index];

    /// <summary>Adds a link that opens a URI.</summary>
    /// <param name="rectangle">The region of the page, in default user space.</param>
    /// <param name="uri">The URI: an absolute URI, written as 7-bit ASCII (12.6.4.8): an internationalized host in its IDNA ASCII form (<c>xn--</c> labels), the other non-ASCII characters percent-encoded as UTF-8.</param>
    /// <returns>The link.</returns>
    public PdfLinkAnnotation AddLink(PdfRectangle rectangle, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("The URI must be absolute", nameof(uri));

        var action = new PdfDictionary
        {
            [PdfNames.S] = PdfNames.URI,
            [PdfNames.URI] = PdfString.FromBytes(Encoding.ASCII.GetBytes(PdfUriEncoder.ToAscii(uri))),
        };
        return AddLink(rectangle, PdfNames.A, action);
    }

    /// <summary>Adds a link that goes to a destination of the document.</summary>
    /// <param name="rectangle">The region of the page, in default user space.</param>
    /// <param name="destination">The destination.</param>
    /// <returns>The link.</returns>
    public PdfLinkAnnotation AddLink(PdfRectangle rectangle, PdfDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Page.Document != _page.Document)
            throw new ArgumentException("The destination is a page of another document", nameof(destination));

        var link = AddLink(rectangle, key: null, target: null);
        destination.WriteTo(link.Dictionary);
        return link;
    }

    /// <summary>Adds a file attachment annotation (12.5.6.15) that embeds a new file.</summary>
    /// <param name="rectangle">The region of the icon, in default user space.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The attributes of the file, or <see langword="null"/> for the defaults.</param>
    /// <param name="icon">The icon of the annotation.</param>
    /// <returns>The annotation. Its <see cref="PdfAnnotation.Contents"/>, the text that describes it, is the description of the file or its name.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is empty, or the MIME type is not valid.</exception>
    public PdfFileAttachmentAnnotation AddFileAttachment(PdfRectangle rectangle, string fileName, ReadOnlySpan<byte> data, PdfEmbeddedFileOptions? options = null, PdfFileAttachmentIcon icon = PdfFileAttachmentIcon.PushPin)
    {
        var (dictionary, reference) = PdfEmbeddedFile.Create(_page.Document, fileName, data, options);
        return AddFileAttachment(rectangle, new PdfEmbeddedFile(_page.Document, dictionary, reference, key: null), icon);
    }

    /// <summary>Adds a file attachment annotation (12.5.6.15) that holds a file of the document, such as a file of <see cref="PdfDocument.EmbeddedFiles"/>.</summary>
    /// <param name="rectangle">The region of the icon, in default user space.</param>
    /// <param name="file">The file.</param>
    /// <param name="icon">The icon of the annotation.</param>
    /// <returns>The annotation. Its <see cref="PdfAnnotation.Contents"/>, the text that describes it, is the description of the file or its name.</returns>
    /// <exception cref="ArgumentException">The file is not an indirect object of the document.</exception>
    public PdfFileAttachmentAnnotation AddFileAttachment(PdfRectangle rectangle, PdfEmbeddedFile file, PdfFileAttachmentIcon icon = PdfFileAttachmentIcon.PushPin)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Document != _page.Document || file.Reference is not { } fileReference)
            throw new ArgumentException("The file is not an indirect object of the document of the page", nameof(file));

        var iconName = PdfFileAttachmentAnnotation.GetIconName(icon);
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfFileAttachmentAnnotation.FileAttachmentName,
            [PdfNames.Rect] = rectangle.ToArray(),
            [PdfNames.Contents] = PdfString.FromText(file.Description ?? file.FileName),
            [PdfNames.P] = _page.Reference,

            // Print, NoZoom and NoRotate (12.5.3): the icon keeps its size and orientation
            [PdfNames.F] = PdfInteger.Get(4 | 8 | 16),
            [PdfFileAttachmentAnnotation.FSName] = fileReference,
            [PdfNames.Name] = iconName,

            // The file is associated with the annotation (14.13.2), which PDF/A-3 requires of every embedded file
            [PdfEmbeddedFile.AFName] = new PdfArray(fileReference),
            [PdfName.Create("AP")] = new PdfDictionary { [PdfNames.N] = CreateIcon(rectangle) },
        };
        var reference = _page.Document.Objects.Add(dictionary);
        GetOrCreateArray().Add(reference);
        return new PdfFileAttachmentAnnotation(_page, dictionary, reference);
    }

    /// <summary>Adds a note (a text annotation, 12.5.6.4): an icon that opens a text, drawn with the color.</summary>
    /// <param name="position">The lower-left corner of the icon, in default user space; the icon is 20 x 20.</param>
    /// <param name="text">The text of the note.</param>
    /// <param name="color">The color of the icon, or <see langword="null"/> for yellow.</param>
    /// <returns>The note.</returns>
    public PdfMarkupAnnotation AddNote(PdfPoint position, string text, PdfColor? color = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var fill = color ?? PdfColor.FromRgb(1, 0.85, 0.2);
        var rectangle = PdfRectangle.FromSize(position.X, position.Y, 20, 20);
        var dictionary = CreateMarkup("Text", rectangle, text, fill);
        dictionary[PdfNames.Name] = PdfName.Create("Comment");

        // Print, NoZoom and NoRotate (12.5.3): the icon keeps its size and orientation
        dictionary[PdfNames.F] = PdfInteger.Get(4 | 8 | 16);
        var form = _page.Document.CreateForm(new PdfRectangle(0, 0, 20, 20));
        form.CreateContentBuilder()
            .SetFillColor(fill).SetStrokeGray(0.2).SetLineWidth(1).SetLineJoin(PdfLineJoin.Round)
            .MoveTo(2, 6).LineTo(2, 18).LineTo(18, 18).LineTo(18, 6).LineTo(9, 6).LineTo(5, 2).LineTo(5, 6).ClosePath().FillAndStroke()
            .MoveTo(5, 14.5).LineTo(15, 14.5).MoveTo(5, 10.5).LineTo(13, 10.5).Stroke();
        return AddMarkup(dictionary, form);
    }

    /// <summary>Adds a text markup annotation (12.5.6.10): a highlight, an underline, a squiggly underline or a strikeout of regions of the page, such as the lines of a text.</summary>
    /// <param name="kind">The kind of markup.</param>
    /// <param name="regions">The regions, in default user space: one rectangle per line of the text, from its baseline to its top.</param>
    /// <param name="color">The color, or <see langword="null"/> for yellow (a highlight) or red.</param>
    /// <param name="text">The text of the annotation (a comment), or <see langword="null"/>.</param>
    /// <returns>The annotation. Its appearance is drawn with the color; a highlight is multiplied with the content, so that the text stays readable.</returns>
    /// <exception cref="ArgumentException">There is no region.</exception>
    public PdfMarkupAnnotation AddTextMarkup(PdfTextMarkupKind kind, IEnumerable<PdfRectangle> regions, PdfColor? color = null, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var list = regions.ToList();
        if (list.Count == 0)
            throw new ArgumentException("A text markup needs a region", nameof(regions));

        var subtype = kind switch
        {
            PdfTextMarkupKind.Highlight => "Highlight",
            PdfTextMarkupKind.Underline => "Underline",
            PdfTextMarkupKind.Squiggly => "Squiggly",
            PdfTextMarkupKind.StrikeOut => "StrikeOut",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is not valid"),
        };
        var stroke = color ?? (kind == PdfTextMarkupKind.Highlight ? PdfColor.FromRgb(1, 1, 0) : PdfColor.FromRgb(0.9, 0, 0));
        var bounds = new PdfRectangle(list.Min(r => r.Left), list.Min(r => r.Bottom), list.Max(r => r.Right), list.Max(r => r.Top));
        var dictionary = CreateMarkup(subtype, bounds, text, stroke);

        // The quadrilaterals, in the order viewers expect: upper-left, upper-right, lower-left, lower-right (12.5.6.10, table 182)
        var quadPoints = new List<double>(list.Count * 8);
        foreach (var region in list)
        {
            quadPoints.AddRange([region.Left, region.Top, region.Right, region.Top, region.Left, region.Bottom, region.Right, region.Bottom]);
        }

        dictionary[PdfName.Create("QuadPoints")] = PdfArray.FromNumbers([.. quadPoints]);

        // The appearance is in the coordinates of the page: its bounding box is the rectangle of the annotation
        var form = _page.Document.CreateForm(bounds);
        var builder = form.CreateContentBuilder();
        if (kind == PdfTextMarkupKind.Highlight)
        {
            builder.SetGraphicsState(new PdfExtGState { BlendMode = PdfBlendMode.Multiply }).SetFillColor(stroke);
            foreach (var region in list)
            {
                builder.Rectangle(region);
            }

            builder.Fill();
        }
        else
        {
            builder.SetStrokeColor(stroke);
            foreach (var region in list)
            {
                var thickness = Math.Max(region.Height / 14, 0.5);
                builder.SetLineWidth(thickness);
                switch (kind)
                {
                    case PdfTextMarkupKind.Underline:
                        builder.MoveTo(region.Left, region.Bottom + thickness).LineTo(region.Right, region.Bottom + thickness);
                        break;
                    case PdfTextMarkupKind.StrikeOut:
                        builder.MoveTo(region.Left, region.Bottom + (region.Height * 0.4)).LineTo(region.Right, region.Bottom + (region.Height * 0.4));
                        break;
                    default:
                        // A wave of half a period of twice the thickness, at the bottom of the region
                        var step = Math.Max(thickness * 2, 1);
                        builder.MoveTo(region.Left, region.Bottom + thickness);
                        var up = true;
                        for (var x = region.Left + step; x < region.Right + step; x += step)
                        {
                            builder.LineTo(Math.Min(x, region.Right), region.Bottom + (up ? thickness * 2.5 : thickness));
                            up = !up;
                        }

                        break;
                }
            }

            builder.Stroke();
        }

        return AddMarkup(dictionary, form);
    }

    /// <summary>Adds a free text annotation (12.5.6.6): a text drawn on the page, in a box with a border.</summary>
    /// <param name="rectangle">The box, in default user space.</param>
    /// <param name="text">The text: it is broken into lines that fit the box.</param>
    /// <param name="font">The font, a standard font: the default appearance string of the annotation (<c>/DA</c>) names it.</param>
    /// <param name="fontSize">The font size.</param>
    /// <param name="color">The color of the text and of the border, or <see langword="null"/> for black.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentException">A character of the text has no glyph in the font.</exception>
    public PdfMarkupAnnotation AddFreeText(PdfRectangle rectangle, string text, PdfStandardFont font = PdfStandardFont.Helvetica, double fontSize = 12, PdfColor? color = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);
        var textColor = color ?? PdfColor.FromGray(0);
        var pdfFont = PdfFont.GetStandardFont(font);
        var form = _page.Document.CreateForm(new PdfRectangle(0, 0, rectangle.Width, rectangle.Height));
        form.CreateContentBuilder()
            .SetStrokeColor(textColor).SetLineWidth(1).Rectangle(0.5, 0.5, Math.Max(rectangle.Width - 1, 0), Math.Max(rectangle.Height - 1, 0)).Stroke()
            .SetFillColor(textColor)
            .DrawTextBox(text, pdfFont, fontSize, new PdfRectangle(3, 3, Math.Max(rectangle.Width - 3, 3), Math.Max(rectangle.Height - 3, 3)));

        var dictionary = CreateMarkup("FreeText", rectangle, text, color: null);
        var operators = textColor.ColorSpace switch
        {
            PdfDeviceColorSpace.Gray => "g",
            PdfDeviceColorSpace.Rgb => "rg",
            _ => "k",
        };
        var components = string.Join(' ', textColor.GetComponents().Select(value => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        dictionary[PdfName.Create("DA")] = PdfString.FromText($"/{pdfFont.PostScriptName} {fontSize.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} Tf {components} {operators}");
        dictionary[PdfName.Create("BS")] = new PdfDictionary { [PdfName.Create("W")] = PdfInteger.Get(1) };
        return AddMarkup(dictionary, form);
    }

    /// <summary>Adds a rubber stamp annotation (12.5.6.12): a word, such as <c>APPROVED</c> or <c>DRAFT</c>, in a frame with rounded corners.</summary>
    /// <param name="rectangle">The rectangle of the stamp, in default user space.</param>
    /// <param name="text">The text of the stamp: it is fitted in the rectangle.</param>
    /// <param name="color">The color of the stamp, or <see langword="null"/> for red.</param>
    /// <returns>The annotation. Its <c>/Name</c> is <c>Draft</c>, <c>Approved</c>... when the text is the name of a standard stamp.</returns>
    /// <exception cref="ArgumentException">The text is empty, or a character of the text has no glyph in Helvetica Bold.</exception>
    public PdfMarkupAnnotation AddStamp(PdfRectangle rectangle, string text, PdfColor? color = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var stampColor = color ?? PdfColor.FromRgb(0.8, 0.1, 0.1);
        var font = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
        var (width, height) = (Math.Max(rectangle.Width, 1), Math.Max(rectangle.Height, 1));
        var line = Math.Max(Math.Min(width, height) * 0.06, 0.5);
        var fontSize = Math.Min((height - (4 * line)) * 0.7, (width - (6 * line)) / Math.Max(font.GetTextWidth(text, 1), 0.001));
        fontSize = Math.Max(fontSize, 1);
        var textWidth = font.GetTextWidth(text, fontSize);
        var radius = Math.Min(width, height) * 0.2;
        var form = _page.Document.CreateForm(new PdfRectangle(0, 0, width, height));
        var (l, b, r, t) = (line, line, width - line, height - line);
        var k = radius * 0.5523;
        form.CreateContentBuilder()
            .SetStrokeColor(stampColor).SetFillColor(stampColor).SetLineWidth(line)
            .MoveTo(l + radius, b).LineTo(r - radius, b).CurveTo(r - radius + k, b, r, b + radius - k, r, b + radius)
            .LineTo(r, t - radius).CurveTo(r, t - radius + k, r - radius + k, t, r - radius, t)
            .LineTo(l + radius, t).CurveTo(l + radius - k, t, l, t - radius + k, l, t - radius)
            .LineTo(l, b + radius).CurveTo(l, b + radius - k, l + radius - k, b, l + radius, b).ClosePath().Stroke()
            .DrawText(text, font, fontSize, (width - textWidth) / 2, (height - (font.CapHeight * fontSize / 1000)) / 2);

        var dictionary = CreateMarkup("Stamp", rectangle, text, stampColor);
        var standard = new[] { "Approved", "Experimental", "NotApproved", "AsIs", "Expired", "NotForPublicRelease", "Confidential", "Final", "Sold", "Departmental", "ForComment", "TopSecret", "Draft", "ForPublicRelease" }
            .FirstOrDefault(name => string.Equals(name, text.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
        dictionary[PdfNames.Name] = PdfName.Create(standard ?? "Draft");
        return AddMarkup(dictionary, form);
    }

    /// <summary>Adds a line annotation (12.5.6.7): a straight line, with endings such as arrows.</summary>
    /// <param name="start">The start of the line, in default user space.</param>
    /// <param name="end">The end of the line, in default user space.</param>
    /// <param name="color">The color of the line, or <see langword="null"/> for red.</param>
    /// <param name="width">The width of the line, in points.</param>
    /// <param name="startEnding">The ending at <paramref name="start"/>.</param>
    /// <param name="endEnding">The ending at <paramref name="end"/>.</param>
    /// <returns>The annotation. Its closed endings are filled with the color of the line (<see cref="PdfLineAnnotation.InteriorColor"/>).</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    public PdfLineAnnotation AddLine(PdfPoint start, PdfPoint end, PdfColor? color = null, double width = 1, PdfLineEnding startEnding = PdfLineEnding.None, PdfLineEnding endEnding = PdfLineEnding.None)
    {
        CheckWidth(width);
        var stroke = color ?? DefaultShapeColor;
        var bounds = GetBounds([start, end], (width * 4) + 5);
        var dictionary = CreateShape("Line", bounds, stroke, width);
        dictionary[PdfNames.L] = PdfArray.FromNumbers(start.X, start.Y, end.X, end.Y);
        PdfLineEndingNames.Write(dictionary, startEnding, endEnding);
        if (startEnding != PdfLineEnding.None || endEnding != PdfLineEnding.None)
        {
            dictionary[AnnotationNames.IC] = PdfArray.FromNumbers(stroke.GetComponents());
        }

        return AddGenerated<PdfLineAnnotation>(dictionary);
    }

    /// <summary>Adds a square annotation (12.5.6.8): a rectangle.</summary>
    /// <param name="rectangle">The rectangle, in default user space: the border is drawn inside it.</param>
    /// <param name="color">The color of the border, or <see langword="null"/> for red.</param>
    /// <param name="interiorColor">The color that fills the rectangle, or <see langword="null"/> for none.</param>
    /// <param name="width">The width of the border, in points; 0 for none.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    public PdfSquareAnnotation AddSquare(PdfRectangle rectangle, PdfColor? color = null, PdfColor? interiorColor = null, double width = 1)
    {
        var dictionary = CreateShape("Square", rectangle, color ?? DefaultShapeColor, width);
        SetInteriorColor(dictionary, interiorColor);
        return AddGenerated<PdfSquareAnnotation>(dictionary);
    }

    /// <summary>Adds a circle annotation (12.5.6.8): an ellipse inscribed in a rectangle.</summary>
    /// <param name="rectangle">The rectangle, in default user space: the border is drawn inside it.</param>
    /// <param name="color">The color of the border, or <see langword="null"/> for red.</param>
    /// <param name="interiorColor">The color that fills the ellipse, or <see langword="null"/> for none.</param>
    /// <param name="width">The width of the border, in points; 0 for none.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    public PdfCircleAnnotation AddCircle(PdfRectangle rectangle, PdfColor? color = null, PdfColor? interiorColor = null, double width = 1)
    {
        var dictionary = CreateShape("Circle", rectangle, color ?? DefaultShapeColor, width);
        SetInteriorColor(dictionary, interiorColor);
        return AddGenerated<PdfCircleAnnotation>(dictionary);
    }

    /// <summary>Adds a polygon annotation (12.5.6.9): a closed shape of vertices.</summary>
    /// <param name="vertices">The vertices, in default user space: at least 2.</param>
    /// <param name="color">The color of the border, or <see langword="null"/> for red.</param>
    /// <param name="interiorColor">The color that fills the polygon, or <see langword="null"/> for none.</param>
    /// <param name="width">The width of the border, in points; 0 for none.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentException">There are fewer than 2 vertices, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    public PdfPolygonAnnotation AddPolygon(IEnumerable<PdfPoint> vertices, PdfColor? color = null, PdfColor? interiorColor = null, double width = 1)
    {
        var points = GetVertices(vertices);
        CheckWidth(width);
        var dictionary = CreateShape("Polygon", GetBounds(points, (width / 2) + 1), color ?? DefaultShapeColor, width);
        dictionary[AnnotationNames.Vertices] = PdfAnnotation.WritePoints(points);
        SetInteriorColor(dictionary, interiorColor);
        return AddGenerated<PdfPolygonAnnotation>(dictionary);
    }

    /// <summary>Adds a polyline annotation (12.5.6.9): connected straight lines, with endings such as arrows at the first and last vertices.</summary>
    /// <param name="vertices">The vertices, in default user space: at least 2.</param>
    /// <param name="color">The color of the lines, or <see langword="null"/> for red.</param>
    /// <param name="width">The width of the lines, in points.</param>
    /// <param name="startEnding">The ending at the first vertex.</param>
    /// <param name="endEnding">The ending at the last vertex.</param>
    /// <returns>The annotation. Its closed endings are filled with the color of the lines.</returns>
    /// <exception cref="ArgumentException">There are fewer than 2 vertices, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    public PdfPolyLineAnnotation AddPolyLine(IEnumerable<PdfPoint> vertices, PdfColor? color = null, double width = 1, PdfLineEnding startEnding = PdfLineEnding.None, PdfLineEnding endEnding = PdfLineEnding.None)
    {
        var points = GetVertices(vertices);
        CheckWidth(width);
        var stroke = color ?? DefaultShapeColor;
        var dictionary = CreateShape("PolyLine", GetBounds(points, (width * 4) + 5), stroke, width);
        dictionary[AnnotationNames.Vertices] = PdfAnnotation.WritePoints(points);
        PdfLineEndingNames.Write(dictionary, startEnding, endEnding);
        if (startEnding != PdfLineEnding.None || endEnding != PdfLineEnding.None)
        {
            dictionary[AnnotationNames.IC] = PdfArray.FromNumbers(stroke.GetComponents());
        }

        return AddGenerated<PdfPolyLineAnnotation>(dictionary);
    }

    /// <summary>Adds an ink annotation (12.5.6.13): freehand strokes, as drawn with a pen, with round caps and joins.</summary>
    /// <param name="strokes">The strokes: each one a list of points, in default user space.</param>
    /// <param name="color">The color of the strokes, or <see langword="null"/> for red.</param>
    /// <param name="width">The width of the strokes, in points.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentException">There is no stroke, a stroke is empty, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    public PdfInkAnnotation AddInk(IEnumerable<IEnumerable<PdfPoint>> strokes, PdfColor? color = null, double width = 1)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        CheckWidth(width);
        var list = strokes.Select(stroke => stroke?.ToList() ?? []).ToList();
        if (list.Count == 0 || list.Any(stroke => stroke.Count == 0))
            throw new ArgumentException("An ink annotation needs strokes of at least one point", nameof(strokes));

        var dictionary = CreateShape("Ink", GetBounds(list.SelectMany(stroke => stroke), (width / 2) + 1), color ?? DefaultShapeColor, width);
        dictionary[AnnotationNames.InkList] = new PdfArray(list.Select(stroke => (PdfObject)PdfAnnotation.WritePoints(stroke)));
        return AddGenerated<PdfInkAnnotation>(dictionary);
    }

    /// <summary>Adds a caret annotation (12.5.6.11): a symbol that marks where text is to be inserted.</summary>
    /// <param name="rectangle">The rectangle of the caret, in default user space.</param>
    /// <param name="text">The text to insert, or a comment, or <see langword="null"/>.</param>
    /// <param name="color">The color of the caret, or <see langword="null"/> for blue.</param>
    /// <returns>The annotation.</returns>
    public PdfCaretAnnotation AddCaret(PdfRectangle rectangle, string? text = null, PdfColor? color = null)
    {
        var dictionary = CreateMarkup("Caret", rectangle, text, color ?? PdfColor.FromRgb(0, 0.3, 0.9));
        dictionary[AnnotationNames.Sy] = PdfName.Create("None");
        return AddGenerated<PdfCaretAnnotation>(dictionary);
    }

    /// <summary>
    /// Adds a redaction annotation (12.5.6.23): marks regions whose content is to be removed. The content stays until the
    /// redaction is applied, by <see cref="PdfDocument.ApplyRedactions(PdfRedactionOptions?)"/> or by a viewer; until then,
    /// the regions are outlined in red.
    /// </summary>
    /// <param name="regions">The regions, in default user space.</param>
    /// <param name="overlayText">The text drawn in the regions once they are redacted, or <see langword="null"/>.</param>
    /// <param name="interiorColor">The color that fills the regions once they are redacted, or <see langword="null"/> for black.</param>
    /// <returns>The annotation.</returns>
    /// <exception cref="ArgumentException">There is no region.</exception>
    public PdfRedactAnnotation AddRedaction(IEnumerable<PdfRectangle> regions, string? overlayText = null, PdfColor? interiorColor = null)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var list = regions.ToList();
        if (list.Count == 0)
            throw new ArgumentException("A redaction needs a region", nameof(regions));

        var bounds = new PdfRectangle(list.Min(r => r.Left), list.Min(r => r.Bottom), list.Max(r => r.Right), list.Max(r => r.Top));
        var dictionary = CreateMarkup("Redact", bounds, text: null, PdfColor.FromRgb(1, 0, 0));
        dictionary[AnnotationNames.QuadPoints] = PdfAnnotation.WriteQuadrilaterals(list);
        dictionary[AnnotationNames.IC] = PdfArray.FromNumbers((interiorColor ?? PdfColor.Black).GetComponents());
        if (overlayText is not null)
        {
            dictionary[AnnotationNames.OverlayText] = PdfString.FromText(overlayText);
        }

        return AddGenerated<PdfRedactAnnotation>(dictionary);
    }

    /// <summary>Adds the pop-up annotation of a markup annotation (12.5.6.14): the window that shows its text, and sets its <see cref="PdfMarkupAnnotation.Popup"/>.</summary>
    /// <param name="parent">The markup annotation, on this page.</param>
    /// <param name="rectangle">The rectangle of the window, in default user space.</param>
    /// <param name="isOpen">Whether the window is initially open.</param>
    /// <returns>The pop-up annotation.</returns>
    /// <exception cref="ArgumentException">The annotation is not on this page.</exception>
    public PdfPopupAnnotation AddPopup(PdfMarkupAnnotation parent, PdfRectangle rectangle, bool isOpen = false)
    {
        CheckOnPage(parent);
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = AnnotationNames.PopupSubtype,
            [PdfNames.Rect] = rectangle.ToArray(),
            [PdfNames.P] = _page.Reference,
            [PdfNames.Parent] = (PdfObject?)parent.Reference ?? parent.Dictionary,
        };
        if (isOpen)
        {
            dictionary[AnnotationNames.Open] = PdfBoolean.True;
        }

        var reference = _page.Document.Objects.Add(dictionary);
        GetOrCreateArray().Add(reference);
        var popup = (PdfPopupAnnotation)PdfAnnotation.Create(_page, dictionary, reference);
        parent.Popup = popup;
        return popup;
    }

    /// <summary>
    /// Adds a reply to a markup annotation (12.5.6.2): a text annotation in reply to it (<c>/IRT</c>), at the same place.
    /// Viewers list the replies in the thread of the annotation; the reply has an empty appearance, so that it does not
    /// draw a second icon.
    /// </summary>
    /// <param name="parent">The annotation, on this page.</param>
    /// <param name="text">The text of the reply.</param>
    /// <param name="author">The author of the reply, or <see langword="null"/>.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="ArgumentException">The annotation is not on this page.</exception>
    public PdfTextAnnotation AddReply(PdfMarkupAnnotation parent, string text, string? author = null)
    {
        CheckOnPage(parent);
        ArgumentNullException.ThrowIfNull(text);
        var dictionary = CreateMarkup("Text", parent.Rectangle, text, parent.Color);
        dictionary[PdfNames.Name] = PdfName.Create("Comment");
        dictionary[AnnotationNames.IRT] = (PdfObject?)parent.Reference ?? parent.Dictionary;
        if (author is not null)
        {
            dictionary[PdfName.Create("T")] = PdfString.FromText(author);
        }

        // Print, NoZoom and NoRotate (12.5.3), as the notes
        dictionary[PdfNames.F] = PdfInteger.Get(4 | 8 | 16);
        var form = _page.Document.CreateForm(new PdfRectangle(0, 0, Math.Max(parent.Rectangle.Width, 1), Math.Max(parent.Rectangle.Height, 1)));
        form.CreateContentBuilder().Complete();
        return (PdfTextAnnotation)AddMarkup(dictionary, form);
    }

    /// <summary>
    /// Removes an annotation from the page, with the references that would keep it in a saved file: its pop-up
    /// annotation, the <c>/IRT</c> and <c>/Popup</c> entries of the other annotations of the page that point to it, the
    /// object references of the structure tree, and, for a widget, its entry in its form field.
    /// </summary>
    /// <param name="annotation">The annotation.</param>
    /// <returns><see langword="true"/> when the annotation was on the page.</returns>
    public bool Remove(PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (_page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } array)
            return false;

        for (var i = 0; i < array.Count; i++)
        {
            if ((annotation.Reference is not null && annotation.Reference.Equals(array[i])) || ReferenceEquals(PdfReference.ResolveIfReference(array[i]), annotation.Dictionary))
            {
                AnnotationRemover.Remove(_page, [(array[i], annotation.Dictionary)]);
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns an enumerator over the annotations.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<PdfAnnotation> GetEnumerator() => GetItems().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private PdfLinkAnnotation AddLink(PdfRectangle rectangle, PdfName? key, PdfObject? target)
    {
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfNames.Link,
            [PdfNames.Rect] = rectangle.ToArray(),
            [PdfNames.Border] = PdfArray.FromNumbers(0, 0, 0),
            [PdfNames.P] = _page.Reference,
            [PdfNames.F] = PdfInteger.Get(4), // Print (12.5.3)
        };
        if (key is not null)
        {
            dictionary[key] = target;
        }

        var reference = _page.Document.Objects.Add(dictionary);
        GetOrCreateArray().Add(reference);
        return new PdfLinkAnnotation(_page, dictionary, reference);
    }

    /// <summary>
    /// Creates the appearance of a file attachment (12.5.5): a sheet with a folded corner, drawn in the rectangle, which
    /// PDF/A requires (ISO 19005-2, 6.3.3) and viewers show instead of their own icon.
    /// </summary>
    private PdfReference CreateIcon(PdfRectangle rectangle)
    {
        var (width, height) = (Math.Max(rectangle.Width, 1), Math.Max(rectangle.Height, 1));
        var form = _page.Document.CreateForm(PdfRectangle.FromSize(0, 0, width, height));
        var fold = Math.Min(width, height) * 0.3;
        var line = Math.Max(Math.Min(width, height) * 0.06, 0.5);
        form.CreateContentBuilder()
            .SetFillGray(1)
            .SetStrokeGray(0.25)
            .SetLineWidth(line)
            .SetLineJoin(PdfLineJoin.Round)
            .MoveTo(line, line)
            .LineTo(width - line, line)
            .LineTo(width - line, height - fold)
            .LineTo(width - fold, height - line)
            .LineTo(line, height - line)
            .ClosePath()
            .FillAndStroke()
            .MoveTo(width - fold, height - line)
            .LineTo(width - fold, height - fold)
            .LineTo(width - line, height - fold)
            .Stroke();
        return form.Reference;
    }

    private static readonly PdfColor DefaultShapeColor = PdfColor.FromRgb(0.9, 0, 0);

    private static List<PdfPoint> GetVertices(IEnumerable<PdfPoint> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var list = vertices.ToList();
        if (list.Count < 2)
            throw new ArgumentException("A polygon or a polyline needs at least 2 vertices", nameof(vertices));

        return list;
    }

    private static void SetInteriorColor(PdfDictionary dictionary, PdfColor? color)
    {
        if (color is { } value)
        {
            dictionary[AnnotationNames.IC] = PdfArray.FromNumbers(value.GetComponents());
        }
    }

    private void CheckOnPage(PdfMarkupAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (!this.Any(item => ReferenceEquals(item.Dictionary, annotation.Dictionary)))
            throw new ArgumentException("The annotation is not on this page", nameof(annotation));
    }

    private PdfDictionary CreateMarkup(string subtype, PdfRectangle rectangle, string? text, PdfColor? color)
    {
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfName.Create(subtype),
            [PdfNames.Rect] = rectangle.ToArray(),
            [PdfNames.P] = _page.Reference,
            [PdfNames.F] = PdfInteger.Get(4), // Print (12.5.3)
        };
        if (text is not null)
        {
            dictionary[PdfNames.Contents] = PdfString.FromText(text);
        }

        if (color is { } value)
        {
            dictionary[PdfNames.C] = PdfArray.FromNumbers(value.GetComponents());
        }

        return dictionary;
    }

    private PdfMarkupAnnotation AddMarkup(PdfDictionary dictionary, PdfForm appearance)
    {
        dictionary[PdfName.Create("AP")] = new PdfDictionary { [PdfNames.N] = appearance.Reference };
        var reference = _page.Document.Objects.Add(dictionary);
        GetOrCreateArray().Add(reference);
        return (PdfMarkupAnnotation)PdfAnnotation.Create(_page, dictionary, reference);
    }

    /// <summary>Adds an annotation dictionary to the page, draws its appearance from its properties, and returns the annotation.</summary>
    private T AddGenerated<T>(PdfDictionary dictionary)
        where T : PdfAnnotation
    {
        var reference = _page.Document.Objects.Add(dictionary);
        GetOrCreateArray().Add(reference);
        var annotation = (T)PdfAnnotation.Create(_page, dictionary, reference);
        annotation.UpdateAppearance();
        return annotation;
    }

    /// <summary>Gets the rectangle of a geometry: the bounds of its points, extended by the margin its line and endings need.</summary>
    private static PdfRectangle GetBounds(IEnumerable<PdfPoint> points, double margin)
    {
        var (left, bottom, right, top) = (double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new ArgumentException("The coordinates must be finite numbers", nameof(points));

            (left, bottom, right, top) = (Math.Min(left, point.X), Math.Min(bottom, point.Y), Math.Max(right, point.X), Math.Max(top, point.Y));
        }

        return new PdfRectangle(left - margin, bottom - margin, right + margin, top + margin);
    }

    private static void CheckWidth(double width)
    {
        if (!(width >= 0 && double.IsFinite(width)))
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be a positive number");
    }

    private PdfDictionary CreateShape(string subtype, PdfRectangle rectangle, PdfColor? color, double width)
    {
        CheckWidth(width);
        var dictionary = CreateMarkup(subtype, rectangle, text: null, color);
        dictionary[AnnotationNames.BS] = new PdfDictionary { [AnnotationNames.W] = PdfObjectFactory.Number(width) };
        return dictionary;
    }

    private PdfArray GetOrCreateArray()
    {
        if (_page.Dictionary.Get<PdfArray>(PdfNames.Annots) is { } array)
            return array;

        array = new PdfArray();
        _page.Dictionary[PdfNames.Annots] = array;
        return array;
    }

    private List<PdfAnnotation> GetItems()
    {
        var array = _page.Dictionary.Get<PdfArray>(PdfNames.Annots);
        if (_items is not null && ReferenceEquals(array, _itemsArray) && (array is null || array.Version == _itemsVersion))
            return _items;

        var result = new List<PdfAnnotation>(array?.Count ?? 0);
        if (array is not null)
        {
            foreach (var item in array)
            {
                // A page of the document is not an annotation, even when its /Annots lists it: flattening or removing it
                // would remove the page (fuzz regression: a page whose /Type is /Annot listed itself)
                if (PdfReference.ResolveIfReference(item) is PdfDictionary dictionary && !ReferenceEquals(dictionary, _page.Dictionary) && !(item is PdfReference reference && _page.Document.Pages.Find(reference) is not null))
                {
                    result.Add(PdfAnnotation.Create(_page, dictionary, item as PdfReference));
                }
            }
        }

        // The list is replaced, never changed: an enumeration that removes annotations goes on over the previous one
        _items = result;
        _itemsArray = array;
        _itemsVersion = array?.Version ?? 0;
        return result;
    }
}
