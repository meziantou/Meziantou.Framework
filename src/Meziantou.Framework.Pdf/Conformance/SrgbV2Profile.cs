using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Pdf.Conformance;

/// <summary>
/// The sRGB profile of the output intent of PDF/A-1: PDF 1.4 knows the ICC profiles of version 2 only (ISO 19005-1,
/// 6.2.2), and the sRGB profile of Imaging is of version 4. The profile is built from the numbers of IEC 61966-2-1, as
/// the profile of Imaging is: a version 2.1 display profile with a CIEXYZ connection space, the sRGB primaries adapted
/// to D50 with the Bradford transform, and the sRGB decoding function sampled in 1024 points (a parametric curve is a
/// type of version 4).
/// </summary>
internal static class SrgbV2Profile
{
    private const int HeaderSize = 128;
    private const int TagEntrySize = 12;
    private const int CurvePoints = 1024;

    // s15Fixed16 numbers: the D50 illuminant and the colorants of the profile of Imaging, which sum to it
    private static readonly int[] D50 = [0x0000F6D6, 0x00010000, 0x0000D32D];
    private static readonly int[] RedColorant = [0x00006FA0, 0x000038F5, 0x00000390];
    private static readonly int[] GreenColorant = [0x00006297, 0x0000B787, 0x000018DA];
    private static readonly int[] BlueColorant = [0x0000249F, 0x00000F84, 0x0000B6C3];

    /// <summary>Gets the bytes of the profile.</summary>
    public static ReadOnlySpan<byte> Data => Profile;

    private static byte[] Profile { get; } = Create();

    private static byte[] Create()
    {
        var curve = CreateCurve();
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", CreateDescription("sRGB IEC61966-2.1")),
            ("cprt", CreateText("Public domain (CC0-1.0)")),
            ("wtpt", CreateXyz(D50)),
            ("rXYZ", CreateXyz(RedColorant)),
            ("gXYZ", CreateXyz(GreenColorant)),
            ("bXYZ", CreateXyz(BlueColorant)),
            ("rTRC", curve),
            ("gTRC", curve),
            ("bTRC", curve),
        };

        // Layout: header, tag table, then each distinct tag data on a 4-byte boundary (the three curves share theirs)
        var offsets = new int[tags.Count];
        var size = HeaderSize + 4 + (TagEntrySize * tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var shared = tags.FindIndex(tag => ReferenceEquals(tag.Data, tags[i].Data));
            if (shared < i)
            {
                offsets[i] = offsets[shared];
                continue;
            }

            offsets[i] = size;
            size += (tags[i].Data.Length + 3) & ~3;
        }

        // The header (ICC.1:1998-09, 6.1): version 2.1, display device, RGB data, XYZ connection space, perceptual intent
        var data = new byte[size];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x02100000);
        "mntr"u8.CopyTo(span[12..]);
        "RGB "u8.CopyTo(span[16..]);
        "XYZ "u8.CopyTo(span[20..]);

        // Creation date: 2026-01-01 00:00:00 UTC, so that the bytes are deterministic
        BinaryPrimitives.WriteUInt16BigEndian(span[24..], 2026);
        BinaryPrimitives.WriteUInt16BigEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(span[28..], 1);
        "acsp"u8.CopyTo(span[36..]);
        for (var i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(span[(68 + (4 * i))..], D50[i]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(span[HeaderSize..], (uint)tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var entry = span.Slice(HeaderSize + 4 + (TagEntrySize * i), TagEntrySize);
            Encoding.ASCII.GetBytes(tags[i].Signature, entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Data.Length);
            tags[i].Data.CopyTo(span[offsets[i]..]);
        }

        return data;
    }

    /// <summary>Creates the curveType of the sRGB decoding function (IEC 61966-2-1, 5.2), sampled at evenly spaced inputs.</summary>
    private static byte[] CreateCurve()
    {
        var data = new byte[12 + (2 * CurvePoints)];
        "curv"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), CurvePoints);
        for (var i = 0; i < CurvePoints; i++)
        {
            var value = i / (double)(CurvePoints - 1);
            var linear = value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12 + (2 * i)), (ushort)Math.Round(linear * 65535, MidpointRounding.AwayFromZero));
        }

        return data;
    }

    /// <summary>Creates a textDescriptionType (ICC.1:1998-09, 6.5.16): the ASCII description, without the Unicode and ScriptCode descriptions.</summary>
    private static byte[] CreateDescription(string text)
    {
        var count = text.Length + 1;
        var data = new byte[12 + count + 4 + 4 + 2 + 1 + 67];
        "desc"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)count);
        Encoding.ASCII.GetBytes(text, data.AsSpan(12));
        return data;
    }

    /// <summary>Creates a textType (ICC.1:1998-09, 6.5.15): ASCII text ended by a null character.</summary>
    private static byte[] CreateText(string text)
    {
        var data = new byte[8 + text.Length + 1];
        "text"u8.CopyTo(data);
        Encoding.ASCII.GetBytes(text, data.AsSpan(8));
        return data;
    }

    /// <summary>Creates an XYZType (ICC.1:1998-09, 6.5.19) of one XYZ number.</summary>
    private static byte[] CreateXyz(ReadOnlySpan<int> values)
    {
        var data = new byte[8 + (4 * values.Length)];
        "XYZ "u8.CopyTo(data);
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8 + (4 * i)), values[i]);
        }

        return data;
    }
}
