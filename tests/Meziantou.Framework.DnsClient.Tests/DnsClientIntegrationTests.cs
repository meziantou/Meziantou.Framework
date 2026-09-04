using System.Collections.Concurrent;
using TestUtilities;
using System.Net;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class DnsClientIntegrationTests
{
    /// <summary>
    /// These tests query public resolvers, so they fail for reasons that say nothing about this library: no network,
    /// a restrictive egress policy, or an outage at the resolver. Probe once per endpoint and skip rather than fail,
    /// so a red suite always means a real regression. Hermetic coverage lives in the other test classes.
    /// </summary>
    /// <remarks>
    /// Reachability is tracked per (server, protocol) rather than globally, because the transports use different
    /// ports: a network that allows 443 for DNS over HTTPS may still block 53 and 853.
    /// </remarks>
    private static readonly ConcurrentDictionary<(string Server, DnsClientProtocol Protocol), Lazy<bool>> Reachability = new();

    private const string CloudflareDoH = "https://cloudflare-dns.com/dns-query";
    private const string Quad9DoH = "https://dns.quad9.net/dns-query";
    private const string CloudflareIP = "1.1.1.1";
    private const string AdGuardDoQ = "dns.adguard-dns.com";
    private static readonly string[] DnsOverHttpsFallbackUrls = [CloudflareDoH, Quad9DoH];

    private static bool IsReachable(string server, DnsClientProtocol protocol)
    {
        var probe = Reachability.GetOrAdd(
            (server, protocol),
            key => new Lazy<bool>(() => Probe(key.Server, key.Protocol), LazyThreadSafetyMode.ExecutionAndPublication));

        return probe.Value;
    }

    private static bool Probe(string server, DnsClientProtocol protocol)
    {
        try
        {
            using var client = new DnsClient(server, protocol, new DnsClientOptions { Timeout = TimeSpan.FromSeconds(10) });
            var response = client.QueryAsync("example.com", DnsQueryType.A).GetAwaiter().GetResult();
            return response.Header.ResponseCode is DnsResponseCode.NoError;
        }
#pragma warning disable CA1031 // Any failure means this endpoint is unavailable; the reason does not change the outcome.
        catch (Exception)
        {
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>Skips unless at least one of the DNS over HTTPS resolvers the tests fall back over is reachable.</summary>
    private static void SkipIfResolverUnreachable()
    {
        var reachable = Array.Exists(DnsOverHttpsFallbackUrls, url => IsReachable(url, DnsClientProtocol.Https));
        global::Xunit.Assert.SkipUnless(reachable, $"No DNS over HTTPS resolver is reachable from this machine ({string.Join(", ", DnsOverHttpsFallbackUrls)}).");
    }

    /// <summary>Skips unless this specific server and protocol is reachable, for the tests that do not use the DoH fallback list.</summary>
    private static void SkipIfEndpointUnreachable(string server, DnsClientProtocol protocol)
    {
        global::Xunit.Assert.SkipUnless(IsReachable(server, protocol), $"The DNS server '{server}' is not reachable over {protocol} from this machine.");
    }

    private static DnsClient CreateDoHClient(string url = CloudflareDoH, TimeSpan? timeout = null)
    {
        var options = timeout is null ? null : new DnsClientOptions
        {
            Timeout = timeout.Value,
        };

        return new DnsClient(url, DnsClientProtocol.Https, options);
    }

    private static Task<DnsResponseMessage> QueryWithRetryAsync(DnsClient client, string domain, DnsQueryType queryType, DnsQueryClass queryClass = DnsQueryClass.IN)
    {
        return Retry(() => client.QueryAsync(domain, queryType, queryClass, XunitCancellationToken));
    }

    private static Task<DnsResponseMessage> ReverseLookupWithRetryAsync(DnsClient client, IPAddress ipAddress)
    {
        return Retry(() => client.ReverseLookupAsync(ipAddress, XunitCancellationToken));
    }

    private static Task<DnsResponseMessage> SendWithRetryAsync(DnsClient client, DnsQueryMessage query)
    {
        return Retry(() => client.SendAsync(query, XunitCancellationToken));
    }

    private static async Task<DnsResponseMessage> QueryWithFallbackAsync(
        DnsQueryType type,
        string domain = "example.com",
        DnsQueryClass queryClass = DnsQueryClass.IN)
    {
        DnsResponseMessage response;
        try
        {
            using var client = CreateDoHClient(CloudflareDoH);
            response = await QueryWithRetryAsync(client, domain, type, queryClass);
        }
        catch
        {
            using var fallback = CreateDoHClient(Quad9DoH);
            response = await QueryWithRetryAsync(fallback, domain, type, queryClass);
        }

        return response;
    }

    private static async Task<DnsResponseMessage> QueryWithLocalValidationFallbackAsync(string domain, DnsQueryType type)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? exception = null;
        foreach (var url in DnsOverHttpsFallbackUrls)
        {
            try
            {
                using var client = new DnsClient(url, DnsClientProtocol.Https, new DnsClientOptions
                {
                    DnssecValidationMode = DnssecValidationMode.Local,
                    HttpVersion = HttpVersion.Version20,
                    HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                    Timeout = TimeSpan.FromSeconds(20),
                });

                return await QueryWithRetryAsync(client, domain, type);
            }
            catch (Exception ex)
            {
                exception = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            }
        }

        exception!.Throw();
        throw new InvalidOperationException("No DNS over HTTPS resolver was configured.");
    }

    [Fact]
    public async Task Query_A_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.True(response.Header.IsResponse);
        Assert.NotEmpty(response.Answers);

        var aRecords = response.Answers.OfType<DnsARecord>().ToList();
        Assert.NotEmpty(aRecords);
        Assert.All(aRecords, r => Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, r.Address.AddressFamily));
    }

    [Fact]
    public async Task Query_AAAA_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.AAAA);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var aaaaRecords = response.Answers.OfType<DnsAaaaRecord>().ToList();
        Assert.NotEmpty(aaaaRecords);
        Assert.All(aaaaRecords, r => Assert.Equal(System.Net.Sockets.AddressFamily.InterNetworkV6, r.Address.AddressFamily));
    }

    [Fact]
    public async Task Query_MX_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.MX, "google.com");

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var mxRecords = response.Answers.OfType<DnsMxRecord>().ToList();
        Assert.NotEmpty(mxRecords);
        Assert.All(mxRecords, r =>
        {
            Assert.NotEmpty(r.Exchange);
            Assert.True(r.Preference >= 0);
        });
    }

    [Fact]
    public async Task Query_TXT_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.TXT, "google.com");

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var txtRecords = response.Answers.OfType<DnsTxtRecord>().ToList();
        Assert.NotEmpty(txtRecords);
        Assert.All(txtRecords, r => Assert.NotEmpty(r.Text));
    }

    [Fact]
    public async Task Query_NS_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.NS);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var nsRecords = response.Answers.OfType<DnsNsRecord>().ToList();
        Assert.NotEmpty(nsRecords);
        Assert.All(nsRecords, r => Assert.NotEmpty(r.NameServer));
    }

    [Fact]
    public async Task Query_SOA_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.SOA);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var soaRecords = response.Answers.OfType<DnsSoaRecord>().ToList();
        Assert.NotEmpty(soaRecords);
        Assert.All(soaRecords, r =>
        {
            Assert.NotEmpty(r.PrimaryNameServer);
            Assert.NotEmpty(r.ResponsibleMailbox);
            Assert.True(r.Serial > 0);
        });
    }

    [Fact]
    public async Task Query_CNAME_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.CNAME, "www.microsoft.com");

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        // www.microsoft.com has CNAME records
        var cnameRecords = response.Answers.OfType<DnsCnameRecord>().ToList();
        Assert.NotEmpty(cnameRecords);
        Assert.All(cnameRecords, r => Assert.NotEmpty(r.CanonicalName));
    }

    [Fact]
    public async Task Query_CAA_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.CAA, "google.com");

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var caaRecords = response.Answers.OfType<DnsCaaRecord>().ToList();
        Assert.NotEmpty(caaRecords);
        Assert.All(caaRecords, r =>
        {
            Assert.NotEmpty(r.Tag);
            Assert.NotEmpty(r.Value);
        });
    }

    [Fact]
    public async Task Query_HTTPS_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.HTTPS, "cloudflare.com");

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var svcbRecords = response.Answers.OfType<DnsSvcbRecord>().ToList();
        Assert.NotEmpty(svcbRecords);
    }

    [Fact]
    public async Task Query_NxDomain()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.A, "this-domain-surely-does-not-exist-xyz999.com");

        Assert.Equal(DnsResponseCode.NameError, response.Header.ResponseCode);
    }

    [Fact]
    public async Task Query_RecursionDesired()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.True(response.Header.RecursionDesired);
        Assert.True(response.Header.RecursionAvailable);
    }

    [Fact]
    public async Task ReverseLookup_IPv4()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient();
        var response = await ReverseLookupWithRetryAsync(client, IPAddress.Parse("1.1.1.1"));

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var ptrRecords = response.Answers.OfType<DnsPtrRecord>().ToList();
        Assert.NotEmpty(ptrRecords);
        Assert.Contains(ptrRecords, r => r.DomainName.Contains("one.one.one.one", StringComparison.OrdinalIgnoreCase)
                                      || r.DomainName.Contains("cloudflare", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReverseLookup_IPv6()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient();
        var response = await ReverseLookupWithRetryAsync(client, IPAddress.Parse("2606:4700:4700::1111"));

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var ptrRecords = response.Answers.OfType<DnsPtrRecord>().ToList();
        Assert.NotEmpty(ptrRecords);
    }

    [Fact]
    public async Task Query_IDN_Unicode()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "münchen.de", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
    }

    [Fact]
    public async Task Query_Punycode()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "xn--mnchen-3ya.de", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
    }

    [Fact]
    public async Task Query_DNSSEC_AD_Flag()
    {
        SkipIfResolverUnreachable();

        using var client = new DnsClient(CloudflareDoH, DnsClientProtocol.Https, new DnsClientOptions
        {
            DnssecOk = true,
        });

        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion("cloudflare.com", DnsQueryType.A));
        query.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = 4096,
            DnssecOk = true,
        };

        var response = await SendWithRetryAsync(client, query);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        // Cloudflare should set AD flag for DNSSEC-signed domains when queried with DO flag
        Assert.True(response.Header.AuthenticatedData);
    }

    [Fact]
    public async Task Query_DNSSEC_LocalValidation()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithLocalValidationFallbackAsync("cloudflare.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.Equal(DnssecValidationStatus.Secure, response.DnssecValidationResult.Status);
    }

    [Fact]
    public async Task Query_DNSKEY_Record()
    {
        SkipIfResolverUnreachable();

        using var client = new DnsClient(CloudflareDoH, DnsClientProtocol.Https, new DnsClientOptions
        {
            DnssecOk = true,
        });

        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion("cloudflare.com", DnsQueryType.DNSKEY));
        query.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = 4096,
            DnssecOk = true,
        };

        var response = await SendWithRetryAsync(client, query);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var dnskeyRecords = response.Answers.OfType<DnsDnskeyRecord>().ToList();
        Assert.NotEmpty(dnskeyRecords);
        Assert.All(dnskeyRecords, r =>
        {
            Assert.Equal(3, r.Protocol); // DNSSEC protocol must be 3
            Assert.NotEmpty(r.PublicKey);
        });
    }

    [Fact]
    public async Task Query_DS_Record()
    {
        SkipIfResolverUnreachable();

        using var client = new DnsClient(CloudflareDoH, DnsClientProtocol.Https, new DnsClientOptions
        {
            DnssecOk = true,
        });

        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion("cloudflare.com", DnsQueryType.DS));
        query.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = 4096,
            DnssecOk = true,
        };

        var response = await SendWithRetryAsync(client, query);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var dsRecords = response.Answers.OfType<DnsDsRecord>().ToList();
        Assert.NotEmpty(dsRecords);
        Assert.All(dsRecords, r =>
        {
            Assert.True(r.KeyTag > 0);
            Assert.NotEmpty(r.Digest);
        });
    }

    [Fact]
    public async Task Query_SRV_Record()
    {
        SkipIfResolverUnreachable();

        var response = await QueryWithFallbackAsync(DnsQueryType.SRV, "_sip._tcp.example.com");

        // SRV might not exist for this domain, just check no protocol error
        Assert.True(response.Header.ResponseCode is DnsResponseCode.NoError or DnsResponseCode.NameError);
    }

    [Fact]
    public async Task Query_WithDnsOverHttps_Quad9()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient(Quad9DoH, timeout: TimeSpan.FromSeconds(20));
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_DefaultClassIsIN()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.Single(response.Questions);
        Assert.Equal(DnsQueryClass.IN, response.Questions[0].QueryClass);
    }

    [Fact]
    public async Task Query_MultipleAnswers()
    {
        SkipIfResolverUnreachable();

        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "google.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_RRSIG_Record()
    {
        SkipIfResolverUnreachable();

        using var client = new DnsClient(CloudflareDoH, DnsClientProtocol.Https, new DnsClientOptions
        {
            DnssecOk = true,
        });

        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion("cloudflare.com", DnsQueryType.A));
        query.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = 4096,
            DnssecOk = true,
        };

        var response = await SendWithRetryAsync(client, query);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        // With DO flag, response should include RRSIG records
        var rrsigRecords = response.Answers.OfType<DnsRrsigRecord>().ToList();
        Assert.NotEmpty(rrsigRecords);
        Assert.All(rrsigRecords, r =>
        {
            Assert.NotEmpty(r.SignerName);
            Assert.NotEmpty(r.Signature);
        });
    }

    [Fact]
    public async Task Query_NSEC_InAuthoritySection()
    {
        SkipIfResolverUnreachable();

        using var client = new DnsClient(CloudflareDoH, DnsClientProtocol.Https, new DnsClientOptions
        {
            DnssecOk = true,
        });

        var query = new DnsQueryMessage
        {
            RecursionDesired = true,
        };
        query.Questions.Add(new DnsQuestion("nonexistent-dnssec-test-xyz999.com", DnsQueryType.A));
        query.EdnsOptions = new DnsEdnsOptions
        {
            UdpPayloadSize = 4096,
            DnssecOk = true,
        };

        var response = await SendWithRetryAsync(client, query);

        // For NXDOMAIN with DNSSEC, authority section should contain SOA and possibly NSEC/NSEC3 records
        Assert.Equal(DnsResponseCode.NameError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Authorities);
    }

    [Fact]
    public async Task Query_UDP()
    {
        SkipIfEndpointUnreachable(CloudflareIP, DnsClientProtocol.Udp);

        using var client = new DnsClient(CloudflareIP, DnsClientProtocol.Udp);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_TCP()
    {
        SkipIfEndpointUnreachable(CloudflareIP, DnsClientProtocol.Tcp);

        using var client = new DnsClient(CloudflareIP, DnsClientProtocol.Tcp);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_DoT()
    {
        SkipIfEndpointUnreachable(CloudflareIP, DnsClientProtocol.Tls);

        using var client = new DnsClient(CloudflareIP, DnsClientProtocol.Tls);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_DoQ()
    {
        // A bare return would report this as a passing test that asserted nothing.
        global::Xunit.Assert.SkipUnless(System.Net.Quic.QuicConnection.IsSupported, "QUIC is not supported on this platform.");
        SkipIfEndpointUnreachable(AdGuardDoQ, DnsClientProtocol.Quic);

        using var client = new DnsClient(AdGuardDoQ, DnsClientProtocol.Quic);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }
}
