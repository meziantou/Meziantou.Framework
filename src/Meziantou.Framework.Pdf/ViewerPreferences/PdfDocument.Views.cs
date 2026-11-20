using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfDocument
{
    private static readonly PdfName PageLayoutName = PdfName.Create("PageLayout");
    private static readonly PdfName OpenActionName = PdfName.Create("OpenAction");

    private PdfOptionalContentProperties? _optionalContent;
    private PdfViewerPreferences? _viewerPreferences;
    private PdfAdditionalActions? _additionalActions;

    /// <summary>Gets the optional content of the document (ISO 32000-2, 8.11): its groups, the layers viewers let the user show or hide, and its configurations.</summary>
    public PdfOptionalContentProperties OptionalContent => _optionalContent ??= new PdfOptionalContentProperties(this);

    /// <summary>Gets the viewer preferences of the document (12.2): how viewers show it, and the settings of their print dialog.</summary>
    public PdfViewerPreferences ViewerPreferences => _viewerPreferences ??= new PdfViewerPreferences(this);

    /// <summary>Gets the additional actions of the document (<c>/AA</c>, 12.6.3): the actions run before it is closed, saved or printed, and after it is saved or printed.</summary>
    public PdfAdditionalActions AdditionalActions => _additionalActions ??= new PdfAdditionalActions(this, Catalog, PdfAdditionalActions.DocumentTriggers);

    /// <summary>Gets or sets how viewers lay out the pages when the document is opened (<c>/PageLayout</c>, 7.7.2).</summary>
    public PdfPageLayout PageLayout
    {
        get => Catalog.GetName(PageLayoutName)?.Value switch
        {
            "OneColumn" => PdfPageLayout.OneColumn,
            "TwoColumnLeft" => PdfPageLayout.TwoColumnLeft,
            "TwoColumnRight" => PdfPageLayout.TwoColumnRight,
            "TwoPageLeft" => PdfPageLayout.TwoPageLeft,
            "TwoPageRight" => PdfPageLayout.TwoPageRight,
            _ => PdfPageLayout.SinglePage,
        };
        set => Catalog[PageLayoutName] = value switch
        {
            PdfPageLayout.SinglePage => null,
            PdfPageLayout.OneColumn => PdfName.Create("OneColumn"),
            PdfPageLayout.TwoColumnLeft => PdfName.Create("TwoColumnLeft"),
            PdfPageLayout.TwoColumnRight => PdfName.Create("TwoColumnRight"),
            PdfPageLayout.TwoPageLeft => PdfName.Create("TwoPageLeft"),
            PdfPageLayout.TwoPageRight => PdfName.Create("TwoPageRight"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The page layout is not valid"),
        };
    }

    /// <summary>Gets or sets what viewers show next to the pages when the document is opened (<c>/PageMode</c>, 7.7.2): the bookmarks, the thumbnails, the layers, the attachments, or the pages in full screen.</summary>
    public PdfPageMode PageMode
    {
        get => Catalog.GetName(PdfNames.PageMode)?.Value switch
        {
            "UseOutlines" => PdfPageMode.UseOutlines,
            "UseThumbs" => PdfPageMode.UseThumbs,
            "FullScreen" => PdfPageMode.FullScreen,
            "UseOC" => PdfPageMode.UseOptionalContent,
            "UseAttachments" => PdfPageMode.UseAttachments,
            _ => PdfPageMode.UseNone,
        };
        set => Catalog[PdfNames.PageMode] = value == PdfPageMode.UseNone ? null : GetPageModeName(value);
    }

    /// <summary>
    /// Gets or sets the action run when the document is opened (<c>/OpenAction</c>, 7.7.2), such as going to a page; a
    /// destination of the catalog is read as a <see cref="PdfGoToAction"/>. <see langword="null"/> when the document has none.
    /// </summary>
    /// <exception cref="ArgumentException">The action refers to objects of another document.</exception>
    public PdfAction? OpenAction
    {
        get => Catalog.GetResolved(OpenActionName) switch
        {
            PdfArray destination => new PdfGoToAction(new PdfDictionary { [PdfNames.S] = PdfNames.GoTo, [PdfNames.D] = destination }, this),
            PdfDictionary action => PdfAction.Read(this, action),
            _ => null,
        };
        set
        {
            if (value is not null)
            {
                PdfAction.CheckDocument(value, this, nameof(value));
            }

            Catalog[OpenActionName] = value?.Dictionary;
        }
    }

    internal static PdfName GetPageModeName(PdfPageMode value) => PdfName.Create(value switch
    {
        PdfPageMode.UseNone => "UseNone",
        PdfPageMode.UseOutlines => "UseOutlines",
        PdfPageMode.UseThumbs => "UseThumbs",
        PdfPageMode.FullScreen => "FullScreen",
        PdfPageMode.UseOptionalContent => "UseOC",
        PdfPageMode.UseAttachments => "UseAttachments",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The page mode is not valid"),
    });
}
