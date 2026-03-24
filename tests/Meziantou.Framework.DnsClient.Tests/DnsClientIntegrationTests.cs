using System.Net;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using TestUtilities;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class DnsClientIntegrationTests
{
    private const string CloudflareDoH = "https://cloudflare-dns.com/dns-query";
    private const string Quad9DoH = "https://dns.quad9.net/dns-query";

    private static DnsClient CreateDoHClient(string url = CloudflareDoH)
    {
        return new DnsClient(url, DnsClientProtocol.Https);
    }

    private static Task<DnsResponseMessage> QueryWithRetryAsync(DnsClient client, string domain, DnsQueryType queryType, DnsQueryClass queryClass = DnsQueryClass.IN)
    {
        return XUnitStaticHelpers.Retry(() => client.QueryAsync(domain, queryType, queryClass, XUnitStaticHelpers.XunitCancellationToken));
    }

    private static Task<DnsResponseMessage> ReverseLookupWithRetryAsync(DnsClient client, IPAddress ipAddress)
    {
        return XUnitStaticHelpers.Retry(() => client.ReverseLookupAsync(ipAddress, XUnitStaticHelpers.XunitCancellationToken));
    }

    private static Task<DnsResponseMessage> SendWithRetryAsync(DnsClient client, DnsQueryMessage query)
    {
        return XUnitStaticHelpers.Retry(() => client.SendAsync(query, XUnitStaticHelpers.XunitCancellationToken));
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

    [Fact]
    public async Task Query_A_Record()
    {
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
        var response = await QueryWithFallbackAsync(DnsQueryType.AAAA);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var aaaaRecords = response.Answers.OfType<DnsAaaaRecord>().ToList();
        Assert.NotEmpty(aaaaRecords);
        Assert.All(aaaaRecords, r => Assert.Equal(System.Net.Sockets.AddressFamily.InterNetworkV6, r.Address.AddressFamily));
    }

    [Fact]
    public async Task Query_MX_Record()
    {
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
        var response = await QueryWithFallbackAsync(DnsQueryType.TXT, "google.com");

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var txtRecords = response.Answers.OfType<DnsTxtRecord>().ToList();
        Assert.NotEmpty(txtRecords);
        Assert.All(txtRecords, r => Assert.NotEmpty(r.Text));
    }

    [Fact]
    public async Task Query_NS_Record()
    {
        var response = await QueryWithFallbackAsync(DnsQueryType.NS);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var nsRecords = response.Answers.OfType<DnsNsRecord>().ToList();
        Assert.NotEmpty(nsRecords);
        Assert.All(nsRecords, r => Assert.NotEmpty(r.NameServer));
    }

    [Fact]
    public async Task Query_SOA_Record()
    {
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
        var response = await QueryWithFallbackAsync(DnsQueryType.HTTPS, "cloudflare.com");

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var svcbRecords = response.Answers.OfType<DnsSvcbRecord>().ToList();
        Assert.NotEmpty(svcbRecords);
    }

    [Fact]
    public async Task Query_NxDomain()
    {
        var response = await QueryWithFallbackAsync(DnsQueryType.A, "this-domain-surely-does-not-exist-xyz999.com");

        Assert.Equal(DnsResponseCode.NameError, response.Header.ResponseCode);
    }

    [Fact]
    public async Task Query_RecursionDesired()
    {
        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.True(response.Header.RecursionDesired);
        Assert.True(response.Header.RecursionAvailable);
    }

    [Fact]
    public async Task ReverseLookup_IPv4()
    {
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
        using var client = CreateDoHClient();
        var response = await ReverseLookupWithRetryAsync(client, IPAddress.Parse("2606:4700:4700::1111"));

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        var ptrRecords = response.Answers.OfType<DnsPtrRecord>().ToList();
        Assert.NotEmpty(ptrRecords);
    }

    [Fact]
    public async Task Query_IDN_Unicode()
    {
        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "münchen.de", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
    }

    [Fact]
    public async Task Query_Punycode()
    {
        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "xn--mnchen-3ya.de", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
    }

    [Fact]
    public async Task Query_DNSSEC_AD_Flag()
    {
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
    public async Task Query_DNSKEY_Record()
    {
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
        var response = await QueryWithFallbackAsync(DnsQueryType.SRV, "_sip._tcp.example.com");

        // SRV might not exist for this domain, just check no protocol error
        Assert.True(response.Header.ResponseCode is DnsResponseCode.NoError or DnsResponseCode.NameError);
    }

    [Fact]
    public async Task Query_WithDnsOverHttps_Quad9()
    {
        using var client = CreateDoHClient(Quad9DoH);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_DefaultClassIsIN()
    {
        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.Single(response.Questions);
        Assert.Equal(DnsQueryClass.IN, response.Questions[0].QueryClass);
    }

    [Fact]
    public async Task Query_MultipleAnswers()
    {
        using var client = CreateDoHClient();
        var response = await QueryWithRetryAsync(client, "google.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_RRSIG_Record()
    {
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
        using var client = new DnsClient("1.1.1.1", DnsClientProtocol.Udp);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_TCP()
    {
        using var client = new DnsClient("1.1.1.1", DnsClientProtocol.Tcp);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

    [Fact]
    public async Task Query_DoT()
    {
        using var client = new DnsClient("1.1.1.1", DnsClientProtocol.Tls);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public async Task Query_DoQ()
    {
        if (!System.Net.Quic.QuicConnection.IsSupported)
            return;

        using var client = new DnsClient("dns.adguard-dns.com", DnsClientProtocol.Quic);
        var response = await QueryWithRetryAsync(client, "example.com", DnsQueryType.A);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }
#endif
}
