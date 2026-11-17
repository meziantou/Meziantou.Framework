using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>A definition list: definitions, each with its terms.</summary>
public sealed class MarkdownDefinitionListSyntax : MarkdownBlockSyntax
{
    internal MarkdownDefinitionListSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownDefinitionItemSyntax>? _items;

    /// <summary>Gets the definitions of the list. A definition that follows another one has no terms.</summary>
    public IReadOnlyList<MarkdownDefinitionItemSyntax> Items => GetNodes(ref _items);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownDefinitionList(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownDefinitionList(this);
    }
}
