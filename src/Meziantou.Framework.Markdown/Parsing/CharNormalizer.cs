namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>Converts a character to ASCII for the identifiers of the headings, as Markdig does.</summary>
internal static partial class CharNormalizer
{
    /// <summary>Gets the ASCII characters of the compatibility decomposition (NFKD) of a character, such as <c>e</c> for <c>é</c>.</summary>
    /// <returns>The ASCII characters, or <see langword="null"/> when the character is ASCII or has none.</returns>
    public static string? ConvertToAscii(char c) => c >= 160 && CodeToAscii.TryGetValue(c, out var ascii) ? ascii : null;
}
