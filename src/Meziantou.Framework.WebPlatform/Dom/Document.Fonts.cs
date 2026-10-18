using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private FontFaceSet? _fonts;
    private FontSelector? _fontSelector;

    /// <summary>
    /// The worker whose font source is this document (https://drafts.csswg.org/css-font-loading/#font-source), or null: the
    /// document only holds the font faces of the worker (<see cref="WorkerGlobalScope.Fonts"/>) and computes the fonts of
    /// its canvases. It has no style sheet, no browsing context and is only used on the thread of the worker.
    /// </summary>
    internal WorkerGlobalScope? FontSourceWorker { get; init; }

    /// <summary>The font source of the document (https://drafts.csswg.org/css-font-loading/#font-source).</summary>
    [IdlMember("fonts", "https://drafts.csswg.org/css-font-loading/#dom-fontfacesource-fonts")]
    public FontFaceSet Fonts => _fonts ?? CreateFonts(deferEnvironmentCheck: false);

    /// <summary>The font source of the document when it was created, else null.</summary>
    internal FontFaceSet? FontsOrNull => _fonts;

    /// <summary>The font matching of the document, when it was created, else null.</summary>
    internal FontSelector? FontSelectorOrNull => _fontSelector;

    /// <summary>The font matching of the document (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm).</summary>
    internal FontSelector FontSelector => _fontSelector ??= new FontSelector(this);

    /// <summary>
    /// The fonts that the host installed in addition to the bundled fonts (<see cref="BrowsingContextOptions.InstalledFonts"/>
    /// of the browsing context of the document, or of the worker whose font source is the document), or null.
    /// </summary>
    internal InstalledFontCollection? InstalledFonts => (FontSourceWorker?.Options ?? EnvironmentOptions)?.InstalledFonts;

    /// <summary>
    /// Returns the font source of the document for the font matching: it is created without loading the font faces used by
    /// the text during the computation of a style (they are loaded by a task).
    /// </summary>
    internal FontFaceSet GetFontsForMatching() => _fonts ?? CreateFonts(deferEnvironmentCheck: true);

    /// <summary>
    /// Creates the font source of the document. It is the font source before its environment is checked: the check loads the
    /// font faces used by the text and may update the layout, whose font matching uses the font source.
    /// </summary>
    private FontFaceSet CreateFonts(bool deferEnvironmentCheck)
    {
        var fonts = new FontFaceSet(this);
        _fonts = fonts;
        fonts.CheckEnvironment(deferEnvironmentCheck);
        return fonts;
    }

    /// <summary>
    /// Notifies the font source that the document may no longer be loading. Once the document of a browsing context is
    /// loaded, the font faces used by its text are loaded, as when the user agent renders the document.
    /// </summary>
    internal void OnFontEnvironmentChanged()
    {
        if (_fonts is null && (BrowsingContext is null || ReadyState != DocumentReadyState.Complete))
            return;

        Fonts.OnEnvironmentChanged();
    }
}
