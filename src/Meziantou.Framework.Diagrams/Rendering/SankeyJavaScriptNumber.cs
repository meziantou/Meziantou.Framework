using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Writes numbers as JavaScript does, for the texts that Mermaid computes with its number methods.</summary>
internal static class SankeyJavaScriptNumber
{
    /// <summary>Writes a number as the <c>toString</c> of JavaScript: the shortest digits, in exponential notation below 1e-6 and from 1e21.</summary>
    public static string ToString(double value)
    {
        if (value == 0)
            return "0";

        if (!double.IsFinite(value))
            return double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity";

        var (negative, digits, n) = Decompose(value);
        var k = digits.Length;
        var builder = new StringBuilder();
        if (negative)
        {
            builder.Append('-');
        }

        if (k <= n && n <= 21)
        {
            builder.Append(digits).Append('0', n - k);
        }
        else if (n > 0 && n <= 21)
        {
            builder.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
        }
        else if (n > -6 && n <= 0)
        {
            builder.Append("0.").Append('0', -n).Append(digits);
        }
        else
        {
            var e = n - 1;
            builder.Append(digits[0]);
            if (k > 1)
            {
                builder.Append('.').Append(digits, 1, k - 1);
            }

            builder.Append('e').Append(e >= 0 ? '+' : '-').Append(Math.Abs(e).ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Returns the sign, the significant digits and n, such that the value is 0.digits × 10^n, of the shortest representation of a finite non-zero number.</summary>
    private static (bool Negative, string Digits, int N) Decompose(double value)
    {
        var negative = value < 0;
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var exponentIndex = text.IndexOfAny(['E', 'e']);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integerLength = point < 0 ? text.Length : point;
        var all = point < 0 ? text : text.Remove(point, 1);
        var leadingZeros = 0;
        while (leadingZeros < all.Length - 1 && all[leadingZeros] == '0')
        {
            leadingZeros++;
        }

        return (negative, all[leadingZeros..].TrimEnd('0'), integerLength - leadingZeros + exponent);
    }

    /// <summary>Writes a number as the <c>toExponential()</c> of JavaScript, without a number of digits: the shortest digits.</summary>
    public static string ToExponential(double value)
    {
        if (value == 0)
            return "0e+0";

        var (negative, digits, n) = Decompose(value);
        var e = n - 1;
        var builder = new StringBuilder();
        if (negative)
        {
            builder.Append('-');
        }

        builder.Append(digits[0]);
        if (digits.Length > 1)
        {
            builder.Append('.').Append(digits, 1, digits.Length - 1);
        }

        return builder.Append('e').Append(e >= 0 ? '+' : '-').Append(Math.Abs(e).ToString(CultureInfo.InvariantCulture)).ToString();
    }
}
