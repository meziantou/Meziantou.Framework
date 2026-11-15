using System.Buffers;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Syntax.InternalSyntax;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// Builds the green tree from the nodes and tokens found by the parser. The characters between two tokens are trivia: the
/// ones up to the end of the line of the first token are its trailing trivia, and the others are the leading trivia of the
/// second token, as in Roslyn. Every character of the text is in a token or in trivia, so the tree reproduces the text.
/// </summary>
internal sealed class GreenBuilder
{
    // The strings of one ASCII character, which most tokens and trivia are
    private static readonly string[] AsciiStrings = CreateAsciiStrings();

    // The most common trivia: green nodes have no position or parent, so trees share them, as Roslyn does
    private static readonly Green.SyntaxTrivia LineFeed = new((int)SyntaxKind.EndOfLineTrivia, "\n");
    private static readonly Green.SyntaxTrivia CarriageReturnLineFeed = new((int)SyntaxKind.EndOfLineTrivia, "\r\n");
    private static readonly Green.SyntaxTrivia[] Spaces = [.. Enumerable.Range(1, 8).Select(count => new Green.SyntaxTrivia((int)SyntaxKind.WhitespaceTrivia, new string(' ', count)))];

    private readonly string _text;
    private readonly List<TriviaSpan> _triviaSpans;
    // Where the tokens start and end, in the order of the text
    private readonly int[] _starts;
    private readonly int[] _ends;
    private int _tokenCount;
    private readonly List<GreenNode?> _trivia = [];
    private int _tokenIndex;
    private int _triviaSpanIndex;

    private GreenBuilder(string text, List<TriviaSpan> triviaSpans, int tokenCount)
    {
        _text = text;
        _triviaSpans = triviaSpans;
        _starts = new int[tokenCount];
        _ends = new int[tokenCount];
    }

    public static GreenNode Build(string text, BuildNode document, List<TriviaSpan> triviaSpans)
    {
        var builder = new GreenBuilder(text, triviaSpans, CountTokens(document));
        builder.CollectTokens(document);

        var children = new GreenNode?[document.Children.Count + 1];
        for (var i = 0; i < document.Children.Count; i++)
        {
            children[i] = builder.BuildElement(document.Children[i]);
        }

        // The end of file token has the trivia after the last token
        var index = builder._tokenCount;
        children[^1] = new Green.SyntaxToken((int)SyntaxKind.EndOfFileToken, "", builder.CreateTrivia(builder.GetLeadingTrivia(index)), trailingTrivia: null, isMissing: false);
        return new MarkdownNode(SyntaxKind.Document, children);
    }

    private static int CountTokens(BuildNode node)
    {
        var count = 0;
        foreach (var child in node.Children)
        {
            count += child.Node is { } childNode ? CountTokens(childNode) : 1;
        }

        return count;
    }

    private void CollectTokens(BuildNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Node is { } childNode)
            {
                CollectTokens(childNode);
                continue;
            }

            var previousEnd = _tokenCount == 0 ? 0 : _ends[_tokenCount - 1];
            if (child.Start < previousEnd)
            {
                throw new InvalidOperationException($"The token {child.Kind} at {child.Start} starts before the end of the previous token, at {previousEnd}.");
            }

