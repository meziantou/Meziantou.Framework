using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The reports of the accesses between two windows that a report-only opener policy would put in different browsing context
/// groups (https://html.spec.whatwg.org/multipage/browsers.html#coop-check-access-report): the <c>coop</c> reports sent to
/// the report-only reporting endpoints of the opener policies of the top-level documents of the two windows. The report of the
/// window that performs the access has the location of the script, and its reporting observers receive it with the type
/// <c>coop-access-violation</c>; the report of the accessed window is only sent to its endpoint. The types and the members of
/// the reports are the ones of the browsers (e.g. <c>access-from-coop-page-to-openee</c>).
/// </summary>
internal static class OpenerPolicyReporting
{
    /// <summary>Whether the accesses from <paramref name="accessor"/> to <paramref name="accessed"/> may be reported: their top-level browsing contexts are in different virtual browsing context groups.</summary>
    public static bool ShouldCheckAccess(BrowsingContext accessor, BrowsingContextInstance accessed)
    {
        if (accessed.IsDiscarded)
            return false;

        var accessorTop = accessor.Top;
        var accessedTop = accessed.Navigable.Top;
        if (ReferenceEquals(accessorTop, accessedTop) || accessorTop.ActiveBrowsingContext.VirtualGroupId == accessedTop.ActiveBrowsingContext.VirtualGroupId)
            return false;

        return accessorTop.Document.OpenerPolicy.ReportOnlyReportingEndpoint is not null || accessedTop.Document.OpenerPolicy.ReportOnlyReportingEndpoint is not null;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsers.html#coop-check-access-report: reports the access to the property
    /// <paramref name="propertyName"/> (a cross-origin accessible window property name, <c>indexed</c> or <c>named</c>) of
    /// the window of <paramref name="accessed"/> by the code of the window of <paramref name="accessor"/>.
    /// </summary>
    public static void CheckAccess(BrowsingContext accessor, BrowsingContextInstance accessed, string propertyName)
    {
        if (!ShouldCheckAccess(accessor, accessed))
            return;

        var accessedNavigable = accessed.Navigable;
        var accessorDocument = accessor.Document;
        var accessedDocument = accessedNavigable.Document;
        if (!accessorDocument.IsFullyActive || !accessedDocument.IsFullyActive)
            return;

        // The documents of the windows and of their ancestors are same origin with their top-level documents: the reports do
        // not reveal cross-origin frames
        var accessorTop = accessor.Top;
        var accessedTop = accessedNavigable.Top;
        var accessorTopDocument = accessorTop.Document;
        var accessedTopDocument = accessedTop.Document;
        if (!AreAncestorsSameOrigin(accessor, accessorTopDocument.Origin) || !AreAncestorsSameOrigin(accessedNavigable, accessedTopDocument.Origin))
            return;

        var accessorCoop = accessorTopDocument.OpenerPolicy;
        var accessedCoop = accessedTopDocument.OpenerPolicy;
        var accessorOrigin = accessorDocument.Origin;
        var accessedOrigin = accessedDocument.Origin;
        var accessorUrl = accessorDocument.UrlRecord;
        var accessedUrl = accessedDocument.UrlRecord;
        var isSameOrigin = OriginRecord.IsSameOrigin(accessorOrigin, accessedOrigin);

        // The location of the script that performs the access (the engine is queried from the thread of the event loop of the accessor)
        var location = accessorDocument.RelevantWindow?.ScriptEngine?.GetCurrentScriptLocation();
        var sourceFile = location is not { } scriptLocation ? null : UrlParser.Parse(scriptLocation.Url, baseUrl: null) is { } sourceUrl ? ContentSecurityPolicyViolation.StripUrlForUseInReports(sourceUrl) : scriptLocation.Url;

        if (accessedTop.Opener is { } accessedOpener && IsInclusiveAncestor(accessedOpener, accessor))
        {
            // The accessor is the opener
            QueueAccessorReport(accessorTopDocument, accessorCoop, new OpenerPolicyReportBody
            {
                Type = "access-from-coop-page-to-openee",
                Disposition = "reporting",
                EffectivePolicy = OpenerPolicy.Serialize(accessorCoop.ReportOnlyValue),
                Property = propertyName,
                OpeneeUrl = isSameOrigin ? OpenerPolicyReportBody.Sanitize(accessedUrl) : "",
                InitialPopupUrl = GetInitialUrl(accessedTop, accessorOrigin),
                SourceFile = sourceFile,
                LineNumber = location?.LineNumber,
                ColumnNumber = location?.ColumnNumber,
            });

            QueueAccessedReport(accessedTopDocument, accessedCoop, new OpenerPolicyReportBody
            {
                Type = "access-to-coop-page-from-opener",
                Disposition = "reporting",
                EffectivePolicy = OpenerPolicy.Serialize(accessedCoop.ReportOnlyValue),
                Property = propertyName,
                OpenerUrl = isSameOrigin ? OpenerPolicyReportBody.Sanitize(accessorUrl) : "",
                Referrer = accessedTopDocument.Referrer,
            });
        }
        else if (accessorTop.Opener is { } accessorOpener && IsInclusiveAncestor(accessorOpener, accessedNavigable))
        {
            // The accessor is the openee
            QueueAccessorReport(accessorTopDocument, accessorCoop, new OpenerPolicyReportBody
            {
                Type = "access-from-coop-page-to-opener",
                Disposition = "reporting",
                EffectivePolicy = OpenerPolicy.Serialize(accessorCoop.ReportOnlyValue),
                Property = propertyName,
                OpenerUrl = isSameOrigin ? OpenerPolicyReportBody.Sanitize(accessedUrl) : "",
                Referrer = accessorTopDocument.Referrer,
                SourceFile = sourceFile,
                LineNumber = location?.LineNumber,
                ColumnNumber = location?.ColumnNumber,
            });

            QueueAccessedReport(accessedTopDocument, accessedCoop, new OpenerPolicyReportBody
            {
                Type = "access-to-coop-page-from-openee",
                Disposition = "reporting",
                EffectivePolicy = OpenerPolicy.Serialize(accessedCoop.ReportOnlyValue),
                Property = propertyName,
                OpeneeUrl = isSameOrigin ? OpenerPolicyReportBody.Sanitize(accessorUrl) : "",
                InitialPopupUrl = GetInitialUrl(accessorTop, accessedOrigin),
            });
        }
        else
        {
            QueueAccessorReport(accessorTopDocument, accessorCoop, new OpenerPolicyReportBody
            {
                Type = "access-from-coop-page-to-other",
                Disposition = "reporting",
                EffectivePolicy = OpenerPolicy.Serialize(accessorCoop.ReportOnlyValue),
                Property = propertyName,
                OtherDocumentUrl = isSameOrigin ? OpenerPolicyReportBody.Sanitize(accessedUrl) : "",
                SourceFile = sourceFile,
                LineNumber = location?.LineNumber,
                ColumnNumber = location?.ColumnNumber,
            });

            QueueAccessedReport(accessedTopDocument, accessedCoop, new OpenerPolicyReportBody
            {
                Type = "access-to-coop-page-from-other",
                Disposition = "reporting",
                EffectivePolicy = OpenerPolicy.Serialize(accessedCoop.ReportOnlyValue),
                Property = propertyName,
                OtherDocumentUrl = isSameOrigin ? OpenerPolicyReportBody.Sanitize(accessorUrl) : "",
            });
        }

        // The initial URL of a popup, when the opener origin at creation of the popup is the origin of the reporting document
        static string GetInitialUrl(BrowsingContext popup, OriginRecord reportingOrigin)
        {
            var instance = popup.ActiveBrowsingContext;
            return instance.InitialUrl is { } initialUrl && instance.OpenerOriginAtCreation is { } openerOrigin && OriginRecord.IsSameOrigin(reportingOrigin, openerOrigin)
                ? OpenerPolicyReportBody.Sanitize(initialUrl)
                : "";
        }
    }

    /// <summary>
    /// Queues the report of the window that performs the access: its reporting observers receive it synchronously (the access
    /// runs on the event loop of the window).
    /// </summary>
    private static void QueueAccessorReport(Document topDocument, OpenerPolicy coop, OpenerPolicyReportBody body)
    {
        if (coop.ReportOnlyReportingEndpoint is not { } endpoint || topDocument.RelevantWindow is not { } window)
            return;

        window.GenerateAndQueueReport(body, "coop", endpoint, observedType: "coop-access-violation");
    }

    /// <summary>Queues the report of the accessed window, from a task of its event loop: its reporting observers do not receive it.</summary>
    private static void QueueAccessedReport(Document topDocument, OpenerPolicy coop, OpenerPolicyReportBody body)
    {
        if (coop.ReportOnlyReportingEndpoint is not { } endpoint || topDocument.RelevantWindow is not { } window)
            return;

        window.EventLoop.QueueTask(() => window.GenerateAndQueueReport(body, "coop", endpoint, observedType: null));
    }

    private static bool IsInclusiveAncestor(BrowsingContext ancestor, BrowsingContext navigable)
    {
        for (var current = navigable; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static bool AreAncestorsSameOrigin(BrowsingContext navigable, OriginRecord topOrigin)
    {
        for (var current = navigable; current is not null; current = current.Parent)
        {
            if (!OriginRecord.IsSameOrigin(current.Document.Origin, topOrigin))
                return false;
        }

        return true;
    }
}
