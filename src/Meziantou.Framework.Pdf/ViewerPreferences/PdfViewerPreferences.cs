using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The viewer preferences of a document (<c>/ViewerPreferences</c>, ISO 32000-2, 12.2): how viewers show the document
/// and the settings of their print dialog. The dictionary is created when a value is set, and removed when its last
/// entry is.
/// </summary>
public sealed class PdfViewerPreferences
{
    private static readonly PdfName ViewerPreferencesName = PdfName.Create("ViewerPreferences");
    private static readonly PdfName NonFullScreenPageModeName = PdfName.Create("NonFullScreenPageMode");
    private static readonly PdfName DirectionName = PdfName.Create("Direction");
    private static readonly PdfName PrintScalingName = PdfName.Create("PrintScaling");
    private static readonly PdfName DuplexName = PdfName.Create("Duplex");
    private static readonly PdfName PickTrayName = PdfName.Create("PickTrayByPDFSize");
    private static readonly PdfName PrintPageRangeName = PdfName.Create("PrintPageRange");
    private static readonly PdfName NumCopiesName = PdfName.Create("NumCopies");
    private static readonly PdfName EnforceName = PdfName.Create("Enforce");

    private readonly PdfDocument _document;

    internal PdfViewerPreferences(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>Gets the viewer preferences dictionary of the catalog, or <see langword="null"/> when the document has none.</summary>
    public PdfDictionary? Dictionary => _document.Catalog.Get<PdfDictionary>(ViewerPreferencesName);

    /// <summary>Gets or sets a value indicating whether viewers hide their toolbars (<c>/HideToolbar</c>).</summary>
    public bool HideToolbar
    {
        get => GetFlag("HideToolbar");
        set => SetFlag("HideToolbar", value);
    }

    /// <summary>Gets or sets a value indicating whether viewers hide their menu bar (<c>/HideMenubar</c>).</summary>
    public bool HideMenubar
    {
        get => GetFlag("HideMenubar");
        set => SetFlag("HideMenubar", value);
    }

    /// <summary>Gets or sets a value indicating whether viewers hide the elements of their window, such as the scroll bars, to show only the pages (<c>/HideWindowUI</c>).</summary>
    public bool HideWindowUI
    {
        get => GetFlag("HideWindowUI");
        set => SetFlag("HideWindowUI", value);
    }

    /// <summary>Gets or sets a value indicating whether viewers resize their window to the first page (<c>/FitWindow</c>).</summary>
    public bool FitWindow
    {
        get => GetFlag("FitWindow");
        set => SetFlag("FitWindow", value);
    }

    /// <summary>Gets or sets a value indicating whether viewers center their window on the screen (<c>/CenterWindow</c>).</summary>
    public bool CenterWindow
    {
        get => GetFlag("CenterWindow");
        set => SetFlag("CenterWindow", value);
    }

    /// <summary>Gets or sets a value indicating whether viewers show the title of the document in their title bar instead of the name of the file (<c>/DisplayDocTitle</c>), which PDF/UA requires.</summary>
    public bool DisplayDocumentTitle
    {
        get => GetFlag("DisplayDocTitle");
        set => SetFlag("DisplayDocTitle", value);
    }

    /// <summary>Gets or sets what viewers show when they leave full-screen mode (<c>/NonFullScreenPageMode</c>), when <see cref="PdfDocument.PageMode"/> is <see cref="PdfPageMode.FullScreen"/>. The default is <see cref="PdfPageMode.UseNone"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is <see cref="PdfPageMode.FullScreen"/> or <see cref="PdfPageMode.UseAttachments"/>.</exception>
    public PdfPageMode NonFullScreenPageMode
    {
        get => Dictionary?.GetName(NonFullScreenPageModeName)?.Value switch
        {
            "UseOutlines" => PdfPageMode.UseOutlines,
            "UseThumbs" => PdfPageMode.UseThumbs,
            "UseOC" => PdfPageMode.UseOptionalContent,
            _ => PdfPageMode.UseNone,
        };
        set => Set(NonFullScreenPageModeName, value switch
        {
            PdfPageMode.UseNone => null,
            PdfPageMode.UseOutlines or PdfPageMode.UseThumbs or PdfPageMode.UseOptionalContent => PdfDocument.GetPageModeName(value),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The mode must be UseNone, UseOutlines, UseThumbs or UseOptionalContent"),
        });
    }

    /// <summary>Gets or sets the reading order of the text (<c>/Direction</c>), which sets how viewers lay out pages side by side.</summary>
    public PdfReadingDirection Direction
    {
        get => Dictionary?.GetName(DirectionName)?.Value == "R2L" ? PdfReadingDirection.RightToLeft : PdfReadingDirection.LeftToRight;
        set => Set(DirectionName, value switch
        {
            PdfReadingDirection.LeftToRight => null,
            PdfReadingDirection.RightToLeft => PdfName.Create("R2L"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The direction is not valid"),
        });
    }

    /// <summary>Gets or sets a value indicating whether the print dialog prints the pages at their size, without scaling them to the paper (<c>/PrintScaling /None</c>).</summary>
    public bool DisablePrintScaling
    {
        get => Dictionary?.GetName(PrintScalingName)?.Value == "None";
        set => Set(PrintScalingName, value ? PdfName.Create("None") : null);
    }

    /// <summary>Gets or sets how the print dialog prints the pages on the sides of the sheets (<c>/Duplex</c>).</summary>
    public PdfDuplexMode Duplex
    {
        get => Dictionary?.GetName(DuplexName)?.Value switch
        {
            "Simplex" => PdfDuplexMode.Simplex,
            "DuplexFlipShortEdge" => PdfDuplexMode.DuplexFlipShortEdge,
            "DuplexFlipLongEdge" => PdfDuplexMode.DuplexFlipLongEdge,
            _ => PdfDuplexMode.Unspecified,
        };
        set => Set(DuplexName, value switch
        {
            PdfDuplexMode.Unspecified => null,
            PdfDuplexMode.Simplex => PdfName.Create("Simplex"),
            PdfDuplexMode.DuplexFlipShortEdge => PdfName.Create("DuplexFlipShortEdge"),
            PdfDuplexMode.DuplexFlipLongEdge => PdfName.Create("DuplexFlipLongEdge"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The duplex mode is not valid"),
        });
    }

    /// <summary>Gets or sets whether the print dialog picks the paper tray from the size of the pages (<c>/PickTrayByPDFSize</c>), or <see langword="null"/> when the document does not say.</summary>
    public bool? PickTrayByPdfSize
    {
        get => Dictionary?.Get<PdfBoolean>(PickTrayName)?.Value;
        set => Set(PickTrayName, value is { } flag ? PdfBoolean.Get(flag) : null);
    }

    /// <summary>
    /// Gets or sets the pages the print dialog selects (<c>/PrintPageRange</c>): ranges of zero-based page indexes, both
    /// ends included; an empty list for all the pages.
    /// </summary>
    /// <exception cref="ArgumentException">A range is negative or reversed.</exception>
    public IReadOnlyList<(int First, int Last)> PrintPageRanges
    {
        get
        {
            var result = new List<(int, int)>();
            if (Dictionary?.Get<PdfArray>(PrintPageRangeName) is { } array)
            {
                // The ranges are written with 1-based page numbers (12.2, table 147)
                for (var i = 0; i + 1 < array.Count && result.Count < 4096; i += 2)
                {
                    var (first, last) = (array.GetNumber(i), array.GetNumber(i + 1));
                    if (first >= 1 && last >= first && last <= int.MaxValue)
                    {
                        result.Add(((int)first - 1, (int)last - 1));
                    }
                }
            }

            return result;
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(range => range.First < 0 || range.Last < range.First))
                throw new ArgumentException("The ranges must be positive and in order", nameof(value));

            Set(PrintPageRangeName, value.Count == 0 ? null : PdfArray.FromNumbers([.. value.SelectMany(range => new double[] { range.First + 1, range.Last + 1 })]));
        }
    }

    /// <summary>Gets or sets the number of copies the print dialog selects (<c>/NumCopies</c>), from 1 to 5; <see langword="null"/> to let the print dialog choose.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 5.</exception>
    public int? NumberOfCopies
    {
        get => Dictionary?.GetResolved(NumCopiesName) is PdfInteger { Value: >= 1 and <= 5 } copies ? (int)copies.Value : null;
        set
        {
            if (value is not (null or (>= 1 and <= 5)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The number of copies must be between 1 and 5");

            Set(NumCopiesName, value is { } copies ? PdfInteger.Get(copies) : null);
        }
    }

    /// <summary>Gets the names of the preferences viewers must not let the user override (<c>/Enforce</c>, PDF 2.0), such as <c>PrintScaling</c>.</summary>
    public IReadOnlyList<string> EnforcedPreferences => Dictionary?.Get<PdfArray>(EnforceName) is { } array
        ? [.. array.Take(64).Select(item => (PdfReference.ResolveIfReference(item) as PdfName)?.Value).OfType<string>()]
        : [];

    private bool GetFlag(string key) => Dictionary?.GetBoolean(PdfName.Create(key)) ?? false;

    private void SetFlag(string key, bool value) => Set(PdfName.Create(key), value ? PdfBoolean.True : null);

    private void Set(PdfName key, PdfObject? value)
    {
        var dictionary = Dictionary;
        if (dictionary is null)
        {
            if (value is null)
                return;

            dictionary = new PdfDictionary();
            _document.Catalog[ViewerPreferencesName] = dictionary;
        }

        dictionary[key] = value;
        if (dictionary.Count == 0)
        {
            _document.Catalog.Remove(ViewerPreferencesName);
        }
    }
}
