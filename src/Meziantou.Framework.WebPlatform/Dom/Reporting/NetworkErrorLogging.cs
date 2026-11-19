using System.Text.Json;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The Network Error Logging policies of the origins (https://w3c.github.io/network-error-logging/), set by the <c>NEL</c>
/// header of their responses: the requests to an origin that has a policy (or to its subdomains, for a policy with
/// <c>include_subdomains</c>) generate <c>network-error</c> reports, sampled by the <c>success_fraction</c> and
/// <c>failure_fraction</c> of the policy, which are sent to the endpoint named by its <c>report_to</c> member (an
/// endpoint of the <c>Reporting-Endpoints</c> header, or a group of the <c>Report-To</c> header, of the response that set
/// the policy). See <see cref="BrowsingContextOptions.NetworkErrorLogging"/>.
/// </summary>
/// <remarks>
/// <para>
/// A report is generated for each response of the network (the redirects and the <c>304</c> responses of a revalidation
/// included, not the responses of the HTTP cache) and for each request that fails: its type is <c>ok</c>, <c>http.error</c>
/// (a <c>4xx</c> or <c>5xx</c> status), <c>dns.name_not_resolved</c> or <c>tcp.failed</c> (the
/// <see cref="HttpRequestError"/> of a <see cref="HttpRequestException"/> thrown by the <see cref="DocumentLoader"/>), or
/// <c>unknown</c>. The reports are not visible to the <see cref="ReportingObserver"/> objects, and the requests that send
/// them do not generate reports.
/// </para>
/// <para>The cache is thread-safe: it can be shared by several browsing contexts.</para>
/// </remarks>
public sealed class NetworkErrorLoggingCache
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, NetworkErrorLoggingPolicy> _policies = new(StringComparer.Ordinal);

    /// <summary>Gets the number of policies that are not expired.</summary>
    /// <param name="now">The current time.</param>
    public int GetPolicyCount(DateTimeOffset now)
    {
        lock (_lock)
        {
            return _policies.Values.Count(policy => policy.Expires > now);
        }
    }

    /// <summary>Whether the cache has no policy.</summary>
    internal bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _policies.Count == 0;
            }
        }
    }

    /// <summary>Removes the policies of every origin.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _policies.Clear();
        }
    }

    /// <summary>
    /// https://w3c.github.io/network-error-logging/#process-policy-headers: stores or removes the policy of the origin of a
    /// secure response whose headers have a valid <c>NEL</c> header.
    /// </summary>
    internal void ProcessResponse(UrlRecord url, IReadOnlyList<KeyValuePair<string, string>> headers, DateTimeOffset now)
    {
        if (url.Scheme != "https" || HeaderList.Get(headers, "NEL") is not { } value || NetworkErrorLoggingPolicyHeader.Parse(value) is not { } header)
            return;

        var origin = OriginRecord.FromUrl(url).Serialize();
        if (header.MaxAge == 0)
        {
            lock (_lock)
            {
                _policies.Remove(origin);
            }

            return;
        }

        // The endpoint of the policy: the endpoint (or the group) of the response that the report_to member names
        if (ReportingEndpoint.Process(headers, url)?.Find(endpoint => endpoint.Name == header.ReportTo) is not { } endpoint)
            return;

        var lifetime = TimeSpan.FromSeconds(header.MaxAge);
        var expires = now > DateTimeOffset.MaxValue - lifetime ? DateTimeOffset.MaxValue : now + lifetime;
        var policy = new NetworkErrorLoggingPolicy(origin, url.Host?.Serialized ?? "", url.Port, header, endpoint.Url, expires);
        lock (_lock)
        {
            _policies[origin] = policy;
        }
    }

    /// <summary>
    /// https://w3c.github.io/network-error-logging/#choose-a-policy-for-a-request: the policy of the origin of the URL, else
    /// the <c>include_subdomains</c> policy of the closest superdomain with the same scheme and port, or null.
    /// </summary>
    internal (NetworkErrorLoggingPolicy Policy, bool IsSubdomainPolicy)? ChoosePolicy(UrlRecord url, DateTimeOffset now)
    {
        if (url.Host is not { } host)
            return null;

        lock (_lock)
        {
            if (_policies.TryGetValue(OriginRecord.FromUrl(url).Serialize(), out var policy))
            {
                if (policy.Expires > now)
                    return (policy, false);

                _policies.Remove(policy.Origin);
            }

            var domain = host.Serialized;
            while (domain.IndexOf('.', StringComparison.Ordinal) is var dot and >= 0)
            {
                domain = domain[(dot + 1)..];
                foreach (var candidate in _policies.Values)
                {
                    if (candidate.Header.IncludeSubdomains && candidate.Host == domain && candidate.Port == url.Port && candidate.Expires > now && candidate.Origin.StartsWith(url.Scheme + "://", StringComparison.Ordinal))
                        return (candidate, true);
                }
            }
        }

        return null;
    }
}

