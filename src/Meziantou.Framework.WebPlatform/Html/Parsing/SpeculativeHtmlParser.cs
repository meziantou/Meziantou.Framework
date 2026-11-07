using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>
/// The speculative HTML parser (https://html.spec.whatwg.org/multipage/parsing.html#speculative-html-parsing): while the
/// parser waits for a parsing-blocking script, the rest of the input is parsed into a copy of the document (see
/// <see cref="HtmlTreeBuilder.CreateSpeculativeParser"/>), and the resources the elements would fetch are requested from
/// the <see cref="DocumentLoader"/> (speculative fetches). The speculative parser runs synchronously, until the end of the
/// input, then parses the characters appended to the input stream while the parser waits (the chunks of a response that
/// arrives over time); a new one starts each time the parser pauses for another script. The scripts of the copy do not run, so the resources are the ones
/// that would be fetched if the blocking script did nothing. The <c>base</c> elements, the <c>&lt;meta name="referrer"&gt;</c>
/// elements and the <c>&lt;meta http-equiv="Content-Security-Policy"&gt;</c> elements found speculatively apply to the
/// subsequent speculative fetches (the <c>&lt;meta name="viewport"&gt;</c> elements are ignored, as in the desktop browsers).
/// </summary>
internal sealed class SpeculativeHtmlParser
{
    private readonly Document _document;
    private readonly Document _speculativeDocument;
    private readonly BrowsingContext _browsingContext;

    // The CSP list of the document, and the policies of the meta elements found speculatively
    private readonly List<ContentSecurityPolicy> _contentSecurityPolicies;

    private SpeculativeHtmlParser(Document document, Document speculativeDocument, BrowsingContext browsingContext)
    {
        _document = document;
        _speculativeDocument = speculativeDocument;
        _browsingContext = browsingContext;
        _contentSecurityPolicies = [.. document.ContentSecurityPoliciesOrNull ?? []];
    }

    // The tree builder of the speculative parser
    private HtmlTreeBuilder? _parser;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#start-the-speculative-html-parser: parses the rest of the input
    /// of <paramref name="parser"/> speculatively, when the browsing context enables it (<see cref="BrowsingContextOptions.SpeculativeParsing"/>).
    /// Returns the speculative parser, which parses the characters appended to the input stream of <paramref name="parser"/>
    /// while it waits (see <see cref="Append"/>), or null when the document is not parsed speculatively.
    /// <paramref name="encoding"/> is the encoding of the URLs, when it is not the encoding of the document (the encoding
    /// is not determined yet).
    /// </summary>
    public static SpeculativeHtmlParser? Start(Document document, HtmlTreeBuilder parser, WebEncodingId? encoding = null)
    {
        if (document.BrowsingContext is not { Options: { SpeculativeParsing: true, DocumentLoader: not null } } browsingContext || document.ExpectsNoLinkedResources || parser.IsStopped)
            return null;

        // The copy of the document has the URL, the encoding and the referrer policy of the document, so the URLs are
        // resolved and the requests are created as by the document
        var speculativeDocument = Document.CreateHtmlDocument();
        speculativeDocument.UrlRecord = document.FallbackBaseUrl;
        speculativeDocument.Encoding = encoding ?? document.Encoding;
        speculativeDocument.Mode = document.Mode;
        speculativeDocument.ReferrerPolicy = document.ReferrerPolicy;
        speculativeDocument.ScriptingEnabled = document.ScriptingEnabled;

        var speculativeParser = new SpeculativeHtmlParser(document, speculativeDocument, browsingContext);
        speculativeParser._parser = parser.CreateSpeculativeParser(speculativeDocument, speculativeParser.OnElementInserted);
        speculativeParser._parser.Run();
        return speculativeParser;
    }

    /// <summary>
    /// Parses the characters appended to the input stream of the parser (the bytes of the response that arrive while the
    /// speculative parser runs), or the end of the file.
    /// </summary>
    public void Append(string text, bool endOfFile)
    {
        var tokenizer = _parser!.Tokenizer;
        if (endOfFile)
        {
            tokenizer.EofInserted = true;
        }

        if (text.Length > 0)
        {
            tokenizer.Insert(positionFromEnd: 0, text);
        }

        _parser.Run();
    }

    /// <summary>Whether a <c>Document-Policy</c> header (a structured field dictionary) enables <c>expect-no-linked-resources</c> (https://github.com/WICG/document-policy/blob/main/document-policy-explainer.md).</summary>
    public static bool HasExpectNoLinkedResources(string documentPolicy) => HasDocumentPolicyFeature(documentPolicy, "expect-no-linked-resources");

