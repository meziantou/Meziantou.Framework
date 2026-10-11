using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The ICC profiles provided by the library, built from the numbers of IEC 61966-2-1 (sRGB): version 4.4 display
/// profiles with a CIEXYZ connection space, parametric or identity tone curves, and for RGB the sRGB primaries adapted
/// from D65 to the D50 illuminant of the connection space with the linear Bradford transform.
/// </summary>
/// <remarks>
/// The profiles are ordinary profiles: conversions read them through the same code as any other profile. Their bytes are
/// deterministic (fixed creation date, no profile ID).
/// </remarks>
internal static class BuiltInIccProfiles
{
    // s15Fixed16 numbers. The colorants are rounded so that they sum exactly to the D50 illuminant of the header: white
    // (1, 1, 1) converts to the white of the connection space
    private static readonly int[] D50 = [0x0000F6D6, 0x00010000, 0x0000D32D];
    private static readonly int[] RedColorant = [0x00006FA0, 0x000038F5, 0x00000390];
    private static readonly int[] GreenColorant = [0x00006297, 0x0000B787, 0x000018DA];
    private static readonly int[] BlueColorant = [0x0000249F, 0x00000F84, 0x0000B6C3];

    // The Bradford matrix adapting D65 to D50 (chromaticAdaptationTag)
    private static readonly int[] ChromaticAdaptation =
    [
        0x00010C42, 0x000005DE, unchecked((int)0xFFFFF325),
        0x00000793, 0x0000FD90, unchecked((int)0xFFFFFBA1),
        unchecked((int)0xFFFFFDA2), 0x000003DC, 0x0000C06E,
    ];

    // The sRGB decoding function as a parametric curve of type 3: g = 2.4, a = 1 / 1.055, b = 0.055 / 1.055,
    // c = 1 / 12.92, d = 0.04045
    private static readonly int[] SrgbCurveParameters = [0x00026666, 0x0000F2A7, 0x00000D59, 0x000013D0, 0x00000A5B];

    /// <summary>Gets the sRGB profile.</summary>
    public static IccProfile Srgb { get; } = Create(gray: false, linear: false, "sRGB IEC61966-2.1");

    /// <summary>Gets the grayscale profile with the sRGB transfer function and white point.</summary>
    public static IccProfile SrgbGray { get; } = Create(gray: true, linear: false, "sGray");

    /// <summary>Gets the profile of linear-light samples with the sRGB primaries and white point.</summary>
    public static IccProfile LinearSrgb { get; } = Create(gray: false, linear: true, "Linear sRGB");

    /// <summary>Gets the profile of linear-light grayscale samples with the sRGB white point.</summary>
    public static IccProfile LinearGray { get; } = Create(gray: true, linear: true, "Linear gray");

    private static IccProfile Create(bool gray, bool linear, string description)
    {
        var curve = linear ? CreateIdentityCurve() : CreateSrgbCurve();
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", CreateText(description)),
            ("cprt", CreateText("Public domain (CC0-1.0)")),
            ("wtpt", CreateNumbers("XYZ ", D50)),
            ("chad", CreateNumbers("sf32", ChromaticAdaptation)),
        };

        if (gray)
        {
            tags.Add(("kTRC", curve));
        }
        else
        {
            tags.Add(("rXYZ", CreateNumbers("XYZ ", RedColorant)));
            tags.Add(("gXYZ", CreateNumbers("XYZ ", GreenColorant)));
            tags.Add(("bXYZ", CreateNumbers("XYZ ", BlueColorant)));
            tags.Add(("rTRC", curve));
            tags.Add(("gTRC", curve));
            tags.Add(("bTRC", curve));
        }

        // Layout: header, tag table, then each distinct tag data on a 4-byte boundary (the three curves share theirs)
        var offsets = new int[tags.Count];
        var size = IccReader.HeaderSize + 4 + (IccReader.TagEntrySize * tags.Count);
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

        var data = new byte[size];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x04400000);
        "mntr"u8.CopyTo(span[12..]);
        (gray ? "GRAY"u8 : "RGB "u8).CopyTo(span[16..]);
        "XYZ "u8.CopyTo(span[20..]);

        // Creation date: 2026-01-01 00:00:00 UTC
        BinaryPrimitives.WriteUInt16BigEndian(span[24..], 2026);
        BinaryPrimitives.WriteUInt16BigEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(span[28..], 1);
        "acsp"u8.CopyTo(span[36..]);
        for (var i = 0; i < 3; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(span[(68 + (4 * i))..], D50[i]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(span[IccReader.HeaderSize..], (uint)tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var entry = span.Slice(IccReader.HeaderSize + 4 + (IccReader.TagEntrySize * i), IccReader.TagEntrySize);
            Encoding.ASCII.GetBytes(tags[i].Signature, entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Data.Length);
            tags[i].Data.CopyTo(span[offsets[i]..]);
        }

        return new IccProfile(MetadataBlob.FromOwnedArray(data));
    }

    /// <summary>Creates a tag made of a type signature, four reserved bytes and 32-bit numbers (XYZType, s15Fixed16ArrayType).</summary>
    private static byte[] CreateNumbers(string type, ReadOnlySpan<int> values)
    {
        var data = new byte[8 + (4 * values.Length)];
        Encoding.ASCII.GetBytes(type, data);
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8 + (4 * i)), values[i]);
        }

        return data;
    }

    /// <summary>Creates a parametricCurveType of function type 3 with the sRGB parameters.</summary>
    private static byte[] CreateSrgbCurve()
    {
        var data = new byte[12 + (4 * SrgbCurveParameters.Length)];
        "para"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), 3);
        for (var i = 0; i < SrgbCurveParameters.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12 + (4 * i)), SrgbCurveParameters[i]);
        }

        return data;
    }

    /// <summary>Creates a curveType without entry: the identity.</summary>
    private static byte[] CreateIdentityCurve()
    {
        var data = new byte[12];
        "curv"u8.CopyTo(data);
        return data;
    }

    /// <summary>Creates a multiLocalizedUnicodeType with one en-US record.</summary>
    private static byte[] CreateText(string text)
    {
        var length = Encoding.BigEndianUnicode.GetByteCount(text);
        var data = new byte[28 + length];
        "mluc"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 12);
        "enUS"u8.CopyTo(data.AsSpan(16));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), 28);
        Encoding.BigEndianUnicode.GetBytes(text, data.AsSpan(28));
        return data;
    }
}
