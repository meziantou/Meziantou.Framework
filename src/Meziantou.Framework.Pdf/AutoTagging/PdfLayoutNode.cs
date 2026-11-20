namespace Meziantou.Framework.Pdf;

/// <summary>A node of the layout of a page (<see cref="PdfPage.ExtractLayout(PdfLayoutOptions?)"/>): a heading, a paragraph, a list, a table, a figure, a header or a footer, and its parts.</summary>
public sealed class PdfLayoutNode
{
    internal PdfLayoutNode(PdfLayoutNodeKind kind, PdfRectangle bounds, string text, IReadOnlyList<PdfLayoutNode> children)
    {
        Kind = kind;
        Bounds = bounds;
        Text = text;
        Children = children;
    }

    /// <summary>Gets what the node is.</summary>
    public PdfLayoutNodeKind Kind { get; }

    /// <summary>Gets the box of the node, in the default user space of the page.</summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Gets the text of the node, its lines separated by line feeds; empty for a figure.</summary>
    public string Text { get; }

    /// <summary>Gets the parts of the node: the items of a list, the cells of a table.</summary>
    public IReadOnlyList<PdfLayoutNode> Children { get; }

    /// <summary>Gets the level of a heading, from 1 for the largest font; 0 for the other nodes.</summary>
    public int Level { get; init; }

    /// <summary>Gets the zero-based row of a table cell, from the top; -1 for the other nodes.</summary>
    public int Row { get; init; } = -1;

    /// <summary>Gets the zero-based column of a table cell, from the left; -1 for the other nodes.</summary>
    public int Column { get; init; } = -1;

    /// <summary>Gets the image of a figure, or <see langword="null"/> for the other nodes.</summary>
    public PdfImageOccurrence? Image { get; init; }

    /// <summary>Gets the indexes of the operators of the content of the page the node is made of: its text operators, the <c>Do</c> of its image.</summary>
    internal HashSet<int> Operations { get; init; } = [];

    /// <inheritdoc/>
    public override string ToString() => Level > 0 ? $"{Kind} {Level}: {Text}" : $"{Kind}: {Text}";
}
