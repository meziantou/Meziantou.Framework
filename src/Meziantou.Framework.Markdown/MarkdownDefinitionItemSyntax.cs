using System.Collections.Immutable;
using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A definition of a definition list: its terms, the lines of the paragraph before the marker, then its marker, <c>:</c> or <c>~</c>, and its blocks.</summary>
public sealed class MarkdownDefinitionItemSyntax : MarkdownBlockSyntax
{
    internal MarkdownDefinitionItemSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private ImmutableArray<MarkdownDefinitionTermSyntax> _terms;
    private ImmutableArray<MarkdownBlockSyntax> _blocks;

    /// <summary>Gets the terms of the definition, one per line, or none when the definition follows another one.</summary>
    public ImmutableArray<MarkdownDefinitionTermSyntax> Terms => GetNodes(ref _terms);

    /// <summary>Gets the marker of the definition: <c>:</c> or <c>~</c>.</summary>
    public SyntaxToken MarkerToken => GetToken(SyntaxKind.DefinitionMarkerToken);

    /// <summary>Gets the blocks of the definition, without its terms.</summary>
    public ImmutableArray<MarkdownBlockSyntax> Blocks
    {
        get
        {
            if (_blocks.IsDefault)
            {
                _blocks = [.. ChildNodes().OfType<MarkdownBlockSyntax>().Where(block => block is not MarkdownDefinitionTermSyntax)];
            }

            return _blocks;
        }
    }

    // The blank lines that Markdig leaves in the definition, which decide whether its paragraph is written in <p>
    internal int BlankLineCount => MarkerToken.Value is int count ? count : 0;

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownDefinitionItem(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownDefinitionItem(this);
    }
}
