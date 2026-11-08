using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The widget of a visible signature (ISO 32000-2, 12.7.5.5 and 12.5.5): the page and the rectangle it is shown in, and
/// what it shows: the drawing of the caller, or the default layout of the library.
/// </summary>
/// <remarks>
/// The default layout shows the name of the signer (<see cref="PdfSignatureOptions.SignerName"/>, or the name of the
/// certificate), the time of the signature, the reason and the location, with <see cref="Image"/> at their left (or above
/// them, in a rectangle taller than wide).
/// </remarks>
public sealed class PdfSignatureAppearance
{
    private const double MaxAutomaticFontSize = 10;
    private const double MinAutomaticFontSize = 1;
    private const double LineHeight = 1.2;

    /// <summary>Initializes a new instance of the <see cref="PdfSignatureAppearance"/> class.</summary>
    /// <param name="pageIndex">The index of the page, from 0.</param>
    /// <param name="rectangle">The rectangle of the widget, in the default user space of the page.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative, or the rectangle is empty.</exception>
    public PdfSignatureAppearance(int pageIndex, PdfRectangle rectangle)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (!(rectangle.Width > 0 && rectangle.Height > 0))
            throw new ArgumentOutOfRangeException(nameof(rectangle), rectangle, "The rectangle of a visible signature is empty");

        PageIndex = pageIndex;
        Rectangle = rectangle;
    }

    /// <summary>Gets the index of the page, from 0.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the rectangle of the widget, in the default user space of the page.</summary>
    public PdfRectangle Rectangle { get; }

    /// <summary>
    /// Gets the drawing of the appearance stream, a form whose coordinates go from (0, 0) to the width and the height of
    /// <see cref="Rectangle"/>; <see langword="null"/> (the default) for the default layout. A drawing that draws nothing
    /// gives an empty appearance.
    /// </summary>
    public Action<PdfContentBuilder>? Draw { get; init; }

    /// <summary>Gets the image of the default layout, such as a handwritten signature or a logo, scaled to fit its part of the rectangle; <see langword="null"/> (the default) for text only.</summary>
    public PdfImage? Image { get; init; }

    /// <summary>
    /// Gets the font of the text of the default layout. The default is Helvetica, which shows the characters of
    /// <c>WinAnsiEncoding</c>: use an embedded font for the other characters.
    /// </summary>
    public PdfFont? Font { get; init; }

    /// <summary>Gets the font size of the text of the default layout; 0 (the default) for the largest size, up to 10, at which the text fits in the rectangle.</summary>
    public double FontSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The font size is not a finite number");

            field = value;
        }
    }

    /// <summary>Gets the drawing of the appearance: <see cref="Draw"/>, or the default layout for the information of the signature.</summary>
    /// <exception cref="ArgumentException">The font of the default layout cannot show the text.</exception>
    internal Action<PdfContentBuilder> GetDrawing(PdfSignatureOptions options, DateTimeOffset signingTime, string parameterName)
    {
        if (Draw is not null)
            return Draw;

        var text = GetDefaultText(options, signingTime);
        var font = Font ?? PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        foreach (var line in text.Split('\n'))
        {
            if (!font.CanEncode(line))
                throw new ArgumentException($"The font {font.PostScriptName} of the appearance of the signature cannot show the text '{line}': set {nameof(PdfSignatureAppearance)}.{nameof(Font)} to a font that has its characters", parameterName);
        }

        return builder => DrawDefaultLayout(builder, text, font);
    }

    /// <summary>The lines of the default layout, separated by line feeds.</summary>
    private static string GetDefaultText(PdfSignatureOptions options, DateTimeOffset signingTime)
    {
        var text = new StringBuilder();
        var name = options.SignerName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = options.Certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            text.Append("Digitally signed by ").Append(name).Append('\n');
        }

        // The time the signer claims (/M), in the offset it was given in: the same value on every machine
        text.Append("Date: ").Append(signingTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(options.Reason))
        {
            text.Append("\nReason: ").Append(options.Reason);
        }

        if (!string.IsNullOrWhiteSpace(options.Location))
        {
            text.Append("\nLocation: ").Append(options.Location);
        }

        return text.ToString();
    }

    private void DrawDefaultLayout(PdfContentBuilder builder, string text, PdfFont font)
    {
        var width = Rectangle.Width;
        var height = Rectangle.Height;
        var padding = Math.Min(4, Math.Min(width, height) / 10);
        var content = new PdfRectangle(padding, padding, width - padding, height - padding);
        var textBox = content;

        if (Image is { } image)
        {
            // The image takes 40% of the long side of the rectangle: the left part, or the top part when it is taller than wide
            PdfRectangle imageBox;
            if (width >= height)
            {
                var split = content.Left + (content.Width * 0.4);
                imageBox = new PdfRectangle(content.Left, content.Bottom, split, content.Top);
                textBox = new PdfRectangle(Math.Min(split + padding, content.Right), content.Bottom, content.Right, content.Top);
            }
            else
            {
                var split = content.Top - (content.Height * 0.4);
                imageBox = new PdfRectangle(content.Left, split, content.Right, content.Top);
                textBox = new PdfRectangle(content.Left, content.Bottom, content.Right, Math.Max(split - padding, content.Bottom));
            }

            // Scaled to fit, keeping its proportions, and centered
            var scale = Math.Min(imageBox.Width / image.Width, imageBox.Height / image.Height);
            var imageWidth = image.Width * scale;
            var imageHeight = image.Height * scale;
            if (imageWidth > 0 && imageHeight > 0)
            {
                builder.DrawImage(image, imageBox.Left + ((imageBox.Width - imageWidth) / 2), imageBox.Bottom + ((imageBox.Height - imageHeight) / 2), imageWidth, imageHeight);
            }
        }

        if (textBox.Width <= 0 || textBox.Height <= 0)
            return;

        var fontSize = FontSize > 0 ? FontSize : GetFittingFontSize(text, font, textBox);

        // Centered vertically: the box starts at the top of the first line and ends at the bottom of the last one
        var lineCount = PdfTextWrapper.Wrap(text, font, fontSize, textBox.Width).Count;
        var textHeight = GetTextHeight(font, fontSize, lineCount);
        if (textHeight < textBox.Height)
        {
            var top = textBox.Top - ((textBox.Height - textHeight) / 2);
            textBox = new PdfRectangle(textBox.Left, textBox.Bottom, textBox.Right, top);
        }

        builder.DrawTextBox(text, font, fontSize, textBox, new PdfTextBoxOptions { LineHeight = LineHeight });
    }

    /// <summary>The largest font size, by steps of 0.5 from 10 down to 1, at which every line of the text fits in the box.</summary>
    private static double GetFittingFontSize(string text, PdfFont font, PdfRectangle box)
    {
        for (var fontSize = MaxAutomaticFontSize; fontSize > MinAutomaticFontSize; fontSize -= 0.5)
        {
            var lineCount = PdfTextWrapper.Wrap(text, font, fontSize, box.Width).Count;
            if (GetTextHeight(font, fontSize, lineCount) <= box.Height)
                return fontSize;
        }

        return MinAutomaticFontSize;
    }

    /// <summary>The height of lines of text, from the ascent of the first line to the descent of the last one.</summary>
    private static double GetTextHeight(PdfFont font, double fontSize, int lineCount) => ((font.Ascent - font.Descent) * fontSize / 1000) + ((lineCount - 1) * LineHeight * fontSize);
}
