using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The <c>Cross-Origin-Resource-Policy</c> header (https://fetch.spec.whatwg.org/#cross-origin-resource-policy-header) and
/// the embedder policy of the documents (https://html.spec.whatwg.org/multipage/browsers.html#coep): a cross-origin
/// response of a no-cors request is blocked when its policy does not allow the origin of the request, or when it has no
/// policy and the embedder policy of the document requires one.
/// </summary>
internal static class CrossOriginResourcePolicy
{
    /// <summary>https://fetch.spec.whatwg.org/#cross-origin-resource-policy-check</summary>
    public static bool IsBlocked(OriginRecord origin, EmbedderPolicyValue embedderPolicyValue, UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, bool forNavigation, bool requestIncludesCredentials)
    {
        return IsBlockedInternal(origin, EmbedderPolicyValue.UnsafeNone, responseUrl, headers, forNavigation, requestIncludesCredentials)
            || IsBlockedInternal(origin, embedderPolicyValue, responseUrl, headers, forNavigation, requestIncludesCredentials);
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#cross-origin-resource-policy-check, with the violation reports of the embedder policy of
    /// <paramref name="settings"/> (its report-only value is checked too): a response blocked only because of the embedder
    /// policy queues a <c>coep</c> report (<see cref="QueueCorpViolationReport"/>). <paramref name="embedderPolicyValue"/> is
    /// the value that blocks (e.g. the document isolation policy of a document without embedder policy, which is not reported).
    /// </summary>
    public static bool IsBlocked(Document settings, EmbedderPolicyValue embedderPolicyValue, string destination, UrlRecord reportedUrl, UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, bool forNavigation, bool requestIncludesCredentials)
    {
        // Step 3: the violations that are not due to the embedder policy are not reported
        var origin = settings.Origin;
        if (IsBlockedInternal(origin, EmbedderPolicyValue.UnsafeNone, responseUrl, headers, forNavigation, requestIncludesCredentials))
            return true;

        // Step 4
        var policy = settings.EmbedderPolicy;
        if (policy.ReportOnlyValue != EmbedderPolicyValue.UnsafeNone && IsBlockedInternal(origin, policy.ReportOnlyValue, responseUrl, headers, forNavigation, requestIncludesCredentials))
        {
            QueueCorpViolationReport(settings, reportedUrl, destination, reportOnly: true);
        }

        // Steps 5-7
        if (!IsBlockedInternal(origin, embedderPolicyValue, responseUrl, headers, forNavigation, requestIncludesCredentials))
            return false;

        if (policy.Value != EmbedderPolicyValue.UnsafeNone && IsBlockedInternal(origin, policy.Value, responseUrl, headers, forNavigation, requestIncludesCredentials))
        {
            QueueCorpViolationReport(settings, reportedUrl, destination, reportOnly: false);
        }

        return true;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#queue-a-cross-origin-embedder-policy-corp-violation-report: a <c>coep</c> report of type
    /// <c>corp</c> for the reporting endpoint (or the report-only reporting endpoint) of the embedder policy of the settings object.
    /// </summary>
    private static void QueueCorpViolationReport(Document settings, UrlRecord url, string destination, bool reportOnly)
    {
        var policy = settings.EmbedderPolicy;
        var endpoint = reportOnly ? policy.ReportOnlyReportingEndpoint : policy.ReportingEndpoint;
        var body = new EmbedderPolicyReportBody
        {
            Type = "corp",
            BlockedUrl = EmbedderPolicyReportBody.SerializeResponseUrlForReporting(url),
            Destination = destination,
            Disposition = reportOnly ? "reporting" : "enforce",
        };

        (settings.WorkerGlobal ?? (GlobalScope?)settings.RelevantWindow)?.QueueReport(body, "coep", endpoint);
    }

    /// <summary>https://fetch.spec.whatwg.org/#cross-origin-resource-policy-internal-check</summary>
    private static bool IsBlockedInternal(OriginRecord origin, EmbedderPolicyValue embedderPolicyValue, UrlRecord responseUrl, IReadOnlyList<KeyValuePair<string, string>> headers, bool forNavigation, bool requestIncludesCredentials)
    {
        if (forNavigation && embedderPolicyValue == EmbedderPolicyValue.UnsafeNone)
            return false;

        var policy = HeaderList.Get(headers, "Cross-Origin-Resource-Policy");
        if (policy is not ("same-origin" or "same-site" or "cross-origin"))
        {
            policy = embedderPolicyValue switch
            {
                EmbedderPolicyValue.Credentialless when requestIncludesCredentials || forNavigation => "same-origin",
                EmbedderPolicyValue.RequireCorp => "same-origin",
                _ => null,
            };
        }

        var responseOrigin = OriginRecord.FromUrl(responseUrl);
        return policy switch
        {
            "same-origin" => !OriginRecord.IsSameOrigin(origin, responseOrigin),

            // The origin is schemelessly same site with the origin of the response, and an https: origin does not accept an http: response
            "same-site" => !IsSchemelesslySameSite(origin, responseOrigin) || (origin.Scheme == "https" && responseUrl.Scheme != "https"),
            _ => false,
        };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#schemelessly-same-site</summary>
    private static bool IsSchemelesslySameSite(OriginRecord a, OriginRecord b)
    {
        if (a.IsOpaque || b.IsOpaque)
            return ReferenceEquals(a, b);

        return OriginRecord.IsSameSite(a, OriginRecord.CreateTuple(a.Scheme!, b.Host, b.Port));
    }
}
