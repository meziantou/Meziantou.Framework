using System.Net;
using Meziantou.Framework.DnsClient.Helpers;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class ReverseLookupHelperTests
{
    [Fact]
    public void GetReverseLookupDomain_IPv4()
    {
        var address = IPAddress.Parse("192.0.2.1");
        var result = ReverseLookupHelper.GetReverseLookupDomain(address);
        Assert.Equal("1.2.0.192.in-addr.arpa", result);
    }

    [Fact]
    public void GetReverseLookupDomain_IPv4_Loopback()
    {
        var address = IPAddress.Loopback;
        var result = ReverseLookupHelper.GetReverseLookupDomain(address);
        Assert.Equal("1.0.0.127.in-addr.arpa", result);
    }

    [Fact]
    public void GetReverseLookupDomain_IPv6_Full()
    {
        var address = IPAddress.Parse("2001:db8::1");
        var result = ReverseLookupHelper.GetReverseLookupDomain(address);
        Assert.Equal("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.8.b.d.0.1.0.0.2.ip6.arpa", result);
    }

    [Fact]
    public void GetReverseLookupDomain_IPv6_Loopback()
    {
        var address = IPAddress.IPv6Loopback;
        var result = ReverseLookupHelper.GetReverseLookupDomain(address);
        Assert.Equal("1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.ip6.arpa", result);
    }

    [Fact]
    public void GetReverseLookupDomain_IPv4_CloudflareDns()
    {
        var address = IPAddress.Parse("1.1.1.1");
        var result = ReverseLookupHelper.GetReverseLookupDomain(address);
        Assert.Equal("1.1.1.1.in-addr.arpa", result);
    }
}
