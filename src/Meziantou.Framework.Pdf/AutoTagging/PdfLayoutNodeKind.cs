namespace Meziantou.Framework.Pdf;

/// <summary>What a node of the layout of a page is (<see cref="PdfLayoutNode.Kind"/>).</summary>
public enum PdfLayoutNodeKind
{
    /// <summary>A heading: a short block in a larger or bold font. <see cref="PdfLayoutNode.Level"/> is its level.</summary>
    Heading,

    /// <summary>A paragraph.</summary>
    Paragraph,

    /// <summary>A list: its children are its items.</summary>
    List,

    /// <summary>An item of a list: lines that start with a bullet or a number, and the lines that follow them.</summary>
    ListItem,

    /// <summary>A table whose cells are separated by rules: its children are its cells.</summary>
    Table,

    /// <summary>A cell of a table: <see cref="PdfLayoutNode.Row"/> and <see cref="PdfLayoutNode.Column"/> are its position.</summary>
    TableCell,

    /// <summary>An image.</summary>
    Figure,

    /// <summary>A header: text repeated at the same place at the top of the pages, such as a title.</summary>
    Header,

    /// <summary>A footer: text repeated at the same place at the bottom of the pages, such as a page number.</summary>
    Footer,
}
