using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Markdown.Parsing;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>A Markdown document: its blocks, and the end of the text.</summary>
public sealed class MarkdownDocumentSyntax : MarkdownSyntaxNode
{
    internal MarkdownDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownBlockSyntax>? _blocks;
    private Dictionary<string, MarkdownLinkReferenceDefinitionSyntax>? _definitions;
    private Dictionary<string, MarkdownAbbreviationDefinitionSyntax>? _abbreviations;
    private Dictionary<string, MarkdownFootnoteDefinitionSyntax>? _footnotes;

    /// <summary>Gets the blocks of the document, in the order of the text.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    /// <summary>Gets the token that ends the document, which holds the trivia after the last block.</summary>
    public SyntaxToken EndOfFileToken => GetToken(SyntaxKind.EndOfFileToken);

    /// <summary>Gets the link reference definitions of the document, in the order of the text.</summary>
    public IEnumerable<MarkdownLinkReferenceDefinitionSyntax> LinkReferenceDefinitions => DescendantNodes(IsBlockContainer).OfType<MarkdownLinkReferenceDefinitionSyntax>();

    // The nodes whose children are blocks, where definitions can be
    internal static bool IsBlockContainer(SyntaxNode node) => node is MarkdownDocumentSyntax or MarkdownBlockQuoteSyntax or MarkdownAlertSyntax or MarkdownListSyntax or MarkdownListItemSyntax or MarkdownFootnoteDefinitionSyntax;

    /// <summary>Finds the definition of a footnote: the first one with the same label, compared as CommonMark compares link labels.</summary>
    /// <param name="label">The label, with its <c>^</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public MarkdownFootnoteDefinitionSyntax? GetFootnoteDefinition(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (_footnotes is null)
        {
            var footnotes = new Dictionary<string, MarkdownFootnoteDefinitionSyntax>(StringComparer.Ordinal);
            foreach (var definition in Blocks.OfType<MarkdownFootnoteDefinitionSyntax>())
            {
                footnotes.TryAdd(Scanners.NormalizeLabel(definition.Label), definition);
            }

            _footnotes = footnotes;
        }

        return _footnotes.GetValueOrDefault(Scanners.NormalizeLabel(label));
    }

    /// <summary>Finds the definition of an abbreviation: the last one with the same label, compared as the text writes it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public MarkdownAbbreviationDefinitionSyntax? GetAbbreviationDefinition(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (_abbreviations is null)
        {
            var abbreviations = new Dictionary<string, MarkdownAbbreviationDefinitionSyntax>(StringComparer.Ordinal);
            foreach (var definition in DescendantNodes(IsBlockContainer).OfType<MarkdownAbbreviationDefinitionSyntax>())
            {
                abbreviations[definition.Label] = definition;
            }

            _abbreviations = abbreviations;
        }

        return _abbreviations.GetValueOrDefault(label);
    }

    /// <summary>Finds the definition of a link label: the first one with the same label, compared as CommonMark does.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    public MarkdownLinkReferenceDefinitionSyntax? GetLinkReferenceDefinition(string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (_definitions is null)
        {
            var definitions = new Dictionary<string, MarkdownLinkReferenceDefinitionSyntax>(StringComparer.Ordinal);
            foreach (var definition in LinkReferenceDefinitions)
            {
                definitions.TryAdd(Scanners.NormalizeLabel(definition.Label), definition);
            }

            _definitions = definitions;
        }

        return _definitions.GetValueOrDefault(Scanners.NormalizeLabel(label));
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownDocument(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownDocument(this);
    }
}
