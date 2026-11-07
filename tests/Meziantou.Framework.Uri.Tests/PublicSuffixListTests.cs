using System.Reflection;

namespace Meziantou.Framework.Uri.Tests;

public sealed class PublicSuffixListTests
{
    public static TheoryData<string?, string?> OfficialTestSuite()
    {
        var data = new TheoryData<string?, string?>();
        using var stream = typeof(PublicSuffixListTests).GetTypeInfo().Assembly.GetManifestResourceStream("public_suffix_tests.txt");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length is 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.HasCount(2, parts);
            data.Add(ParseValue(parts[0]), ParseValue(parts[1]));
        }

        return data;

        static string? ParseValue(string value) => value is "null" ? null : value;
    }

    [Theory]
    [MemberData(nameof(OfficialTestSuite))]
    public void GetRegistrableDomain_OfficialTestSuite(string? domain, string? expected)
    {
        Assert.Equal(expected, PublicSuffixList.GetRegistrableDomain(domain));
    }

    [Fact]
    public void ReadmeExample_Usage()
    {
        #region PublicSuffixList_Usage
        // Get the registrable domain (eTLD+1)
        Assert.Equal("example.co.uk", PublicSuffixList.GetRegistrableDomain("www.example.co.uk"));
        Assert.Null(PublicSuffixList.GetRegistrableDomain("co.uk"));

        // Get the public suffix (eTLD)
        Assert.Equal("co.uk", PublicSuffixList.GetPublicSuffix("www.example.co.uk"));

        // Test if a domain is a public suffix
        Assert.True(PublicSuffixList.IsPublicSuffix("co.uk"));
        Assert.False(PublicSuffixList.IsPublicSuffix("example.com"));

        // Decompose a domain name
        Assert.True(PublicSuffixList.TryGetDomainInfo("www.a.example.co.uk", out DomainInfo info));
        Assert.Equal("co.uk", info.PublicSuffix);
        Assert.Equal("example.co.uk", info.RegistrableDomain);
        Assert.Equal("www.a", info.Subdomain);
        Assert.Equal(PublicSuffixRuleSources.Icann, info.Source);
        Assert.True(info.IsKnownPublicSuffix);

        // A Uri can be used directly
        Assert.True(PublicSuffixList.TryGetDomainInfo(new System.Uri("https://www.example.co.uk/path"), out _));
        #endregion
    }

    [Fact]
    public void ReadmeExample_RuleSources()
    {
        #region PublicSuffixList_RuleSources
        // blogspot.com is a private rule
        Assert.Equal("foo.blogspot.com", PublicSuffixList.GetRegistrableDomain("foo.blogspot.com"));
        Assert.Equal("blogspot.com", PublicSuffixList.GetRegistrableDomain("foo.blogspot.com", PublicSuffixRuleSources.Icann));
        #endregion
    }

    [Fact]
    public void GetPublicSuffix_MultiLabelSuffix()
    {
        Assert.Equal("co.uk", PublicSuffixList.GetPublicSuffix("www.example.co.uk"));
    }

    [Fact]
    public void TryGetDomainInfo_SplitsTheDomain()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("www.a.example.co.uk", out var domainInfo));
        Assert.Equal("www.a.example.co.uk", domainInfo.Domain);
        Assert.Equal("co.uk", domainInfo.PublicSuffix);
        Assert.Equal("example.co.uk", domainInfo.RegistrableDomain);
        Assert.Equal("www.a", domainInfo.Subdomain);
        Assert.Equal(PublicSuffixRuleSources.Icann, domainInfo.Source);
        Assert.True(domainInfo.IsKnownPublicSuffix);
    }

    [Fact]
    public void TryGetDomainInfo_NoSubdomain()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("example.com", out var domainInfo));
        Assert.Equal("example.com", domainInfo.RegistrableDomain);
        Assert.Null(domainInfo.Subdomain);
    }

    [Fact]
    public void TryGetDomainInfo_DomainIsAPublicSuffix()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("co.uk", out var domainInfo));
        Assert.Equal("co.uk", domainInfo.PublicSuffix);
        Assert.Null(domainInfo.RegistrableDomain);
        Assert.Null(domainInfo.Subdomain);
    }

    [Fact]
    public void TryGetDomainInfo_UnknownTopLevelDomainUsesTheImplicitRule()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("www.example.unknowntld", out var domainInfo));
        Assert.Equal("unknowntld", domainInfo.PublicSuffix);
        Assert.Equal("example.unknowntld", domainInfo.RegistrableDomain);
        Assert.False(domainInfo.IsKnownPublicSuffix);
        Assert.Equal(PublicSuffixRuleSources.None, domainInfo.Source);
    }

    [Fact]
    public void TryGetDomainInfo_WildcardRule()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("www.example.kawasaki.jp", out var domainInfo));
        Assert.Equal("example.kawasaki.jp", domainInfo.PublicSuffix);
        Assert.Equal("www.example.kawasaki.jp", domainInfo.RegistrableDomain);
    }

    [Fact]
    public void TryGetDomainInfo_ExceptionRule()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("www.city.kawasaki.jp", out var domainInfo));
        Assert.Equal("kawasaki.jp", domainInfo.PublicSuffix);
        Assert.Equal("city.kawasaki.jp", domainInfo.RegistrableDomain);
    }

    [Fact]
    public void TryGetDomainInfo_TrailingDot()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("www.example.com.", out var domainInfo));
        Assert.Equal("www.example.com", domainInfo.Domain);
        Assert.Equal("example.com", domainInfo.RegistrableDomain);
    }

    [Fact]
    public void TryGetDomainInfo_Uri()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo(new System.Uri("https://www.example.co.uk/path?q=1"), out var domainInfo));
        Assert.Equal("example.co.uk", domainInfo.RegistrableDomain);
    }

    [Fact]
    public void TryGetDomainInfo_UriWithIPAddress()
    {
        Assert.False(PublicSuffixList.TryGetDomainInfo(new System.Uri("https://127.0.0.1/"), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData(".com")]
    [InlineData("example..com")]
    public void TryGetDomainInfo_InvalidDomain(string? domain)
    {
        Assert.False(PublicSuffixList.TryGetDomainInfo(domain, out _));
        Assert.Null(PublicSuffixList.GetPublicSuffix(domain));
        Assert.Null(PublicSuffixList.GetRegistrableDomain(domain));
        Assert.False(PublicSuffixList.IsPublicSuffix(domain));
    }

    [Fact]
    public void NullDomainDoesNotThrow()
    {
        Assert.False(PublicSuffixList.TryGetDomainInfo(domain: null, out var domainInfo));
        Assert.Null(domainInfo.RegistrableDomain);
        Assert.Null(domainInfo.Subdomain);
        Assert.Equal("", domainInfo.Domain);
        Assert.Equal("", domainInfo.PublicSuffix);
        Assert.Null(PublicSuffixList.GetPublicSuffix(domain: null));
        Assert.Null(PublicSuffixList.GetRegistrableDomain(domain: null));
        Assert.False(PublicSuffixList.IsPublicSuffix(domain: null));
    }

    [Fact]
    public void EmptySpanDoesNotThrow()
    {
        Assert.False(PublicSuffixList.TryGetDomainInfo([], out _));
        Assert.Null(PublicSuffixList.GetPublicSuffix([]));
        Assert.Null(PublicSuffixList.GetRegistrableDomain([]));
        Assert.False(PublicSuffixList.IsPublicSuffix([]));
    }

    [Fact]
    public void TryGetDomainInfo_NullUriThrows()
    {
        Assert.Throws<ArgumentNullException>(() => PublicSuffixList.TryGetDomainInfo((System.Uri)null!, out _));
    }

    [Fact]
    public void GetRegistrableDomain_PrivateRule()
    {
        Assert.Equal("foo.blogspot.com", PublicSuffixList.GetRegistrableDomain("www.foo.blogspot.com"));
    }

    [Fact]
    public void GetRegistrableDomain_PrivateRuleIsIgnoredWhenOnlyIcannIsRequested()
    {
        Assert.Equal("blogspot.com", PublicSuffixList.GetRegistrableDomain("www.foo.blogspot.com", PublicSuffixRuleSources.Icann));
    }

    [Fact]
    public void TryGetDomainInfo_ReportsThePrivateSection()
    {
        Assert.True(PublicSuffixList.TryGetDomainInfo("foo.blogspot.com", out var domainInfo));
        Assert.Equal("blogspot.com", domainInfo.PublicSuffix);
        Assert.Equal(PublicSuffixRuleSources.Private, domainInfo.Source);
    }

    [Fact]
    public void TryGetDomainInfo_NoSourceMatchesNothing()
    {
        Assert.False(PublicSuffixList.TryGetDomainInfo("example.com", out _, PublicSuffixRuleSources.None));
    }

    [Theory]
    [InlineData("com", true)]
    [InlineData("co.uk", true)]
    [InlineData("blogspot.com", true)]
    [InlineData("example.com", false)]
    [InlineData("unknowntld", false)]
    public void IsPublicSuffix(string domain, bool expected)
    {
        Assert.Equal(expected, PublicSuffixList.IsPublicSuffix(domain));
    }

    [Fact]
    public void IsPublicSuffix_PrivateRuleIsIgnoredWhenOnlyIcannIsRequested()
    {
        Assert.False(PublicSuffixList.IsPublicSuffix("blogspot.com", PublicSuffixRuleSources.Icann));
    }

    [Fact]
    public void GetPublicSuffix_IsCaseInsensitive()
    {
        Assert.Equal("com", PublicSuffixList.GetPublicSuffix("WwW.Example.COM"));
    }

    [Fact]
    public void GetPublicSuffix_Span()
    {
        Assert.Equal("co.uk", PublicSuffixList.GetPublicSuffix("www.example.co.uk".AsSpan()));
    }

    [Fact]
    public void EmbeddedListIsLoaded()
    {
        Assert.True(PublicSuffixList.RuleCount > 5000);
        Assert.True(PublicSuffixList.LastUpdated > new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Documentation_SameSite()
    {
        #region PublicSuffixList_SameSite
        static bool IsSameSite(string host1, string host2)
        {
            var site1 = PublicSuffixList.GetRegistrableDomain(host1);
            var site2 = PublicSuffixList.GetRegistrableDomain(host2);
            return site1 is not null && site1 == site2;
        }

        Assert.True(IsSameSite("www.example.co.uk", "shop.example.co.uk"));
        Assert.False(IsSameSite("example.co.uk", "contoso.co.uk"));

        // github.io is a public suffix: two sites hosted there are different sites
        Assert.False(IsSameSite("alice.github.io", "bob.github.io"));
        #endregion
    }

    [Fact]
    public void Documentation_Normalization()
    {
        #region PublicSuffixList_Normalization
        // The domain is lower-cased and a trailing dot is ignored
        Assert.True(PublicSuffixList.TryGetDomainInfo("WWW.Example.COM.", out var info));
        Assert.Equal("www.example.com", info.Domain);
        Assert.Equal("example.com", info.RegistrableDomain);

        // A top-level domain that is not in the list is still a public suffix, by the implicit "*" rule
        Assert.True(PublicSuffixList.TryGetDomainInfo("www.example.unknowntld", out var unknown));
        Assert.Equal("unknowntld", unknown.PublicSuffix);
        Assert.Equal("example.unknowntld", unknown.RegistrableDomain);
        Assert.False(unknown.IsKnownPublicSuffix);
        Assert.Equal(PublicSuffixRuleSources.None, unknown.Source);
        Assert.False(PublicSuffixList.IsPublicSuffix("unknowntld"));

        // A value that is not a domain name
        Assert.False(PublicSuffixList.TryGetDomainInfo("example..com", out _));
        Assert.Null(PublicSuffixList.GetRegistrableDomain(""));

        // The host of a Uri must be a domain name, not an IP address
        Assert.False(PublicSuffixList.TryGetDomainInfo(new System.Uri("https://192.168.1.1/"), out _));
        #endregion
    }
}
