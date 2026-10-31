using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The status of a <see cref="FontFaceSet"/> (https://drafts.csswg.org/css-font-loading/#enumdef-fontfacesetloadstatus).</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#enumdef-fontfacesetloadstatus"/>
[IdlInterface("FontFaceSetLoadStatus", "https://drafts.csswg.org/css-font-loading/#enumdef-fontfacesetloadstatus")]
public enum FontFaceSetLoadStatus
{
    /// <summary>The set is loading at least one font face.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadstatus-loading"/>
    [IdlMember("loading", "https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadstatus-loading")]
    Loading,

    /// <summary>The set has finished loading its font faces.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadstatus-loaded"/>
    [IdlMember("loaded", "https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadstatus-loaded")]
    Loaded,
}

/// <summary>https://drafts.csswg.org/css-font-loading/#dictdef-fontfacesetloadeventinit</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#dictdef-fontfacesetloadeventinit"/>
[IdlInterface("FontFaceSetLoadEventInit", "https://drafts.csswg.org/css-font-loading/#dictdef-fontfacesetloadeventinit")]
public sealed class FontFaceSetLoadEventInit : EventInit
{
    /// <summary>Gets the font faces that started or finished loading.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadeventinit-fontfaces"/>
    [IdlMember("fontfaces", "https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadeventinit-fontfaces")]
    public IReadOnlyList<FontFace> FontFaces { get; init; } = [];
}

/// <summary>The event fired when a <see cref="FontFaceSet"/> starts or finishes loading fonts (https://drafts.csswg.org/css-font-loading/#fontfacesetloadevent).</summary>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfacesetloadevent"/>
[IdlInterface("FontFaceSetLoadEvent", "https://drafts.csswg.org/css-font-loading/#fontfacesetloadevent")]
public sealed class FontFaceSetLoadEvent : Event
{
    /// <summary>Creates an event of the given type with the font faces of the initialization dictionary.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadevent-fontfacesetloadevent"/>
    [IdlMember("constructor", "https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadevent-fontfacesetloadevent")]
    public FontFaceSetLoadEvent(string type, FontFaceSetLoadEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        FontFaces = [.. eventInitDict?.FontFaces ?? []];
    }

    /// <summary>Gets the font faces that started or finished loading.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadevent-fontfaces"/>
    [IdlMember("fontfaces", "https://drafts.csswg.org/css-font-loading/#dom-fontfacesetloadevent-fontfaces")]
    public IReadOnlyList<FontFace> FontFaces { get; }
}

/// <summary>
/// The font faces of a document (https://drafts.csswg.org/css-font-loading/#fontfaceset): the CSS-connected font faces of
/// the @font-face rules of the style sheets of the document, in document order, followed by the font faces added by
/// <see cref="Add"/>, in insertion order. The set of a worker (<see cref="WorkerGlobalScope.Fonts"/>) only has the font
/// faces added by <see cref="Add"/>: it is never pending on the environment, and its tasks run on the event loop of the worker.
/// </summary>
/// <remarks>
/// The font faces are loaded by <see cref="FontFace.Load"/> and <see cref="Load"/>, and by the user agent when the text of
/// the document uses them: the font matching of the layout requests the font faces it needs (the first available font of
/// the font-relative units and of the line boxes, the font of each character, the next family when a font face fails to
/// load), and, for the documents that are not rendered, the font faces of the first family of the texts are loaded
/// (<see cref="LoadUsedFontFaces"/>). The set is pending on the environment while the document is loading, has pending
/// style sheets, or has "pending layout operations which might cause the user agent to request a font, or which depend on
/// recently-loaded fonts": <see cref="Ready"/> is fulfilled once the document is loaded, its layout is up to date and no
/// font face is loading (<see cref="UpdateLayoutForFonts"/>).
/// </remarks>
/// <seealso href="https://drafts.csswg.org/css-font-loading/#fontfaceset"/>
[IdlInterface("FontFaceSet", "https://drafts.csswg.org/css-font-loading/#fontfaceset")]
public sealed class FontFaceSet : EventTarget, IReadOnlyCollection<FontFace>
{
    private readonly Document _document;

    // The worker whose font source is the set, or null for the set of a document
    private readonly WorkerGlobalScope? _worker;

    // The CSS-connected font faces, in document order, and the font faces added by add(), in insertion order
    private List<FontFace> _cssConnectedFaces = [];
    private readonly List<FontFace> _addedFaces = [];
    private readonly Dictionary<CssFontFaceRule, FontFace> _facesByRule = [];
    private (int Dom, int Style) _styleSheetsVersion = (-1, -1);

    private readonly List<FontFace> _loadingFonts = [];
    private List<FontFace> _loadedFonts = [];
    private List<FontFace> _failedFonts = [];
    // The [[ReadyPromise]]: fulfilled when the document is loaded and no font face of the set is loading
    private TaskCompletionSource<FontFaceSet> _readyPromise;
    private bool _stuckOnTheEnvironment;
    private bool _isUpdatingLayout;

    /// <param name="document">The document of the set.</param>
    internal FontFaceSet(Document document)
    {
        _document = document;
        _worker = document.FontSourceWorker;
        _readyPromise = CreateReadyPromise();
        if (_worker is not null)
        {
            // The set of a worker has no font face yet and nothing to wait for: it is loaded
            Status = FontFaceSetLoadStatus.Loaded;
            _readyPromise.TrySetResult(this);
            return;
        }

        // The set is pending on the environment until the document is loaded (see CheckEnvironment)
        _stuckOnTheEnvironment = true;
    }

    /// <summary>
    /// Checks the environment of a new set, once it is the font source of its document (the check may load the font faces
    /// used by the text and update the layout, which use the font source).
    /// </summary>
    /// <param name="defer">
    /// Whether the check runs in a task: the set is created by the font matching while a style is computed.
    /// </param>
    internal void CheckEnvironment(bool defer)
    {
        if (defer && _document.BrowsingContext is not null)
        {
            _document.QueueGlobalTask(OnEnvironmentChanged);
        }
        else
        {
            OnEnvironmentChanged();
        }
    }

    internal override GlobalScope? RelevantGlobal => _worker ?? (GlobalScope?)_document.RelevantWindow;

    /// <summary>The document of the font face set.</summary>
    internal Document Document => _document;

    /// <summary>
    /// The font faces of the set for the font matching (https://drafts.csswg.org/css-font-loading/#font-face-set-css): the
    /// CSS-connected font faces in document order, then the font faces added by <see cref="Add"/>.
    /// </summary>
    internal IEnumerable<FontFace> GetFacesForMatching()
    {
        UpdateCssConnectedFaces();
        return _cssConnectedFaces.Concat(_addedFaces);
    }

    /// <summary>Gets the number of font faces in the set.</summary>
    [NotInIdl("The size member of setlike<FontFace>")]
    public int Count
    {
        get
        {
            UpdateCssConnectedFaces();
            return _cssConnectedFaces.Count + _addedFaces.Count;
        }
    }

    /// <summary>Gets or sets the event handler of the <c>loading</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-onloading"/>
    [IdlMember("onloading", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-onloading")]
    public Action<Event>? OnLoading
    {
        get => GetEventHandler("loading");
        set => SetEventHandler("loading", value);
    }

    /// <summary>Gets or sets the event handler of the <c>loadingdone</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-onloadingdone"/>
    [IdlMember("onloadingdone", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-onloadingdone")]
    public Action<Event>? OnLoadingDone
    {
        get => GetEventHandler("loadingdone");
        set => SetEventHandler("loadingdone", value);
    }

    /// <summary>Gets or sets the event handler of the <c>loadingerror</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-onloadingerror"/>
    [IdlMember("onloadingerror", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-onloadingerror")]
    public Action<Event>? OnLoadingError
    {
        get => GetEventHandler("loadingerror");
        set => SetEventHandler("loadingerror", value);
    }

    /// <summary>
    /// Gets the promise resolved when the set is loaded. The font faces used by the text of the document that is not rendered
    /// yet are loaded first: the set is pending on the environment while the document has pending layout operations
    /// (https://drafts.csswg.org/css-font-loading/#fontfaceset-pending-on-the-environment).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-ready"/>
    [IdlMember("ready", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-ready")]
    public Task<FontFaceSet> Ready
    {
        get
        {
            _document.LoadPendingFontFaces();

            // As in Blink (FontFaceSetDocument::ready), the pending layout operations run before a promise that is already
            // resolved is returned: the font faces they request replace it with a new promise
            if (!_stuckOnTheEnvironment && !IsPendingOnTheEnvironment())
            {
                UpdateLayoutForFonts();
            }

            return _readyPromise.Task;
        }
    }

    /// <summary>Gets the load status of the set.</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-status"/>
    [IdlMember("status", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-status")]
    public FontFaceSetLoadStatus Status { get; private set; } = FontFaceSetLoadStatus.Loading;

    /// <summary>Returns whether the font face is in the set.</summary>
    [NotInIdl("The has() member of setlike<FontFace>")]
    public bool Has(FontFace font)
    {
        ArgumentNullException.ThrowIfNull(font);
        UpdateCssConnectedFaces();
        return _cssConnectedFaces.Contains(font) || _addedFaces.Contains(font);
    }

    /// <summary>Adds a font face that is not CSS-connected (https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-add).</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-add"/>
    [IdlMember("add", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-add")]
    public FontFaceSet Add(FontFace font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (Has(font))
            return this;

        if (font.IsCssConnected)
            throw new DomException("A CSS-connected font face cannot be added to a font face set", "InvalidModificationError");

        _addedFaces.Add(font);
        font.Sets.Add(this);
        font.AdoptDocument(_document);
        _document.FontSelectorOrNull?.Invalidate();
        if (font.Status == FontFaceLoadStatus.Loading)
        {
            OnFontFaceLoading(font);
        }

        return this;
    }

    /// <summary>Removes a font face that is not CSS-connected (https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-delete).</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-delete"/>
    [IdlMember("delete", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-delete")]
    public bool Delete(FontFace font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (font.IsCssConnected)
            return false;

        var deleted = _addedFaces.Remove(font);
        if (deleted)
        {
            font.Sets.Remove(this);
            _document.FontSelectorOrNull?.Invalidate();
        }

        RemoveFromLists(font);
        return deleted;
    }

    /// <summary>Removes the font faces that are not CSS-connected (https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-clear).</summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-clear"/>
    [IdlMember("clear", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-clear")]
    public void Clear()
    {
        foreach (var font in _addedFaces)
        {
            font.Sets.Remove(this);
        }

        if (_addedFaces.Count > 0)
        {
            _document.FontSelectorOrNull?.Invalidate();
        }

        _addedFaces.Clear();
        _loadedFonts.RemoveAll(font => !font.IsCssConnected);
        _failedFonts.RemoveAll(font => !font.IsCssConnected);
        if (_loadingFonts.Count > 0)
        {
            _loadingFonts.Clear();
            SwitchToLoaded();
        }
    }

    /// <summary>
    /// Loads the font faces that match a font (a value of the font shorthand, e.g. <c>12px MyFont</c>) and a text
    /// (https://drafts.csswg.org/css-font-loading/#font-face-set-load). The task completes with the matching font faces,
    /// once they are loaded, or fails when one of them cannot be loaded or when the font is invalid.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-load"/>
    [IdlMember("load", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-load")]
    public Task<IReadOnlyList<FontFace>> Load(string font, string text = " ")
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (FindMatchingFontFaces(font, text) is not { } matched)
            return Task.FromException<IReadOnlyList<FontFace>>(new DomException($"'{font}' is not a valid font", "SyntaxError"));

        var completion = new TaskCompletionSource<IReadOnlyList<FontFace>>();
        QueueTask(() =>
        {
            var tasks = matched.FontFaces.Select(face => face.Load()).ToArray();
            _ = Task.WhenAll(tasks).ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        // The first rejection, in the order of the font faces (https://webidl.spec.whatwg.org/#wait-for-all)
                        completion.TrySetException(Array.Find(tasks, t => t.IsFaulted)!.Exception!.InnerException!);
                    }
                    else
                    {
                        completion.TrySetResult(matched.FontFaces);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        });

        return completion.Task;
    }

    /// <summary>
    /// Returns whether the text can be rendered with the font without loading a font face
    /// (https://drafts.csswg.org/css-font-loading/#font-face-set-check): all the matching font faces are loaded.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-check"/>
    [IdlMember("check", "https://drafts.csswg.org/css-font-loading/#dom-fontfaceset-check")]
    public bool Check(string font, string text = " ")
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        var matched = FindMatchingFontFaces(font, text) ?? throw new DomException($"'{font}' is not a valid font", "SyntaxError");
        return matched.FontFaces.All(face => face.Status == FontFaceLoadStatus.Loaded);
    }

    /// <summary>Enumerates the font faces: the CSS-connected font faces, then the font faces added by <see cref="Add"/>.</summary>
    [NotInIdl("The iterator of setlike<FontFace>")]
    public IEnumerator<FontFace> GetEnumerator()
    {
        UpdateCssConnectedFaces();
        return _cssConnectedFaces.Concat(_addedFaces).ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Updates the CSS-connected font faces from the @font-face rules of the style sheets of the document
    /// (https://drafts.csswg.org/css-font-loading/#document-font-face-set). A font face whose rule was removed, or whose src
    /// descriptor changed, is no longer CSS-connected.
    /// </summary>
    private void UpdateCssConnectedFaces()
    {
        var version = (_document.DomVersion, _document.StyleSheetsVersion);
        if (version == _styleSheetsVersion)
            return;

        _styleSheetsVersion = version;
        var rules = new List<CssFontFaceRule>();
        foreach (var sheet in _document.StyleSheets)
        {
            CollectRules(sheet, rules, depth: 0);
        }

        foreach (var sheet in _document.AdoptedStyleSheets)
        {
            CollectRules(sheet, rules, depth: 0);
        }

        var faces = new List<FontFace>(rules.Count);
        foreach (var rule in rules)
        {
            if (_facesByRule.TryGetValue(rule, out var face) && face.Urls == rule.Style.GetPropertyValue("src"))
            {
                faces.Add(face);
                continue;
            }

            face = new FontFace(rule, _document);
            face.Sets.Add(this);
            _facesByRule[rule] = face;
            faces.Add(face);
        }

        foreach (var face in _cssConnectedFaces)
        {
            if (!faces.Contains(face))
            {
                if (face.Rule is { } rule && _facesByRule.TryGetValue(rule, out var current) && ReferenceEquals(current, face))
                {
                    _facesByRule.Remove(rule);
                }

                face.Disconnect();
                face.Sets.Remove(this);
                RemoveFromLists(face);
            }
        }

        _cssConnectedFaces = faces;

        static void CollectRules(CssStyleSheet sheet, List<CssFontFaceRule> rules, int depth)
        {
            if (sheet.Disabled || depth > 64)
                return;

            CollectFromList(sheet.RuleList, rules, depth);
        }

        static void CollectFromList(IEnumerable<CssRule> list, List<CssFontFaceRule> rules, int depth)
        {
            foreach (var rule in list)
            {
                switch (rule)
                {
                    case CssFontFaceRule fontFaceRule:
                        rules.Add(fontFaceRule);
                        break;
                    case CssImportRule { StyleSheet: { } imported }:
                        CollectRules(imported, rules, depth + 1);
                        break;
                    case CssGroupingRule grouping:
                        CollectFromList(grouping.CssRules, rules, depth);
                        break;
                }
            }
        }
    }

    private void RemoveFromLists(FontFace font)
    {
        _loadedFonts.Remove(font);
        _failedFonts.Remove(font);
        if (_loadingFonts.Remove(font) && _loadingFonts.Count == 0)
        {
            SwitchToLoaded();
        }
    }

    /// <summary>Called when a font face of the set starts loading.</summary>
    internal void OnFontFaceLoading(FontFace font)
    {
        if (_loadingFonts.Count == 0)
        {
            SwitchToLoading();
        }

        _loadingFonts.Add(font);
    }

    /// <summary>Called when a font face of the set is loaded or fails to load.</summary>
    internal void OnFontFaceLoaded(FontFace font, bool failed)
    {
        (failed ? _failedFonts : _loadedFonts).Add(font);
        if (_loadingFonts is [var last] && ReferenceEquals(last, font) && !_stuckOnTheEnvironment && !IsPendingOnTheEnvironment())
        {
            // The last font face is loaded: the layout "depend[s] on recently-loaded fonts", and may request other font faces
            // (the next family of the list when the font face failed to load or does not have a character). The set stays
            // loading while they load: the font face is removed from the loading fonts afterwards
            UpdateLayoutForFonts();
        }

        if (_loadingFonts.Remove(font) && _loadingFonts.Count == 0)
        {
            SwitchToLoaded();
        }
    }

    /// <summary>
    /// Runs the "pending layout operations which might cause the user agent to request a font"
    /// (https://drafts.csswg.org/css-font-loading/#fontfaceset-pending-on-the-environment), so that the set is not switched to
    /// loaded (and <see cref="Ready"/> is not resolved) before the font faces that the layout needs are loaded: the styles and
    /// the layout of the document are updated, and the font matching starts the loads of the font faces they use
    /// (<see cref="FontSelector.RequestLoad"/>). The layout is only updated for a document that is rendered (it has a
    /// rendering backend or its layout was already computed) and whose set has a font face that is not loaded yet: the other
    /// documents have no font to request.
    /// </summary>
    internal void UpdateLayoutForFonts()
    {
        if (_isUpdatingLayout || !_document.IsFullyActive || _document.BrowsingContext is not { } context)
            return;

        if (context.Options.RenderingBackend is null && _document.LayoutEngineOrNull is null)
            return;

        // (the @font-face rules of the shadow trees define font faces that are not in the set: the layout requests them)
        UpdateCssConnectedFaces();
        if (!_cssConnectedFaces.Exists(static face => face.Status == FontFaceLoadStatus.Unloaded) && !_addedFaces.Exists(static face => face.Status == FontFaceLoadStatus.Unloaded) && !HasUnloadedShadowFontFaces())
            return;

        _isUpdatingLayout = true;
        try
        {
            _document.GetLayoutEngine().UpdateLayout();
        }
        finally
        {
            _isUpdatingLayout = false;
        }
    }

    /// <summary>Whether a shadow root of the document has an @font-face rule whose font face is not loaded (see <see cref="FontSelector.GetFaces"/>).</summary>
    private bool HasUnloadedShadowFontFaces()
    {
        if (!_document.HasShadowRoots)
            return false;

        var engine = _document.GetStyleEngine();
        foreach (var node in _document.ShadowIncludingInclusiveDescendants())
        {
            if (node is ShadowRoot root && engine.GetFontFaceRules(root).Count > 0 && _document.FontSelector.GetFaces(root).Exists(static face => face.Face.Status == FontFaceLoadStatus.Unloaded))
                return true;
        }

        return false;
    }

    /// <summary>https://drafts.csswg.org/css-font-loading/#switch-the-fontfaceset-to-loading</summary>
    private void SwitchToLoading()
    {
        Status = FontFaceSetLoadStatus.Loading;
        if (_readyPromise.Task.IsCompleted)
        {
            _readyPromise = CreateReadyPromise();
        }

        QueueTask(() => FireFontLoadEvent("loading", []));
    }

    /// <summary>https://drafts.csswg.org/css-font-loading/#switch-the-fontfaceset-to-loaded</summary>
    private void SwitchToLoaded()
    {
        if (IsPendingOnTheEnvironment())
        {
            _stuckOnTheEnvironment = true;
            return;
        }

        Status = FontFaceSetLoadStatus.Loaded;
        _readyPromise.TrySetResult(this);
        QueueTask(() =>
        {
            var loadedFonts = _loadedFonts;
            var failedFonts = _failedFonts;
            _loadedFonts = [];
            _failedFonts = [];
            FireFontLoadEvent("loadingdone", loadedFonts);
            if (failedFonts.Count > 0)
            {
                FireFontLoadEvent("loadingerror", failedFonts);
            }
        });
    }

    /// <summary>Called when the document stops loading or a style sheet is loaded: the set may no longer be pending on the environment.</summary>
    internal void OnEnvironmentChanged()
    {
        if (!_stuckOnTheEnvironment || IsPendingOnTheEnvironment())
            return;

        _stuckOnTheEnvironment = false;
        LoadUsedFontFaces();
        UpdateLayoutForFonts();
        if (_loadingFonts.Count == 0)
        {
            SwitchToLoaded();
        }
    }

    /// <summary>
    /// Called when the rendering of the document is updated: the font faces used by its text are loaded, unless the set is
    /// pending on the environment (they are loaded when the document is loaded, see <see cref="OnEnvironmentChanged"/>).
    /// </summary>
    internal void OnRenderingUpdate()
    {
        if (_stuckOnTheEnvironment || IsPendingOnTheEnvironment())
            return;

        LoadUsedFontFaces();
    }

    /// <summary>Reports the violations of the content security policies by the sources of the font faces of the style sheets (see <see cref="FontFace.CheckContentSecurityPolicy"/>).</summary>
    internal void CheckContentSecurityPolicy()
    {
        UpdateCssConnectedFaces();
        foreach (var face in _cssConnectedFaces.ToArray())
        {
            face.CheckContentSecurityPolicy();
        }
    }

    /// <summary>https://drafts.csswg.org/css-font-loading/#fontfaceset-pending-on-the-environment: the document is loading or has pending style sheets.</summary>
    private bool IsPendingOnTheEnvironment() => _worker is null && (_document.ReadyState != DocumentReadyState.Complete || _document.StyleSheetImportsDelayingTheLoadEvent > 0);

    /// <summary>https://drafts.csswg.org/css-font-loading/#fire-a-font-load-event</summary>
    private void FireFontLoadEvent(string type, List<FontFace> fontFaces)
    {
        UpdateCssConnectedFaces();
        FireEvent(new FontFaceSetLoadEvent(type, new FontFaceSetLoadEventInit { FontFaces = fontFaces.FindAll(face => _cssConnectedFaces.Contains(face) || _addedFaces.Contains(face)) }));
    }

    private TaskCompletionSource<FontFaceSet> CreateReadyPromise()
    {
        var promise = new TaskCompletionSource<FontFaceSet>();
        Scripting.PromiseSlots.Register(promise.Task);
        return promise;
    }

    private void QueueTask(Action task)
    {
        if (_worker is not null)
        {
            _worker.QueueGlobalTask(task);
        }
        else if (_document.BrowsingContext is not null)
        {
            _document.QueueGlobalTask(task);
        }
        else
        {
            task();
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-font-loading/#find-the-matching-font-faces: the font faces of the set selected by the font
    /// matching (https://drafts.csswg.org/css-fonts-4/#font-style-matching) for each family of the font, whose unicode-range
    /// contains a character of the text. Returns null when the font is not a valid value of the font shorthand.
    /// </summary>
    private (IReadOnlyList<FontFace> FontFaces, bool Found)? FindMatchingFontFaces(string font, string text)
    {
        var components = CssParser.ParseListOfComponentValues(font);
        if (ValueMatcher.ContainsSubstitutionFunction(components) || CssShorthands.ParseValue("font", components, font) is not { } longhands)
            return null;

        string? GetLonghand(string name) => longhands.Find(longhand => longhand.Name == name).Value;
        if (longhands.Exists(longhand => ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(longhand.Value.Trim()))))
            return null;

        // The relative values are absolutized against the initial values (e.g. bolder is 700)
        var style = FontSlope.Parse(GetLonghand("font-style") ?? "normal");
        var weight = ParseWeight(GetLonghand("font-weight") ?? "normal");
        var width = FontSelectionCapabilities.ParseWidth(GetLonghand("font-width") ?? GetLonghand("font-stretch") ?? "normal") ?? 100;

        var faces = GetFacesForMatching().ToList();
        var matched = new List<FontFace>();
        foreach (var family in FontFamilyName.ParseList(GetLonghand("font-family") ?? ""))
        {
            if (!family.IsGeneric)
            {
                matched.AddRange(MatchFamily(faces, family.Name, style, weight, width));
            }
        }

        var found = matched.Count > 0;
        matched.RemoveAll(face => !face.SupportsAnyCharacter(text));
        return (matched, found);
    }

    /// <summary>
    /// Returns the font faces of a family selected by the font matching for a style, a weight and a width
    /// (https://drafts.csswg.org/css-fonts-4/#font-style-matching): the faces with the selected descriptors (several faces
    /// when their unicode-range differ).
    /// </summary>
    private List<FontFace> MatchFamily(List<FontFace> faces, string family, FontSlope style, double weight, double width)
    {
        var candidates = faces.FindAll(face => FontSelector.FamilyNamesEqual(ParseFamilies(face.Family) is [var name] ? name : face.Family, family));
        if (candidates.Count == 0)
            return candidates;

        // The rules that do not apply (e.g. in a @media or @supports rule whose condition is false) are not used by the font
        // matching, although their font faces are in the set
        if (candidates.Exists(static face => face.Rule is not null))
        {
            var applicableRules = _document.GetStyleEngine().GetFontFaceRules(_document);
            candidates.RemoveAll(face => face.Rule is { } rule && !applicableRules.ContainsKey(rule));
            if (candidates.Count == 0)
                return candidates;
        }

        // When several @font-face rules define the same font face (the same descriptors), the one in the last cascade layer
        // wins, then the last one (https://drafts.csswg.org/css-cascade-5/#layering)
        if (candidates.Count(face => face.Rule is not null) > 1)
        {
            var engine = _document.GetStyleEngine();
            var cssFaces = candidates.Where(face => face.Rule is not null).OrderBy(face => engine.GetFontFaceLayer(face.Rule!)).ToList();
            candidates.RemoveAll(face => face.Rule is not null && cssFaces.FindLastIndex(other => other.Style == face.Style && other.Weight == face.Weight && other.Stretch == face.Stretch && other.UnicodeRange == face.UnicodeRange) != cssFaces.IndexOf(face));
        }

        var capabilities = candidates.ConvertAll(face => FontSelectionCapabilities.FromDescriptors(face.GetDescriptorValue("font-weight"), face.GetDescriptorValue("font-width"), face.GetDescriptorValue("font-style"), face.FontFile?.Faces[0]));
        var selected = new List<int>(candidates.Count);
        FontMatcher.Select(capabilities, weight, style, width, selected);
        return selected.ConvertAll(index => candidates[index]);
    }

    /// <summary>
    /// Loads the font faces used by the text of the document, as the user agent does when it renders the text
    /// (https://drafts.csswg.org/css-font-loading/#font-face-load): for each element with text, the font faces of the first
    /// family of its font-family that has font faces in the set; for each element with a <c>text-overflow</c> that elides, the
    /// font faces that render its ellipsis (https://drafts.csswg.org/css-overflow-4/#text-overflow); for the generated content,
    /// the font faces that render the strings of <c>content</c> and the list markers.
    /// </summary>
    private const string CounterSample = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ. ";

    internal void LoadUsedFontFaces()
    {
        // The document of the font source of a worker has no text
        if (_worker is not null)
            return;

        UpdateCssConnectedFaces();
        var faces = this.ToList();
        if (!faces.Exists(face => face.Status == FontFaceLoadStatus.Unloaded))
            return;

        var engine = _document.GetStyleEngine();
        var toLoad = new List<FontFace>();

        // (the text of the shadow trees too, in shadow-including tree order: WPT css/css-shadow/font-face-*.html)
        foreach (var node in _document.ShadowIncludingInclusiveDescendants())
        {
            if (node is Element element)
            {
                var elementStyle = engine.GetStyle(element);
                if (elementStyle.GetComputedValue("text-overflow") is not ("clip" or "") && elementStyle.GetComputedValue("display") != "none")
                {
                    var textOverflow = Layout.TextOverflow.Parse(elementStyle.GetComputedValue("text-overflow"));
                    AddUsedFaces(elementStyle, GetEllipsis(textOverflow.First));
                    if (textOverflow.Second is { } second)
                    {
                        AddUsedFaces(elementStyle, GetEllipsis(second));
                    }
                }

                AddGeneratedContentFaces(element, elementStyle);

                // https://w3c.github.io/mathml-core/#the-math-script-level-property: font-size: math scales the font size with the
                // constants of the MATH table of the first available font of the parent, which is loaded to compute it (also
                // for an element without text, WPT css/css-fonts/math-script-level-and-math-style/math-script-level-004.tentative.html)
                if (elementStyle.TryGetCascadedValue("font-size", out var fontSize) && fontSize.Value.Value.Trim().Equals("math", StringComparison.OrdinalIgnoreCase)
                    && elementStyle.Parent is { } parentStyle && parentStyle.GetComputedValue("display") != "none")
                {
                    _ = parentStyle.Fonts.PrimaryFont;
                }

                continue;
            }

            // (the text of a shadow root has the style of its host)
            if (node is not Text text || string.IsNullOrWhiteSpace(text.Data) || (text.ParentNode as Element ?? (text.ParentNode as ShadowRoot)?.Host) is not { } parent)
                continue;

            var style = engine.GetStyle(parent);
            if (style.GetComputedValue("display") == "none")
                continue;

            AddUsedFaces(style, text.Data);
        }

        foreach (var face in toLoad)
        {
            _ = face.Load();
        }

        // The text of the generated content: the strings of the content of ::before, ::after and ::marker, and the marker
        // string of a list item (the representation of its counter is not computed here: the digits and the basic Latin
        // letters stand for it, with the suffix of the predefined counter styles)
        void AddGeneratedContentFaces(Element element, Cascade.ComputedStyle elementStyle)
        {
            var display = elementStyle.GetComputedValue("display");
            if (display is "none" or "contents")
                return;

            foreach (var pseudoElement in (ReadOnlySpan<string>)["before", "after"])
            {
                if (!engine.MayHavePseudoElementRules(element, pseudoElement))
                    continue;

                var pseudoStyle = engine.GetStyle(element, pseudoElement);
                if (pseudoStyle.GetComputedValue("content") is var content and not ("none" or "normal" or ""))
                {
                    AddUsedFaces(pseudoStyle, GetContentText(content));
                }
            }

            if (!display.Contains("list-item", StringComparison.Ordinal))
                return;

            var markerStyle = engine.GetStyle(element, "marker");
            var markerContent = markerStyle.GetComputedValue("content");
            if (markerContent is not ("none" or "normal" or ""))
            {
                AddUsedFaces(markerStyle, GetContentText(markerContent));
            }
            else if (markerStyle.GetComputedValue("list-style-type") is var type and not "none")
            {
                AddUsedFaces(markerStyle, type.StartsWith('"', StringComparison.Ordinal) || type.StartsWith('\'', StringComparison.Ordinal) ? GetContentText(type) : CounterSample);
            }
        }

        static string GetContentText(string content)
        {
            var text = new System.Text.StringBuilder();
            foreach (var value in Syntax.CssParser.ParseListOfComponentValues(content))
            {
                if (value is Syntax.CssPreservedToken { Type: Syntax.CssTokenType.String } token)
                {
                    text.Append(token.Token.Value);
                }
                else if (value is Syntax.CssFunction)
                {
                    text.Append(CounterSample);
                }
            }

            return text.ToString();
        }

        static string GetEllipsis(Layout.TextOverflowSide side) => side.Kind switch
        {
            Layout.TextOverflowKind.Ellipsis => "\u2026",
            Layout.TextOverflowKind.String => side.String ?? "",
            _ => "",
        };

        void AddUsedFaces(Cascade.ComputedStyle style, string text)
        {
            if (text.Length == 0)
                return;

            var weight = ParseWeight(style.GetComputedValue("font-weight"));
            var slope = FontSlope.Parse(style.GetComputedValue("font-style"));
            var width = FontDescription.ParsePercentage(style.GetComputedValue("font-width")) ?? 100;
            foreach (var family in FontFamilyName.ParseList(style.GetComputedValue("font-family")))
            {
                if (family.IsGeneric)
                    continue;

                var matched = MatchFamily(faces, family.Name, slope, weight, width);
                if (matched.Count == 0)
                    continue;

                toLoad.AddRange(matched.Where(face => face.Status == FontFaceLoadStatus.Unloaded && !toLoad.Contains(face) && face.SupportsAnyCharacter(text)));
                break;
            }
        }
    }

    private static double ParseWeight(string weight) => weight switch
    {
        "normal" or "auto" or "" => 400,
        "bold" => 700,
        "bolder" => 700,
        "lighter" => 100,
        _ => double.TryParse(weight, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 400,
    };

    /// <summary>Returns the names of the families of a value of font-family (the generic families are not names of font faces).</summary>
    internal static List<string> ParseFamilies(string value)
    {
        var families = new List<string>();
        foreach (var entry in Cascade.CssValueComputer.SplitArguments(CssParser.ParseListOfComponentValues(value)))
        {
            var significant = ValueMatcher.RemoveWhitespace(entry);
            if (significant is [CssPreservedToken { Type: CssTokenType.String } stringToken])
            {
                families.Add(stringToken.Token.Value);
            }
            else if (significant.Count > 0 && significant.TrueForAll(component => component is CssPreservedToken { Type: CssTokenType.Ident }))
            {
                families.Add(string.Join(' ', significant.Select(component => ((CssPreservedToken)component).Token.Value)));
            }
        }

        return families;
    }
}
