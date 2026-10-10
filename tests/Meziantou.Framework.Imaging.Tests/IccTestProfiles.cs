using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Synthetic ICC profiles for the color conversion tests, built field by field from ICC.1:2001-04 (version 2) and
/// ICC.1:2022 (version 4): header, tag table and tag data. Nothing here uses the library to produce profile bytes.
/// </summary>
internal static class IccTestProfiles
{
    /// <summary>The sRGB primaries adapted to D50 (columns red, green, blue; each X, Y, Z).</summary>
    public static readonly double[][] SrgbColorants = [[0.4360747, 0.2225045, 0.0139322], [0.3850649, 0.7168786, 0.0971045], [0.1430804, 0.0606169, 0.7141733]];

    /// <summary>The Display P3 primaries adapted to D50.</summary>
    public static readonly double[][] DisplayP3Colorants = [[0.5151, 0.2412, -0.0011], [0.2919, 0.6922, 0.0419], [0.1572, 0.0666, 0.7841]];

    /// <summary>A <c>curveType</c>: no entry is the identity, one entry is a u8Fixed8 gamma, more are samples.</summary>
    public static byte[] Curve(params ushort[] entries)
    {
        var data = new byte[12 + (2 * entries.Length)];
        "curv"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12 + (2 * i)), entries[i]);
        }

        return data;
    }

    /// <summary>A <c>curveType</c> with one entry: the gamma as a u8Fixed8Number (<paramref name="gamma256"/> / 256).</summary>
    public static byte[] Gamma(ushort gamma256) => Curve(gamma256);

    /// <summary>A <c>parametricCurveType</c>; the parameters are rounded to s15Fixed16Number.</summary>
    public static byte[] Parametric(ushort function, params double[] parameters)
    {
        var data = new byte[12 + (4 * parameters.Length)];
        "para"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), function);
        for (var i = 0; i < parameters.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12 + (4 * i)), ToS15Fixed16(parameters[i]));
        }

        return data;
    }

    /// <summary>The sRGB decoding function as a parametric curve of type 3.</summary>
    public static byte[] SrgbCurve() => Parametric(3, 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045);

    /// <summary>An <c>XYZType</c> with one value, rounded to s15Fixed16Number.</summary>
    public static byte[] Xyz(double x, double y, double z)
    {
        var data = new byte[20];
        "XYZ "u8.CopyTo(data);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), ToS15Fixed16(x));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), ToS15Fixed16(y));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), ToS15Fixed16(z));
        return data;
    }

    /// <summary>A matrix-based RGB display profile with the same curve for the three channels.</summary>
    public static IccProfile Rgb(double[][] colorants, byte[] curve) => Rgb(colorants, curve, curve, curve);

    /// <summary>A matrix-based RGB display profile.</summary>
    public static IccProfile Rgb(double[][] colorants, byte[] red, byte[] green, byte[] blue)
        => Build(
            "RGB ",
            "XYZ ",
            ("rXYZ", Xyz(colorants[0][0], colorants[0][1], colorants[0][2])),
            ("gXYZ", Xyz(colorants[1][0], colorants[1][1], colorants[1][2])),
            ("bXYZ", Xyz(colorants[2][0], colorants[2][1], colorants[2][2])),
            ("rTRC", red),
            ("gTRC", green),
            ("bTRC", blue));

    /// <summary>A monochrome display profile.</summary>
    public static IccProfile Gray(byte[] curve, string connectionSpace = "XYZ ") => Build("GRAY", connectionSpace, ("kTRC", curve));

    /// <summary>A version 4 display profile with the given tags.</summary>
    public static IccProfile Build(string colorSpace, string connectionSpace, params (string Signature, byte[] Data)[] tags)
        => new(new MetadataBlob(BuildBytes(colorSpace, connectionSpace, tags)));

    /// <summary>The bytes of a profile: a version 4 display profile unless the header is changed by <paramref name="configureHeader"/>.</summary>
    public static byte[] BuildBytes(string colorSpace, string connectionSpace, (string Signature, byte[] Data)[] tags, Action<byte[]>? configureHeader = null)
    {
        var tableSize = 132 + (12 * tags.Length);
        var body = new List<byte>(new byte[tableSize]);
        var entries = new (int Offset, int Length)[tags.Length];
        for (var i = 0; i < tags.Length; i++)
        {
            // Identical tag data is stored once, as real profiles do for their three tone curves
            var shared = Array.FindIndex(tags, tag => ReferenceEquals(tag.Data, tags[i].Data));
            if (shared < i)
            {
                entries[i] = entries[shared];
                continue;
            }

            while (body.Count % 4 != 0)
            {
                body.Add(0);
            }

            entries[i] = (body.Count, tags[i].Data.Length);
            body.AddRange(tags[i].Data);
        }

        while (body.Count % 4 != 0)
        {
            body.Add(0);
        }

        var data = body.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        data[8] = 4;
        data[9] = 0x40;
        "mntr"u8.CopyTo(data.AsSpan(12));
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16);
        Encoding.ASCII.GetBytes(connectionSpace).CopyTo(data, 20);
        "acsp"u8.CopyTo(data.AsSpan(36));

        // The PCS illuminant: D50
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(68), 0x0000F6D6);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(72), 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(76), 0x0000D32D);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(128), (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var entry = data.AsSpan(132 + (12 * i), 12);
            Encoding.ASCII.GetBytes(tags[i].Signature).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)entries[i].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)entries[i].Length);
        }

        configureHeader?.Invoke(data);
        return data;
    }

    public static int ToS15Fixed16(double value) => (int)Math.Round(value * 65536, MidpointRounding.AwayFromZero);
}
