namespace Meziantou.Framework.Markdown;

/// <summary>Configures how <see cref="MarkdownFormatter"/> writes a document.</summary>
public sealed record MarkdownFormattingOptions
{
    /// <summary>Gets the options used when none are given.</summary>
    public static MarkdownFormattingOptions Default { get; } = new();

    /// <summary>
    /// Gets the line ending of the lines: <c>"\n"</c>, <c>"\r\n"</c> or <c>"\r"</c>, or <see langword="null"/>, the default,
    /// for the first line ending of the document, or <c>"\n"</c> when it has a single line.
    /// </summary>
    public string? NewLine { get; init; }

    /// <summary>
    /// Gets the marker of the items of the bullet lists: <c>-</c>, the default, <c>*</c> or <c>+</c>, or <see langword="null"/>
    /// to keep the marker of each list.
    /// </summary>
    /// <remarks>
    /// Two lists that follow each other are one list when their markers are the same, and a list item whose text is made of
    /// dashes is a thematic break: such a list gets another marker.
    /// </remarks>
    public char? BulletListMarker { get; init; } = '-';

    /// <summary>
    /// Gets a value indicating whether the items of an ordered list are numbered from the number of the first one, which is
    /// the only one that changes the HTML. The default is <see langword="true"/>. The letters and the roman numbers of
    /// <see cref="MarkdownExtensions.ListExtras"/> are kept.
    /// </summary>
    public bool RenumberOrderedLists { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the setext headings, which are underlined, are written as ATX headings, which start with
    /// <c>#</c>. The default is <see langword="true"/>. A setext heading of several lines stays a setext heading.
    /// </summary>
    public bool UseAtxHeadings { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the indented code blocks are written as fenced code blocks. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool UseFencedCodeBlocks { get; init; } = true;

    /// <summary>
    /// Gets the character of the fences of the code blocks: <c>`</c>, the default, or <c>~</c>. A code block whose info
    /// string has a backtick is fenced with <c>~</c>.
    /// </summary>
    public char CodeFenceCharacter { get; init; } = '`';

    /// <summary>
    /// Gets the character of the thematic breaks: <c>-</c>, the default, <c>*</c> or <c>_</c>. A thematic break that follows
    /// a block without a blank line is written with <c>*</c>, as <c>---</c> would make a paragraph before it a heading, and a
    /// thematic break that starts a list item is written with <c>_</c>, as <c>- ---</c> is a thematic break.
    /// </summary>
    public char ThematicBreakCharacter { get; init; } = '-';

    /// <summary>
    /// Gets a value indicating whether the cells of the tables are padded so that their pipes are aligned. The default is
    /// <see langword="true"/>. The pipe tables whose delimiter row gives the widths of the columns, with
    /// <see cref="MarkdownPipeTableOptions.InferColumnWidthsFromSeparator"/>, and the tables without a delimiter row are kept.
    /// </summary>
    public bool AlignTables { get; init; } = true;
}
