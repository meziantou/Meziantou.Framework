using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The origins, document.domain, the origin-keyed agent clusters and the Origin interface.</summary>
public sealed class OriginTests
{
    [Theory]
    [InlineData("com", "com", null)]
    [InlineData("example.com", "com", "example.com")]
    [InlineData("www.example.com", "com", "example.com")]
    [InlineData("a.b.example.co.uk", "co.uk", "example.co.uk")]
    [InlineData("user.github.io", "github.io", "user.github.io")]
    [InlineData("web-platform.test", "test", "web-platform.test")]
    [InlineData("test", "test", null)]
    [InlineData("www.ck", "ck", "www.ck")]
    [InlineData("a.b.ck", "b.ck", "a.b.ck")]
    [InlineData("www.example.com.", "com.", "example.com.")]
    [InlineData("xn--mlauted-m2a.example", "example", "xn--mlauted-m2a.example")]
    public void PublicSuffixAndRegistrableDomain(string host, string publicSuffix, string? registrableDomain)
    {
        var parsed = HostParser.Parse(host)!.Value;
        Assert.Equal(publicSuffix, PublicSuffixList.GetPublicSuffix(parsed));
        Assert.Equal(registrableDomain, PublicSuffixList.GetRegistrableDomain(parsed));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public void PublicSuffix_IpAddress_IsNull(string host)
    {
        var parsed = HostParser.Parse(host)!.Value;
        Assert.Null(PublicSuffixList.GetPublicSuffix(parsed));
        Assert.Null(PublicSuffixList.GetRegistrableDomain(parsed));
    }

    [Fact]
    public void DocumentDomain_RelaxesTheOrigin()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://www.example.com/page", _ => "<!doctype html>");
        Assert.Equal("www.example.com", document.Domain);

        document.Domain = "example.com";
        Assert.Equal("example.com", document.Domain);

        // The serialization of the origin does not include the domain
        Assert.Equal("https://www.example.com", document.DefaultView!.Origin);
    }

    [Theory]
    [InlineData("com")]
    [InlineData(".example.com")]
    [InlineData("other.example.com")]
    [InlineData("example.org")]
    [InlineData("")]
    public void DocumentDomain_NotARegistrableDomainSuffix_Throws(string value)
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://www.example.com/", _ => "<!doctype html>");

