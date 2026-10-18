using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/browsers.html#cross-origin-opener-policy-value</summary>
internal enum OpenerPolicyValue
{
    UnsafeNone,
    SameOriginAllowPopups,
    SameOrigin,
    SameOriginPlusCoep,
    NoopenerAllowPopups,
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/browsers.html#cross-origin-opener-policy: the policy of the
/// <c>Cross-Origin-Opener-Policy</c> and <c>Cross-Origin-Opener-Policy-Report-Only</c> headers of a top-level document.
/// The reporting endpoints are parsed, but no report is sent.
/// </summary>
internal sealed record OpenerPolicy(
    OpenerPolicyValue Value = OpenerPolicyValue.UnsafeNone,
    string? ReportingEndpoint = null,
    OpenerPolicyValue ReportOnlyValue = OpenerPolicyValue.UnsafeNone,
    string? ReportOnlyReportingEndpoint = null)
{
    public static OpenerPolicy Default { get; } = new();

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#obtain-coop</summary>
    public static OpenerPolicy Obtain(IReadOnlyList<KeyValuePair<string, string>> headers, bool isSecureContext)
    {
        if (!isSecureContext)
            return Default;

        var policy = Default;
        if (StructuredFields.GetItem(headers, "Cross-Origin-Opener-Policy") is { } item)
        {
            OpenerPolicyValue? value = (item.Value as StructuredFieldToken?)?.Value switch
            {
                "same-origin" => EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(EmbedderPolicy.Obtain(headers, isSecureContext).Value) ? OpenerPolicyValue.SameOriginPlusCoep : OpenerPolicyValue.SameOrigin,
                "same-origin-allow-popups" => OpenerPolicyValue.SameOriginAllowPopups,
                "noopener-allow-popups" => OpenerPolicyValue.NoopenerAllowPopups,
                _ => null,
            };

            if (value is { } openerPolicyValue)
            {
                policy = policy with { Value = openerPolicyValue };
            }

            if (item.GetParameter("report-to") is string endpoint)
            {
                policy = policy with { ReportingEndpoint = endpoint };
            }
        }

        if (StructuredFields.GetItem(headers, "Cross-Origin-Opener-Policy-Report-Only") is { } reportOnlyItem)
        {
            OpenerPolicyValue? value = (reportOnlyItem.Value as StructuredFieldToken?)?.Value switch
            {
                "same-origin" => EmbedderPolicy.Obtain(headers, isSecureContext) is var coep && (EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(coep.Value) || EmbedderPolicy.IsCompatibleWithCrossOriginIsolation(coep.ReportOnlyValue))
                    ? OpenerPolicyValue.SameOriginPlusCoep
                    : OpenerPolicyValue.SameOrigin,
                "same-origin-allow-popups" => OpenerPolicyValue.SameOriginAllowPopups,
                _ => null,
            };

            if (value is { } reportOnlyValue)
            {
                policy = policy with { ReportOnlyValue = reportOnlyValue };
            }

            if (reportOnlyItem.GetParameter("report-to") is string endpoint)
            {
                policy = policy with { ReportOnlyReportingEndpoint = endpoint };
            }
        }

        return policy;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#matching-coop</summary>
    public static bool Match(OpenerPolicyValue documentCoop, OriginRecord documentOrigin, OpenerPolicyValue responseCoop, OriginRecord responseOrigin)
    {
        if (documentCoop == OpenerPolicyValue.UnsafeNone && responseCoop == OpenerPolicyValue.UnsafeNone)
            return true;

        if (documentCoop == OpenerPolicyValue.UnsafeNone || responseCoop == OpenerPolicyValue.UnsafeNone)
            return false;

        return documentCoop == responseCoop && OriginRecord.IsSameOrigin(documentOrigin, responseOrigin);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#check-browsing-context-group-switch-coop-value</summary>
    public static bool RequiresBrowsingContextGroupSwitch(bool isInitialAboutBlank, OriginRecord responseOrigin, OriginRecord activeDocumentNavigationOrigin, OpenerPolicyValue responseCoopValue, OpenerPolicyValue activeDocumentCoopValue)
    {
        if (isInitialAboutBlank)
        {
            // https://html.spec.whatwg.org/multipage/browsers.html#check-browsing-context-group-switch-coop-value-popup
            if (responseCoopValue == OpenerPolicyValue.NoopenerAllowPopups)
                return true;

            if (activeDocumentCoopValue is OpenerPolicyValue.SameOriginAllowPopups or OpenerPolicyValue.NoopenerAllowPopups && responseCoopValue == OpenerPolicyValue.UnsafeNone)
                return false;
        }

        return !Match(activeDocumentCoopValue, activeDocumentNavigationOrigin, responseCoopValue, responseOrigin);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#check-bcg-switch-navigation-report-only</summary>
    public static bool ReportOnlyRequiresBrowsingContextGroupSwitch(bool isInitialAboutBlank, OriginRecord responseOrigin, OriginRecord activeDocumentNavigationOrigin, OpenerPolicy responseCoop, OpenerPolicy activeDocumentCoop)
    {
        if (!RequiresBrowsingContextGroupSwitch(isInitialAboutBlank, responseOrigin, activeDocumentNavigationOrigin, responseCoop.ReportOnlyValue, activeDocumentCoop.ReportOnlyValue))
            return false;

        return RequiresBrowsingContextGroupSwitch(isInitialAboutBlank, responseOrigin, activeDocumentNavigationOrigin, responseCoop.Value, activeDocumentCoop.ReportOnlyValue)
            || RequiresBrowsingContextGroupSwitch(isInitialAboutBlank, responseOrigin, activeDocumentNavigationOrigin, responseCoop.ReportOnlyValue, activeDocumentCoop.Value);
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/browsers.html#coop-enforcement-result</summary>
internal sealed record OpenerPolicyEnforcementResult(
    UrlRecord Url,
    OriginRecord Origin,
    OpenerPolicy OpenerPolicy,
    bool NeedsBrowsingContextGroupSwitch = false,
    bool WouldNeedBrowsingContextGroupSwitchDueToReportOnly = false,
    bool CurrentContextIsNavigationSource = false)
{
    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#coop-enforce (without the violation reports)</summary>
    public OpenerPolicyEnforcementResult Enforce(bool isInitialAboutBlank, UrlRecord responseUrl, OriginRecord responseOrigin, OpenerPolicy responseCoop)
    {
        var result = this with { Url = responseUrl, Origin = responseOrigin, OpenerPolicy = responseCoop, CurrentContextIsNavigationSource = true };
        if (OpenerPolicy.RequiresBrowsingContextGroupSwitch(isInitialAboutBlank, responseOrigin, Origin, responseCoop.Value, OpenerPolicy.Value))
        {
            result = result with { NeedsBrowsingContextGroupSwitch = true };
        }

        if (OpenerPolicy.ReportOnlyRequiresBrowsingContextGroupSwitch(isInitialAboutBlank, responseOrigin, Origin, responseCoop, OpenerPolicy))
        {
            result = result with { WouldNeedBrowsingContextGroupSwitchDueToReportOnly = true };
        }

        return result;
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/browsers.html#embedder-policy-value</summary>
internal enum EmbedderPolicyValue
{
    UnsafeNone,
    RequireCorp,
    Credentialless,
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/browsers.html#embedder-policy: the policy of the <c>Cross-Origin-Embedder-Policy</c>
/// and <c>Cross-Origin-Embedder-Policy-Report-Only</c> headers. The violations are reported to the reporting endpoints
/// (<c>coep</c> reports, see <see cref="QueueInheritanceViolation"/> and <see cref="CrossOriginResourcePolicy"/>).
/// </summary>
internal sealed record EmbedderPolicy(
    EmbedderPolicyValue Value = EmbedderPolicyValue.UnsafeNone,
    string? ReportingEndpoint = null,
    EmbedderPolicyValue ReportOnlyValue = EmbedderPolicyValue.UnsafeNone,
    string? ReportOnlyReportingEndpoint = null)
{
    public static EmbedderPolicy Default { get; } = new();

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#compatible-with-cross-origin-isolation</summary>
    public static bool IsCompatibleWithCrossOriginIsolation(EmbedderPolicyValue value) => value is EmbedderPolicyValue.RequireCorp or EmbedderPolicyValue.Credentialless;

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#obtain-an-embedder-policy</summary>
    public static EmbedderPolicy Obtain(IReadOnlyList<KeyValuePair<string, string>> headers, bool isSecureContext)
    {
        if (!isSecureContext)
            return Default;

        var policy = Default;
        if (StructuredFields.GetItem(headers, "Cross-Origin-Embedder-Policy") is { } item && Parse(item) is { } value)
        {
            policy = policy with { Value = value, ReportingEndpoint = item.GetParameter("report-to") as string };
        }

        if (StructuredFields.GetItem(headers, "Cross-Origin-Embedder-Policy-Report-Only") is { } reportOnlyItem && Parse(reportOnlyItem) is { } reportOnlyValue)
        {
            policy = policy with { ReportOnlyValue = reportOnlyValue, ReportOnlyReportingEndpoint = reportOnlyItem.GetParameter("report-to") as string };
        }

        return policy;

        static EmbedderPolicyValue? Parse(StructuredFieldItem item) => (item.Value as StructuredFieldToken?)?.Value switch
        {
            "require-corp" => EmbedderPolicyValue.RequireCorp,
            "credentialless" => EmbedderPolicyValue.Credentialless,
            _ => null,
        };
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsers.html#queue-a-cross-origin-embedder-policy-inheritance-violation: a
    /// <c>coep</c> report of a document (type <c>navigation</c>) or a dedicated worker (type <c>worker initialization</c>)
    /// whose embedder policy is not compatible with the one of its container document or of its owner.
    /// </summary>
    public static void QueueInheritanceViolation(UrlRecord responseUrl, string type, string? endpoint, string disposition, Document settings)
    {
        var body = new EmbedderPolicyReportBody
        {
            Type = type,
            BlockedUrl = EmbedderPolicyReportBody.SerializeResponseUrlForReporting(responseUrl),
            Disposition = disposition,
        };

        (settings.WorkerGlobal ?? (GlobalScope?)settings.RelevantWindow)?.QueueReport(body, "coep", endpoint);
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#cross-origin-isolation-mode</summary>
internal enum CrossOriginIsolationMode
{
    None,
    Logical,
    Concrete,
}

/// <summary>
/// A browsing context group (https://html.spec.whatwg.org/multipage/document-sequences.html#browsing-context-group): the
/// top-level browsing contexts that can reach each other (by their names or their openers). A navigation whose opener
/// policy requires it moves its navigable to a new group.
/// </summary>
internal sealed class BrowsingContextGroup
{
    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#bcg-cross-origin-isolation</summary>
    public CrossOriginIsolationMode CrossOriginIsolationMode { get; init; }
}
