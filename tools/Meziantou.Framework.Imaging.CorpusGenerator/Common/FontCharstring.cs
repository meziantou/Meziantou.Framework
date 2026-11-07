namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Assembles a Type 2 charstring (Adobe Technical Note #5177) from numbers and operators, for the CFF fonts of
/// <see cref="FontCffWriter"/>.
/// </summary>
internal sealed class FontCharstring
{
    public const int HStem = 1;
    public const int VStem = 3;
    public const int VMoveTo = 4;
    public const int RLineTo = 5;
    public const int HLineTo = 6;
    public const int VLineTo = 7;
    public const int RRCurveTo = 8;
    public const int CallSubr = 10;
    public const int Return = 11;
    public const int EndChar = 14;

    /// <summary>CFF2: selects the subtable of the variation store the blends use.</summary>
    public const int VsIndex = 15;

    /// <summary>CFF2: replaces values and their deltas by the values of the instance.</summary>
    public const int Blend = 16;

    public const int HStemHM = 18;
    public const int HintMask = 19;
    public const int CntrMask = 20;
    public const int RMoveTo = 21;
    public const int HMoveTo = 22;
    public const int VStemHM = 23;
    public const int RCurveLine = 24;
    public const int RLineCurve = 25;
    public const int VVCurveTo = 26;
    public const int HHCurveTo = 27;
    public const int CallGSubr = 29;
    public const int VHCurveTo = 30;
    public const int HVCurveTo = 31;

    // The second byte of the two-byte operators.
    public const int And = 3;
    public const int Or = 4;
    public const int Not = 5;
    public const int Abs = 9;
    public const int Add = 10;
    public const int Sub = 11;
    public const int Div = 12;
    public const int Neg = 14;
    public const int Eq = 15;
    public const int Drop = 18;
    public const int Put = 20;
    public const int Get = 21;
    public const int IfElse = 22;
    public const int Random = 23;
    public const int Mul = 24;
    public const int Sqrt = 26;
    public const int Dup = 27;
    public const int Exch = 28;
    public const int Index = 29;
    public const int Roll = 30;
    public const int HFlex = 34;
    public const int Flex = 35;
    public const int HFlex1 = 36;
    public const int Flex1 = 37;

    private readonly List<byte> _bytes = [];

    public byte[] ToArray() => [.. _bytes];

    /// <summary>Adds integers, each in its shortest encoding.</summary>
    public FontCharstring Numbers(params ReadOnlySpan<int> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case >= -107 and <= 107:
                    _bytes.Add((byte)(value + 139));
                    break;

                case >= 108 and <= 1131:
                    _bytes.Add((byte)(((value - 108) >> 8) + 247));
                    _bytes.Add((byte)(value - 108));
                    break;

                case >= -1131 and <= -108:
                    _bytes.Add((byte)(((-value - 108) >> 8) + 251));
                    _bytes.Add((byte)(-value - 108));
                    break;

                case >= short.MinValue and <= short.MaxValue:
                    _bytes.Add(28);
                    _bytes.Add((byte)(value >> 8));
                    _bytes.Add((byte)value);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(values), "A charstring integer has 16 bits; use Fixed for the other numbers");
            }
        }

        return this;
    }

    /// <summary>Adds a number as a 16.16 fixed-point value.</summary>
    public FontCharstring Fixed(double value)
    {
        var bits = (int)Math.Round(value * 65536, MidpointRounding.AwayFromZero);
        _bytes.Add(255);
        _bytes.Add((byte)(bits >> 24));
        _bytes.Add((byte)(bits >> 16));
        _bytes.Add((byte)(bits >> 8));
        _bytes.Add((byte)bits);
        return this;
    }

    /// <summary>Adds numbers, then a one-byte operator.</summary>
    public FontCharstring Op(int @operator, params ReadOnlySpan<int> arguments)
    {
        Numbers(arguments);
        _bytes.Add((byte)@operator);
        return this;
    }

    /// <summary>Adds numbers, then a two-byte operator.</summary>
    public FontCharstring Escape(int @operator, params ReadOnlySpan<int> arguments)
    {
        Numbers(arguments);
        _bytes.Add(12);
        _bytes.Add((byte)@operator);
        return this;
    }

    /// <summary>Adds bytes as they are: the bits of a hint mask, or malformed content.</summary>
    public FontCharstring Raw(params ReadOnlySpan<byte> bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    /// <summary>Adds a call of a subroutine, whose number is biased by the number of subroutines of its kind.</summary>
    public FontCharstring Call(int @operator, int subroutine, int subroutineCount)
    {
        var bias = subroutineCount < 1240 ? 107 : subroutineCount < 33900 ? 1131 : 32768;
        return Op(@operator, subroutine - bias);
    }
}