        var exception = Assert.Throws<DomException>(() => document.Domain = value);
        Assert.Equal("SecurityError", exception.Name);
        Assert.Equal("www.example.com", document.Domain);
    }

    [Fact]
    public void DocumentDomain_IpAddress_CannotBeRelaxed()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("http://127.0.0.1/", _ => "<!doctype html>");

        document.Domain = "127.0.0.1";
        Assert.Equal("SecurityError", Assert.Throws<DomException>(() => document.Domain = "0.1").Name);
    }

    [Fact]
    public void DocumentDomain_WithoutBrowsingContext_Throws()
    {
        var document = new HtmlDocumentParser(new HtmlParserOptions { Url = "https://www.example.com/" }).Parse("");
        Assert.Equal("www.example.com", document.Domain);
        Assert.Equal("SecurityError", Assert.Throws<DomException>(() => document.Domain = "example.com").Name);
    }

    [Fact]
    public void DocumentDomain_OpaqueOrigin_Throws()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("data:text/html,", _ => "<!doctype html>");

        Assert.Equal("", document.Domain);
        Assert.Equal("SecurityError", Assert.Throws<DomException>(() => document.Domain = "").Name);
    }

    [Fact]
    public void DocumentDomain_CspSandbox_Throws()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://www.example.com/", _ => new DocumentResource("<!doctype html>") { Headers = [new("Content-Security-Policy", "sandbox allow-scripts allow-same-origin")] });

        Assert.Equal("https://www.example.com", document.DefaultView!.Origin);
        Assert.Equal("SecurityError", Assert.Throws<DomException>(() => document.Domain = "example.com").Name);
    }

    [Fact]
    public void CspSandbox_WithoutAllowSameOrigin_IsAnOpaqueOrigin()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://www.example.com/", _ => new DocumentResource("<!doctype html>") { Headers = [new("Content-Security-Policy", "default-src 'self'; sandbox allow-scripts")] });

        Assert.Equal("null", document.DefaultView!.Origin);
    }

    [Theory]
    [InlineData("allow-same-origin", "https://www.example.com")]
    [InlineData("ALLOW-SAME-ORIGIN", "https://www.example.com")]
    [InlineData("allow-same-or\U00000130gin", "null")]
    public void CspSandbox_KeywordsAreAsciiCaseInsensitive(string keyword, string expected)
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://www.example.com/", _ => new DocumentResource("<!doctype html>") { Headers = [new("Content-Security-Policy", "sandbox allow-scripts " + keyword)] });

        Assert.Equal(expected, document.DefaultView!.Origin);
    }

    [Theory]
    [InlineData("allow-scripts", true)]
    [InlineData("allow-\U0000017Fcripts", false)]
    public void CspSandbox_AllowScriptsIsAsciiCaseInsensitive(string keyword, bool expected)
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create(new BrowsingContextOptions { ScriptingEnabled = true }).Navigate("https://www.example.com/", _ => new DocumentResource("<!doctype html>") { Headers = [new("Content-Security-Policy", "sandbox " + keyword)] });

        Assert.Equal(expected, document.ScriptingEnabled);
    }

    [Fact]
    public void IframeSandbox_SupportsTheSandboxKeywords()
    {
        var document = new HtmlDocumentParser().Parse("<iframe></iframe><link>");
        var sandbox = ((HtmlIFrameElement)document.QuerySelector("iframe")!).Sandbox;
        Assert.True(sandbox.Supports("allow-scripts"));
        Assert.True(sandbox.Supports("ALLOW-SAME-ORIGIN"));
        Assert.False(sandbox.Supports("allow-\U0000017Fcripts"));
        Assert.True(((HtmlLinkElement)document.QuerySelector("link")!).Blocking.Supports("render"));
    }

    [Theory]
    [InlineData("https://example.com/", "?1", true)]
    [InlineData("https://example.com/", "?1;param", true)]
    [InlineData("https://example.com/", "?0", false)]
    [InlineData("https://example.com/", "1", false)]
    [InlineData("http://example.com/", "?1", false)]
    [InlineData("http://localhost/", "?1", true)]
    public void OriginAgentCluster_IsTheHeaderOfASecureContext(string url, string header, bool expected)
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate(url, _ => new DocumentResource("<!doctype html>") { Headers = [new("Origin-Agent-Cluster", header)] });

        Assert.Equal(expected, document.DefaultView!.OriginAgentCluster);
    }

    [Fact]
    public void OriginAgentCluster_DocumentDomainSetterDoesNothing()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://www.example.com/", _ => new DocumentResource("<!doctype html>") { Headers = [new("Origin-Agent-Cluster", "?1")] });

        // The value is checked, but the domain is not set
        Assert.Equal("SecurityError", Assert.Throws<DomException>(() => document.Domain = "com").Name);
        document.Domain = "example.com";
        Assert.Equal("www.example.com", document.Domain);
    }

    [Fact]
    public void OriginAgentCluster_HistoricalKey_KeepsSameOriginDocumentsTogether()
    {
        using var scope = new BrowsingContextScope();
        var context = scope.Create();
        var first = context.Navigate("https://example.com/1", _ => "<!doctype html>").DefaultView!;
        var second = context.Navigate("https://example.com/2", _ => new DocumentResource("<!doctype html>") { Headers = [new("Origin-Agent-Cluster", "?1")] }).DefaultView!;

        // The origin was site-keyed first in the browsing context group: the header is ignored
        Assert.False(first.OriginAgentCluster);
        Assert.False(second.OriginAgentCluster);
    }

    [Fact]
    public void OriginAgentCluster_OpaqueOrigin_IsOriginKeyed()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("data:text/html,", _ => "<!doctype html>");

        Assert.True(document.DefaultView!.OriginAgentCluster);
    }

    [Theory]
    [InlineData("https://a.example", "https://a.example", true, true)]
    [InlineData("https://a.example", "https://a.example:443", true, true)]
    [InlineData("https://a.example", "https://b.a.example", false, true)]
    [InlineData("https://a.example", "https://b.example", false, false)]
    [InlineData("http://a.example", "https://a.example", false, false)]
    [InlineData("https://a.github.io", "https://b.github.io", false, false)]
    [InlineData("https://127.0.0.1", "https://127.0.0.1:8443", false, true)]
    public void Origin_Comparisons(string a, string b, bool sameOrigin, bool sameSite)
    {
        var originA = Origin.From(a);
        var originB = Origin.From(b);
        Assert.False(originA.Opaque);
        Assert.Equal(sameOrigin, originA.IsSameOrigin(originB));
        Assert.Equal(sameSite, originA.IsSameSite(originB));
    }

    [Fact]
    public void Origin_Opaque_IsOnlySameOriginWithItself()
    {
        var a = new Origin();
        var b = Origin.From("data:text/plain,");
        Assert.True(a.Opaque);
        Assert.True(b.Opaque);
        Assert.True(a.IsSameOrigin(a));
        Assert.True(a.IsSameSite(a));
        Assert.False(a.IsSameOrigin(b));
        Assert.False(a.IsSameSite(b));
        Assert.False(b.IsSameOrigin(Origin.From("data:text/plain,")));
    }

    [Fact]
    public void Origin_From_InvalidValue_Throws()
    {
        Assert.Throws<TypeErrorException>(() => Origin.From("not a URL"));
        Assert.Throws<TypeErrorException>(() => Origin.From(null));
        Assert.Throws<TypeErrorException>(() => Origin.From(1.0));
    }

    [Fact]
    public void Origin_From_PlatformObjects()
    {
        using var scope = new BrowsingContextScope();
        var document = scope.Create().Navigate("https://example.com/", _ => "<!doctype html><a href='https://other.example/path'></a><a></a>");

        Assert.True(Origin.From(document.DefaultView).IsSameOrigin(Origin.From("https://example.com")));
        Assert.True(Origin.From(new Url("https://example.com/path")).IsSameOrigin(Origin.From("https://example.com")));

        var links = document.GetElementsByTagName("a");
        Assert.True(Origin.From(links[0]).IsSameOrigin(Origin.From("https://other.example")));
        Assert.Throws<TypeErrorException>(() => Origin.From(links[1]));
        Assert.Throws<TypeErrorException>(() => Origin.From(document.DefaultView!.Location));
    }
}
