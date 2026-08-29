using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.Http.Hsts.Tests;
public sealed class HstsDomainPolicyCollectionTests
{
    [Theory]
    [InlineData("google", 1)]
    [InlineData("google.com", 2)]
    [InlineData("foo.google.com", 3)]
    public void CountSegments(string domain, int count)
    {
        Assert.Equal(count, HstsDomainPolicyCollection.CountSegments(domain));
    }

    [Fact]
    public void HstsCollection_Match_IncludeSubdomain_True()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("google.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        Assert.True(hsts.MustUpgradeRequest("google.com"));
        Assert.True(hsts.MustUpgradeRequest("dummy.google.com"));

        Assert.False(hsts.MustUpgradeRequest("example.com"));
        Assert.False(hsts.MustUpgradeRequest("agoogle.com"));
        Assert.False(hsts.MustUpgradeRequest("oogle.com"));
        Assert.False(hsts.MustUpgradeRequest("google.net"));
    }

    [Fact]
    public void HstsCollection_Match_IncludeSubdomain_False()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("google.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        Assert.True(hsts.MustUpgradeRequest("google.com"));
        Assert.False(hsts.MustUpgradeRequest("dummy.google.com"));
        Assert.False(hsts.MustUpgradeRequest("example.com"));
    }

    [Fact]
    public void HstsCollection_Match_MoreSpecificPolicy_IsNotShadowedByParent()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
        hsts.Add("foo.example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        Assert.True(hsts.MustUpgradeRequest("foo.example.com"));
        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.False(hsts.MustUpgradeRequest("bar.example.com"));
        Assert.False(hsts.MustUpgradeRequest("sub.foo.example.com"));
    }

