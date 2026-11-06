using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private bool _cssResourcesUpdateRequested;
    private (int Dom, int ElementState, int Style, int Cues) _cssResourcesVersion = (-1, -1, -1, 0);
    private (int Dom, int ElementState, int Style) _cssImagesVersion = (-1, -1, -1);
    private (int Dom, int ElementState, int Style) _fontFacesBeforeLoadVersion = (-1, -1, -1);
    private int _cueRenderingVersion;

    /// <summary>Whether a STYLE block of the WebVTT file of a text track of the document may reference an image (a <c>data:</c> URL).</summary>
    internal bool CueStyleSheetsMayReferenceImages { get; set; }
    private HashSet<(string Url, bool Cors, Css.Values.CssRequestModifiers Modifiers)>? _fetchedCssImages;

    // The ICC profiles of the @color-profile rules by URL (null while they load, or when they are not valid), and the
    // profiles of the custom color spaces by name
    private Dictionary<string, Css.Cascade.CssColorProfile?>? _colorProfilesByUrl;
    private Dictionary<string, Css.Cascade.CssColorProfile>? _colorProfiles;
    private int _colorProfilesVersion = -1;

    /// <summary>Returns the ICC profile of a custom color space (https://drafts.csswg.org/css-color-5/#at-profile), or null when no @color-profile rule defines it or its profile is not loaded (or not valid).</summary>
    internal Css.Cascade.CssColorProfile? GetColorProfile(string name) => _colorProfiles?.GetValueOrDefault(name);

    /// <summary>
    /// Schedules the fetch of the resources used by the rendering of the document (the images of the computed styles and the
    /// font faces of the text) at the next rendering opportunity, when the document has a browsing context that can fetch them.
    /// </summary>
    internal void RequestCssResourcesUpdate()
    {
        RequestAnimationStylesUpdate();

        // The rendering is updated when something requests it, not at each frame: a change of a document that contains nested
        // browsing contexts may resize their viewports, whose resize events are fired by the next rendering update
        // (WPT css/css-values/dynamic-viewport-units-rule-cache.html)
        if (ChildNavigables is { Count: > 0 } && EventLoop is { HasNestedWindowsWithResizeListeners: true } resizeEventLoop)
        {
            resizeEventLoop.RequestRenderingUpdate();
        }

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
        FetchColorProfiles();
        CssImageFetching.FetchUsedImages(this, _fetchedCssImages ??= []);
        return true;
    }

    /// <summary>
    /// Loads the font faces used by the text of the document (see <see cref="FontFaceSet.LoadUsedFontFaces"/> and
    /// <see cref="FontFaceSet.UpdateLayoutForFonts"/>) before the load event, as the browsers do when they render the document
    /// while it loads: their loads delay the load event (see <see cref="FontFacesDelayingTheLoadEvent"/>). Returns whether the
    /// styles were examined.
    /// </summary>
    private bool LoadUsedFontFacesBeforeLoadEvent()
    {
        if (!IsFullyActive || BrowsingContext is not { Options.DocumentLoader: not null })
            return false;

        var version = (DomVersion, ElementStateVersion, StyleSheetsVersion);
        if (version == _fontFacesBeforeLoadVersion)
            return false;

        _fontFacesBeforeLoadVersion = version;
        if (_fonts is null && GetStyleEngine().GetFontFaceRules(this).Count == 0)
            return false;

        // The layout of a rendered document requests the font faces that only its font matching knows, as the rendering of the
        // browsers does while the document loads: e.g. the next family while the first one loads (WPT css/css-fonts/font-display/font-display-failure-fallback.html)
        var fonts = GetFontsForMatching();
        fonts.LoadUsedFontFaces();
        fonts.UpdateLayoutForFonts();
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
    /// Fetches the ICC profiles of the @color-profile rules of the document (https://drafts.csswg.org/css-color-5/#at-profile)
    /// and maps the names of the custom color spaces to the loaded profiles: the styles are computed again when a profile is
    /// loaded or a name gets another profile. The fetches started before the load event delay it.
    /// </summary>
    private void FetchColorProfiles()
    {
        if (StyleSheetsVersion == _colorProfilesVersion || BrowsingContext is not { Options.DocumentLoader: not null })
            return;

        _colorProfilesVersion = StyleSheetsVersion;
        var rules = GetStyleEngine().GetColorProfileRules();
        if (rules.Count == 0 && _colorProfiles is null)
            return;

        foreach (var (_, (rule, baseUrl)) in rules)
        {
            if (GetColorProfileUrl(rule, baseUrl) is not { } url || !(_colorProfilesByUrl ??= new(StringComparer.Ordinal)).TryAdd(url.Serialize(), null))
                continue;

            var key = url.Serialize();
            var delaysLoadEvent = ReadyState != DocumentReadyState.Complete;
            if (delaysLoadEvent)
            {
                CssImagesDelayingTheLoadEvent++;
            }

            var request = SubresourceFetching.CreateRequest(this, url, RequestDestination.Empty, cors: false, referrerPolicy: "", initiator: null);
            var started = SubresourceFetching.Fetch(this, request, resource =>
            {
                _colorProfilesByUrl[key] = resource?.ContentBytes is { } bytes ? Css.Cascade.CssColorProfile.Parse(bytes.Span) : null;
                UpdateColorProfiles();
                if (delaysLoadEvent)
                {
                    CssImagesDelayingTheLoadEvent--;
                    TryQueueLoadEvent();
                }
            }, initiatorType: "css");
            if (!started && delaysLoadEvent)
            {
                CssImagesDelayingTheLoadEvent--;
            }
        }

        UpdateColorProfiles();
    }

    /// <summary>Maps the names of the custom color spaces to their loaded profiles, and computes the styles again when the map changes.</summary>
    private void UpdateColorProfiles()
    {
        var profiles = new Dictionary<string, Css.Cascade.CssColorProfile>(StringComparer.Ordinal);
        foreach (var (name, (rule, baseUrl)) in GetStyleEngine().GetColorProfileRules())
        {
            if (GetColorProfileUrl(rule, baseUrl) is { } url && _colorProfilesByUrl?.GetValueOrDefault(url.Serialize()) is { } profile)
            {
                profiles[name] = profile;
            }
        }

        var previous = _colorProfiles;
        if (previous is null ? profiles.Count == 0 : previous.Count == profiles.Count && profiles.All(p => previous.TryGetValue(p.Key, out var other) && ReferenceEquals(other, p.Value)))
            return;

        _colorProfiles = profiles;
        IncrementDomVersion();
    }

    /// <summary>Returns the URL of the <c>src</c> descriptor of a @color-profile rule (a <c>url()</c>), or null.</summary>
    private Urls.UrlRecord? GetColorProfileUrl(CssColorProfileRule rule, string? baseUrl)
    {
        string? value = null;
        foreach (var component in Css.Syntax.CssParser.ParseListOfComponentValues(rule.Src))
        {
            switch (component)
            {
                case Css.Syntax.CssPreservedToken { Type: Css.Syntax.CssTokenType.Url } urlToken:
                    value = urlToken.Token.Value;
                    break;
                case Css.Syntax.CssFunction { Name: var functionName } function when Infra.InfraAscii.EqualsIgnoreAsciiCase(functionName, "url") && Css.Values.ValueMatcher.RemoveWhitespace(function.Value) is [Css.Syntax.CssPreservedToken { Type: Css.Syntax.CssTokenType.String } stringToken]:
                    value = stringToken.Token.Value;
                    break;
            }
        }

        if (value is null)
            return null;

        var parsedBase = baseUrl is not null ? Urls.UrlParser.Parse(baseUrl, baseUrl: null) : null;
        return Urls.UrlParser.Parse(value, parsedBase ?? DocumentBaseUrl);
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
        FetchColorProfiles();
        CssImageFetching.FetchUsedImages(this, _fetchedCssImages ??= []);
        if (ContentSecurityPoliciesOrNull is { Count: > 0 })
        {
            Fonts.CheckContentSecurityPolicy();
        }

        if (_fonts is not null || ReadyState == DocumentReadyState.Complete)
        {
            Fonts.OnRenderingUpdate();
        }
    }
}
