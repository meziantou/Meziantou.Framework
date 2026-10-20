using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The headers of the responses (<see cref="DocumentResource.Headers"/>) used by the documents.</summary>
public sealed class ResponseHeadersTests
{
    [Fact]
    public void DocumentResource_Example()
    {
        #region DocumentResource_Example
        // A resource created from its decoded content
        var page = new DocumentResource("<p>Hello</p>");
        Assert.Equal("<p>Hello</p>", page.Content);

        // A resource created from its bytes, decoded using the charset of the Content-Type header
        var bytes = "console.log('Hello');"u8.ToArray();
        var script = new DocumentResource(bytes) { Headers = [new("Content-Type", "text/javascript; charset=utf-8")] };
        Assert.Null(script.Content);
        Assert.Equal(bytes, script.ContentBytes?.ToArray());
        #endregion
    }

    [Theory]
    [InlineData("Sun, 06 Nov 1994 08:49:37 GMT")]
    [InlineData("Sunday, 06-Nov-94 08:49:37 GMT")]
    [InlineData("Sun Nov  6 08:49:37 1994")]
    public void ParseHttpDate(string value)
    {
        Assert.Equal(new DateTimeOffset(1994, 11, 6, 8, 49, 37, TimeSpan.Zero), HeaderList.ParseHttpDate(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("1994-11-06T08:49:37Z")]
    public void ParseHttpDate_Invalid(string value)
    {
        Assert.Null(HeaderList.ParseHttpDate(value));
    }

    [Fact]
    public void LastModified_IsTheLastModifiedHeader()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("") { Headers = [new("last-modified", "Thu, 01 Jan 1970 01:23:45 GMT")] });

        var expected = TimeZoneInfo.ConvertTime(new DateTimeOffset(1970, 1, 1, 1, 23, 45, TimeSpan.Zero), TimeZoneInfo.Local);
        Assert.Equal(expected.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture), document.LastModified);
    }

    [Fact]
    public void LastModified_InvalidHeader_IsTheCurrentDate()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("") { Headers = [new("Last-Modified", "invalid")] });

        var lastModified = DateTime.ParseExact(document.LastModified, "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        var now = TimeProvider.System.GetLocalNow().DateTime;
        Assert.InRange(lastModified, now.AddMinutes(-1), now.AddMinutes(1));
    }

    [Theory]
    [InlineData("fr", "<p id=target>", true)]
    [InlineData("fr-CA", "<p id=target>", true)]
    [InlineData(" fr ", "<p id=target>", true)]
    [InlineData("fr, en", "<p id=target>", true)]
    [InlineData("en, fr", "<p id=target>", false)]
    [InlineData("", "<p id=target>", false)]
    [InlineData("fr", "<p id=target lang=en>", false)]
    [InlineData("en", "<meta http-equiv=content-language content=fr><p id=target>", true)]
    [InlineData("fr", "<meta http-equiv=Content-Language content='en'><p id=target>", false)]
    [InlineData("fr", "<meta http-equiv=content-language content='en, de'><p id=target>", true)]
    [InlineData(null, "<meta http-equiv=content-language content='  fr  ca'><p id=target>", true)]
    [InlineData(null, "<meta http-equiv=content-language content=''><p id=target>", false)]
    public void ContentLanguage_IsTheDefaultLanguage(string? contentLanguage, string html, bool expectedFrench)
    {
        // https://html.spec.whatwg.org/multipage/dom.html#language: the lang attributes, the pragma-set default language, then the Content-Language header
        // (its first language, as in the browsers)
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(html) { Headers = contentLanguage is null ? [] : [new("Content-Language", contentLanguage)] });
        Assert.Equal(expectedFrench, document.QuerySelector("#target")!.Matches(":lang(fr)"));
    }

    [Fact]
    public void ContentLanguagePragma_IsProcessedWhenTheMetaElementIsInserted()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => "<p id=target>");
        var target = document.QuerySelector("#target")!;
        Assert.False(target.Matches(":lang(fr)"));

        var meta = document.CreateElement("meta");
        meta.SetAttribute("http-equiv", "content-language");
        meta.SetAttribute("content", "fr");
        Assert.False(target.Matches(":lang(fr)"));

        document.Head!.AppendChild(meta);
        Assert.True(target.Matches(":lang(fr)"));

        // The pragma-set default language stays when the element is removed or changed
        meta.Remove();
        meta.SetAttribute("content", "en");
        Assert.True(target.Matches(":lang(fr)"));
    }

    [Fact]
    public void ContentSecurityPolicy_TrustedTypesDirectivesOfTheHeaders()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("")
        {
            Headers =
            [
                new("Content-Security-Policy", "default-src 'self'"),
                new("content-security-policy", "require-trusted-types-for 'script', trusted-types one two"),
                new("Content-Security-Policy-Report-Only", "trusted-types three"),
            ],
        });

        Assert.Collection(
            document.TrustedTypesDirectives,
            directives =>
            {
                Assert.True(directives.RequireTrustedTypesForScript);
                Assert.Null(directives.TrustedTypes);
                Assert.False(directives.ReportOnly);
            },
            directives =>
            {
                Assert.False(directives.RequireTrustedTypesForScript);
                Assert.Equal(["one", "two"], directives.TrustedTypes);
                Assert.False(directives.ReportOnly);
            },
            directives =>
            {
                Assert.Equal(["three"], directives.TrustedTypes);
                Assert.True(directives.ReportOnly);
            });

        Assert.Throws<TypeErrorException>(() => document.Body!.InnerHtml = "<b>text</b>");
    }

    [Theory]
    [InlineData("<head><meta http-equiv=Content-Security-Policy content=\"require-trusted-types-for 'script'\"></head>", true)]
    [InlineData("<body><meta http-equiv=Content-Security-Policy content=\"require-trusted-types-for 'script'\"></body>", false)]
    [InlineData("<head><meta http-equiv=Content-Security-Policy content=\"default-src 'self'\"></head>", false)]
    public void ContentSecurityPolicy_MetaElement(string html, bool expected)
    {
        // https://html.spec.whatwg.org/multipage/semantics.html#attr-meta-http-equiv-content-security-policy: the element must be a child of the head element
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => html);
        Assert.Equal(expected, document.TrustedTypesDirectives.Any(directives => directives.RequireTrustedTypesForScript));
    }

    [Fact]
    public void ContentSecurityPolicy_MetaElement_DocumentWithoutBrowsingContext()
    {
        var document = HtmlDocumentParser.ParseDocument("<head><meta http-equiv=Content-Security-Policy content=\"require-trusted-types-for 'script'\"></head>");
        Assert.Empty(document.TrustedTypesDirectives);
    }
}
