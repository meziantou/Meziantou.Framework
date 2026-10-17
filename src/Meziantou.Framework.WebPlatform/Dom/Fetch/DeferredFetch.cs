using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://fetch.spec.whatwg.org/#deferred-fetch-record-invoke-state</summary>
internal enum DeferredFetchInvokeState
{
    Pending,
    Sent,
    Aborted,
}

/// <summary>
/// A deferred fetch record (https://fetch.spec.whatwg.org/#deferred-fetch-record): the request of a <c>fetchLater()</c> call,
/// fetched when the fetch group of its document is terminated, or after its <c>activateAfter</c> delay.
/// </summary>
internal sealed class DeferredFetchRecord(FetchRequest request, Action notifyInvoked)
{
    public FetchRequest Request { get; } = request;

    /// <summary>https://fetch.spec.whatwg.org/#deferred-fetch-record-notify-invoked</summary>
    public Action NotifyInvoked { get; } = notifyInvoked;

    public DeferredFetchInvokeState InvokeState { get; set; }

    /// <summary>The total request length (https://fetch.spec.whatwg.org/#total-request-length), computed when the record is created.</summary>
    public long TotalRequestLength { get; init; }
}

/// <summary>
/// Deferred fetching (https://fetch.spec.whatwg.org/#deferred-fetch): the records of <c>fetchLater()</c>, and their quota,
/// which the top-level document shares with the documents of its origin and delegates to the cross-origin iframes with the
/// "deferred-fetch" and "deferred-fetch-minimal" policy-controlled features.
/// </summary>
internal static class DeferredFetch
{
    private const long Kibibyte = 1024;

    /// <summary>The quota of a top-level traversable.</summary>
    private const long TopLevelQuota = 640 * Kibibyte;

    /// <summary>https://fetch.spec.whatwg.org/#quota-reserved-for-deferred-fetch-minimal</summary>
    private const long QuotaReservedForMinimal = 128 * Kibibyte;

    /// <summary>https://fetch.spec.whatwg.org/#reserved-deferred-fetch-quota-minimal-quota</summary>
    private const long MinimalQuota = 8 * Kibibyte;

    /// <summary>https://fetch.spec.whatwg.org/#reserved-deferred-fetch-quota-normal-quota</summary>
    private const long NormalQuota = 64 * Kibibyte;

    /// <summary>The quota of the requests to an origin.</summary>
    private const long QuotaForRequestOrigin = 64 * Kibibyte;

    // https://fetch.spec.whatwg.org/#reserved-deferred-fetch-quota: of the navigable containers (0 when they have no entry)
    private static readonly ConditionalWeakTable<Element, StrongBox<long>> ReservedQuotas = [];

    private static long GetReservedQuota(Element container) => ReservedQuotas.TryGetValue(container, out var quota) ? quota.Value : 0;

