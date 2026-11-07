using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The Reporting API (https://w3c.github.io/reporting/) and the reports of the CSP <c>report-to</c> directive.</summary>
public sealed class ReportingTests : IDisposable
{
    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly List<DocumentRequest> _requests = [];

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    [Fact]
    public void StructuredFields_Dictionary()
    {
        var dictionary = StructuredFields.ParseDictionary("a=\"x\", b=?0;p=1, c=(1 2);q, a=\"y\",d")!;

        Assert.Equal(["a", "b", "c", "d"], dictionary.Select(entry => entry.Key));
        Assert.Equal("y", dictionary[0].Value.Item!.Value);
        Assert.Equal(false, dictionary[1].Value.Item!.Value);
        Assert.Equal(1L, dictionary[1].Value.Item!.GetParameter("p"));
        Assert.Equal([1L, 2L], dictionary[2].Value.InnerList!.Select(item => item.Value));
        Assert.Equal(true, dictionary[3].Value.Item!.Value);
        Assert.Null(StructuredFields.ParseDictionary("a=1,"));
        Assert.Null(StructuredFields.ParseDictionary("A=1"));
    }

    [Fact]
    public void Endpoints_ReportingEndpointsHeader()
    {
        var endpoints = ReportingEndpoint.Process(
            [new("Reporting-Endpoints", "a=\"/reports\", b=\"http://insecure.example/\", c=token, d=\"http://127.0.0.1/r\", a=\"https://other.example/r\"")],
            Url("https://example.com/page"))!;

        // The last value of a key is used, the URLs are resolved against the URL of the response, the URLs that are not
        // potentially trustworthy and the members that are not strings are ignored
        Assert.Equal(["a https://other.example/r", "d http://127.0.0.1/r"], endpoints.Select(endpoint => endpoint.Name + " " + endpoint.Url.Serialize()));
    }

    [Fact]
    public void Endpoints_InsecureResponse_HasNoEndpoint()
    {
        Assert.Null(ReportingEndpoint.Process([new("Reporting-Endpoints", "a=\"https://example.com/\"")], Url("http://example.com/")));
    }

    [Fact]
    public void Endpoints_ReportToHeader_IsUsedWithoutReportingEndpoints()
    {
        var reportTo = new KeyValuePair<string, string>("Report-To", """{ "group": "g", "max_age": 10, "endpoints": [{ "url": "http://insecure.example/" }, { "url": "/r1" }, { "url": "/r2" }] }, { "max_age": 10, "endpoints": [{ "url": "https://other.example/" }] }""");

        var endpoints = ReportingEndpoint.Process([reportTo], Url("https://example.com/page"))!;
        Assert.Equal(["g https://example.com/r1", "default https://other.example/"], endpoints.Select(endpoint => endpoint.Name + " " + endpoint.Url.Serialize()));

        endpoints = ReportingEndpoint.Process([reportTo, new("Reporting-Endpoints", "x=\"/x\"")], Url("https://example.com/page"))!;
        Assert.Equal(["x https://example.com/x"], endpoints.Select(endpoint => endpoint.Name + " " + endpoint.Url.Serialize()));
    }

    [Fact]
    public void ReportTo_ObserverReceivesTheCspViolationReport()
    {
        var document = Load("""
            <script>
            new ReportingObserver((reports, observer) => {
              const report = reports[0];
              document.title = [reports.length, observer instanceof ReportingObserver, report.type, report.url, JSON.stringify(report.body)].join("|");
            }).observe();
            </script>
            <img src="https://other.example/a.png#fragment">
            """, [new("Content-Security-Policy", "img-src 'self'; report-to csp"), new("Reporting-Endpoints", "csp=\"/reports\"")]);

        Assert.Equal("""1|true|csp-violation|https://example.com/|{"blockedURL":"https://other.example/a.png","columnNumber":null,"disposition":"enforce","documentURL":"https://example.com/","effectiveDirective":"img-src","lineNumber":null,"originalPolicy":"img-src 'self'; report-to csp","referrer":"","sample":"","sourceFile":null,"statusCode":200}""", document.Title);
    }

    [Fact]
    public void ReportTo_DeliversTheReportsToTheEndpoint()
    {
        Load("""<img src="https://other.example/a.png"><img src="https://other.example/b.png">""", [new("Content-Security-Policy", "img-src 'self'; report-to csp csp2; report-uri /report-uri"), new("Reporting-Endpoints", "csp=\"/reports\", csp2=\"/reports2\"")]);

        // The reports of a task are sent together, report-uri is ignored, and the second token of report-to is ignored
        var request = Assert.Single(_requests, r => r.Destination == RequestDestination.Report);
        Assert.Equal("https://example.com/reports", request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal("application/reports+json", request.BodyContentType);
        Assert.True(request.IncludeCredentials);
        using var json = JsonDocument.Parse(request.Body!.Value);
        var reports = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, reports.Count);
        Assert.Equal(["age", "type", "url", "user_agent", "body"], reports[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal("csp-violation", reports[0].GetProperty("type").GetString());
        Assert.Equal("https://example.com/", reports[0].GetProperty("url").GetString());
        Assert.Equal(Navigator.DefaultUserAgent, reports[0].GetProperty("user_agent").GetString());
        Assert.Equal("https://other.example/a.png", reports[0].GetProperty("body").GetProperty("blockedURL").GetString());
        Assert.Equal("https://other.example/b.png", reports[1].GetProperty("body").GetProperty("blockedURL").GetString());
    }

    [Fact]
    public void ReportTo_UnknownEndpoint_IsObservedButNotSent()
    {
        var document = Load("""
            <script>new ReportingObserver(reports => document.title = reports[0].type).observe();</script>
            <img src="https://other.example/a.png">
            """, [new("Content-Security-Policy", "img-src 'self'; report-to unknown"), new("Reporting-Endpoints", "csp=\"/reports\"")]);

        Assert.Equal("csp-violation", document.Title);
        Assert.DoesNotContain(_requests, r => r.Destination == RequestDestination.Report);
    }

    [Fact]
    public void ReportTo_AllowedInMeta()
    {
        Load("""<meta http-equiv="Content-Security-Policy" content="img-src 'self'; report-to csp"><img src="https://other.example/a.png">""", [new("Reporting-Endpoints", "csp=\"/reports\"")]);
        Assert.Single(_requests, r => r.Destination == RequestDestination.Report);
    }

    [Fact]
    public void Observer_BufferedTypesAndTakeRecords()
    {
        var document = Load("""<img src="https://other.example/a.png">""", [new("Content-Security-Policy", "img-src 'self'; report-to csp")]);
        var window = document.RelevantWindow!;
        window.GenerateAndQueueReport(new TestReportBody("1"), "test", "default");

        RunScript(document, """
            const describe = reports => reports.map(r => r.type === "test" ? r.body.message : r.type).join(",");
            new ReportingObserver(reports => document.title += "typed:" + describe(reports) + ";", { types: ["test"], buffered: true }).observe();
            new ReportingObserver(reports => document.title += "all:" + describe(reports) + ";", { buffered: true }).observe();
            globalThis.taken = new ReportingObserver(() => document.title += "never;");
            taken.observe();
            """);
        _contexts.EventLoop.RunUntilIdle();

        // The observer whose queue is emptied by takeRecords() is not invoked, and a disconnected observer gets no report
        window.GenerateAndQueueReport(new TestReportBody("2"), "test", "default");
        RunScript(document, """
            document.title += "taken:" + taken.takeRecords().map(r => r.body.message).join(",") + ";";
            taken.disconnect();
            """);
        window.GenerateAndQueueReport(new TestReportBody("3"), "test", "default");
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("typed:1;all:csp-violation,1;taken:2;typed:2,3;all:2,3;", document.Title);
    }

    [Fact]
    public void ReportBuffer_KeepsTheLast100ReportsOfEachType()
    {
        var document = Load("<p>", []);
        var window = document.RelevantWindow!;
        for (var i = 0; i < 110; i++)
        {
            window.GenerateAndQueueReport(new TestReportBody(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), "test", "default");
        }

        var buffer = window.GetReportBuffer();
        Assert.Equal(100, buffer.Length);
        Assert.Equal("10", ((TestReportBody)buffer[0].Body!).Message);
    }

    [Fact]
    public void CspHash_ReportsTheHashOfTheScripts()
    {
        var document = Load("""
            <script>new ReportingObserver(() => document.title += "observed;").observe();</script>
            <script src="/a.js"></script>
            """, [new("Content-Security-Policy", "script-src 'self' 'unsafe-inline' 'report-sha256'; report-to hashes"), new("Reporting-Endpoints", "hashes=\"/reports\"")]);
        _contexts.EventLoop.RunUntilIdle();

        // The csp-hash reports are not visible to the observers
        Assert.Equal("a", document.Title);
        var request = Assert.Single(_requests, r => r.Destination == RequestDestination.Report);
        using var json = JsonDocument.Parse(request.Body!.Value);
        var report = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("csp-hash", report.GetProperty("type").GetString());
        var body = report.GetProperty("body");
        Assert.Equal("https://example.com/", body.GetProperty("documentURL").GetString());
        Assert.Equal("https://example.com/a.js", body.GetProperty("subresourceURL").GetString());
        var expectedHash = "sha256-" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData("document.title += ' a'"u8));
        Assert.Equal(expectedHash, body.GetProperty("hash").GetString());
        Assert.Equal("script", body.GetProperty("destination").GetString());
        Assert.Equal("subresource", body.GetProperty("type").GetString());
    }

    [Fact]
    public void EmbedderPolicy_CorpViolation_IsReported()
    {
        var document = Load("""
            <script>
            new ReportingObserver(reports => document.title += reports.map(r => r.type + " " + JSON.stringify(r.body)).join(";")).observe();
            fetch("https://other.example/data", { mode: "no-cors" }).catch(() => document.title += "blocked;");
            </script>
            """, [new("Cross-Origin-Embedder-Policy", "require-corp; report-to=\"coep\""), new("Cross-Origin-Embedder-Policy-Report-Only", "require-corp"), new("Reporting-Endpoints", "coep=\"/reports\"")]);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("""blocked;coep {"type":"corp","blockedURL":"https://other.example/data","destination":"","disposition":"reporting"};coep {"type":"corp","blockedURL":"https://other.example/data","destination":"","disposition":"enforce"}""", document.Title);

        // Only the enforced policy names an endpoint
        var request = Assert.Single(_requests, r => r.Destination == RequestDestination.Report);
        using var json = JsonDocument.Parse(request.Body!.Value);
        Assert.Equal("enforce", Assert.Single(json.RootElement.EnumerateArray()).GetProperty("body").GetProperty("disposition").GetString());
    }

    private static UrlRecord Url(string url) => UrlParser.Parse(url, baseUrl: null)!;

    private static void RunScript(Document document, string code)
    {
        var script = document.CreateElement("script");
        script.TextContent = code;
        document.Body!.AppendChild(script);
    }

    private Document Load(string html, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this, html, headers) });
        context.Navigate("https://example.com/");
        _contexts.EventLoop.RunUntilIdle();
        return context.Document;
    }

    /// <summary>Serves the document for the navigation, a PNG for the .png URLs, a script that appends its name to the title for the .js URLs, and accepts the reports.</summary>
    private sealed class Loader(ReportingTests test, string html, IReadOnlyList<KeyValuePair<string, string>> headers) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            var path = request.Url.Split('?')[0];
            var name = path[(path.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
            DocumentResource resource = request.Destination switch
            {
                RequestDestination.Document => new DocumentResource(html) { Headers = [new("Content-Type", "text/html"), .. headers] },
                _ when path.EndsWith(".png", StringComparison.Ordinal) => DocumentResource.Create(Png, "image/png"),
                _ when path.EndsWith(".js", StringComparison.Ordinal) => DocumentResource.Create($"document.title += ' {name[..^3]}'", "text/javascript"),
                _ => DocumentResource.Create("data", "text/plain"),
            };

            return ValueTask.FromResult<DocumentResource?>(resource);
        }
    }
}
