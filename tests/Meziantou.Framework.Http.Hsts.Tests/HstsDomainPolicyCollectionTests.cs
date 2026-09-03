using Meziantou.Xunit;
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
    public void HstsCollection_Add_VeryLargeMaxAge_Saturates()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", TimeSpan.MaxValue, includeSubdomains: false);

        Assert.Equal(DateTimeOffset.MaxValue, Assert.Single(hsts).ExpiresAt);
        Assert.True(hsts.MustUpgradeRequest("example.com"));
    }

    [Fact]
    public void HstsCollection_Add_MaxAgeBeyondTheRepresentableRange_Saturates()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.MaxValue.AddYears(-1));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false);

        // The handler clamps max-age to 100 years before it gets here, but the collection is public
        hsts.Add("example.com", TimeSpan.FromDays(365 * 100), includeSubdomains: false);

        Assert.Equal(DateTimeOffset.MaxValue, Assert.Single(hsts).ExpiresAt);
    }

    [Fact]
    public void HstsCollection_Add_VeryNegativeMaxAge_Saturates()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", TimeSpan.MinValue, includeSubdomains: false);

        Assert.Equal(DateTimeOffset.MinValue, Assert.Single(hsts).ExpiresAt);
        Assert.False(hsts.MustUpgradeRequest("example.com"));
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
        // .invalid is reserved, so this cannot start matching when the preload list is regenerated
        Assert.False(hsts.MustUpgradeRequest(Guid.NewGuid().ToString("N") + ".invalid"));
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
    public void HstsCollection_ExpiredPolicy_IsDroppedOnLookup()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false);
        hsts.Add("example.com", TimeSpan.FromDays(30), includeSubdomains: true);

        timeProvider.Advance(TimeSpan.FromDays(31));
        Assert.False(hsts.MustUpgradeRequest("example.com"));

        // Otherwise a process that talks to many hosts keeps every policy it has ever learned
        Assert.Empty(hsts);
    }

    [Fact]
    public void HstsCollection_ExpiredPolicy_IsDroppedWithoutHidingAMoreSpecificOne()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false);
        hsts.Add("example.com", TimeSpan.FromDays(30), includeSubdomains: true);
        hsts.Add("foo.example.com", TimeSpan.FromDays(90), includeSubdomains: true);

        timeProvider.Advance(TimeSpan.FromDays(31));

        Assert.True(hsts.MustUpgradeRequest("foo.example.com"));
        Assert.Equal("foo.example.com", Assert.Single(hsts).Host);
    }

    [Fact]
    public void HstsCollection_RenewedPolicy_IsNotDroppedByAConcurrentLookup()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false);
        hsts.Add("example.com", TimeSpan.FromDays(30), includeSubdomains: true);

        timeProvider.Advance(TimeSpan.FromDays(31));
        hsts.Add("example.com", TimeSpan.FromDays(30), includeSubdomains: true);

        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.Single(hsts);
    }

    [Fact]
    public void HstsCollection_PreloadedPolicy_IsNotDroppedOnLookup()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: true);

        timeProvider.Advance(TimeSpan.FromDays(365 * 100));

        Assert.True(hsts.MustUpgradeRequest("github.com"));
        Assert.Contains(hsts, policy => policy.Host == "github.com");
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

    [Theory]
    [InlineData("跳.jp")]
    [InlineData("xn--vt3a.jp")]
    public void HstsCollection_Match_UnicodeAndPunycodeAreInterchangeable(string addedHost)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add(addedHost, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-10
        Assert.True(hsts.MustUpgradeRequest("跳.jp"));
        Assert.True(hsts.MustUpgradeRequest("xn--vt3a.jp"));
    }

    [Fact]
    public void HstsCollection_Add_StoresTheCanonicalizedHost()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("跳.jp", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        Assert.Equal("xn--vt3a.jp", Assert.Single(hsts).Host);
    }

    [Theory]
    [InlineData("跳.jp")]
    [InlineData("xn--vt3a.jp")]
    public void HstsCollection_Remove_AcceptsEitherForm(string removedHost)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("跳.jp", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        Assert.True(hsts.Remove(removedHost));
        Assert.Empty(hsts);
    }

    [Fact]
    public void HstsCollection_Match_UnicodeSubdomainOfAnInternationalizedDomain()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("跳.jp", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        Assert.True(hsts.MustUpgradeRequest("sub.跳.jp"));
        Assert.True(hsts.MustUpgradeRequest("sub.xn--vt3a.jp"));
    }

    [Fact]
    public void HstsCollection_Match_PreloadedDomainInItsUnicodeForm()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        // The preload list stores Punycode names; the Unicode form of the same domain must match them
        Assert.True(hsts.MustUpgradeRequest("跳.jp"));
        Assert.True(hsts.MustUpgradeRequest("sub.αβ.net"));
    }

    [Fact]
    public void HstsCollection_Match_UnconvertibleHostDoesNotThrow()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);

        // A name that cannot be canonicalized cannot match a policy, but a lookup must not fail the request
        Assert.False(hsts.MustUpgradeRequest("\u0080.example"));
        Assert.False(hsts.Remove("\u0080.example"));
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void HstsCollection_Add_RejectsAnUnconvertibleHost()
    {
        // IDNA validation follows the platform: ICU rejects a disallowed code point, while the managed
        // fallback used in globalization-invariant mode encodes it instead. Add throws whenever the
        // conversion fails, so only the strict mode reaches the exception.
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);

        Assert.Throws<ArgumentException>(() => hsts.Add("\u0080.example", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false));
        Assert.Empty(hsts);
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
    public void HstsCollection_Add_CannotNarrowAPreloadedPolicy()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        // github.com is preloaded with includeSubdomains, and a narrower policy must not take that away
        hsts.Add("github.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        var policy = hsts.Single(entry => entry.Host == "github.com");
        Assert.True(policy.IsPreloaded);
        Assert.True(policy.IncludeSubdomains);
        Assert.Equal(DateTimeOffset.MaxValue, policy.ExpiresAt);
        Assert.True(hsts.MustUpgradeRequest("sub.github.com"));
    }

    [Fact]
    public void HstsCollection_Add_WidensAPreloadedPolicy()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        // Picked from the data rather than hardcoded, so regenerating the preload list cannot invalidate it
        var host = hsts.First(policy => policy.IsPreloaded && !policy.IncludeSubdomains).Host;
        Assert.False(hsts.MustUpgradeRequest("sub." + host));

        hsts.Add(host, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        Assert.True(hsts.MustUpgradeRequest("sub." + host));
        Assert.True(hsts.Single(entry => entry.Host == host).IncludeSubdomains);
    }

    [Fact]
    public void HstsCollection_ExpiredPolicyOnAPreloadedHost_DoesNotRemoveThePreloadedEntry()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: true);
        hsts.Add("github.com", TimeSpan.FromSeconds(1), includeSubdomains: false);

        timeProvider.Advance(TimeSpan.FromDays(1));

        // The learned policy lapses; the preload entry underneath it is untouched
        Assert.True(hsts.MustUpgradeRequest("github.com"));
        Assert.True(hsts.MustUpgradeRequest("sub.github.com"));
        Assert.Contains(hsts, policy => policy.Host == "github.com");
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
        // The cap is raised so this stays a concurrency test; HstsCollection_LearnedPolicies_AreCapped covers eviction
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true, maxLearnedPolicies: 1_000_000);

        var domains = Enumerable.Range(0, 500_000).Select(GenerateDomainName).ToArray();

        Parallel.ForEach(domains, domain =>
        {
            hsts.Add(domain, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
        });

        Parallel.ForEach(domains, domain =>
        {
            Assert.True(hsts.MustUpgradeRequest(domain));
        });

        // .invalid is reserved, so this cannot start matching when the preload list is regenerated
        Assert.False(hsts.MustUpgradeRequest(Guid.NewGuid().ToString("N") + ".invalid"));

        static string GenerateDomainName(int i)
        {
            var partCount = Random.Shared.Next(1, 16);
            return string.Join('.', Enumerable.Range(0, partCount).Select(_ => Guid.NewGuid().ToString("N").ToLowerInvariant()));
        }
    }

    [Fact]
    public void HstsDomainPolicy_ToString_UsesTheInvariantFormat()
    {
        var policy = CreatePolicyExpiringOn2030();

        Assert.Equal("example.com; expires=2030-03-04T05:06:07.0000000+00:00; includeSubdomains", policy.ToString());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void HstsDomainPolicy_ToString_DoesNotDependOnTheCulture(string culture)
    {
        var policy = CreatePolicyExpiringOn2030();

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("example.com; expires=2030-03-04T05:06:07.0000000+00:00; includeSubdomains", policy.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("example.com:443")]
    [InlineData("https://example.com/")]
    [InlineData("example.com/path")]
    [InlineData("user@example.com")]
    [InlineData("exam ple.com")]
    [InlineData("-example.com")]
    public void HstsCollection_Add_RejectsAHostARequestCanNeverProduce(string host)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);

        // Uri.IdnHost never yields a scheme, a port, a path, userinfo or whitespace, so a policy stored under
        // one of these would silently never match while every signal said it was added
        Assert.Throws<ArgumentException>(() => hsts.Add(host, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true));
        Assert.Empty(hsts);
    }

    [Theory]
    [InlineData("a_b.com")]
    [InlineData("192.168.0.1")]
    [InlineData("a-.com")]
    public void HstsCollection_Add_AcceptsAnUnusualButValidHost(string host)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add(host, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        Assert.True(hsts.MustUpgradeRequest(new Uri("http://" + host).IdnHost));
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void HstsCollection_Add_StoresAnIPv6LiteralInTheFormUriProduces(string host)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add(host, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);

        // Uri.IdnHost reports an IPv6 literal without brackets, so a bracketed key would never match
        Assert.Equal("::1", Assert.Single(hsts).Host);
        Assert.True(hsts.MustUpgradeRequest(new Uri("http://[::1]").IdnHost));
    }

    [Theory]
    [InlineData("MyHost.Example.com", "myhost.example.com")]
    [InlineData("myhost.example.com", "MYHOST.EXAMPLE.COM")]
    public void HstsCollection_Match_IsCaseInsensitive(string addedHost, string lookedUpHost)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add(addedHost, DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-8.2
        Assert.True(hsts.MustUpgradeRequest(lookedUpHost));
        Assert.True(hsts.MustUpgradeRequest("sub." + lookedUpHost));
        Assert.True(hsts.TryGetPolicy(lookedUpHost, out _));
        Assert.True(hsts.Remove(lookedUpHost));
    }

    [Fact]
    public void HstsCollection_Match_PreloadedHostIsCaseInsensitive()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        Assert.True(hsts.MustUpgradeRequest("GitHub.COM"));
        Assert.True(hsts.MustUpgradeRequest("SUB.GITHUB.COM"));
    }

    [Fact]
    public void HstsCollection_Add_TrimsATrailingDotIntroducedByTheIdnaMapping()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);

        // U+3002 IDEOGRAPHIC FULL STOP maps to '.', which would leave a trailing dot on the stored key
        hsts.Add("example.com\u3002", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        Assert.Equal("example.com", Assert.Single(hsts).Host);
        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.True(hsts.MustUpgradeRequest("sub.example.com"));
    }

    [Fact]
    public void HstsCollection_TryGetPolicy()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", new DateTimeOffset(2030, 3, 4, 5, 6, 7, TimeSpan.Zero), includeSubdomains: true);

        Assert.True(hsts.TryGetPolicy("example.com", out var policy));
        Assert.Equal("example.com", policy.Host);
        Assert.Equal(new DateTimeOffset(2030, 3, 4, 5, 6, 7, TimeSpan.Zero), policy.ExpiresAt);
        Assert.True(policy.IncludeSubdomains);
        Assert.False(policy.IsPreloaded);

        Assert.False(hsts.TryGetPolicy("other.com", out _));

        // A host covered only by its parent's includeSubdomains has no policy of its own
        Assert.True(hsts.MustUpgradeRequest("sub.example.com"));
        Assert.False(hsts.TryGetPolicy("sub.example.com", out _));
    }

    [Fact]
    public void HstsCollection_TryGetPolicy_ReportsAPreloadedHost()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        Assert.True(hsts.TryGetPolicy("github.com", out var policy));
        Assert.True(policy.IsPreloaded);
        Assert.True(policy.IncludeSubdomains);
        Assert.Equal(DateTimeOffset.MaxValue, policy.ExpiresAt);
    }

    [Fact]
    public void HstsCollection_ClearLearnedPolicies()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        Assert.True(hsts.Remove("github.com"));

        hsts.ClearLearnedPolicies();

        // Learned policies are dropped and the preload entry Remove masked is back
        Assert.False(hsts.MustUpgradeRequest("example.com"));
        Assert.True(hsts.MustUpgradeRequest("github.com"));
    }

    [Fact]
    public void HstsCollection_Remove_OfAPreloadedHostDoesNotAffectAnotherCollection()
    {
        var first = new HstsDomainPolicyCollection(includePreloadDomains: true);
        var second = new HstsDomainPolicyCollection(includePreloadDomains: true);

        Assert.True(first.Remove("github.com"));

        // The preload data is shared and immutable, so removing masks it for one collection only
        Assert.False(first.MustUpgradeRequest("github.com"));
        Assert.True(second.MustUpgradeRequest("github.com"));
    }

    [Fact]
    public void HstsCollection_LearnedPolicies_AreCapped()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false, maxLearnedPolicies: 16);

        for (var i = 0; i < 500; i++)
        {
            hsts.Add($"host{i.ToString(CultureInfo.InvariantCulture)}.example", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
        }

        // An application fed host names by a remote peer must not be able to grow the store without bound
        Assert.InRange(hsts.Count(), 1, 17);
    }

    [Fact]
    public void HstsCollection_ExpiredPolicy_IsSweptWithoutBeingLookedUp()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false, maxLearnedPolicies: 10_000);
        hsts.Add("forgotten.example", TimeSpan.FromDays(1), includeSubdomains: false);

        timeProvider.Advance(TimeSpan.FromDays(2));

        // Nothing ever looks this host up again, so only the periodic sweep can drop it
        for (var i = 0; i < 300; i++)
        {
            hsts.Add($"host{i.ToString(CultureInfo.InvariantCulture)}.example", TimeSpan.FromDays(365), includeSubdomains: false);
        }

        Assert.DoesNotContain(hsts, policy => policy.Host == "forgotten.example");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HstsCollection_Constructor_RejectsANonPositiveLimit(int maxLearnedPolicies)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HstsDomainPolicyCollection(includePreloadDomains: false, maxLearnedPolicies: maxLearnedPolicies));
    }

    [Fact]
    public void HstsCollection_EveryPreloadedHostIsFoundByLookup()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);

        // The preload entries are binary-searched inside a sorted blob, so a sample spread across the whole
        // data set is what proves the ordering and the case folding agree with the search
        var sampled = 0;
        var index = 0;
        foreach (var policy in hsts)
        {
            if (index++ % 499 != 0)
                continue;

            sampled++;
            Assert.True(hsts.MustUpgradeRequest(policy.Host), policy.Host);
            Assert.True(hsts.MustUpgradeRequest(policy.Host.ToUpperInvariant()), policy.Host);
            Assert.True(hsts.TryGetPolicy(policy.Host, out var found), policy.Host);
            Assert.Equal(policy.IncludeSubdomains, found.IncludeSubdomains);
        }

        Assert.InRange(sampled, 100, int.MaxValue);
        Assert.False(hsts.MustUpgradeRequest(Guid.NewGuid().ToString("N") + ".invalid"));
    }

    private static HstsDomainPolicy CreatePolicyExpiringOn2030()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", new DateTimeOffset(2030, 3, 4, 5, 6, 7, TimeSpan.Zero), includeSubdomains: true);
        return Assert.Single(hsts);
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
