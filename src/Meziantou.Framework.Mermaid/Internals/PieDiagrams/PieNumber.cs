using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.PieDiagrams;

/// <summary>Reads and writes the numbers of the Langium grammars of the pie and radar charts: digits and an optional fraction, without exponent.</summary>
internal static class PieNumber
{
    /// <summary>Writes a finite number with all its digits, without exponent: the shortest digits that read back to the same number.</summary>
    public static string Format(double value)
    {
        if (value == 0)
            return "0";

        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? text : text[..dot];
        var all = dot < 0 ? text : integer + text[(dot + 1)..];
        var leadingZeros = all.Length - all.TrimStart('0').Length;
        var digits = all.Trim('0');

        // The number is 0.digits × 10^n
        var n = integer.Length - leadingZeros + exponent;
        var builder = new StringBuilder();
        if (value < 0)
        {
            builder.Append('-');
        }

        if (n >= digits.Length)
        {
            builder.Append(digits).Append('0', n - digits.Length);
        }
        else if (n > 0)
        {
            builder.Append(digits, 0, n).Append('.').Append(digits, n, digits.Length - n);
        }
        else
        {
            builder.Append("0.").Append('0', -n).Append(digits);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a number: digits, and a fraction after a dot. As the terminals of Mermaid, an integer cannot start with a
    /// zero, and a dot cannot follow the number.
    /// </summary>
    public static double Read(Scanner scanner, bool allowMinus)
    {
        var start = scanner.Position;
        if (allowMinus && scanner.Current == '-')
        {
            scanner.Position++;
        }

        var digitsStart = scanner.Position;
        while (char.IsAsciiDigit(scanner.Current))
        {
            scanner.Position++;
        }

        if (scanner.Position == digitsStart)
        {
            scanner.Position = start;
            throw scanner.Error($"Unexpected '{scanner.Describe()}': a number is expected.");
        }

        var integerLength = scanner.Position - digitsStart;
        var hasFraction = false;
        if (scanner.Current == '.' && char.IsAsciiDigit(scanner.Peek()))
        {
            scanner.Position++;
            while (char.IsAsciiDigit(scanner.Current))
            {
                scanner.Position++;
            }

            hasFraction = true;
        }

        var text = scanner.Text[start..scanner.Position];
        if (scanner.Current == '.')
            throw scanner.Error(start, $"'{text}.' is not a valid number.");

        if (!hasFraction && integerLength > 1 && scanner.Text[digitsStart] == '0')
            throw scanner.Error(start, $"'{text}' is not a valid number: it cannot start with 0.");

        var value = double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (!double.IsFinite(value))
            throw scanner.Error(start, $"The number '{text}' is too large.");

        return value;
    }
}
