namespace Meziantou.Framework.Markdown;

/// <summary>
/// The footnotes of a document that are referred to, with their numbers, as Markdig numbers them: in the order of their first
/// reference, the references in the footnotes being read where the first footnote is defined. The references are numbered
/// footnote by footnote, for the links back from the footnotes to them.
/// </summary>
internal sealed class MarkdownFootnotes
{
    private readonly Dictionary<MarkdownFootnoteDefinitionSyntax, (int Number, List<MarkdownFootnoteReferenceSyntax> References)> _footnotes = [];
    private readonly Dictionary<MarkdownFootnoteReferenceSyntax, int> _referenceNumbers = [];

    public MarkdownFootnotes(MarkdownDocumentSyntax document)
    {
        var footnotesRead = false;
        foreach (var block in document.Blocks)
        {
            if (block is MarkdownFootnoteDefinitionSyntax)
            {
                if (!footnotesRead)
                {
                    foreach (var footnote in document.Blocks.OfType<MarkdownFootnoteDefinitionSyntax>())
                    {
                        AddReferences(footnote);
                    }

                    footnotesRead = true;
                }

                continue;
            }

            AddReferences(block);
        }

        Footnotes = [.. _footnotes.OrderBy(footnote => footnote.Value.Number).Select(footnote => footnote.Key)];
        var number = 0;
        foreach (var footnote in Footnotes)
        {
            foreach (var reference in _footnotes[footnote].References)
            {
                _referenceNumbers[reference] = ++number;
            }
        }
    }

    /// <summary>Gets the footnotes that are referred to, in the order of their numbers.</summary>
    public IReadOnlyList<MarkdownFootnoteDefinitionSyntax> Footnotes { get; }

    public int GetNumber(MarkdownFootnoteDefinitionSyntax footnote) => _footnotes[footnote].Number;

    public IReadOnlyList<MarkdownFootnoteReferenceSyntax> GetReferences(MarkdownFootnoteDefinitionSyntax footnote) => _footnotes[footnote].References;

    public int GetReferenceNumber(MarkdownFootnoteReferenceSyntax reference) => _referenceNumbers[reference];

    private void AddReferences(MarkdownSyntaxNode node)
    {
        foreach (var reference in node.DescendantNodes().OfType<MarkdownFootnoteReferenceSyntax>())
        {
            if (reference.Definition is not { } footnote)
            {
                continue;
            }

            if (!_footnotes.TryGetValue(footnote, out var entry))
            {
                entry = (_footnotes.Count + 1, []);
                _footnotes[footnote] = entry;
            }

            entry.References.Add(reference);
        }
    }
}
