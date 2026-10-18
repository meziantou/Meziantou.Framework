using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>Fetches the external classic scripts with the fetch algorithm (<see cref="Fetching"/>).</summary>
internal static class ScriptFetching
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-script. When the fetch completes
    /// synchronously (e.g. the loader completes synchronously), the method returns true and <paramref name="result"/> is
    /// the script (null for a network error). Otherwise, <paramref name="onComplete"/> is called with the script from a
    /// task of the event loop.
    /// </summary>
    /// <param name="reportTimingFromTask">
    /// Whether the resource timing of a fetch that completes synchronously is reported by a task (the script is run by a task):
    /// a script that the parser runs at once has its entry in the performance timeline when it runs.
    /// </param>
    public static bool FetchClassicScript(Element element, BrowsingContext browsingContext, Window window, string url, Action<ClassicScript?> onComplete, out ClassicScript? result, bool reportTimingFromTask = false)
    {
        var document = element.NodeDocument;

        // The encoding of the charset attribute, else of the document (https://html.spec.whatwg.org/multipage/scripting.html#script-processing-encoding)
        var fallbackEncoding = element is HtmlScriptElement && element.GetAttributeValue("charset") is { } charset && WebEncodingRegistry.TryGetEncoding(charset, out var charsetEncoding) ? charsetEncoding : document.Encoding;
        if (UrlParser.Parse(url, baseUrl: null) is not { } parsedUrl || (browsingContext.Options.DocumentLoader is null && parsedUrl.Scheme is not ("data" or "blob")))
        {
            result = null;
            return true;
        }

        // A potential-CORS request (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#create-a-potential-cors-request)
        // whose referrer policy is the one of the referrerpolicy attribute (https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element)
        // and whose integrity metadata is the value of the integrity attribute
        var request = SubresourceFetching.CreateRequest(document, parsedUrl, RequestDestination.Script, cors: element.HasAttribute("crossorigin"), (element as HtmlScriptElement)?.ReferrerPolicy ?? "", element, useCredentials: SubresourceFetching.UsesCredentials(element), integrity: (element as HtmlScriptElement)?.GetAttributeValue("integrity") ?? "");
        request.ParserInserted = element is HtmlScriptElement { IsParserInserted: true } or SvgScriptElement { IsParserInserted: true };

        // https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element: "If el is currently render-blocking,
        // then set options's render-blocking to true"
        request.RenderBlocking = document.IsRenderBlocking(element);
        var timing = ResourceTiming.Start(document, request, "script");
        var fetch = Fetching.StartFetch(request, CancellationToken.None);
        if (fetch.IsCompleted)
        {
            result = CreateScript(Fetching.GetResult(fetch), synchronous: true);
            return true;
        }

        _ = fetch.ContinueWith(completed => onComplete(CreateScript(Fetching.GetResult(completed), synchronous: false)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        result = null;
        return false;

        ClassicScript? CreateScript(FetchResponse response, bool synchronous)
        {
            if (request.IsSentToLoader && timing is not null)
            {
                if (synchronous && reportTimingFromTask)
                {
                    timing.ReportFromTask(response);
                }
                else
                {
                    timing.Report(response);
                }
            }

            // A response whose status is not an ok status (e.g. a 404 page) is not executed
            if (response.ToDocumentResource() is not { IsOkStatus: true } resource)
                return null;

            var responseUrl = resource.Url ?? url;
            var sourceText = resource.Content ?? Decode(resource.ContentBytes!.Value.Span, resource.MimeType, fallbackEncoding);

            // The errors of a script whose response is not CORS-same-origin are muted (e.g. a cross-origin script without
            // CORS, even when it is redirected back to the origin of the document)
            return new ClassicScript(window, sourceText, responseUrl) { Url = responseUrl, MutedErrors = !resource.IsCorsSameOrigin, Element = element };
        }
    }

    /// <summary>
    /// Decodes a classic script (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-script): the byte
    /// order mark, then the charset parameter of the Content-Type, then the encoding of the document.
    /// </summary>
    internal static string Decode(ReadOnlySpan<byte> bytes, MimeType? mimeType, WebEncodingId fallbackEncoding)
    {
        var bomLength = WebEncodingRegistry.SniffBom(bytes, out var encoding);
        if (bomLength == 0)
        {
            encoding = LegacyExtractEncoding(mimeType, fallbackEncoding);
        }

        return WebEncodingRegistry.CreateDecoder(encoding).DecodeToString(bytes[bomLength..]);
    }

    /// <summary>https://fetch.spec.whatwg.org/#legacy-extract-an-encoding</summary>
    internal static WebEncodingId LegacyExtractEncoding(MimeType? mimeType, WebEncodingId fallbackEncoding)
        => mimeType?.GetParameter("charset") is { } charset && WebEncodingRegistry.TryGetEncoding(charset, out var encoding) ? encoding : fallbackEncoding;
}
