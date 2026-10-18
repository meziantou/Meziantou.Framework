using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private bool _cssResourcesUpdateRequested;
    private (int Dom, int ElementState, int Style, int Cues) _cssResourcesVersion = (-1, -1, -1, 0);
    private (int Dom, int ElementState, int Style) _cssImagesVersion = (-1, -1, -1);
    private int _cueRenderingVersion;

    /// <summary>Whether a STYLE block of the WebVTT file of a text track of the document may reference an image (a <c>data:</c> URL).</summary>
    internal bool CueStyleSheetsMayReferenceImages { get; set; }
    private HashSet<(string Url, bool Cors, Css.Values.CssRequestModifiers Modifiers)>? _fetchedCssImages;

    /// <summary>
    /// Schedules the fetch of the resources used by the rendering of the document (the images of the computed styles and the
    /// font faces of the text) at the next rendering opportunity, when the document has a browsing context that can fetch them.
    /// </summary>
    internal void RequestCssResourcesUpdate()
    {
        RequestAnimationStylesUpdate();
        if (_cssResourcesUpdateRequested || BrowsingContext is not { Options.DocumentLoader: not null } || EventLoop is not { } eventLoop)
            return;

        _cssResourcesUpdateRequested = true;
        eventLoop.RequestCssResourcesUpdate(this);
    }

    /// <summary>
    /// Loads the font faces used by the text of the document when the rendering of the document is pending (the tree or the
    /// styles changed since the last rendering opportunity), as the user agent does when it updates the layout.
    /// </summary>
    internal void LoadPendingFontFaces()
    {
        if (_cssResourcesUpdateRequested && _fonts is not null && IsFullyActive)
        {
            _fonts.OnRenderingUpdate();
        }
    }

    /// <summary>
    /// Fetches the images used by the rendering of the document whose fetch waits for the next rendering opportunity (see
    /// <see cref="RequestCssResourcesUpdate"/>), before the load event. Returns whether the styles were examined.
    /// </summary>
    private bool FetchPendingCssImages()
    {
        if (!_cssResourcesUpdateRequested || !IsFullyActive || BrowsingContext is null)
            return false;

        var version = (DomVersion, ElementStateVersion, StyleSheetsVersion);
        if (version == _cssImagesVersion)
            return false;

        _cssImagesVersion = version;
        CssImageFetching.FetchUsedImages(this, _fetchedCssImages ??= []);
        return true;
    }

    /// <summary>
    /// The cues rendered over a video element of the document changed: the images of their styles are fetched at the next
    /// rendering opportunity.
    /// </summary>
    internal void OnCueRenderingChanged()
    {
        _cueRenderingVersion++;
        RequestCssResourcesUpdate();
    }

    /// <summary>Fetches the images used by the cues that the layout renders over a video element (see <see cref="CssImageFetching.FetchCueImages(Document, HtmlVideoElement, IReadOnlyList{WebVtt.WebVttDisplayItem}, HashSet{ValueTuple{string, bool, Css.Values.CssRequestModifiers}})"/>).</summary>
    internal void FetchCueImages(HtmlVideoElement video, IReadOnlyList<WebVtt.WebVttDisplayItem> items)
    {
        if (IsFullyActive && BrowsingContext is { Options.DocumentLoader: not null })
        {
            CssImageFetching.FetchCueImages(this, video, items, _fetchedCssImages ??= []);
        }
    }

    /// <summary>
    /// Fetches the resources used by the rendering of the document, as the user agent does when it updates the rendering
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering): the images referenced by the computed
    /// values of the rendered elements, and, once the document is loaded, the font faces used by its text.
    /// </summary>
    internal void UpdateCssResources()
    {
        _cssResourcesUpdateRequested = false;
        if (!IsFullyActive || BrowsingContext is null)
            return;

        // The computed styles only change when the tree, the state of an element or a style sheet changes
        var version = (DomVersion, ElementStateVersion, StyleSheetsVersion, _cueRenderingVersion);
        if (version == _cssResourcesVersion)
            return;

        // https://w3c.github.io/webvtt/#rules-for-updating-the-display-of-webvtt-text-tracks: the rules run when the cues that
        // are displayed change, and the boxes of a cue keep their position while the cue is displayed (the text track cue
        // display state). The positions are computed by the layout: a document that is rendered updates its layout at the
        // rendering opportunity that follows the change, so that a cue displayed later is placed relative to these cues.
        if (_cueRenderingVersion != _cssResourcesVersion.Cues && (LayoutEngineOrNull is not null || BrowsingContext.Options.RenderingBackend is not null))
        {
            GetLayoutEngine().UpdateLayout();
        }

        _cssResourcesVersion = version;
        CssImageFetching.FetchUsedImages(this, _fetchedCssImages ??= []);
        if (_fonts is not null || ReadyState == DocumentReadyState.Complete)
        {
            Fonts.OnRenderingUpdate();
        }
    }
}
