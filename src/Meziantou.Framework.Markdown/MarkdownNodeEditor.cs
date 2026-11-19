using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Syntax.InternalSyntax;
using Green = Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// Builds the nodes that the <c>With</c> methods of the nodes return: a copy of a node whose children are changed, which is in no
/// tree. The children are green nodes, so that the tokens of the copy keep the trivia of the ones they replace.
/// </summary>
internal static class MarkdownNodeEditor
{
    public static List<GreenNode?> GetChildren(MarkdownSyntaxNode node)
    {
        var children = new List<GreenNode?>();
        foreach (var child in node.ChildNodesAndTokens())
        {
            children.Add(child.AsNode(out var childNode) ? childNode.Green : child.AsToken().Node);
        }

        return children;
    }

    public static TNode WithChildren<TNode>(TNode node, List<GreenNode?> children)
        where TNode : MarkdownSyntaxNode
        => WithChildren(node, node.Kind(), children);

    /// <summary>Gets a node of <paramref name="kind"/> with the children <paramref name="children"/> and the annotations of <paramref name="node"/>.</summary>
    public static TNode WithChildren<TNode>(TNode node, SyntaxKind kind, List<GreenNode?> children)
        where TNode : MarkdownSyntaxNode
    {
        GreenNode green = new MarkdownNode(kind, CollectionsMarshal.AsSpan(children));
        if (node.Green.GetAnnotations() is { Length: > 0 } annotations)
        {
            green = green.SetAnnotations(annotations);
        }

        return (TNode)green.CreateRed(parent: null, position: 0);
    }

    /// <summary>Gets the index of the first child token of <paramref name="kind"/>, or -1.</summary>
    public static int IndexOfToken(List<GreenNode?> children, SyntaxKind kind, int start = 0)
    {
        for (var i = start; i < children.Count; i++)
        {
            if (children[i] is { IsToken: true } token && token.RawKind == (int)kind)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Gets the index of the last child token of <paramref name="kind"/>, or -1.</summary>
    public static int LastIndexOfToken(List<GreenNode?> children, SyntaxKind kind)
    {
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i] is { IsToken: true } token && token.RawKind == (int)kind)
            {
                return i;
            }
        }

