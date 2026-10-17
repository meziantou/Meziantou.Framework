using System.Text;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>Punycode (RFC 3492).</summary>
internal static class Punycode
{
    private const int Base = 36;
    private const int TMin = 1;
    private const int TMax = 26;
    private const int Skew = 38;
    private const int Damp = 700;
    private const int InitialBias = 72;
    private const int InitialN = 128;
    private const char Delimiter = '-';

    private static int Adapt(int delta, int numPoints, bool firstTime)
    {
        delta = firstTime ? delta / Damp : delta / 2;
        delta += delta / numPoints;
        var k = 0;
        while (delta > ((Base - TMin) * TMax) / 2)
        {
            delta /= Base - TMin;
            k += Base;
        }

        return k + (((Base - TMin + 1) * delta) / (delta + Skew));
    }

    private static int DecodeDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0' + 26,
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a',
        _ => Base,
    };

    private static char EncodeDigit(int digit) => (char)(digit < 26 ? 'a' + digit : '0' + digit - 26);

    /// <summary>Decodes a Punycode string (without the "xn--" prefix). Returns null on failure.</summary>
    public static string? Decode(ReadOnlySpan<char> input)
    {
        var output = new List<int>(input.Length);
        var basicLength = input.LastIndexOf(Delimiter);
        if (basicLength < 0)
        {
            basicLength = 0;
        }

        for (var j = 0; j < basicLength; j++)
        {
            if (input[j] >= 0x80)
                return null;

            output.Add(input[j]);
        }

        var n = InitialN;
        var i = 0;
        var bias = InitialBias;
        for (var index = basicLength > 0 ? basicLength + 1 : 0; index < input.Length;)
        {
            var oldI = i;
            var w = 1;
            for (var k = Base; ; k += Base)
            {
                if (index >= input.Length)
                    return null;

                var digit = DecodeDigit(input[index++]);
                if (digit >= Base)
                    return null;

                if (digit > (int.MaxValue - i) / w)
                    return null;

                i += digit * w;
                var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                if (digit < t)
                    break;

                if (w > int.MaxValue / (Base - t))
                    return null;

                w *= Base - t;
            }

            bias = Adapt(i - oldI, output.Count + 1, oldI == 0);
            if (i / (output.Count + 1) > int.MaxValue - n)
                return null;

            n += i / (output.Count + 1);
            i %= output.Count + 1;
            if (n > 0x10FFFF || n is >= 0xD800 and <= 0xDFFF)
                return null;

            output.Insert(i, n);
            i++;
        }

        var builder = new StringBuilder(output.Count);
        foreach (var codePoint in output)
        {
            builder.Append(new Rune(codePoint));
        }

        return builder.ToString();
    }

    /// <summary>Encodes a string to Punycode (without the "xn--" prefix). Returns null on overflow.</summary>
    public static string? Encode(ReadOnlySpan<char> input)
    {
        var codePoints = new List<int>(input.Length);
        foreach (var rune in input.EnumerateRunes())
        {
            codePoints.Add(rune.Value);
        }

        var output = new StringBuilder(input.Length + 8);
        foreach (var codePoint in codePoints)
        {
            if (codePoint < 0x80)
            {
                output.Append((char)codePoint);
            }
        }

        var basicLength = output.Length;
        var handled = basicLength;
        if (basicLength > 0)
        {
            output.Append(Delimiter);
        }

        var n = InitialN;
        var delta = 0;
        var bias = InitialBias;
        while (handled < codePoints.Count)
        {
            var m = int.MaxValue;
            foreach (var codePoint in codePoints)
            {
                if (codePoint >= n && codePoint < m)
                {
                    m = codePoint;
                }
            }

            if (m - n > (int.MaxValue - delta) / (handled + 1))
                return null;

            delta += (m - n) * (handled + 1);
            n = m;
            foreach (var codePoint in codePoints)
            {
                if (codePoint < n)
                {
                    if (++delta == int.MaxValue)
                        return null;
                }

                if (codePoint == n)
                {
                    var q = delta;
                    for (var k = Base; ; k += Base)
                    {
                        var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                        if (q < t)
                            break;

                        output.Append(EncodeDigit(t + ((q - t) % (Base - t))));
                        q = (q - t) / (Base - t);
                    }

                    output.Append(EncodeDigit(q));
                    bias = Adapt(delta, handled + 1, handled == basicLength);
                    delta = 0;
                    handled++;
                }
            }

            delta++;
            n++;
        }

        return output.ToString();
    }
}
