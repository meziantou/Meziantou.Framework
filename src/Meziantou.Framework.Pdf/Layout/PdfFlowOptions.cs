namespace Meziantou.Framework.Pdf;

/// <summary>The options of a flow document (<see cref="PdfFlowDocument"/>): its pages, its default text style, and the metadata of the document it builds.</summary>
public sealed class PdfFlowOptions
{
    private double _fontSize = 11;
    private double _lineHeight = 1.25;

    /// <summary>Gets the size of the pages. The default is A4.</summary>
    public PdfSize PageSize { get; init; } = PdfSize.A4;

    /// <summary>Gets the left margin, in points. The default is 72 (one inch).</summary>
    public double MarginLeft { get; init; } = 72;

    /// <summary>Gets the right margin, in points. The default is 72.</summary>
    public double MarginRight { get; init; } = 72;

    /// <summary>Gets the top margin, in points. The default is 72.</summary>
    public double MarginTop { get; init; } = 72;

    /// <summary>Gets the bottom margin, in points. The default is 72.</summary>
    public double MarginBottom { get; init; } = 72;

    /// <summary>Gets the font of the text. The default is Helvetica, a standard font that is not embedded: embed a font for PDF/UA and PDF/A, and for the characters beyond <c>WinAnsiEncoding</c>.</summary>
    public PdfFont Font { get; init; } = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

    /// <summary>Gets the font of the headings, or <see langword="null"/> for Helvetica Bold when <see cref="Font"/> is Helvetica, and <see cref="Font"/> otherwise.</summary>
    public PdfFont? HeadingFont { get; init; }

    /// <summary>Gets the font size of the text, in points. The default is 11.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double FontSize
    {
        get => _fontSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _fontSize = value;
        }
    }

    /// <summary>Gets the height of the lines, as a multiple of the font size. The default is 1.25.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double LineHeight
    {
        get => _lineHeight;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _lineHeight = value;
        }
    }

    /// <summary>Gets the color of the text. The default is black.</summary>
    public PdfColor TextColor { get; init; } = PdfColor.Black;

    /// <summary>Gets the space after a paragraph, a list, an image or a table, in points. The default is 8.</summary>
    public double ParagraphSpacing { get; init; } = 8;

    /// <summary>Gets the title of the document (<see cref="PdfDocumentInformation.Title"/>), which PDF/UA requires.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the natural language of the document (<see cref="PdfDocument.Language"/>), such as <c>en-US</c>, which PDF/UA requires.</summary>
    public string? Language { get; init; }

    /// <summary>Gets a value indicating whether the document is tagged (14.8): its content is in a structure tree of headings, paragraphs, lists, tables and figures, and the decorations are artifacts. The default is <see langword="true"/>.</summary>
    public bool Tagged { get; init; } = true;

    /// <summary>Gets a value indicating whether the headings are added to the outline (the bookmarks). The default is <see langword="true"/>.</summary>
    public bool CreateOutline { get; init; } = true;

    /// <summary>Gets the function that gives the text of the header of a page from its number and the number of pages, or <see langword="null"/> for none. The header is drawn centered in the top margin, as an artifact.</summary>
    public Func<int, int, string>? Header { get; init; }

    /// <summary>Gets the function that gives the text of the footer of a page from its number and the number of pages, such as <c>(page, count) =&gt; $"{page} / {count}"</c>, or <see langword="null"/> for none. The footer is drawn centered in the bottom margin, as an artifact.</summary>
    public Func<int, int, string>? Footer { get; init; }
}
