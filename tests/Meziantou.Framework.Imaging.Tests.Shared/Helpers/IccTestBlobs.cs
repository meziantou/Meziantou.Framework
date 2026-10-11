using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Blocks with a plausible ICC header for the metadata tests, which only carry a profile from an image to a file and back.
/// They are not usable profiles: see <see cref="IccTestProfiles"/> for the color conversion tests.
/// </summary>
internal static class IccTestBlobs
{
    /// <summary>Creates a header followed by one 'desc'-like tag of 12 bytes.</summary>
    public static byte[] Create(string colorSpace)
    {
        var data = new byte[128 + 4 + 12 + 12];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16);
        "XYZ "u8.CopyTo(data.AsSpan(20));
        "acsp"u8.CopyTo(data.AsSpan(36));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(128), 1);
        "desc"u8.CopyTo(data.AsSpan(132));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(136), 144);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(140), 12);
        return data;
    }

    /// <summary>Creates a display profile header followed by a deterministic payload, of a given total length.</summary>
    public static byte[] Create(string colorSpace, int length)
    {
        var data = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        "mntr"u8.CopyTo(data.AsSpan(12));
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(data.AsSpan(16));
        "XYZ "u8.CopyTo(data.AsSpan(20));
        "acsp"u8.CopyTo(data.AsSpan(36));
        for (var i = 132; i < length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        return data;
    }
}
