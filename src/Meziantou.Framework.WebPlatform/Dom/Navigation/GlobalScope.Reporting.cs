using System.Text.Encodings.Web;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The Reporting API (https://w3c.github.io/reporting/): the endpoints, the reports and the reporting observers of a
// window or a worker (https://w3c.github.io/reporting/#document-configuration)
public abstract partial class GlobalScope
{
    /// <summary>The maximum number of reports of each type in the report buffer (https://w3c.github.io/reporting/#notify-observers, step 4).</summary>
    private const int ReportBufferSizePerType = 100;

    private List<ReportingObserver>? _registeredReportingObservers;
    private List<QueuedReport>? _reportBuffer;
    private List<QueuedReport>? _reports;
    private bool _reportDeliveryQueued;

    // The browsing context of the document of a window when its last report was queued: its loader sends the reports once
    // the document is unloaded
    private BrowsingContext? _reportingBrowsingContext;

    /// <summary>
    /// The endpoints list of the global object (https://w3c.github.io/reporting/#windoworworkerglobalscope-endpoints), set
    /// from the response of its document or of its script (<see cref="InitializeReportingEndpoints"/>), or null.
    /// </summary>
    internal List<ReportingEndpoint>? ReportingEndpoints { get; private set; }

    /// <summary>https://w3c.github.io/reporting/#initialize-endpoints-from-response: the endpoints of the <c>Reporting-Endpoints</c> header of a response.</summary>
    internal void InitializeReportingEndpoints(IReadOnlyList<KeyValuePair<string, string>> headers, UrlRecord responseUrl)
    {
        ReportingEndpoints = ReportingEndpoint.Process(headers, responseUrl);
    }

    /// <summary>
    /// Whether the reports of a type are visible to the reporting observers (https://w3c.github.io/reporting/#visible-to-reportingobservers):
    /// the <c>test</c> reports of the Generate Test Report command, the <c>csp-violation</c> reports
    /// (https://w3c.github.io/webappsec-csp/#reporting) and the <c>coep</c> reports
    /// (https://html.spec.whatwg.org/multipage/browsers.html#coep-report-type), and the <c>permissions-policy-violation</c>
    /// and <c>potential-permissions-policy-violation</c> reports (https://w3c.github.io/webappsec-permissions-policy/#reporting).
    /// The <c>csp-hash</c> reports are not.
    /// </summary>
    internal static bool IsReportTypeVisibleToReportingObservers(string type)
        => type is "test" or "csp-violation" or "coep" or "permissions-policy-violation" or "potential-permissions-policy-violation";

    /// <summary>Appends an observer to the registered reporting observer list (https://w3c.github.io/reporting/#dom-reportingobserver-observe, step 2).</summary>
    internal void RegisterReportingObserver(ReportingObserver observer)
    {
        _registeredReportingObservers ??= [];
        if (!_registeredReportingObservers.Contains(observer))
        {
            _registeredReportingObservers.Add(observer);
        }
    }

    /// <summary>Removes an observer from the registered reporting observer list (https://w3c.github.io/reporting/#dom-reportingobserver-disconnect).</summary>
    internal void UnregisterReportingObserver(ReportingObserver observer) => _registeredReportingObservers?.Remove(observer);

    /// <summary>A copy of the registered reporting observer list.</summary>
    internal ReportingObserver[] GetRegisteredReportingObservers() => _registeredReportingObservers?.ToArray() ?? [];

    /// <summary>A copy of the report buffer (https://w3c.github.io/reporting/#windoworworkerglobalscope-report-buffer), in the order in which the reports were generated.</summary>
    internal QueuedReport[] GetReportBuffer() => _reportBuffer?.ToArray() ?? [];

    /// <summary>
    /// https://w3c.github.io/reporting/#queue-report: generates a report of the global object (its URL is the URL of the
    /// document or of the worker), notifies the reporting observers, and queues it for the delivery to the endpoint named
    /// <paramref name="destination"/> (the reports of a task are sent together, from a task queued on the event loop of the
    /// global object, which runs even if the document is no longer active).
    /// </summary>
    internal void GenerateAndQueueReport(ReportBody? body, string type, string destination)
    {
        // https://w3c.github.io/reporting/#generate-a-report
        var report = new QueuedReport
        {
            Body = body,
            Url = ContentSecurityPolicyViolation.StripUrlForUseInReports(EnvironmentDocument.UrlRecord),
            UserAgent = Navigator.DefaultUserAgent,
            Destination = destination,
            Type = type,
            Timestamp = EventLoop.TimeProvider.GetUtcNow(),
        };

        // https://w3c.github.io/reporting/#queue-report, step 3
        NotifyReportingObservers(report);

        // Step 4: the reports whose destination is not an endpoint are discarded by the delivery
        if (ReportingEndpoints is null || !ReportingEndpoints.Exists(endpoint => endpoint.Name == destination))
            return;

        _reports ??= [];
        _reports.Add(report);
        _reportingBrowsingContext = AssociatedDocument?.BrowsingContext ?? _reportingBrowsingContext;
        if (!_reportDeliveryQueued)
        {
            _reportDeliveryQueued = true;
            EventLoop.QueueTask(SendReports);
        }
    }

    /// <summary>
    /// Generates and queues a report (<see cref="GenerateAndQueueReport"/>) from a task of the event loop of the global object:
    /// the algorithms that may run on another thread (e.g. a fetch, or a worker for its owner) use it.
    /// </summary>
    /// <param name="body">The body of the report.</param>
    /// <param name="type">The type of the report.</param>
    /// <param name="destination">The name of the endpoint, or null when the policy names no endpoint (the report is only observed).</param>
    internal void QueueReport(ReportBody body, string type, string? destination)
    {
        EventLoop.QueueTask(() => GenerateAndQueueReport(body, type, destination ?? ""));
    }

    /// <summary>https://w3c.github.io/reporting/#notify-observers</summary>
    private void NotifyReportingObservers(QueuedReport report)
    {
        // Step 1
        if (_registeredReportingObservers is { Count: > 0 } observers)
        {
            foreach (var observer in observers.ToArray())
            {
                observer.AddReport(report);
            }
        }

        // Steps 2-4
        if (!IsReportTypeVisibleToReportingObservers(report.Type))
            return;

        _reportBuffer ??= [];
        _reportBuffer.Add(report);
        var count = 0;
        foreach (var buffered in _reportBuffer)
        {
            if (buffered.Type == report.Type)
            {
                count++;
            }
        }

        if (count > ReportBufferSizePerType)
        {
            _reportBuffer.RemoveAt(_reportBuffer.FindIndex(buffered => buffered.Type == report.Type));
        }
    }

    /// <summary>
    /// https://w3c.github.io/reporting/#send-reports: sends the queued reports to their endpoints, one request per endpoint
    /// and origin of the URL of the reports. The failed deliveries are not retried.
    /// </summary>
    private void SendReports()
    {
        _reportDeliveryQueued = false;
        if (_reports is not { } reports)
            return;

        _reports = null;

        // Step 2: the reports of each endpoint, in order
        var endpointMap = new List<(ReportingEndpoint Endpoint, List<QueuedReport> Reports)>();
        foreach (var report in reports)
        {
            if (ReportingEndpoints?.Find(endpoint => endpoint.Name == report.Destination) is not { } endpoint)
                continue;

            var index = endpointMap.FindIndex(entry => ReferenceEquals(entry.Endpoint, endpoint));
            if (index < 0)
            {
                endpointMap.Add((endpoint, [report]));
            }
            else
            {
                endpointMap[index].Reports.Add(report);
            }
        }

        // Step 3: the reports of each origin
        foreach (var (endpoint, reportList) in endpointMap)
        {
            var originMap = new List<(string Origin, List<QueuedReport> Reports)>();
            foreach (var report in reportList)
            {
                var origin = UrlParser.Parse(report.Url, baseUrl: null) is { } url ? OriginRecord.FromUrl(url).Serialize() : "null";
                var index = originMap.FindIndex(entry => entry.Origin == origin);
                if (index < 0)
                {
                    originMap.Add((origin, [report]));
                }
                else
                {
                    originMap[index].Reports.Add(report);
                }
            }

            foreach (var (origin, perOriginReports) in originMap)
            {
                AttemptToDeliverReports(endpoint, origin, perOriginReports);
            }
        }
    }

    /// <summary>
    /// https://w3c.github.io/reporting/#try-delivery: POSTs the reports to the endpoint (<c>application/reports+json</c>, with
    /// the credentials of a same-origin endpoint only). A <c>410</c> response removes the endpoint; the other failures are
    /// counted. The request has the environment of the global object as client (its loader and its cookies, the browsers use
    /// the cookies of the context of the document) and the keepalive flag, so that the reports of a document that is unloaded
    /// are sent.
    /// </summary>
    private void AttemptToDeliverReports(ReportingEndpoint endpoint, string origin, List<QueuedReport> reports)
    {
        DeliverReports(EnvironmentDocument, _reportingBrowsingContext, endpoint, origin, reports, response =>
        {
            if (HeaderList.IsOkStatus(response.Status))
                return;

            if (response.Status == 410)
            {
                ReportingEndpoints?.Remove(endpoint);
            }
            else
            {
                endpoint.Failures++;
            }
        });
    }

    /// <summary>
    /// https://w3c.github.io/reporting/#try-delivery: POSTs reports to an endpoint, with <paramref name="client"/> as the
    /// environment of the request (see <see cref="AttemptToDeliverReports"/>). <paramref name="processResponse"/> runs in a
    /// task of the event loop of the client.
    /// </summary>
    internal static void DeliverReports(Document client, BrowsingContext? keepaliveBrowsingContext, ReportingEndpoint endpoint, string origin, List<QueuedReport> reports, Action<FetchResponse>? processResponse)
    {
        if (client.EventLoop is not { } eventLoop)
            return;

        var request = new FetchRequest(endpoint.Url)
        {
            Method = "POST",
            Origin = origin,
            HeaderList = [new("Content-Type", "application/reports+json")],
            Client = client,
            ServiceWorkersMode = ServiceWorkersMode.None,
            Referrer = "no-referrer",
            Destination = RequestDestination.Report,
            Mode = RequestMode.Cors,
            UnsafeRequest = true,
            CredentialsMode = FetchCredentialsMode.SameOrigin,
            Keepalive = true,
            KeepaliveBrowsingContext = keepaliveBrowsingContext,
            Body = SerializeReports(reports, eventLoop.TimeProvider.GetUtcNow()),
        };

        Fetching.Fetch(request, response => processResponse?.Invoke(response), eventLoop.QueueTask);
    }

    /// <summary>https://w3c.github.io/reporting/#serialize-a-list-of-reports-to-json</summary>
    private static byte[] SerializeReports(List<QueuedReport> reports, DateTimeOffset now)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var report in reports)
            {
                writer.WriteStartObject();
                writer.WriteNumber("age", (long)(now - report.Timestamp).TotalMilliseconds);
                writer.WriteString("type", report.Type);
                writer.WriteString("url", report.Url);
                writer.WriteString("user_agent", report.UserAgent);
                writer.WritePropertyName("body");
                if (report.Body is { } body)
                {
                    body.WriteJson(writer);
                }
                else
                {
                    writer.WriteNullValue();
                }

                writer.WriteEndObject();
                report.Attempts++;
            }

            writer.WriteEndArray();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
