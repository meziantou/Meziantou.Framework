namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>A pipe table found in the inlines of a paragraph: the rows, as positions of its subject.</summary>
internal sealed class PipeTableScan
{
    /// <summary>Gets or sets where the lines before the table end, which are a paragraph when they are not whitespace.</summary>
    public int LeadingEnd { get; set; }

    /// <summary>
    /// Gets or sets the first line break that is in a delimiter of emphasis or a bracket when the table has delimiters in one, or
    /// -1: Markdig moves the inlines after it out of the delimiter, and they are not emphasis.
    /// </summary>
    public int SplitLineBreak { get; set; } = -1;

    /// <summary>Gets or sets the quotes of SmartyPants, by their position, and whether they are paired, or <see langword="null"/> when there are none.</summary>
    public IReadOnlyDictionary<int, bool>? Quotes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the table has a header row and a delimiter row, which a table that
    /// <see cref="MarkdownPipeTableOptions.RequireHeaderSeparator"/> allows has not.
    /// </summary>
    public bool HasDelimiterRow { get; set; } = true;

    /// <summary>Gets the rows: the header row, the delimiter row, then the other rows, or only the other rows when the table has no delimiter row.</summary>
    public List<PipeTableScanRow> Rows { get; } = [];

    /// <summary>Gets or sets where the lines after the table start, which are a paragraph, or -1 when the table ends the paragraph.</summary>
    public int TrailingStart { get; set; } = -1;
}
