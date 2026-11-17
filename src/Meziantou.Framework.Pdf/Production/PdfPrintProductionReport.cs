using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// What a printer checks before printing a document: the output intents of the document, the page boxes (14.11.2) and the
/// colorants of each page, and the issues of the boxes.
/// </summary>
public sealed class PdfPrintProductionReport
{
    private const double Epsilon = 1e-3;

    private static readonly PdfName OutputIntentsName = PdfName.Create("OutputIntents");
    private static readonly PdfName BleedBoxName = PdfName.Create("BleedBox");
    private static readonly PdfName TrimBoxName = PdfName.Create("TrimBox");
    private static readonly PdfName ArtBoxName = PdfName.Create("ArtBox");

    private PdfPrintProductionReport(IReadOnlyList<PdfOutputIntent> outputIntents, PdfTrapped? trapped, IReadOnlyList<PdfPageProductionInfo> pages)
    {
        OutputIntents = outputIntents;
        Trapped = trapped;
        Pages = pages;
    }

    /// <summary>Gets the output intents of the document (<c>/OutputIntents</c> of the catalog).</summary>
    public IReadOnlyList<PdfOutputIntent> OutputIntents { get; }

    /// <summary>Gets whether the document is trapped (<c>/Trapped</c> of the document information), or <see langword="null"/> when it does not say.</summary>
    public PdfTrapped? Trapped { get; }

    /// <summary>Gets the reports of the pages.</summary>
    public IReadOnlyList<PdfPageProductionInfo> Pages { get; }

    /// <summary>Gets the spot colorants of all the pages, sorted by name.</summary>
    public IReadOnlyList<string> SpotColorants => [.. Pages.SelectMany(page => page.Colorants.SpotColorants).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Creates the report of a document. The document is not changed.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The report.</returns>
    /// <exception cref="PdfException">The page tree of the document cannot be read.</exception>
    public static PdfPrintProductionReport Create(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var intents = new List<PdfOutputIntent>();
        if (document.Catalog.GetResolved(OutputIntentsName) is PdfArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array.TryGetResolved<PdfDictionary>(i, out var intent))
                {
                    int? components = intent.GetResolved(PdfName.Create("DestOutputProfile")) is PdfStream profile ? (int)profile.Dictionary.GetInteger(PdfNames.N) : null;
                    intents.Add(new PdfOutputIntent(intent.GetName(PdfNames.S)?.Value, intent.GetText(PdfName.Create("OutputConditionIdentifier")), intent.GetText(PdfName.Create("OutputCondition")), intent.GetText(PdfName.Create("RegistryName")), intent.GetText(PdfName.Create("Info")), components));
                }
            }
        }

        var pages = new List<PdfPageProductionInfo>();
        var index = 0;
        foreach (var page in document.Pages)
        {
            pages.Add(CreatePage(page, index));
            index++;
        }

        PdfTrapped? trapped;
        try
        {
            trapped = document.Information.Trapped;
        }
        catch (PdfException)
        {
            trapped = null;
        }

        return new PdfPrintProductionReport(intents, trapped, pages);
    }

    private static PdfPageProductionInfo CreatePage(PdfPage page, int index)
    {
        var media = page.MediaBox;
        var crop = page.CropBox;
        var bleed = ReadBox(page, BleedBoxName);
        var trim = ReadBox(page, TrimBoxName);
        var art = ReadBox(page, ArtBoxName);
        var issues = new List<string>();
        if (trim is null && art is null)
        {
            issues.Add("The page has no trim box nor art box: the size of the finished page is not known");
        }

        CheckInside(issues, "bleed box", bleed, "media box", media);
        CheckInside(issues, "trim box", trim, "media box", media);
        CheckInside(issues, "trim box", trim, "bleed box", bleed);
        CheckInside(issues, "art box", art, "media box", media);
        if (trim is not null && art is not null)
        {
            issues.Add("The page has both a trim box and an art box: PDF/X allows one of them only");
        }

        PdfColorantUsage colorants;
        try
        {
            colorants = PdfSeparations.GetColorants(page);
        }
        catch (PdfException)
        {
            colorants = new PdfColorantUsage([], [], [], usesOverprint: false);
        }

        return new PdfPageProductionInfo(index, media, crop, bleed, trim, art, colorants, issues);
    }

    private static PdfRectangle? ReadBox(PdfPage page, PdfName name) => page.Dictionary.GetResolved(name) is PdfArray array && PdfRectangle.TryRead(array, out var box) ? box : null;

    private static void CheckInside(List<string> issues, string innerName, PdfRectangle? inner, string outerName, PdfRectangle? outer)
    {
        if (inner is not { } box || outer is not { } container)
            return;

        if (box.Left < container.Left - Epsilon || box.Bottom < container.Bottom - Epsilon || box.Right > container.Right + Epsilon || box.Top > container.Top + Epsilon)
        {
            issues.Add($"The {innerName} {box} extends outside the {outerName} {container}");
        }
    }
}
