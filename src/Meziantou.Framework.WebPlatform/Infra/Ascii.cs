using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>
/// ASCII helpers as defined by the WHATWG Infra Standard. These never depend on the current culture or on ICU.
/// </summary>
internal static class InfraAscii
{
    /// <summary>ASCII whitespace is U+0009 TAB, U+000A LF, U+000C FF, U+000D CR, or U+0020 SPACE.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAsciiWhitespace(int c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char ToAsciiLower(char c) => char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char ToAsciiUpper(char c) => char.IsAsciiLetterLower(c) ? (char)(c & ~0x20) : c;

    /// <summary>Strips leading and trailing ASCII whitespace.</summary>
    public static ReadOnlySpan<char> StripLeadingAndTrailingAsciiWhitespace(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && IsAsciiWhitespace(value[start]))
        {
            start++;
        }

        var end = value.Length;
        while (end > start && IsAsciiWhitespace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    /// <summary>ASCII lowercases a string, returning the same instance when there is nothing to change.</summary>
    public static string ToAsciiLowercase(string value)
    {
        var index = value.AsSpan().IndexOfAnyInRange('A', 'Z');
        if (index < 0)
            return value;

        return string.Create(value.Length, (value, index), static (span, state) =>
        {
            state.value.AsSpan().CopyTo(span);
            for (var i = state.index; i < span.Length; i++)
            {
                span[i] = ToAsciiLower(span[i]);
            }
        });
    }

    /// <summary>ASCII uppercases a string, returning the same instance when there is nothing to change.</summary>
    public static string ToAsciiUppercase(string value)
    {
        var index = value.AsSpan().IndexOfAnyInRange('a', 'z');
        if (index < 0)
            return value;

        return string.Create(value.Length, (value, index), static (span, state) =>
        {
            state.value.AsSpan().CopyTo(span);
            for (var i = state.index; i < span.Length; i++)
            {
                span[i] = ToAsciiUpper(span[i]);
            }
        });
    }

    /// <summary>Compares two strings for equality, ASCII case-insensitively (the other characters are compared exactly).</summary>
    public static bool EqualsIgnoreAsciiCase(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        // System.Text.Ascii.EqualsIgnoreCase is vectorized, but it returns false as soon as a string contains a non-ASCII character
        if (System.Text.Ascii.EqualsIgnoreCase(left, right))
            return true;

        if (left.Length != right.Length || (System.Text.Ascii.IsValid(left) && System.Text.Ascii.IsValid(right)))
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i] && ToAsciiLower(left[i]) != ToAsciiLower(right[i]))
                return false;
        }

        return true;
    }
}
