using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class CookieJarTests
{
    private static string? GetCookieHeader(CookieJar jar, string url, string? siteForCookies = null, string? origin = null, RequestDestination destination = RequestDestination.Empty, string method = "GET")
        => new DocumentRequest(url) { CookieJar = jar, SiteForCookies = siteForCookies, TopLevelOrigin = siteForCookies, Origin = origin, Destination = destination, Method = method }.GetCookieHeader();

    [Fact]
    public void SetCookie_ParsesTheNameValueAndAttributes()
    {
        var jar = new CookieJar();
        Assert.True(jar.SetCookie("https://example.com/dir/page", "  name = value ; Path=/dir; Secure; HttpOnly; SameSite=Strict; Max-Age=60"));

        var cookie = Assert.Single(jar.GetCookies());
        Assert.Equal("name", cookie.Name);
        Assert.Equal("value", cookie.Value);
        Assert.Equal("example.com", cookie.Domain);
        Assert.True(cookie.HostOnly);
        Assert.Equal("/dir", cookie.Path);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(CookieSameSite.Strict, cookie.SameSite);
        Assert.NotNull(cookie.Expires);
    }

    [Theory]
    [InlineData("https://example.com/a/b/c", "/a/b")]
    [InlineData("https://example.com/a", "/")]
    [InlineData("https://example.com/", "/")]
    public void SetCookie_UsesTheDefaultPath(string url, string expectedPath)
    {
        var jar = new CookieJar();
        jar.SetCookie(url, "a=1; Path=relative");

        Assert.Equal(expectedPath, Assert.Single(jar.GetCookies()).Path);
    }

    [Theory]
    [InlineData("a=1\u0001", false)]
    [InlineData("a=1\t2", true)]
    [InlineData("=", false)]
    [InlineData("value-without-name", true)]
    [InlineData("__Secure-a=1", false)]
    [InlineData("__Secure-a=1; Secure", true)]
    [InlineData("__Host-a=1; Secure", false)]
    [InlineData("__Host-a=1; Secure; Path=", false)]
    [InlineData("__Host-a=1; Secure; Path=/", true)]
    [InlineData("__host-a=1; Secure; Path=/; Domain=example.com", false)]
    [InlineData("__Http-a=1; Secure", false)]
    [InlineData("__Http-a=1; Secure; HttpOnly", true)]
    [InlineData("a=1; SameSite=None", false)]
    [InlineData("a=1; SameSite=None; Secure", true)]
    [InlineData("a=1; Partitioned", false)]
    [InlineData("a=1; Domain=com", false)]
    [InlineData("a=1; Domain=other.com", false)]
    [InlineData("a=1; Domain=.EXAMPLE.com", true)]
    [InlineData("=__Secure-a", false)]
    public void SetCookie_RejectsTheInvalidCookies(string setCookie, bool expected)
    {
        var jar = new CookieJar();
        Assert.Equal(expected, jar.SetCookie("https://www.example.com/", setCookie));
        Assert.Equal(expected ? 1 : 0, jar.GetCookies().Count);
    }

    [Fact]
    public void SetCookie_ASecureCookieCannotBeSetOrOverlaidFromAnInsecureUrl()
    {
        var jar = new CookieJar();
        Assert.False(jar.SetCookie("http://example.com/", "a=1; Secure"));
        Assert.True(jar.SetCookie("https://example.com/", "a=1; Secure; Path=/"));
        Assert.False(jar.SetCookie("http://example.com/", "a=2; Path=/"));
        Assert.Equal("1", Assert.Single(jar.GetCookies()).Value);
    }

    [Theory]
    [InlineData("Thu, 01 Jan 2032 00:00:00 GMT", 2032, 1, 1)]
    [InlineData("Wed, 21-Oct-15 07:28:00 GMT", 2015, 10, 21)]
    [InlineData("1 Feb 1999 10:20:30", 1999, 2, 1)]
    public void ParseCookieDate_ParsesTheDates(string value, int year, int month, int day)
    {
        var date = CookieJar.ParseCookieDate(value);
        Assert.NotNull(date);
        Assert.Equal(new DateOnly(year, month, day), DateOnly.FromDateTime(date.Value.UtcDateTime));
    }

    [Theory]
    [InlineData("31 Feb 2030 00:00:00")]
    [InlineData("01 Jan 1500 00:00:00")]
    [InlineData("01 Jan 2030")]
    public void ParseCookieDate_RejectsTheInvalidDates(string value) => Assert.Null(CookieJar.ParseCookieDate(value));

    [Fact]
    public void Cookies_ExpireWithTheClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var jar = new CookieJar(clock);
        jar.SetCookie("https://example.com/", "a=1; Max-Age=10");
        jar.SetCookie("https://example.com/", "b=1; Expires=Fri, 01 Jan 2100 00:00:00 GMT");

        // The lifetime is limited to 400 days
        Assert.Equal(clock.GetUtcNow().AddDays(400), jar.GetCookies().Single(cookie => cookie.Name == "b").Expires);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("b", Assert.Single(jar.GetCookies()).Name);
    }

    [Fact]
    public void GetCookieHeader_SortsTheCookiesByPathLengthThenCreationTime()
    {
        var jar = new CookieJar();
        jar.SetCookie("https://example.com/", "a=1; Path=/");
        jar.SetCookie("https://example.com/", "b=2; Path=/dir");
        jar.SetCookie("https://example.com/", "c=3; Path=/");
        jar.SetCookie("https://example.com/", "nameless");

        Assert.Equal("b=2; a=1; c=3; nameless", GetCookieHeader(jar, "https://example.com/dir/page"));
        Assert.Equal("a=1; c=3; nameless", GetCookieHeader(jar, "https://example.com/directory"));
    }

    [Fact]
    public void GetCookieHeader_MatchesTheDomains()
    {
        var jar = new CookieJar();
        jar.SetCookie("https://example.com/", "host=1");
        jar.SetCookie("https://example.com/", "domain=2; Domain=example.com");
        jar.SetCookie("https://xn--lve-6lad.example.com/", "idn=3; Domain=xn--lve-6lad.example.com");

        Assert.Equal("host=1; domain=2", GetCookieHeader(jar, "https://example.com/"));
        Assert.Equal("domain=2", GetCookieHeader(jar, "https://www.example.com/"));
        Assert.Equal("domain=2; idn=3", GetCookieHeader(jar, "https://élève.example.com/"));
        Assert.Null(GetCookieHeader(jar, "https://notexample.com/"));
    }

    [Fact]
    public void GetCookieHeader_AppliesTheSameSiteEnforcement()
    {
        var jar = new CookieJar();
        jar.SetCookie("https://example.com/", "strict=1; SameSite=Strict");
        jar.SetCookie("https://example.com/", "lax=2; SameSite=Lax");
        jar.SetCookie("https://example.com/", "none=3; SameSite=None; Secure");
        jar.SetCookie("https://example.com/", "default=4");

        // A same-site request, from a subdomain
        Assert.Equal("strict=1; lax=2; none=3; default=4", GetCookieHeader(jar, "https://example.com/", siteForCookies: "https://www.example.com", origin: "https://www.example.com"));

        // A cross-site subresource request, and a request of a cross-site iframe (opaque site for cookies)
        Assert.Equal("none=3", GetCookieHeader(jar, "https://example.com/", siteForCookies: "https://other.com", origin: "https://other.com"));
        Assert.Equal("none=3", GetCookieHeader(jar, "https://example.com/", siteForCookies: "null", origin: "https://example.com"));

        // A cross-site top-level navigation sends the Lax cookies with a safe method
        Assert.Equal("lax=2; none=3; default=4", GetCookieHeader(jar, "https://example.com/", origin: "https://other.com", destination: RequestDestination.Document));
        Assert.Equal("none=3", GetCookieHeader(jar, "https://example.com/", origin: "https://other.com", destination: RequestDestination.Document, method: "POST"));

        // Schemeful same-site: an http: site is cross-site with an https: URL
        Assert.Equal("none=3", GetCookieHeader(jar, "https://example.com/", siteForCookies: "http://example.com", origin: "http://example.com"));
    }

    [Fact]
    public void GetCookieHeader_ARedirectChainWithACrossSiteUrlIsCrossSite()
    {
        var jar = new CookieJar();
        jar.SetCookie("https://example.com/", "strict=1; SameSite=Strict");
        var request = new DocumentRequest("https://example.com/") { CookieJar = jar, Destination = RequestDestination.Empty, SiteForCookies = "https://example.com", Origin = "https://example.com", RedirectUrls = ["https://other.com/redirect"] };

        Assert.Null(request.GetCookieHeader());
    }

    [Fact]
    public void StoreCookies_ACrossSiteRequestOnlySetsTheSameSiteNoneCookies()
    {
        var jar = new CookieJar();
        var request = new DocumentRequest("https://example.com/") { CookieJar = jar, Destination = RequestDestination.Empty, SiteForCookies = "https://other.com", Origin = "https://other.com" };
        request.StoreCookies(["lax=1; SameSite=Lax", "none=2; SameSite=None; Secure", "default=3"]);

        Assert.Equal("none", Assert.Single(jar.GetCookies()).Name);
    }

    [Fact]
    public void StoreCookies_IgnoresTheResponseWhenTheCredentialsAreNotIncluded()
    {
        var jar = new CookieJar();
        new DocumentRequest("https://example.com/") { CookieJar = jar, IncludeCredentials = false }.StoreCookies(["a=1"]);

        Assert.Empty(jar.GetCookies());
    }

    [Fact]
    public void PartitionedCookies_AreKeyedByTheTopLevelSite()
    {
        var jar = new CookieJar();
        new DocumentRequest("https://embed.com/") { CookieJar = jar, Destination = RequestDestination.Image, SiteForCookies = "null", TopLevelOrigin = "https://top1.com", Origin = "https://top1.com" }
            .StoreCookies(["a=1; Secure; SameSite=None; Partitioned"]);

        Assert.Equal(new CookiePartitionKey { TopLevelSite = "https://top1.com", HasCrossSiteAncestor = true }, Assert.Single(jar.GetCookies()).PartitionKey);
        Assert.Equal("a=1", new DocumentRequest("https://embed.com/") { CookieJar = jar, Destination = RequestDestination.Image, SiteForCookies = "null", TopLevelOrigin = "https://www.top1.com" }.GetCookieHeader());
        Assert.Null(new DocumentRequest("https://embed.com/") { CookieJar = jar, Destination = RequestDestination.Image, SiteForCookies = "null", TopLevelOrigin = "https://top2.com" }.GetCookieHeader());
    }

    [Fact]
    public void Remove_And_Clear_RemoveTheCookies()
    {
        var jar = new CookieJar();
        jar.SetCookie("https://example.com/", "a=1");
        jar.SetCookie("https://example.com/", "b=2");

        Assert.True(jar.Remove(jar.GetCookies()[0]));
        Assert.Equal("b", Assert.Single(jar.GetCookies()).Name);

        jar.Clear();
        Assert.Empty(jar.GetCookies());
    }

    [Fact]
    public void Documentation_Cookies()
    {
        #region WebPlatform_Cookies
        var cookies = new CookieJar();
        cookies.SetCookie("https://example.com/", "session=abc; Path=/; Secure; HttpOnly");
        cookies.SetCookie("https://example.com/", "theme=dark; Path=/");

        using var context = new BrowsingContext(new BrowsingContextOptions { CookieJar = cookies });
        var document = context.Navigate("https://example.com/", _ => "<p>Hello</p>");

        // document.cookie does not see the HttpOnly cookies
        Assert.Equal("theme=dark", document.Cookie);
        document.Cookie = "lang=fr; Path=/";

        var names = cookies.GetCookies("https://example.com/").Select(cookie => cookie.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["lang", "session", "theme"], names);
        #endregion
    }
}
