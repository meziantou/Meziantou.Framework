using System.Net;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using Meziantou.Framework.DnsClient.Transport;

using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient.Tests;

public sealed class DnsClientDnssecOptionsTests
{
    [Fact]
    public void Constructor_WithLocalValidationAndEdnsDisabled_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new DnsClient("1.1.1.1", DnsClientProtocol.Udp, new DnsClientOptions
        {
            EnableEdns = false,
            DnssecValidationMode = DnssecValidationMode.Local,
        }));

        Assert.Contains("EDNS", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_DefaultOptions_DoesNotSetDnssecValidationBits()
    {
        using var transport = new CapturingTransport();
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        var response = await client.QueryAsync("example.com", DnsQueryType.A);

        Assert.NotNull(transport.LastQuery);
        Assert.False(IsCheckingDisabled(transport.LastQuery));
        Assert.False(GetOptDnssecOk(transport.LastQuery));
        Assert.Equal(DnssecValidationStatus.NotValidated, response.DnssecValidationResult.Status);
    }

    [Fact]
    public async Task QueryAsync_LocalValidation_SetsDoAndCdBits()
    {
        using var transport = new CapturingTransport();
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, new DnsClientOptions
        {
            DnssecValidationMode = DnssecValidationMode.Local,
        });

        await client.QueryAsync("example.com", DnsQueryType.A);

        Assert.NotNull(transport.LastQuery);
        Assert.True(IsCheckingDisabled(transport.LastQuery));
        Assert.True(GetOptDnssecOk(transport.LastQuery));
    }

    [Fact]
    public async Task QueryAsync_Https_DefaultOptions_RequestsHttp2OrLower()
    {
        using var handler = new CapturingHttpMessageHandler();
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
        });

        await client.QueryAsync("example.com", DnsQueryType.A);

        Assert.Equal(HttpVersion.Version20, handler.RequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, handler.RequestVersionPolicy);
    }

    [Fact]
    public async Task QueryAsync_Https_UsesConfiguredHttpVersion()
    {
        using var handler = new CapturingHttpMessageHandler();
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
            HttpVersion = HttpVersion.Version20,
            HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        });

        await client.QueryAsync("example.com", DnsQueryType.A);

        Assert.Equal(HttpVersion.Version20, handler.RequestVersion);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, handler.RequestVersionPolicy);
    }

    [Fact]
    public async Task QueryAsync_Https_ResponseWithAnAgeHeader_ReducesTheRecordTimeToLives()
    {
        // RFC 8484 5.1: a response replayed by an HTTP cache still carries the TTLs the resolver first sent, so the
        // time it spent in that cache has to come out of them.
        using var handler = new TimeToLiveHttpMessageHandler(age: TimeSpan.FromSeconds(250));
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken);

        Assert.Equal(350u, Assert.Single(response.Answers).TimeToLive);
        Assert.Equal(650u, Assert.Single(response.Authorities).TimeToLive);
    }

    [Fact]
    public async Task QueryAsync_Https_ResponseWithoutAnAgeHeader_KeepsTheRecordTimeToLives()
    {
        using var handler = new TimeToLiveHttpMessageHandler(age: null);
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken);

        Assert.Equal(600u, Assert.Single(response.Answers).TimeToLive);
        Assert.Equal(900u, Assert.Single(response.Authorities).TimeToLive);
    }

    [Fact]
    public async Task QueryAsync_Https_ResponseWithAnAgeLongerThanTheTimeToLives_ClampsThemToZero()
    {
        using var handler = new TimeToLiveHttpMessageHandler(age: TimeSpan.FromSeconds(5000));
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken);

        Assert.Equal(0u, Assert.Single(response.Answers).TimeToLive);
        Assert.Equal(0u, Assert.Single(response.Authorities).TimeToLive);
    }

    [Fact]
    public async Task QueryAsync_Https_ResponseWithAnAgeHeader_LeavesTheOptRecordMetadataIntact()
    {
        using var handler = new TimeToLiveHttpMessageHandler(age: TimeSpan.FromSeconds(250), optTimeToLive: 0x01008000);
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken);
        var opt = Assert.IsType<DnsOptRecord>(Assert.Single(response.AdditionalRecords));

        Assert.Equal(0x01008000u, opt.TimeToLive);
        Assert.True(opt.DnssecOk);
    }

    [Fact]
    public async Task QueryAsync_Https_UnknownLengthResponseLargerThanADnsMessage_IsRejectedWithoutBufferingItAll()
    {
        // The response declares no Content-Length, so the size limit can only be enforced while reading the body.
        using var handler = new StreamingHttpMessageHandler(totalLength: 1024 * 1024);
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
        });

        await Assert.ThrowsAsync<DnsProtocolException>(() => client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken));

        Assert.True(handler.BytesRead <= MaxDnsMessageLength + 1, $"The transport read {handler.BytesRead} bytes of an oversized response instead of stopping at {MaxDnsMessageLength + 1}.");
    }

    [Fact]
    public async Task QueryAsync_Https_UnknownLengthResponseWithinTheLimit_IsRead()
    {
        using var handler = new StreamingHttpMessageHandler(totalLength: null);
        using var client = new DnsClient("https://example.com/dns-query", DnsClientProtocol.Https, new DnsClientOptions
        {
            HttpHandler = handler,
        });

        var response = await client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.Equal("example.com", response.Questions[0].Name);
    }

    /// <summary>A DNS message can never exceed 65535 bytes; the transport must not buffer more than that.</summary>
    private const int MaxDnsMessageLength = 65535;

    /// <summary>Answers with a body of unknown length, counting how many bytes the transport actually pulls from it.</summary>
    private sealed class StreamingHttpMessageHandler : HttpMessageHandler
    {
        private readonly int? _totalLength;

        /// <param name="totalLength">
        /// The size of the padding body to answer with, representing a resolver that sends far more data than a DNS
        /// message can hold, or <see langword="null" /> to answer with a well-formed response.
        /// </param>
        public StreamingHttpMessageHandler(int? totalLength)
        {
            _totalLength = totalLength;
        }

        public long BytesRead { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var body = _totalLength is { } length ? new byte[length] : CreateEmptyResponse(query);

            // A non-seekable stream is what makes StreamContent report no Content-Length. The response owns it.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new CountingStream(this, body)),
            };
        }

        private sealed class CountingStream : Stream
        {
            private readonly StreamingHttpMessageHandler _owner;
            private readonly byte[] _content;
            private int _position;

            public CountingStream(StreamingHttpMessageHandler owner, byte[] content)
            {
                _owner = owner;
                _content = content;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                // Answer in small chunks, so a reader that ignores the limit has to come back for more.
                var count = Math.Min(Math.Min(buffer.Length, 4096), _content.Length - _position);
                _content.AsSpan(_position, count).CopyTo(buffer);
                _position += count;
                _owner.BytesRead += count;
                return count;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Read(buffer.Span));

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override void Flush() => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    private static bool IsCheckingDisabled(byte[] query)
    {
        var flags = (query[2] << 8) | query[3];
        return (flags & 0x0010) != 0;
    }

    private static bool GetOptDnssecOk(byte[] query)
    {
        var position = 12;
        while (query[position] != 0)
        {
            position += query[position] + 1;
        }

        position += 1 + 2 + 2;
        Assert.Equal(0, query[position]);
        Assert.Equal(0, query[position + 1]);
        Assert.Equal((byte)DnsQueryType.OPT, query[position + 2]);

        var flags = (query[position + 7] << 8) | query[position + 8];
        return (flags & 0x8000) != 0;
    }

    private static byte[] CreateEmptyResponse(byte[] query) => DnsTestMessages.CreateEmptyResponse(query);

    private sealed class CapturingTransport : IDnsTransport
    {
        public byte[] LastQuery { get; private set; } = [];

        public Task<DnsTransportResponse> SendAsync(byte[] query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new DnsTransportResponse(CreateEmptyResponse(query)));
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Answers every query with an A record of TTL 600 and an SOA authority record of TTL 900.</summary>
    private sealed class TimeToLiveHttpMessageHandler : HttpMessageHandler
    {
        private readonly TimeSpan? _age;
        private readonly uint _optTimeToLive;

        public TimeToLiveHttpMessageHandler(TimeSpan? age, uint optTimeToLive = 0)
        {
            _age = age;
            _optTimeToLive = optTimeToLive;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var body = DnsTestMessages.CreateResponseWithTimeToLives(query, answerTimeToLive: 600, authorityTimeToLive: 900, _optTimeToLive);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };

            response.Headers.Age = _age;
            return response;
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public Version? RequestVersion { get; private set; }

        public HttpVersionPolicy? RequestVersionPolicy { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestVersion = request.Version;
            RequestVersionPolicy = request.VersionPolicy;

            var query = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(CreateEmptyResponse(query)),
            };
        }
    }

    [Fact]
    public async Task QueryAsync_ResponseWithMismatchedIdentifier_IsRejected()
    {
        using var transport = new ScriptedTransport(query => MutateHeader(DnsTestMessages.CreateEmptyResponse(query), id: 0xFFFF));
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        await Assert.ThrowsAsync<DnsProtocolException>(() => client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueryAsync_ResponseAnsweringADifferentQuestion_IsRejected()
    {
        using var transport = new ScriptedTransport(query =>
        {
            // Echo the identifier but answer for a name the caller never asked about.
            var response = DnsTestMessages.CreateEmptyResponse(query);
            var forged = new List<byte>(response[..12]);
            forged.AddRange([8, (byte)'a', (byte)'t', (byte)'t', (byte)'a', (byte)'c', (byte)'k', (byte)'e', (byte)'r', 4, (byte)'t', (byte)'e', (byte)'s', (byte)'t', 0]);
            forged.AddRange([0x00, 0x01, 0x00, 0x01]);
            return [.. forged];
        });
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        await Assert.ThrowsAsync<DnsProtocolException>(() => client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueryAsync_ResponseWithoutTheQueryResponseBit_IsRejected()
    {
        using var transport = new ScriptedTransport(query => MutateHeader(DnsTestMessages.CreateEmptyResponse(query), clearQueryResponseBit: true));
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        await Assert.ThrowsAsync<DnsProtocolException>(() => client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueryAsync_MatchingResponse_IsAccepted()
    {
        using var transport = new ScriptedTransport(query => DnsTestMessages.CreateEmptyResponse(query));
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        var response = await client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken);

        Assert.Equal(DnsResponseCode.NoError, response.Header.ResponseCode);
        Assert.Equal("example.com", response.Questions[0].Name);
    }

    [Fact]
    public async Task SendAsync_DoesNotMutateTheCallersMessage()
    {
        using var transport = new ScriptedTransport(query => DnsTestMessages.CreateEmptyResponse(query));
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, new DnsClientOptions
        {
            DnssecValidationMode = DnssecValidationMode.None,
            DnssecOk = true,
        });

        var message = new DnsQueryMessage();
        message.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));

        await client.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Null(message.EdnsOptions);
        Assert.False(message.CheckingDisabled);
    }

    [Fact]
    public async Task SendAsync_ConvertsUnicodeQuestionNamesToPunycode()
    {
        using var transport = new ScriptedTransport(query => DnsTestMessages.CreateEmptyResponse(query));
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        var message = new DnsQueryMessage();
        message.Questions.Add(new DnsQuestion("münchen.de", DnsQueryType.A));

        await client.SendAsync(message, TestContext.Current.CancellationToken);

        var name = ReadQuestionName(transport.LastQuery);
        Assert.Equal("xn--mnchen-3ya.de", name);
    }

    [Fact]
    public async Task SendAsync_DefaultOptions_IncludesAnEdnsOptRecord()
    {
        using var transport = new ScriptedTransport(query => DnsTestMessages.CreateEmptyResponse(query));
        using var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);

        var message = new DnsQueryMessage();
        message.Questions.Add(new DnsQuestion("example.com", DnsQueryType.A));

        await client.SendAsync(message, TestContext.Current.CancellationToken);

        // ARCOUNT must be 1: QueryAsync and SendAsync have to agree on whether EDNS is advertised.
        Assert.Equal(1, (transport.LastQuery[10] << 8) | transport.LastQuery[11]);
    }

    [Fact]
    public async Task QueryAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        using var transport = new ScriptedTransport(query => DnsTestMessages.CreateEmptyResponse(query));
        var client = new DnsClient(transport, DnsClientProtocol.Udp, options: null);
        client.Dispose();
        client.Dispose(); // must be idempotent

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.QueryAsync("example.com", DnsQueryType.A, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveTimeout_ThrowsArgumentOutOfRangeException(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using var client = new DnsClient("1.1.1.1", DnsClientProtocol.Udp, new DnsClientOptions { Timeout = TimeSpan.FromSeconds(seconds) });
        });
    }

    [Fact]
    public void Constructor_HttpsProtocolWithACleartextUrl_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            using var client = new DnsClient("http://resolver.example/dns-query", DnsClientProtocol.Https);
        });
    }

    [Theory]
    [InlineData("8.8.8.8:70000")]
    [InlineData("8.8.8.8: 53")]
    public void Constructor_MalformedPort_ThrowsArgumentException(string server)
    {
        // A bad port must be reported against the server argument, not as an internal 'port' parameter.
        var exception = Record.Exception(() =>
        {
            using var client = new DnsClient(server, DnsClientProtocol.Udp);
        });

        Assert.IsAssignableTo<ArgumentException>(exception);
    }

    private static string ReadQuestionName(byte[] query)
    {
        var reader = new DnsWireReader(query.AsSpan(12).ToArray());
        return reader.ReadDomainName();
    }

    private static byte[] MutateHeader(byte[] response, ushort? id = null, bool clearQueryResponseBit = false)
    {
        if (id is not null)
        {
            response[0] = (byte)(id.Value >> 8);
            response[1] = (byte)id.Value;
        }

        if (clearQueryResponseBit)
        {
            response[2] &= 0x7F;
        }

        return response;
    }

    private sealed class ScriptedTransport : IDnsTransport
    {
        private readonly Func<byte[], byte[]> _respond;

        public ScriptedTransport(Func<byte[], byte[]> respond)
        {
            _respond = respond;
        }

        public byte[] LastQuery { get; private set; } = [];

        public Task<DnsTransportResponse> SendAsync(byte[] query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new DnsTransportResponse(_respond(query)));
        }

        public void Dispose()
        {
        }
    }
}
