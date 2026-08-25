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
}