    [Fact]
    public void HstsCollection_Match_MoreSpecificPolicy_IsNotShadowedByExpiredParent()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(-1), includeSubdomains: true);
        hsts.Add("foo.example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        Assert.True(hsts.MustUpgradeRequest("foo.example.com"));
        Assert.True(hsts.MustUpgradeRequest("sub.foo.example.com"));
        Assert.False(hsts.MustUpgradeRequest("example.com"));
        Assert.False(hsts.MustUpgradeRequest("bar.example.com"));
    }

    [Fact]
    public void HstsCollection_Match_ExpiredPolicy()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false);
        hsts.Add("example.com", TimeSpan.FromDays(30), includeSubdomains: true);

        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.True(hsts.MustUpgradeRequest("foo.example.com"));

        timeProvider.Advance(TimeSpan.FromDays(31));

        Assert.False(hsts.MustUpgradeRequest("example.com"));
        Assert.False(hsts.MustUpgradeRequest("foo.example.com"));
    }

    [Fact]
    public void HstsCollection_Match_PreloadedInternationalizedDomain()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        // The preload list stores internationalized domains in their Punycode form,
        // which is what Uri.IdnHost returns (Uri.Host returns the Unicode form)
        Assert.True(hsts.MustUpgradeRequest("xn--vt3a.jp"));
        Assert.True(hsts.MustUpgradeRequest(new Uri("http://跳.jp").IdnHost));
        Assert.True(hsts.MustUpgradeRequest(new Uri("http://sub.αβ.net").IdnHost));
    }

    [Fact]
    public async Task HstsCollection_ConcurrentAddAndLookup()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var barrier = new Barrier(2);

        // The writer grows the bucket list while the reader indexes into it
        var writer = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (var i = 1; i <= 64; i++)
            {
                hsts.Add(string.Join('.', Enumerable.Repeat("a", i)), DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
            }
        });

        var reader = Task.Run(() =>
        {
            var host = string.Join('.', Enumerable.Repeat("b", 64));
            barrier.SignalAndWait();
            for (var i = 0; i < 500_000; i++)
            {
                Assert.False(hsts.MustUpgradeRequest(host));
            }
        });

        await Task.WhenAll(writer, reader);
        Assert.True(hsts.MustUpgradeRequest(string.Join('.', Enumerable.Repeat("a", 64))));
    }

    [Fact]
    public void HstsCollection_PreloadedPolicies_DoNotExpire()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: true);

        Assert.True(hsts.MustUpgradeRequest("github.com"));

        // The preload list is compiled into the assembly; its entries stay valid until the package is updated
        timeProvider.Advance(TimeSpan.FromDays(365 * 100));

        Assert.True(hsts.MustUpgradeRequest("github.com"));
    }

    [Fact]
    public void HstsCollection_Match_TrailingDot()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        hsts.Add("other.com.", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        // A fully-qualified domain name may end with a dot; it designates the same host
        Assert.True(hsts.MustUpgradeRequest("example.com."));
        Assert.True(hsts.MustUpgradeRequest("foo.example.com."));
        Assert.True(hsts.MustUpgradeRequest("other.com"));
        Assert.True(hsts.MustUpgradeRequest("other.com."));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(".com")]
    [InlineData("..com")]
    [InlineData("a..com")]
    [InlineData("a..b.com")]
    public void HstsCollection_Add_RejectsAnEmptyLabel(string host)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);

        // Uri rejects these host names, and stored as is they would land in a bucket no lookup reads
        Assert.Throws<ArgumentException>(() => hsts.Add(host, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true));
        Assert.Empty(hsts);
    }

    [Theory]
    [InlineData("com")]
    [InlineData("example.com")]
    [InlineData("example.com.")]
    [InlineData("a.b.c.example.com")]
    [InlineData("xn--vt3a.jp")]
    public void HstsCollection_Add_AcceptsAWellFormedHost(string host)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add(host, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        Assert.True(hsts.MustUpgradeRequest(host));
    }

    [Fact]
    public void HstsCollection_Remove()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        Assert.True(hsts.Remove("example.com"));
        Assert.False(hsts.MustUpgradeRequest("example.com"));

        Assert.False(hsts.Remove("example.com"));
        Assert.False(hsts.Remove("never-added.com"));

        // The bucket for that segment count does not exist
        Assert.False(hsts.Remove("a.b.c.d.e.f.example.com"));
    }

    [Fact]
    public void HstsCollection_Remove_PreloadedPolicy()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        // Remove is explicit, so it drops preloaded entries too
        Assert.True(hsts.Remove("github.com"));
        Assert.False(hsts.MustUpgradeRequest("github.com"));
    }

    [Fact]
    public void HstsCollection_Add_KeepsThePreloadedFlag()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);
        hsts.Add("github.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        var policy = hsts.Single(entry => entry.Host == "github.com");
        Assert.True(policy.IsPreloaded);
        Assert.False(policy.IncludeSubdomains);
    }

    [Fact]
    public void HstsCollection_Match_UsePreloadDomains()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        Assert.True(hsts.MustUpgradeRequest("whatever.amazon"));
        Assert.True(hsts.MustUpgradeRequest("amazon"));
        Assert.False(hsts.MustUpgradeRequest("zzz"));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness")]
    public void HstsCollection_Parallel()
    {
        var hsts = new HstsDomainPolicyCollection();

        var domains = Enumerable.Range(0, 500_000).Select(GenerateDomainName).ToArray();

        Parallel.ForEach(domains, domain =>
        {
            hsts.Add(domain, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
        });

        Parallel.ForEach(domains, domain =>
        {
            Assert.True(hsts.MustUpgradeRequest(domain));
        });

        Assert.False(hsts.MustUpgradeRequest("dummy.google.com"));

        static string GenerateDomainName(int i)
        {
            var partCount = Random.Shared.Next(1, 16);
            return string.Join('.', Enumerable.Range(0, partCount).Select(_ => Guid.NewGuid().ToString("N").ToLowerInvariant()));
        }
    }

    [Fact]
    public void GetEnumerator()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("google.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        var list = hsts.OrderBy(entry => entry.Host, StringComparer.Ordinal).ToList();
        Assert.Collection(list,
            entry => Assert.Equal("example.com", entry.Host),
            entry => Assert.Equal("google.com", entry.Host));
    }
}
