using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-window-object. A window only exists for the documents
/// of a <see cref="BrowsingContext"/>. It is the global object of the realm of the <see cref="BrowsingContextOptions.ScriptEngine"/>,
/// if any. The members that need a rendering or a user interaction (dialogs...) are not implemented.
/// </summary>
/// <remarks>
/// The <c>WindowProxy</c> exotic object is not modeled: the members returning a <c>WindowProxy</c> return the active
/// window of the browsing context. A window is reused when an initial <c>about:blank</c> document is replaced by a
/// same-origin document, as specified.
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#window"/>
[IdlInterface("Window", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#window")]
public sealed partial class Window : GlobalScope
{
    private Location? _location;
    private Navigator? _navigator;
    private Navigation? _navigation;

    internal Window(Document document, BrowsingContextOptions options, DomEventLoop eventLoop)
        : base(options, eventLoop)
    {
        Document = document;
    }

    /// <summary>The script engine of the realm of the window, or null when scripting is disabled.</summary>
    internal override ScriptEngine? ScriptEngine => Options.ScriptEngine;

    internal override Urls.UrlRecord ApiBaseUrl => Document.DocumentBaseUrl;

    internal override string ScriptUrl => Document.Url;

    internal override OriginRecord OriginRecord => Document.Origin;

    internal override bool CanRunScript => Document.IsFullyActive;

    internal override bool IsScriptingEnabled => Document.ScriptingEnabled;

    internal override Document? AssociatedDocument => Document;

    internal override Document EnvironmentDocument => Document;

    internal override ExposureSet Exposure => ExposureSet.Window;

    internal override string InterfaceName => "Window";

    internal override void QueueGlobalTask(Action task) => Document.QueueGlobalTask(task);

    internal override TimeSpan SuspendedDuration => Document.GetSuspendedDuration();

    internal override bool AreTimersSuspended => Document.IsInBackForwardCache;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-document-window</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-document-2"/>
    [IdlMember("document", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-document-2")]
    public Document Document { get; internal set; }

    /// <summary>The browsing context of the associated document (https://html.spec.whatwg.org/multipage/nav-history-apis.html#window-bc), or null when it was destroyed.</summary>
    [NotInIdl("The browsing context of the window, which is not exposed to scripts")]
    public BrowsingContext? BrowsingContext => Document.BrowsingContext;

    /// <summary>
    /// The browsing context whose active window was this window when it was discarded (e.g. its iframe was removed, or the
    /// popup was closed): its WindowProxy still gives access to this window.
    /// </summary>
    internal BrowsingContextInstance? DiscardedBrowsingContext { get; set; }

    internal override bool IsWindow => true;

    internal override GlobalScope? RelevantGlobal => this;

    /// <summary>The Event Timing state of the window, created by its first trusted event of a considered type (or when a script reads the event counts).</summary>
    internal EventTimingState EventTiming => field ??= new EventTimingState(this);

    /// <summary>
    /// Initializes the event timing entry of a trusted event that is about to be dispatched to a target of the window
    /// (https://w3c.github.io/event-timing/#sec-modifications-DOM), or returns null when the type of the event is not considered
    /// or Event Timing is disabled (<see cref="WebPlatformFeatures.EventTimingEnabled"/>).
    /// </summary>
    internal PerformanceEventTiming? InitializeEventTiming(Event @event)
        => EventCounts.IsConsidered(@event.Type) && Options.EffectiveFeatures.EventTimingEnabled ? EventTiming.Initialize(@event) : null;

    /// <summary>Fires an event at the window with the legacy target override flag set: the target of the event is the document.</summary>
    internal void FireLegacyTargetOverrideEvent(Event @event)
    {
        @event.IsTrusted = true;
        EventDispatcher.Dispatch(@event, this, legacyTargetOverride: true);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window"/>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-self"/>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-frames"/>
    [IdlMember("window", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window")]
    [IdlMember("self", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-self")]
    [IdlMember("frames", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-frames")]
    public Window Self => this;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-name</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-name"/>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-name")]
    public string Name
    {
        get => BrowsingContext?.Name ?? "";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (BrowsingContext is { } browsingContext)
            {
                browsingContext.Name = value;
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-window-customelements</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-window-customelements"/>
    [IdlMember("customElements", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-window-customelements")]
    public CustomElementRegistry CustomElements => Document.CustomElementRegistryStorage!;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location"/>
    [IdlMember("location", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-location")]
    public Location Location => _location ??= new Location(this);

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator"/>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-clientinformation"/>
    [IdlMember("navigator", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator")]
    [IdlMember("clientInformation", "https://html.spec.whatwg.org/multipage/system-state.html#dom-clientinformation")]
    public Navigator Navigator => _navigator ??= new Navigator(this);

    /// <summary>The navigator of the window, if it was created.</summary>
    internal Navigator? NavigatorOrNull => _navigator;

    /// <summary>Gets the Cookie Store API of the window (https://cookiestore.spec.whatwg.org/#dom-window-cookiestore).</summary>
    /// <seealso href="https://cookiestore.spec.whatwg.org/#dom-window-cookiestore"/>
    [IdlMember("cookieStore", "https://cookiestore.spec.whatwg.org/#dom-window-cookiestore")]
    public CookieStore CookieStore => field ??= new CookieStore(this);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history: the history object of the associated document.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history"/>
    [IdlMember("history", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history")]
    public History History => Document.HistoryObject ??= new History(this);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation"/>
    [IdlMember("navigation", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation")]
    public Navigation Navigation => _navigation ??= new Navigation(this);

    /// <summary>The navigation API object, when it was created (it has no state before).</summary>
    internal Navigation? NavigationIfCreated => _navigation;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-locationbar: the location bar <see cref="BarProp"/> object of the window.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-locationbar"/>
    [IdlMember("locationbar", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-locationbar")]
    public BarProp LocationBar => field ??= new BarProp(this);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-menubar: the menu bar <see cref="BarProp"/> object of the window.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-menubar"/>
    [IdlMember("menubar", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-menubar")]
    public BarProp MenuBar => field ??= new BarProp(this);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-personalbar: the personal bar <see cref="BarProp"/> object of the window.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-personalbar"/>
    [IdlMember("personalbar", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-personalbar")]
    public BarProp PersonalBar => field ??= new BarProp(this);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-scrollbars: the scrollbar <see cref="BarProp"/> object of the window.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-scrollbars"/>
    [IdlMember("scrollbars", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-scrollbars")]
    public BarProp ScrollBars => field ??= new BarProp(this);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-statusbar: the status bar <see cref="BarProp"/> object of the window.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-statusbar"/>
    [IdlMember("statusbar", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-statusbar")]
    public BarProp StatusBar => field ??= new BarProp(this);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-toolbar: the toolbar <see cref="BarProp"/> object of the window.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-toolbar"/>
    [IdlMember("toolbar", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-toolbar")]
    public BarProp ToolBar => field ??= new BarProp(this);

    /// <summary>The status bar text: the last value it was set to (https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-status).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-status"/>
    [IdlMember("status", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-status")]
    public string Status
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "";

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-closed</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-closed"/>
    [IdlMember("closed", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-closed")]
    public bool Closed => BrowsingContext is not { } browsingContext || browsingContext.IsClosing || IsInDiscardedBrowsingContext;

    /// <summary>
    /// Whether the browsing context of the window was replaced in its navigable (a browsing context group switch) while its
    /// document is kept in the back/forward cache: the window appears closed to the other windows that reach it.
    /// </summary>
    internal bool IsInDiscardedBrowsingContext { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-close</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-close"/>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-close")]
    public void Close()
    {
        // https://html.spec.whatwg.org/multipage/document-sequences.html#script-closable: an auxiliary browsing context, or a
        // top-level traversable whose session history has a single entry
        if (BrowsingContext is not { Parent: null, IsClosing: false } browsingContext || (!browsingContext.IsAuxiliary && browsingContext.GetSessionHistoryEntries()?.Count != 1))
            return;

        // The incumbent global object's browsing context must be familiar with the traversable, and allowed by sandboxing to
        // navigate it (with the source snapshot params of the incumbent document, whatwg/html#12960): a document sandboxed
        // without allow-top-navigation cannot close its top-level traversable
        if (browsingContext.Options.ScriptEngine?.GetCurrentGlobal() is Window { BrowsingContext: { } incumbentNavigable } incumbent && !ReferenceEquals(incumbent, this)
            && (!incumbentNavigable.IsFamiliarWith(browsingContext) || !Sandboxing.IsAllowedToNavigate(incumbentNavigable, browsingContext, incumbent.Document.ActiveSandboxingFlags, incumbent.HasTransientActivation)))
        {
            return;
        }

        browsingContext.IsClosing = true;
        browsingContext.QueueTask(browsingContext.Close);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#dom-window-focus: runs the focusing steps for the navigable of the
    /// window (its active document). As in the browsers, the document keeps its focused element (see <see cref="FocusModel.FocusNavigable"/>).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-window-focus"/>
    [IdlMember("focus", "https://html.spec.whatwg.org/multipage/interaction.html#dom-window-focus")]
    public void Focus()
    {
        if (BrowsingContext is { } browsingContext && ReferenceEquals(browsingContext.Document, Document) && FocusModel.AllowFocus(Document))
        {
            FocusModel.FocusNavigable(Document);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-window-blur: does nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-window-blur"/>
    [IdlMember("blur", "https://html.spec.whatwg.org/multipage/interaction.html#dom-window-blur")]
    public void Blur()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-stop: aborts the ongoing navigation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-stop"/>
    [IdlMember("stop", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-stop")]
    public void Stop() => BrowsingContext?.StopLoading();

    /// <summary>The number of document-tree child navigables (https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-length).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-length")]
    public uint Length => BrowsingContext is null ? 0 : (uint)Document.GetDocumentTreeChildNavigables().Count;

    /// <summary>
    /// Returns the window of the document-tree child navigable at the given index (https://html.spec.whatwg.org/multipage/nav-history-apis.html#windowproxy-getownproperty),
    /// or null. The child navigables are indexed in the order in which their containers were inserted into the document
    /// (the order of their creation), not in tree order.
    /// </summary>
    [NotInIdl("The indexed properties of the WindowProxy exotic object")]
    public Window? this[int index]
    {
        get
        {
            if (BrowsingContext is null || index < 0)
                return null;

            var children = Document.GetDocumentTreeChildNavigables(creationOrder: true);
            return index < children.Count ? children[index].Window : null;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-top</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-top"/>
    [IdlMember("top", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-top")]
    public Window? Top => BrowsingContext?.Top.Window;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-opener: the window of the opener browsing context of
    /// an auxiliary browsing context, or null (also once the opener is discarded, e.g. closed). Setting it to null disowns
    /// the opener.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-opener"/>
    [IdlMember("opener", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-opener")]
    public object? Opener
    {
        get => BrowsingContext?.Opener is { IsDestroyed: false } opener ? opener.Window : null;
        set
        {
            // Setting another value than null defines an own property of the JavaScript object (done by the bindings)
            if (value is null && BrowsingContext is { } browsingContext)
            {
                browsingContext.Opener = null;
            }
        }
    }

    /// <summary>
    /// The window open steps (https://html.spec.whatwg.org/multipage/nav-history-apis.html#window-open-steps): navigates the
    /// navigable chosen by <paramref name="target"/>, or a new auxiliary browsing context (for <c>_blank</c> and the unknown
    /// names), which shares the event loop of this window and is discarded with the browsing context that owns the group.
    /// </summary>
    /// <param name="url">The URL to load, relative to the document of the window; the empty string keeps the initial about:blank document.</param>
    /// <param name="target">The target name (<c>_blank</c> when empty).</param>
    /// <param name="features">
    /// The window features: <c>noopener</c> and <c>noreferrer</c>, and for a new browsing context, the features that request
    /// a popup (<see cref="BrowsingContext.IsPopup"/>) and the ones that set the size of its viewport and the position of
    /// its client window (<c>width</c>, <c>height</c>, <c>left</c>, <c>top</c> and their aliases).
    /// </param>
    /// <returns>The window of the target navigable, or null (<c>noopener</c>, <c>noreferrer</c>, or no navigable).</returns>
    /// <exception cref="DomException">A <c>SyntaxError</c> when the URL cannot be parsed.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-open"/>
    [IdlMember("open", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-open")]
    public Window? Open(string url = "", string target = "_blank", string features = "") => Open(url, target, features, sourceNavigable: null);

    /// <summary>
    /// The window open steps. <paramref name="sourceNavigable"/> replaces the navigable of the entry document to choose the
    /// target navigable (e.g. <c>_self</c>) and as the opener.
    /// </summary>
    internal Window? Open(string url, string target, string features, BrowsingContext? sourceNavigable)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(features);

        // Nothing is opened while a document of the event loop is being unloaded (e.g. from an unload event handler)
        if (EventLoop.TerminationNestingLevel != 0)
            return null;

        // https://wicg.github.io/nav-speculation/prerendering.html#patch-window-open: a prerendering document opens nothing
        if (Document.Prerendering)
            return null;

        var sourceDocument = Dom.Document.EntryGlobalDocument ?? Document;
        Urls.UrlRecord? urlRecord = null;
        if (url.Length > 0)
        {
            urlRecord = Html.Reflection.EncodingParseUrl(sourceDocument, url) ?? throw DomException.Syntax($"The URL '{url}' is invalid");
        }

        if (target.Length == 0)
        {
            target = "_blank";
        }

        var tokenizedFeatures = WindowFeatures.Tokenize(features);
        var noreferrer = tokenizedFeatures.Remove("noreferrer", out var noreferrerValue) && WindowFeatures.ParseBoolean(noreferrerValue);
        var noopener = tokenizedFeatures.Remove("noopener", out var noopenerValue) && WindowFeatures.ParseBoolean(noopenerValue);
        var referrerPolicy = "";
        if (noreferrer)
        {
            noopener = true;
            referrerPolicy = "no-referrer";
        }

        // The creation of a popup consumes the user activation: the navigation is initiated with the activation of the call
        var hasTransientActivation = sourceDocument.RelevantWindow?.HasTransientActivation ?? false;
        sourceNavigable ??= sourceDocument.BrowsingContext;
        if (sourceNavigable is null || sourceNavigable.ChooseNavigable(target, noopener, out var windowType) is not { } targetNavigable)
            return null;

        if (windowType is WindowType.NewAndUnrestricted or WindowType.NewWithNoOpener)
        {
            // The remaining features (noopener and noreferrer are removed) tell whether a popup is requested, and give
            // the size and the position of its client window
            targetNavigable.IsPopup = WindowFeatures.IsPopupRequested(tokenizedFeatures);
            targetNavigable.SetUpFeatures(tokenizedFeatures);

            // The client window is sized after its creation: a resize event is fired at the listeners added meanwhile
            var newWindow = targetNavigable.Window;
            newWindow.IsClientWindowBeingSized = true;
            targetNavigable.QueueTask(() => newWindow.IsClientWindowBeingSized = false);

            // An about:blank URL keeps the initial about:blank document, whose URL is updated (e.g. about:blank#foo)
            if (urlRecord is not null && BrowsingContext.MatchesAboutBlank(urlRecord))
            {
                if (!BrowsingContext.UrlEquals(urlRecord, targetNavigable.Document.UrlRecord, excludeFragments: false))
                {
                    targetNavigable.UpdateUrlAndHistory(urlRecord, serializedData: null, hasSerializedData: false, NavigationHistoryBehavior.Replace);
                }
            }
            else if (urlRecord is not null)
            {
                targetNavigable.Navigate(urlRecord, sourceDocument, NavigationHistoryBehavior.Auto, referrerPolicy: referrerPolicy, exceptionsEnabled: true, hasTransientActivation: hasTransientActivation);
            }
        }
        else if (urlRecord is not null)
        {
            targetNavigable.Navigate(urlRecord, sourceDocument, NavigationHistoryBehavior.Auto, referrerPolicy: referrerPolicy, exceptionsEnabled: true, hasTransientActivation: hasTransientActivation);
        }

        // The opener of a new popup is set when it is created (https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-top-level-traversable).
        // An existing navigable gets the source navigable as opener, except (as in the browsers) an inclusive ancestor of the
        // source navigable (_self, _parent, _top...), which keeps its opener: the source would be discarded by the navigation.
        if (!noopener && windowType == WindowType.ExistingOrNone && !IsInclusiveAncestor(targetNavigable, sourceNavigable))
        {
            targetNavigable.Opener = sourceNavigable;
        }

        if (windowType == WindowType.NewWithNoOpener)
            return null;

        if (noopener && !Infra.InfraAscii.EqualsIgnoreAsciiCase(target, "_self") && !Infra.InfraAscii.EqualsIgnoreAsciiCase(target, "_parent") && !Infra.InfraAscii.EqualsIgnoreAsciiCase(target, "_top"))
            return null;

        return targetNavigable.Window;
    }

    private static bool IsInclusiveAncestor(BrowsingContext ancestor, BrowsingContext navigable)
    {
        for (BrowsingContext? current = navigable; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-parent</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-parent"/>
    [IdlMember("parent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-parent")]
    public Window? Parent
    {
        get
        {
            if (BrowsingContext is not { } browsingContext)
                return null;

            return browsingContext.Parent is { } parent ? parent.Window : this;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-frameelement</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-frameelement"/>
    [IdlMember("frameElement", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-frameelement")]
    public Element? FrameElement
    {
        get
        {
            if (BrowsingContext?.Container is not { } container)
                return null;

            // The container is only exposed to a same origin-domain document
            return Document.IsSameOriginDomain(container.NodeDocument, Document) ? container : null;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-originagentcluster: whether the agent cluster of the window is origin-keyed.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-originagentcluster"/>
    [IdlMember("originAgentCluster", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-originagentcluster")]
    public bool OriginAgentCluster => AgentCluster?.IsOriginKeyed ?? false;

    /// <summary>The agent cluster of the similar-origin window agent of the window (https://html.spec.whatwg.org/multipage/webappapis.html#similar-origin-window-agent).</summary>
    internal AgentCluster? AgentCluster { get; init; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-alert: shows an empty message with
    /// <see cref="BrowsingContextOptions.UserPromptHandler"/> (without handler, it does nothing).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-alert"/>
    [IdlMember("alert", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-alert")]
    public void Alert() => Alert("");

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-alert: shows the message with
    /// <see cref="BrowsingContextOptions.UserPromptHandler"/> (without handler, it does nothing).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-alert"/>
    [IdlMember("alert", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-alert")]
    public void Alert(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _ = ShowUserPrompt(UserPromptType.Alert, message, defaultValue: "");
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-confirm: asks the question with
    /// <see cref="BrowsingContextOptions.UserPromptHandler"/>, and returns whether the user accepted it (without handler, it returns false).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-confirm"/>
    [IdlMember("confirm", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-confirm")]
    public bool Confirm(string message = "")
    {
        ArgumentNullException.ThrowIfNull(message);
        return ShowUserPrompt(UserPromptType.Confirm, message, defaultValue: "") is { Accepted: true };
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-prompt: asks the question with
    /// <see cref="BrowsingContextOptions.UserPromptHandler"/>, and returns the text the user entered, or null when the user
    /// dismissed the dialog (without handler, it returns null).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-prompt"/>
    [IdlMember("prompt", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-prompt")]
    public string? Prompt(string message = "", string @default = "")
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(@default);
        return ShowUserPrompt(UserPromptType.Prompt, message, @default) is { Accepted: true } result ? result.Text ?? @default : null;
    }

    /// <summary>
    /// Shows a simple dialog with <see cref="BrowsingContextOptions.UserPromptHandler"/>, or returns null when it cannot be
    /// shown (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#cannot-show-simple-dialogs): there is no handler,
    /// the window is not the window of the active document of its browsing context, the document is sandboxed without
    /// allow-modals, or the document is being unloaded.
    /// </summary>
    internal UserPromptResult? ShowUserPrompt(UserPromptType type, string message, string defaultValue)
    {
        if (BrowsingContext is not { Options.UserPromptHandler: { } handler } browsingContext || !ReferenceEquals(browsingContext.Document, Document))
            return null;

        if ((Document.ActiveSandboxingFlags & SandboxingFlags.Modals) != SandboxingFlags.None)
            return null;

        if (type != UserPromptType.BeforeUnload && Document.UnloadCounter > 0)
            return null;

        // "Optionally, pause while waiting for the user to acknowledge the message": the handler blocks the event loop
        return handler(this, new UserPrompt(type, NormalizeNewlines(message), NormalizeNewlines(defaultValue)));

        // "Normalize newlines" of the message (https://infra.spec.whatwg.org/#normalize-newlines)
        static string NormalizeNewlines(string value) => value.Contains('\r', StringComparison.Ordinal) ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-print: there is no rendering, so it does nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-print"/>
    [IdlMember("print", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#dom-print")]
    public void Print()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-window-captureevents: does nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-window-captureevents"/>
    [IdlMember("captureEvents", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-window-captureevents")]
    public void CaptureEvents()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-window-releaseevents: does nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-window-releaseevents"/>
    [IdlMember("releaseEvents", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-window-releaseevents")]
    public void ReleaseEvents()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-external: the obsolete <see cref="Dom.External"/> object.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-external"/>
    [IdlMember("external", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-external")]
    public External External => field ??= new External();

    /// <summary>
    /// https://dom.spec.whatwg.org/#dom-window-event: the event being dispatched to a listener whose callback's global object
    /// is this window.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-window-event"/>
    [IdlMember("event", "https://dom.spec.whatwg.org/#dom-window-event")]
    public Event? Event => CurrentEvent;

    /// <summary>https://dom.spec.whatwg.org/#window-current-event</summary>
    internal Event? CurrentEvent { get; set; }

    // ----------------------------------------------------------------------------------------------------------------
    // WindowOrWorkerGlobalScope (https://html.spec.whatwg.org/multipage/webappapis.html#windoworworkerglobalscope-mixin)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-origin")]
    public override string Origin => Document.SerializeOrigin();

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext"/>
    [IdlMember("isSecureContext", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-issecurecontext")]
    public override bool IsSecureContext
    {
        get
        {
            // https://html.spec.whatwg.org/multipage/webappapis.html#secure-context: the documents of the navigable and of its ancestors must be potentially trustworthy
            for (var document = Document; document is not null; document = document.BrowsingContext?.Container?.NodeDocument)
            {
                if (!document.IsPotentiallyTrustworthyForSecureContext())
                    return false;
            }

            return true;
        }
    }

    /// <summary>https://wicg.github.io/anonymous-iframe/#dom-window-credentialless: whether the document is loaded in a credentialless iframe.</summary>
    /// <seealso href="https://wicg.github.io/anonymous-iframe/#dom-window-credentialless"/>
    [IdlMember("credentialless", "https://wicg.github.io/anonymous-iframe/#dom-window-credentialless")]
    public bool Credentialless => Document.IsCredentialless;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated: the cross-origin isolation mode of the
    /// browsing context group is "concrete" (its top-level document has the opener policy "same-origin" and an embedder
    /// policy compatible with cross-origin isolation), and the document may use the "cross-origin-isolated" feature
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#concept-settings-object-cross-origin-isolated-capability).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated"/>
    [IdlMember("crossOriginIsolated", "https://html.spec.whatwg.org/multipage/webappapis.html#dom-crossoriginisolated")]
    public override bool CrossOriginIsolated => BrowsingContext is { } browsingContext
        && browsingContext.Group.CrossOriginIsolationMode == CrossOriginIsolationMode.Concrete
        && PermissionsPolicyAlgorithms.IsFeatureEnabled(PolicyControlledFeature.CrossOriginIsolated, Document, Document.Origin, report: false);

    internal override object? AgentClusterKey => AgentCluster;

    // The agent clusters of a browsing context group have the cross-origin isolation mode of the group
    internal override bool IsAgentClusterCrossOriginIsolated => BrowsingContext is { } browsingContext && browsingContext.Group.CrossOriginIsolationMode != CrossOriginIsolationMode.None;

    // ----------------------------------------------------------------------------------------------------------------
    // Named properties (https://html.spec.whatwg.org/multipage/nav-history-apis.html#named-access-on-the-window-object)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the value of a named property of the window: the window of a document-tree child navigable whose target
    /// name is <paramref name="name"/>, an element, or an <see cref="HtmlCollection"/> of the named objects; or null.
    /// </summary>
    [NotInIdl("The named getter of the Window interface")]
    public object? GetNamedItem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
            return null;

        // The navigables whose target name is the name are named objects, but the name is a supported property name only when
        // the active document of the first of them is same origin with the document (or when an element is named with it too)
        var namedNavigable = GetFirstNamedChildNavigable(name, out var isSupported);
        if (isSupported)
            return namedNavigable!.Window;

        // The ID map of the document avoids searching the tree for each access to a global variable. Without an element
        // named by its name attribute (embed, form, img, object), the elements with the ID are the only named elements
        // (e.g. no element is named as a property of the prototype chain of the window, such as addEventListener).
        if (!Document.HasNamedObjectElements)
        {
            var count = IdMap.GetOrCreateForRoot(Document)!.GetCount(name, out var single);
            if (count == 0)
                return null;

            if (namedNavigable is not null)
                return namedNavigable.Window;

            return count == 1 ? single : new HtmlCollection(Document, element => IsNamedElement(element, name));
        }

        var elements = new List<Element>();
        for (var node = Document.FirstChild; node is not null && elements.Count < 2; node = node.GetNextInTreeOrder(Document))
        {
            if (node is Element element && IsNamedElement(element, name))
            {
                elements.Add(element);
            }
        }

        if (elements.Count > 0 && namedNavigable is not null)
            return namedNavigable.Window;

        return elements.Count switch
        {
            0 => null,
            1 => elements[0],
            _ => new HtmlCollection(Document, element => IsNamedElement(element, name)),
        };
    }

    /// <summary>
    /// Returns the first document-tree child navigable whose target name is <paramref name="name"/>, or null.
    /// <paramref name="isSupported"/> tells whether the name is in the document-tree child navigable target name property set
    /// (https://html.spec.whatwg.org/multipage/nav-history-apis.html#document-tree-child-navigable-target-name-property-set):
    /// the active document of that navigable is same origin with the document of the window. A cross-origin navigable hides
    /// the next ones that have the same name.
    /// </summary>
    internal BrowsingContext? GetFirstNamedChildNavigable(string name, out bool isSupported)
    {
        isSupported = false;
        if (name.Length == 0)
            return null;

        foreach (var child in Document.GetDocumentTreeChildNavigables())
        {
            if (child.Name == name)
            {
                isSupported = Document.IsSameOrigin(child.Document, Document);
                return child;
            }
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#named-access-on-the-window-object</summary>
    private static bool IsNamedElement(Element element, string name)
    {
        if (element.Id == name)
            return true;

        return element.IsHtmlNamespace && element.LocalName is "embed" or "form" or "img" or "object" && element.GetAttributeValue("name") == name;
    }
}

/// <summary>The window features of <c>window.open()</c> (https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-window-open-features-tokenize).</summary>
internal static class WindowFeatures
{
    private static bool IsSeparator(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r' or '=' or ',';

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-window-open-features-tokenize</summary>
    public static Dictionary<string, string> Tokenize(string features)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position < features.Length)
        {
            while (position < features.Length && IsSeparator(features[position]))
            {
                position++;
            }

            var start = position;
            while (position < features.Length && !IsSeparator(features[position]))
            {
                position++;
            }

            var name = Infra.InfraAscii.ToAsciiLowercase(features[start..position]) switch
            {
                "screenx" => "left",
                "screeny" => "top",
                "innerwidth" => "width",
                "innerheight" => "height",
                var other => other,
            };

            while (position < features.Length && features[position] != '=')
            {
                if (features[position] == ',' || !IsSeparator(features[position]))
                    break;

                position++;
            }

            var value = "";
            if (position < features.Length && IsSeparator(features[position]))
            {
                while (position < features.Length && IsSeparator(features[position]))
                {
                    if (features[position] == ',')
                        break;

                    position++;
                }

                start = position;
                while (position < features.Length && !IsSeparator(features[position]))
                {
                    position++;
                }

                value = Infra.InfraAscii.ToAsciiLowercase(features[start..position]);
            }

            if (name.Length > 0)
            {
                result[name] = value;
            }
        }

        return result;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-window-open-features-parse-boolean</summary>
    public static bool ParseBoolean(string value)
    {
        if (value.Length == 0 || value is "yes" or "true")
            return true;

        // An error is parsed as 0
        return Html.Microsyntaxes.TryParseInteger(value, out var parsed) && parsed != 0;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#popup-window-is-requested</summary>
    public static bool IsPopupRequested(Dictionary<string, string> tokenizedFeatures)
    {
        if (tokenizedFeatures.Count == 0)
            return false;

        if (tokenizedFeatures.TryGetValue("popup", out var popup))
            return ParseBoolean(popup);

        if (!IsFeatureSet(tokenizedFeatures, "location", defaultValue: false) && !IsFeatureSet(tokenizedFeatures, "toolbar", defaultValue: false))
            return true;

        return !IsFeatureSet(tokenizedFeatures, "menubar", defaultValue: false)
            || !IsFeatureSet(tokenizedFeatures, "resizable", defaultValue: true)
            || !IsFeatureSet(tokenizedFeatures, "scrollbars", defaultValue: false)
            || !IsFeatureSet(tokenizedFeatures, "status", defaultValue: false);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#window-feature-is-set</summary>
    private static bool IsFeatureSet(Dictionary<string, string> tokenizedFeatures, string name, bool defaultValue)
        => tokenizedFeatures.TryGetValue(name, out var value) ? ParseBoolean(value) : defaultValue;

    /// <summary>
    /// The value of a size or position feature (https://drafts.csswg.org/cssom-view/#set-up-browsing-context-features): the
    /// rules for parsing integers, an error giving 0. The values that do not fit in an integer are clamped.
    /// </summary>
    public static int ParseInteger(string value)
        => Html.Microsyntaxes.TryParseInteger(value, out var parsed) ? (int)Math.Clamp(parsed, int.MinValue, int.MaxValue) : 0;
}
