using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Transport;

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

        Assert.Contains("EDNS", exception.Message, StringComparison.Ordinal);
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

    private sealed class CapturingTransport : IDnsTransport
    {
        public byte[] LastQuery { get; private set; } = [];

        public Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(CreateEmptyResponse(query));
        }

        public void Dispose()
        {
        }

        private static byte[] CreateEmptyResponse(byte[] query)
        {
            return
            [
                query[0], query[1],
                0x81, 0x80,
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
            ];
        }
    }
}
