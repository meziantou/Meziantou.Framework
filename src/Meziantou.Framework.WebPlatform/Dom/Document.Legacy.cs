using System.Globalization;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

// Members of Document defined by the HTML specification. Unless it is loaded in a BrowsingContext, a document has no
// browsing context: it is not fully active, and it is cookie-averse.
public partial class Document
{
    private HtmlCollection? _images;
    private HtmlCollection? _embeds;
    private HtmlCollection? _links;
    private HtmlCollection? _forms;
    private HtmlCollection? _scripts;
    private HtmlCollection? _anchors;
    private HtmlCollection? _applets;
    private HtmlAllCollection? _all;
    private bool _designMode;

    /// <summary>
    /// The document whose origin this document has (https://html.spec.whatwg.org/multipage/browsers.html#concept-origin),
    /// for documents created by DOM APIs (createHTMLDocument, DOMParser...), whose URL is not the one of their origin.
    /// </summary>
    internal Document? OriginDocument { get; set; }

    /// <summary>https://svgwg.org/svg2-draft/struct.html#__svg__Document__rootElement</summary>
    /// <seealso href="https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__Document__rootElement"/>
    [IdlMember("rootElement", "https://w3c.github.io/svgwg/svg2-draft/struct.html#__svg__Document__rootElement")]
    public SvgSvgElement? RootElement => DocumentElement as SvgSvgElement;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsers.html#dom-document-domain: the effective domain of the origin. Setting it
    /// relaxes the same-origin restriction: the origin's domain is set to a registrable domain suffix of the effective domain
    /// (it does nothing in an origin-keyed agent cluster).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/browsers.html#dom-document-domain"/>
    [IdlMember("domain", "https://html.spec.whatwg.org/multipage/browsers.html#dom-document-domain")]
    public string Domain
    {
        get => Origin.EffectiveDomain?.Serialized ?? "";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (BrowsingContext is null)
                throw new DomException("document.domain cannot be set on a document without a browsing context", "SecurityError");

            if ((ActiveSandboxingFlags & SandboxingFlags.DocumentDomain) != SandboxingFlags.None)
                throw new DomException("document.domain cannot be set in a sandboxed document", "SecurityError");

            var origin = Origin;
            if (origin.EffectiveDomain is not { } effectiveDomain)
                throw new DomException("document.domain cannot be set on a document with an opaque origin", "SecurityError");

            if (!IsRegistrableDomainSuffixOfOrIsEqualTo(value, effectiveDomain))
                throw new DomException($"'{value}' is not a suffix of '{effectiveDomain.Serialized}'", "SecurityError");

            if (RelevantWindow?.AgentCluster?.IsOriginKeyed is true)
                return;

            origin.Domain = Urls.HostParser.Parse(value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#is-a-registrable-domain-suffix-of-or-is-equal-to</summary>
    internal static bool IsRegistrableDomainSuffixOfOrIsEqualTo(string hostSuffixString, Urls.UrlHost originalHost)
    {
        if (hostSuffixString.Length == 0)
            return false;

        if (Urls.HostParser.Parse(hostSuffixString) is not { } hostSuffix)
            return false;

        if (hostSuffix != originalHost)
        {
            // The IP addresses cannot be relaxed
            if (hostSuffix.Kind != Urls.UrlHostKind.Domain || originalHost.Kind != Urls.UrlHostKind.Domain)
                return false;

            if (!originalHost.Serialized.EndsWith("." + hostSuffix.Serialized, StringComparison.Ordinal))
                return false;

            // The suffix cannot be a public suffix
            if (hostSuffix.Serialized == Urls.PublicSuffixList.GetPublicSuffix(hostSuffix)
                || (Urls.PublicSuffixList.GetPublicSuffix(originalHost) is { } originalPublicSuffix && originalPublicSuffix.EndsWith("." + hostSuffix.Serialized, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-document-referrer: the URL of the document that created or navigated to this document.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-referrer"/>
    [IdlMember("referrer", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-referrer")]
    public string Referrer { get; internal set; } = "";

    /// <summary>
    /// The referrer policy of the policy container of the document (https://html.spec.whatwg.org/multipage/browsers.html#policy-container-referrer-policy):
    /// the <c>Referrer-Policy</c> header of its response or a <c>&lt;meta name="referrer"&gt;</c> element, or the empty
    /// string (the default referrer policy).
    /// </summary>
    internal string ReferrerPolicy { get; set; } = "";

    /// <summary>
    /// The HTTP status code of the response of the document (or of the script of a worker), or 0 when the document was not
    /// created from a response: the status of the violations of its content security policies (https://w3c.github.io/webappsec-csp/#violation-status).
    /// </summary>
    internal int ResponseStatus { get; set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#dom-document-cookie: the cookies of <see cref="BrowsingContextOptions.CookieJar"/>
    /// for the URL of the document, except the HttpOnly cookies (the cookie-string of a "non-HTTP" API,
    /// https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html#name-non-http-apis). A document whose site for
    /// cookies is opaque (e.g. a cross-site iframe) only reads and writes the <c>SameSite=None</c> cookies. A document
    /// without browsing context or cookie jar, or whose URL is not an http(s) URL, is cookie-averse: it has no cookies.
    /// </summary>
    /// <exception cref="DomException">The origin of the document is opaque (e.g. a sandboxed document).</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-cookie"/>
    [IdlMember("cookie", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-cookie")]
    public string Cookie
    {
        get
        {
            if (GetCookieJar() is not { } cookieJar)
                return "";

            if (Origin.IsOpaque)
                throw new DomException("The document has an opaque origin", "SecurityError");

            var cookies = cookieJar.GetCookieString(CookieContexts.ForScript(this, GetCookieUrl(creationUrl: false)));
            return cookies is null ? "" : InfraString.Utf8DecodeWithoutBom(cookies);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (GetCookieJar() is not { } cookieJar)
                return;

            if (Origin.IsOpaque)
                throw new DomException("The document has an opaque origin", "SecurityError");

            cookieJar.ReceiveSetCookieString(CookieContexts.ForScript(this, GetCookieUrl(creationUrl: false)), InfraString.Utf8EncodeToIsomorphic(value));
        }
    }

    /// <summary>
    /// The cookie jar of a document that is not cookie-averse (https://html.spec.whatwg.org/multipage/dom.html#cookie-averse-document-object):
    /// the URL of its cookies (see <see cref="GetCookieUrl"/>) is an http(s) URL.
    /// </summary>
    internal CookieJar? GetCookieJar()
    {
        if (CookieJar is not { } cookieJar || GetCookieUrl(creationUrl: false).Scheme is not ("http" or "https"))
            return null;

        return cookieJar;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dom.html#dom-document-lastmodified: the date and time of the <c>Last-Modified</c>
    /// header of the response of the document, in the local time zone, or the current date and time when it is unknown (measured
    /// with <see cref="BrowsingContextOptions.TimeProvider"/>).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-lastmodified"/>
    [IdlMember("lastModified", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-lastmodified")]
    public string LastModified
    {
        get
        {
            // The current date and time is measured with the clock of the window (BrowsingContextOptions.TimeProvider)
            var timeProvider = (RelevantWindow ?? OriginDocument?.RelevantWindow)?.EventLoop.TimeProvider ?? TimeProvider.System;
            var date = LastModifiedTime is { } lastModified ? TimeZoneInfo.ConvertTime(lastModified, timeProvider.LocalTimeZone) : timeProvider.GetLocalNow();
            return date.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The last modification date and time of the source of the document (the <c>Last-Modified</c> header), or null when it is unknown.</summary>
    internal DateTimeOffset? LastModifiedTime { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#pragma-set-default-language: set by <c>&lt;meta http-equiv="content-language"&gt;</c>.</summary>
    internal string? PragmaSetDefaultLanguage { get; set; }

    /// <summary>
    /// The language of the <c>Content-Language</c> header of the response of the document (the "higher-level protocol" of
    /// https://html.spec.whatwg.org/multipage/dom.html#language), or null when there is none (the first language when it lists several, as in the browsers).
    /// </summary>
    internal string? ProtocolLanguage { get; set; }

    /// <summary>Gets a live collection of the <c>img</c> elements of the document.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-images"/>
    [IdlMember("images", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-images")]
    public HtmlCollection Images => _images ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Img));

    /// <summary>Gets a live collection of the <c>embed</c> elements of the document.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-embeds"/>
    [IdlMember("embeds", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-embeds")]
    public HtmlCollection Embeds => _embeds ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Embed));

    /// <summary>Gets a live collection of the <c>embed</c> elements of the document (alias of <c>embeds</c>).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-plugins"/>
    [IdlMember("plugins", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-plugins")]
    public HtmlCollection Plugins => Embeds;

    /// <summary>
    /// Gets a live collection of the <c>a</c> and <c>area</c> elements of the document that have an <c>href</c>
    /// attribute.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-links"/>
    [IdlMember("links", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-links")]
    public HtmlCollection Links => _links ??= new HtmlCollection(this, static e => (e.IsHtml(TagId.A) || e.IsHtml(TagId.Area)) && e.HasAttributeWithLocalName("href"));

    /// <summary>Gets a live collection of the <c>form</c> elements of the document.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-forms"/>
    [IdlMember("forms", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-forms")]
    public HtmlCollection Forms => _forms ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Form));

    /// <summary>Gets a live collection of the <c>script</c> elements of the document.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-scripts"/>
    [IdlMember("scripts", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-scripts")]
    public HtmlCollection Scripts => _scripts ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Script));

    /// <summary>
    /// Gets a live collection of the <c>a</c> elements of the document that have a <c>name</c> attribute.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-anchors"/>
    [IdlMember("anchors", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-anchors")]
    public HtmlCollection Anchors => _anchors ??= new HtmlCollection(this, static e => e.IsHtml(TagId.A) && e.HasAttributeWithLocalName("name"));

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-all</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-all"/>
    [IdlMember("all", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-all")]
    public HtmlAllCollection All => _all ??= new HtmlAllCollection(this);

    /// <summary>Gets an always empty collection, as applets are no longer supported.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-applets"/>
    [IdlMember("applets", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-applets")]
    public HtmlCollection Applets => _applets ??= new HtmlCollection(this, static _ => false);

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#designMode</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-document-designmode"/>
    [IdlMember("designMode", "https://html.spec.whatwg.org/multipage/interaction.html#dom-document-designmode")]
    public string DesignMode
    {
        get => _designMode ? "on" : "off";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var keyword = InfraAscii.ToAsciiLowercase(value);
            if (keyword == "on" && !_designMode)
            {
                _designMode = true;

                // The active range is reset to the start of the document (the start of the document element, the editing
                // host). As in the browsers, an existing selection is kept.
                if (GetSelection() is { RangeCount: 0 } selection)
                {
                    selection.Collapse((Node?)DocumentElement ?? this, 0);
                }

                if (DocumentElement is { } documentElement)
                {
                    FocusModel.Focus(documentElement);
                }
            }
            else if (keyword == "off")
            {
                _designMode = false;
            }
        }
    }

    internal bool IsDesignModeEnabled => _designMode;

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#execcommand(): runs an editing command at the selection of the
    /// document, and returns false when the command is not supported or not enabled, or when its value is not valid.
    /// </summary>
    /// <exception cref="TypeErrorException">The command is <c>insertHTML</c>, Trusted Types are required and the default policy did not convert the value.</exception>
    /// <seealso href="https://w3c.github.io/editing/docs/execCommand/#execcommand()"/>
    [IdlMember("execCommand", "https://w3c.github.io/editing/docs/execCommand/#execcommand()")]
    public bool ExecCommand(string commandId, bool showUI = false, string value = "") => ExecCommandCore(commandId, showUI, value ?? "");

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#execcommand(): runs an editing command whose value was created by a
    /// Trusted Types policy (the HTML of the <c>insertHTML</c> command).
    /// </summary>
    /// <seealso href="https://w3c.github.io/editing/docs/execCommand/#execcommand()"/>
    [IdlMember("execCommand", "https://w3c.github.io/editing/docs/execCommand/#execcommand()")]
    public bool ExecCommand(string commandId, bool showUI, TrustedHtml value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ExecCommandCore(commandId, showUI, value);
    }

    private bool ExecCommandCore(string commandId, bool showUI, object value)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ThrowIfNotHtmlDocument();
        _ = showUI;

        // The value of the insertHTML command is a Trusted Types sink, checked before the command is tested (a command that is
        // not enabled throws for a string that is not trusted)
        string stringValue;
        if (Infra.InfraAscii.EqualsIgnoreAsciiCase(commandId, "insertHTML"))
        {
            stringValue = value is TrustedHtml trustedHtml
                ? TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, this, trustedHtml, "Document execCommand")
                : TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, this, (string)value, "Document execCommand");
        }
        else
        {
            stringValue = value as string ?? ((TrustedHtml)value).Data;
        }

        return Editing.Execute(this, commandId, stringValue);
    }

    /// <summary>Returns a value indicating whether the editing command can currently be executed.</summary>
    /// <seealso href="https://w3c.github.io/editing/docs/execCommand/#querycommandenabled()"/>
    [IdlMember("queryCommandEnabled", "https://w3c.github.io/editing/docs/execCommand/#querycommandenabled()")]
    public bool QueryCommandEnabled(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ThrowIfNotHtmlDocument();
        return Editing.IsEnabled(this, commandId);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#querycommandindeterm(): whether the command is indeterminate (e.g. a part of the selection is bold).</summary>
    /// <seealso href="https://w3c.github.io/editing/docs/execCommand/#querycommandindeterm()"/>
    [IdlMember("queryCommandIndeterm", "https://w3c.github.io/editing/docs/execCommand/#querycommandindeterm()")]
    public bool QueryCommandIndeterm(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ThrowIfNotHtmlDocument();
        return Editing.QueryIndeterm(this, commandId);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#querycommandstate(): the state of the command (e.g. whether the selection is bold).</summary>
    /// <seealso href="https://w3c.github.io/editing/docs/execCommand/#querycommandstate()"/>
    [IdlMember("queryCommandState", "https://w3c.github.io/editing/docs/execCommand/#querycommandstate()")]
    public bool QueryCommandState(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ThrowIfNotHtmlDocument();
        return Editing.QueryState(this, commandId);
    }

    /// <summary>Returns a value indicating whether the editing command is supported.</summary>
    /// <seealso href="https://w3c.github.io/editing/docs/execCommand/#querycommandsupported()"/>
    [IdlMember("queryCommandSupported", "https://w3c.github.io/editing/docs/execCommand/#querycommandsupported()")]
    public bool QueryCommandSupported(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ThrowIfNotHtmlDocument();
        return Editing.IsSupported(commandId);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#querycommandvalue(): the value of the command (e.g. the font name of the selection).</summary>
    /// <seealso href="https://w3c.github.io/editing/docs/execCommand/#querycommandvalue()"/>
    [IdlMember("queryCommandValue", "https://w3c.github.io/editing/docs/execCommand/#querycommandvalue()")]
    public string QueryCommandValue(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        ThrowIfNotHtmlDocument();
        return Editing.QueryValue(this, commandId);
    }

    /// <summary>The state of the editing commands of the document (see <see cref="Html.EditingState"/>).</summary>
    internal EditingState EditingState => field ??= new();

    /// <summary>The transaction of the undo history that records the mutations of the document while a command or an edit of the user runs (see <see cref="Html.EditingHistory"/>), or null.</summary>
    internal EditingTransaction? EditingRecorder { get; set; }

    /// <summary>The editing methods throw an InvalidStateError when the document is not an HTML document (as in the browsers).</summary>
    private void ThrowIfNotHtmlDocument()
    {
        if (!IsHtmlDocument)
            throw DomException.InvalidState("The editing commands are only supported in HTML documents");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-document-hidden</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-document-hidden"/>
    [IdlMember("hidden", "https://html.spec.whatwg.org/multipage/interaction.html#dom-document-hidden")]
    public bool Hidden => !IsVisible;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#dom-document-visibilitystate: <c>visible</c> for the active
    /// document of a browsing context whose top-level browsing context is visible (see <see cref="BrowsingContext.IsVisible"/>),
    /// else <c>hidden</c>.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-document-visibilitystate"/>
    [IdlMember("visibilityState", "https://html.spec.whatwg.org/multipage/interaction.html#dom-document-visibilitystate")]
    public string VisibilityState => IsVisible ? "visible" : "hidden";

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#visibility-state</summary>
    internal bool IsVisible { get; private set; }

    /// <summary>
    /// Sets the initial visibility state of the document, the system visibility state of its traversable
    /// (https://html.spec.whatwg.org/multipage/document-lifecycle.html#initialise-the-document-object): no event is fired, and
    /// the first visibility state entry of the performance timeline of the window starts at 0.
    /// </summary>
    internal void InitializeVisibilityState(bool visible)
    {
        IsVisible = visible;
        if (RelevantWindow is { } window && ReferenceEquals(window.Document, this))
        {
            window.Performance.QueueVisibilityStateEntry(visible, timestamp: null);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#update-the-visibility-state: sets the visibility state, queues
    /// a visibility state entry and fires visibilitychange at the document when it changes.
    /// </summary>
    internal void UpdateVisibilityState(bool visible)
    {
        if (IsVisible == visible)
            return;

        IsVisible = visible;
        if (RelevantWindow is { } window && ReferenceEquals(window.Document, this))
        {
            window.Performance.QueueVisibilityStateEntry(visible, window.GetCurrentHighResolutionTime());
        }

        if (!visible)
        {
            RunViewTransitionPageVisibilityChangeSteps();
        }

        FireEvent(new Event("visibilitychange", new EventInit { Bubbles = true }));
    }

    /// <summary>Does nothing; this obsolete method is kept for compatibility.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-clear"/>
    [IdlMember("clear", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-clear")]
    public void Clear()
    {
    }

    /// <summary>Does nothing; this obsolete method is kept for compatibility.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-captureevents"/>
    [IdlMember("captureEvents", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-captureevents")]
    public void CaptureEvents()
    {
    }

    /// <summary>Does nothing; this obsolete method is kept for compatibility.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-releaseevents"/>
    [IdlMember("releaseEvents", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-releaseevents")]
    public void ReleaseEvents()
    {
    }
}
