using Xunit;
using Xunit.Internal;

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

        Assert.True(hsts.Match("google.com"));
        Assert.True(hsts.Match("dummy.google.com"));

        Assert.False(hsts.Match("example.com"));
        Assert.False(hsts.Match("agoogle.com"));
        Assert.False(hsts.Match("oogle.com"));
        Assert.False(hsts.Match("google.net"));
    }

    [Fact]
    public void HstsCollection_Match_IncludeSubdomain_False()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("google.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        Assert.True(hsts.Match("google.com"));
        Assert.False(hsts.Match("dummy.google.com"));
        Assert.False(hsts.Match("example.com"));
    }

    [Fact]
    public void HstsCollection_Match_UsePreloadDomains()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        Assert.True(hsts.Match("whatever.amazon"));
        Assert.True(hsts.Match("amazon"));
        Assert.False(hsts.Match("zzz"));
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
            Assert.True(hsts.Match(domain));
        });

        Assert.False(hsts.Match("dummy.google.com"));

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
