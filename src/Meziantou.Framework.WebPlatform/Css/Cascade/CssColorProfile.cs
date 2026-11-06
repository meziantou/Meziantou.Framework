using System.Buffers.Binary;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The ICC profile of a custom color space (https://drafts.csswg.org/css-color-5/#at-profile): an RGB display profile
/// based on a matrix and tone curves (ICC.1:2022 section 8.5.3, the profiles of the displays and of the RGB color spaces),
/// which converts the components of <c>color(--name r g b)</c> to the CIEXYZ of the D50 profile connection space. The
/// profiles based on lookup tables (and the other color spaces, e.g. CMYK) are not supported.
/// </summary>
internal sealed class CssColorProfile
{
    private const int HeaderSize = 128;

    private readonly ToneCurve[] _curves;
    private readonly double[] _matrix;

    private CssColorProfile(ToneCurve[] curves, double[] matrix)
    {
        _curves = curves;
        _matrix = matrix;
    }

    /// <summary>The number of components of the colors of the profile.</summary>
    public static int ComponentCount => 3;

    /// <summary>Parses the bytes of an ICC profile, or returns null when it is not an RGB profile based on a matrix and tone curves.</summary>
    public static CssColorProfile? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + 4 || !data.Slice(36, 4).SequenceEqual("acsp"u8) || !data.Slice(16, 4).SequenceEqual("RGB "u8) || !data.Slice(20, 4).SequenceEqual("XYZ "u8))
            return null;

        var tagCount = BinaryPrimitives.ReadUInt32BigEndian(data[HeaderSize..]);
        if (tagCount > (data.Length - HeaderSize - 4) / 12)
            return null;

        ReadOnlySpan<byte> red = default, green = default, blue = default, redCurve = default, greenCurve = default, blueCurve = default;
        for (var i = 0; i < (int)tagCount; i++)
        {
            var entry = data.Slice(HeaderSize + 4 + (i * 12), 12);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            if (offset > data.Length || size > data.Length - offset)
                return null;

            var tag = data.Slice((int)offset, (int)size);
            switch (System.Text.Encoding.ASCII.GetString(entry[..4]))
            {
                case "rXYZ": red = tag; break;
                case "gXYZ": green = tag; break;
                case "bXYZ": blue = tag; break;
                case "rTRC": redCurve = tag; break;
                case "gTRC": greenCurve = tag; break;
                case "bTRC": blueCurve = tag; break;
            }
        }

        // The columns of the matrix are the colorants (already adapted to the D50 illuminant of the connection space)
        var matrix = new double[9];
        if (!TryReadXyz(red, matrix, 0) || !TryReadXyz(green, matrix, 1) || !TryReadXyz(blue, matrix, 2))
            return null;

        if (ToneCurve.Parse(redCurve) is not { } r || ToneCurve.Parse(greenCurve) is not { } g || ToneCurve.Parse(blueCurve) is not { } b)
            return null;

        return new CssColorProfile([r, g, b], matrix);
    }

    /// <summary>Converts the components of a color (from 0 to 1, the values outside of the range are clamped) to the CIEXYZ of the D50 connection space.</summary>
    public double[] ToXyzD50(ReadOnlySpan<double> components)
    {
        var linear = new double[3];
        for (var i = 0; i < 3; i++)
        {
            linear[i] = _curves[i].Evaluate(double.IsNaN(components[i]) ? 0 : Math.Clamp(components[i], 0, 1));
        }

        return
        [
            (_matrix[0] * linear[0]) + (_matrix[1] * linear[1]) + (_matrix[2] * linear[2]),
            (_matrix[3] * linear[0]) + (_matrix[4] * linear[1]) + (_matrix[5] * linear[2]),
            (_matrix[6] * linear[0]) + (_matrix[7] * linear[1]) + (_matrix[8] * linear[2]),
        ];
    }

    /// <summary>Reads an XYZType tag (ICC.1:2022 section 10.31) into a column of the matrix.</summary>
    private static bool TryReadXyz(ReadOnlySpan<byte> tag, double[] matrix, int column)
    {
        if (tag.Length < 20 || !tag[..4].SequenceEqual("XYZ "u8))
            return false;

        for (var row = 0; row < 3; row++)
        {
            matrix[(row * 3) + column] = ReadS15Fixed16(tag[(8 + (row * 4))..]);
        }

        return true;
    }

    private static double ReadS15Fixed16(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadInt32BigEndian(data) / 65536.0;

    /// <summary>A tone curve: a curveType (ICC.1:2022 section 10.6) or a parametricCurveType (section 10.18).</summary>
    private sealed class ToneCurve
    {
        private readonly double[]? _table;
        private readonly double[]? _parameters;
        private readonly int _functionType;

        private ToneCurve(double[]? table, int functionType, double[]? parameters)
        {
            _table = table;
            _functionType = functionType;
            _parameters = parameters;
        }

        public static ToneCurve? Parse(ReadOnlySpan<byte> tag)
        {
            if (tag.Length >= 12 && tag[..4].SequenceEqual("curv"u8))
            {
                var count = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
                if (count > (tag.Length - 12) / 2)
                    return null;

                // No entry is the identity, one entry a gamma (u8Fixed8Number), more a sampled curve
                if (count == 0)
                    return new ToneCurve(table: null, functionType: 0, [1]);

                if (count == 1)
                    return new ToneCurve(table: null, functionType: 0, [BinaryPrimitives.ReadUInt16BigEndian(tag[12..]) / 256.0]);

                var table = new double[count];
                for (var i = 0; i < table.Length; i++)
                {
                    table[i] = BinaryPrimitives.ReadUInt16BigEndian(tag[(12 + (i * 2))..]) / 65535.0;
                }

                return new ToneCurve(table, functionType: 0, parameters: null);
            }

            if (tag.Length >= 12 && tag[..4].SequenceEqual("para"u8))
            {
                var functionType = BinaryPrimitives.ReadUInt16BigEndian(tag[8..]);
                var parameterCount = functionType switch
                {
                    0 => 1,
                    1 => 3,
                    2 => 4,
                    3 => 5,
                    4 => 7,
                    _ => -1,
                };
                if (parameterCount < 0 || tag.Length < 12 + (parameterCount * 4))
                    return null;

                var parameters = new double[parameterCount];
                for (var i = 0; i < parameterCount; i++)
                {
                    parameters[i] = ReadS15Fixed16(tag[(12 + (i * 4))..]);
                }

                return new ToneCurve(table: null, functionType, parameters);
            }

            return null;
        }

        public double Evaluate(double x)
        {
            if (_table is { } table)
            {
                // Linear interpolation between the samples
                var position = x * (table.Length - 1);
                var index = Math.Min((int)position, table.Length - 2);
                return table[index] + ((table[index + 1] - table[index]) * (position - index));
            }

            var p = _parameters!;
            var g = p[0];
            return _functionType switch
            {
                0 => Math.Pow(x, g),
                1 => x >= -p[2] / p[1] ? Math.Pow((p[1] * x) + p[2], g) : 0,
                2 => x >= -p[2] / p[1] ? Math.Pow((p[1] * x) + p[2], g) + p[3] : p[3],
                3 => x >= p[4] ? Math.Pow((p[1] * x) + p[2], g) : p[3] * x,
                _ => x >= p[4] ? Math.Pow((p[1] * x) + p[2], g) + p[5] : (p[3] * x) + p[6],
            };
        }
    }
}
