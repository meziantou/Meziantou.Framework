using System.Net;

namespace Meziantou.Framework.DnsClient.Helpers;

internal static class ReverseLookupHelper
{
    public static string GetReverseLookupDomain(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => GetIPv4ReverseDomain(address),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => GetIPv6ReverseDomain(address),
            _ => throw new ArgumentException($"Unsupported address family: {address.AddressFamily}", nameof(address)),
        };
    }

    private static string GetIPv4ReverseDomain(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return $"{bytes[3]}.{bytes[2]}.{bytes[1]}.{bytes[0]}.in-addr.arpa";
    }

    private static string GetIPv6ReverseDomain(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        var sb = new StringBuilder(73); // 64 nibbles + 31 dots + ".ip6.arpa" - 1 = 73+8

        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            if (sb.Length > 0)
            {
                sb.Append('.');
            }

            sb.Append(GetHexNibble(bytes[i] & 0x0F));
            sb.Append('.');
            sb.Append(GetHexNibble((bytes[i] >> 4) & 0x0F));
        }

        sb.Append(".ip6.arpa");
        return sb.ToString();
    }

    private static char GetHexNibble(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);
}
