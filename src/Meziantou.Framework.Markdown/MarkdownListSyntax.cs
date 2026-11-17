using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

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
    public bool IsOrdered => FirstMarker is { Length: > 0 } marker && marker[0] is not ('-' or '+' or '*');

    /// <summary>Gets how the items of an ordered list are numbered, or <see langword="null"/> for a bullet list.</summary>
    public MarkdownOrderedListStyle? OrderedStyle => IsOrdered ? (MarkdownOrderedListStyle)(Items[0].MarkerToken.Value is int style ? style : 0) : null;

    /// <summary>Gets the number of the first item of an ordered list, or <see langword="null"/> for a bullet list.</summary>
    /// <remarks>The letters and the roman numbers are numbers too: <c>c.</c> is 3, and <c>iv.</c> is 4.</remarks>
    public int? StartNumber
    {
        get
        {
            var marker = FirstMarker.AsSpan(0, Math.Max(FirstMarker.Length - 1, 0));
            return OrderedStyle switch
            {
                null => null,
                MarkdownOrderedListStyle.LowerAlpha or MarkdownOrderedListStyle.UpperAlpha => (marker[0] | 0x20) - 'a' + 1,
                MarkdownOrderedListStyle.LowerRoman or MarkdownOrderedListStyle.UpperRoman => Parsing.BlockParser.RomanToNumber(marker),
                _ => int.Parse(marker, System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }

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
        var quoteDepth = Ancestors().Count(ancestor => ancestor is MarkdownBlockQuoteSyntax);
        if (HasBlankLineBetween(items, quoteDepth))
        {
            return false;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (HasBlankLineBetween(item.Blocks, quoteDepth) || HasBlankLineInCustomContainers(item.Blocks, quoteDepth, lastIsExempt: i == items.Count - 1))
            {
                return false;
            }
        }

        return true;
    }

    // As in Markdig, a blank line in a custom container of an item, between its fences and its blocks, makes the list loose. Markdig
    // counts it after the container, at the level of the item, and ignores the blank lines at the end of the last item: the
    // blank lines of the last block of the last item do not count.
    private static bool HasBlankLineInCustomContainers(IReadOnlyList<MarkdownBlockSyntax> blocks, int quoteDepth, bool lastIsExempt)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index] is not MarkdownCustomContainerSyntax container)
            {
                continue;
            }

            var exempt = lastIsExempt && index == blocks.Count - 1;
            var children = container.Blocks;
            var openingLineEnd = container.OpeningFenceToken;
            foreach (var child in container.ChildNodesAndTokens())
            {
                if (child.AsNode() is MarkdownBlockSyntax)
                {
                    break;
                }

                openingLineEnd = child.IsToken ? child.AsToken() : child.AsNode()!.GetLastToken();
            }

            var closing = container.ClosingFenceToken;
            if (exempt)
            {
                continue;
            }

            if (children.Count == 0
                ? closing.RawKind != 0 && IsBlankLineBetween(openingLineEnd, closing, quoteDepth)
                : IsBlankLineBetween(openingLineEnd, children[0].GetFirstToken(), quoteDepth) || HasBlankLineBetween(children, quoteDepth) || (closing.RawKind != 0 && IsBlankLineBetween(children[^1].GetLastToken(), closing, quoteDepth)) || HasBlankLineInCustomContainers(children, quoteDepth, lastIsExempt: false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBlankLineBetween<T>(IReadOnlyList<T> nodes, int quoteDepth)
        where T : MarkdownSyntaxNode
    {
        for (var i = 1; i < nodes.Count; i++)
        {
            if (IsBlankLineBetween(nodes[i - 1].GetLastToken(), nodes[i].GetFirstToken(), quoteDepth))
            {
                return true;
            }
        }

        return false;
    }

    // A line between the tokens that has nothing but the markers of the containers of the list is blank. The marker of a block
    // quote in the list is not: the line is a line of the quote. The text between the tokens is trivia: the end of the line of the
    // first one, unless it ends the line, such as a line of code, and the lines before the second.
    private static bool IsBlankLineBetween(SyntaxToken last, SyntaxToken first, int quoteDepth)
    {
        var gap = (last.TrailingTrivia.ToFullString() + first.LeadingTrivia.ToFullString()).ReplaceLineEndings("\n").Split('\n');
        for (var j = last.Text is [.., '\n' or '\r'] ? 0 : 1; j < gap.Length - 1; j++)
        {
            if (gap[j].Count(c => c == '>') <= quoteDepth)
            {
                return true;
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
