namespace Meziantou.Framework.Markdown;

/// <summary>How <see cref="MarkdownExtensions.PipeTables"/> reads and renders the pipe tables, as the options of the pipe tables of Markdig.</summary>
public sealed record MarkdownPipeTableOptions
{
    /// <summary>Gets the options of Markdig.</summary>
    public static MarkdownPipeTableOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether a table needs a delimiter row, <see langword="true"/> by default. When it does not, the
    /// lines of a paragraph that have a pipe, up to its end, are a table without a header row, as in Kramdown.
    /// </summary>
    public bool RequireHeaderSeparator { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a table has as many columns as its first row has cells: the renderer adds empty cells
    /// to the shorter rows and does not write the cells of the longer ones. By default, a table has as many columns as its
    /// longest row has cells.
    /// </summary>
    public bool UseHeaderForColumnCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether the renderer writes the width of each column of the delimiter row, a <c>&lt;col&gt;</c>
    /// whose width is proportional to the number of dashes of the column.
    /// </summary>
    public bool InferColumnWidthsFromSeparator { get; init; }
}
