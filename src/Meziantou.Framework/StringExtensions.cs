using System.Buffers;

namespace Meziantou.Framework;

#if PUBLIC_STRING_EXTENSIONS
public
#else
internal
#endif
static partial class StringExtensions
{
    public static string? Nullify(this string? str, bool trim)
    {
        if (str is null)
            return null;

        if (trim)
        {
            str = str.Trim();
        }

        if (string.IsNullOrEmpty(str))
            return null;

        return str;
    }

    public static bool EqualsOrdinal(this string? str1, string? str2)
    {
        return string.Equals(str1, str2, StringComparison.Ordinal);
    }

    public static bool EqualsIgnoreCase(this string? str1, string? str2)
    {
        return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsIgnoreCase(this string str, string value)
    {
        return str.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    public static string ReplaceAny(this string text, SearchValues<char> values, char newValue)
    {
        if (!text.ContainsAny(values))
            return text;

        return string.Create(text.Length, (text, values, newValue), static (span, state) =>
        {
            var source = state.text.AsSpan();
            source.CopyTo(span);

            var start = 0;
            while (start < source.Length)
            {
                var index = source[start..].IndexOfAny(state.values);
                if (index < 0)
                    break;

                start += index;
                span[start] = state.newValue;
                start++;
            }
        });
    }

    [return: NotNullIfNotNull(parameterName: nameof(str))]
    public static string? RemoveDiacritics(this string? str)
    {
        if (str is null)
            return null;

        var normalizedString = str.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        // InvariantMode doesn't support string normalization, so the decomposed characters are replaced using a table
        // generated from the Unicode Character Database (StringExtensions.g.cs)
        // https://source.dot.net/#System.Private.CoreLib/Normalization.cs,25
        var replaceDecomposableCharacters = GlobalizationMode.Invariant;
        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(replaceDecomposableCharacters ? RemoveDiacritic(c) : c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Removes the trailing occurrence of a specified string from the current string.</summary>
    public static string RemoveSuffix(this string str, string suffix)
    {
        return RemoveSuffix(str, suffix, StringComparison.Ordinal);
    }

    /// <summary>Removes the trailing occurrence of a specified string from the current string.</summary>
    public static string RemoveSuffix(this string str, string suffix, StringComparison stringComparison)
    {
        // Ordinal comparisons always match exactly suffix.Length characters
        if (stringComparison is StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase)
            return str.EndsWith(suffix, stringComparison) ? str[..^suffix.Length] : str;

        // A linguistic comparison can match a number of characters that differs from suffix.Length,
        // so the length of the match is what must be removed.
        var (compareInfo, options) = GetCompareInfo(stringComparison);

        // A suffix made only of ignorable characters matches everywhere and stands for nothing,
        // so there is nothing to remove. IsSuffix would report the whole string as the match.
        if (compareInfo.Compare(suffix, "", options) is 0)
            return str;

        return compareInfo.IsSuffix(str, suffix, options, out var matchLength) ? str[..^matchLength] : str;
    }

    /// <summary>Removes the leading occurrence of a specified string from the current string.</summary>
    public static string RemovePrefix(this string str, string prefix)
    {
        return RemovePrefix(str, prefix, StringComparison.Ordinal);
    }

    /// <summary>Removes the leading occurrence of a specified string from the current string.</summary>
    public static string RemovePrefix(this string str, string prefix, StringComparison stringComparison)
    {
        // Ordinal comparisons always match exactly prefix.Length characters
        if (stringComparison is StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase)
            return str.StartsWith(prefix, stringComparison) ? str[prefix.Length..] : str;

        // A linguistic comparison can match a number of characters that differs from prefix.Length,
        // so the length of the match is what must be removed.
        var (compareInfo, options) = GetCompareInfo(stringComparison);

        // A prefix made only of ignorable characters matches everywhere and stands for nothing,
        // so there is nothing to remove. IsPrefix would report the whole string as the match.
        if (compareInfo.Compare(prefix, "", options) is 0)
            return str;

        return compareInfo.IsPrefix(str, prefix, options, out var matchLength) ? str[matchLength..] : str;
    }

    private static (CompareInfo CompareInfo, CompareOptions Options) GetCompareInfo(StringComparison stringComparison)
    {
        return stringComparison switch
        {
            StringComparison.CurrentCulture => (CultureInfo.CurrentCulture.CompareInfo, CompareOptions.None),
            StringComparison.CurrentCultureIgnoreCase => (CultureInfo.CurrentCulture.CompareInfo, CompareOptions.IgnoreCase),
#pragma warning disable RS0030 // The banned invariant comparisons are mapped here, not chosen: the caller decides which comparison to use
            StringComparison.InvariantCulture => (CultureInfo.InvariantCulture.CompareInfo, CompareOptions.None),
            StringComparison.InvariantCultureIgnoreCase => (CultureInfo.InvariantCulture.CompareInfo, CompareOptions.IgnoreCase),
#pragma warning restore RS0030
            _ => throw new ArgumentException($"The string comparison type '{stringComparison}' is not supported.", nameof(stringComparison)),
        };
    }
}
