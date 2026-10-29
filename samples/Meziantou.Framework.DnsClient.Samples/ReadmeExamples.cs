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

    // The examples below are in the documentation (docs/73-Dns). dns.example.net stands for the resolver you use.

    public static void Protocols()
    {
        #region DnsClient_Protocols
        // UDP and TCP: an IP address or a host name, port 53 by default
        using var udpClient = new DnsClient("192.0.2.53", DnsClientProtocol.Udp);
        using var tcpClient = new DnsClient("192.0.2.53:5353", DnsClientProtocol.Tcp);

        // DNS over TLS and DNS over QUIC: port 853 by default; the certificate must match the host name
        using var tlsClient = new DnsClient("dns.example.net", DnsClientProtocol.Tls);
        using var quicClient = new DnsClient("dns.example.net:853", DnsClientProtocol.Quic);

        // DNS over HTTPS: the URL of the endpoint, https only
        using var httpsClient = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https);
        #endregion
    }

    public static DnsClient CreateQuicClientWithFallback()
    {
        #region DnsClient_QuicFallback
        DnsClient client;
        try
        {
            client = new DnsClient("dns.example.net", DnsClientProtocol.Quic);
        }
        catch (PlatformNotSupportedException)
        {
            // QUIC is not available on this machine
            client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https);
        }
        #endregion

        return client;
    }

    public static async Task<DnsResponseMessage> HttpsOptionsAsync()
    {
        #region DnsClient_HttpsOptions
        using var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        using var client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
            HttpVersion = HttpVersion.Version30,
            HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Timeout = TimeSpan.FromSeconds(10),
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A);
        #endregion

        return response;
    }

    public static DnsClient ServerAddressResolver()
    {
        #region DnsClient_ServerAddressResolver
        // Resolves the host name of the DNS server itself, instead of asking the system resolver
        var client = new DnsClient("dns.example.net", DnsClientProtocol.Tls, new DnsClientOptions
        {
            ServerAddressResolver = hostName => [IPAddress.Parse("192.0.2.53")],
        });
        #endregion

        return client;
    }

    public static async Task LocalDnssecValidationAsync()
    {
        #region DnsClient_LocalDnssecValidation
        using var client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            DnssecValidationMode = DnssecValidationMode.Local,
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A);
        var validation = response.DnssecValidationResult;
        switch (validation.Status)
        {
            case DnssecValidationStatus.Secure:
                // Signed by a chain of trust that starts at the root anchors, and answers the question
                break;

            case DnssecValidationStatus.Insecure:
                // The zone is provably unsigned
                break;

            case DnssecValidationStatus.Bogus:
            case DnssecValidationStatus.Indeterminate:
                foreach (var issue in validation.Issues)
                {
                    Console.WriteLine($"{issue.Code} {issue.Name} {issue.RecordType}: {issue.Message}");
                }

                break;
        }
        #endregion
    }

    public static async Task DnssecRecordsAsync()
    {
        #region DnsClient_DnssecRecords
        // Sets the DNSSEC OK flag, so the resolver returns the signatures with the answer
        using var client = new DnsClient("https://dns.example.net/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            DnssecOk = true,
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A);

        // The AD flag: the resolver says it validated the answer. The client does not check it.
        var validatedByResolver = response.Header.AuthenticatedData;

        foreach (var signature in response.Answers.OfType<DnsRrsigRecord>())
        {
            Console.WriteLine($"{signature.TypeCovered} signed by {signature.SignerName} (key {signature.KeyTag})");
        }
        #endregion

        Console.WriteLine(validatedByResolver);
    }
}
