using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>The global object identifier of a meeting ([MS-OXOCAL] 2.2.1.27), and its conversion to and from the <c>UID</c> of iCalendar ([MS-OXCICAL] 2.1.3.1.1.20.26).</summary>
internal static class GlobalObjectId
{
    private const int HeaderSize = 40;

    private static ReadOnlySpan<byte> ByteArrayId => [0x04, 0x00, 0x00, 0x00, 0x82, 0x00, 0xE0, 0x00, 0x74, 0xC5, 0xB7, 0x10, 0x1A, 0x82, 0xE0, 0x08];

    // The data of the identifier of a UID that is not an encoded identifier: "vCal-Uid", then the version 1
    private static ReadOnlySpan<byte> ThirdPartyPrefix => [0x76, 0x43, 0x61, 0x6C, 0x2D, 0x55, 0x69, 0x64, 0x01, 0x00, 0x00, 0x00];

    /// <summary>Returns the identifier of a UID, for the whole series (the instance date is zero).</summary>
    public static byte[] FromUid(string uid)
    {
        if (IsEncoded(uid))
        {
            var bytes = Convert.FromHexString(uid);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(36));
            if (size == bytes.Length - HeaderSize)
            {
                bytes.AsSpan(16, 4).Clear();
                return bytes;
            }
        }

        var data = Encoding.UTF8.GetBytes(uid);
        var result = new byte[HeaderSize + ThirdPartyPrefix.Length + data.Length];
        ByteArrayId.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(36), (uint)(ThirdPartyPrefix.Length + data.Length));
        ThirdPartyPrefix.CopyTo(result.AsSpan(HeaderSize));
        data.CopyTo(result.AsSpan(HeaderSize + ThirdPartyPrefix.Length));
        return result;
    }

    /// <summary>Returns the UID of an identifier, or <see langword="null"/> when it is not a global object identifier.</summary>
    public static string? ToUid(ReadOnlySpan<byte> id)
    {
        if (id.Length < HeaderSize || !id.StartsWith(ByteArrayId))
        {
            return null;
        }

        var size = BinaryPrimitives.ReadUInt32LittleEndian(id[36..]);
        if (size > id.Length - HeaderSize)
        {
            return null;
        }

        var data = id.Slice(HeaderSize, (int)size);
        if (data.StartsWith(ThirdPartyPrefix))
        {
            return Encoding.UTF8.GetString(data[ThirdPartyPrefix.Length..]);
        }

        // The identifier, without its instance date, in hexadecimal
        var clean = id[..(HeaderSize + (int)size)].ToArray();
        clean.AsSpan(16, 4).Clear();
        return Convert.ToHexString(clean);
    }

    /// <summary>Returns whether a UID is an encoded global object identifier ([MS-OXCICAL] 2.1.3.1.1.20.26).</summary>
    private static bool IsEncoded(string uid)
    {
        return uid.Length >= 82 && uid.Length % 2 == 0 && uid.AsSpan().StartsWith("040000008200E00074C5B7101A82E008", StringComparison.OrdinalIgnoreCase) && !uid.AsSpan().ContainsAnyExcept("0123456789abcdefABCDEF");
    }
}
