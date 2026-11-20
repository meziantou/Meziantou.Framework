using System.Collections.Immutable;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>The definition of a footnote: <c>[^label]:</c> and its blocks.</summary>
public sealed class MarkdownFootnoteDefinitionSyntax : MarkdownBlockSyntax
{
    internal MarkdownFootnoteDefinitionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private ImmutableArray<MarkdownBlockSyntax> _blocks;

    /// <summary>Gets the label of the footnote, with its <c>^</c>.</summary>
    public string Label => GetToken(SyntaxKind.FootnoteLabelToken).Text;

    /// <summary>Gets the blocks of the footnote.</summary>
    public ImmutableArray<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownFootnoteDefinition(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownFootnoteDefinition(this);
    }
}
