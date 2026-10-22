using System.Text.Encodings.Web;
using System.Text.Json;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A violation of a content security policy (https://w3c.github.io/webappsec-csp/#violation): created by
/// <see cref="Create"/> when a check of a policy fails (e.g. <see cref="ContentSecurityPolicy.ShouldBlockRequest(FetchRequest)"/>,
/// the Trusted Types directives), completed by the caller (its resource, element and sample), and reported by
/// <see cref="Report"/>, whatever the disposition of the policy.
/// </summary>
internal sealed class ContentSecurityPolicyViolation
{
    /// <summary>The maximum length of a sample (https://w3c.github.io/webappsec-csp/#violation-sample).</summary>
    public const int SampleLength = 40;

    private ContentSecurityPolicyViolation(Document environment, ContentSecurityPolicy policy, string effectiveDirective)
    {
        Environment = environment;
        Policy = policy;
        EffectiveDirective = effectiveDirective;
    }

    /// <summary>
    /// The global object whose policy is violated (https://w3c.github.io/webappsec-csp/#violation-global-object),
    /// represented by the document of its environment settings object: the associated document of a window, the
    /// environment document of a worker (<see cref="Document.WorkerGlobal"/>), or a document without window (its own global object).
    /// </summary>
    public Document Environment { get; }

    /// <summary>
    /// Whether the violation has no global object (https://w3c.github.io/webappsec-csp/#violation-global-object): a violation
    /// of a navigation response check (<see cref="CreateWithoutGlobal"/>), for which no event is fired. <see cref="Environment"/>
    /// is then only the environment of the fetches of the reports.
    /// </summary>
    public bool HasNoGlobal { get; private set; }

    /// <summary>The URL of the global object, or of the response of a violation without global object (https://w3c.github.io/webappsec-csp/#violation-url).</summary>
    public UrlRecord Url => _url ?? Environment.UrlRecord;

    private UrlRecord? _url;

    /// <summary>The HTTP status code of the response of the global object (https://w3c.github.io/webappsec-csp/#violation-status).</summary>
    public int Status { get; private set; }

    /// <summary>
    /// The resource that violated the policy when it is a keyword (https://w3c.github.io/webappsec-csp/#violation-resource):
    /// <c>inline</c>, <c>eval</c>, <c>wasm-eval</c>, <c>trusted-types-policy</c> or <c>trusted-types-sink</c>. See <see cref="ResourceUrl"/>.
    /// </summary>
    public string? Resource { get; set; }

    /// <summary>The resource that violated the policy when it is a URL (https://w3c.github.io/webappsec-csp/#violation-resource), e.g. the URL of a request.</summary>
    public UrlRecord? ResourceUrl { get; set; }

    /// <summary>The referrer of the document of a window (https://w3c.github.io/webappsec-csp/#violation-referrer), or the empty string.</summary>
    public string Referrer { get; private set; } = "";

    /// <summary>The violated policy (https://w3c.github.io/webappsec-csp/#violation-policy).</summary>
    public ContentSecurityPolicy Policy { get; }

    /// <summary>The disposition of the violated policy (https://w3c.github.io/webappsec-csp/#violation-disposition).</summary>
    public SecurityPolicyViolationEventDisposition Disposition => Policy.Disposition;

    /// <summary>The directive whose enforcement caused the violation (https://w3c.github.io/webappsec-csp/#violation-effective-directive).</summary>
    public string EffectiveDirective { get; }

    /// <summary>The URL of the script that caused the violation, or null (https://w3c.github.io/webappsec-csp/#violation-source-file).</summary>
    public UrlRecord? SourceFile { get; set; }

    /// <summary>https://w3c.github.io/webappsec-csp/#violation-line-number</summary>
    public int LineNumber { get; set; }

    /// <summary>https://w3c.github.io/webappsec-csp/#violation-column-number</summary>
    public int ColumnNumber { get; set; }

    /// <summary>The element that caused the violation (the target of the event when it is connected), or null (https://w3c.github.io/webappsec-csp/#violation-element).</summary>
    public Element? Element { get; set; }

    /// <summary>A sample of the code or of the value that caused the violation, or the empty string (https://w3c.github.io/webappsec-csp/#violation-sample).</summary>
    public string Sample { get; set; } = "";

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#create-violation-for-global: a violation of <paramref name="policy"/> for the
    /// global object of <paramref name="environment"/> (see <see cref="Environment"/>), with the location of the running
    /// script, the referrer and the status of the document. The caller sets the resource.
    /// </summary>
    public static ContentSecurityPolicyViolation Create(Document environment, ContentSecurityPolicy policy, string directive)
    {
        var violation = new ContentSecurityPolicyViolation(environment, policy, directive);

        // Step 2: the location of the running script, when the engine can extract it (the URL requested by the page). The
        // engine of the global object is only queried from the thread of its event loop (a window's violation may be created
        // by a worker).
        GlobalScope? global = environment.WorkerGlobal ?? (GlobalScope?)environment.RelevantWindow;
        var isGlobalThread = environment.WorkerGlobal is { } worker ? ReferenceEquals(GlobalScope.CurrentWorkerGlobal, worker) : GlobalScope.CurrentWorkerGlobal is null;
        if (isGlobalThread && global?.ScriptEngine?.GetCurrentScriptLocation() is { } location)
        {
            violation.SetSourceLocation(location);
        }

        // Step 3: the referrer of the document of a window
        if (environment.WorkerGlobal is null)
        {
            violation.Referrer = environment.Referrer;
        }

        // Step 4
        violation.Status = environment.ResponseStatus;
        return violation;
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#create-violation-for-global with a null global object: a violation of a navigation
    /// response check (https://w3c.github.io/webappsec-csp/#should-block-navigation-response), whose URL and status are the
    /// ones of the response (there is no document yet). No event is fired; the reports of the <c>report-uri</c> directive are
    /// sent from the origin of the response, with <paramref name="reportingEnvironment"/> as the environment of the fetches
    /// (its event loop and its loader), as in the browsers.
    /// </summary>
    public static ContentSecurityPolicyViolation CreateWithoutGlobal(Document reportingEnvironment, ContentSecurityPolicy policy, string directive, UrlRecord url, int status)
    {
        return new ContentSecurityPolicyViolation(reportingEnvironment, policy, directive)
        {
            HasNoGlobal = true,
            _url = url,
            Status = status,
        };
    }

    /// <summary>Sets the source file, the line number and the column number of the violation (https://w3c.github.io/webappsec-csp/#create-violation-for-global, step 2).</summary>
    public void SetSourceLocation(Scripting.ScriptLocation location)
    {
        SourceFile = UrlParser.Parse(location.Url, baseUrl: null);
        LineNumber = location.LineNumber;
        ColumnNumber = location.ColumnNumber;
    }

    /// <summary>
    /// The first 40 characters of a sample (https://w3c.github.io/webappsec-csp/#violation-sample): the first 40 code units,
    /// or 41 when the 40th one starts a surrogate pair, as in Firefox (https://github.com/w3c/webappsec-csp/issues/704).
    /// </summary>
    public static string ClipSample(string sample)
    {
        if (sample.Length <= SampleLength)
            return sample;

        return char.IsSurrogatePair(sample[SampleLength - 1], sample[SampleLength]) ? sample[..(SampleLength + 1)] : sample[..SampleLength];
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#obtain-violation-blocked-uri</summary>
    public string GetBlockedUri() => ResourceUrl is { } url ? StripUrlForUseInReports(url) : Resource ?? "";

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#strip-url-for-use-in-reports: the scheme of a URL that is not an HTTP(S) URL,
    /// else the URL without its fragment and its credentials. As in the browsers, the <c>ws</c> and <c>wss</c> URLs are kept
    /// (https://github.com/w3c/webappsec-csp/issues/735): the URL of a WebSocket request is an HTTP(S) URL, so only a source
    /// file (a <c>//# sourceURL=</c> comment) can have these schemes.
    /// </summary>
    public static string StripUrlForUseInReports(UrlRecord url) => StripUrlForUseInReports(url, excludeQuery: false);

    private static string StripUrlForUseInReports(UrlRecord url, bool excludeQuery)
    {
        if (url.Scheme is not ("http" or "https" or "ws" or "wss"))
            return url.Scheme;

        if (url.Username.Length == 0 && url.Password.Length == 0 && (!excludeQuery || url.Query is null))
            return url.Serialize(excludeFragment: true);

        var stripped = url.Clone();
        stripped.Username = "";
        stripped.Password = "";
        if (excludeQuery)
        {
            stripped.Query = null;
        }

        return stripped.Serialize(excludeFragment: true);
    }

    /// <summary>
    /// The source file stripped for use in the reports, or null. Its query is removed too, as in Chromium
    /// (https://crbug.com/1074317): the script may have been fetched after redirects, whose URLs must not leak
    /// (https://w3c.github.io/webappsec-csp/#create-violation-for-global, step 2).
    /// </summary>
    private string? GetStrippedSourceFile() => SourceFile is { } sourceFile ? StripUrlForUseInReports(sourceFile, excludeQuery: true) : null;

    /// <summary>The referrer stripped for use in the reports (https://w3c.github.io/webappsec-csp/#strip-url-for-use-in-reports), or the empty string.</summary>
    private string GetStrippedReferrer() => Referrer.Length > 0 && UrlParser.Parse(Referrer, baseUrl: null) is { } referrer ? StripUrlForUseInReports(referrer) : "";

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#report-violation: fires the <c>securitypolicyviolation</c> event and sends the
    /// reports of the <c>report-uri</c> directive of the policy, in a task queued on the event loop of the global object
    /// (after the script that caused the violation, which may change the DOM). The task is not associated with the document
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#queue-a-task): it runs even if the document is no longer
    /// fully active, e.g. when the navigation to a <c>javascript:</c> URL that caused the violation navigated the window. Nothing
    /// is reported for a document that has no event loop (no browsing context), nor by a worker that is closing. The
    /// <c>report-to</c> directive queues a <c>csp-violation</c> report (the Reporting API) instead of the <c>report-uri</c> reports.
    /// </summary>
    public void Report()
    {
        if (Environment.WorkerGlobal is { } worker)
        {
            worker.QueueGlobalTask(ReportTask);
        }
        else
        {
            Environment.EventLoop?.QueueTask(ReportTask);
        }
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#report-violation, step 3: the task that fires the event and sends or queues the reports.</summary>
    private void ReportTask()
    {
        if (HasNoGlobal)
        {
            SendReports();
            return;
        }

        // Step 3.1: the event is fired at the element only if it is connected to the document of a window
        var target = (EventTarget?)Element;
        if (Element is { } element && Environment.WorkerGlobal is null && !ReferenceEquals(element.GetShadowIncludingRoot(), Environment))
        {
            target = null;
        }

        // Step 3.2: the document of a window, or the global object of a worker
        target ??= Environment.WorkerGlobal ?? (EventTarget)Environment;

        // Step 3.3
        target.FireEvent(new SecurityPolicyViolationEvent("securitypolicyviolation", new SecurityPolicyViolationEventInit
        {
            DocumentUri = StripUrlForUseInReports(Url),
            Referrer = GetStrippedReferrer(),
            BlockedUri = GetBlockedUri(),
            EffectiveDirective = EffectiveDirective,
            ViolatedDirective = EffectiveDirective,
            OriginalPolicy = Policy.Serialized,
            Disposition = Disposition,
            SourceFile = GetStrippedSourceFile() ?? "",
            StatusCode = (ushort)Status,
            LineNumber = (uint)LineNumber,
            ColumnNumber = (uint)ColumnNumber,
            Sample = Sample,
            Bubbles = true,
            Composed = true,
        }));

        SendReports();
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#report-violation, steps 3.4 and 3.5: the reports of the <c>report-uri</c> directive,
    /// unless the policy has a <c>report-to</c> directive, and the report of the <c>report-to</c> directive, which is queued
    /// for the global object of the violation: a violation without global object (<see cref="CreateWithoutGlobal"/>, e.g.
    /// <c>frame-ancestors</c>) has no settings object to generate it, so it only sends the <c>report-uri</c> reports.
    /// </summary>
    private void SendReports()
    {
        // Step 3.4: the report-uri directive, unless the policy has a report-to directive
        var reportTo = Policy.GetDirectiveValue("report-to");
        if (Policy.GetDirectiveValue("report-uri") is { } endpoints && reportTo is null)
        {
            foreach (var token in endpoints)
            {
                // The URL of a worker whose policy is inherited (blob: or data:) cannot be a base URL: as in the browsers,
                // which resolve the endpoints against the URL of the response that delivered the policy, the token is
                // then resolved against the origin of the global object
                var endpoint = UrlParser.Parse(token, Url);
                if (endpoint is null && !HasNoGlobal && !Environment.Origin.IsOpaque && UrlParser.Parse(Environment.SerializeOrigin(), baseUrl: null) is { } originUrl)
                {
                    endpoint = UrlParser.Parse(token, originUrl);
                }

                if (endpoint is not null)
                {
                    SendDeprecatedReport(endpoint);
                }
            }
        }

        // Step 3.5: the report-to directive, whose value is a single token (the next ones are ignored, as in the browsers)
        if (reportTo is [var group, ..] && !HasNoGlobal)
        {
            GlobalScope? global = Environment.WorkerGlobal ?? (GlobalScope?)Environment.RelevantWindow;
            global?.GenerateAndQueueReport(new CspViolationReportBody
            {
                DocumentUrl = StripUrlForUseInReports(Url),
                Referrer = GetStrippedReferrer(),
                BlockedUrl = GetBlockedUri(),
                EffectiveDirective = EffectiveDirective,
                OriginalPolicy = Policy.Serialized,
                SourceFile = GetStrippedSourceFile(),
                Sample = Sample,
                Disposition = Disposition,
                StatusCode = (ushort)Status,
                LineNumber = SourceFile is null ? null : (uint)LineNumber,
                ColumnNumber = SourceFile is null ? null : (uint)ColumnNumber,
            }, "csp-violation", group);
        }
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#report-violation, step 3.4.2: sends the deprecated serialization of the
    /// violation to an endpoint of the <c>report-uri</c> directive (the response is ignored). The redirects are followed:
    /// a "no-cors" request whose redirect mode is "error" is a network error when it is cross-origin
    /// (https://fetch.spec.whatwg.org/#main-fetch, step 12), and the browsers send the reports to the other origins.
    /// </summary>
    private void SendDeprecatedReport(UrlRecord endpoint)
    {
        var request = new FetchRequest(endpoint)
        {
            Method = "POST",
            Client = Environment,
            Origin = HasNoGlobal ? Url.SerializeOrigin() : Environment.SerializeOrigin(),
            Destination = RequestDestination.Report,
            CredentialsMode = FetchCredentialsMode.SameOrigin,
            Keepalive = true,
            HeaderList = [new("Content-Type", "application/csp-report")],
            Body = GetDeprecatedSerialization(),
            Mode = RequestMode.NoCors,
            RedirectMode = FetchRedirectMode.Follow,
        };

        _ = Fetching.StartFetch(request, CancellationToken.None);
    }

    /// <summary>https://w3c.github.io/webappsec-csp/#deprecated-serialize-violation</summary>
    public byte[] GetDeprecatedSerialization()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("csp-report");
            writer.WriteString("document-uri", StripUrlForUseInReports(Url));
            writer.WriteString("referrer", GetStrippedReferrer());
            writer.WriteString("blocked-uri", GetBlockedUri());
            writer.WriteString("effective-directive", EffectiveDirective);
            writer.WriteString("violated-directive", EffectiveDirective);
            writer.WriteString("original-policy", Policy.Serialized);
            writer.WriteString("disposition", Disposition == SecurityPolicyViolationEventDisposition.Report ? "report" : "enforce");
            writer.WriteNumber("status-code", Status);
            writer.WriteString("script-sample", Sample);
            if (GetStrippedSourceFile() is { } sourceFile)
            {
                writer.WriteString("source-file", sourceFile);
                writer.WriteNumber("line-number", LineNumber);
                writer.WriteNumber("column-number", ColumnNumber);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