/// <summary>A NEL policy of an origin (https://w3c.github.io/network-error-logging/#dfn-nel-policy).</summary>
internal sealed record NetworkErrorLoggingPolicy(string Origin, string Host, int? Port, NetworkErrorLoggingPolicyHeader Header, UrlRecord Endpoint, DateTimeOffset Expires);

/// <summary>
/// The members of a <c>NEL</c> header (https://w3c.github.io/network-error-logging/#nel-response-header): a JSON object
/// (the first one of a list of objects separated by commas, as the headers of a response are combined).
/// </summary>
internal sealed record NetworkErrorLoggingPolicyHeader
{
    /// <summary>The name of the endpoint group of the reports (https://w3c.github.io/network-error-logging/#report_to-member).</summary>
    public required string ReportTo { get; init; }

    /// <summary>The maximum lifetime of a policy, in seconds: 10 years. A larger <c>max_age</c> is clamped to it.</summary>
    public const long MaxMaxAge = 10L * 365 * 24 * 60 * 60;

    /// <summary>The lifetime of the policy, in seconds (https://w3c.github.io/network-error-logging/#max_age-member), at most <see cref="MaxMaxAge"/>: 0 removes the policy.</summary>
    public required long MaxAge { get; init; }

    /// <summary>Whether the policy applies to the subdomains (https://w3c.github.io/network-error-logging/#include_subdomains-member).</summary>
    public bool IncludeSubdomains { get; init; }

    /// <summary>The sampling rate of the successful requests, between 0 and 1 (https://w3c.github.io/network-error-logging/#success_fraction-member).</summary>
    public double SuccessFraction { get; init; }

    /// <summary>The sampling rate of the failed requests, between 0 and 1 (https://w3c.github.io/network-error-logging/#failure_fraction-member).</summary>
    public double FailureFraction { get; init; } = 1.0;

    /// <summary>The names of the request headers whose values are reported (https://w3c.github.io/network-error-logging/#request_headers-member).</summary>
    public IReadOnlyList<string> RequestHeaders { get; init; } = [];

    /// <summary>The names of the response headers whose values are reported (https://w3c.github.io/network-error-logging/#response_headers-member).</summary>
    public IReadOnlyList<string> ResponseHeaders { get; init; } = [];

    /// <summary>
    /// https://w3c.github.io/network-error-logging/#process-policy-headers: parses the value of a <c>NEL</c> header, or returns
    /// null when it is not valid: it is not a JSON object, its <c>max_age</c> is not a non-negative integer, its
    /// <c>report_to</c> is not a string (when <c>max_age</c> is not 0), or a member has another type. The fractions are
    /// clamped to [0, 1], <c>max_age</c> to <see cref="MaxMaxAge"/>, and the header names are lowercased.
    /// </summary>
    public static NetworkErrorLoggingPolicyHeader? Parse(string value)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse("[" + value + "]", new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.GetArrayLength() == 0 || document.RootElement[0] is not { ValueKind: JsonValueKind.Object } policy)
                return null;

            if (!policy.TryGetProperty("max_age", out var maxAgeElement) || maxAgeElement.ValueKind != JsonValueKind.Number || !maxAgeElement.TryGetInt64(out var maxAge) || maxAge < 0)
                return null;

            string reportTo;
            if (policy.TryGetProperty("report_to", out var reportToElement))
            {
                if (reportToElement.ValueKind != JsonValueKind.String)
                    return null;

                reportTo = reportToElement.GetString()!;
            }
            else if (maxAge == 0)
            {
                reportTo = "";
            }
            else
            {
                return null;
            }

