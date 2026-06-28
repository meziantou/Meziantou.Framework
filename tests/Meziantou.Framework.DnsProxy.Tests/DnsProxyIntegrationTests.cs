using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using DnsProxyProgram = global::Program;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class DnsProxyIntegrationTests
{
    [Fact]
    public async Task Proxy_StartsWithCustomConfiguration_AndProcessesRequests()
    {
        const int DnsPort = 0;
        const int HttpPort = 5080;
        const string FilterRefreshInterval = "00:10:00";
        const string DnsPortVariableName = "DnsProxy__DnsPort";
        const string HttpPortVariableName = "DnsProxy__HttpPort";
        const string FilterRefreshIntervalVariableName = "DnsProxy__FilterRefreshInterval";
        const string Upstream0UrlVariableName = "DnsProxy__Upstreams__0__Url";
        const string Upstream1UrlVariableName = "DnsProxy__Upstreams__1__Url";
        const string Upstream2UrlVariableName = "DnsProxy__Upstreams__2__Url";
        const string BootstrapDnsServersVariableName = "DnsProxy__BootstrapDnsServers__0";

        var previousDnsPort = Environment.GetEnvironmentVariable(DnsPortVariableName);
        var previousHttpPort = Environment.GetEnvironmentVariable(HttpPortVariableName);
        var previousFilterRefreshInterval = Environment.GetEnvironmentVariable(FilterRefreshIntervalVariableName);
        var previousUpstream0Url = Environment.GetEnvironmentVariable(Upstream0UrlVariableName);
        var previousUpstream1Url = Environment.GetEnvironmentVariable(Upstream1UrlVariableName);
        var previousUpstream2Url = Environment.GetEnvironmentVariable(Upstream2UrlVariableName);
        var previousBootstrapDnsServers = Environment.GetEnvironmentVariable(BootstrapDnsServersVariableName);

        Environment.SetEnvironmentVariable(DnsPortVariableName, DnsPort.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(HttpPortVariableName, HttpPort.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(FilterRefreshIntervalVariableName, FilterRefreshInterval);
        Environment.SetEnvironmentVariable(Upstream0UrlVariableName, "https://1.1.1.1/dns-query");
        Environment.SetEnvironmentVariable(Upstream1UrlVariableName, "https://9.9.9.9/dns-query");
        Environment.SetEnvironmentVariable(Upstream2UrlVariableName, "https://dns.nextdns.io/dns-query");
        Environment.SetEnvironmentVariable(BootstrapDnsServersVariableName, "1.1.1.1");

        try
        {
            using var factory = new WebApplicationFactory<DnsProxyProgram>();
            var webClient = factory.CreateClient();
            var html = await webClient.GetStringAsync("/");

            Assert.Contains($"<span class='mono'>DnsPort</span>: {DnsPort}", html, StringComparison.Ordinal);
            Assert.Contains($"<span class='mono'>HttpPort</span>: {HttpPort}", html, StringComparison.Ordinal);
            Assert.Contains($"<span class='mono'>FilterRefreshInterval</span>: {FilterRefreshInterval}", html, StringComparison.Ordinal);

            var query = CreateAQuery("localhost", id: 0x1234);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/dns-query")
            {
                Content = new ByteArrayContent(query),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
            using var response = await webClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            AssertDnsResponseHasARecord(responseBytes, expectedId: 0x1234, expectedAddress: IPAddress.Parse("127.0.0.1"));

            var htmlAfterQuery = await webClient.GetStringAsync("/");
            Assert.Contains("localhost A", htmlAfterQuery, StringComparison.Ordinal);
            Assert.Contains("Rewritten", htmlAfterQuery, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DnsPortVariableName, previousDnsPort);
            Environment.SetEnvironmentVariable(HttpPortVariableName, previousHttpPort);
            Environment.SetEnvironmentVariable(FilterRefreshIntervalVariableName, previousFilterRefreshInterval);
            Environment.SetEnvironmentVariable(Upstream0UrlVariableName, previousUpstream0Url);
            Environment.SetEnvironmentVariable(Upstream1UrlVariableName, previousUpstream1Url);
            Environment.SetEnvironmentVariable(Upstream2UrlVariableName, previousUpstream2Url);
            Environment.SetEnvironmentVariable(BootstrapDnsServersVariableName, previousBootstrapDnsServers);
        }
    }

    [Fact]
    public async Task DisableFilteringEndpoint_DisablesRewriteRulesTemporarily()
    {
        const int DnsPort = 0;
        const int HttpPort = 5080;
        const int DiagnosticsHistoryCapacity = 1;
        const string RewriteDomain = "temporary-filter-disable.example";
        const string RewriteAddress = "203.0.113.123";

        using var baseFactory = new WebApplicationFactory<DnsProxyProgram>();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DnsProxy:DnsPort", DnsPort.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("DnsProxy:HttpPort", HttpPort.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("DnsProxy:DiagnosticsHistoryCapacity", DiagnosticsHistoryCapacity.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("DnsProxy:Filters:0:Url", "");
            builder.UseSetting("DnsProxy:Filters:1:Url", "");
            builder.UseSetting("DnsProxy:Rewrites:0:Domain", RewriteDomain);
            builder.UseSetting("DnsProxy:Rewrites:0:Type", "A");
            builder.UseSetting("DnsProxy:Rewrites:0:Value", RewriteAddress);
            builder.UseSetting("DnsProxy:Upstreams:0:Endpoint", "");
            builder.UseSetting("DnsProxy:Upstreams:1:Endpoint", "");
            builder.UseSetting("DnsProxy:Upstreams:2:Endpoint", "");
        });
        var webClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var query = CreateAQuery(RewriteDomain, id: 0x1234);
        using var requestBeforePause = new HttpRequestMessage(HttpMethod.Post, "/dns-query")
        {
            Content = new ByteArrayContent(query),
        };
        requestBeforePause.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        requestBeforePause.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
        using var responseBeforePause = await webClient.SendAsync(requestBeforePause);
        responseBeforePause.EnsureSuccessStatusCode();
        var responseBeforePauseBytes = await responseBeforePause.Content.ReadAsByteArrayAsync();
        var rewriteAddress = IPAddress.Parse(RewriteAddress);
        AssertDnsResponseHasARecord(responseBeforePauseBytes, expectedId: 0x1234, expectedAddress: rewriteAddress);

        using var disableContent = new StringContent("");
        using var disableResponse = await webClient.PostAsync("/filtering/disable", disableContent);
        Assert.Equal(HttpStatusCode.Redirect, disableResponse.StatusCode);
        Assert.Equal("/", disableResponse.Headers.Location?.ToString());

        var html = await webClient.GetStringAsync("/");
        Assert.Contains("Filtering is disabled until", html, StringComparison.Ordinal);

        using var requestAfterPause = new HttpRequestMessage(HttpMethod.Post, "/dns-query")
        {
            Content = new ByteArrayContent(query),
        };
        requestAfterPause.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        requestAfterPause.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
        using var responseAfterPause = await webClient.SendAsync(requestAfterPause);
        responseAfterPause.EnsureSuccessStatusCode();
        var responseAfterPauseBytes = await responseAfterPause.Content.ReadAsByteArrayAsync();
        AssertDnsResponseDoesNotHaveARecord(responseAfterPauseBytes, expectedId: 0x1234, unexpectedAddress: rewriteAddress);

        var htmlAfterQuery = await webClient.GetStringAsync("/");
        Assert.DoesNotContain("Rewritten", htmlAfterQuery, StringComparison.Ordinal);
    }

    private static byte[] CreateAQuery(string domain, ushort id)
    {
        using var ms = new MemoryStream();
        WriteUInt16(ms, id);
        WriteUInt16(ms, 0x0100); // RD=1
        WriteUInt16(ms, 1);      // QDCOUNT
        WriteUInt16(ms, 0);      // ANCOUNT
        WriteUInt16(ms, 0);      // NSCOUNT
        WriteUInt16(ms, 0);      // ARCOUNT

        WriteDomainName(ms, domain);
        WriteUInt16(ms, 1);      // QTYPE=A
        WriteUInt16(ms, 1);      // QCLASS=IN
        return ms.ToArray();
    }

    private static void AssertDnsResponseHasARecord(byte[] response, ushort expectedId, IPAddress expectedAddress)
    {
        Assert.True(response.Length >= 12);

        var offset = 0;
        var id = ReadUInt16(response, ref offset);
        Assert.Equal(expectedId, id);

        var flags = ReadUInt16(response, ref offset);
        Assert.Equal(0, flags & 0x000F); // RCODE=NOERROR
        var questionCount = ReadUInt16(response, ref offset);
        var answerCount = ReadUInt16(response, ref offset);
        _ = ReadUInt16(response, ref offset); // authorityCount
        _ = ReadUInt16(response, ref offset); // additionalCount

        Assert.Equal(1, questionCount);
        Assert.True(answerCount >= 1);

        SkipDomainName(response, ref offset);
        offset += 4; // QTYPE + QCLASS

        SkipDomainName(response, ref offset);
        var recordType = ReadUInt16(response, ref offset);
        var recordClass = ReadUInt16(response, ref offset);
        offset += 4; // TTL
        var rdLength = ReadUInt16(response, ref offset);

        Assert.Equal(1, recordType);
        Assert.Equal(1, recordClass);
        Assert.Equal(4, rdLength);
        Assert.True(offset + rdLength <= response.Length);

        var address = new IPAddress(response.AsSpan(offset, rdLength));
        Assert.Equal(expectedAddress, address);
    }

    private static void AssertDnsResponseDoesNotHaveARecord(byte[] response, ushort expectedId, IPAddress unexpectedAddress)
    {
        Assert.True(response.Length >= 12);

        var offset = 0;
        var id = ReadUInt16(response, ref offset);
        Assert.Equal(expectedId, id);

        _ = ReadUInt16(response, ref offset); // flags
        var questionCount = ReadUInt16(response, ref offset);
        var answerCount = ReadUInt16(response, ref offset);
        _ = ReadUInt16(response, ref offset); // authorityCount
        _ = ReadUInt16(response, ref offset); // additionalCount

        Assert.Equal(1, questionCount);

        SkipDomainName(response, ref offset);
        offset += 4; // QTYPE + QCLASS

        for (var i = 0; i < answerCount; i++)
        {
            SkipDomainName(response, ref offset);
            var recordType = ReadUInt16(response, ref offset);
            _ = ReadUInt16(response, ref offset); // recordClass
            offset += 4; // TTL
            var rdLength = ReadUInt16(response, ref offset);

            if (recordType == 1 && rdLength == 4)
            {
                var address = new IPAddress(response.AsSpan(offset, rdLength));
                Assert.NotEqual(unexpectedAddress, address);
            }

            offset += rdLength;
        }
    }

    private static void WriteDomainName(MemoryStream stream, string domain)
    {
        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var labelBytes = System.Text.Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)labelBytes.Length);
            stream.Write(labelBytes, 0, labelBytes.Length);
        }

        stream.WriteByte(0);
    }

    private static void WriteUInt16(MemoryStream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = (ushort)((data[offset] << 8) | data[offset + 1]);
        offset += 2;
        return value;
    }

    private static void SkipDomainName(ReadOnlySpan<byte> data, ref int offset)
    {
        while (offset < data.Length)
        {
            var length = data[offset];
            if (length is 0)
            {
                offset++;
                return;
            }

            if ((length & 0b1100_0000) == 0b1100_0000)
            {
                offset += 2;
                return;
            }

            offset++;
            offset += length;
        }
    }
}
