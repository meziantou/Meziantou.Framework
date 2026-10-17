using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class CookiePersistenceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static string? GetCookieHeader(CookieJar jar, string url) => new DocumentRequest(url) { CookieJar = jar }.GetCookieHeader();

    [Fact]
    public void PersistentCookies_AreStoredAndLoadedByAnotherJar()
    {
        var persistence = new TestCookiePersistence();
        var jar = new CookieJar(persistence, _clock);
        jar.SetCookie("https://example.com/", "a=1; Max-Age=3600");
        _clock.Advance(TimeSpan.FromSeconds(1));
        jar.SetCookie("https://example.com/dir/page", "b=2; Path=/dir; Max-Age=3600; HttpOnly; SameSite=Strict");
        jar.SetCookie("https://example.com/", "c=3; Max-Age=3600");
        jar.SetCookie("https://example.com/", "session=4");
        new DocumentRequest("https://embed.com/") { CookieJar = jar, Destination = RequestDestination.Image, SiteForCookies = "null", TopLevelOrigin = "https://top.com", Origin = "https://top.com" }
            .StoreCookies(["p=5; Secure; SameSite=None; Partitioned; Max-Age=3600"]);

        Assert.Equal(["set a", "set b", "set c", "set p"], persistence.Log);

        var other = new CookieJar(persistence, _clock);
        Assert.Equal("b=2; a=1; c=3", GetCookieHeader(other, "https://example.com/dir/page"));
        var b = other.GetCookies().Single(cookie => cookie.Name == "b");
        Assert.True(b.HttpOnly);
        Assert.Equal(CookieSameSite.Strict, b.SameSite);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(3600), b.Expires);
        Assert.Equal(new CookiePartitionKey { TopLevelSite = "https://top.com", HasCrossSiteAncestor = true }, other.GetCookies().Single(cookie => cookie.Name == "p").PartitionKey);
        Assert.Equal("p=5", new DocumentRequest("https://embed.com/") { CookieJar = other, Destination = RequestDestination.Image, SiteForCookies = "null", TopLevelOrigin = "https://top.com" }.GetCookieHeader());
    }

    [Fact]
    public void Changes_AreReported()
    {
        var persistence = new TestCookiePersistence();
        var jar = new CookieJar(persistence, _clock);
        jar.SetCookie("https://example.com/", "a=1; Max-Age=3600");
        jar.SetCookie("https://example.com/", "a=1; Max-Age=3600");
        jar.SetCookie("https://example.com/", "a=2; Max-Age=3600");
        jar.SetCookie("https://example.com/", "b=1; Max-Age=60");
        jar.SetCookie("https://example.com/", "c=1; Max-Age=3600");
        jar.SetCookie("https://example.com/", "c=; Max-Age=0");
        Assert.True(jar.Remove(jar.GetCookies().Single(cookie => cookie.Name == "a")));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty(jar.GetCookies());

        Assert.Equal(["set a", "set a", "set b", "set c", "remove c", "remove a", "remove b"], persistence.Log);
        Assert.Empty(persistence.Cookies);
    }

    [Fact]
    public void PersistentCookieReplacedBySessionCookie_IsRemoved()
    {
        var persistence = new TestCookiePersistence();
        var jar = new CookieJar(persistence, _clock);
        jar.SetCookie("https://example.com/", "a=1; Max-Age=3600");
        jar.SetCookie("https://example.com/", "a=2");
        jar.SetCookie("https://example.com/", "a=3");
        jar.SetCookie("https://example.com/", "a=4; Max-Age=3600");

        Assert.Equal(["set a", "remove a", "set a"], persistence.Log);
        Assert.Equal("4", Assert.Single(persistence.Cookies.Values).Value);
    }

    [Fact]
    public void Clear_ClearsThePersistence()
    {
        var persistence = new TestCookiePersistence();
        var jar = new CookieJar(persistence, _clock);
        jar.SetCookie("https://example.com/", "a=1; Max-Age=3600");
        jar.SetCookie("https://example.com/", "b=1; Max-Age=3600");
        jar.Clear();

        Assert.Equal(["set a", "set b", "clear"], persistence.Log);
        Assert.Empty(persistence.Cookies);
    }

    [Fact]
    public void Load_IgnoresTheInvalidAndExpiredCookies()
    {
        var now = _clock.GetUtcNow();
        var persistence = new TestCookiePersistence();
        persistence.Add(new StoredCookie { Name = "valid", Value = "1", Domain = "example.com", Path = "/", HostOnly = true, Expires = now.AddYears(5), CreationTime = now });
        persistence.Add(new StoredCookie { Name = "expired", Value = "1", Domain = "example.com", Path = "/", HostOnly = true, Expires = now, CreationTime = now });
        persistence.Add(new StoredCookie { Name = "no-domain", Value = "1", Domain = "", Path = "/", Expires = now.AddDays(1) });
        persistence.Add(new StoredCookie { Name = "relative-path", Value = "1", Domain = "example.com", Path = "dir", Expires = now.AddDays(1) });
        persistence.Add(new StoredCookie { Name = "session", Value = "1", Domain = "example.com", Path = "/", HostOnly = true, CreationTime = now });

        var jar = new CookieJar(persistence, _clock);

        Assert.Equal(["remove expired"], persistence.Log);
        Assert.Equal("valid=1; session=1", GetCookieHeader(jar, "https://example.com/"));
        Assert.Equal(now.AddDays(400), jar.GetCookies().Single(cookie => cookie.Name == "valid").Expires);

        // A loaded session cookie is removed from the persistence when it changes
        jar.SetCookie("https://example.com/", "session=2");
        Assert.Equal("remove session", persistence.Log[^1]);
    }

    [Fact]
    public void Load_RemovesTheExcessCookies()
    {
        var now = _clock.GetUtcNow();
        var persistence = new TestCookiePersistence();
        for (var i = 0; i < 181; i++)
        {
            persistence.Add(new StoredCookie { Name = "c" + i, Value = "1", Domain = "example.com", Path = "/", HostOnly = true, Expires = now.AddDays(1), CreationTime = now, LastAccessTime = now.AddSeconds(i == 42 ? -1 : i) });
        }

        var jar = new CookieJar(persistence, _clock);

        Assert.Equal(180, jar.GetCookies().Count);
        Assert.Equal(["remove c42"], persistence.Log);
    }

    [Fact]
    public void LastAccessTime_IsPersistedAtMostOnceAMinute()
    {
        var persistence = new TestCookiePersistence();
        var jar = new CookieJar(persistence, _clock);
        jar.SetCookie("https://example.com/", "a=1; Max-Age=36000");
        jar.SetCookie("https://example.com/", "session=1");
        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal("a=1; session=1", GetCookieHeader(jar, "https://example.com/"));
        }

        Assert.Equal(["set a", "set a", "set a"], persistence.Log);
        Assert.Equal(_clock.GetUtcNow().AddSeconds(-30), Assert.Single(persistence.Cookies.Values).LastAccessTime);
    }

    private sealed class TestCookiePersistence : CookiePersistence
    {
        public Dictionary<(string Name, string Domain, bool HostOnly, string Path, CookiePartitionKey? PartitionKey), StoredCookie> Cookies { get; } = [];

        public List<string> Log { get; } = [];

        public void Add(StoredCookie cookie) => Cookies[GetIdentity(cookie)] = cookie;

        public override IEnumerable<StoredCookie> LoadCookies() => [.. Cookies.Values];

        public override void SetCookie(StoredCookie cookie)
        {
            Log.Add("set " + cookie.Name);
            Cookies[GetIdentity(cookie)] = cookie;
        }

        public override void RemoveCookie(StoredCookie cookie)
        {
            Log.Add("remove " + cookie.Name);
            Cookies.Remove(GetIdentity(cookie));
        }

        public override void ClearAll()
        {
            Log.Add("clear");
            Cookies.Clear();
        }

        private static (string, string, bool, string, CookiePartitionKey?) GetIdentity(StoredCookie cookie) => (cookie.Name, cookie.Domain, cookie.HostOnly, cookie.Path, cookie.PartitionKey);
    }
}