            var includeSubdomains = false;
            if (policy.TryGetProperty("include_subdomains", out var includeSubdomainsElement))
            {
                if (includeSubdomainsElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return null;

                includeSubdomains = includeSubdomainsElement.GetBoolean();
            }

            if (GetFraction(policy, "success_fraction", 0.0) is not { } successFraction || GetFraction(policy, "failure_fraction", 1.0) is not { } failureFraction)
                return null;

            if (GetHeaderNames(policy, "request_headers") is not { } requestHeaders || GetHeaderNames(policy, "response_headers") is not { } responseHeaders)
                return null;

            return new NetworkErrorLoggingPolicyHeader
            {
                ReportTo = reportTo,
                MaxAge = Math.Min(maxAge, MaxMaxAge),
                IncludeSubdomains = includeSubdomains,
                SuccessFraction = successFraction,
                FailureFraction = failureFraction,
                RequestHeaders = requestHeaders,
                ResponseHeaders = responseHeaders,
            };
        }

        static double? GetFraction(JsonElement policy, string name, double defaultValue)
        {
            if (!policy.TryGetProperty(name, out var element))
                return defaultValue;

            if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var fraction) || !double.IsFinite(fraction))
                return null;

            return Math.Clamp(fraction, 0.0, 1.0);
        }

        static List<string>? GetHeaderNames(JsonElement policy, string name)
        {
            if (!policy.TryGetProperty(name, out var element))
                return [];

            if (element.ValueKind != JsonValueKind.Array)
                return null;

            var names = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    return null;

                names.Add(InfraAscii.ToAsciiLowercase(item.GetString()!));
            }

            return names;
        }
    }
}

/// <summary>The body of a <c>network-error</c> report (https://w3c.github.io/network-error-logging/#generate-a-network-error-report).</summary>
internal sealed class NetworkErrorReportBody : ReportBody
{
    public required string Referrer { get; init; }

    public required double SamplingFraction { get; init; }

    public required string ServerIp { get; init; }

    public required string Protocol { get; init; }

    public required string Method { get; init; }

    public required IReadOnlyList<KeyValuePair<string, string>> RequestHeaders { get; init; }

