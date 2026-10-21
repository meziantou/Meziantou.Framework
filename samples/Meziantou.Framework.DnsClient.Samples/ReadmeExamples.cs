using System.Net;
using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

// Not in the Meziantou.Framework.DnsClient namespace, so the examples resolve the types through the same using
// directives as the code of a consumer.
namespace DnsClientSamples;

internal static class ReadmeExamples
{
    public static async Task QueryAsync()
    {
        #region DnsClient_Query
        // Simple query using DNS over HTTPS
        using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https);
        var response = await client.QueryAsync("example.com", DnsQueryType.A);

        foreach (var record in response.Answers.OfType<DnsARecord>())
        {
            Console.WriteLine(record.Address);
        }
        #endregion
    }

    public static async Task<DnsResponseMessage> QueryOverTlsAsync()
    {
        #region DnsClient_Tls
        using var client = new DnsClient("1.1.1.1", DnsClientProtocol.Tls);
        var response = await client.QueryAsync("example.com", DnsQueryType.AAAA);
        #endregion

        return response;
    }

    public static async Task ReverseLookupAsync()
    {
        #region DnsClient_ReverseLookup
        using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https);
        var response = await client.ReverseLookupAsync(IPAddress.Parse("1.1.1.1"));

        foreach (var ptr in response.Answers.OfType<DnsPtrRecord>())
        {
            Console.WriteLine(ptr.DomainName);
        }
        #endregion
    }

    public static async Task<DnsResponseMessage> QueryInternationalizedDomainNameAsync()
    {
        #region DnsClient_Idn
        using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https);
        // Unicode domain names are automatically converted to punycode
        var response = await client.QueryAsync("münchen.de", DnsQueryType.A);
        #endregion

        return response;
    }

    public static async Task DnssecAsync()
    {
        #region DnsClient_Dnssec
        using var client = new DnsClient("https://cloudflare-dns.com/dns-query", DnsClientProtocol.Https,
            new DnsClientOptions { DnssecValidationMode = DnssecValidationMode.Local });

        var response = await client.QueryAsync("cloudflare.com", DnsQueryType.A);
        Console.WriteLine($"Local DNSSEC validation: {response.DnssecValidationResult.Status}");
        #endregion
    }
}
