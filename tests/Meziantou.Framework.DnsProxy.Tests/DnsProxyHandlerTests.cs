using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Hosting;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using DnsProxyProgram = global::Program;
using DnsResponseCode = Meziantou.Framework.DnsServer.Protocol.DnsResponseCode;

namespace Meziantou.Framework.DnsProxy.Tests;

/// <summary>
/// Drives the proxy end to end against a fake upstream DNS server running on loopback, so the pipeline is covered
/// without depending on the public internet.
/// </summary>
[Collection("DnsProxyEnvironment")]
public sealed class DnsProxyHandlerTests
{
    private const ushort OptRecordType = 41;
    private const ushort RrsigRecordType = 46;

    [Fact(DisableParallelization = true)]
    public async Task ForwardsTheDnssecOkBitToTheUpstreamServer()
    {
        DnsMessage? receivedQuery = null;
        await using var upstream = FakeUpstream.Start(context =>
        {
            receivedQuery = context.Query;
            var response = context.CreateResponse();
            response.Answers.Add(CreateARecord(context, "203.0.113.10"));
            response.Answers.Add(new DnsResourceRecord
            {
                Name = context.Query.Questions[0].Name,
                Type = DnsQueryType.RRSIG,
                Class = DnsQueryClass.IN,
                TimeToLive = 300,
                Data = new DnsRrsigRecordData
                {
                    TypeCovered = DnsQueryType.A,
                    Algorithm = 13,
                    Labels = 2,
                    OriginalTtl = 300,
                    SignatureExpiration = 2_000_000_000,
                    SignatureInception = 1_000_000_000,
                    KeyTag = 1234,
                    SignerName = "example",
                    Signature = [1, 2, 3, 4],
                },
            });

            return response;
        });

        using var scope = EnvironmentScope.ForUpstreams(upstream.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var response = await QueryAsync(factory, "signed.example", dnssecOk: true);

        Assert.NotNull(receivedQuery);
        Assert.NotNull(receivedQuery.EdnsOptions);
        Assert.True(receivedQuery.EdnsOptions.DnssecOk, "The DNSSEC-OK bit was not forwarded to the upstream server.");
        Assert.Equal(1, CountRecords(response, RrsigRecordType));
    }

    [Fact(DisableParallelization = true)]
    public async Task DoesNotSetDnssecOkUpstreamWhenTheClientDidNotAskForIt()
    {
        DnsMessage? receivedQuery = null;
        await using var upstream = FakeUpstream.Start(context =>
        {
            receivedQuery = context.Query;
            var response = context.CreateResponse();
            response.Answers.Add(CreateARecord(context, "203.0.113.11"));
            return response;
        });

        using var scope = EnvironmentScope.ForUpstreams(upstream.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        _ = await QueryAsync(factory, "unsigned.example", dnssecOk: false);

        Assert.NotNull(receivedQuery);
        Assert.False(receivedQuery.EdnsOptions?.DnssecOk);
    }

    [Fact(DisableParallelization = true)]
    public async Task AdvertisesItsOwnPayloadSizeInsteadOfEchoingTheClientValue()
    {
        await using var upstream = FakeUpstream.Start(context =>
        {
            var response = context.CreateResponse();
            response.Answers.Add(CreateARecord(context, "203.0.113.12"));
            return response;
        });

        using var scope = EnvironmentScope.ForUpstreams(upstream.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var response = await QueryAsync(factory, "payload.example", dnssecOk: false, udpPayloadSize: 512);

        var opt = Assert.Single(ReadRecords(response), record => record.Type == OptRecordType);
        Assert.Equal(1232, opt.Class);
    }

    [Fact(DisableParallelization = true)]
    public async Task UnsupportedEdnsVersionIsAnsweredWithBadVersion()
    {
        await using var upstream = FakeUpstream.Start(context => context.CreateResponse());

        using var scope = EnvironmentScope.ForUpstreams(upstream.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var response = await QueryAsync(factory, "badvers.example", dnssecOk: false, ednsVersion: 1);

        var opt = Assert.Single(ReadRecords(response), record => record.Type == OptRecordType);
        Assert.Equal(0, response[3] & 0x0F);
        Assert.Equal(1u, opt.TimeToLive >> 24); // BADVERS (16) is carried in the upper 8 bits
        Assert.Equal(0u, (opt.TimeToLive >> 16) & 0xFF); // the highest supported version is 0
    }

    [Fact(DisableParallelization = true)]
    public async Task DropsTheUpstreamOptRecordSoTheResponseCarriesExactlyOne()
    {
        await using var upstream = FakeUpstream.Start(context =>
        {
            var response = context.CreateResponse();
            response.Answers.Add(CreateARecord(context, "203.0.113.13"));
            return response;
        });

        using var scope = EnvironmentScope.ForUpstreams(upstream.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var response = await QueryAsync(factory, "opt.example", dnssecOk: true);

        Assert.Equal(1, CountRecords(response, OptRecordType));
    }

    [Fact(DisableParallelization = true)]
    public async Task FailsOverToTheNextUpstreamOnServerFailure()
    {
        var brokenUpstreamCallCount = 0;
        await using var broken = FakeUpstream.Start(context =>
        {
            brokenUpstreamCallCount++;
            var response = context.CreateResponse();
            response.ResponseCode = DnsResponseCode.ServerFailure;
            return response;
        });

        await using var healthy = FakeUpstream.Start(context =>
        {
            var response = context.CreateResponse();
            response.Answers.Add(CreateARecord(context, "203.0.113.14"));
            return response;
        });

        using var scope = EnvironmentScope.ForUpstreams(broken.Url, healthy.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var response = await QueryAsync(factory, "failover.example", dnssecOk: false);

        Assert.Equal(0, response[3] & 0x0F); // NOERROR
        Assert.Equal(1, brokenUpstreamCallCount);
        Assert.Contains(ReadRecords(response), record => record.Type == 1);
    }

    [Fact(DisableParallelization = true)]
    public async Task DoesNotFailOverOnNameError()
    {
        await using var nxdomain = FakeUpstream.Start(context =>
        {
            var response = context.CreateResponse();
            response.ResponseCode = DnsResponseCode.NameError;
            return response;
        });

        var secondUpstreamCallCount = 0;
        await using var second = FakeUpstream.Start(context =>
        {
            secondUpstreamCallCount++;
            var response = context.CreateResponse();
            response.Answers.Add(CreateARecord(context, "203.0.113.15"));
            return response;
        });

        using var scope = EnvironmentScope.ForUpstreams(nxdomain.Url, second.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var response = await QueryAsync(factory, "missing.example", dnssecOk: false);

        Assert.Equal(3, response[3] & 0x0F); // NXDOMAIN is an answer, not a failure
        Assert.Equal(0, secondUpstreamCallCount);
    }

    [Fact(DisableParallelization = true)]
    public async Task ReturnsServerFailureWhenEveryUpstreamIsUnhealthy()
    {
        await using var broken = FakeUpstream.Start(context =>
        {
            var response = context.CreateResponse();
            response.ResponseCode = DnsResponseCode.Refused;
            return response;
        });

        using var scope = EnvironmentScope.ForUpstreams(broken.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var response = await QueryAsync(factory, "allbroken.example", dnssecOk: false);

        Assert.Equal(5, response[3] & 0x0F); // the upstream's REFUSED is surfaced to the client
    }

    [Fact(DisableParallelization = true)]
    public async Task MultipleQuestionsAreRejectedWithFormError()
    {
        await using var upstream = FakeUpstream.Start(context => context.CreateResponse());

        using var scope = EnvironmentScope.ForUpstreams(upstream.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();

        var query = BuildQuery(["first.example", "second.example"], dnssecOk: false, udpPayloadSize: 1232, ednsVersion: 0);
        var response = await SendAsync(factory, query);

        Assert.Equal(1, response[3] & 0x0F); // FORMERR
    }

    [Fact(DisableParallelization = true)]
    public async Task AppliesADnsRewriteRuleWithoutContactingAnUpstream()
    {
        var upstreamCallCount = 0;
        await using var upstream = FakeUpstream.Start(context =>
        {
            upstreamCallCount++;
            return context.CreateResponse();
        });

        await using var filterList = FakeFilterList.Start("||rewritten.example^$dnsrewrite=203.0.113.99");

        using var scope = EnvironmentScope.ForUpstreams([upstream.Url], filterList.Url);
        await using var factory = new WebApplicationFactory<DnsProxyProgram>();
        await WaitForFilterRulesAsync(factory);

        var response = await QueryAsync(factory, "rewritten.example", dnssecOk: false);

        Assert.Equal(0, response[3] & 0x0F); // NOERROR
        Assert.Equal(0, upstreamCallCount);
        var answer = Assert.Single(ReadRecords(response), record => record.Type == 1);
        Assert.Equal(1, answer.Class);
    }

    private static async Task WaitForFilterRulesAsync(WebApplicationFactory<DnsProxyProgram> factory)
    {
        var webClient = factory.CreateClient();
        for (var i = 0; i < 100; i++)
        {
            var html = await webClient.GetStringAsync("/");
            if (!html.Contains("<span class='mono'>LoadedFilterRules</span>: 0", StringComparison.Ordinal))
                return;

            await Task.Delay(50);
        }

        Assert.Fail("The filter list was never loaded.");
    }

    private static DnsResourceRecord CreateARecord(DnsRequestContext context, string address)
    {
        return new DnsResourceRecord
        {
            Name = context.Query.Questions[0].Name,
            Type = DnsQueryType.A,
            Class = DnsQueryClass.IN,
            TimeToLive = 300,
            Data = new DnsARecordData { Address = IPAddress.Parse(address) },
        };
    }

    private static async Task<byte[]> QueryAsync(WebApplicationFactory<DnsProxyProgram> factory, string domain, bool dnssecOk, ushort udpPayloadSize = 1232, byte ednsVersion = 0)
    {
        var query = BuildQuery([domain], dnssecOk, udpPayloadSize, ednsVersion);
        return await SendAsync(factory, query);
    }

    private static async Task<byte[]> SendAsync(WebApplicationFactory<DnsProxyProgram> factory, byte[] query)
    {
        var webClient = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/dns-query")
        {
            Content = new ByteArrayContent(query),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        using var response = await webClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static byte[] BuildQuery(string[] domains, bool dnssecOk, ushort udpPayloadSize, byte ednsVersion)
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, 0x4242);
        WriteUInt16(stream, 0x0100); // recursion desired
        WriteUInt16(stream, (ushort)domains.Length);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 1); // one OPT record

        foreach (var domain in domains)
        {
            WriteDomainName(stream, domain);
            WriteUInt16(stream, 1); // A
            WriteUInt16(stream, 1); // IN
        }

        stream.WriteByte(0); // OPT owner name is the root
        WriteUInt16(stream, OptRecordType);
        WriteUInt16(stream, udpPayloadSize);
        stream.WriteByte(0); // extended rcode
        stream.WriteByte(ednsVersion);
        WriteUInt16(stream, (ushort)(dnssecOk ? 0x8000 : 0));
        WriteUInt16(stream, 0); // rdlength

        return stream.ToArray();
    }

    private static int CountRecords(byte[] message, ushort recordType)
    {
        return ReadRecords(message).Count(record => record.Type == recordType);
    }

    private static List<WireRecord> ReadRecords(byte[] message)
    {
        // The four section counts live in the header, at offsets 4, 6, 8 and 10.
        var countOffset = 4;
        var questionCount = ReadUInt16(message, ref countOffset);
        var recordCount = ReadUInt16(message, ref countOffset) + ReadUInt16(message, ref countOffset) + ReadUInt16(message, ref countOffset);

        var offset = 12;
        for (var i = 0; i < questionCount; i++)
        {
            SkipDomainName(message, ref offset);
            offset += 4;
        }

        var records = new List<WireRecord>();
        for (var i = 0; i < recordCount; i++)
        {
            SkipDomainName(message, ref offset);
            var type = ReadUInt16(message, ref offset);
            var recordClass = ReadUInt16(message, ref offset);
            var timeToLive = (uint)((message[offset] << 24) | (message[offset + 1] << 16) | (message[offset + 2] << 8) | message[offset + 3]);
            offset += 4;
            var rdLength = ReadUInt16(message, ref offset);
            offset += rdLength;
            records.Add(new WireRecord(type, recordClass, timeToLive));
        }

        return records;
    }

    private static void WriteDomainName(MemoryStream stream, string domain)
    {
        foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
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

    private static ushort ReadUInt16(byte[] data, ref int offset)
    {
        var value = (ushort)((data[offset] << 8) | data[offset + 1]);
        offset += 2;
        return value;
    }

    private static void SkipDomainName(byte[] data, ref int offset)
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

            offset += 1 + length;
        }
    }

    private sealed record WireRecord(ushort Type, ushort Class, uint TimeToLive);

    /// <summary>A DNS server on loopback that answers with whatever the test's handler returns.</summary>
    private sealed class FakeUpstream : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private FakeUpstream(WebApplication app, int port)
        {
            _app = app;
            Url = $"udp://127.0.0.1:{port}";
        }

        public string Url { get; }

        public static FakeUpstream Start(Func<DnsRequestContext, DnsMessage> handler)
        {
            var port = GetAvailableUdpPort();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.AddDnsServer(options => options.AddUdpListener(port, IPAddress.Loopback));

            var app = builder.Build();
            app.MapDnsHandler((context, cancellationToken) => ValueTask.FromResult(handler(context)));
            app.StartAsync().GetAwaiter().GetResult();

            return new FakeUpstream(app, port);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static int GetAvailableUdpPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }

    /// <summary>Serves a filter list over loopback HTTP so the refresh service has something to download.</summary>
    private sealed class FakeFilterList : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private FakeFilterList(WebApplication app, int port)
        {
            _app = app;
            Url = $"http://127.0.0.1:{port}/filters.txt";
        }

        public string Url { get; }

        public static FakeFilterList Start(string content)
        {
            var port = GetAvailableTcpPort();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            var app = builder.Build();
            app.MapGet("/filters.txt", () => Results.Text(content, "text/plain"));
            app.StartAsync().GetAwaiter().GetResult();

            return new FakeFilterList(app, port);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static int GetAvailableTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }
    }

    /// <summary>
    /// Points every configured upstream at the given fake servers and keeps the proxy offline, restoring the previous
    /// environment on dispose.
    /// </summary>
    private sealed class EnvironmentScope : IDisposable
    {
        private const int ConfiguredUpstreamCount = 6;
        private const string UnreachableFilterUrl = "http://127.0.0.1:1/filters.txt";

        private readonly Dictionary<string, string?> _previousValues = [];

        private EnvironmentScope(Dictionary<string, string?> values)
        {
            foreach (var (name, value) in values)
            {
                _previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public static EnvironmentScope ForUpstreams(params string[] upstreamUrls) => ForUpstreams(upstreamUrls, filterListUrl: null);

        public static EnvironmentScope ForUpstreams(string[] upstreamUrls, string? filterListUrl)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DnsProxy__DnsPort"] = "0",
                ["DnsProxy__HttpPort"] = "0",
                ["DnsProxy__FilterRefreshInterval"] = "01:00:00",
                ["DnsProxy__Filters__0__Url"] = filterListUrl ?? UnreachableFilterUrl,
                ["DnsProxy__Filters__0__Format"] = "AdBlock",
                ["DnsProxy__Filters__1__Url"] = UnreachableFilterUrl,
            };

            // appsettings.json declares six upstreams; every one of them must point at a fake server, otherwise a
            // failover test would fall through to a real public resolver.
            for (var i = 0; i < ConfiguredUpstreamCount; i++)
            {
                var url = upstreamUrls[Math.Min(i, upstreamUrls.Length - 1)];
                values[$"DnsProxy__Upstreams__{i}__Url"] = url;
                values[$"DnsProxy__Upstreams__{i}__Name"] = $"Fake {i}";
                values[$"DnsProxy__Upstreams__{i}__Priority"] = i.ToString(CultureInfo.InvariantCulture);
            }

            return new EnvironmentScope(values);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
