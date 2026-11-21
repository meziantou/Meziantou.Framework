using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>The entry identifiers of the members of a distribution list ([MS-OXCDATA] 2.2.5.1 and [MS-OXOCNTC] 2.2.2.2.4.1.1).</summary>
internal static class EntryIds
{
    private static ReadOnlySpan<byte> OneOffProvider => [0x81, 0x2B, 0x1F, 0xA4, 0xBE, 0xA3, 0x10, 0x19, 0x9D, 0x6E, 0x00, 0xDD, 0x01, 0x0F, 0x54, 0x02];

    private static ReadOnlySpan<byte> WrappedProvider => [0xC0, 0x91, 0xAD, 0xD3, 0x51, 0x9D, 0xCF, 0x11, 0xA4, 0xA9, 0x00, 0xAA, 0x00, 0x47, 0xFA, 0xA4];

    private const int OneOffHeaderSize = 4 + 16 + 2 + 2;

    /// <summary>Parses a one-off entry identifier: a display name, an address type and an address.</summary>
    public static MapiDistributionListMember? ParseOneOff(ReadOnlySpan<byte> data)
    {
        if (data.Length < OneOffHeaderSize || !data.Slice(4, 16).SequenceEqual(OneOffProvider))
        {
            return null;
        }

        // The flag word is stored big-endian: 0x0080 (the second byte) marks UTF-16 strings
        var unicode = (data[23] & 0x80) != 0;
        var rest = data[OneOffHeaderSize..];
        var displayName = ReadString(ref rest, unicode);
        var addressType = ReadString(ref rest, unicode);
        var emailAddress = ReadString(ref rest, unicode);
        return new MapiDistributionListMember { DisplayName = displayName, AddressType = addressType, EmailAddress = emailAddress };
    }

    /// <summary>Parses a wrapped entry identifier that contains a one-off entry identifier.</summary>
    public static MapiDistributionListMember? ParseWrappedOneOff(ReadOnlySpan<byte> data)
    {
        if (data.Length < 21 || !data.Slice(4, 16).SequenceEqual(WrappedProvider) || (data[20] & 0x0F) != 0)
        {
            return null;
        }

        return ParseOneOff(data[21..]);
    }

    public static byte[] CreateOneOff(string? displayName, string? addressType, string? emailAddress)
    {
        using var stream = new MemoryStream();
        stream.Write([0, 0, 0, 0]);
        stream.Write(OneOffProvider);
        stream.Write([0, 0, 0x01, 0x80]);
        foreach (var value in new[] { displayName, addressType, emailAddress })
        {
            stream.Write(Encoding.Unicode.GetBytes(value ?? ""));
            stream.Write([0, 0]);
        }

        return stream.ToArray();
    }

    public static byte[] CreateWrappedOneOff(byte[] oneOff)
    {
        var result = new byte[21 + oneOff.Length];
        WrappedProvider.CopyTo(result.AsSpan(4));
        oneOff.CopyTo(result, 21);
        return result;
    }

    private static string? ReadString(ref ReadOnlySpan<byte> data, bool unicode)
    {
        if (unicode)
        {
            for (var i = 0; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    var value = Ltp.PropertyValueCodec.DecodeUnicode(data[..i]);
                    data = data[(i + 2)..];
                    return value;
                }
            }
        }
        else
        {
            var end = data.IndexOf((byte)0);
            if (end >= 0)
            {
                var value = PstContext.GetEncoding(1252).GetString(data[..end]);
                data = data[(end + 1)..];
                return value;
            }
        }

        data = [];
        return null;
    }
}
