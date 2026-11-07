using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An entry of the map of preloaded resources of a document (https://html.spec.whatwg.org/multipage/links.html#preload-entry):
/// the integrity metadata of the preload, and its response once it is available (a network error included, so that a
/// preload that failed is not requested again).
/// </summary>
internal sealed class PreloadEntry(string integrityMetadata)
{
    private TaskCompletionSource<FetchResponse>? _onResponseAvailable;

    public string IntegrityMetadata => integrityMetadata;

    /// <summary>The response of the preload, or null while it is being fetched.</summary>
    public FetchResponse? Response { get; private set; }

    /// <summary>The response of the entry for the fetch that consumes it: it completes when the preload does.</summary>
    public Task<FetchResponse> Consume() => Response is { } response ? Task.FromResult(response) : (_onResponseAvailable ??= new TaskCompletionSource<FetchResponse>()).Task;

    /// <summary>The response of the preload is available: it is given to the fetch that consumed the entry, if any.</summary>
    public void SetResponse(FetchResponse response)
    {
        if (_onResponseAvailable is { } onResponseAvailable)
        {
            onResponseAvailable.TrySetResult(response);
        }
        else
        {
            Response = response;
        }
    }
}

/// <summary>
/// The preloads of the documents (https://html.spec.whatwg.org/multipage/links.html#link-type-preload): the resources of the
/// <c>&lt;link rel=preload&gt;</c> elements and of the <c>Link</c> headers of the documents are fetched and kept in the map of
/// preloaded resources of the document, where the next fetch of the same preload key consumes them (see
/// <see cref="Document.TryConsumePreloadedResource"/>).
/// </summary>
internal static class LinkPreload
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#translate-a-preload-destination: the destination of a preload whose
    /// <c>as</c> attribute is a preload destination ("fetch" is the empty destination), or null.
    /// </summary>
    public static RequestDestination? TranslatePreloadDestination(string? value) => InfraAscii.ToAsciiLowercase(value ?? "") switch
    {
        "fetch" => RequestDestination.Empty,
        "font" => RequestDestination.Font,
        "image" => RequestDestination.Image,
        "script" => RequestDestination.Script,
        "style" => RequestDestination.Style,
        "track" => RequestDestination.Track,
        _ => null,
    };

    /// <summary>
    /// The destination and the type of the module of a module preload (https://html.spec.whatwg.org/multipage/links.html#module-preload-destination):
    /// "script" when the <c>as</c> attribute is in no state (missing, empty or not a destination), a JSON, CSS or text module
    /// for the "json", "style" and "text" destinations, and a JavaScript module for a script-like destination. The type is
    /// null for the other destinations. The modules preloaded for a worklet destination are fetched as scripts (a worklet
    /// has its own module responses map: it does not use the preloaded modules of the window).
    /// </summary>
    public static (RequestDestination Destination, ModuleType? ModuleType) TranslateModulePreloadDestination(string? value) => InfraAscii.ToAsciiLowercase(value ?? "") switch
    {
        "json" => (RequestDestination.Json, ModuleType.Json),
        "style" => (RequestDestination.Style, ModuleType.Css),
        "text" => (RequestDestination.Text, ModuleType.Text),
        "script" or "audioworklet" or "paintworklet" => (RequestDestination.Script, ModuleType.JavaScript),
        "worker" => (RequestDestination.Worker, ModuleType.JavaScript),
        "sharedworker" => (RequestDestination.SharedWorker, ModuleType.JavaScript),
        "serviceworker" => (RequestDestination.ServiceWorker, ModuleType.JavaScript),
        "fetch" or "audio" or "document" or "embed" or "font" or "frame" or "iframe" or "image" or "manifest" or "object" or "report" or "track" or "video" or "webidentity" or "xslt" => (RequestDestination.Empty, null),
        _ => (RequestDestination.Script, ModuleType.JavaScript),
    };

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#match-preload-type: whether the <c>type</c> attribute of a preload
    /// matches its destination. The type must be a MIME type that the library supports for the destination.
    /// </summary>
    public static bool TypeMatchesDestination(string type, RequestDestination destination)
    {
        if (type.Length == 0 || destination == RequestDestination.Empty)
            return true;

        if (MimeType.Parse(type) is not { } mimeType)
            return false;

        return destination switch
        {
            RequestDestination.Script => mimeType.IsJavaScript,
            RequestDestination.Image => HtmlImageElement.IsSupportedImageType(mimeType.Essence),
            RequestDestination.Font => mimeType.Essence is "font/ttf" or "font/otf" or "font/woff" or "font/woff2" or "font/collection" or "font/sfnt" or "application/font-sfnt" or "application/font-ttf" or "application/font-woff" or "application/vnd.ms-opentype",
            RequestDestination.Style => mimeType.Essence == "text/css",
            RequestDestination.Track => mimeType.Essence == "text/vtt",
            _ => false,
        };
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#preload: fetches a request and adds its entry to the map of preloaded
    /// resources of the document. <paramref name="processResponse"/> is called with the response from a task of the event
    /// loop, after the fetch that consumed the entry (if any). Returns false when nothing is fetched (see
    /// <see cref="SubresourceFetching.FetchResponse"/>).
    /// </summary>
    public static bool Preload(Document document, FetchRequest request, Action<FetchResponse>? processResponse)
    {
        var entry = new PreloadEntry(request.Integrity);
        var key = Document.GetPreloadKey(request);
        var fetched = SubresourceFetching.FetchResponse(
            document,
            request,
            response =>
            {
                entry.SetResponse(response);
                processResponse?.Invoke(response);
            },
            initiatorType: "link");
        if (!fetched)
            return false;

        // The entry is added once the fetch has started: the fetch of the preload consumes the entry of a previous preload
        // of the same key
        document.SetPreloadedResource(key, entry);
        return true;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#process-link-headers: processes the <c>Link</c> headers of the
    /// response of a document, once the document is created. The <c>preload</c> links are preloaded; as in the browsers, the
    /// <c>modulepreload</c> links are fetched into the module map (the specification makes them do nothing). The other
    /// link types are ignored, and the media of the links are evaluated at once (the specification processes the links that
    /// have a <c>media</c> attribute once the viewport meta element is known).
    /// </summary>
    public static void ProcessLinkHeaders(Document document, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (document.BrowsingContext is null)
            return;

        foreach (var (name, value) in headers)
        {
            if (!InfraAscii.EqualsIgnoreAsciiCase(name, "Link"))
                continue;

            foreach (var link in LinkHeaderParser.Parse(value))
            {
                ProcessLinkHeader(document, link);
            }
        }
    }

    private static void ProcessLinkHeader(Document document, LinkHeaderValue link)
    {
        var isPreload = false;
        var isModulePreload = false;
        foreach (var token in (link.GetParameter("rel") ?? "").Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            isPreload |= InfraAscii.EqualsIgnoreAsciiCase(token, "preload");
            isModulePreload |= InfraAscii.EqualsIgnoreAsciiCase(token, "modulepreload");
        }

        if (!isPreload && !isModulePreload)
            return;

        // https://html.spec.whatwg.org/multipage/semantics.html#create-link-options-from-element (from the attributes of the header)
        if (link.GetParameter("media") is { } media && !MediaQueryEvaluator.Matches(media, StyleEngine.CreateEnvironment(document)))
            return;

        var href = link.Target;
        var crossOrigin = link.GetParameter("crossorigin");
        var useCredentials = crossOrigin is not null && InfraAscii.EqualsIgnoreAsciiCase(crossOrigin, "use-credentials");
        var referrerPolicy = InfraAscii.ToAsciiLowercase(link.GetParameter("referrerpolicy") ?? "");
        if (!ReferrerPolicies.IsValid(referrerPolicy))
        {
            referrerPolicy = "";
        }

        if (isPreload)
        {
            if (TranslatePreloadDestination(link.GetParameter("as")) is not { } destination || !TypeMatchesDestination(link.GetParameter("type") ?? "", destination))
                return;

            if (destination == RequestDestination.Image && link.GetParameter("imagesrcset") is { } imageSrcset && HtmlImageElement.SelectImageSource(href, imageSrcset, link.GetParameter("imagesizes"), document) is { } source)
            {
                href = source;
            }

            if (href.Length == 0 || UrlParser.Parse(href, document.UrlRecord) is not { } url)
                return;

            var request = SubresourceFetching.CreateRequest(document, url, destination, cors: crossOrigin is not null, referrerPolicy, initiator: null, useCredentials: useCredentials, integrity: link.GetParameter("integrity") ?? "");
            request.Nonce = link.GetParameter("nonce") ?? "";
            Preload(document, request, processResponse: null);
            return;
        }

        if (href.Length == 0 || UrlParser.Parse(href, document.UrlRecord) is not { } moduleUrl)
            return;

        if (document.BrowsingContext?.Options is not { ScriptingEnabled: true, ScriptEngine: { SupportsModuleScripts: true } engine } || document.DefaultView is not { } window || !ReferenceEquals(window.Document, document))
            return;

        var (moduleDestination, moduleType) = TranslateModulePreloadDestination(link.GetParameter("as"));
        if (moduleType is null)
            return;

        var options = new ModuleFetchOptions { IncludeCredentials = useCredentials, ReferrerPolicy = referrerPolicy, Nonce = link.GetParameter("nonce") ?? "" };
        engine.PreloadModuleScript(window, moduleUrl.Serialize(), moduleType.Value, moduleDestination, options, element: null, static _ => { });
    }
}

/// <summary>A link of a <c>Link</c> header (https://httpwg.org/specs/rfc8288.html#header): its target and its parameters, whose names are in lowercase.</summary>
internal sealed record LinkHeaderValue(string Target, List<KeyValuePair<string, string>> Parameters)
{
    /// <summary>The value of the first parameter with the name, or null.</summary>
    public string? GetParameter(string name)
    {
        foreach (var (parameterName, value) in Parameters)
        {
            if (parameterName == name)
                return value;
        }

        return null;
    }
}

/// <summary>The parser of the <c>Link</c> header (https://httpwg.org/specs/rfc8288.html#parse-set).</summary>
internal static class LinkHeaderParser
{
    /// <summary>Parses the value of a <c>Link</c> header (a comma-separated list of links). The links that are not valid are skipped.</summary>
    public static List<LinkHeaderValue> Parse(string value)
    {
        var links = new List<LinkHeaderValue>();
        var position = 0;
        while (position < value.Length)
        {
            // The target: <URI-Reference>
            SkipWhitespace(value, ref position);
            if (position >= value.Length)
                break;

            if (value[position] == ',')
            {
                position++;
                continue;
            }

            if (value[position] != '<')
            {
                SkipToNextLink(value, ref position);
                continue;
            }

            var end = value.IndexOf('>', position + 1, StringComparison.Ordinal);
            if (end < 0)
                break;

            var target = value[(position + 1)..end].Trim(' ', '\t');
            position = end + 1;

            // The parameters: *( OWS ";" OWS link-param )
            var parameters = new List<KeyValuePair<string, string>>();
            while (true)
            {
                SkipWhitespace(value, ref position);
                if (position >= value.Length || value[position] != ';')
                    break;

                position++;
                SkipWhitespace(value, ref position);
                var nameStart = position;
                while (position < value.Length && value[position] is not ('=' or ';' or ',' or ' ' or '\t'))
                {
                    position++;
                }

                var name = InfraAscii.ToAsciiLowercase(value[nameStart..position]);
                SkipWhitespace(value, ref position);
                var parameterValue = "";
                if (position < value.Length && value[position] == '=')
                {
                    position++;
                    SkipWhitespace(value, ref position);
                    if (position < value.Length && value[position] == '"')
                    {
                        parameterValue = MimeType.CollectHttpQuotedString(value, ref position, extractValue: true);
                    }
                    else
                    {
                        var valueStart = position;
                        while (position < value.Length && value[position] is not (';' or ','))
                        {
                            position++;
                        }

                        parameterValue = value[valueStart..position].TrimEnd(' ', '\t');
                    }
                }

                // The first occurrence of a parameter wins
                if (name.Length > 0 && !parameters.Exists(parameter => parameter.Key == name))
                {
                    parameters.Add(new(name, parameterValue));
                }
            }

            links.Add(new LinkHeaderValue(target, parameters));
            SkipToNextLink(value, ref position);
        }

        return links;
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && value[position] is ' ' or '\t')
        {
            position++;
        }
    }

    private static void SkipToNextLink(string value, ref int position)
    {
        while (position < value.Length && value[position] != ',')
        {
            position++;
        }

        if (position < value.Length)
        {
            position++;
        }
    }
}

public partial class Document
{
    private Dictionary<string, PreloadEntry>? _preloadedResources;

    /// <summary>Sets the entry of a preload key in the map of preloaded resources (https://html.spec.whatwg.org/multipage/links.html#map-of-preloaded-resources).</summary>
    internal void SetPreloadedResource(string key, PreloadEntry entry) => (_preloadedResources ??= new(StringComparer.Ordinal))[key] = entry;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/links.html#consume-a-preloaded-resource: removes the entry of the preload key of
    /// a request from the map of preloaded resources and gives its response, which completes when the preload does. Returns
    /// false when there is no entry or when the integrity metadata of the request are not the ones of the preload (the
    /// resource is then fetched again). A synchronous request does not wait for a preload that is being fetched.
    /// </summary>
    internal bool TryConsumePreloadedResource(FetchRequest request, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Task<FetchResponse>? response)
    {
        response = null;
        if (_preloadedResources is null || _preloadedResources.Count == 0)
            return false;

        var key = GetPreloadKey(request);
        if (!_preloadedResources.TryGetValue(key, out var entry))
            return false;

        var consumerMetadata = SubresourceIntegrity.Parse(request.Integrity);
        if (consumerMetadata.Count > 0 && !consumerMetadata.SetEquals(SubresourceIntegrity.Parse(entry.IntegrityMetadata)))
            return false;

        if (request.Synchronous && entry.Response is null)
            return false;

        _preloadedResources.Remove(key);
        response = entry.Consume();
        return true;
    }
}
