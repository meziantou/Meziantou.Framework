namespace Meziantou.Framework;

/// <summary>Provides Unicode helper methods.</summary>
public static partial class Unicode
{
    /// <summary>Replaces confusable Unicode characters using the Unicode confusables table.</summary>
    /// <param name="str">The text to normalize.</param>
    /// <returns>The text with confusable characters replaced.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string ReplaceConfusablesCharacters(string str)
    {
        ArgumentNullException.ThrowIfNull(str);

        if (str.Length == 0)
            return str;

        var sb = new StringBuilder(str.Length);
        var index = 0;
        var hasReplacement = false;

        while (index < str.Length)
        {
            if (!Rune.TryGetRuneAt(str, index, out var rune))
            {
                sb.Append(str[index]);
                index++;
                continue;
            }

            if (UnicodeConfusablesData.TryGetReplacement(rune, out var replacement))
            {
                sb.Append(replacement);
                hasReplacement = true;
            }
            else
            {
                sb.Append(rune.ToString());
            }

            index += rune.Utf16SequenceLength;
        }

        if (!hasReplacement)
            return str;

        return sb.ToString();
    }
}
