using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The fetch directives of Content Security Policy (https://w3c.github.io/webappsec-csp/#directives-fetch).</summary>
public sealed class ContentSecurityPolicyTests : IDisposable
{
    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly BrowsingContextScope _contexts = new();
    private readonly JintScriptEngine _engine = new();
    private readonly List<DocumentRequest> _requests = [];

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    [Theory]
    // 'none' and the empty source list match nothing
    [InlineData("img-src 'none'", "https://example.com/a.png", true)]
    [InlineData("img-src", "https://example.com/a.png", true)]
    [InlineData("img-src 'none' https://example.com", "https://example.com/a.png", false)]
    // * matches the HTTP(S) URLs and the scheme of the document, not data:
    [InlineData("img-src *", "https://other.example/a.png", false)]
    [InlineData("img-src *", "data:image/png,", true)]
    [InlineData("img-src * data:", "data:image/png,", false)]
    // 'self' allows the secure upgrade of the origin
    [InlineData("img-src 'self'", "https://example.com/a.png", false)]
    [InlineData("img-src 'self'", "https://other.example/a.png", true)]
    [InlineData("img-src 'self'", "http://example.com/a.png", true)]
    // Host sources: the scheme upgrade, the wildcards, the default port and the paths
    [InlineData("img-src http://other.example", "https://other.example/a.png", false)]
    [InlineData("img-src https://other.example", "http://other.example/a.png", true)]
    [InlineData("img-src *.example", "https://a.b.example/a.png", false)]
    [InlineData("img-src *.example", "https://example/a.png", true)]
    [InlineData("img-src other.example:443", "https://other.example/a.png", false)]
    [InlineData("img-src other.example", "https://other.example:8443/a.png", true)]
    [InlineData("img-src other.example:*", "https://other.example:8443/a.png", false)]
    [InlineData("img-src https://other.example/images/", "https://other.example/images/a.png", false)]
    [InlineData("img-src https://other.example/images/", "https://other.example/a.png", true)]
    [InlineData("img-src https://other.example/images/a.png", "https://other.example/images/a.png", false)]
    [InlineData("img-src https://other.example/images/a.png", "https://other.example/images/b.png", true)]
    // Scheme sources
    [InlineData("img-src https:", "https://other.example/a.png", false)]
    [InlineData("img-src http:", "https://other.example/a.png", false)]
    [InlineData("img-src https:", "http://other.example/a.png", true)]
    // The fallback to default-src, and the directive that executes is the first one of the fallback list
    [InlineData("default-src 'none'", "https://example.com/a.png", true)]
    [InlineData("default-src 'none'; img-src *", "https://example.com/a.png", false)]
    [InlineData("script-src 'none'", "https://example.com/a.png", false)]
    // The first directive with a name is used, and the names are case-insensitive
    [InlineData("IMG-SRC 'none'; img-src *", "https://example.com/a.png", true)]
    public void Image(string policy, string url, bool blocked)
    {
        Assert.Equal(blocked, ShouldBlock(policy, url, RequestDestination.Image));
    }

    [Theory]
    [InlineData("script-src 'nonce-abc'", "abc", false, false)]
    [InlineData("script-src 'nonce-abc'", "def", false, true)]
    [InlineData("script-src 'nonce-abc'", "", false, true)]
    [InlineData("script-src 'nonce-abc' 'strict-dynamic'", "", false, false)]
    [InlineData("script-src 'nonce-abc' 'strict-dynamic'", "", true, true)]
    [InlineData("script-src 'nonce-abc' 'strict-dynamic' https:", "", true, true)]
    [InlineData("script-src-elem 'nonce-abc'; script-src 'none'", "abc", false, false)]
    public void Script(string policy, string nonce, bool parserInserted, bool blocked)
    {
        var document = Document.CreateHtmlDocument();
        var script = document.CreateElement("script");
        script.SetAttribute("nonce", nonce);
        Assert.Equal(blocked, ShouldBlock(policy, "https://other.example/a.js", RequestDestination.Script, script, parserInserted));
    }

    [Theory]
    [InlineData("script-src 'sha256-abc='", "sha256-abc=", false)]
    [InlineData("script-src 'sha256-abc='", "sha256-abc= sha384-def", true)]
    [InlineData("script-src 'sha256-abc=' 'sha384-def'", "sha256-abc= sha384-def", false)]
    [InlineData("script-src 'sha256-abc='", "sha256-xyz=", true)]
    [InlineData("script-src 'sha256-abc='", "md5-abc=", true)]
    public void Script_Integrity(string policy, string integrity, bool blocked)
    {
        var document = Document.CreateHtmlDocument();
        var script = document.CreateElement("script");
        script.SetAttribute("integrity", integrity);
        Assert.Equal(blocked, ShouldBlock(policy, "https://other.example/a.js", RequestDestination.Script, script));
    }

    [Theory]
    // worker-src (and script-src through the fallback list) applies the pre-request check of the script directives to the workers
    [InlineData("script-src 'nonce-abc' 'strict-dynamic'", false)]
    [InlineData("worker-src 'strict-dynamic'", false)]
    [InlineData("script-src 'nonce-abc'", true)]
    [InlineData("worker-src 'self'", true)]
    public void Worker(string policy, bool blocked)
    {
        Assert.Equal(blocked, ShouldBlock(policy, "https://other.example/worker.js", RequestDestination.Worker));
    }

    [Fact]
    public void ReportOnly_DoesNotBlock()
    {
        var policy = ContentSecurityPolicy.Parse("img-src 'none'", reportOnly: true, fromMeta: false);
        var url = UrlParser.Parse("https://example.com/a.png", baseUrl: null)!;
        Assert.False(ContentSecurityPolicy.ShouldBlockRequest([policy], OriginRecord.FromUrl(url), url, RequestDestination.Image));
    }

    [Fact]
    public void Redirect_IgnoresThePath()
    {
        var policy = ContentSecurityPolicy.Parse("img-src https://other.example/images/", reportOnly: false, fromMeta: false);
        var url = UrlParser.Parse("https://other.example/redirected.png", baseUrl: null)!;
        var origin = OriginRecord.FromUrl(UrlParser.Parse("https://example.com/", baseUrl: null)!);
        Assert.True(ContentSecurityPolicy.ShouldBlockRequest([policy], origin, url, RequestDestination.Image));
        Assert.False(ContentSecurityPolicy.ShouldBlockRequest([policy], origin, url, RequestDestination.Image, redirectCount: 1));
    }

    [Fact]
    public void MetaElement_BlocksTheSubsequentImages()
    {
        var document = Load("""
            <head>
            <script src=before.js></script>
            <meta http-equiv="Content-Security-Policy" content="img-src 'none'">
            </head>
            <body><img src=after.png onerror="document.title += ' error'">
            """);

        Assert.Equal("before error", document.Title);
        Assert.Equal(["Script https://example.com/before.js"], Requests);
    }

    [Fact]
    public void MetaElement_OutsideOfTheHead_IsIgnored()
    {
        var document = Load("""<body><meta http-equiv="Content-Security-Policy" content="img-src 'none'"><img src=a.png onload="document.title = 'load'">""");
        Assert.Equal("load", document.Title);
    }

    [Fact]
    public void Header_BlocksTheScriptsWithoutNonce()
    {
        var document = Load(
            """
            <script nonce=abc src=allowed.js></script>
            <script src=blocked.js></script>
            """,
            headers: [new("Content-Security-Policy", "script-src 'nonce-abc'")]);

        Assert.Equal("allowed", document.Title);
        Assert.Equal(["Script https://example.com/allowed.js"], Requests);
    }

    [Fact]
    public void Header_ListOfPolicies_AllPoliciesAreEnforced()
    {
        Load("<img src=a.png><img src=https://other.example/b.png>", headers: [new("Content-Security-Policy", "img-src *, img-src 'self'")]);
        Assert.Equal(["Image https://example.com/a.png"], Requests);
    }

    [Fact]
    public void Fetch_ConnectSrc()
    {
        var document = Load(
            """
            <script>
            fetch("https://other.example/data").then(() => document.title = "fetched", () => document.title = "blocked");
            </script>
            """,
            headers: [new("Content-Security-Policy", "connect-src 'self'")]);

        Assert.Equal("blocked", document.Title);
        Assert.Empty(Requests);
    }

    // Records the violations in the title of the document
    private const string ViolationListener = """
        <script>
        document.addEventListener("securitypolicyviolation", e => document.title += [e.blockedURI, e.effectiveDirective, e.violatedDirective, e.originalPolicy, e.disposition, e.statusCode, e.sample, e.bubbles, e.composed, e.target.nodeName, e.documentURI].join(" ") + ";");
        </script>
        """;

    [Fact]
    public void Violation_FiresSecurityPolicyViolationEvent()
    {
        var document = Load(ViolationListener + """<img src="https://user:pass@other.example/a.png#fragment" onerror="document.title += 'error;'">""", headers: [new("Content-Security-Policy", "img-src 'self'; script-src * 'unsafe-inline'")]);

        // The event is fired by a task queued when the request is blocked, before the error event of the image (the title
        // collapses the whitespace of the empty sample)
        Assert.Equal("https://other.example/a.png img-src img-src img-src 'self'; script-src * 'unsafe-inline' enforce 200 true true #document https://example.com/;error;", document.Title);
        Assert.Empty(Requests);
    }

    [Fact]
    public void Violation_DefaultSrc_EffectiveDirectiveIsTheDirectiveOfTheRequest()
    {
        var document = Load(ViolationListener + """<script>fetch("https://other.example/data").catch(() => {});</script>""", headers: [new("Content-Security-Policy", "default-src 'self' 'unsafe-inline'")]);
        Assert.StartsWith("https://other.example/data connect-src connect-src default-src 'self' 'unsafe-inline' enforce", document.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Violation_ReportOnly_IsReportedAndNotBlocked()
    {
        var document = Load(ViolationListener + "<img src=a.png>", headers: [new("Content-Security-Policy-Report-Only", "img-src 'none'")]);

        Assert.Equal("https://example.com/a.png img-src img-src img-src 'none' report 200 true true #document https://example.com/;", document.Title);
        Assert.Equal(["Image https://example.com/a.png"], Requests);
    }

    [Fact]
    public void Violation_EnforcedAndReportOnlyPolicies_EachPolicyReportsItsViolation()
    {
        var document = Load(ViolationListener + "<img src=https://other.example/a.png>", headers: [new("Content-Security-Policy", "img-src 'self'"), new("Content-Security-Policy-Report-Only", "img-src 'none'")]);

        Assert.Contains("img-src 'self' enforce", document.Title, StringComparison.Ordinal);
        Assert.Contains("img-src 'none' report", document.Title, StringComparison.Ordinal);
        Assert.Empty(Requests);
    }

    [Fact]
    public void Violation_ReportUri_SendsTheDeprecatedReport()
    {
        Load("<img src=https://other.example/a.png#fragment>", headers: [new("Content-Security-Policy", "img-src 'self'; report-uri /report1 https://reports.example/report2")]);

        var reports = _requests.Where(r => r.Destination == RequestDestination.Report).ToList();
        Assert.Equal(["https://example.com/report1", "https://reports.example/report2"], reports.Select(r => r.Url));
        var report = reports[0];
        Assert.Equal("POST", report.Method);
        Assert.Equal("application/csp-report", report.BodyContentType);
        using var json = System.Text.Json.JsonDocument.Parse(report.Body!.Value);
        var body = json.RootElement.GetProperty("csp-report");
        Assert.Equal("https://example.com/", body.GetProperty("document-uri").GetString());
        Assert.Equal("https://other.example/a.png", body.GetProperty("blocked-uri").GetString());
        Assert.Equal("img-src", body.GetProperty("effective-directive").GetString());
        Assert.Equal("img-src", body.GetProperty("violated-directive").GetString());
        Assert.Equal("img-src 'self'; report-uri /report1 https://reports.example/report2", body.GetProperty("original-policy").GetString());
        Assert.Equal("enforce", body.GetProperty("disposition").GetString());
        Assert.Equal(200, body.GetProperty("status-code").GetInt32());
    }

    [Fact]
    public void Violation_ReportTo_DisablesReportUri()
    {
        Load("<img src=https://other.example/a.png>", headers: [new("Content-Security-Policy", "img-src 'self'; report-uri /report; report-to endpoint")]);
        Assert.DoesNotContain(_requests, r => r.Destination == RequestDestination.Report);
    }

    [Fact]
    public void Violation_ReportUri_IgnoredInMeta()
    {
        Load("""<meta http-equiv="Content-Security-Policy" content="img-src 'self'; report-uri /report"><img src=https://other.example/a.png>""");
        Assert.DoesNotContain(_requests, r => r.Destination == RequestDestination.Report);
    }

    [Fact]
    public void Violation_Redirect_BlockedUriIsTheUrlOfTheRequest()
    {
        var document = Load(ViolationListener + "<img src=redirect.png>", headers: [new("Content-Security-Policy", "img-src 'self'")], redirects: new() { ["https://example.com/redirect.png"] = "https://other.example/secret.png?token=1" });
        Assert.StartsWith("https://example.com/redirect.png img-src", document.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Violation_TrustedTypes_PolicyCreation()
    {
        var document = Load(ViolationListener + """
            <script>
            try { trustedTypes.createPolicy("two", {}); } catch { document.title += "blocked;"; }
            </script>
            """, headers: [new("Content-Security-Policy", "trusted-types one")]);

        Assert.Equal("blocked;trusted-types-policy trusted-types trusted-types trusted-types one enforce 200 two true true #document https://example.com/;", document.Title);
    }

    [Fact]
    public void Violation_TrustedTypes_SinkTypeMismatch_ReportOnly()
    {
        var document = Load(ViolationListener + """
            <div id=target></div>
            <script>
            document.getElementById("target").innerHTML = "<b>0123456789012345678901234567890123456789</b>";
            document.title += document.getElementById("target").textContent.length + ";";
            </script>
            """, headers: [new("Content-Security-Policy-Report-Only", "require-trusted-types-for 'script'")]);

        Assert.Equal("40;trusted-types-sink require-trusted-types-for require-trusted-types-for require-trusted-types-for 'script' report 200 Element innerHTML|<b>0123456789012345678901234567890123456 true true #document https://example.com/;", document.Title);
    }

    [Theory]
    // 'unsafe-inline' allows the inline scripts, unless the source list has a nonce, a hash or 'strict-dynamic'
    [InlineData("script-src 'unsafe-inline'", "<script>document.title += 'a';</script>", "a")]
    [InlineData("script-src 'self'", "<script>document.title += 'a';</script>", "")]
    [InlineData("script-src 'unsafe-inline' 'nonce-abc'", "<script>document.title += 'a';</script><script nonce=abc>document.title += 'b';</script>", "b")]
    [InlineData("script-src 'unsafe-inline' 'strict-dynamic'", "<script>document.title += 'a';</script>", "")]
    [InlineData("default-src 'unsafe-inline'", "<script>document.title += 'a';</script>", "a")]
    [InlineData("script-src-elem 'unsafe-inline'; script-src 'none'", "<script>document.title += 'a';</script>", "a")]
    [InlineData("script-src-attr 'unsafe-inline'; script-src 'none'", "<script>document.title += 'a';</script>", "")]
    // The hash of the UTF-8 source text (sha256, sha384, sha512; base64 or base64url; the algorithm is case-insensitive)
    [InlineData("script-src 'sha256-/dHeIAt/XBcmmp30Rj5Py5dY0WWn440VosxSLZQLzTE='", "<script>document.title += 'a';</script>", "a")]
    [InlineData("script-src 'SHA256-_dHeIAt_XBcmmp30Rj5Py5dY0WWn440VosxSLZQLzTE='", "<script>document.title += 'a';</script>", "a")]
    [InlineData("script-src 'sha256-/dHeIAt/XBcmmp30Rj5Py5dY0WWn440VosxSLZQLzTE='", "<script>document.title += 'b';</script>", "")]
    // 'strict-dynamic' allows the inline scripts inserted by an allowed script, not the ones of the parser
    [InlineData("script-src 'nonce-abc' 'strict-dynamic'", "<script nonce=abc>var s = document.createElement('script'); s.textContent = \"document.title += 'a';\"; document.head.append(s);</script><script>document.title += 'b';</script>", "a")]
    // The nonce of a script element one of whose attributes contains "<script", or that had a duplicate attribute, does not match
    [InlineData("script-src 'nonce-abc'", "<script nonce=abc data=\"<script\">document.title += 'a';</script><script a a nonce=abc>document.title += 'b';</script><script nonce=abc>document.title += 'c';</script>", "c")]
    public void InlineScript(string policy, string html, string expectedTitle)
    {
        var document = Load("<title></title>" + html, headers: [new("Content-Security-Policy", policy)]);
        Assert.Equal(expectedTitle, document.Title);
    }

    [Fact]
    public void InlineScript_Violation()
    {
        var document = Load(
            """<script nonce=abc>document.addEventListener("securitypolicyviolation", e => document.title += [e.blockedURI, e.effectiveDirective, e.sample, e.target.nodeName, e.sourceFile, e.lineNumber, e.columnNumber, e.disposition].join(" ") + ";");</script>""" + "\n<script>document.title += 'ran';</script>",
            headers: [new("Content-Security-Policy", "script-src 'nonce-abc' 'report-sample'"), new("Content-Security-Policy-Report-Only", "script-src 'none'")]);

        // Each policy reports its violation (the report-only one without sample), at the script element, with the position
        // of its source text in the document: the listener violates the report-only policy, the second script both policies
        Assert.Equal("inline script-src-elem SCRIPT https://example.com/ 1 19 report;inline script-src-elem document.title += 'ran'; SCRIPT https://example.com/ 2 9 enforce;inline script-src-elem SCRIPT https://example.com/ 2 9 report;", document.Title);
    }

    [Theory]
    [InlineData("script-src 'unsafe-inline'", false)]
    [InlineData("script-src 'nonce-abc'", true)]
    [InlineData("script-src 'nonce-abc'; script-src-attr 'unsafe-inline'", false)]
    [InlineData("script-src-elem 'nonce-abc'; script-src 'unsafe-inline'", false)]
    [InlineData("script-src 'nonce-abc' 'sha256-/dHeIAt/XBcmmp30Rj5Py5dY0WWn440VosxSLZQLzTE=' 'sha256-q4sTT6a1D1Kh+QPCfqDe78rk0ZOPjLFP4uXdszySezc='", true)]
    [InlineData("script-src 'nonce-abc' 'unsafe-hashes' 'sha256-/dHeIAt/XBcmmp30Rj5Py5dY0WWn440VosxSLZQLzTE=' 'sha256-q4sTT6a1D1Kh+QPCfqDe78rk0ZOPjLFP4uXdszySezc='", false)]
    public void EventHandlerContentAttribute(string policy, bool blocked)
    {
        var document = Load("""
            <title></title>
            <div id=target onclick="document.title += 'a';"></div>
            <script nonce=abc>
            var target = document.getElementById("target");
            target.click();
            target.setAttribute("onclick", "document.title += 'b';");
            target.click();
            </script>
            """, headers: [new("Content-Security-Policy", policy)]);

        Assert.Equal(blocked ? "" : "ab", document.Title);
    }

    [Fact]
    public void EventHandlerContentAttribute_Blocked_KeepsThePreviousHandler()
    {
        var document = Load("""
            <title></title>
            <div id=target></div>
            <script nonce=abc>
            var target = document.getElementById("target");
            target.onclick = () => document.title += "idl;";
            target.setAttribute("onclick", "document.title += 'attribute;';");
            target.click();
            </script>
            """, headers: [new("Content-Security-Policy", "script-src 'nonce-abc'")]);

        Assert.Equal("idl;", document.Title);
    }

    [Theory]
    [InlineData("style-src 'unsafe-inline'", "rgb(0, 0, 255)")]
    [InlineData("style-src 'self'", "rgb(0, 0, 0)")]
    [InlineData("style-src 'nonce-abc'", "rgb(0, 128, 0)")]
    [InlineData("style-src 'sha256-pckGv9YvNcB5xy+Y4fbqhyo+ib850wyiuWeNbZvLi00='", "rgb(255, 0, 0)")]
    [InlineData("style-src-elem 'unsafe-inline'; style-src 'none'", "rgb(0, 0, 255)")]
    public void InlineStyle(string policy, string expectedColor)
    {
        var document = Load("""
            <style>p { color: red; }</style>
            <style nonce=abc>p { color: green; }</style>
            <style>p { color: blue; }</style>
            <p id=target>text</p>
            <script nonce=n>document.title = getComputedStyle(document.getElementById("target")).color;</script>
            """, headers: [new("Content-Security-Policy", policy + "; script-src 'nonce-n'")]);

        Assert.Equal(expectedColor, document.Title);
    }

    [Fact]
    public void InlineStyle_Blocked_FiresErrorEvent()
    {
        var document = Load("""
            <script nonce=n>
            var style = document.createElement("style");
            style.textContent = "p { color: red; }";
            style.onload = () => document.title += "load;";
            style.onerror = () => document.title += "error;";
            document.addEventListener("securitypolicyviolation", e => document.title += e.effectiveDirective + ";");
            document.head.append(style);
            document.title += (style.sheet === null) + ";";
            </script>
            """, headers: [new("Content-Security-Policy", "style-src 'self'; script-src 'nonce-n'")]);

        Assert.Equal("true;style-src-elem;error;", document.Title);
    }

    [Fact]
    public void StyleAttribute()
    {
        var document = Load("""
            <p id=a style="color: red">a</p>
            <p id=b>b</p>
            <script nonce=n>
            var a = document.getElementById("a");
            var b = document.getElementById("b");
            b.setAttribute("style", "color: blue");
            var results = [a.style.color, getComputedStyle(a).color, b.style.color, getComputedStyle(b).color];

            // The CSSOM is not checked, nor are the copies of the style attribute
            a.style.backgroundColor = "green";
            var clone = a.cloneNode();
            document.body.append(clone);
            results.push(a.getAttribute("style"), clone.style.backgroundColor, getComputedStyle(clone).backgroundColor);
            document.title = results.join(";");
            </script>
            """, headers: [new("Content-Security-Policy", "style-src 'self'; script-src 'nonce-n'")]);

        Assert.Equal(";rgb(0, 0, 0);;rgb(0, 0, 0);background-color: green;;green;rgb(0, 128, 0)", document.Title);
    }

    [Theory]
    [InlineData("style-src 'unsafe-inline'", "rgb(255, 0, 0)")]
    [InlineData("style-src-attr 'unsafe-inline'; style-src 'none'", "rgb(255, 0, 0)")]
    [InlineData("style-src 'sha256-NerDAUWfwD31YdZHveMrq0GLjsNFMwxLpZl0dPUeCcw='", "rgb(0, 0, 0)")]
    [InlineData("style-src 'unsafe-hashes' 'sha256-NerDAUWfwD31YdZHveMrq0GLjsNFMwxLpZl0dPUeCcw='", "rgb(255, 0, 0)")]
    public void StyleAttribute_SourceList(string policy, string expectedColor)
    {
        var document = Load("""
            <p id=a style="color: red">a</p>
            <script nonce=n>document.title = getComputedStyle(document.getElementById("a")).color;</script>
            """, headers: [new("Content-Security-Policy", policy + "; script-src 'nonce-n'")]);

        Assert.Equal(expectedColor, document.Title);
    }

    [Theory]
    // The nonce content attribute is hidden when the policy is delivered by a header, not by a meta element
    [InlineData(true, "abc |0")]
    [InlineData(false, "abc abc|1")]
    public void NonceHiding(bool header, string expectedTitle)
    {
        var meta = header ? "" : """<meta http-equiv="Content-Security-Policy" content="script-src 'nonce-abc'">""";
        var document = Load(meta + """
            <script nonce=abc>
            document.title = document.currentScript.nonce + " " + document.currentScript.getAttribute("nonce") + "|" + document.querySelectorAll("[nonce=abc]").length;
            </script>
            """, headers: header ? [new("Content-Security-Policy", "script-src 'nonce-abc'")] : []);

        Assert.Equal(expectedTitle, document.Title);
    }

    [Theory]
    [InlineData("script-src 'nonce-n'", "")]
    [InlineData("script-src 'unsafe-inline'", "ran")]
    [InlineData("script-src 'nonce-n' 'sha256-QNypiaYohI1aVW137g80LSeTFMKHQR5iCUPJHJL6kIA='", "")]
    [InlineData("script-src 'nonce-n' 'unsafe-hashes' 'sha256-QNypiaYohI1aVW137g80LSeTFMKHQR5iCUPJHJL6kIA='", "ran")]
    public void JavaScriptUrl(string policy, string expectedTitle)
    {
        var document = Load("""<title></title><a id=link href="javascript:void(document.title='ran')">link</a><script nonce=n>document.getElementById("link").click();</script>""", headers: [new("Content-Security-Policy", policy)]);
        Assert.Equal(expectedTitle, document.Title);
    }

    [Fact]
    public void JavaScriptUrl_Violation()
    {
        var document = Load("""
            <title></title>
            <iframe id=frame></iframe>
            <script nonce=n>
            var frame = document.getElementById("frame");
            frame.addEventListener("securitypolicyviolation", e => document.title += [e.blockedURI, e.effectiveDirective, e.sample, e.target.nodeName, e.lineNumber].join(" ") + ";");
            frame.src = "javascript:'inline url'";
            </script>
            """, headers: [new("Content-Security-Policy", "script-src 'nonce-n' 'report-sample'")]);

        Assert.Equal("inline script-src-elem javascript:'inline url' IFRAME 6;", document.Title);
    }

    private static DocumentResource Frame(string html, params (string Name, string Value)[] headers) => new(html) { Headers = [new("Content-Type", "text/html"), .. headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value))] };

    // Records the violations in the title of the document (the blocked URI and the effective directive)
    private const string ShortViolationListener = """
        <script>
        document.addEventListener("securitypolicyviolation", e => document.title += e.blockedURI + " " + e.effectiveDirective + ";");
        </script>
        """;

    [Theory]
    // frame-src, and child-src then default-src through the fallback list
    [InlineData("frame-src 'self'", true)]
    [InlineData("child-src 'self'", true)]
    [InlineData("default-src 'self' 'unsafe-inline'", true)]
    [InlineData("frame-src https://other.example; child-src 'none'", false)]
    [InlineData("script-src 'none'; img-src 'none'", false)]
    public void FrameSrc(string policy, bool blocked)
    {
        var document = Load("<title></title><iframe src=https://other.example/frame.html></iframe>", headers: [new("Content-Security-Policy", policy + "; script-src 'unsafe-inline'")], frames: new() { ["https://other.example/frame.html"] = Frame("<title>frame</title>") });

        Assert.Equal(blocked, !_requests.Exists(r => r.Url == "https://other.example/frame.html"));
        Assert.Equal(blocked ? "" : "frame", document.BrowsingContext!.Children[0].Document.Title);
    }

    [Fact]
    public void FrameSrc_Violation_CrossOriginUrlIsStrippedToItsOrigin()
    {
        var document = Load(ShortViolationListener + "<iframe src=https://other.example/path/frame.html></iframe><iframe src=/same.html></iframe>", headers: [new("Content-Security-Policy", "frame-src 'none'; script-src 'unsafe-inline'")]);
        Assert.Equal("https://other.example frame-src;https://example.com/same.html frame-src;", document.Title);
    }

    [Fact]
    public void FrameSrc_DataUrl()
    {
        var document = Load(ShortViolationListener + "<iframe src='data:text/html,<title>data</title>'></iframe>", headers: [new("Content-Security-Policy", "frame-src https:; script-src 'unsafe-inline'")]);
        Assert.Equal("data frame-src;", document.Title);
        Assert.Equal("", document.BrowsingContext!.Children[0].Document.Title);
    }

    [Theory]
    [InlineData("post", "form-action 'self'", "https://other.example/submit", true)]
    [InlineData("get", "form-action 'self'", "https://other.example/submit", true)]
    [InlineData("post", "form-action 'self'", "/submit", false)]
    // form-action does not fall back to default-src
    [InlineData("post", "default-src 'none'; frame-src *", "https://other.example/submit", false)]
    [InlineData("post", "form-action 'none'", "javascript:document.title='ran'", true)]
    public void FormAction(string method, string policy, string action, bool blocked)
    {
        var document = Load(ShortViolationListener + $"""
            <iframe name=target></iframe>
            <form method={method} action="{action}" target=target><input name=a value=b></form>
            <script>document.forms[0].submit();</script>
            """, headers: [new("Content-Security-Policy", policy + "; script-src 'unsafe-inline'")]);

        Assert.Equal(blocked, !_requests.Exists(r => r.Url.Contains("submit", StringComparison.Ordinal)) && !document.Title.Contains("ran", StringComparison.Ordinal));
        Assert.Equal(blocked, document.Title.Contains("form-action", StringComparison.Ordinal));
    }

    [Fact]
    public void FormAction_Redirect()
    {
        var document = Load(ShortViolationListener + """
            <iframe name=target></iframe>
            <form method=post action=/redirect target=target></form>
            <script>document.forms[0].submit();</script>
            """, headers: [new("Content-Security-Policy", "form-action 'self'; script-src 'unsafe-inline'")], redirects: new() { ["https://example.com/redirect"] = "https://other.example/submit" });

        // The URL of the redirect is checked, and the blocked URI is the URL of the request
        Assert.Equal("https://example.com/redirect form-action;", document.Title);
        Assert.DoesNotContain(_requests, r => r.Url == "https://other.example/submit");
    }

    [Theory]
    [InlineData("frame-ancestors 'none'", true)]
    [InlineData("frame-ancestors 'self'", true)]
    [InlineData("frame-ancestors https://example.com", false)]
    [InlineData("frame-ancestors *", false)]
    [InlineData("default-src 'none'", false)]
    public void FrameAncestors(string policy, bool blocked)
    {
        var document = Load("<iframe src=https://other.example/frame.html></iframe>", frames: new() { ["https://other.example/frame.html"] = Frame("<title>frame</title>", ("Content-Security-Policy", policy)) });
        var frame = document.BrowsingContext!.Children[0].Document;
        Assert.Equal(blocked ? "" : "frame", frame.Title);
        Assert.Equal(blocked, frame.Origin.IsOpaque);
    }

    [Fact]
    public void FrameAncestors_NestedFrame_EveryAncestorIsChecked()
    {
        var document = Load("<iframe src=https://other.example/outer.html></iframe>", frames: new()
        {
            ["https://other.example/outer.html"] = Frame("<iframe src=https://third.example/inner.html></iframe>"),
            ["https://third.example/inner.html"] = Frame("<title>inner</title>", ("Content-Security-Policy", "frame-ancestors https://other.example")),
        });

        // The top-level document (https://example.com) is not allowed
        Assert.Equal("", document.BrowsingContext!.Children[0].Children[0].Document.Title);
    }

    [Theory]
    // frame-ancestors is ignored in a meta element and in a report-only policy (which only reports)
    [InlineData("""<meta http-equiv="Content-Security-Policy" content="frame-ancestors 'none'">""", "Content-Security-Policy-Report-Only")]
    public void FrameAncestors_MetaAndReportOnly_DoNotBlock(string meta, string headerName)
    {
        var document = Load("<iframe src=https://other.example/frame.html></iframe>", frames: new() { ["https://other.example/frame.html"] = Frame(meta + "<title>frame</title>", (headerName, "frame-ancestors 'none'; report-uri /report")) });
        Assert.Equal("frame", document.BrowsingContext!.Children[0].Document.Title);

        // The report of the report-only policy is sent from the origin of the response, to an endpoint resolved against its URL
        var report = Assert.Single(_requests, r => r.Destination == RequestDestination.Report);
        Assert.Equal("https://other.example/report", report.Url);
        using var json = System.Text.Json.JsonDocument.Parse(report.Body!.Value);
        Assert.Equal("https://other.example/frame.html", json.RootElement.GetProperty("csp-report").GetProperty("document-uri").GetString());
        Assert.Equal("frame-ancestors", json.RootElement.GetProperty("csp-report").GetProperty("effective-directive").GetString());
    }

    [Theory]
    [InlineData("DENY", null, true)]
    [InlineData("SAMEORIGIN", null, true)]
    [InlineData("ALLOWALL", null, false)]
    [InlineData("DENY, SAMEORIGIN", null, true)]
    [InlineData("invalid, other", null, false)]
    // An enforced frame-ancestors directive overrides X-Frame-Options
    [InlineData("DENY", "frame-ancestors *", false)]
    public void XFrameOptions(string xFrameOptions, string? policy, bool blocked)
    {
        (string, string)[] headers = policy is null ? [("X-Frame-Options", xFrameOptions)] : [("X-Frame-Options", xFrameOptions), ("Content-Security-Policy", policy)];
        var document = Load("<iframe src=https://other.example/frame.html></iframe>", frames: new() { ["https://other.example/frame.html"] = Frame("<title>frame</title>", headers) });
        Assert.Equal(blocked ? "" : "frame", document.BrowsingContext!.Children[0].Document.Title);
    }

    [Fact]
    public void XFrameOptions_SameOrigin()
    {
        var document = Load("<iframe src=/frame.html></iframe>", frames: new() { ["https://example.com/frame.html"] = Frame("<title>frame</title>", ("X-Frame-Options", "SAMEORIGIN")) });
        Assert.Equal("frame", document.BrowsingContext!.Children[0].Document.Title);
    }

    [Theory]
    [InlineData("base-uri 'self'", "https://other.example/base/", "https://example.com/;https://other.example/base/ base-uri;")]
    [InlineData("base-uri 'self'", "/base/", "https://example.com/base/;")]
    [InlineData("base-uri 'none'", "/base/", "https://example.com/;https://example.com/base/ base-uri;")]
    [InlineData("default-src 'none'", "https://other.example/base/", "https://other.example/base/;")]
    public void BaseUri(string policy, string href, string expectedTitle)
    {
        var document = Load(ShortViolationListener + $"""<base href="{href}"><script>document.title += document.baseURI + ";";</script>""", headers: [new("Content-Security-Policy", policy + "; script-src 'unsafe-inline'")]);
        Assert.Equal(expectedTitle, document.Title);
    }

    [Theory]
    // The initial about:blank document, the about:blank, srcdoc and data: documents inherit the policies of their creator,
    // parent or initiator
    [InlineData("var frame = document.body.appendChild(document.createElement('iframe')); var doc = frame.contentDocument;")]
    [InlineData("var frame = document.body.appendChild(document.createElement('iframe')); frame.src = 'about:blank';")]
    [InlineData("var frame = document.createElement('iframe'); frame.srcdoc = '<p>'; document.body.appendChild(frame);")]
    [InlineData("var frame = document.createElement('iframe'); frame.src = 'data:text/html,<p>'; document.body.appendChild(frame);")]
    [InlineData("var popup = window.open();")]
    public void Inheritance_LocalSchemes(string script)
    {
        var document = Load($"<body><script>{script}</script>", headers: [new("Content-Security-Policy", "img-src 'none'; script-src 'unsafe-inline'")]);
        var child = document.BrowsingContext!.Children.Count > 0 ? document.BrowsingContext.Children[0] : null;
        var childDocument = child?.Document ?? FindPopupDocument(document);
        Assert.NotNull(childDocument);
        var img = childDocument.CreateElement("img");
        img.SetAttribute("src", "https://example.com/inherited.png");
        childDocument.Body!.AppendChild(img);
        _contexts.EventLoop.RunUntilIdle();

        Assert.DoesNotContain(_requests, r => r.Url == "https://example.com/inherited.png");
    }

    [Fact]
    public void Inheritance_HttpDocument_DoesNotInherit()
    {
        var document = Load("<iframe src=/frame.html></iframe>", headers: [new("Content-Security-Policy", "img-src 'none'")], frames: new() { ["https://example.com/frame.html"] = Frame("<img src=/frame.png>") });
        Assert.Contains(_requests, r => r.Url == "https://example.com/frame.png");
        Assert.NotNull(document);
    }

    [Fact]
    public void Inheritance_SrcdocSelfIsTheOriginOfTheParent()
    {
        // The srcdoc document is sandboxed (opaque origin): 'self' of its own meta policy is the self-origin of the inherited CSP list
        var document = Load("""<iframe sandbox srcdoc="<meta http-equiv=Content-Security-Policy content=&quot;img-src 'self'&quot;><img src=/self.png><img src=https://other.example/other.png>"></iframe>""", headers: [new("Content-Security-Policy", "font-src 'none'")]);
        Assert.Contains(_requests, r => r.Url == "https://example.com/self.png");
        Assert.DoesNotContain(_requests, r => r.Url == "https://other.example/other.png");
        Assert.NotNull(document);
    }

    [Fact]
    public void Inheritance_JavaScriptUrlDocument_KeepsThePoliciesOfTheTarget()
    {
        // The result of a javascript: URL replaces the document of the frame, whose policies it keeps
        var document = Load("""<iframe src=/frame.html></iframe><script>onload = () => document.querySelector("iframe").src = "javascript:'<img src=/javascript.png>'";</script>""", headers: [new("Content-Security-Policy", "img-src *; script-src 'unsafe-inline'")], frames: new() { ["https://example.com/frame.html"] = Frame("<p>", ("Content-Security-Policy", "img-src 'none'")) });
        Assert.DoesNotContain(_requests, r => r.Url == "https://example.com/javascript.png");
        Assert.NotNull(document);
    }

    private static Document? FindPopupDocument(Document opener)
    {
        foreach (var context in opener.BrowsingContext!.GetGroupTopLevelBrowsingContexts())
        {
            if (!ReferenceEquals(context, opener.BrowsingContext))
                return context.Document;
        }

        return null;
    }

    private static bool ShouldBlock(string serializedPolicy, string url, RequestDestination destination, Element? initiator = null, bool parserInserted = false)
    {
        var policy = ContentSecurityPolicy.Parse(serializedPolicy, reportOnly: false, fromMeta: false);
        var origin = OriginRecord.FromUrl(UrlParser.Parse("https://example.com/", baseUrl: null)!);
        return ContentSecurityPolicy.ShouldBlockRequest([policy], origin, UrlParser.Parse(url, baseUrl: null)!, destination, initiator, parserInserted);
    }

    private Document Load(string html, IReadOnlyList<KeyValuePair<string, string>>? headers = null, Dictionary<string, string>? redirects = null, Dictionary<string, DocumentResource>? frames = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this, html, headers ?? [], redirects ?? [], frames ?? []) });
        context.Navigate("https://example.com/");
        _contexts.EventLoop.RunUntilIdle();
        return context.Document;
    }

    private List<string> Requests => [.. _requests.Where(r => r.Destination is not (RequestDestination.Document or RequestDestination.Report)).Select(r => r.Destination + " " + r.Url)];

    /// <summary>
    /// Serves the document for the navigation, a PNG for the .png URLs and a script that appends its name to the title for
    /// the .js URLs, the redirects of <paramref name="redirects"/>, and the documents of the nested navigables of
    /// <paramref name="frames"/>.
    /// </summary>
    private sealed class Loader(ContentSecurityPolicyTests test, string html, IReadOnlyList<KeyValuePair<string, string>> headers, Dictionary<string, string> redirects, Dictionary<string, DocumentResource> frames) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            if (redirects.TryGetValue(request.Url, out var location))
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(ReadOnlyMemory<byte>.Empty) { StatusCode = 302, Headers = [new("Location", location)] });

            if (frames.TryGetValue(request.Url, out var frame))
                return ValueTask.FromResult<DocumentResource?>(frame);

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
