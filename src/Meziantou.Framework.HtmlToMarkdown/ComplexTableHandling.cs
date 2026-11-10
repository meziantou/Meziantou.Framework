namespace Meziantou.Framework;

/// <summary>Specifies how tables that a pipe table cannot represent are converted.</summary>
public enum ComplexTableHandling
{
    /// <summary>Write a pipe table: the blocks of the cells are flattened onto one line, and the spans are handled as set by <see cref="HtmlToMarkdownOptions.TableSpanHandling"/>.</summary>
    Flatten,

    /// <summary>
    /// Write the table as raw HTML when it has several header rows, a cell that holds a heading, a list, a code block,
    /// a block quote, a table, a horizontal rule or several paragraphs, or, when <see cref="HtmlToMarkdownOptions.TableSpanHandling"/>
    /// is <see cref="TableSpanHandling.Ignore"/>, a cell that spans several columns or rows.
    /// </summary>
    Html,
}
