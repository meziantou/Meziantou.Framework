namespace Meziantou.Framework.Csv;

/// <summary>Specifies which spaces <see cref="CsvReader"/> and <see cref="Utf8CsvReader"/> remove from the values they read.</summary>
/// <remarks>Only the space character (U+0020) is removed. Tabs and other white spaces are part of the values.</remarks>
[Flags]
public enum CsvTrimOptions
{
    /// <summary>The values are returned as they are in the data.</summary>
    None = 0,

    /// <summary>
    /// The spaces before and after a value are removed. A quoted value may be preceded and followed by spaces: <c> "a,b" </c> is the value <c>a,b</c>.
    /// </summary>
    Outer = 1,

    /// <summary>The spaces at the start and at the end of the content of a quoted value are removed: <c>" a "</c> is the value <c>a</c>.</summary>
    InsideQuotes = 2,

    /// <summary>Combines <see cref="Outer"/> and <see cref="InsideQuotes"/>.</summary>
    All = Outer | InsideQuotes,
}
