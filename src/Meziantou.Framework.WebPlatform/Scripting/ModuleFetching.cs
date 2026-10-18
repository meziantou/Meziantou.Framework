using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The source of a fetched module: its response URL, its source text, and the policy of its <c>Referrer-Policy</c> header
/// (the empty string when there is none), which becomes the referrer policy of the fetch options of the module.
/// </summary>
internal sealed record FetchedModule(string Url, string SourceText, string ReferrerPolicy = "");

/// <summary>
/// Fetches the module scripts (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script) with
/// the fetch algorithm (<see cref="Fetching"/>): CORS requests, whose response must be a JavaScript, JSON or CSS resource
/// (depending on the module type, any resource for a text module), decoded as UTF-8.
/// </summary>
internal static class ModuleFetching
{
    /// <summary>
    /// Fetches a single module script. <paramref name="onComplete"/> is always called from a task of the event loop, with
    /// the module, or null for a network error, a CORS failure, a non-ok status or a wrong MIME type (any MIME type is valid for a text module). The referrer is the
    /// base URL of the referencing script (https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule),
    /// or null for the document ("client"). <paramref name="isPreload"/> is whether the module is fetched by a module preload.
    /// </summary>
    public static void Fetch(GlobalScope window, string url, ModuleType type, ModuleFetchOptions options, Element? initiator, Action<FetchedModule?> onComplete, RequestDestination? destinationOverride = null, string? referrer = null, bool isPreload = false)
    {
        var document = window.EnvironmentDocument;
        var eventLoop = window.EventLoop;
        var parsedUrl = UrlParser.Parse(url, baseUrl: null);
        if (parsedUrl is null || !window.CanRunScript)
        {
            eventLoop.QueueTask(() => onComplete(null));
            return;
        }

        var destination = type switch
        {
            ModuleType.Json => RequestDestination.Json,
            ModuleType.Css => RequestDestination.Style,
            ModuleType.Text => RequestDestination.Text,
            _ => destinationOverride ?? RequestDestination.Script,
        };

        // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-worker-script-tree: the static imports of a
        // module worker are fetched with the destination of the worker (worker-src applies) and the environment that created
        // it as fetch client (the client of the job that installs a service worker), whose content security policies apply
        // (the dynamic imports use the environment of the worker)
        Document? policyClient = null;
        if (type == ModuleType.JavaScript && window is WorkerGlobalScope { IsFetchingModuleScriptGraph: true } worker)
        {
            destination = worker.Agent.Kind switch
            {
                WorkerKind.Shared => RequestDestination.SharedWorker,
                WorkerKind.Service => RequestDestination.ServiceWorker,
                _ => RequestDestination.Worker,
            };
            policyClient = worker.Agent.CreatorDocument ?? worker.Agent.ServiceWorker?.UpdateJobClient;
        }

        // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-worklet-script-graph: the modules of a worklet are
        // fetched with the destination of the worklet, and with its document as fetch client (the environment document of
        // the global scope)
        var worklet = (window as WorkletGlobalScope)?.Worklet;
        if (type == ModuleType.JavaScript && worklet is not null)
        {
            destination = worklet.Destination;
        }

        // The data: and blob: URLs are processed by fetch (https://fetch.spec.whatwg.org/#concept-scheme-fetch), the other URLs by the loader
        if (window.Options.DocumentLoader is null && parsedUrl.Scheme is not ("data" or "blob"))
        {
            eventLoop.QueueTask(() => onComplete(null));
            return;
        }

        // A CORS request whose credentials mode is "same-origin" unless the crossorigin attribute is "use-credentials". The
        // cryptographic nonce metadata and the parser metadata are the ones of the fetch options (the content security policies
        // assume that the nonce of an unknown referencing script matches).
        var request = new FetchRequest(parsedUrl)
        {
            Client = document,
            PolicyClient = policyClient,
            Origin = document.SerializeOrigin(),
            Destination = destination,
            Mode = RequestMode.Cors,
            CredentialsMode = options.CredentialsMode ?? (options.IncludeCredentials ? FetchCredentialsMode.Include : FetchCredentialsMode.SameOrigin),
            Initiator = initiator,
            Nonce = options.IsUnknownReferrer ? ContentSecurityPolicy.AnyNonce : options.Nonce,
            ParserInserted = options.ParserInserted,
            RenderBlocking = options.RenderBlocking,
            Referrer = referrer ?? "client",
            ReferrerPolicy = ReferrerPolicies.GetRequestPolicy(options.ReferrerPolicy, document),

            // https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element and
            // https://html.spec.whatwg.org/multipage/webappapis.html#hostloadimportedmodule: the integrity metadata of the
            // integrity attribute of the script element (or of the link element of a module preload), else the one that the
            // import map gives to the URL of the module
            Integrity = (initiator is HtmlScriptElement or HtmlLinkElement ? initiator.GetAttributeValue("integrity") : null) ?? ModuleSpecifierResolution.ResolveIntegrityMetadata(window, parsedUrl.Serialize()),
        };

        if (worklet is not null)
        {
            // The response of a URL is fetched once for the global scopes of the worklet (the module responses map); its
            // timing is added to the performance timeline of the window of the document
            worklet.FetchModuleResponse(
                parsedUrl.Serialize(),
                onFetched =>
                {
                    var timing = document.RelevantWindow is { } documentWindow ? ResourceTiming.Start(documentWindow, request, "script") : null;
                    Fetching.Fetch(
                        request,
                        response =>
                        {
                            if (request.IsSentToLoader && window.CanRunScript)
                            {
                                timing?.Report(response);
                            }

                            onFetched(response.ToDocumentResource());
                        },
                        eventLoop.QueueTask);
                },
                resource => onComplete(CreateModule(resource, url, type)));
            return;
        }

        // The timing of the fetch is added to the performance timeline of the global object: the initiator type of the module
        // scripts and of their imports is "script" (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-initiatortype),
        // and the one of a module preload is "other", as in the browsers
        var timing = ResourceTiming.Start(window, request, isPreload ? "other" : "script");

        // The static imports of a module service worker are stored in its script resource map while it is installed, and
        // read from the map afterwards (https://w3c.github.io/ServiceWorker/#update-algorithm, the perform the fetch hook)
        if (window is ServiceWorkerGlobalScope { IsFetchingModuleScriptGraph: true } serviceWorker)
        {
            serviceWorker.FetchStaticImport(request, timing, response => onComplete(CreateModule(response.ToDocumentResource(), url, type)));
            return;
        }

        Fetching.Fetch(
            request,
            response =>
            {
                if (request.IsSentToLoader && window.CanRunScript)
                {
                    timing?.Report(response);
                }

                onComplete(CreateModule(response.ToDocumentResource(), url, type));
            },
            eventLoop.QueueTask);
    }

