using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The disposition of a connection allowlist (https://w3c.github.io/connection-allowlists/#connection-allowlist-disposition).</summary>
internal enum ConnectionAllowlistDisposition
{
    /// <summary>The connections that the allowlist does not match are blocked and reported (<c>Connection-Allowlist</c>).</summary>
    Enforce,

    /// <summary>The connections that the allowlist does not match are only reported (<c>Connection-Allowlist-Report-Only</c>).</summary>
    Report,
}

/// <summary>
/// A connection allowlist (https://w3c.github.io/connection-allowlists/#connection-allowlist): the URL patterns of the
/// connections that the environment of a policy container may open (fetches, navigations, WebSockets, ...), whether the
/// redirects and WebRTC are allowed, and the endpoint of its violation reports. A document or a worker gets it from the
/// <c>Connection-Allowlist</c> and <c>Connection-Allowlist-Report-Only</c> headers of its response, and from the
/// <c>connectionallowlist</c> attribute of the iframe that contains it (embedded enforcement).
/// </summary>
internal sealed class ConnectionAllowlist
{
    /// <summary>The maximum length of a valid <c>connectionallowlist</c> attribute (https://w3c.github.io/connection-allowlists/#valid-connectionallowlist-attribute-value).</summary>
    private const int MaxAttributeLength = 4096;

    private ConnectionAllowlist(IReadOnlyList<UrlPattern> patterns, IReadOnlyList<string> serializedPatterns, ConnectionAllowlistDisposition disposition, string? reportingEndpoint, bool allowRedirects, bool allowWebRtc)
    {
        Patterns = patterns;
        SerializedPatterns = serializedPatterns;
        Disposition = disposition;
        ReportingEndpoint = reportingEndpoint;
        AllowRedirects = allowRedirects;
        AllowWebRtc = allowWebRtc;
    }

    /// <summary>The URL patterns of the allowlist (https://w3c.github.io/connection-allowlists/#connection-allowlist-allowlist).</summary>
    public IReadOnlyList<UrlPattern> Patterns { get; }

    /// <summary>The strings the patterns were created from (the origin of the response for <c>response-origin</c>), which the reports list.</summary>
    public IReadOnlyList<string> SerializedPatterns { get; }

    /// <summary>Whether the allowlist blocks the connections it does not match, or only reports them.</summary>
    public ConnectionAllowlistDisposition Disposition { get; }

    /// <summary>The name of the reporting endpoint of the violations (the <c>report-to</c> parameter), or null when they are not reported.</summary>
    public string? ReportingEndpoint { get; }

    /// <summary>Whether the redirects are allowed (<c>redirects=allow</c>): by default, a request that is redirected is blocked.</summary>
    public bool AllowRedirects { get; }

    /// <summary>
    /// Whether WebRTC is allowed (<c>webrtc=allow</c>): by default, it is blocked. The library has no WebRTC, so it only
    /// matters to the embedded enforcement (a required allowlist that blocks WebRTC is satisfied by one that blocks it too).
    /// </summary>
    public bool AllowWebRtc { get; }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#parse-a-responses-connection-allowlists: the allowlists of the
    /// <c>Connection-Allowlist</c> header of a response (enforced), then of its <c>Connection-Allowlist-Report-Only</c>
    /// header (reported), or an empty list.
    /// </summary>
    public static IReadOnlyList<ConnectionAllowlist> FromHeaders(IReadOnlyList<KeyValuePair<string, string>> headers, UrlRecord responseUrl)
    {
        List<ConnectionAllowlist>? allowlists = null;
        if (StructuredFields.GetList(headers, "Connection-Allowlist") is { } list && Parse(list, responseUrl, ConnectionAllowlistDisposition.Enforce) is { } allowlist)
        {
            allowlists = [allowlist];
        }

        if (StructuredFields.GetList(headers, "Connection-Allowlist-Report-Only") is { } reportList && Parse(reportList, responseUrl, ConnectionAllowlistDisposition.Report) is { } reportAllowlist)
        {
            (allowlists ??= []).Add(reportAllowlist);
        }

        return allowlists ?? [];
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#parse-a-serialized-connection-allowlist: the allowlist of a value of the
    /// <c>Connection-Allowlist</c> grammar (e.g. a required allowlist), or null when it is not a list whose first member is
    /// an inner list.
    /// </summary>
    public static ConnectionAllowlist? Parse(string value, UrlRecord? responseUrl, ConnectionAllowlistDisposition disposition)
        => StructuredFields.ParseList(value) is { } list ? Parse(list, responseUrl, disposition) : null;

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#parse-a-connection-allowlist-header: the first member of the list is an
    /// inner list of URL pattern strings (absolute patterns: the ones that are not valid are ignored) and of
    /// <c>response-origin</c> tokens (the origin of the response), whose <c>report-to</c>, <c>redirects</c> and
    /// <c>webrtc</c> parameters are tokens. The other members of the list are ignored.
    /// </summary>
    /// <param name="list">The members of the header.</param>
    /// <param name="responseUrl">The URL of the response, whose origin <c>response-origin</c> stands for, or null to keep the token as the literal pattern <c>response-origin</c> (which is not a valid pattern).</param>
    /// <param name="disposition">The disposition of the allowlist.</param>
    public static ConnectionAllowlist? Parse(IReadOnlyList<StructuredFieldMember> list, UrlRecord? responseUrl, ConnectionAllowlistDisposition disposition)
    {
        // Steps 1 and 2
        if (list is not [{ InnerList: { } items } member, ..])
            return null;

        // Step 4
        var patterns = new List<UrlPattern>(items.Count);
        var serializedPatterns = new List<string>(items.Count);
        foreach (var item in items)
        {
            var serializedPattern = item.Value switch
            {
                StructuredFieldToken { Value: "response-origin" } => responseUrl is null ? null : OriginRecord.FromUrl(responseUrl).Serialize(),
                string value => value,
                _ => null,
            };

            if (serializedPattern is null || CreatePattern(serializedPattern) is not { } pattern)
                continue;

            patterns.Add(pattern);
            serializedPatterns.Add(serializedPattern);
        }

        // Step 5: the parameters whose value is not a token are ignored
        string? reportingEndpoint = null;
        var allowRedirects = false;
        var allowWebRtc = false;
        foreach (var (key, value) in member.InnerListParameters)
        {
            if (value is not StructuredFieldToken token)
                continue;

            switch (key)
            {
                case "report-to":
                    reportingEndpoint = token.Value;
                    break;

                case "redirects":
                    allowRedirects = token.Value != "block";
                    break;

                case "webrtc":
                    allowWebRtc = token.Value != "block";
                    break;
            }
        }

        return new ConnectionAllowlist(patterns, serializedPatterns, disposition, reportingEndpoint, allowRedirects, allowWebRtc);
    }

    /// <summary>
    /// https://urlpattern.spec.whatwg.org/#build-a-url-pattern-from-an-http-structured-field-value without base URL: a
    /// relative pattern is not valid. A pattern whose regular expression is too complex to match the special schemes in time
    /// is not valid either.
    /// </summary>
    private static UrlPattern? CreatePattern(string value)
    {
        try
        {
            return new UrlPattern(value);
        }
        catch (TypeErrorException)
        {
            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#match-a-url-to-a-connection-allowlist: the local URLs (about:, blob:,
    /// data:) are always allowed, the others must match a pattern of the allowlist.
    /// </summary>
    public bool Matches(UrlRecord url)
    {
        if (url.Scheme is "about" or "blob" or "data")
            return true;

        foreach (var pattern in Patterns)
        {
            if (TestPattern(pattern, url))
                return true;
        }

        return false;
    }

    private static bool TestPattern(UrlPattern pattern, UrlRecord url)
    {
        try
        {
            return pattern.Test(url);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#connection-allowlist-subset and
    /// https://w3c.github.io/connection-allowlists/#connection-allowlist-satisfies: every pattern of the allowlist is a
    /// pattern of the required one, and the allowlist blocks the redirects and WebRTC when the required one does.
    /// </summary>
    public bool Satisfies(ConnectionAllowlist required)
    {
        foreach (var pattern in SerializedPatterns)
        {
            if (!required.SerializedPatterns.Contains(pattern, StringComparer.Ordinal))
                return false;
        }

        return (required.AllowRedirects || !AllowRedirects) && (required.AllowWebRtc || !AllowWebRtc);
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#should-request-be-blocked-by-connection-allowlists: whether the
    /// connection allowlists of the policy container of a request block it. A request that is redirected is blocked by the
    /// allowlists that block the redirects; the others check the current URL. The violations are reported with the URL of
    /// the request (the first URL of its URL list), except for a speculative fetch (a preload of the speculative parser).
    /// </summary>
    public static bool ShouldBlockRequest(FetchRequest request)
    {
        if ((request.PolicyClient ?? request.Client) is not { ConnectionAllowlists: { Count: > 0 } allowlists } client)
            return false;

        foreach (var allowlist in allowlists)
        {
            if (request.UrlList.Count > 1)
            {
                if (allowlist.AllowRedirects)
                    continue;
            }
            else if (allowlist.Matches(request.CurrentUrl))
            {
                continue;
            }

            if (!request.IsSpeculative)
            {
                allowlist.ReportViolation(ContentSecurityPolicyViolation.StripUrlForUseInReports(request.Url), client);
            }

            if (allowlist.Disposition == ConnectionAllowlistDisposition.Enforce)
                return true;
        }

        return false;
    }

    /// <summary>Whether an enforced allowlist of a list does not match a URL, without reporting the violations (e.g. the allowlists of an early hints response, which has no environment).</summary>
    public static bool IsBlocked(IReadOnlyList<ConnectionAllowlist> allowlists, UrlRecord url)
    {
        foreach (var allowlist in allowlists)
        {
            if (allowlist.Disposition == ConnectionAllowlistDisposition.Enforce && !allowlist.Matches(url))
                return true;
        }

        return false;
    }

    /// <summary>https://w3c.github.io/connection-allowlists/#should-url-be-blocked-by-connection-allowlists: whether the connection allowlists of an environment block a URL.</summary>
    public static bool ShouldBlockUrl(UrlRecord url, Document? environment)
    {
        if (environment is not { ConnectionAllowlists: { Count: > 0 } allowlists })
            return false;

        foreach (var allowlist in allowlists)
        {
            if (allowlist.Matches(url))
                continue;

            allowlist.ReportViolation(ContentSecurityPolicyViolation.StripUrlForUseInReports(url), environment);
            if (allowlist.Disposition == ConnectionAllowlistDisposition.Enforce)
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#report-a-connection-allowlist-violation: a report of type
    /// <c>connection-allowlist</c> for the reporting endpoint of the allowlist (a violation is not reported when the
    /// allowlist has none).
    /// </summary>
    /// <param name="connection">The URL of the connection, stripped for use in reports.</param>
    /// <param name="environment">The environment of the connection.</param>
    private void ReportViolation(string connection, Document environment)
    {
        if (ReportingEndpoint is null)
            return;

        GlobalScope? global = environment.WorkerGlobal ?? (GlobalScope?)environment.RelevantWindow;
        if (global is null)
            return;

        var body = new ConnectionAllowlistViolationReportBody
        {
            Url = ContentSecurityPolicyViolation.StripUrlForUseInReports(environment.UrlRecord),
            Connection = connection,
            Allowlist = SerializedPatterns,
            Disposition = Disposition,
        };
        global.QueueReport(body, "connection-allowlist", ReportingEndpoint);
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#valid-connectionallowlist-attribute-value: whether the value of the
    /// <c>connectionallowlist</c> attribute of an iframe can be required of its documents: a list whose first member is an
    /// inner list, at least as strict as the allowlist the parent requires.
    /// </summary>
    public static bool IsValidAttributeValue(string value, string? parentRequired)
    {
        if (value.Length is 0 or > MaxAttributeLength)
            return false;

        if (Parse(value, responseUrl: null, ConnectionAllowlistDisposition.Enforce) is not { } allowlist)
            return false;

        // https://w3c.github.io/connection-allowlists/#connection-allowlist-at-least-as-strict: response-origin is compared
        // as the literal pattern string (a required allowlist that is not valid requires nothing)
        if (parentRequired is null || Parse(parentRequired, responseUrl: null, ConnectionAllowlistDisposition.Enforce) is not { } parent)
            return true;

        var candidatePatterns = GetLiteralPatterns(value);
        var parentPatterns = GetLiteralPatterns(parentRequired);
        foreach (var pattern in candidatePatterns)
        {
            if (!parentPatterns.Contains(pattern, StringComparer.Ordinal))
                return false;
        }

        return (parent.AllowRedirects || !allowlist.AllowRedirects) && (parent.AllowWebRtc || !allowlist.AllowWebRtc);

        static List<string> GetLiteralPatterns(string value)
        {
            if (StructuredFields.ParseList(value) is not [{ InnerList: { } items }, ..])
                return [];

            var patterns = new List<string>(items.Count);
            foreach (var item in items)
            {
                switch (item.Value)
                {
                    case StructuredFieldToken { Value: "response-origin" }:
                        patterns.Add("response-origin");
                        break;

                    case string pattern when CreatePattern(pattern) is not null:
                        patterns.Add(pattern);
                        break;
                }
            }

            return patterns;
        }
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#should-be-blocked-by-embedded-enforcement: whether the response of the
    /// navigation of a child navigable does not honor the allowlist its container requires: the response must allow the
    /// origin of the request in its <c>Allow-Connection-Allowlist-From</c> header, or have a <c>Connection-Allowlist</c>
    /// header that satisfies the required allowlist. A required allowlist that is not valid is ignored.
    /// </summary>
    public static bool ShouldBlockByEmbeddedEnforcement(string? requirement, IReadOnlyList<KeyValuePair<string, string>> responseHeaders, UrlRecord responseUrl, string requestOrigin)
    {
        if (requirement is null || Parse(requirement, responseUrl, ConnectionAllowlistDisposition.Enforce) is not { } required)
            return false;

        // https://w3c.github.io/connection-allowlists/#allows-connection-allowlist-from
        if (HeaderList.Get(responseHeaders, "Allow-Connection-Allowlist-From") is { } allowFrom)
        {
            allowFrom = allowFrom.Trim(' ', '\t');
            if (allowFrom == "*" || allowFrom == requestOrigin)
                return false;
        }

        return !(StructuredFields.GetList(responseHeaders, "Connection-Allowlist") is { } list && Parse(list, responseUrl, ConnectionAllowlistDisposition.Enforce) is { } asserted && asserted.Satisfies(required));
    }

    /// <summary>
    /// https://w3c.github.io/connection-allowlists/#apply-a-required-connection-allowlist: the allowlists of a document whose
    /// container requires an allowlist: the required allowlist is enforced, unless an enforced allowlist of the document
    /// already satisfies it.
    /// </summary>
    public static IReadOnlyList<ConnectionAllowlist> ApplyRequired(IReadOnlyList<ConnectionAllowlist> allowlists, string? requirement, UrlRecord responseUrl)
    {
        if (requirement is null || Parse(requirement, responseUrl, ConnectionAllowlistDisposition.Enforce) is not { } required)
            return allowlists;

        foreach (var allowlist in allowlists)
        {
            if (allowlist.Disposition == ConnectionAllowlistDisposition.Enforce && allowlist.Satisfies(required))
                return allowlists;
        }

        return [.. allowlists, required];
    }
}

/// <summary>
/// The body of the reports of type <c>connection-allowlist</c> (https://w3c.github.io/connection-allowlists/#dictdef-connectionallowlistviolationreport):
/// a connection blocked (or reported) by a connection allowlist. The bindings give it to the scripts as its JSON serialization.
/// </summary>
internal sealed class ConnectionAllowlistViolationReportBody : ReportBody
{
    internal override bool IsIdlDictionary => false;

    /// <summary>The creation URL of the environment, stripped for use in reports.</summary>
    public required string Url { get; init; }

    /// <summary>The URL of the connection, stripped for use in reports (the specification also reports hosts and <c>webrtc</c>, which the library does not check).</summary>
    public required string Connection { get; init; }

    /// <summary>The patterns of the allowlist.</summary>
    public required IReadOnlyList<string> Allowlist { get; init; }

    /// <summary>The disposition of the allowlist.</summary>
    public required ConnectionAllowlistDisposition Disposition { get; init; }

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("url", Url);
        writer.WriteString("connection", Connection);
        writer.WriteStartArray("allowlist");
        foreach (var pattern in Allowlist)
        {
            writer.WriteStringValue(pattern);
        }

        writer.WriteEndArray();
        writer.WriteString("disposition", Disposition == ConnectionAllowlistDisposition.Enforce ? "enforce" : "report");
        writer.WriteEndObject();
    }
}
