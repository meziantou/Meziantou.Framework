using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The text of a watermark, drawn in a form at a font size of 100, and how to paint it on a page.</summary>
internal sealed class PdfWatermark
{
    private const double ReferenceSize = 100;

    private PdfWatermark(PdfForm form, double width, double height, double middle)
    {
        Form = form;
        Width = width;
        Height = height;
        Middle = middle;
    }

    public PdfForm Form { get; }

    // The width and the height of the text at a font size of 1, and the height of the middle of its capital letters
    public double Width { get; }

    public double Height { get; }

    public double Middle { get; }

    public static PdfWatermark Create(PdfDocument document, string text, PdfWatermarkOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (!(options.Opacity is >= 0 and <= 1))
            throw new ArgumentOutOfRangeException(nameof(options), options.Opacity, "The opacity must be between 0 and 1");

        if (options.FontSize is { } fontSize && !(fontSize > 0 && double.IsFinite(fontSize)))
            throw new ArgumentOutOfRangeException(nameof(options), fontSize, "The font size must be a positive number");

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
        return new PdfWatermark(form, width, ascent - descent, middle);
    }

    public void Draw(PdfPage page, PdfWatermarkOptions options)
    {
        // A page whose visible box is empty, as in a damaged file, shows nothing: it gets no watermark
        var box = page.VisibleBox;
        if (!(box.Width > 0 && box.Height > 0 && double.IsFinite(box.Width) && double.IsFinite(box.Height)))
            return;

        var size = page.DisplaySize;
        var angle = options.Rotation ?? (Math.Atan2(size.Height, size.Width) * 180 / Math.PI);
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

        var properties = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("Pagination"),
            [PdfNames.Subtype] = PdfName.Create("Watermark"),
        };
        page.CreateContentBuilder(options.Placement)
            .BeginMarkedContent("Artifact", properties)
            .SaveGraphicsState()
            .Transform(matrix)
            .DrawForm(Form)
            .RestoreGraphicsState()
            .EndMarkedContent()
            .Complete();
    }
}
