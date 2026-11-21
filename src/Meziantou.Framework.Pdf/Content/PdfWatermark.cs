using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The text of a watermark, drawn in a form at a font size of 100, and how to paint it on a page.</summary>
internal sealed class PdfWatermark
{
    private const double ReferenceSize = 100;

    // The tiling patterns of a tiled watermark, by pattern matrix and steps: the pages of the same size and rotation share one
    private readonly Dictionary<(PdfMatrix Matrix, double XStep, double YStep), PdfTilingPattern> _patterns = [];

    private PdfWatermark(PdfForm form, double width, double height, double middle, double descent)
    {
        Form = form;
        Width = width;
        Height = height;
        Middle = middle;
        Descent = descent;
    }

    public PdfForm Form { get; }

    // The width and the height of the text at a font size of 1, the height of the middle of its capital letters, and the
    // descent of the font (negative), from the baseline
    public double Width { get; }

    public double Height { get; }

    public double Middle { get; }

    public double Descent { get; }

    public static PdfWatermark Create(PdfDocument document, string text, PdfWatermarkOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (!(options.Opacity is >= 0 and <= 1))
            throw new ArgumentOutOfRangeException(nameof(options), options.Opacity, "The opacity must be between 0 and 1");

        if (options.FontSize is { } fontSize && !(fontSize > 0 && double.IsFinite(fontSize)))
            throw new ArgumentOutOfRangeException(nameof(options), fontSize, "The font size must be a positive number");

        if (options.Rotation is { } rotation && !double.IsFinite(rotation))
            throw new ArgumentOutOfRangeException(nameof(options), rotation, "The rotation must be a finite number");

        var font = options.Font ?? PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
        var width = font.GetTextWidth(text, 1);
        var ascent = Math.Max(font.Ascent, 0) / 1000;
        var descent = Math.Min(font.Descent, 0) / 1000;
        var middle = (font.CapHeight > 0 ? font.CapHeight : font.Ascent + font.Descent) / 2000;
        var form = document.CreateForm(new PdfRectangle(0, descent * ReferenceSize, Math.Max(width, 0.001) * ReferenceSize, Math.Max(ascent, 0.001) * ReferenceSize));
        var builder = form.CreateContentBuilder();
        if (options.Opacity < 1)
        {
            builder.SetGraphicsState(new PdfExtGState { FillAlpha = options.Opacity });
        }

        builder.SetFillColor(options.Color)
            .BeginText()
            .SetFont(font, ReferenceSize)
            .ShowText(text)
            .EndText()
            .Complete();
        return new PdfWatermark(form, width, ascent - descent, middle, descent);
    }

    public void Draw(PdfPage page, PdfWatermarkOptions options)
    {
        // A page whose visible box is empty, as in a damaged file, shows nothing: it gets no watermark
        var box = page.VisibleBox;
        if (!(box.Width > 0 && box.Height > 0 && double.IsFinite(box.Width) && double.IsFinite(box.Height)))
            return;

        if (options.IsTiled)
        {
            DrawTiled(page, options, box);
        }
        else if (options.Position is PdfWatermarkPosition.Center)
        {
            DrawCentered(page, options);
        }
        else
        {
            DrawAnchored(page, options);
        }
    }

