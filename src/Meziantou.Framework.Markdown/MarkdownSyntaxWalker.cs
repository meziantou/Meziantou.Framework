using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Markdown;

/// <summary>Visits a Markdown node and everything below it.</summary>
public class MarkdownSyntaxWalker : MarkdownSyntaxVisitor
{
    private int _recursionDepth;

    public MarkdownSyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <param name="depth">How far down the walker goes.</param>
    /// <param name="visitGridTableCells">
    /// Whether the walker visits the content of the cells of the grid tables, which is not part of the tree: after the children of
    /// a grid table, the document of each of its cells, row by row.
    /// </param>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownSyntaxWalker_GridTableCells" ref:dedent="true">
    /// var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
    /// var tree = MarkdownSyntaxTree.ParseText("[a](/u)\n\n+---------+---------+\n| [b](/v) | [c](/w) |\n+---------+---------+\n", options);
    ///
    /// var collector = new LinkCollector();
    /// collector.Visit(tree.GetRoot());
    ///
    /// Assert.Equal(["/u", "/v", "/w"], collector.Links.Select(link =&gt; link.Destination));
    /// </code>
    /// </example>
    public MarkdownSyntaxWalker(SyntaxWalkerDepth depth, bool visitGridTableCells)
    {
        Depth = depth;
        VisitsGridTableCells = visitGridTableCells;
    }

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <summary>
    /// Gets a value indicating whether this walker visits the content of the cells of the grid tables: the document of each
    /// cell, whose positions are in the text of the cell (see <see cref="MarkdownGridTableCell.MapSpan(TextSpan)"/>).
    /// </summary>
    protected bool VisitsGridTableCells { get; }

    /// <exception cref="InsufficientExecutionStackException">The document is nested too deeply to walk.</exception>
    public override void DefaultVisit(MarkdownSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode(out var childNode))
            {
                Visit((MarkdownSyntaxNode)childNode);
            }
            else if (Depth >= SyntaxWalkerDepth.Token)
            {
                VisitToken(child.AsToken());
            }
        }

        if (VisitsGridTableCells && node is MarkdownGridTableSyntax table)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    Visit(cell.Content.GetRoot());
                }
            }
        }

        _recursionDepth--;
    }

    public virtual void VisitToken(SyntaxToken token)
    {
        if (Depth < SyntaxWalkerDepth.Trivia)
        {
            return;
        }

        foreach (var trivia in token.LeadingTrivia)
        {
            VisitTrivia(trivia);
        }

        foreach (var trivia in token.TrailingTrivia)
        {
            VisitTrivia(trivia);
        }
    }

    public virtual void VisitTrivia(SyntaxTrivia trivia)
    {
    }
}
