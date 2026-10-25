using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/system-state.html#the-navigator-object: the identity and the state of the user
/// agent. The values are the ones of a desktop user agent that does not support plug-ins nor a PDF viewer.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#navigator"/>
[IdlInterface("Navigator", "https://html.spec.whatwg.org/multipage/system-state.html#navigator")]
public sealed partial class Navigator
{
    /// <summary>
    /// The user agent string of the library (<c>navigator.userAgent</c> when <see cref="BrowsingContextOptions.UserAgent"/> is
    /// null), also sent in the <c>User-Agent</c> header by the hosts that follow it.
    /// </summary>
    [NotInIdl]
    public const string DefaultUserAgent = "Mozilla/5.0 (Meziantou.Framework.WebPlatform)";

    private readonly Window _window;
    private UserActivation? _userActivation;
    private PluginArray? _plugins;
    private PluginMimeTypeArray? _mimeTypes;
    private Clipboard? _clipboard;

    internal Navigator(Window window)
    {
        _window = window;
    }

    // NavigatorID (https://html.spec.whatwg.org/multipage/system-state.html#navigatorid)

    /// <summary>
    /// Gets the maximum number of simultaneous touch contacts of the device (https://w3c.github.io/pointerevents/#dom-navigator-maxtouchpoints):
    /// 1 when the device has a touch screen (<see cref="ViewportOptions.HasTouch"/>, as the touch emulation of Chromium), 0
    /// otherwise (the touch pointers are injected by the host application, see <see cref="UserInput"/>).
    /// </summary>
    /// <seealso href="https://w3c.github.io/pointerevents/#dom-navigator-maxtouchpoints"/>
    [IdlMember("maxTouchPoints", "https://w3c.github.io/pointerevents/#dom-navigator-maxtouchpoints")]
    public int MaxTouchPoints => _window.BrowsingContext?.Top.Viewport is { HasTouch: true } ? 1 : 0;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appcodename</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appcodename"/>
    [IdlMember("appCodeName", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appcodename")]
    public string AppCodeName => "Mozilla";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appname</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appname"/>
    [IdlMember("appName", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appname")]
    public string AppName => "Netscape";

    /// <summary>The user agent string without its "Mozilla/" prefix (https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appversion).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appversion"/>
    [IdlMember("appVersion", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-appversion")]
    public string AppVersion => UserAgent.StartsWith("Mozilla/", StringComparison.Ordinal) ? UserAgent["Mozilla/".Length..] : UserAgent;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-platform</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-platform"/>
    [IdlMember("platform", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-platform")]
    public string Platform => "";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-product</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-product"/>
    [IdlMember("product", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-product")]
    public string Product => "Gecko";

    /// <summary>The value of the compatibility mode "Gecko" (https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-productsub).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-productsub"/>
    [IdlMember("productSub", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-productsub")]
    public string ProductSub => "20100101";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-useragent</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-useragent"/>
    [IdlMember("userAgent", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-useragent")]
    public string UserAgent => _window.Options.EffectiveUserAgent;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendor</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendor"/>
    [IdlMember("vendor", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendor")]
    public string Vendor => "";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendorsub</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendorsub"/>
    [IdlMember("vendorSub", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-vendorsub")]
    public string VendorSub => "";

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-navigator-taintenabled</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-navigator-taintenabled"/>
    [IdlMember("taintEnabled", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-navigator-taintenabled")]
    public bool TaintEnabled() => false;

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-navigator-oscpu</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-navigator-oscpu"/>
    [IdlMember("oscpu", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-navigator-oscpu")]
    public string Oscpu => "";

    // NavigatorLanguage (https://html.spec.whatwg.org/multipage/system-state.html#navigatorlanguage)

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-language</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-language"/>
    [IdlMember("language", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-language")]
    public string Language => _window.Options.EffectiveLanguages[0];

    /// <summary>The same list is returned until the languages change (https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-languages).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-languages"/>
    [IdlMember("languages", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-languages")]
    public IReadOnlyList<string> Languages => _window.Options.EffectiveLanguages;

    // NavigatorOnLine (https://html.spec.whatwg.org/multipage/system-state.html#navigatoronline)

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-online: false while the browsing context is offline (<see cref="BrowsingContext.IsOffline"/>).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-online"/>
    [IdlMember("onLine", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-online")]
    public bool OnLine => _window.BrowsingContext is not { IsOffline: true };

    // NavigatorContentUtils (https://html.spec.whatwg.org/multipage/system-state.html#navigatorcontentutils)

    /// <summary>
    /// Validates the parameters; the handlers are not registered as there is no user to accept them
    /// (https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-registerprotocolhandler).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-registerprotocolhandler"/>
    [IdlMember("registerProtocolHandler", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-registerprotocolhandler")]
    public void RegisterProtocolHandler(string scheme, string url) => NormalizeProtocolHandlerParameters(scheme, url);

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-unregisterprotocolhandler</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-unregisterprotocolhandler"/>
    [IdlMember("unregisterProtocolHandler", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-unregisterprotocolhandler")]
    public void UnregisterProtocolHandler(string scheme, string url) => NormalizeProtocolHandlerParameters(scheme, url);

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#normalize-protocol-handler-parameters</summary>
    private void NormalizeProtocolHandlerParameters(string scheme, string url)
    {
        scheme = scheme.ToLowerInvariant();
        if (!IsSafelistedScheme(scheme) && !(scheme.StartsWith("web+", StringComparison.Ordinal) && scheme.Length > 4 && scheme.AsSpan(4).IndexOfAnyExceptInRange('a', 'z') < 0))
            throw new DomException("The scheme is not allowed", "SecurityError");

        if (!url.Contains("%s", StringComparison.Ordinal))
            throw new DomException("The URL does not contain %s", "SyntaxError");

        var urlRecord = Reflection.EncodingParseUrl(_window.Document, url) ?? throw new DomException("The URL is not valid", "SyntaxError");
        if (urlRecord.Scheme is not ("http" or "https") || !OriginRecord.IsSameOrigin(OriginRecord.FromUrl(urlRecord), _window.Document.Origin) || !ReferrerPolicies.IsPotentiallyTrustworthy(urlRecord))
            throw new DomException("The URL is not allowed", "SecurityError");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#safelisted-scheme</summary>
    private static bool IsSafelistedScheme(string scheme) => scheme is "bitcoin" or "ftp" or "ftps" or "geo" or "im" or "irc" or "ircs" or "magnet" or "mailto"
        or "matrix" or "mms" or "news" or "nntp" or "openpgp4fpr" or "sftp" or "sip" or "sms" or "smsto" or "ssh" or "tel" or "urn" or "webcal" or "wtai" or "xmpp";

    // NavigatorCookies (https://html.spec.whatwg.org/multipage/system-state.html#navigatorcookies)

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-cookieenabled</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-cookieenabled"/>
    [IdlMember("cookieEnabled", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-cookieenabled")]
    public bool CookieEnabled => true;

    // NavigatorPlugins (https://html.spec.whatwg.org/multipage/system-state.html#navigatorplugins)

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-plugins</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-plugins"/>
    [IdlMember("plugins", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-plugins")]
    public PluginArray Plugins => _plugins ??= new PluginArray();

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-mimetypes</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-mimetypes"/>
    [IdlMember("mimeTypes", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-mimetypes")]
    public PluginMimeTypeArray MimeTypes => _mimeTypes ??= new PluginMimeTypeArray();

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-javaenabled</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-javaenabled"/>
    [IdlMember("javaEnabled", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-javaenabled")]
    public bool JavaEnabled() => false;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-pdfviewerenabled</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-pdfviewerenabled"/>
    [IdlMember("pdfViewerEnabled", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-pdfviewerenabled")]
    public bool PdfViewerEnabled => false;

    // NavigatorConcurrentHardware (https://html.spec.whatwg.org/multipage/workers.html#navigatorconcurrenthardware)

    /// <summary>https://html.spec.whatwg.org/multipage/workers.html#dom-navigator-hardwareconcurrency</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/workers.html#dom-navigator-hardwareconcurrency"/>
    [IdlMember("hardwareConcurrency", "https://html.spec.whatwg.org/multipage/workers.html#dom-navigator-hardwareconcurrency")]
    public ulong HardwareConcurrency => (ulong)Environment.ProcessorCount;

    /// <summary>Gets the clipboard of the window (https://w3c.github.io/clipboard-apis/#dom-navigator-clipboard).</summary>
    /// <seealso href="https://w3c.github.io/clipboard-apis/#dom-navigator-clipboard"/>
    [IdlMember("clipboard", "https://w3c.github.io/clipboard-apis/#dom-navigator-clipboard")]
    public Clipboard Clipboard => _clipboard ??= new Clipboard(_window);

    /// <summary>Gets the service workers of the window (https://w3c.github.io/ServiceWorker/#navigator-service-worker).</summary>
    /// <exception cref="DomException">A <c>SecurityError</c> when the origin of the document is opaque (e.g. a sandboxed document), as in the browsers.</exception>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-navigator-serviceworker"/>
    [IdlMember("serviceWorker", "https://w3c.github.io/ServiceWorker/#dom-navigator-serviceworker")]
    public ServiceWorkerContainer ServiceWorker => _window.Document.Origin.IsOpaque
        ? throw new DomException("The service workers cannot be used by a document whose origin is opaque", "SecurityError")
        : _window.ServiceWorkerObjects.Container ?? new ServiceWorkerContainer(_window);

    private LockManager? _locks;
    private StorageManager? _storage;

    /// <summary>Gets the storage manager of the window (https://storage.spec.whatwg.org/#dom-navigatorstorage-storage).</summary>
    /// <seealso href="https://storage.spec.whatwg.org/#dom-navigatorstorage-storage"/>
    [IdlMember("storage", "https://storage.spec.whatwg.org/#dom-navigatorstorage-storage")]
    public StorageManager Storage => _storage ??= new StorageManager(_window);

    /// <summary>Gets the lock manager of the window (https://w3c.github.io/web-locks/#dom-navigatorlocks-locks).</summary>
    /// <seealso href="https://w3c.github.io/web-locks/#dom-navigatorlocks-locks"/>
    [IdlMember("locks", "https://w3c.github.io/web-locks/#dom-navigatorlocks-locks")]
    public LockManager Locks => _locks ??= new LockManager(_window);

    /// <summary>The clipboard of the window, if it was created.</summary>
    internal Clipboard? ClipboardOrNull => _clipboard;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-navigator-useractivation</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-navigator-useractivation"/>
    [IdlMember("userActivation", "https://html.spec.whatwg.org/multipage/interaction.html#dom-navigator-useractivation")]
    public UserActivation UserActivation => _userActivation ??= new UserActivation(_window);

    /// <summary>
    /// Sends <paramref name="data"/> to <paramref name="url"/> with a POST request that is sent even if the document is
    /// unloaded (https://w3c.github.io/beacon/#sendbeacon-method). <paramref name="data"/> is null, a <see cref="Blob"/>, a
    /// byte array, a <see cref="FormData"/>, a <see cref="UrlSearchParams"/> or a string. The request is a keepalive
    /// request: the method returns false when its body exceeds the quota of the inflight keepalive requests of the
    /// document (64 KiB), or when the request is blocked as mixed content or by the Content Security Policy. The response
    /// is ignored.
    /// </summary>
    /// <exception cref="TypeErrorException">The URL is not a valid http or https URL, or the data is a stream.</exception>
    /// <seealso href="https://w3c.github.io/beacon/#sendbeacon-method"/>
    [IdlMember("sendBeacon", "https://w3c.github.io/beacon/#sendbeacon-method")]
    public bool SendBeacon(string url, object? data = null)
    {
        ArgumentNullException.ThrowIfNull(url);

        // Steps 1 to 3: the URL is parsed with the API base URL (and UTF-8)
        var document = _window.Document;
        var parsedUrl = UrlParser.Parse(url, document.DocumentBaseUrl);
        if (parsedUrl is null || parsedUrl.Scheme is not ("http" or "https"))
            throw new TypeErrorException($"'{url}' is not a valid http or https URL");

        // Steps 4 to 6: the body, extracted with the keepalive flag
        var headerList = new List<KeyValuePair<string, string>>();
        var corsMode = RequestMode.NoCors;
        byte[]? transmittedData = null;
        if (data is not null)
        {
            if (data is Scripting.JsReadableStream)
                throw new TypeErrorException("A keepalive request cannot have a stream body");

            (transmittedData, var contentType) = XmlHttpRequest.ExtractBody(data);
            if (transmittedData.Length + document.FetchGroup.GetInflightKeepaliveBytes() > FetchGroup.KeepaliveQuota)
                return false;

            if (contentType is not null)
            {
                if (!FetchHeaders.IsCorsSafelistedRequestHeader("Content-Type", contentType))
                {
                    corsMode = RequestMode.Cors;
                }

                headerList.Add(new("Content-Type", contentType));
            }
        }

        // A request blocked before it is sent (mixed content, Content Security Policy) is not queued: false is returned, as in
        // the browsers (the specification returns true and the fetch fails). The URL is checked once upgraded by the
        // upgrade-insecure-requests directive. The violations are reported once: by this check when the request is blocked (as
        // the fetch does: the report-only policies check the URL before it is upgraded), else by the fetch.
        var checkedUrl = UpgradeInsecureRequests.UpgradeSubresourceUrl(document, parsedUrl);
        if (MixedContent.ShouldBlockFetch(document, checkedUrl))
            return false;

        if (ContentSecurityPolicy.ShouldBlockRequest(document, checkedUrl, RequestDestination.Empty, reportViolations: false))
        {
            ContentSecurityPolicy.ShouldBlockRequest(document, parsedUrl, RequestDestination.Empty, disposition: SecurityPolicyViolationEventDisposition.Report);
            ContentSecurityPolicy.ShouldBlockRequest(document, checkedUrl, RequestDestination.Empty, disposition: SecurityPolicyViolationEventDisposition.Enforce);
            return false;
        }

        // Step 7: the request is fetched in parallel. The Content-Type is chosen by the author: a request whose Content-Type
        // is not CORS-safelisted is preceded by a CORS-preflight request, as in the browsers.
        var request = new FetchRequest(parsedUrl)
        {
            Method = "POST",
            Client = document,
            Origin = document.SerializeOrigin(),
            HeaderList = headerList,
            UnsafeRequest = true,
            Keepalive = true,
            Body = transmittedData,
            Mode = corsMode,
            CredentialsMode = FetchCredentialsMode.Include,
        };

        // The request is a record of the fetch group until it is done. It is done in a task queued when its response is
        // received, as the response of a network is processed asynchronously (the loader may complete synchronously).
        var eventLoop = document.BrowsingContext?.EventLoop;
        var record = document.FetchGroup.Add(abort: null, request);
        var timing = ResourceTiming.Start(_window, request, "beacon");
        _ = Fetching.FetchAsync(request, CancellationToken.None).ContinueWith(
            completed =>
            {
                if (eventLoop is null)
                {
                    record.Dispose();
                }
                else
                {
                    eventLoop.QueueTask(() =>
                    {
                        record.Dispose();
                        if (_window.Document.IsFullyActive)
                        {
                            timing?.Report(completed.Result);
                        }
                    });
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#the-useractivation-interface</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#useractivation"/>
[IdlInterface("UserActivation", "https://html.spec.whatwg.org/multipage/interaction.html#useractivation")]
public sealed class UserActivation
{
    private readonly Window _window;

    internal UserActivation(Window window)
    {
        _window = window;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-useractivation-hasbeenactive</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-useractivation-hasbeenactive"/>
    [IdlMember("hasBeenActive", "https://html.spec.whatwg.org/multipage/interaction.html#dom-useractivation-hasbeenactive")]
    public bool HasBeenActive => _window.HasStickyActivation;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-useractivation-isactive</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-useractivation-isactive"/>
    [IdlMember("isActive", "https://html.spec.whatwg.org/multipage/interaction.html#dom-useractivation-isactive")]
    public bool IsActive => _window.HasTransientActivation;
}
// The plugin objects are never created (there is no PDF viewer): the members ignore their arguments
#pragma warning disable IDE0051, IDE0060

/// <summary>
/// https://html.spec.whatwg.org/multipage/system-state.html#pluginarray: the PDF viewer plugins, when the PDF viewer is
/// supported. The library has no PDF viewer: the array is empty.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#pluginarray"/>
[IdlInterface("PluginArray", "https://html.spec.whatwg.org/multipage/system-state.html#pluginarray")]
public sealed class PluginArray
{
    internal PluginArray()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-refresh</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-refresh"/>
    [IdlMember("refresh", "https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-refresh")]
    public void Refresh()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-length</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-length")]
    public uint Length => 0;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-item</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-item"/>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-item")]
    public Plugin? Item(uint index) => null;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-nameditem</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-nameditem")]
    public Plugin? NamedItem(string name) => null;

    /// <summary>Gets the plugin at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-item"/>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Plugin? this[uint index] => Item(index);

    /// <summary>Gets the plugin with the given name, or null when there is none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/system-state.html#dom-pluginarray-nameditem")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public Plugin? this[string name] => NamedItem(name);

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#pluginarray: the supported property names (none).</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames() => [];
}

/// <summary>https://html.spec.whatwg.org/multipage/system-state.html#mimetypearray: the MIME types of the PDF viewer plugins (none).</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#mimetypearray"/>
[IdlInterface("MimeTypeArray", "https://html.spec.whatwg.org/multipage/system-state.html#mimetypearray")]
public sealed class PluginMimeTypeArray
{
    internal PluginMimeTypeArray()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-length</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-length")]
    public uint Length => 0;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-item</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-item"/>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-item")]
    public PluginMimeType? Item(uint index) => null;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-nameditem</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-nameditem")]
    public PluginMimeType? NamedItem(string name) => null;

    /// <summary>Gets the MIME type at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-item"/>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public PluginMimeType? this[uint index] => Item(index);

    /// <summary>Gets the MIME type with the given name, or null when there is none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetypearray-nameditem")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public PluginMimeType? this[string name] => NamedItem(name);

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#mimetypearray: the supported property names (none).</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames() => [];
}

/// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin: a PDF viewer plugin (never created: there is no PDF viewer).</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin"/>
[IdlInterface("Plugin", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin")]
public sealed class Plugin
{
    private Plugin()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-name</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-name"/>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-name")]
    public string Name => "";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-description</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-description"/>
    [IdlMember("description", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-description")]
    public string Description => "Portable Document Format";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-filename</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-filename"/>
    [IdlMember("filename", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-filename")]
    public string Filename => "internal-pdf-viewer";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-length</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-length")]
    public uint Length => 0;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-item</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-item"/>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-item")]
    public PluginMimeType? Item(uint index) => null;

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-nameditem</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-nameditem")]
    public PluginMimeType? NamedItem(string name) => null;

    /// <summary>Gets the MIME type at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-item"/>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public PluginMimeType? this[uint index] => Item(index);

    /// <summary>Gets the MIME type with the given name, or null when there is none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/system-state.html#dom-plugin-nameditem")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public PluginMimeType? this[string name] => NamedItem(name);

    /// <summary>The supported property names (none).</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames() => [];
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/system-state.html#mimetype: a MIME type of a PDF viewer plugin (never created:
/// there is no PDF viewer). The name avoids a conflict with the MIME type records of Infra.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#mimetype"/>
[IdlInterface("MimeType", "https://html.spec.whatwg.org/multipage/system-state.html#mimetype")]
public sealed class PluginMimeType
{
    private PluginMimeType(Plugin enabledPlugin)
    {
        EnabledPlugin = enabledPlugin;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-type</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-type"/>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-type")]
    public string Type => "application/pdf";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-description</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-description"/>
    [IdlMember("description", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-description")]
    public string Description => "Portable Document Format";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-suffixes</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-suffixes"/>
    [IdlMember("suffixes", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-suffixes")]
    public string Suffixes => "pdf";

    /// <summary>https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-enabledplugin</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-enabledplugin"/>
    [IdlMember("enabledPlugin", "https://html.spec.whatwg.org/multipage/system-state.html#dom-mimetype-enabledplugin")]
    public Plugin EnabledPlugin { get; }
}