    private void DrawCentered(PdfPage page, PdfWatermarkOptions options)
    {
        var size = page.DisplaySize;
        var angle = options.Rotation ?? GetDiagonalAngle(size);
        var radians = angle * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));

        // The font size whose rotated box fits in 80% of the page
        var fontSize = options.FontSize ?? (0.8 * Math.Min(size.Width / ((cos * Width) + (sin * Height)), size.Height / ((sin * Width) + (cos * Height))));
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            return;

        var scale = fontSize / ReferenceSize;
        var matrix = PdfMatrix.CreateTranslation(-Width * ReferenceSize / 2, -Middle * ReferenceSize)
            * PdfMatrix.CreateScale(scale, scale)
            * PdfMatrix.CreateRotation(angle)
            * PdfMatrix.CreateTranslation(size.Width / 2, size.Height / 2)
            * page.DisplayToUserSpace;
        DrawForm(page, options, matrix);
    }

    /// <summary>Paints the text once, its rotated box at the margin of an edge or a corner of the page as it is displayed.</summary>
    private void DrawAnchored(PdfPage page, PdfWatermarkOptions options)
    {
        var size = page.DisplaySize;
        var angle = options.Rotation ?? 0;
        var fontSize = options.FontSize ?? GetDefaultFontSize(size);
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            return;

        // The half extents of the box of the text, rotated, in the display space
        var radians = angle * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var width = Width * fontSize;
        var height = Height * fontSize;
        var halfWidth = ((cos * width) + (sin * height)) / 2;
        var halfHeight = ((sin * width) + (cos * height)) / 2;

        // The center of the rotated box: on the left, the box starts at the margin; on the right, it ends at the margin
        var margin = options.Margin;
        var x = options.Position switch
        {
            PdfWatermarkPosition.TopLeft or PdfWatermarkPosition.Left or PdfWatermarkPosition.BottomLeft => margin + halfWidth,
            PdfWatermarkPosition.TopRight or PdfWatermarkPosition.Right or PdfWatermarkPosition.BottomRight => size.Width - margin - halfWidth,
            _ => size.Width / 2,
        };
        var y = options.Position switch
        {
            PdfWatermarkPosition.TopLeft or PdfWatermarkPosition.Top or PdfWatermarkPosition.TopRight => size.Height - margin - halfHeight,
            PdfWatermarkPosition.BottomLeft or PdfWatermarkPosition.Bottom or PdfWatermarkPosition.BottomRight => margin + halfHeight,
            _ => size.Height / 2,
        };

        // The center of the box of the text, from the descent to the ascent, at the origin, then rotated around it
        var scale = fontSize / ReferenceSize;
        var matrix = PdfMatrix.CreateTranslation(-Width * ReferenceSize / 2, -(Descent + (Height / 2)) * ReferenceSize)
            * PdfMatrix.CreateScale(scale, scale)
            * PdfMatrix.CreateRotation(angle)
            * PdfMatrix.CreateTranslation(x, y)
            * page.DisplayToUserSpace;
        DrawForm(page, options, matrix);
    }

    /// <summary>
    /// Fills the visible box of the page with a colored tiling pattern (8.7.3) whose cell paints the form twice: a row of
    /// copies, and the next row offset by half a copy.
    /// </summary>
    private void DrawTiled(PdfPage page, PdfWatermarkOptions options, PdfRectangle box)
    {
        var size = page.DisplaySize;
        var angle = options.Rotation ?? GetDiagonalAngle(size);
        var fontSize = options.FontSize ?? GetDefaultFontSize(size);
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            return;

        // The pattern space is the space of the form: the text at the reference size, scaled by the matrix
        var scale = fontSize / ReferenceSize;
        var textWidth = Width * ReferenceSize;
        var textHeight = Height * ReferenceSize;
        var spacing = options.TileSpacing is { } value ? value / scale : textHeight;
        var xStep = textWidth + spacing;
        var rowHeight = textHeight + spacing;
        var yStep = 2 * rowHeight;

        // The steps are written with six decimals: a step written as 0 is not a pattern
        if (!(double.IsFinite(xStep) && double.IsFinite(yStep) && xStep >= 0.000001 && yStep >= 0.000001))
            return;

        // The pattern matrix maps the pattern space to the default coordinate space of the page (8.7.3.1), whatever the
        // transformation matrix when the pattern is painted: a copy of the text is centered on the page as it is
        // displayed, and the rows follow the angle of the text
        var matrix = PdfMatrix.CreateTranslation(-textWidth / 2, -textHeight / 2)
            * PdfMatrix.CreateScale(scale, scale)
            * PdfMatrix.CreateRotation(angle)
            * PdfMatrix.CreateTranslation(size.Width / 2, size.Height / 2)
            * page.DisplayToUserSpace;
        if (!_patterns.TryGetValue((matrix, xStep, yStep), out var pattern))
        {
            // The cell covers both copies: it is wider than the step, and the copy of the second row that overlaps the
            // next cell is painted by this one (8.7.3.1 lets the bounding box differ from the steps)
            var formBox = Form.BoundingBox;
            var cell = new PdfRectangle(0, 0, (xStep / 2) + formBox.Width, rowHeight + formBox.Height);
            pattern = page.Document.CreateTilingPattern(cell, xStep, yStep, isColored: true, matrix);

            // A form is painted in a graphics state of its own (8.10.1): the translations add up
            pattern.CreateContentBuilder()
                .Transform(PdfMatrix.CreateTranslation(0, -formBox.Bottom))
                .DrawForm(Form)
                .Transform(PdfMatrix.CreateTranslation(xStep / 2, rowHeight))
                .DrawForm(Form)
                .Complete();
            _patterns.Add((matrix, xStep, yStep), pattern);
        }

        page.CreateContentBuilder(options.Placement)
            .BeginMarkedContent("Artifact", CreateArtifactProperties())
            .SaveGraphicsState()
            .SetFillPattern(pattern)
            .Rectangle(box)
            .Fill()
            .RestoreGraphicsState()
            .EndMarkedContent()
            .Complete();
    }

    /// <summary>Paints the form with a matrix, as a pagination artifact (14.8.2.2).</summary>
    private void DrawForm(PdfPage page, PdfWatermarkOptions options, PdfMatrix matrix)
    {
        page.CreateContentBuilder(options.Placement)
            .BeginMarkedContent("Artifact", CreateArtifactProperties())
            .SaveGraphicsState()
            .Transform(matrix)
            .DrawForm(Form)
            .RestoreGraphicsState()
            .EndMarkedContent()
            .Complete();
    }

    private static PdfDictionary CreateArtifactProperties() => new()
    {
        [PdfNames.Type] = PdfName.Create("Pagination"),
        [PdfNames.Subtype] = PdfName.Create("Watermark"),
    };

    private static double GetDiagonalAngle(PdfSize size) => Math.Atan2(size.Height, size.Width) * 180 / Math.PI;

    // 1/20 of the diagonal of the page: the font size of the copies that are not fitted to the page
    private static double GetDefaultFontSize(PdfSize size) => Math.Sqrt((size.Width * size.Width) + (size.Height * size.Height)) / 20;
}
