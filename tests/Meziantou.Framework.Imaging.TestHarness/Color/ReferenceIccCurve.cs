using System.Buffers.Binary;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// A one-dimensional ICC curve in <see cref="decimal"/> arithmetic: <c>curveType</c> (ICC.1:2022 section 10.6),
/// <c>parametricCurveType</c> (section 10.18) or a table of evenly spaced normalized entries.
/// </summary>
internal sealed class ReferenceIccCurve
{
    private readonly decimal[]? _table;
    private readonly int _function;
    private readonly decimal[] _parameters;

    private ReferenceIccCurve(decimal[] table)
    {
        _table = table;
        _function = -1;
        _parameters = [];
    }

    private ReferenceIccCurve(int function, decimal[] parameters)
    {
        _function = function;
        _parameters = parameters;
    }

    /// <summary>Creates a curve from normalized entries (at least two).</summary>
    public static ReferenceIccCurve FromTable(decimal[] table) => new(table);

    /// <summary>Parses a curve element and returns its unpadded length.</summary>
    public static ReferenceIccCurve Parse(ReadOnlySpan<byte> data, out int length)
    {
        if (data[..4].SequenceEqual("curv"u8))
        {
            var count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]));
            length = 12 + (2 * count);
            if (count == 0)
                return new ReferenceIccCurve([0m, 1m]);

            if (count == 1)
                return new ReferenceIccCurve(0, [BinaryPrimitives.ReadUInt16BigEndian(data[12..]) / 256m]);

            var table = new decimal[count];
            for (var i = 0; i < count; i++)
            {
                table[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(12 + (2 * i))..]) / 65535m;
            }

            return new ReferenceIccCurve(table);
        }

        if (data[..4].SequenceEqual("para"u8))
        {
            int function = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
            var count = function switch
            {
                0 => 1,
                1 => 3,
                2 => 4,
                3 => 5,
                4 => 7,
                _ => throw new NotSupportedException($"Parametric curve function {function} is not defined."),
            };

            var parameters = new decimal[count];
            for (var i = 0; i < count; i++)
            {
                parameters[i] = BinaryPrimitives.ReadInt32BigEndian(data[(12 + (4 * i))..]) / 65536m;
            }

            length = 12 + (4 * count);
            return new ReferenceIccCurve(function, parameters);
        }

        throw new NotSupportedException("The element is neither a curveType nor a parametricCurveType.");
    }

    /// <summary>Evaluates the curve: input and output are clipped to [0, 1].</summary>
    public decimal Evaluate(decimal x)
    {
        x = ReferenceIccMath.Clip(x);
        if (_table is not null)
        {
            var position = x * (_table.Length - 1);
            var index = (int)decimal.Floor(position);
            if (index >= _table.Length - 1)
                return _table[^1];

            return _table[index] + ((_table[index + 1] - _table[index]) * (position - index));
        }

        return ReferenceIccMath.Clip(EvaluateParametric(x));
    }

    /// <summary>
    /// Evaluates the generalized inverse: the smallest input in [0, 1] whose output reaches <paramref name="y"/> (0 when
    /// the first output already does, 1 when none does). A sampled curve is first replaced by its running maximum
    /// (minimum when it decreases).
    /// </summary>
    public decimal Invert(decimal y)
    {
        y = ReferenceIccMath.Clip(y);
        return _table is not null ? InvertTable(y) : ReferenceIccMath.Clip(InvertParametric(y));
    }

    /// <summary>ICC.1:2022 Table 68, with the parameters g, a, b, c, d, e, f in this order.</summary>
    private decimal EvaluateParametric(decimal x)
    {
        var p = _parameters;
        switch (_function)
        {
            case 0:
                return DecimalMath.Pow(x, p[0]);

            case 1:
                return x >= -p[2] / p[1] ? Power((p[1] * x) + p[2], p[0]) : 0;

            case 2:
                return x >= -p[2] / p[1] ? Power((p[1] * x) + p[2], p[0]) + p[3] : p[3];

            case 3:
                return x >= p[4] ? Power((p[1] * x) + p[2], p[0]) : p[3] * x;

            default:
                return x >= p[4] ? Power((p[1] * x) + p[2], p[0]) + p[5] : (p[3] * x) + p[6];
        }
    }

    private decimal InvertParametric(decimal y)
    {
        var p = _parameters;
        var g = p[0];
        if (g <= 0)
            throw new NotSupportedException("The reference only inverts parametric curves with a positive exponent.");

        if (_function == 0)
            return DecimalMath.Pow(y, 1 / g);

        var (a, b) = (p[1], p[2]);
        if (a <= 0)
            throw new NotSupportedException("The reference only inverts increasing parametric curves.");

        if (_function is 1 or 2)
        {
            // Below -b/a the curve is constant (0 or c), then it is the shifted power
            var constant = _function == 2 ? p[3] : 0;
            return y <= constant ? 0 : (DecimalMath.Pow(y - constant, 1 / g) - b) / a;
        }

        var (c, d) = (p[3], p[4]);
        var (e, f) = _function == 4 ? (p[5], p[6]) : (0m, 0m);
        if (c < 0 || (a * d) + b < 0)
            throw new NotSupportedException("The reference only inverts increasing parametric curves.");

        if (d <= 0)
        {
            // Only the power segment is inside [0, 1]
            return y <= DecimalMath.Pow(b, g) + e ? 0 : (DecimalMath.Pow(y - e, 1 / g) - b) / a;
        }

        // The linear segment starts at f; it reaches y before d, or the power segment does (from d)
        if (y <= f)
            return 0;

        if (c > 0 && (y - f) / c < d)
            return (y - f) / c;

        if (y <= DecimalMath.Pow((a * d) + b, g) + e)
            return d;

        return (DecimalMath.Pow(y - e, 1 / g) - b) / a;
    }

    private decimal InvertTable(decimal y)
    {
        var table = _table!;
        var decreasing = table[^1] < table[0];
        var envelope = new decimal[table.Length];
        envelope[0] = table[0];
        for (var i = 1; i < table.Length; i++)
        {
            envelope[i] = decreasing ? Math.Min(envelope[i - 1], table[i]) : Math.Max(envelope[i - 1], table[i]);
        }

        if (envelope[0] == envelope[^1])
            throw new NotSupportedException("A constant curve has no inverse.");

        // The first entry that reaches the value: the input is interpolated between it and the entry before
        for (var i = 0; i < envelope.Length; i++)
        {
            if (decreasing ? envelope[i] <= y : envelope[i] >= y)
                return i == 0 ? 0 : (i - 1 + ((y - envelope[i - 1]) / (envelope[i] - envelope[i - 1]))) / (envelope.Length - 1);
        }

        return 1;
    }

    /// <summary>A power of a base that may be negative: not a number then, which the clipping of the curve output turns into 0.</summary>
    private static decimal Power(decimal value, decimal exponent)
    {
        if (value < 0)
            throw new NotSupportedException("The reference does not evaluate a parametric curve whose power segment has a negative base.");

        return DecimalMath.Pow(value, exponent);
    }
}
