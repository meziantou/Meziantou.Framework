using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Fetches the subresources of the elements whose response is processed by a callback (the poster of a video, the image
/// of an SVG image element, the style sheet of a link element...) with the fetch algorithm (<see cref="Fetching"/>), whose
/// HTTP requests are sent by the <see cref="DocumentLoader"/> of the browsing context.
/// </summary>
internal static class SubresourceFetching
{
    /// <summary>
    /// Creates the request of a subresource of a document: a potential-CORS request (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#create-a-potential-cors-request)
    /// whose client is the document. The referrer policy is <paramref name="referrerPolicy"/> (the referrerpolicy attribute
    /// of the element), else the policy of <paramref name="policyDocument"/> (the document by default). <paramref name="imageSet"/>
    /// is whether the initiator type is "imageset" (see <see cref="FetchRequest.ImageSetInitiator"/>), and <paramref name="integrity"/>
    /// the integrity metadata of the request.
    /// </summary>
    public static FetchRequest CreateRequest(Document document, UrlRecord url, RequestDestination destination, bool cors, string referrerPolicy, Element? initiator, Document? policyDocument = null, bool speculative = false, bool imageSet = false, bool useCredentials = false, string integrity = "")
    {
        return new FetchRequest(url)
        {
            Client = document,
            Origin = document.SerializeOrigin(),
            Destination = destination,
            Mode = cors ? RequestMode.Cors : RequestMode.NoCors,

            // The credentials mode of a CORS request is "same-origin" unless the CORS settings attribute is "use-credentials"
            CredentialsMode = cors && !useCredentials ? FetchCredentialsMode.SameOrigin : FetchCredentialsMode.Include,
            Initiator = initiator,
            ReferrerPolicy = ReferrerPolicies.GetRequestPolicy(referrerPolicy, policyDocument ?? document),
            IsSpeculative = speculative,
            ImageSetInitiator = imageSet,
            Integrity = integrity,
        };
    }

    /// <summary>Whether the CORS settings attribute of an element is in the "use-credentials" state (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#cors-settings-attribute).</summary>
    public static bool UsesCredentials(Element element) => element.GetAttributeValue("crossorigin") is { } value && Infra.InfraAscii.EqualsIgnoreAsciiCase(value, "use-credentials");

    /// <summary>
    /// Fetches a subresource of a document that has a browsing context. <paramref name="onComplete"/> is called from a task
    /// of the event loop with the response (see <see cref="FetchResponse.ToDocumentResource"/>), or null for a network
    /// error: a request or a response blocked by the fetch algorithm (e.g. mixed content, the content security policies, the
    /// CORS check, the integrity metadata), an exception thrown by the loader, a canceled request. Returns false when nothing
    /// is fetched (the document has no browsing context, or the URL needs a loader and there is none): the callback is not called.
    /// The timing of a request sent to the loader is added to the performance timeline of the document
    /// (https://w3c.github.io/resource-timing/#dfn-mark-resource-timing): its initiator type is <paramref name="initiatorType"/>,
    /// or the local name of the initiator element.
    /// </summary>
    public static bool Fetch(Document document, FetchRequest request, Action<DocumentResource?>? onComplete, string? initiatorType = null, CancellationToken cancellationToken = default)
        => FetchResponse(document, request, onComplete is null ? null : response => onComplete(response.ToDocumentResource()), initiatorType, cancellationToken: cancellationToken);

    /// <summary>
    /// Fetches a subresource of a document as <see cref="Fetch"/> does: <paramref name="onComplete"/> is called with the
    /// response of the fetch algorithm, which is a network error on failure. With <paramref name="reportBlockedRequest"/>,
    /// the timing of a request that is blocked before it is sent to the loader is reported too.
    /// </summary>
    public static bool FetchResponse(Document document, FetchRequest request, Action<FetchResponse>? onComplete, string? initiatorType = null, bool reportBlockedRequest = false, CancellationToken cancellationToken = default)
    {
        if (document.BrowsingContext is not { } browsingContext || (browsingContext.Options.DocumentLoader is null && request.CurrentUrl.Scheme is not ("data" or "blob")))
            return false;

        var timing = ResourceTiming.Start(document, request, initiatorType ?? request.Initiator?.LocalName ?? "other");
        Fetching.Fetch(request, Complete, document.QueueElementTask, cancellationToken);
        return true;

        void Complete(FetchResponse response)
        {
            if (request.IsSentToLoader || reportBlockedRequest)
            {
                timing?.Report(response);
            }

            onComplete?.Invoke(response);
        }
    }
}
