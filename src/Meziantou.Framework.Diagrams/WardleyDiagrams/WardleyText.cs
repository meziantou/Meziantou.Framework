namespace Meziantou.Framework.Diagrams;

/// <summary>The texts of the Wardley maps, trimmed as JavaScript does.</summary>
internal static class WardleyText
{
    /// <summary>Removes the white space and the line terminators that <c>String.prototype.trim</c> removes.</summary>
    public static string Trim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsJavaScriptWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && IsJavaScriptWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    /// <summary>Indicates whether JavaScript trims a character: the white space, the line terminators and the byte order mark.</summary>
    public static bool IsJavaScriptWhiteSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';
}
