namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Makes the human-readable text of a barcode safe to print on one line. A barcode can encode control characters (Code 128
/// and Code 93 encode every ASCII character): written as they are, a new line would add a line the layout did not
/// reserve, and an escape sequence would be run by a terminal.
/// </summary>
internal static class PrintableText
{
    /// <summary>Gets the text with each control character replaced by <c>?</c>.</summary>
    public static string Create(string text)
    {
        if (!text.AsSpan().ContainsAnyInRange('\0', '\u001f') && !text.AsSpan().ContainsAnyInRange('\u007f', '\u009f'))
            return text;

        var sb = new StringBuilder(text.Length);
        Append(sb, text);
        return sb.ToString();
    }

    /// <summary>Appends the text with each control character replaced by <c>?</c>.</summary>
    public static void Append(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            sb.Append(char.IsControl(c) ? '?' : c);
        }
    }
}
