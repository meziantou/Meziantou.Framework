namespace Meziantou.Framework.Markdown;

/// <summary>A column of a grid table.</summary>
public sealed class MarkdownGridTableColumn
{
    internal MarkdownGridTableColumn(MarkdownTableAlignment alignment, float width)
    {
        Alignment = alignment;
        Width = width;
    }

    /// <summary>Gets the alignment of the column, which the colons of its dashes give: <c>:--:</c>.</summary>
    public MarkdownTableAlignment Alignment { get; }

    /// <summary>Gets the width of the column, as a percentage of the width of the table, from the number of its dashes.</summary>
    public float Width { get; }
}
