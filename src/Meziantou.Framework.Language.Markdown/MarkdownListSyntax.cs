using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>A list: list items with the same kind of marker.</summary>
public sealed class MarkdownListSyntax : MarkdownBlockSyntax
{
    internal MarkdownListSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownListItemSyntax>? _items;

    // 0 when not computed yet, 1 when the list is tight, 2 when it is loose
    private int _tight;

    /// <summary>Gets the items of the list.</summary>
    public IReadOnlyList<MarkdownListItemSyntax> Items => GetNodes(ref _items);

    /// <summary>Gets a value indicating whether the items are numbered.</summary>
    public bool IsOrdered => FirstMarker is { Length: > 0 } marker && char.IsAsciiDigit(marker[0]);

    /// <summary>Gets the number of the first item of an ordered list, or <see langword="null"/> for a bullet list.</summary>
    public int? StartNumber => IsOrdered ? int.Parse(FirstMarker.AsSpan(0, FirstMarker.Length - 1), System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>Gets the character of the markers: <c>-</c>, <c>+</c> or <c>*</c> for a bullet list, <c>.</c> or <c>)</c> for an ordered list.</summary>
    public char MarkerCharacter => FirstMarker is { Length: > 0 } marker ? marker[^1] : '\0';

    /// <summary>
    /// Gets a value indicating whether the list is tight: no blank line separates two of its items, or two blocks of one
    /// of its items. The paragraphs of a tight list are rendered without <c>&lt;p&gt;</c>.
    /// </summary>
    public bool IsTight
    {
        get
        {
            if (_tight == 0)
            {
                _tight = ComputeIsTight() ? 1 : 2;
            }

            return _tight == 1;
        }
    }

    private string FirstMarker => Items.Count == 0 ? "" : Items[0].MarkerToken.Text;

    private bool ComputeIsTight()
    {
        var items = Items;
        if (HasBlankLineBetween(items))
        {
            return false;
        }

        foreach (var item in items)
        {
            if (HasBlankLineBetween(item.Blocks))
            {
                return false;
            }
        }

        return true;
    }

    // A line between the nodes that has nothing but the markers of the containers of the list is blank. The marker of a
    // block quote in the list is not: the line is a line of the quote.
    private bool HasBlankLineBetween<T>(IReadOnlyList<T> nodes)
        where T : MarkdownSyntaxNode
    {
        if (nodes.Count < 2)
        {
            return false;
        }

        var quoteDepth = Ancestors().Count(ancestor => ancestor is MarkdownBlockQuoteSyntax);
        for (var i = 1; i < nodes.Count; i++)
        {
            // The text between two siblings is trivia: the end of the line of the first one, unless its last token ends the line, such
            // as a line of code, and the lines before the second
            var last = nodes[i - 1].GetLastToken();
            var gap = (last.TrailingTrivia.ToFullString() + nodes[i].GetFirstToken().LeadingTrivia.ToFullString()).ReplaceLineEndings("\n").Split('\n');
            for (var j = last.Text is [.., '\n' or '\r'] ? 0 : 1; j < gap.Length - 1; j++)
            {
                if (gap[j].Count(c => c == '>') <= quoteDepth)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownList(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownList(this);
    }
}
