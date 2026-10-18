using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>
    /// The global object of the worker whose environment settings object this document represents
    /// (<see cref="GlobalScope.EnvironmentDocument"/>), or null. Such a document is never exposed to the scripts: it is the
    /// client of the fetches of the worker, and holds its URL, its origin and its policies.
    /// </summary>
    internal WorkerGlobalScope? WorkerGlobal { get; private set; }

    /// <summary>Creates the document that represents the environment settings object of a worker.</summary>
    /// <param name="global">The global object of the worker.</param>
    /// <param name="url">The URL of the worker.</param>
    /// <param name="creator">The environment of the creator of the worker, or null for a service worker (whose URL is an HTTP(S) URL).</param>
    internal static Document CreateWorkerEnvironment(WorkerGlobalScope global, UrlRecord url, Document? creator)
    {
        // A worker whose URL is a local scheme inherits the policies of its creator, or of the creator of its blob URL
        var policySource = creator is not null && url.Scheme == "blob" && FileApi.BlobUrlStore.Get(creator).Resolve(url) is { } blobUrlEntry ? blobUrlEntry.Environment : creator;
        var document = new Document(isHtmlDocument: false, contentType: "text/javascript")
        {
            WorkerGlobal = global,
            UrlRecord = url,
            ScriptingEnabled = true,

            // The script of a worker is run only when its response has an ok status
            ResponseStatus = 200,

            // https://html.spec.whatwg.org/multipage/workers.html#run-a-worker: the origin of a worker whose script is a data:
            // URL is a new opaque origin, else the origin of its URL (the origin of the creator for a blob: URL)
            OpaqueOrigin = url.Scheme == "data",

            // https://html.spec.whatwg.org/multipage/workers.html#initialize-worker-policy-container: a worker whose URL is a
            // local scheme inherits the policies; the others get the policies of the response of their script
            // (WorkerGlobalScope.ApplyResponsePolicies)
            ReferrerPolicy = policySource is not null && url.Scheme is "data" or "blob" or "about" ? policySource.ReferrerPolicy : "",
            EmbedderPolicy = policySource?.EmbedderPolicy ?? EmbedderPolicy.Default,
            DocumentIsolationPolicy = policySource?.DocumentIsolationPolicy ?? EmbedderPolicyValue.UnsafeNone,
            IsCredentialless = creator?.IsCredentialless ?? false,
        };

        if (creator is null)
            return document;

        UpgradeInsecureRequests.Inherit(document, creator);

        if (url.Scheme == "blob")
        {
            document.OriginDocument = GetOriginDocument(creator);
        }

        // The CSP list is cloned with its self-origin (https://html.spec.whatwg.org/multipage/workers.html#initialize-worker-policy-container):
        // 'self' matches the origin of the creator (or of the creator of the blob URL), not the opaque origin of a data: worker
        if (url.Scheme is "data" or "blob" or "about" && policySource!.ContentSecurityPoliciesOrNull is { } policies)
        {
            document.ContentSecurityPoliciesOrNull = [.. policies];
            document.ContentSecurityPolicySelfOrigin = policySource.ContentSecurityPolicySelfOrigin;

            // The Trusted Types directives of the policies of the cloned CSP list
            document._trustedTypesDirectives = policySource.TrustedTypesDirectivesOrNull is { Count: > 0 } directives ? [.. directives] : null;
        }

        return document;
    }

    /// <summary>
    /// The document of the window that created the worker this document represents (through its creators, e.g. a worker
    /// created by a worker), or this document (the document of a window, or of a service worker, which has no creator).
    /// </summary>
    internal Document EnvironmentRootDocument
    {
        get
        {
            var document = this;
            while (document.WorkerGlobal is { Agent.CreatorDocument: { } creator })
            {
                document = creator;
            }

            return document;
        }
    }

    /// <summary>
    /// The browsing context of the environment of the document: its browsing context, the one of the window that created the
    /// worker it represents, or the browsing context that owns the group of a service worker. Null when the document is not
    /// fully active (e.g. the window of the creator was discarded).
    /// </summary>
    internal BrowsingContext? EnvironmentBrowsingContext => EnvironmentRootDocument is { WorkerGlobal: { } serviceWorker } ? serviceWorker.Agent.GroupBrowsingContext : EnvironmentRootDocument.BrowsingContext;

    /// <summary>The options of the environment of the document: the options of its browsing context, or of the worker it represents.</summary>
    internal BrowsingContextOptions? EnvironmentOptions => WorkerGlobal?.Options ?? BrowsingContext?.Options;

    /// <summary>The event loop of the environment of the document: the event loop of its browsing context, or of the worker it represents.</summary>
    internal DomEventLoop? EnvironmentEventLoop => WorkerGlobal?.EventLoop ?? BrowsingContext?.EventLoop;
}
