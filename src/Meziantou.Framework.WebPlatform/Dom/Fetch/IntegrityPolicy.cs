using System.Text.Json;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An integrity policy (https://w3c.github.io/webappsec-subresource-integrity/#integrity-policy-section): the destinations
/// whose requests must have integrity metadata (and be CORS requests), the sources it applies to (only the <c>inline</c>
/// source, the integrity metadata of the request, is defined), and the endpoints of its violation reports. A document gets
/// it from the <c>Integrity-Policy</c> and <c>Integrity-Policy-Report-Only</c> headers of its response.
/// </summary>
internal sealed class IntegrityPolicy
{
    /// <summary>The empty integrity policy, which blocks nothing.</summary>
    public static IntegrityPolicy Empty { get; } = new([], [], []);

    private IntegrityPolicy(IReadOnlyList<string> sources, IReadOnlyList<RequestDestination> blockedDestinations, IReadOnlyList<string> endpoints)
    {
        Sources = sources;
        BlockedDestinations = blockedDestinations;
        Endpoints = endpoints;
    }

    /// <summary>The sources of the policy: <c>inline</c>, or nothing.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The destinations whose requests are blocked when they have no integrity metadata: <c>script</c> and <c>style</c>.</summary>
    public IReadOnlyList<RequestDestination> BlockedDestinations { get; }

    /// <summary>The names of the reporting endpoints of the violations.</summary>
    public IReadOnlyList<string> Endpoints { get; }

    /// <summary>Whether the policy has no source and no blocked destination.</summary>
    public bool IsEmpty => Sources.Count == 0 && BlockedDestinations.Count == 0;

    /// <summary>Whether the requests of a destination without integrity metadata are blocked (or reported) by the policy.</summary>
    public bool Applies(RequestDestination destination) => Sources.Contains("inline") && BlockedDestinations.Contains(destination);

    /// <summary>The policy of a header of a response, or the empty policy when the header is missing or invalid.</summary>
    public static IntegrityPolicy FromHeaders(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
        => HeaderList.Get(headers, name) is { } value ? Parse(value.Trim(' ', '\t')) : Empty;

    /// <summary>
    /// https://w3c.github.io/webappsec-subresource-integrity/#parse-integrity-policy-headers (steps 3.3 to 3.7): the value
    /// is a structured dictionary whose <c>blocked-destinations</c>, <c>sources</c> and <c>endpoints</c> members are inner
    /// lists of tokens. A value that is not a dictionary blocks nothing, and the members of another type are ignored.
    /// </summary>
    public static IntegrityPolicy Parse(string value)
    {
        // Step 2: a value that is not a dictionary has no member (its source is "inline", and it blocks nothing)
        var dictionary = StructuredFields.ParseDictionary(value) ?? [];

        var sources = new List<string>();
        var blockedDestinations = new List<RequestDestination>();
        var endpoints = new List<string>();
        var hasSources = false;
        foreach (var (key, member) in dictionary)
        {
            switch (key)
            {
                case "sources":
                    hasSources = true;
                    if (GetTokens(member) is { } sourceTokens && sourceTokens.Contains("inline"))
                    {
                        sources.Add("inline");
                    }

                    break;

                case "blocked-destinations":
                    blockedDestinations.Clear();
                    if (GetTokens(member) is { } destinationTokens)
                    {
                        if (destinationTokens.Contains("script"))
                        {
                            blockedDestinations.Add(RequestDestination.Script);
                        }

                        if (destinationTokens.Contains("style"))
                        {
                            blockedDestinations.Add(RequestDestination.Style);
                        }
                    }

                    break;

                case "endpoints":
                    endpoints.Clear();
                    if (GetTokens(member) is { } endpointTokens)
                    {
                        endpoints.AddRange(endpointTokens);
                    }

                    break;
            }
        }

        // Step 3.5: the default source is "inline"
        if (!hasSources)
        {
            sources.Add("inline");
        }

        return new IntegrityPolicy(sources, blockedDestinations, endpoints);

        // The tokens of an inner list (its strings and its other items are not tokens), or null when the member is not an inner list
        static List<string>? GetTokens(StructuredFieldMember member)
        {
            if (member.InnerList is not { } innerList)
                return null;

            var tokens = new List<string>(innerList.Count);
            foreach (var item in innerList)
            {
                if (item.Value is StructuredFieldToken token)
                {
                    tokens.Add(token.Value);
                }
            }

            return tokens;
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-subresource-integrity/#should-request-be-blocked-by-integrity-policy: whether the
    /// integrity policy of the client of a request blocks it (a script or a style sheet without integrity metadata, or whose
    /// request is not a CORS request), and the reports of the violations of its integrity policy and of its report-only
    /// integrity policy. The URLs of a local scheme (about:, blob:, data:) are not blocked.
    /// </summary>
    public static bool ShouldBlockRequest(FetchRequest request)
    {
        if ((request.PolicyClient ?? request.Client) is not { } client)
            return false;

        // Steps 3 and 4
        if ((SubresourceIntegrity.Parse(request.Integrity).Count > 0 && request.Mode is RequestMode.Cors or RequestMode.SameOrigin) || request.CurrentUrl.Scheme is "about" or "blob" or "data")
            return false;

        // Steps 5 to 9
        var policy = client.IntegrityPolicy;
        var reportPolicy = client.ReportOnlyIntegrityPolicy;
        if (policy.IsEmpty && reportPolicy.IsEmpty)
            return false;

        if (client.RelevantWindow is not { } global)
            return false;

        // Steps 10 to 14
        var block = policy.Applies(request.Destination);
        var reportBlock = reportPolicy.Applies(request.Destination);
        if (block || reportBlock)
        {
            ReportViolation(global, client, request, block ? policy : null, reportBlock ? reportPolicy : null);
        }

        return block;
    }

    /// <summary>https://w3c.github.io/webappsec-subresource-integrity/#report-violation: a report for each endpoint of the policies.</summary>
    private static void ReportViolation(GlobalScope global, Document client, FetchRequest request, IntegrityPolicy? policy, IntegrityPolicy? reportPolicy)
    {
        var documentUrl = ContentSecurityPolicyViolation.StripUrlForUseInReports(client.UrlRecord);
        var blockedUrl = ContentSecurityPolicyViolation.StripUrlForUseInReports(request.CurrentUrl);
        var destination = FetchMetadata.GetDestinationName(request.Destination);
        foreach (var (reportedPolicy, reportOnly) in new[] { (policy, false), (reportPolicy, true) })
        {
            if (reportedPolicy is null)
                continue;

            foreach (var endpoint in reportedPolicy.Endpoints)
            {
                var body = new IntegrityViolationReportBody
                {
                    DocumentUrl = documentUrl,
                    BlockedUrl = blockedUrl,
                    Destination = destination,
                    ReportOnly = reportOnly,
                };
                global.QueueReport(body, "integrity-violation", endpoint);
            }
        }
    }
}

/// <summary>
/// The body of the reports of type <c>integrity-violation</c>
/// (https://w3c.github.io/webappsec-subresource-integrity/#report-violation): a request blocked (or reported) by the
/// integrity policy of a document. The bindings give it to the scripts as its JSON serialization.
/// </summary>
internal sealed class IntegrityViolationReportBody : ReportBody
{
    internal override bool IsIdlDictionary => false;

    /// <summary>The URL of the document, stripped for use in reports.</summary>
    public required string DocumentUrl { get; init; }

    /// <summary>The URL of the request, stripped for use in reports.</summary>
    public required string BlockedUrl { get; init; }

    /// <summary>The destination of the request (<c>script</c> or <c>style</c>).</summary>
    public required string Destination { get; init; }

    /// <summary>Whether the violation is of the report-only integrity policy (the request is not blocked).</summary>
    public required bool ReportOnly { get; init; }

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("documentURL", DocumentUrl);
        writer.WriteString("blockedURL", BlockedUrl);
        writer.WriteString("destination", Destination);
        writer.WriteBoolean("reportOnly", ReportOnly);
        writer.WriteEndObject();
    }
}
