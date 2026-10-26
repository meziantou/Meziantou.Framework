using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// The state of the fetches of the @import rules of a style sheet and of its imported style sheets (the critical
/// subresources of the style sheet, https://html.spec.whatwg.org/multipage/semantics.html#critical-subresources).
/// </summary>
internal sealed class CssImportLoadState(Document document)
{
    /// <summary>The document whose load event is delayed by the fetches.</summary>
    public Document Document { get; } = document;

    /// <summary>The number of fetches in progress.</summary>
    public int Pending { get; set; }

    /// <summary>Whether a fetch failed (a network error, or a response that is not a style sheet).</summary>
    public bool Failed { get; set; }

    /// <summary>Canceled when the style sheet is removed: the results of the fetches are ignored.</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>Called once when no fetch is in progress anymore.</summary>
    public Action? Completed { get; set; }
}

/// <summary>The fetch of a style sheet: its request, the response of the fetch algorithm, and the timing of the request.</summary>
internal sealed record StyleSheetFetch(FetchRequest Request, Task<FetchResponse> Response, ResourceTiming? Timing);

/// <summary>Fetches the style sheets of the @import rules with the fetch algorithm (<see cref="Fetching"/>).</summary>
internal static class CssImportFetching
{
    /// <summary>Fetches the style sheets of the @import rules of a style sheet.</summary>
    public static void FetchImports(CssStyleSheet sheet)
    {
        foreach (var rule in sheet.RuleList)
        {
            // The @import rules precede the other rules, except the @layer statements
            if (rule is CssImportRule importRule)
            {
                FetchImport(importRule);
            }
            else if (rule is not CssLayerStatementRule)
            {
                break;
            }
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-cascade-5/#fetch-an-import: the style sheet is fetched when its root style sheet is the
    /// style sheet of a connected element. A document without a browsing context (e.g. created by <c>createHTMLDocument()</c>)
    /// fetches with the environment of the window that created it, as in the browsers (https://github.com/whatwg/html/issues/4547).
    /// When the loader completes synchronously, <see cref="CssImportRule.StyleSheet"/> is set before the method returns.
    /// </summary>
    public static void FetchImport(CssImportRule rule)
    {
        if (rule.ParentStyleSheet is not { } parent || rule.ParentRule is not null || parent.IsConstructed || !rule.SupportsMatches)
            return;

        var root = parent.RootStyleSheet;
        if (root.OwnerNode is not { IsConnected: true } ownerNode || ownerNode.NodeDocument is not { } document || document.EnvironmentDocument is not { BrowsingContext: { } browsingContext } environmentDocument)
            return;

        // https://drafts.csswg.org/css-values-4/#url-empty: an empty URL is an invalid resource (it is not the URL of the style sheet)
        var baseUrl = parent.BaseUrl is { } parentUrl ? UrlParser.Parse(parentUrl, baseUrl: null) : document.DocumentBaseUrl;
        if (rule.Href.Length == 0 || UrlParser.Parse(rule.Href, baseUrl) is not { } parsedUrl)
            return;

        var url = parsedUrl.Serialize();

        // A style sheet that imports itself (directly or not) is not fetched again
        for (var sheet = parent; sheet is not null; sheet = sheet.ParentStyleSheet)
        {
            if (sheet.Href == url)
                return;
        }

        var loadState = root.ImportLoadState ??= new CssImportLoadState(document);
        var modifiers = rule.RequestModifiers;
        // https://drafts.csswg.org/css-values-4/#fetch-a-style-resource: the initiator type is "css" when the style sheet is
        // origin-clean; the resources of the other style sheets are not reported to the performance timeline
        // The referrer of the request is the style sheet of the rule when it is fetched
        if (!TryStartFetch(environmentDocument, browsingContext, parsedUrl, ownerNode as Element, parent.OriginClean ? "css" : null, loadState.Cancellation.Token, out var fetch, modifiers, parent.ResourceReferrer))
            return;

        loadState.Pending++;
        document.StyleSheetImportsDelayingTheLoadEvent++;
        WhenCompleted(fetch, resource =>
        {
            // The result is ignored when the style sheet was removed (e.g. the content of the style element changed)
            if (!loadState.Cancellation.IsCancellationRequested)
            {
                if (resource is not null && TryGetStyleSheetText(document, url, resource, document.Encoding, out var text, out var responseUrl, out var sameOrigin))
                {
                    var importedSheet = new CssStyleSheet(rule, parent, text, url, responseUrl, sameOrigin)
                    {
                        ResourceReferrer = CssResourceReferrer.Create(responseUrl, resource),
                    };
                    rule.StyleSheet = importedSheet;
                    Cascade.CssStyleVersion.Increment(importedSheet);
                    FetchImports(importedSheet);
                }
                else
                {
                    loadState.Failed = true;
                }
            }

            loadState.Pending--;
            document.StyleSheetImportsDelayingTheLoadEvent--;
            document.OnFontEnvironmentChanged();
            document.RequestCssResourcesUpdate();
            if (loadState.Pending == 0 && loadState.Completed is { } callback)
            {
                loadState.Completed = null;
                callback();
            }

            document.TryQueueLoadEvent();
        });
    }

    /// <summary>
    /// Starts the fetch of a style sheet (destination "style") with the fetch algorithm. Returns false when it cannot be
    /// fetched (the URL needs a loader and the browsing context has none). The request URL modifiers of an @import
    /// (https://drafts.csswg.org/css-values-5/#request-url-modifiers) set the mode, the credentials mode, the integrity
    /// metadata and the referrer policy of the request. The request has no cryptographic nonce metadata, and the timing of
    /// a request sent to the loader is reported to the performance timeline of the document by <see cref="WhenCompleted"/>,
    /// unless <paramref name="initiatorType"/> is null. <paramref name="referrer"/> is the referrer of an @import rule of a
    /// fetched style sheet.
    /// </summary>
    internal static bool TryStartFetch(Document document, BrowsingContext browsingContext, UrlRecord url, Element? initiator, string? initiatorType, CancellationToken cancellationToken, [NotNullWhen(true)] out StyleSheetFetch? fetch, CssRequestModifiers? modifiers = null, CssResourceReferrer? referrer = null)
    {
        modifiers ??= CssRequestModifiers.None;
        if (browsingContext.Options.DocumentLoader is null && url.Scheme is not ("data" or "blob"))
        {
            fetch = null;
            return false;
        }

        // https://drafts.csswg.org/css-values-4/#fetch-a-style-resource: a no-cors request includes the credentials, a CORS
        // request (cross-origin()) only includes them for the same origin, unless its credentials mode is "include"
        var request = SubresourceFetching.CreateRequest(document, url, RequestDestination.Style, modifiers.IsCors(defaultCors: false), modifiers.ReferrerPolicy, initiator, useCredentials: modifiers.UsesCredentials, integrity: modifiers.IntegrityMetadata);
        request.Nonce = "";
        referrer?.Apply(request, modifiers.ReferrerPolicy);
        var timing = initiatorType is null ? null : ResourceTiming.Start(document, request, initiatorType);
        fetch = new StyleSheetFetch(request, Fetching.StartFetch(request, cancellationToken), timing);
        return true;
    }

    /// <summary>
    /// Calls <paramref name="complete"/> with the response of a fetch: immediately when the fetch completed synchronously,
    /// otherwise from the task of the event loop in which it completes. The response is null for a network error (see
    /// <see cref="SubresourceFetching.Fetch"/>). The timing of the fetch is reported first.
    /// </summary>
    internal static void WhenCompleted(StyleSheetFetch fetch, Action<DocumentResource?> complete)
    {
        if (fetch.Response.IsCompleted)
        {
            Complete(fetch.Response);
            return;
        }

        _ = fetch.Response.ContinueWith(Complete, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        void Complete(Task<FetchResponse> completedFetch)
        {
            var response = Fetching.GetResult(completedFetch);
            if (fetch.Request.IsSentToLoader)
            {
                fetch.Timing?.Report(response);
            }

            complete(response.ToDocumentResource());
        }
    }

    /// <summary>
    /// The steps that process the response of a style sheet (https://drafts.csswg.org/css-cascade-5/#fetch-an-import,
    /// https://drafts.csswg.org/cssom/#requirements-on-user-agents-implementing-the-xml-stylesheet-processing-instruction):
    /// returns false when the response is not a style sheet. <paramref name="sameOrigin"/> is whether the response is
    /// CORS-same-origin (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#cors-same-origin).
    /// </summary>
    /// <param name="environmentEncoding">The encoding used when neither the response nor the style sheet declares one.</param>
    internal static bool TryGetStyleSheetText(Document document, string url, DocumentResource resource, WebEncodingId environmentEncoding, out string text, out string responseUrl, out bool sameOrigin)
    {
        text = "";
        responseUrl = resource.Url ?? url;
        sameOrigin = resource.IsCorsSameOrigin;

        // The spec does not check the status, but browsers (and the style sheets of the link elements) ignore an error page
        if (!resource.IsOkStatus)
            return false;

        // A response without a valid Content-Type is sniffed as a style sheet. In quirks mode, a same-origin style sheet is
        // processed whatever its MIME type (not a data: URL, whose origin is opaque)
        var mimeType = resource.MimeType;
        if (!resource.IsCssInStyleContext && !(sameOrigin && UrlParser.Parse(url, baseUrl: null) is { } styleSheetUrl && resource.IsStyleSheetQuirkApplicable(styleSheetUrl, document)))
            return false;

        text = resource.Content ?? Decode(resource.ContentBytes!.Value.Span, mimeType, environmentEncoding);
        return true;
    }

    /// <summary>
    /// Decodes a style sheet (https://drafts.csswg.org/css-syntax/#input-byte-stream): the byte order mark, then the charset
    /// parameter of the Content-Type, then the @charset rule, then the encoding of the document.
    /// </summary>
    internal static string Decode(ReadOnlySpan<byte> bytes, MimeType? mimeType, WebEncodingId environmentEncoding)
        => Decode(bytes, mimeType?.GetParameter("charset"), environmentEncoding, out _);

    /// <summary>Decodes a style sheet (https://drafts.csswg.org/css-syntax/#input-byte-stream).</summary>
    /// <param name="protocolEncoding">The encoding label provided by the protocol (e.g. the charset parameter of the Content-Type).</param>
    /// <param name="environmentEncoding">The environment encoding (e.g. the encoding of the document), if any.</param>
    /// <param name="encoding">The encoding used to decode the style sheet.</param>
    internal static string Decode(ReadOnlySpan<byte> bytes, string? protocolEncoding, WebEncodingId? environmentEncoding, out WebEncodingId encoding)
    {
        var bomLength = WebEncodingRegistry.SniffBom(bytes, out encoding);
        if (bomLength == 0)
        {
            encoding = DetermineFallbackEncoding(bytes, protocolEncoding, environmentEncoding);
        }

        return WebEncodingRegistry.CreateDecoder(encoding).DecodeToString(bytes[bomLength..]);
    }

    /// <summary>https://drafts.csswg.org/css-syntax/#determine-the-fallback-encoding</summary>
    private static WebEncodingId DetermineFallbackEncoding(ReadOnlySpan<byte> bytes, string? protocolEncoding, WebEncodingId? environmentEncoding)
    {
        if (protocolEncoding is not null && WebEncodingRegistry.TryGetEncoding(protocolEncoding, out var charsetEncoding))
            return charsetEncoding;

        // @charset "<label>"; at the start of the first 1024 bytes
        var prefix = "@charset \""u8;
        var head = bytes[..Math.Min(bytes.Length, 1024)];
        if (head.StartsWith(prefix))
        {
            var rest = head[prefix.Length..];
            var end = rest.IndexOf("\";"u8);
            if (end >= 0 && !rest[..end].Contains((byte)'"') && WebEncodingRegistry.TryGetEncoding(System.Text.Encoding.Latin1.GetString(rest[..end]), out var ruleEncoding))
                return ruleEncoding is WebEncodingId.Utf16BE or WebEncodingId.Utf16LE ? WebEncodingId.Utf8 : ruleEncoding;
        }

        return environmentEncoding ?? WebEncodingId.Utf8;
    }
}
