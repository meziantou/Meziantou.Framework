namespace Meziantou.Framework.Mermaid.Internals;

internal static class StringExtensions
{
    /// <summary>Returns the index of the first occurrence of a character from a position, or -1.</summary>
    public static int IndexOfOrdinal(this string text, char value, int startIndex)
    {
        var index = text.AsSpan(startIndex).IndexOf(value);
        return index < 0 ? -1 : startIndex + index;
    }
}
