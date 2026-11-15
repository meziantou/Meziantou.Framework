namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The content of a cell of a grid table: the labels of the link reference definitions of the document of the table, and
/// whether the grid table that ends the content is kept, as Markdig keeps it when it does not close the cell.
/// </summary>
internal sealed record CellContext(IEnumerable<string> References, bool KeepsGridTable);