    private static FetchedModule? CreateModule(DocumentResource? resource, string url, ModuleType type)
    {
        if (resource is null || !resource.IsOkStatus)
            return null;

        // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-single-module-script: the MIME type of the response
        var essence = resource.MimeType?.Essence ?? "";
        var validMimeType = type switch
        {
            ModuleType.Json => IsJsonMimeType(essence),
            ModuleType.Css => essence == "text/css",
            ModuleType.Text => true,
            _ => HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(essence),
        };

        if (!validMimeType)
            return null;

        // https://encoding.spec.whatwg.org/#utf-8-decode
        var sourceText = resource.Content ?? WebEncodingRegistry.CreateDecoder(WebEncodingId.Utf8).DecodeToString(StripUtf8Bom(resource.ContentBytes!.Value.Span));
        return new FetchedModule(resource.Url ?? url, sourceText, ReferrerPolicies.ParseHeader(resource.Headers));
    }

    private static ReadOnlySpan<byte> StripUtf8Bom(ReadOnlySpan<byte> bytes) => bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes;

    /// <summary>https://mimesniff.spec.whatwg.org/#json-mime-type</summary>
    private static bool IsJsonMimeType(string essence) => essence is "application/json" or "text/json" || essence.EndsWith("+json", StringComparison.Ordinal);
}
