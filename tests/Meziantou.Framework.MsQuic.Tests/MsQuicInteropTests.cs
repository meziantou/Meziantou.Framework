using System.Net;
using System.Runtime.CompilerServices;
using Meziantou.Framework.MsQuic.Interop;

namespace Meziantou.Framework.MsQuic.Tests;

public sealed class MsQuicInteropTests
{
    // The sizes msquic.h gives on 64-bit platforms (sizeof, compiled against the header of MsQuic 2.6)
    [Fact]
    public void StructureSizes()
    {
        Assert.Equal(16, Unsafe.SizeOf<QuicBuffer>());
        Assert.Equal(16, Unsafe.SizeOf<QuicRegistrationConfig>());
        Assert.Equal(24, Unsafe.SizeOf<QuicCertificatePkcs12>());
        Assert.Equal(56, Unsafe.SizeOf<QuicCredentialConfig>());
        Assert.Equal(72, Unsafe.SizeOf<QuicNewConnectionInfo>());
        Assert.Equal(144, Unsafe.SizeOf<QuicSettings>());
        Assert.Equal(208, Unsafe.SizeOf<QuicStatisticsV2>());
        Assert.Equal(24, Unsafe.SizeOf<QuicListenerEvent>());
        Assert.Equal(32, Unsafe.SizeOf<QuicConnectionEvent>());
        Assert.Equal(40, Unsafe.SizeOf<QuicStreamEvent>());
        Assert.Equal(29 * IntPtr.Size, Unsafe.SizeOf<QuicApiTable>());
    }

    [Theory]
    [InlineData("127.0.0.1", 443)]
    [InlineData("192.168.1.20", 65535)]
    [InlineData("::1", 4433)]
    [InlineData("2001:db8::1", 1)]
    public void Address_RoundTrip(string address, int port)
    {
        var endPoint = new IPEndPoint(IPAddress.Parse(address), port);
        Span<byte> buffer = stackalloc byte[MsQuicAddress.Size];
        MsQuicAddress.Write(endPoint, buffer, dualMode: false);
        Assert.Equal(endPoint, MsQuicAddress.Read(buffer));

        // The port is in network order at offset 2 on every platform
        Assert.Equal((byte)(port >> 8), buffer[2]);
        Assert.Equal((byte)port, buffer[3]);
    }

    [Fact]
    public void Address_DualModeListensOnAllFamilies()
    {
        Span<byte> buffer = stackalloc byte[MsQuicAddress.Size];
        MsQuicAddress.Write(new IPEndPoint(IPAddress.IPv6Any, 1234), buffer, dualMode: true);
        Assert.Null(MsQuicAddress.Read(buffer));
    }
}