    private static void SetReservedQuota(Element container, long value)
    {
        if (value == 0)
        {
            ReservedQuotas.Remove(container);
        }
        else
        {
            ReservedQuotas.GetValue(container, static _ => new StrongBox<long>()).Value = value;
        }
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#total-request-length: the lengths of the URL without fragment, of the referrer, of the
    /// names and the values of the headers, and of the body.
    /// </summary>
    public static long ComputeTotalRequestLength(FetchRequest request, Document client)
    {
        long length = request.Url.Serialize(excludeFragment: true).Length;

        // The referrer of the request is "client" until the request is fetched: its serialization is the referrer that the
        // referrer policy of the request (else of the client) determines for its URL
        length += request.Referrer switch
        {
            "no-referrer" or "" => 0,
            "client" => ReferrerPolicies.DetermineReferrer(client, request.Url, ReferrerPolicies.GetRequestPolicy(request.ReferrerPolicy, client))?.Length ?? 0,
            var referrer => referrer.Length,
        };

        foreach (var (name, value) in request.HeaderList)
        {
            length += name.Length + value.Length;
        }

        return length + (request.Body?.Length ?? 0);
    }

    /// <summary>https://fetch.spec.whatwg.org/#deferred-fetch-control-document</summary>
    public static Document GetControlDocument(Document document)
    {
        while (document.BrowsingContext?.Parent?.Document is { } containerDocument && OriginRecord.IsSameOrigin(containerDocument.Origin, document.Origin))
        {
            document = containerDocument;
        }

        return document;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#available-deferred-fetch-quota: the bytes that a document can still defer to an origin
    /// (to any origin when it is null).
    /// </summary>
    public static long GetAvailableQuota(Document document, OriginRecord? origin)
    {
        var controlDocument = GetControlDocument(document);
        if (controlDocument.BrowsingContext is not { } navigable)
            return 0;

        var isTopLevel = navigable.Parent is null;
        var deferredFetchAllowed = PermissionsPolicyAlgorithms.IsAllowedToUse(controlDocument, PolicyControlledFeature.DeferredFetch, report: false);
        var deferredFetchMinimalAllowed = PermissionsPolicyAlgorithms.IsAllowedToUse(controlDocument, PolicyControlledFeature.DeferredFetchMinimal, report: false);
        var reservedQuota = navigable.Container is { } navigableContainer ? GetReservedQuota(navigableContainer) : 0;
        long quota;
        if (isTopLevel && !deferredFetchAllowed)
        {
            quota = 0;
        }
        else if (isTopLevel && !deferredFetchMinimalAllowed)
        {
            quota = TopLevelQuota;
        }
        else if (isTopLevel)
        {
            quota = TopLevelQuota - QuotaReservedForMinimal;
        }
        else if (deferredFetchAllowed && reservedQuota == NormalQuota)
        {
            quota = NormalQuota;
        }
        else if (deferredFetchMinimalAllowed && reservedQuota == MinimalQuota)
        {
            quota = MinimalQuota;
        }
        else
        {
            quota = 0;
        }

        var quotaForRequestOrigin = QuotaForRequestOrigin;
        foreach (var descendant in navigable.GetDescendantNavigables().Prepend(navigable))
        {
            var activeDocument = descendant.Document;
            if (!ReferenceEquals(GetControlDocument(activeDocument), controlDocument))
                continue;

            foreach (var container in EnumerateNavigableContainers(activeDocument))
            {
                quota -= GetReservedQuota(container);
            }

            foreach (var record in activeDocument.FetchGroup.DeferredFetchRecords)
            {
                if (record.InvokeState != DeferredFetchInvokeState.Pending)
                    continue;

                quota -= record.TotalRequestLength;
                if (origin is not null && OriginRecord.IsSameOrigin(OriginRecord.FromUrl(record.Request.Url), origin))
                {
                    quotaForRequestOrigin -= record.TotalRequestLength;
                }
            }
        }

        if (quota <= 0)
            return 0;

        return Math.Max(0, Math.Min(quota, quotaForRequestOrigin));
    }

    // "container in navigable's active document's shadow-including inclusive descendants which is a navigable container"
    private static IEnumerable<Element> EnumerateNavigableContainers(Document document)
    {
        if (document.ChildNavigables is not { } children)
            yield break;

        foreach (var child in children)
        {
            if (child.Container is { } container)
            {
                yield return container;
            }
        }
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#reserve-deferred-fetch-quota: called when the document of a navigable container
    /// navigates its content navigable. The container reserves 64 kibibytes when the "deferred-fetch" policy is delegated
    /// to the origin, else 8 kibibytes with the "deferred-fetch-minimal" policy of a top-level control document.
    /// </summary>
    public static void ReserveQuota(Element container, OriginRecord originToNavigateTo)
    {
        SetReservedQuota(container, 0);
        var controlDocument = GetControlDocument(container.NodeDocument);
        if (PermissionsPolicyAlgorithms.DefineInheritedPolicy(PolicyControlledFeature.DeferredFetch, container, originToNavigateTo)
            && GetAvailableQuota(controlDocument, origin: null) >= NormalQuota)
        {
            SetReservedQuota(container, NormalQuota);
            return;
        }

        if (controlDocument.BrowsingContext is not { Parent: null } navigable || !PermissionsPolicyAlgorithms.DefineInheritedPolicy(PolicyControlledFeature.DeferredFetchMinimal, container, originToNavigateTo))
            return;

        var minimalContainers = 0;
        foreach (var descendant in navigable.GetDescendantNavigables())
        {
            if (descendant.Container is { } descendantContainer && GetReservedQuota(descendantContainer) == MinimalQuota)
            {
                minimalContainers++;
            }
        }

        if (minimalContainers < QuotaReservedForMinimal / MinimalQuota)
        {
            SetReservedQuota(container, MinimalQuota);
        }
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#potentially-free-deferred-fetch-quota: called when a document is created. A document
    /// that is same origin with its container document shares its quota: the quota that its container reserved is freed.
    /// </summary>
    public static void PotentiallyFreeQuota(Document document)
    {
        if (document.BrowsingContext is { Container: { } container, Parent.Document: { } containerDocument } && OriginRecord.IsSameOrigin(containerDocument.Origin, document.Origin))
        {
            SetReservedQuota(container, 0);
        }
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#queue-a-deferred-fetch: the record is added to the fetch group of the client, and
    /// processed after <paramref name="activateAfter"/> milliseconds on the clock of the event loop.
    /// </summary>
    public static DeferredFetchRecord Queue(FetchRequest request, Document client, double? activateAfter, Action onActivatedWithoutTermination)
    {
        // "Populate request from client" is done by the fetch. "Set request's service-workers mode to "none"; set request's
        // keepalive to true"
        request.ServiceWorkersMode = ServiceWorkersMode.None;
        request.Keepalive = true;
        var record = new DeferredFetchRecord(request, onActivatedWithoutTermination) { TotalRequestLength = ComputeTotalRequestLength(request, client) };
        client.FetchGroup.DeferredFetchRecords.Add(record);
        if (activateAfter is { } delay && client.EnvironmentEventLoop is { } eventLoop)
        {
            // The delay is clamped to the range of a timer; the virtual clock of the event loop elapses it
            eventLoop.QueueTimer(TimeSpan.FromMilliseconds(Math.Min(delay, int.MaxValue)), () => Process(record, client));
        }

        return record;
    }

    /// <summary>https://fetch.spec.whatwg.org/#process-deferred-fetches: when the fetch group of a document is terminated.</summary>
    public static void ProcessAll(Document client)
    {
        var records = client.FetchGroup.DeferredFetchRecords;
        if (records.Count == 0)
            return;

        foreach (var record in records.ToArray())
        {
            Process(record, client);
        }

        records.Clear();
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#process-a-deferred-fetch: a pending record is fetched (a keepalive request, which
    /// outlives its document), then the result of <c>fetchLater()</c> is updated by a task.
    /// </summary>
    public static void Process(DeferredFetchRecord record, Document client)
    {
        if (record.InvokeState != DeferredFetchInvokeState.Pending)
            return;

        record.InvokeState = DeferredFetchInvokeState.Sent;
        client.FetchGroup.DeferredFetchRecords.Remove(record);

        // The request is a record of the fetch group until it is done, as the other keepalive requests
        var request = record.Request;
        var eventLoop = client.EnvironmentEventLoop;
        var fetchRecord = client.FetchGroup.Add(abort: null, request);
        _ = Fetching.FetchAsync(request, CancellationToken.None).ContinueWith(
            completed =>
            {
                if (eventLoop is null)
                {
                    fetchRecord.Dispose();
                    return;
                }

                eventLoop.QueueTask(fetchRecord.Dispose);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // "Queue a global task on the deferred fetch task source [...] to run deferredRecord's notify invoked": a document that
        // is terminated runs no task
        client.RelevantWindow?.QueueGlobalTask(record.NotifyInvoked);
    }
}
