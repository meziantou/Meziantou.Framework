using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;
using Meziantou.Framework.WebPlatform.Xml;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>
/// Loads the stylesheets of xsl:include and xsl:import, and the documents of document(), with the fetch of a document: the
/// requests are synchronous, same-origin (as in the browsers, a cross-origin stylesheet or document is not loaded) and checked
/// by the content security policies of the document. The stylesheets have the destination <c>xslt</c>, a script-like
/// destination (<c>script-src-elem</c>); as in Chromium, the documents of document() are checked by <c>connect-src</c>.
/// </summary>
internal sealed class XsltDocumentLoader(Document document, bool parserInserted = false) : IXsltDocumentLoader
{
    public Document? Load(UrlRecord url, XsltLoadPurpose purpose)
    {
        if (document.BrowsingContext is not { } browsingContext || (browsingContext.Options.DocumentLoader is null && url.Scheme is not ("data" or "blob")))
            return null;

        var request = SubresourceFetching.CreateRequest(document, url, purpose == XsltLoadPurpose.Stylesheet ? RequestDestination.Xslt : RequestDestination.Empty, cors: false, referrerPolicy: "", initiator: null);
        request.Mode = RequestMode.SameOrigin;
        request.CredentialsMode = FetchCredentialsMode.SameOrigin;
        request.Synchronous = true;
        request.ParserInserted = parserInserted;
        FetchResponse response;
        try
        {
            response = Fetching.FetchAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (response.IsNetworkError || response.ToDocumentResource() is not { IsOkStatus: true } resource)
            return null;

        string text;
        try
        {
            text = resource.Content ?? XmlByteDecoder.Decode(resource.ContentBytes!.Value.Span);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        return Parse(text, resource.Url ?? url.Serialize());
    }

    /// <summary>Parses a loaded XML document; returns null when it is not well-formed.</summary>
    internal static Document? Parse(string text, string url)
    {
        var result = XmlDocumentParser.CreateDocument(new XmlParserOptions { Url = url, ContentType = "application/xml" });
        return XmlTreeBuilder.Parse(result, result, text) is null ? result : null;
    }
}
