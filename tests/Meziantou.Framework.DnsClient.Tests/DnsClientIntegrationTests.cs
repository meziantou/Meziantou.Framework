using System.Collections.Concurrent;
using TestUtilities;
using System.Net;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using System.Net.Sockets;
using Meziantou.Framework.DnsServer.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;
using ServerHandler = Meziantou.Framework.DnsServer.Handler;
using ServerProtocol = Meziantou.Framework.DnsServer.Protocol;
using ServerRecords = Meziantou.Framework.DnsServer.Protocol.Records;

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

    // The tests below query a DNS server that runs on the loopback interface, so they do not depend on the network.
    // They hold the examples of the documentation (docs/73-Dns).

    [Fact]
    public async Task Local_Query_ReturnsTheAddresses()
    {
        await using var server = await LocalDnsServer.StartAsync();
        var serverAddress = server.Address;

        #region DnsClient_GettingStarted
        using var client = new DnsClient(serverAddress, DnsClientProtocol.Udp);
        var response = await client.QueryAsync("www.example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var addresses = response.Answers.OfType<DnsARecord>().Select(record => record.Address).ToList();
        Assert.Equal([IPAddress.Parse("192.0.2.10")], addresses);
        #endregion
    }

    [Fact]
    public async Task Local_Query_ReadsTypedRecords()
    {
        await using var server = await LocalDnsServer.StartAsync();
        using var client = new DnsClient(server.Address, DnsClientProtocol.Udp);

        #region DnsClient_ReadRecords
        var mxResponse = await client.QueryAsync("example.com", DnsQueryType.MX);
        var mailServer = Assert.Single(mxResponse.Answers.OfType<DnsMxRecord>());
        Assert.Equal((ushort)10, mailServer.Preference);
        Assert.Equal("mail.example.com", mailServer.Exchange);
        Assert.Equal(300u, mailServer.TimeToLive);

        var txtResponse = await client.QueryAsync("example.com", DnsQueryType.TXT);
        var txt = Assert.Single(txtResponse.Answers.OfType<DnsTxtRecord>());
        Assert.Equal(["v=spf1 -all"], txt.Text);
        #endregion
    }

    [Fact]
    public async Task Local_Query_DescribesEveryRecordType()
    {
        await using var server = await LocalDnsServer.StartAsync();
        using var client = new DnsClient(server.Address, DnsClientProtocol.Udp);

        #region DnsClient_SwitchOnRecords
        var response = await client.QueryAsync("example.com", DnsQueryType.ANY);

        var lines = new List<string>();
        foreach (var record in response.Answers)
        {
            lines.Add(record switch
            {
                DnsARecord a => $"A {a.Address}",
                DnsMxRecord mx => $"MX {mx.Preference} {mx.Exchange}",
                DnsTxtRecord txt => $"TXT {string.Join(" ", txt.Text)}",
                DnsUnknownRecord unknown => $"{unknown.RecordType} ({unknown.Data.Length} bytes)",
                _ => record.RecordType.ToString(),
            });
        }

        Assert.Equal(["A 192.0.2.1", "MX 10 mail.example.com", "TXT v=spf1 -all"], lines);
        #endregion
    }

    [Fact]
    public async Task Local_Query_GetIPAddresses()
    {
        await using var server = await LocalDnsServer.StartAsync();
        using var client = new DnsClient(server.Address, DnsClientProtocol.Udp);

        #region DnsClient_GetIPAddresses
        var ipv4 = await client.QueryAsync("www.example.com", DnsQueryType.A);
        var ipv6 = await client.QueryAsync("www.example.com", DnsQueryType.AAAA);

        var addresses = ipv4.Answers.Concat(ipv6.Answers).GetIPAddresses().ToList();
        Assert.Equal([IPAddress.Parse("192.0.2.10"), IPAddress.Parse("2001:db8::10")], addresses);
        #endregion
    }

    [Fact]
    public async Task Local_Query_NameError()
    {
        await using var server = await LocalDnsServer.StartAsync();
        using var client = new DnsClient(server.Address, DnsClientProtocol.Udp);

        #region DnsClient_NameError
        var response = await client.QueryAsync("missing.example.com", DnsQueryType.A);

        // NXDOMAIN is an answer, not an exception
        Assert.Equal(DnsResponseCode.NameError, response.Header.ResponseCode);
        Assert.Empty(response.Answers);

        // The SOA record of the zone says how long the absence may be cached
        var soa = Assert.Single(response.Authorities.OfType<DnsSoaRecord>());
        Assert.Equal(60u, soa.Minimum);
        #endregion
    }

    [Fact]
    public async Task Local_ReverseLookup()
    {
        await using var server = await LocalDnsServer.StartAsync();
        using var client = new DnsClient(server.Address, DnsClientProtocol.Udp);

        #region DnsClient_LocalReverseLookup
        // Queries the PTR record of 10.2.0.192.in-addr.arpa
        var response = await client.ReverseLookupAsync(IPAddress.Parse("192.0.2.10"));

        var ptr = Assert.Single(response.Answers.OfType<DnsPtrRecord>());
        Assert.Equal("www.example.com", ptr.DomainName);
        #endregion
    }

    [Fact]
    public async Task Local_Query_InternationalizedName()
    {
        await using var server = await LocalDnsServer.StartAsync();
        using var client = new DnsClient(server.Address, DnsClientProtocol.Udp);

        #region DnsClient_LocalIdn
        // Sent as xn--bcher-kva.example.com
        var response = await client.QueryAsync("bücher.example.com", DnsQueryType.A);

        var record = Assert.Single(response.Answers.OfType<DnsARecord>());
        Assert.Equal("xn--bcher-kva.example.com", record.Name);
        #endregion
    }

    [Fact]
    public async Task Local_SendAsync_CustomMessage()
    {
        await using var server = await LocalDnsServer.StartAsync();
        using var client = new DnsClient(server.Address, DnsClientProtocol.Udp);

        #region DnsClient_SendAsync
        var query = new DnsQueryMessage
        {
            RecursionDesired = false,
            EdnsOptions = new DnsEdnsOptions { UdpPayloadSize = 1400 },
        };
        query.Questions.Add(new DnsQuestion("www.example.com", DnsQueryType.AAAA));

        var response = await client.SendAsync(query);

        var record = Assert.Single(response.Answers.OfType<DnsAaaaRecord>());
        Assert.Equal(IPAddress.Parse("2001:db8::10"), record.Address);
        Assert.False(response.Header.RecursionDesired);
        #endregion
    }

    [Fact]
    public async Task Local_TruncatedAnswer_IsRetriedOverTcp()
    {
        await using var server = await LocalDnsServer.StartAsync();
        var serverAddress = server.Address;

        #region DnsClient_Truncation
        using var client = new DnsClient(serverAddress, DnsClientProtocol.Udp);
        var response = await client.QueryAsync("big.example.com", DnsQueryType.TXT);

        // The answer does not fit in a UDP message: the client asked again over TCP
        Assert.False(response.Header.IsTruncated);
        Assert.HasCount(40, response.Answers);

        using var udpOnlyClient = new DnsClient(serverAddress, DnsClientProtocol.Udp, new DnsClientOptions
        {
            RetryTruncatedOverTcp = false,
        });
        var truncatedResponse = await udpOnlyClient.QueryAsync("big.example.com", DnsQueryType.TXT);
        Assert.True(truncatedResponse.Header.IsTruncated);
        #endregion
    }

    [Fact]
    public async Task Local_Query_Timeout()
    {
        await using var server = await LocalDnsServer.StartAsync();
        var serverAddress = server.Address;

        #region DnsClient_Timeout
        // The server never answers queries for slow.example.com
        using var client = new DnsClient(serverAddress, DnsClientProtocol.Udp, new DnsClientOptions
        {
            Timeout = TimeSpan.FromSeconds(1),
        });

        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync("slow.example.com", DnsQueryType.A));
        #endregion
    }

    [Fact]
    public async Task Local_Query_OverTcp()
    {
        await using var server = await LocalDnsServer.StartAsync();

        using var client = new DnsClient(server.Address, DnsClientProtocol.Tcp);
        var response = await client.QueryAsync("www.example.com", DnsQueryType.A);

        Assert.Equal(IPAddress.Parse("192.0.2.10"), Assert.Single(response.Answers.OfType<DnsARecord>()).Address);
    }

    /// <summary>A DNS server on the loopback interface that answers UDP and TCP queries for a small example.com zone.</summary>
    private sealed class LocalDnsServer : IAsyncDisposable
    {
        private static readonly ServerRecords.DnsSoaRecordData Soa = new()
        {
            PrimaryNameServer = "ns.example.com",
            ResponsibleMailbox = "hostmaster.example.com",
            Serial = 1,
            Refresh = 3600,
            Retry = 600,
            Expire = 86400,
            Minimum = 60,
        };

        private static readonly (string Name, ServerProtocol.DnsQueryType Type, ServerProtocol.DnsResourceRecordData Data)[] Zone =
        [
            ("example.com", ServerProtocol.DnsQueryType.A, new ServerRecords.DnsARecordData { Address = IPAddress.Parse("192.0.2.1") }),
            ("example.com", ServerProtocol.DnsQueryType.MX, new ServerRecords.DnsMxRecordData { Preference = 10, Exchange = "mail.example.com" }),
            ("example.com", ServerProtocol.DnsQueryType.TXT, new ServerRecords.DnsTxtRecordData { Text = ["v=spf1 -all"] }),
            ("www.example.com", ServerProtocol.DnsQueryType.A, new ServerRecords.DnsARecordData { Address = IPAddress.Parse("192.0.2.10") }),
            ("www.example.com", ServerProtocol.DnsQueryType.AAAA, new ServerRecords.DnsAaaaRecordData { Address = IPAddress.Parse("2001:db8::10") }),
            ("xn--bcher-kva.example.com", ServerProtocol.DnsQueryType.A, new ServerRecords.DnsARecordData { Address = IPAddress.Parse("192.0.2.20") }),
            ("10.2.0.192.in-addr.arpa", ServerProtocol.DnsQueryType.PTR, new ServerRecords.DnsPtrRecordData { DomainName = "www.example.com" }),
            .. Enumerable.Range(0, 40).Select(i => ("big.example.com", ServerProtocol.DnsQueryType.TXT, (ServerProtocol.DnsResourceRecordData)new ServerRecords.DnsTxtRecordData { Text = [new string((char)('a' + (i % 26)), 100)] })),
        ];

        private readonly WebApplication _app;

        private LocalDnsServer(WebApplication app, int port)
        {
            _app = app;
            Address = "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
        }

        public string Address { get; }

        public static async Task<LocalDnsServer> StartAsync()
        {
            const int MaxAttempts = 5;
            for (var attempt = 1; ; attempt++)
            {
                // The UDP and TCP listeners share a port, which is probed then released: another process can take it
                // in between, so the server is started again on another port when that happens.
                int port;
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    port = ((IPEndPoint)socket.LocalEndPoint!).Port;
                }

                var builder = WebApplication.CreateBuilder();
                builder.Logging.ClearProviders();
                builder.AddDnsServer(options =>
                {
                    options.AddUdpListener(port, IPAddress.Loopback);
                    options.AddTcpListener(port, IPAddress.Loopback);
                });

                var app = builder.Build();
                app.MapDnsHandler(HandleAsync);
                try
                {
                    await app.StartAsync();
                    return new LocalDnsServer(app, port);
                }
                catch (Exception exception) when (attempt < MaxAttempts && IsAddressAlreadyInUse(exception))
                {
                    await app.DisposeAsync();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static async ValueTask<ServerProtocol.DnsMessage> HandleAsync(ServerHandler.DnsRequestContext context, CancellationToken cancellationToken)
        {
            var response = context.CreateResponse();
            response.IsAuthoritative = true;

            var question = context.Query.Questions[0];
            var name = question.Name.TrimEnd('.');
            if (string.Equals(name, "slow.example.com", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var records = Zone.Where(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (records.Count is 0)
            {
                response.ResponseCode = ServerProtocol.DnsResponseCode.NameError;
            }

            foreach (var record in records)
            {
                if (record.Type == question.Type || question.Type is ServerProtocol.DnsQueryType.ANY)
                {
                    response.Answers.Add(new ServerProtocol.DnsResourceRecord
                    {
                        Name = question.Name,
                        Type = record.Type,
                        Class = ServerProtocol.DnsQueryClass.IN,
                        TimeToLive = 300,
                        Data = record.Data,
                    });
                }
            }

            if (response.Answers.Count is 0)
            {
                response.Authorities.Add(new ServerProtocol.DnsResourceRecord
                {
                    Name = "example.com",
                    Type = ServerProtocol.DnsQueryType.SOA,
                    Class = ServerProtocol.DnsQueryClass.IN,
                    TimeToLive = 60,
                    Data = Soa,
                });
            }

            return response;
        }

        private static bool IsAddressAlreadyInUse(Exception exception)
        {
            if (exception is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return true;

            if (exception.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
                return true;

            return exception.InnerException is not null && IsAddressAlreadyInUse(exception.InnerException);
        }
    }
}
