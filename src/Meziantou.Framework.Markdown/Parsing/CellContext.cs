namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// The content of a cell of a grid table: the labels of the link reference definitions and of the footnotes of the document
/// of the table and of the documents around it, whether the grid table that ends the content is kept, as Markdig keeps it when it does not close the cell, and the number
/// of blocks that the content is in, the table and its containers in every enclosing document, which count in the nesting
/// depth.
/// </summary>
internal sealed record CellContext(IEnumerable<string> References, IEnumerable<string> Footnotes, bool KeepsGridTable, int Depth);
