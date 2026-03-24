using System.Net;
using System.Net.Sockets;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class DnsRecordExtensionsTests
{
    [Fact]
    public void GetIPAddresses_ReturnsIPv4AndIPv6()
    {
        var records = new DnsRecord[]
        {
            CreateARecord("1.2.3.4"),
            CreateAaaaRecord("::1"),
            CreateARecord("5.6.7.8"),
        };

        var addresses = records.GetIPAddresses().ToList();

        Assert.Equal(3, addresses.Count);
        Assert.Equal(IPAddress.Parse("1.2.3.4"), addresses[0]);
        Assert.Equal(IPAddress.Parse("::1"), addresses[1]);
        Assert.Equal(IPAddress.Parse("5.6.7.8"), addresses[2]);
    }

    [Fact]
    public void GetIPAddresses_IgnoresNonAddressRecords()
    {
        var records = new DnsRecord[]
        {
            CreateARecord("1.1.1.1"),
            new DnsMxRecord(),
            new DnsTxtRecord(),
            CreateAaaaRecord("2606:4700::1"),
        };

        var addresses = records.GetIPAddresses().ToList();

        Assert.Equal(2, addresses.Count);
        Assert.Equal(IPAddress.Parse("1.1.1.1"), addresses[0]);
        Assert.Equal(IPAddress.Parse("2606:4700::1"), addresses[1]);
    }

    [Fact]
    public void GetIPAddresses_EmptyCollection_ReturnsEmpty()
    {
        var records = Array.Empty<DnsRecord>();

        Assert.Empty(records.GetIPAddresses());
    }

    [Fact]
    public void GetIPv4Addresses_ReturnsOnlyIPv4()
    {
        var records = new DnsRecord[]
        {
            CreateARecord("1.2.3.4"),
            CreateAaaaRecord("::1"),
            CreateARecord("5.6.7.8"),
        };

        var addresses = records.GetIPv4Addresses().ToList();

        Assert.Equal(2, addresses.Count);
        Assert.All(addresses, a => Assert.Equal(AddressFamily.InterNetwork, a.AddressFamily));
    }

    [Fact]
    public void GetIPv6Addresses_ReturnsOnlyIPv6()
    {
        var records = new DnsRecord[]
        {
            CreateARecord("1.2.3.4"),
            CreateAaaaRecord("::1"),
            CreateAaaaRecord("2606:4700::1"),
        };

        var addresses = records.GetIPv6Addresses().ToList();

        Assert.Equal(2, addresses.Count);
        Assert.All(addresses, a => Assert.Equal(AddressFamily.InterNetworkV6, a.AddressFamily));
    }

    private static DnsARecord CreateARecord(string ip)
    {
        return new DnsARecord { Address = IPAddress.Parse(ip) };
    }

    private static DnsAaaaRecord CreateAaaaRecord(string ip)
    {
        return new DnsAaaaRecord { Address = IPAddress.Parse(ip) };
    }
}
