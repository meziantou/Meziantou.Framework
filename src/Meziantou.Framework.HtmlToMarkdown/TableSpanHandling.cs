namespace Meziantou.Framework;

/// <summary>Specifies how the <c>colspan</c> and <c>rowspan</c> attributes of table cells are handled in pipe tables.</summary>
public enum TableSpanHandling
{
    /// <summary>Ignore the spans: each cell takes one column of its row.</summary>
    Ignore,

    /// <summary>Write the content in the first column and the first row of the span, and empty cells in the other positions.</summary>
    EmptyCells,

    /// <summary>
    /// Write the content of the cell in every position it spans. The spans copy at most 1,000,000 characters in a document:
    /// the other positions of a span that would copy more are empty, as with <see cref="EmptyCells"/>.
    /// </summary>
    Duplicate,
}
