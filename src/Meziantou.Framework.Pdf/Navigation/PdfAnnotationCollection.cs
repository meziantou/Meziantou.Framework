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

    /// <summary>Removes an annotation from the page.</summary>
    /// <param name="annotation">The annotation.</param>
    /// <returns><see langword="true"/> when the annotation was on the page.</returns>
    public bool Remove(PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (_page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } array)
            return false;

        for (var i = 0; i < array.Count; i++)
        {
            if ((annotation.Reference is not null && annotation.Reference.Equals(array[i])) || ReferenceEquals(array[i], annotation.Dictionary))
            {
                array.RemoveAt(i);
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
        return new PdfMarkupAnnotation(_page, dictionary, reference);
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
                if (PdfReference.ResolveIfReference(item) is PdfDictionary dictionary)
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