    /// <summary>Whether a <c>Document-Policy</c> header (a structured field dictionary) enables a boolean feature (https://wicg.github.io/document-policy/).</summary>
    public static bool HasDocumentPolicyFeature(string documentPolicy, string feature)
    {
        var result = false;
        foreach (var member in documentPolicy.Split(','))
        {
            var parameters = member.Split(';')[0];
            var separator = parameters.IndexOf('=', StringComparison.Ordinal);
            var key = (separator < 0 ? parameters : parameters[..separator]).Trim();
            if (key == feature)
            {
                // A boolean member without value is true; the last member wins
                result = separator < 0 || parameters[(separator + 1)..].Trim() == "?1";
            }
        }

        return result;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#speculative-fetch. An element inserted into the contents of a
    /// template that is not a declarative shadow root is not fetched (its URLs may be templates).
    /// </summary>
    private void OnElementInserted(Element element)
    {
        Node root = element.GetRoot();
        while (root is ShadowRoot shadowRoot)
        {
            root = shadowRoot.Host.GetRoot();
        }

        if (root is not Document)
            return;

        switch (element)
        {
            // https://html.spec.whatwg.org/multipage/semantics.html#attr-meta-http-equiv-content-security-policy
            case HtmlMetaElement meta when meta.ParentNode is HtmlHeadElement && InfraAscii.EqualsIgnoreAsciiCase(meta.GetAttributeValue("http-equiv") ?? "", "content-security-policy") && meta.GetAttributeValue("content") is { Length: > 0 } content:
                _contentSecurityPolicies.Add(ContentSecurityPolicy.Parse(content, reportOnly: false, fromMeta: true));
                break;

            case HtmlScriptElement script:
                FetchScript(script, script.GetAttributeValue("src"), script.GetScriptTypeFromAttributes(), script.HasAttribute("nomodule"), script.ReferrerPolicy);
                break;

            case SvgScriptElement svgScript:
            {
                var type = svgScript.GetAttributeValue("type");
                var isJavaScript = type is not { Length: > 0 } || HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(InfraAscii.StripLeadingAndTrailingAsciiWhitespace(type).ToString());
                FetchScript(svgScript, svgScript.GetAttributeValue("href") ?? svgScript.GetAttributeValue(Namespaces.XLink, "href"), isJavaScript ? ScriptElementType.Classic : null, noModule: false, referrerPolicy: "");
                break;
            }

            case HtmlImageElement image:
            {
                // The lazy images are fetched when they are about to intersect the viewport
                if (InfraAscii.EqualsIgnoreAsciiCase(image.GetAttributeValue("loading") ?? "", "lazy"))
                    break;

                if (image.SelectImageSource(_document).Url is { } source)
                {
                    Fetch(image, Reflection.EncodingParseUrl(_speculativeDocument, source), RequestDestination.Image, image.HasAttribute("crossorigin"), image.ReferrerPolicy, imageSet: image.UsesSrcsetOrPicture());
                }

                break;
            }

            // The style sheets and the preloads (the module preloads and the prefetches are fetched when the parser inserts them)
            case HtmlLinkElement link when link.GetLinkResource(_document, out var mediaMatches) is { Kind: LinkResourceKind.StyleSheet or LinkResourceKind.Preload } resource && mediaMatches:
                Fetch(link, resource.Url, resource.Destination, resource.Cors, link.ReferrerPolicy);
                break;

            case HtmlVideoElement video when video.GetAttributeValue("poster") is { Length: > 0 } poster:
                Fetch(video, Reflection.EncodingParseUrl(_speculativeDocument, poster), RequestDestination.Image, cors: false, referrerPolicy: "");
                break;

            case SvgImageElement svgImage when (svgImage.GetAttributeValue("href") ?? svgImage.GetAttributeValue(Namespaces.XLink, "href")) is { Length: > 0 } href:
                Fetch(svgImage, Reflection.EncodingParseUrl(_speculativeDocument, href), RequestDestination.Image, svgImage.HasAttribute("crossorigin"), referrerPolicy: "");
                break;
        }
    }

    /// <summary>The script that the element would fetch (https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element).</summary>
    private void FetchScript(Element element, string? src, ScriptElementType? type, bool noModule, string referrerPolicy)
    {
        if (src is not { Length: > 0 } || type is not ({ } scriptType and (ScriptElementType.Classic or ScriptElementType.Module)) || (scriptType == ScriptElementType.Classic && noModule))
            return;

        // The scripts are not fetched without a script engine, nor the module scripts when the engine does not support them
        if (_browsingContext.Options is not { ScriptingEnabled: true, ScriptEngine: { } engine } || (scriptType == ScriptElementType.Module && !engine.SupportsModuleScripts))
            return;

        Fetch(element, Reflection.EncodingParseUrl(_speculativeDocument, src), RequestDestination.Script, cors: scriptType == ScriptElementType.Module || element.HasAttribute("crossorigin"), referrerPolicy, parserInserted: true);
    }

    private void Fetch(Element element, UrlRecord? url, RequestDestination destination, bool cors, string referrerPolicy, bool imageSet = false, bool parserInserted = false)
    {
        // The data: and blob: URLs are not requested from the loader. A request blocked by a content security policy is not
        // fetched (the URL is not added to the list of speculative fetch URLs, so that the non-speculative fetch is checked).
        if (url is null || url.Scheme is "data" or "blob" || _document.SpeculativeFetchUrls.Contains(url.Serialize()))
            return;

        if (ContentSecurityPolicy.ShouldBlockRequest(_contentSecurityPolicies, _document.ContentSecurityPolicySelfOrigin, url, destination, element, parserInserted))
            return;

        _document.SpeculativeFetchUrls.Add(url.Serialize());

        // The response is ignored. The referrer policy of the copy of the document applies (e.g. a meta element found speculatively).
        SubresourceFetching.Fetch(_document, SubresourceFetching.CreateRequest(_document, url, destination, cors, referrerPolicy, initiator: null, _speculativeDocument, speculative: true, imageSet, useCredentials: SubresourceFetching.UsesCredentials(element)), onComplete: null);
    }
}
