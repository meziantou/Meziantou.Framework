using System.Net;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Response;

/// <summary>Provides extension methods for working with DNS records.</summary>
public static class DnsRecordExtensions
{
    /// <summary>Returns all IP addresses from A and AAAA records in the collection.</summary>
    public static IEnumerable<IPAddress> GetIPAddresses(this IEnumerable<DnsRecord> records)
    {
        foreach (var record in records)
        {
            if (record is DnsARecord a)
            {
                yield return a.Address;
            }
            else if (record is DnsAaaaRecord aaaa)
            {
                yield return aaaa.Address;
            }
        }
    }

    /// <summary>Returns all IPv4 addresses from A records in the collection.</summary>
    public static IEnumerable<IPAddress> GetIPv4Addresses(this IEnumerable<DnsRecord> records)
    {
        foreach (var record in records)
        {
            if (record is DnsARecord a)
            {
                yield return a.Address;
            }
        }
    }

    /// <summary>Returns all IPv6 addresses from AAAA records in the collection.</summary>
    public static IEnumerable<IPAddress> GetIPv6Addresses(this IEnumerable<DnsRecord> records)
    {
        foreach (var record in records)
        {
            if (record is DnsAaaaRecord aaaa)
            {
                yield return aaaa.Address;
            }
        }
    }
}
