using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using DnsProxyProgram = global::Program;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class DnsProxyIntegrationTests
{
    [Fact]
    public async Task Proxy_StartsWithCustomConfiguration_AndProcessesRequests()
    {
        const int dnsPort = 0;
        const int httpPort = 5080;
        const string filterRefreshInterval = "00:10:00";
        const string dnsPortVariableName = "DnsProxy__DnsPort";
        const string httpPortVariableName = "DnsProxy__HttpPort";
        const string filterRefreshIntervalVariableName = "DnsProxy__FilterRefreshInterval";
        const string upstream0ProtocolVariableName = "DnsProxy__Upstreams__0__Protocol";
        const string upstream0EndpointVariableName = "DnsProxy__Upstreams__0__Endpoint";
        const string upstream0UseHttp3VariableName = "DnsProxy__Upstreams__0__UseHttp3";
        const string upstream1ProtocolVariableName = "DnsProxy__Upstreams__1__Protocol";
        const string upstream1EndpointVariableName = "DnsProxy__Upstreams__1__Endpoint";
        const string upstream1UseHttp3VariableName = "DnsProxy__Upstreams__1__UseHttp3";
        const string upstream2ProtocolVariableName = "DnsProxy__Upstreams__2__Protocol";
        const string upstream2EndpointVariableName = "DnsProxy__Upstreams__2__Endpoint";
        const string upstream2UseHttp3VariableName = "DnsProxy__Upstreams__2__UseHttp3";

        var previousDnsPort = Environment.GetEnvironmentVariable(dnsPortVariableName);
        var previousHttpPort = Environment.GetEnvironmentVariable(httpPortVariableName);
        var previousFilterRefreshInterval = Environment.GetEnvironmentVariable(filterRefreshIntervalVariableName);
        var previousUpstream0Protocol = Environment.GetEnvironmentVariable(upstream0ProtocolVariableName);
        var previousUpstream0Endpoint = Environment.GetEnvironmentVariable(upstream0EndpointVariableName);
        var previousUpstream0UseHttp3 = Environment.GetEnvironmentVariable(upstream0UseHttp3VariableName);
        var previousUpstream1Protocol = Environment.GetEnvironmentVariable(upstream1ProtocolVariableName);
        var previousUpstream1Endpoint = Environment.GetEnvironmentVariable(upstream1EndpointVariableName);
        var previousUpstream1UseHttp3 = Environment.GetEnvironmentVariable(upstream1UseHttp3VariableName);
        var previousUpstream2Protocol = Environment.GetEnvironmentVariable(upstream2ProtocolVariableName);
        var previousUpstream2Endpoint = Environment.GetEnvironmentVariable(upstream2EndpointVariableName);
        var previousUpstream2UseHttp3 = Environment.GetEnvironmentVariable(upstream2UseHttp3VariableName);

        Environment.SetEnvironmentVariable(dnsPortVariableName, dnsPort.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(httpPortVariableName, httpPort.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(filterRefreshIntervalVariableName, filterRefreshInterval);
        Environment.SetEnvironmentVariable(upstream0ProtocolVariableName, "Https");
        Environment.SetEnvironmentVariable(upstream0EndpointVariableName, "https://1.1.1.1/dns-query");
        Environment.SetEnvironmentVariable(upstream0UseHttp3VariableName, bool.FalseString);
        Environment.SetEnvironmentVariable(upstream1ProtocolVariableName, "Https");
        Environment.SetEnvironmentVariable(upstream1EndpointVariableName, "https://9.9.9.9/dns-query");
        Environment.SetEnvironmentVariable(upstream1UseHttp3VariableName, bool.FalseString);
        Environment.SetEnvironmentVariable(upstream2ProtocolVariableName, "Https");
        Environment.SetEnvironmentVariable(upstream2EndpointVariableName, "https://dns.nextdns.io/dns-query");
        Environment.SetEnvironmentVariable(upstream2UseHttp3VariableName, bool.FalseString);

        try
        {
            using var factory = new WebApplicationFactory<DnsProxyProgram>();
            var webClient = factory.CreateClient();
            var html = await webClient.GetStringAsync("/");

            Assert.Contains($"<span class='mono'>DnsPort</span>: {dnsPort}", html, StringComparison.Ordinal);
            Assert.Contains($"<span class='mono'>HttpPort</span>: {httpPort}", html, StringComparison.Ordinal);
            Assert.Contains($"<span class='mono'>FilterRefreshInterval</span>: {filterRefreshInterval}", html, StringComparison.Ordinal);

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
            Environment.SetEnvironmentVariable(dnsPortVariableName, previousDnsPort);
            Environment.SetEnvironmentVariable(httpPortVariableName, previousHttpPort);
            Environment.SetEnvironmentVariable(filterRefreshIntervalVariableName, previousFilterRefreshInterval);
            Environment.SetEnvironmentVariable(upstream0ProtocolVariableName, previousUpstream0Protocol);
            Environment.SetEnvironmentVariable(upstream0EndpointVariableName, previousUpstream0Endpoint);
            Environment.SetEnvironmentVariable(upstream0UseHttp3VariableName, previousUpstream0UseHttp3);
            Environment.SetEnvironmentVariable(upstream1ProtocolVariableName, previousUpstream1Protocol);
            Environment.SetEnvironmentVariable(upstream1EndpointVariableName, previousUpstream1Endpoint);
            Environment.SetEnvironmentVariable(upstream1UseHttp3VariableName, previousUpstream1UseHttp3);
            Environment.SetEnvironmentVariable(upstream2ProtocolVariableName, previousUpstream2Protocol);
            Environment.SetEnvironmentVariable(upstream2EndpointVariableName, previousUpstream2Endpoint);
            Environment.SetEnvironmentVariable(upstream2UseHttp3VariableName, previousUpstream2UseHttp3);
        }
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
