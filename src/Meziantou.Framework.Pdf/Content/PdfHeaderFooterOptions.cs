namespace Meziantou.Framework.Pdf;

/// <summary>
/// The headers and footers <see cref="PdfDocument.AddHeaderFooter(PdfHeaderFooterOptions)"/> stamps on the pages: up to
/// three texts at the top of each page and three at the bottom, on the left, in the center and on the right.
/// </summary>
/// <remarks>
/// <para>
/// A text is a template whose tokens are replaced for each page: <c>{page}</c> (the number of the page, from
/// <see cref="StartPageNumber"/>), <c>{pages}</c> (the number of pages, or <see cref="PageCount"/>), <c>{label}</c> (the
/// page label, or the number of the page when the document has none), <c>{date}</c> (<see cref="Date"/> in
/// <see cref="DateFormat"/>) and <c>{bates}</c> (the Bates number of the page, <see cref="Bates"/>). <c>{{</c> and
/// <c>}}</c> are braces.
/// </para>
/// <para>The date is never the current date implicitly: the output of the library depends only on the document and the options.</para>
/// </remarks>
public sealed class PdfHeaderFooterOptions
{
    /// <summary>Gets the text on the left of the header, or <see langword="null"/> for none.</summary>
    public string? HeaderLeft { get; init; }

    /// <summary>Gets the text in the center of the header, or <see langword="null"/> for none.</summary>
    public string? HeaderCenter { get; init; }

    /// <summary>Gets the text on the right of the header, or <see langword="null"/> for none.</summary>
    public string? HeaderRight { get; init; }

    /// <summary>Gets the text on the left of the footer, or <see langword="null"/> for none.</summary>
    public string? FooterLeft { get; init; }

    /// <summary>Gets the text in the center of the footer, or <see langword="null"/> for none.</summary>
    public string? FooterCenter { get; init; }

    /// <summary>Gets the text on the right of the footer, or <see langword="null"/> for none.</summary>
    public string? FooterRight { get; init; }

    /// <summary>Gets the font, or <see langword="null"/> for Helvetica. The standard 14 fonts only show the characters of <c>WinAnsiEncoding</c>.</summary>
    public PdfFont? Font { get; init; }

    /// <summary>Gets the font size, in points. The default is 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number.</exception>
    public double FontSize
    {
        get;
        init
        {
            if (!(value > 0 && double.IsFinite(value)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The font size must be a positive number");

            field = value;
        }
    } = 10;

    /// <summary>Gets the color of the text. The default is black.</summary>
    public PdfColor Color { get; init; } = PdfColor.Black;

    /// <summary>Gets the distance, in points, between the texts and the edges of the page as it is displayed. The default is 36 (half an inch).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double Margin
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 36;

    /// <summary>Gets the number of the first page, for <c>{page}</c>. The default is 1.</summary>
    public int StartPageNumber { get; init; } = 1;

    /// <summary>Gets the number of pages <c>{pages}</c> gives, or <see langword="null"/> (the default) for the number of pages of the document.</summary>
    public int? PageCount { get; init; }

    /// <summary>Gets the date <c>{date}</c> gives, or <see langword="null"/> when the texts have no date.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Gets the format of <c>{date}</c>, with the invariant culture. The default is <c>yyyy-MM-dd</c>.</summary>
    public string DateFormat { get; init; } = "yyyy-MM-dd";

    /// <summary>Gets the Bates numbering <c>{bates}</c> gives, or <see langword="null"/> when the texts have no Bates number.</summary>
    public PdfBatesNumbering? Bates { get; init; }

    /// <summary>Gets the index of the first page stamped. The default is 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int FirstPageIndex
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets the index of the last page stamped, or <see langword="null"/> (the default) for the last page of the document.</summary>
    public int? LastPageIndex { get; init; }

    /// <summary>Gets where the texts are painted: over the content of the page (the default) or under it.</summary>
    public PdfContentPlacement Placement { get; init; }

    internal PdfHeaderFooterOptions WithBates(PdfBatesNumbering bates) => new()
    {
        HeaderLeft = HeaderLeft,
        HeaderCenter = HeaderCenter,
        HeaderRight = HeaderRight,
        FooterLeft = FooterLeft,
        FooterCenter = FooterCenter,
        FooterRight = FooterRight,
        Font = Font,
        FontSize = FontSize,
        Color = Color,
        Margin = Margin,
        StartPageNumber = StartPageNumber,
        PageCount = PageCount,
        Date = Date,
        DateFormat = DateFormat,
        Bates = bates,
        FirstPageIndex = FirstPageIndex,
        LastPageIndex = LastPageIndex,
        Placement = Placement,
    };
}
