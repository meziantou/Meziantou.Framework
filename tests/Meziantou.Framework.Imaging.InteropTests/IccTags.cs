using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>Reads the tags of ICC profiles and evaluates their tone curves, independently of the library.</summary>
internal static class IccTags
{
    /// <summary>Reads the tag table of a profile: the data of each tag, by signature.</summary>
    public static Dictionary<string, ReadOnlyMemory<byte>> Read(byte[] profile)
    {
        var tags = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var count = BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(128));
        for (var i = 0; i < count; i++)
        {
            var entry = profile.AsSpan(132 + (12 * i), 12);
            var signature = System.Text.Encoding.ASCII.GetString(entry[..4]);
            tags[signature] = profile.AsMemory(BinaryPrimitives.ReadInt32BigEndian(entry[4..]), BinaryPrimitives.ReadInt32BigEndian(entry[8..]));
        }

        return tags;
    }

    /// <summary>Evaluates a <c>curveType</c> or a <c>parametricCurveType</c> of ICC.1:2022 (sections 10.6 and 10.18) at a device value.</summary>
    public static double EvaluateCurve(ReadOnlySpan<byte> tag, double x)
    {
        if (tag[..4].SequenceEqual("curv"u8))
        {
            var count = BinaryPrimitives.ReadInt32BigEndian(tag[8..]);
            if (count == 0)
                return x;

            if (count == 1)
                return Math.Pow(x, BinaryPrimitives.ReadUInt16BigEndian(tag[12..]) / 256.0);

            var position = x * (count - 1);
            var index = Math.Min((int)position, count - 2);
            var low = BinaryPrimitives.ReadUInt16BigEndian(tag[(12 + (2 * index))..]) / 65535.0;
            var high = BinaryPrimitives.ReadUInt16BigEndian(tag[(14 + (2 * index))..]) / 65535.0;
            return low + ((high - low) * (position - index));
        }

        Assert.True(tag[..4].SequenceEqual("para"u8), "The curve is neither a curveType nor a parametricCurveType.");
        var function = BinaryPrimitives.ReadUInt16BigEndian(tag[8..]);
        Span<double> p = stackalloc double[7];
        var parameterCount = function switch
        {
            0 => 1,
            1 => 3,
            2 => 4,
            3 => 5,
            4 => 7,
            _ => throw new InvalidOperationException($"Unknown parametric function {function}."),
        };
        for (var i = 0; i < parameterCount; i++)
        {
            p[i] = BinaryPrimitives.ReadInt32BigEndian(tag[(12 + (4 * i))..]) / 65536.0;
        }

        return function switch
        {
            0 => Math.Pow(x, p[0]),
            1 => x >= -p[2] / p[1] ? Math.Pow((p[1] * x) + p[2], p[0]) : 0,
            2 => x >= -p[2] / p[1] ? Math.Pow((p[1] * x) + p[2], p[0]) + p[3] : p[3],
            3 => x >= p[4] ? Math.Pow((p[1] * x) + p[2], p[0]) : p[3] * x,
            _ => x >= p[4] ? Math.Pow((p[1] * x) + p[2], p[0]) + p[5] : (p[3] * x) + p[6],
        };
    }
}