        return -1;
    }

    public static Green.SyntaxToken Token(SyntaxKind kind, string text, GreenNode? leadingTrivia, GreenNode? trailingTrivia)
        => new((int)kind, text, leadingTrivia, trailingTrivia, isMissing: false);

    /// <summary>Gets the same token with another text.</summary>
    public static Green.SyntaxToken WithText(GreenNode token, string text)
    {
        var greenToken = (Green.SyntaxToken)token;
        return Token((SyntaxKind)greenToken.RawKind, text, greenToken.LeadingTrivia, greenToken.TrailingTrivia);
    }

    /// <summary>Gets the trailing trivia of the last token of a child, a token or a node.</summary>
    public static GreenNode? GetTrailingTrivia(GreenNode child) => GetLastToken(child)?.TrailingTrivia;

    /// <summary>Gets a child, a token or a node, whose last token has the trailing trivia <paramref name="trailingTrivia"/>.</summary>
    public static GreenNode WithTrailingTrivia(GreenNode child, GreenNode? trailingTrivia)
    {
        if (child is Green.SyntaxToken token)
        {
            return token.WithTrivia(token.LeadingTrivia, trailingTrivia);
        }

        var node = child.CreateRed(parent: null, position: 0);
        var last = node.GetLastToken();
        if (last.Node is not Green.SyntaxToken lastToken)
        {
            return child;
        }

        var replacement = new SyntaxToken(parent: null, lastToken.WithTrivia(lastToken.LeadingTrivia, trailingTrivia), position: 0, index: 0);
        return node.ReplaceToken(last, replacement).Green;
    }

    /// <summary>
    /// Inserts <paramref name="inserted"/>, a token or a node, after the child at <paramref name="index"/>, on its line: the whitespace
    /// after the child separates them, a space when there is none and <paramref name="space"/> is <see langword="true"/>, and the
    /// rest of its trailing trivia, such as the line ending, goes after the inserted child.
    /// </summary>
    public static void InsertOnLineAfter(List<GreenNode?> children, int index, GreenNode inserted, bool space = true)
    {
        var previous = children[index]!;
        var (whitespace, rest) = SplitWhitespace(GetTrailingTrivia(previous));
        children[index] = WithTrailingTrivia(previous, whitespace ?? (space ? new Green.SyntaxTrivia((int)SyntaxKind.WhitespaceTrivia, " ") : null));
        children.Insert(index + 1, WithTrailingTrivia(inserted, rest));
    }

    /// <summary>
    /// Removes the children from <paramref name="start"/> to <paramref name="end"/>, and the whitespace that separates them from the
    /// child before them: this child gets the trailing trivia of the last removed child, such as the line ending.
    /// </summary>
    public static void RemoveOnLine(List<GreenNode?> children, int start, int end)
    {
        var trailing = GetTrailingTrivia(children[end]!);
        children.RemoveRange(start, end - start + 1);
        children[start - 1] = WithTrailingTrivia(children[start - 1]!, trailing);
    }

    /// <summary>
    /// Gets an ATX heading of <paramref name="level"/> with the inlines of a setext heading: an ATX heading has one line, so the line
    /// endings of the inlines become spaces, and the markers of the containers and the indentation of the next lines are removed. A
    /// closing sequence is added when the text ends with <c>#</c> that would be read as one.
    /// </summary>
    public static MarkdownHeadingSyntax ToAtxHeading(MarkdownHeadingSyntax heading, int level)
    {
        var underline = (Green.SyntaxToken)heading.UnderlineToken.Node!;
        var tokens = heading.DescendantTokens().Where(token => token.RawKind != (int)SyntaxKind.SetextHeadingUnderlineToken).ToList();
        var replacements = new Dictionary<SyntaxToken, Green.SyntaxToken>();
        var text = new System.Text.StringBuilder();
        var atLineStart = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var green = (Green.SyntaxToken)token.Node!;
            var leading = i == 0 ? null : JoinLines(green.LeadingTrivia, ref atLineStart, text);
            Green.SyntaxToken replacement;
            if (green.RawKind is (int)SyntaxKind.LineEndingToken or (int)SyntaxKind.HardLineBreakToken)
            {
                // The space is part of the title of a link or the label of a reference that the line ending is in, which are read from
                // the tokens of their kind. Other tokens, such as an escaped character, are not text that can contain a space.
                var siblings = token.Parent!.ChildNodesAndTokens().ToList();
                var index = siblings.IndexOf(token);
                var kind = index > 0 && index < siblings.Count - 1 && siblings[index - 1].RawKind is (int)SyntaxKind.LinkTitleToken or (int)SyntaxKind.LinkLabelToken && siblings[index + 1].RawKind == siblings[index - 1].RawKind
                    ? (SyntaxKind)siblings[index - 1].RawKind
                    : token.Parent switch
                    {
                        MarkdownCodeSpanSyntax => SyntaxKind.CodeTextToken,
                        MarkdownRawHtmlSyntax => SyntaxKind.HtmlTagToken,
                        MarkdownMathInlineSyntax => SyntaxKind.MathTextToken,
                        _ => SyntaxKind.TextToken,
                    };

                replacement = Token(kind, " ", leading, trailingTrivia: null);
                text.Append(' ');
                atLineStart = true;
            }
            else
            {
                replacement = green.WithTrivia(leading, trailingTrivia: null);
                text.Append(green.Text);
                atLineStart = false;
            }

            // The last token ends the line, with the line ending after the underline. The whitespace at the end of its text, which ends
            // the text of a setext heading, is not part of the text of an ATX heading: it is trivia.
            var trailing = i == tokens.Count - 1 ? underline.TrailingTrivia : JoinLines(green.TrailingTrivia, ref atLineStart, text);
            if (i == tokens.Count - 1 && replacement.RawKind == (int)SyntaxKind.TextToken && replacement.Text.TrimEnd(' ', '\t') is { Length: > 0 } trimmed && trimmed.Length < replacement.Text.Length)
            {
                trailing = Green.SyntaxList.Concat(Whitespace(replacement.Text[trimmed.Length..]), trailing);
                replacement = Token(SyntaxKind.TextToken, trimmed, replacement.LeadingTrivia, trailingTrivia: null);
            }

            replacements.Add(token, replacement.WithTrivia(replacement.LeadingTrivia, trailing));
        }

        var rewritten = heading.ReplaceTokens(tokens, (original, _) => new SyntaxToken(parent: null, replacements[original], position: 0, index: 0));
        var children = GetChildren(rewritten);
        children.RemoveAt(IndexOfToken(children, SyntaxKind.SetextHeadingUnderlineToken));

        // The text can end with # that an ATX heading reads as its closing sequence: a closing sequence after it keeps them
        var content = text.ToString().TrimEnd(' ', '\t');
        var sequenceStart = content.Length;
        while (sequenceStart > 0 && content[sequenceStart - 1] == '#')
        {
            sequenceStart--;
        }

        if (sequenceStart < content.Length && (sequenceStart == 0 || content[sequenceStart - 1] is ' ' or '\t'))
        {
            var last = children.Count - 1;
            var trailing = GetTrailingTrivia(children[last]!);
            children[last] = WithTrailingTrivia(children[last]!, Whitespace(" "));
            children.Add(Token(SyntaxKind.AtxHeadingClosingSequenceToken, "#", leadingTrivia: null, trailing));
        }

        var first = (Green.SyntaxToken)tokens[0].Node!;
        children.Insert(0, Token(SyntaxKind.AtxHeadingMarkerToken, new string('#', level), first.LeadingTrivia, Whitespace(" ")));
        return WithChildren(heading, SyntaxKind.AtxHeading, children);
    }

    private static Green.SyntaxTrivia Whitespace(string text) => new((int)SyntaxKind.WhitespaceTrivia, text);

    // The trivia on one line: a line ending becomes a space, and the trivia after it, the markers of the containers and the
    // indentation of the next line, is removed
    private static GreenNode? JoinLines(GreenNode? trivia, ref bool atLineStart, System.Text.StringBuilder text)
    {
        var items = new List<GreenNode?>();
        foreach (var item in GetTriviaItems(trivia))
        {
            if (atLineStart)
            {
                continue;
            }

            if (item.RawKind == (int)SyntaxKind.EndOfLineTrivia)
            {
                items.Add(Whitespace(" "));
                text.Append(' ');
                atLineStart = true;
            }
            else
            {
                items.Add(item);
                text.Append(((Green.SyntaxTrivia)item).Text);
            }
        }

        return Green.SyntaxList.List(CollectionsMarshal.AsSpan(items));
    }

    private static List<GreenNode> GetTriviaItems(GreenNode? trivia)
    {
        var items = new List<GreenNode>();
        if (trivia is { IsTrivia: true })
        {
            items.Add(trivia);
        }
        else if (trivia is not null)
        {
            for (var i = 0; i < trivia.SlotCount; i++)
            {
                if (trivia.GetSlot(i) is { } item)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    // The whitespace at the start of trivia, and the rest of it
    private static (GreenNode? Whitespace, GreenNode? Remaining) SplitWhitespace(GreenNode? trivia)
    {
        var items = new List<GreenNode?>();
        if (trivia is { IsTrivia: true })
        {
            items.Add(trivia);
        }
        else if (trivia is not null)
        {
            for (var i = 0; i < trivia.SlotCount; i++)
            {
                items.Add(trivia.GetSlot(i));
            }
        }

        var count = 0;
        while (count < items.Count && items[count]?.RawKind == (int)SyntaxKind.WhitespaceTrivia)
        {
            count++;
        }

        return (Green.SyntaxList.List(CollectionsMarshal.AsSpan(items)[..count]), Green.SyntaxList.List(CollectionsMarshal.AsSpan(items)[count..]));
    }

    private static Green.SyntaxToken? GetLastToken(GreenNode node)
    {
        if (node is Green.SyntaxToken token)
        {
            return token;
        }

        for (var i = node.SlotCount - 1; i >= 0; i--)
        {
            if (node.GetSlot(i) is { } slot && GetLastToken(slot) is { } last)
            {
                return last;
            }
        }

        return null;
    }
}
