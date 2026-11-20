using System.Globalization;
using System.Text;
using System.Xml.XPath;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>xsl:number (https://www.w3.org/TR/xslt-10/#number) and format-number() (https://www.w3.org/TR/xslt-10/#format-number).</summary>
internal static class XsltNumbering
{
    // The largest number formatted with letters or roman numerals (the longer ones are formatted with digits)
    private const double MaxAlphabeticNumber = 1e9;

    /// <summary>The numbers of a node for a level of xsl:number (https://www.w3.org/TR/xslt-10/#number).</summary>
    public static List<double> Count(XPathNavigator node, string level, XsltPattern? count, XsltPattern? from)
    {
        bool MatchesCount(XPathNavigator candidate)
        {
            if (count is not null)
                return count.Matches(candidate);

            // The nodes of the same type, and with the same expanded name, as the current node
            if (candidate.NodeType != node.NodeType)
                return false;

            return node.NodeType switch
            {
                XPathNodeType.Element or XPathNodeType.Attribute => candidate.LocalName == node.LocalName && candidate.NamespaceURI == node.NamespaceURI,
                XPathNodeType.ProcessingInstruction or XPathNodeType.Namespace => candidate.LocalName == node.LocalName,
                _ => true,
            };
        }

        bool MatchesFrom(XPathNavigator candidate) => from?.Matches(candidate) == true;

        double CountPrecedingSiblings(XPathNavigator start)
        {
            var result = 1d;
            var sibling = start.Clone();
            while (sibling.MoveToPrevious())
            {
                XsltExecution.GetCurrent().CheckLimits();
                if (MatchesCount(sibling))
                {
                    result++;
                }
            }

            return result;
        }

        switch (level)
        {
            case "multiple":
            {
                var result = new List<double>();
                var current = node.Clone();
                while (true)
                {
                    if (MatchesFrom(current))
                        break;

                    if (MatchesCount(current))
                    {
                        result.Add(CountPrecedingSiblings(current));
                    }

                    if (!current.MoveToParent())
                        break;
                }

                result.Reverse();
                return result;
            }

            case "any":
            {
                // The nodes that match the count pattern among the current node and the preceding and ancestor nodes, after
                // the last node that matches the from pattern
                var result = 0d;
                var current = node.Clone();
                var isCurrent = true;
                while (true)
                {
                    XsltExecution.GetCurrent().CheckLimits();
                    if (!isCurrent && MatchesFrom(current))
                        break;

                    if (MatchesCount(current))
                    {
                        result++;
                    }

                    isCurrent = false;
                    if (!MoveToPreviousInDocumentOrder(current))
                        break;
                }

                return result == 0 ? [] : [result];
            }

            default:
            {
                var current = node.Clone();
                while (!MatchesCount(current))
                {
                    if (MatchesFrom(current) || !current.MoveToParent())
                        return [];
                }

                return [CountPrecedingSiblings(current)];
            }
        }
    }

    /// <summary>Moves to the previous node in document order (the attributes and the namespace nodes are skipped).</summary>
    private static bool MoveToPreviousInDocumentOrder(XPathNavigator node)
    {
        if (node.NodeType is XPathNodeType.Attribute or XPathNodeType.Namespace)
            return node.MoveToParent();

        if (!node.MoveToPrevious())
            return node.MoveToParent();

        // The last descendant of the previous sibling
        while (node.MoveToFirstChild())
        {
            while (node.MoveToNext())
            {
            }
        }

        return true;
    }

    /// <summary>Formats a list of numbers with a format string (https://www.w3.org/TR/xslt-10/#convert).</summary>
    public static string Format(List<double> numbers, string format, string? groupingSeparator, int groupingSize)
    {
        // The format tokens (alphanumeric) and the separators between them
        var tokens = new List<string>();
        var separators = new List<string>();
        var suffix = "";
        var index = 0;
        while (index < format.Length && !IsAlphanumeric(format, index))
        {
            index += char.IsSurrogatePair(format, index) ? 2 : 1;
        }

        var prefix = format[..index];
        while (index < format.Length)
        {
            var start = index;
            while (index < format.Length && IsAlphanumeric(format, index))
            {
                index += char.IsSurrogatePair(format, index) ? 2 : 1;
            }

            tokens.Add(format[start..index]);
            start = index;
            while (index < format.Length && !IsAlphanumeric(format, index))
            {
                index += char.IsSurrogatePair(format, index) ? 2 : 1;
            }

            if (index < format.Length)
            {
                separators.Add(format[start..index]);
            }
            else
            {
                suffix = format[start..index];
            }
        }

        if (tokens.Count == 0)
        {
            tokens.Add("1");
        }

        if (numbers.Count == 0)
            return "";

        var builder = new StringBuilder(prefix);
        for (var i = 0; i < numbers.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(separators.Count == 0 ? "." : separators[Math.Min(i - 1, separators.Count - 1)]);
            }

            builder.Append(FormatToken(numbers[i], tokens[Math.Min(i, tokens.Count - 1)], groupingSeparator, groupingSize));
        }

        builder.Append(suffix);
        return builder.ToString();
    }

    private static bool IsAlphanumeric(string text, int index)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(text, index);
        return category is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;
    }

    private static string FormatToken(double number, string token, string? groupingSeparator, int groupingSize)
    {
        if (number >= 1 && number <= MaxAlphabeticNumber)
        {
            switch (token)
            {
                case "A":
                    return ToAlphabetic((long)number, 'A');
                case "a":
                    return ToAlphabetic((long)number, 'a');
                case "I" when number < 4000:
                    return ToRoman((int)number);
                case "i" when number < 4000:
                    return ToRoman((int)number).ToLowerInvariant();
            }
        }

        // A decimal token: the digits of its length (01 formats 1 as 01), with the zero of the token
        var width = 1;
        var zero = '0';
        if (token.Length > 1 && token[^1] is var last && char.IsDigit(last) && CharUnicodeInfo.GetDecimalDigitValue(last) == 1)
        {
            var tokenZero = (char)(last - 1);
            var allZeros = true;
            for (var i = 0; i < token.Length - 1; i++)
            {
                if (token[i] != tokenZero)
                {
                    allZeros = false;
                    break;
                }
            }

            if (allZeros)
            {
                width = token.Length;
                zero = tokenZero;
            }
        }
        else if (token.Length == 1 && char.IsDigit(token[0]) && CharUnicodeInfo.GetDecimalDigitValue(token[0]) == 1)
        {
            zero = (char)(token[0] - 1);
        }

        var digits = number < 1e15 ? ((long)number).ToString(CultureInfo.InvariantCulture) : XsltValues.NumberToString(number);
        if (digits.Length < width)
        {
            digits = new string('0', width - digits.Length) + digits;
        }

        if (groupingSeparator is not null && groupingSize > 0 && digits.Length > groupingSize)
        {
            var grouped = new StringBuilder();
            for (var i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % groupingSize == 0)
                {
                    grouped.Append(groupingSeparator);
                }

                grouped.Append(digits[i]);
            }

            digits = grouped.ToString();
        }

        if (zero != '0')
        {
            var mapped = new StringBuilder(digits.Length);
            foreach (var c in digits)
            {
                mapped.Append(c is >= '0' and <= '9' ? (char)(zero + (c - '0')) : c);
            }

            digits = mapped.ToString();
        }

        return digits;
    }

    private static string ToAlphabetic(long number, char first)
    {
        var builder = new StringBuilder();
        while (number > 0)
        {
            number--;
            builder.Insert(0, (char)(first + (int)(number % 26)));
            number /= 26;
        }

        return builder.ToString();
    }

    private static string ToRoman(int number)
    {
        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] numerals = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                builder.Append(numerals[i]);
                number -= values[i];
            }
        }

        return builder.ToString();
    }

    // -----------------------------------------------------------------------------------------------------------------
    // format-number() (https://www.w3.org/TR/xslt-10/#format-number): the patterns of the JDK 1.1 DecimalFormat class
    // -----------------------------------------------------------------------------------------------------------------

    private sealed record NumberPattern(string Prefix, string Suffix, int MinimumIntegerDigits, int MinimumFractionDigits, int MaximumFractionDigits, int GroupingSize, int Multiplier);

    /// <summary>Formats a number with a pattern of format-number().</summary>
    public static string FormatNumber(double number, string pattern, XsltDecimalFormat format)
    {
        var subpatterns = SplitPattern(pattern, format.PatternSeparator);
        if (subpatterns.Count is 0 or > 2)
            throw new XsltException($"'{pattern}' is not a valid format-number pattern");

        var positive = ParsePattern(subpatterns[0], format) ?? throw new XsltException($"'{pattern}' is not a valid format-number pattern");
        NumberPattern? negative = null;
        if (subpatterns.Count == 2)
        {
            negative = ParsePattern(subpatterns[1], format) ?? throw new XsltException($"'{pattern}' is not a valid format-number pattern");
        }

        if (double.IsNaN(number))
            return format.NaN;

        var isNegative = number < 0;
        var prefix = positive.Prefix;
        var suffix = positive.Suffix;
        if (isNegative)
        {
            if (negative is not null)
            {
                prefix = negative.Prefix;
                suffix = negative.Suffix;
            }
            else
            {
                prefix = format.MinusSign + positive.Prefix;
            }
        }

        var value = Math.Abs(number) * positive.Multiplier;
        if (double.IsInfinity(value))
            return prefix + format.Infinity + suffix;

        // The digits, rounded half to even to the maximum number of fraction digits
        var maximumFraction = Math.Min(positive.MaximumFractionDigits, 28);
        string digits;
        if (value < 7.9e27 / Math.Pow(10, maximumFraction))
        {
            var rounded = Math.Round((decimal)value, maximumFraction, MidpointRounding.ToEven);
            digits = rounded.ToString("F" + maximumFraction.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
        else
        {
            digits = value.ToString("F" + maximumFraction.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        var point = digits.IndexOf('.', StringComparison.Ordinal);
        var integerPart = point < 0 ? digits : digits[..point];
        var fractionPart = point < 0 ? "" : digits[(point + 1)..];
        integerPart = integerPart.TrimStart('0');
        if (integerPart.Length < positive.MinimumIntegerDigits)
        {
            integerPart = new string('0', positive.MinimumIntegerDigits - integerPart.Length) + integerPart;
        }

        var fractionLength = fractionPart.Length;
        while (fractionLength > positive.MinimumFractionDigits && fractionPart[fractionLength - 1] == '0')
        {
            fractionLength--;
        }

        fractionPart = fractionPart[..fractionLength];
        if (integerPart.Length == 0 && fractionPart.Length == 0)
        {
            integerPart = "0";
        }

        var builder = new StringBuilder(prefix);
        for (var i = 0; i < integerPart.Length; i++)
        {
            if (i > 0 && positive.GroupingSize > 0 && (integerPart.Length - i) % positive.GroupingSize == 0)
            {
                builder.Append(format.GroupingSeparator);
            }

            builder.Append((char)(format.ZeroDigit + (integerPart[i] - '0')));
        }

        if (fractionPart.Length > 0)
        {
            builder.Append(format.DecimalSeparator);
        }

        foreach (var c in fractionPart)
        {
            builder.Append((char)(format.ZeroDigit + (c - '0')));
        }

        builder.Append(suffix);
        return builder.ToString();
    }

    /// <summary>Splits a pattern at the pattern separator that is not quoted.</summary>
    private static List<string> SplitPattern(string pattern, char separator)
    {
        var result = new List<string>();
        var start = 0;
        var quoted = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && pattern[i] == separator)
            {
                result.Add(pattern[start..i]);
                start = i + 1;
            }
        }

        result.Add(pattern[start..]);
        return result;
    }

    private static NumberPattern? ParsePattern(string pattern, XsltDecimalFormat format)
    {
        bool IsNumberChar(char c) => c == format.Digit || c == format.ZeroDigit || c == format.GroupingSeparator || c == format.DecimalSeparator;

        var multiplier = 1;
        var index = 0;
        var prefix = ReadAffix(pattern, ref index, format, ref multiplier, IsNumberChar);
        var numberStart = index;
        while (index < pattern.Length && IsNumberChar(pattern[index]))
        {
            index++;
        }

        var number = pattern[numberStart..index];
        var suffix = ReadAffix(pattern, ref index, format, ref multiplier, IsNumberChar);
        if (index < pattern.Length)
            return null;

        var decimalIndex = number.IndexOf(format.DecimalSeparator, StringComparison.Ordinal);
        if (decimalIndex >= 0 && number.AsSpan(decimalIndex + 1).Contains(format.DecimalSeparator))
            return null;

        var integer = decimalIndex < 0 ? number : number[..decimalIndex];
        var fraction = decimalIndex < 0 ? "" : number[(decimalIndex + 1)..];
        if (fraction.Contains(format.GroupingSeparator, StringComparison.Ordinal))
            return null;

        var minimumInteger = integer.Count(c => c == format.ZeroDigit);
        var minimumFraction = fraction.Count(c => c == format.ZeroDigit);
        var maximumFraction = fraction.Count(c => c == format.ZeroDigit || c == format.Digit);
        if (minimumInteger == 0 && maximumFraction == 0 && !integer.Contains(format.Digit, StringComparison.Ordinal))
            return null;

        var lastGrouping = integer.AsSpan().LastIndexOf(format.GroupingSeparator);
        var groupingSize = lastGrouping < 0 ? 0 : integer.Length - lastGrouping - 1;
        return new NumberPattern(prefix, suffix, minimumInteger, minimumFraction, maximumFraction, groupingSize, multiplier);
    }

    private static string ReadAffix(string pattern, ref int index, XsltDecimalFormat format, ref int multiplier, Func<char, bool> isNumberChar)
    {
        var builder = new StringBuilder();
        while (index < pattern.Length)
        {
            var c = pattern[index];
            if (c == '\'')
            {
                // A quoted literal; '' is a quote
                var end = pattern.AsSpan(index + 1).IndexOf('\'');
                if (end < 0)
                {
                    builder.Append(pattern, index + 1, pattern.Length - index - 1);
                    index = pattern.Length;
                    break;
                }

                end += index + 1;
                builder.Append(end == index + 1 ? "'" : pattern[(index + 1)..end]);
                index = end + 1;
                continue;
            }

            if (isNumberChar(c))
                break;

            if (c == format.Percent)
            {
                multiplier = 100;
            }
            else if (c == format.PerMille)
            {
                multiplier = 1000;
            }

            builder.Append(c);
            index++;
        }

        return builder.ToString();
    }
}
