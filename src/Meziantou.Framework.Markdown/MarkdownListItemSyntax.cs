using System.Collections.Immutable;
using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>An item of a list: its marker, and its blocks.</summary>
public sealed class MarkdownListItemSyntax : MarkdownBlockSyntax
{
    internal MarkdownListItemSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private ImmutableArray<MarkdownBlockSyntax> _blocks;

    /// <summary>Gets the marker of the item: <c>-</c>, <c>+</c>, <c>*</c>, or a number followed by <c>.</c> or <c>)</c>.</summary>
    public SyntaxToken MarkerToken => GetToken(SyntaxKind.ListMarkerToken);

    /// <summary>Gets the <c>[ ]</c> or <c>[x]</c> of a task list item, or the default token for another item.</summary>
    public SyntaxToken TaskListItemMarkerToken => GetToken(SyntaxKind.TaskListItemMarkerToken);

    /// <summary>Gets a value indicating whether the item is a task list item of GitHub Flavored Markdown.</summary>
    public bool IsTask => TaskListItemMarkerToken.RawKind != 0;

    /// <summary>Gets a value indicating whether the item is a task list item marked done: <c>[x]</c> or <c>[X]</c>.</summary>
    public bool IsChecked => IsTask && TaskListItemMarkerToken.Text[1] != ' ';

    /// <summary>Gets the blocks of the item.</summary>
    public ImmutableArray<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownListItem(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownListItem(this);
    }
}