            _starts[_tokenCount] = child.Start;
            _ends[_tokenCount] = child.End;
            _tokenCount++;
        }
    }

    // Where the trivia between the token before index and the token at index is split: the trailing trivia of the token
    // before goes up to the end of its line, unless the token ends the line itself (a line ending, or a line of code)
    private int GetTriviaSplit(int index)
    {
        var start = index == 0 ? 0 : _ends[index - 1];
        if (index == 0)
        {
            return start;
        }

        if (_ends[index - 1] > _starts[index - 1] && _text[_ends[index - 1] - 1] is '\r' or '\n')
        {
            return start;
        }

        var nextStart = index < _tokenCount ? _starts[index] : _text.Length;
        var split = start;
        while (split < nextStart && _text[split] is not ('\r' or '\n'))
        {
            split++;
        }

        if (split < nextStart)
        {
            split += _text[split] == '\r' && split + 1 < nextStart && _text[split + 1] == '\n' ? 2 : 1;
        }

        return split;
    }

    private (int Start, int End) GetLeadingTrivia(int index) => (GetTriviaSplit(index), index < _tokenCount ? _starts[index] : _text.Length);

    private (int Start, int End) GetTrailingTrivia(int index) => (_ends[index], GetTriviaSplit(index + 1));

    private GreenNode BuildElement(BuildElement element)
    {
        if (element.IsToken)
        {
            var token = element;
            var index = _tokenIndex++;
            var leading = CreateTrivia(GetLeadingTrivia(index));
            var text = GetString(token.Start, token.End);
            var trailing = CreateTrivia(GetTrailingTrivia(index));
            if (token.Value != 0)
            {
                return new Green.SyntaxTokenWithValue<int>((int)token.Kind, text, token.Value, text, leading, trailing, isMissing: false);
            }

            return new Green.SyntaxToken((int)token.Kind, text, leading, trailing, isMissing: false);
        }

        // The children are built in a rented buffer: the node copies them into its list
        var node = element.Node!;
        var count = node.Children.Count;
        var children = ArrayPool<GreenNode?>.Shared.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                children[i] = BuildElement(node.Children[i]);
            }

            return new MarkdownNode(node.Kind, children.AsSpan(0, count));
        }
        finally
        {
            children.AsSpan(0, count).Clear();
            ArrayPool<GreenNode?>.Shared.Return(children);
        }
    }

    private string GetString(int start, int end)
    {
        if (end - start == 1 && _text[start] < AsciiStrings.Length)
        {
            return AsciiStrings[_text[start]];
        }

        return _text[start..end];
    }

    private GreenNode? CreateTrivia((int Start, int End) range)
    {
        var (start, end) = range;
        if (start >= end)
        {
            return null;
        }

        _trivia.Clear();
        var position = start;
        while (position < end)
        {
            // The trivia spans of the block parser are in the order of the text, as are the ranges asked for
            while (_triviaSpanIndex < _triviaSpans.Count && _triviaSpans[_triviaSpanIndex].End <= position)
            {
                _triviaSpanIndex++;
            }

            if (_triviaSpanIndex < _triviaSpans.Count && _triviaSpans[_triviaSpanIndex] is var span && span.Start <= position)
            {
                var spanEnd = Math.Min(span.End, end);
                _trivia.Add(new Green.SyntaxTrivia((int)span.Kind, GetString(position, spanEnd)));
                position = spanEnd;
                continue;
            }

            var nextSpanStart = _triviaSpanIndex < _triviaSpans.Count ? Math.Min(_triviaSpans[_triviaSpanIndex].Start, end) : end;
            var c = _text[position];
            var runEnd = position + 1;
            if (c is '\r' or '\n')
            {
                if (c == '\r' && runEnd < end && _text[runEnd] == '\n')
                {
                    runEnd++;
                    _trivia.Add(CarriageReturnLineFeed);
                }
                else
                {
                    _trivia.Add(c == '\n' ? LineFeed : new Green.SyntaxTrivia((int)SyntaxKind.EndOfLineTrivia, "\r"));
                }
            }
            else if (c is ' ' or '\t' or '\v' or '\f')
            {
                // A vertical tab or a form feed is whitespace around the cells of a table
                var onlySpaces = c == ' ';
                while (runEnd < nextSpanStart && _text[runEnd] is ' ' or '\t' or '\v' or '\f')
                {
                    onlySpaces &= _text[runEnd] == ' ';
                    runEnd++;
                }

                _trivia.Add(onlySpaces && runEnd - position <= Spaces.Length ? Spaces[runEnd - position - 1] : new Green.SyntaxTrivia((int)SyntaxKind.WhitespaceTrivia, GetString(position, runEnd)));
            }
            else
            {
                // Text that no construct reads, which the parser should not leave: kept so that the tree has every character
                while (runEnd < nextSpanStart && _text[runEnd] is not (' ' or '\t' or '\v' or '\f' or '\r' or '\n'))
                {
                    runEnd++;
                }

                _trivia.Add(new Green.SyntaxTrivia((int)SyntaxKind.SkippedTextTrivia, GetString(position, runEnd)));
            }

            position = runEnd;
        }

        return Green.SyntaxList.List(CollectionsMarshal.AsSpan(_trivia));
    }

    private static string[] CreateAsciiStrings()
    {
        var strings = new string[128];
        for (var i = 0; i < strings.Length; i++)
        {
            strings[i] = ((char)i).ToString();
        }

        return strings;
    }
}