    public required IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; }

    public required int StatusCode { get; init; }

    public required long ElapsedTime { get; init; }

    public required string Phase { get; init; }

    public required string Type { get; init; }

    internal override bool IsIdlDictionary => false;

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("referrer", Referrer);
        writer.WriteNumber("sampling_fraction", SamplingFraction);
        writer.WriteString("server_ip", ServerIp);
        writer.WriteString("protocol", Protocol);
        writer.WriteString("method", Method);
        WriteHeaders("request_headers", RequestHeaders);
        WriteHeaders("response_headers", ResponseHeaders);
        writer.WriteNumber("status_code", StatusCode);
        writer.WriteNumber("elapsed_time", ElapsedTime);
        writer.WriteString("phase", Phase);
        writer.WriteString("type", Type);
        writer.WriteEndObject();

        void WriteHeaders(string name, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            writer.WriteStartObject(name);
            foreach (var group in headers.GroupBy(header => header.Key, StringComparer.Ordinal))
            {
                writer.WriteStartArray(group.Key);
                foreach (var header in group)
                {
                    writer.WriteStringValue(header.Value);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }
}

/// <summary>The generation and the delivery of the <c>network-error</c> reports (https://w3c.github.io/network-error-logging/#generate-a-network-error-report).</summary>
internal static class NetworkErrorLogging
{
    /// <summary>
    /// Generates the <c>network-error</c> report of a request sent to the network to <paramref name="url"/> with the headers
    /// <paramref name="sentHeaders"/> (<paramref name="resource"/> is null when the request failed), and processes the
    /// <c>NEL</c> header of its response. Runs on the thread of the event loop of
    /// the client of the request.
    /// </summary>
    public static void OnNetworkResponse(FetchRequest request, UrlRecord url, IReadOnlyList<KeyValuePair<string, string>> sentHeaders, DocumentResource? resource, Exception? loaderException, TimeSpan elapsedTime, DateTimeOffset end)
    {
        // The requests that deliver the reports do not generate reports
        var options = request.Navigable?.Options ?? request.Client?.EnvironmentOptions ?? request.KeepaliveBrowsingContext?.Options;
        if (request.Destination == RequestDestination.Report || options?.NetworkErrorLogging is not { } cache)
            return;

        // The environment that sends the report: the client of the request, or the document of the navigable of a navigation
        var client = request.Client ?? request.Navigable?.Document;

        if (resource is not null && resource.Headers is { } responseHeaders)
        {
            cache.ProcessResponse(url, responseHeaders, end);
        }

        if (cache.ChoosePolicy(url, end) is not var (policy, isSubdomainPolicy))
            return;

        var (phase, type) = GetPhaseAndType(resource, loaderException);

        // https://w3c.github.io/network-error-logging/#generate-a-network-error-report: the subdomains of an include_subdomains
        // policy only report the DNS errors, and the URL of their reports is the origin
        if (isSubdomainPolicy && phase != "dns")
            return;

        var samplingFraction = type == "ok" ? policy.Header.SuccessFraction : policy.Header.FailureFraction;
        if (!IsSampled(samplingFraction))
            return;

        var reportUrl = isSubdomainPolicy ? OriginRecord.FromUrl(url).Serialize() + "/" : ContentSecurityPolicyViolation.StripUrlForUseInReports(url);
        var body = new NetworkErrorReportBody
        {
            Referrer = request.Referrer is "no-referrer" or "client" ? "" : request.Referrer,
            SamplingFraction = samplingFraction,
            ServerIp = "",
            Protocol = resource is null ? "" : "http/1.1",
            Method = request.Method,
            RequestHeaders = isSubdomainPolicy ? [] : FilterHeaders(sentHeaders, policy.Header.RequestHeaders),
            ResponseHeaders = isSubdomainPolicy || resource is null ? [] : FilterHeaders(resource.Headers, policy.Header.ResponseHeaders),
            StatusCode = resource?.StatusCode ?? 0,
            ElapsedTime = Math.Max(0, (long)elapsedTime.TotalMilliseconds),
            Phase = phase,
            Type = type,
        };

        var report = new QueuedReport
        {
            Body = body,
            Url = reportUrl,
            UserAgent = options.EffectiveUserAgent,
            Destination = policy.Header.ReportTo,
            Type = "network-error",
            Timestamp = end,
        };

        // The reports are sent from the origin of the request
        if (client is not null)
        {
            GlobalScope.DeliverReports(client, request.Navigable ?? client.BrowsingContext ?? request.KeepaliveBrowsingContext, new ReportingEndpoint(policy.Header.ReportTo, policy.Endpoint), OriginRecord.FromUrl(url).Serialize(), [report], processResponse: null);
        }
    }

    /// <summary>The phase and the type of a report (https://w3c.github.io/network-error-logging/#predefined-network-error-types).</summary>
    private static (string Phase, string Type) GetPhaseAndType(DocumentResource? resource, Exception? loaderException)
    {
        if (resource is not null)
            return ("application", resource.StatusCode is >= 400 and <= 599 ? "http.error" : "ok");

        return (loaderException as HttpRequestException)?.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => ("dns", "dns.name_not_resolved"),
            HttpRequestError.ConnectionError => ("connection", "tcp.failed"),
            HttpRequestError.SecureConnectionError => ("connection", "tls.failed"),
            HttpRequestError.ResponseEnded => ("application", "http.response.invalid.empty"),
            HttpRequestError.InvalidResponse => ("application", "http.protocol.error"),
            _ => ("application", "unknown"),
        };
    }

    private static List<KeyValuePair<string, string>> FilterHeaders(IReadOnlyList<KeyValuePair<string, string>> headers, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return [];

        var result = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in headers)
        {
            var lowercaseName = InfraAscii.ToAsciiLowercase(name);
            if (names.Contains(lowercaseName, StringComparer.Ordinal))
            {
                result.Add(new(lowercaseName, value));
            }
        }

        return result;
    }

#pragma warning disable CA5394 // The sampling of the reports does not need a secure random number generator
    private static bool IsSampled(double fraction) => fraction >= 1.0 || (fraction > 0.0 && Random.Shared.NextDouble() < fraction);
#pragma warning restore CA5394
}
