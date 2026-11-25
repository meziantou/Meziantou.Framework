using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.MsQuic.Interop;

// QUIC_ADDR is the native socket address (SOCKADDR_INET on Windows, a union of sockaddr_in and sockaddr_in6
// elsewhere): 28 bytes, the port in network order at offset 2, the IPv4 address at offset 4, the IPv6 address at
// offset 8 and its scope at offset 24. Only the family differs: a 16-bit value at offset 0 on Windows and Linux, a
// length byte followed by a family byte on macOS and the BSDs.
internal static class MsQuicAddress
{
    public const int Size = 28;

    private static readonly bool HasLengthByte = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsFreeBSD();

    // AF_INET6: 23 on Windows, 28 on FreeBSD, 30 on the Apple platforms, 10 on Linux
    private static readonly ushort InterNetworkV6 = OperatingSystem.IsWindows() ? (ushort)23 : OperatingSystem.IsFreeBSD() ? (ushort)28 : HasLengthByte ? (ushort)30 : (ushort)10;

    public const ushort Unspecified = 0;
    public const ushort InterNetwork = 2;

    public static ushort GetNativeFamily(AddressFamily family) => family switch
    {
        AddressFamily.InterNetwork => InterNetwork,
        AddressFamily.InterNetworkV6 => InterNetworkV6,
        _ => Unspecified,
    };

    public static void Write(IPEndPoint endPoint, Span<byte> destination, bool dualMode)
    {
        destination[..Size].Clear();
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)endPoint.Port);
        ushort family;
        if (dualMode && endPoint.Address.Equals(IPAddress.IPv6Any))
        {
            // QUIC_ADDRESS_FAMILY_UNSPEC listens on IPv4 and IPv6
            family = Unspecified;
        }
        else if (endPoint.AddressFamily is AddressFamily.InterNetwork)
        {
            family = InterNetwork;
            endPoint.Address.TryWriteBytes(destination[4..8], out _);
        }
        else
        {
            family = InterNetworkV6;
            endPoint.Address.TryWriteBytes(destination[8..24], out _);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], (uint)endPoint.Address.ScopeId);
        }

        if (HasLengthByte)
        {
            destination[0] = family == InterNetwork ? (byte)16 : (byte)28;
            destination[1] = (byte)family;
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, family);
        }
    }

    public static IPEndPoint? Read(ReadOnlySpan<byte> address)
    {
        var family = HasLengthByte ? address[1] : BinaryPrimitives.ReadUInt16LittleEndian(address);
        var port = BinaryPrimitives.ReadUInt16BigEndian(address[2..]);
        if (family == InterNetwork)
            return new IPEndPoint(new IPAddress(address[4..8]), port);

        if (family == InterNetworkV6)
            return new IPEndPoint(new IPAddress(address[8..24], BinaryPrimitives.ReadUInt32LittleEndian(address[24..])), port);

        return null;
    }
}
