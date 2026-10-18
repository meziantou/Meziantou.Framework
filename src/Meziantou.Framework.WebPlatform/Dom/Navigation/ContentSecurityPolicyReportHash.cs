using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The <c>csp-hash</c> reports (https://w3c.github.io/webappsec-csp/#csp-hash-report): the hash of the body of each script
/// fetched by a document whose policy has a <c>'report-sha256'</c>, <c>'report-sha384'</c> or <c>'report-sha512'</c> source
/// expression in the directive that applies to the script, sent to the endpoint of its <c>report-to</c> directive.
/// </summary>
internal static class ContentSecurityPolicyReportHash
{
    private static readonly string[] ScriptFallbackList = ["script-src-elem", "script-src", "default-src"];
    private static readonly string[] WorkerFallbackList = ["worker-src", "child-src", "script-src", "default-src"];

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#potentially-report-hash, for each policy of the client of a request whose
    /// destination is script-like (called by the post-request check of the script directives,
    /// https://w3c.github.io/webappsec-csp/#script-post-request, on the directive that applies to the request).
    /// </summary>
    public static void PotentiallyReportHash(FetchRequest request, FetchResponse response)
    {
        if (request.Client is not { ContentSecurityPoliciesOrNull: { } policies } client)
            return;

        // The fallback list of the effective directive of the script-like destinations (https://w3c.github.io/webappsec-csp/#directive-fallback-list)
        var fallbackList = request.Destination switch
        {
            RequestDestination.Script or RequestDestination.PaintWorklet => ScriptFallbackList,
            RequestDestination.Worker or RequestDestination.SharedWorker or RequestDestination.ServiceWorker => WorkerFallbackList,
            _ => null,
        };

        if (fallbackList is null)
            return;

        foreach (var policy in policies)
        {
            // The directive that executes for the request (https://w3c.github.io/webappsec-csp/#should-directive-execute)
            List<string>? directive = null;
            foreach (var name in fallbackList)
            {
                if (policy.GetDirectiveValue(name) is { } value)
                {
                    directive = value;
                    break;
                }
            }

            if (directive is not null)
            {
                ReportHash(request, response, client, directive, policy);
            }
        }
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#potentially-report-hash</summary>
    private static void ReportHash(FetchRequest request, FetchResponse response, Document client, List<string> directive, ContentSecurityPolicy policy)
    {
        // Steps 1-5
        var algorithm = "";
        foreach (var expression in directive)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(expression, "'report-sha256'"))
            {
                algorithm = "sha256";
            }
            else if (InfraAscii.EqualsIgnoreAsciiCase(expression, "'report-sha384'"))
            {
                algorithm = "sha384";
            }
            else if (InfraAscii.EqualsIgnoreAsciiCase(expression, "'report-sha512'"))
            {
                algorithm = "sha512";
            }
        }

        if (algorithm.Length == 0)
            return;

        // Steps 8-9: a document of a window, whose policy has a report-to directive (step 11)
        if (client.WorkerGlobal is not null || client.RelevantWindow is not { } window || policy.GetDirectiveValue("report-to") is not [var group, ..])
            return;

        // Steps 6-7: the hash of the body of a CORS-same-origin response
        var hash = "";
        if (response.Type is FetchResponseType.Default or FetchResponseType.Basic or FetchResponseType.Cors)
        {
            var body = response.Body ?? [];
            var digest = algorithm switch
            {
                "sha256" => SHA256.HashData(body),
                "sha384" => SHA384.HashData(body),
                _ => SHA512.HashData(body),
            };

            hash = algorithm + "-" + Convert.ToBase64String(digest);
        }

        // Steps 10, 12-13: the report is generated on the event loop of the window (the fetch may complete on another thread)
        var reportBody = new CspHashReportBody
        {
            DocumentUrl = ContentSecurityPolicyViolation.StripUrlForUseInReports(client.UrlRecord),
            SubresourceUrl = request.Url.Serialize(),
            Hash = hash,
            Destination = FetchMetadata.GetDestinationName(request.Destination),
        };

        window.EventLoop.QueueTask(() => window.GenerateAndQueueReport(reportBody, "csp-hash", group));
    }
}
