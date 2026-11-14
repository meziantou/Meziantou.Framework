using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Markdown.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>
/// Builds the green tree from the nodes and tokens found by the parser. The characters between two tokens are trivia: the
/// ones up to the end of the line of the first token are its trailing trivia, and the others are the leading trivia of the
/// second token, as in Roslyn. Every character of the text is in a token or in trivia, so the tree reproduces the text.
/// </summary>
internal sealed class GreenBuilder
{
    private readonly string _text;
    private readonly List<TriviaSpan> _triviaSpans;
    private readonly List<BuildToken> _tokens = [];
    private (int Start, int End)[] _leading = [];
    private (int Start, int End)[] _trailing = [];
    private int _tokenIndex;
    private int _triviaSpanIndex;

    private GreenBuilder(string text, List<TriviaSpan> triviaSpans)
    {
        _text = text;
        _triviaSpans = triviaSpans;
    }

    public static GreenNode Build(string text, BuildNode document, List<TriviaSpan> triviaSpans)
    {
        var builder = new GreenBuilder(text, triviaSpans);
        builder.CollectTokens(document);
        builder.ComputeTrivia();

        var children = new List<GreenNode>(document.Children.Count + 1);
        foreach (var child in document.Children)
        {
            children.Add(builder.BuildElement(child));
        }

        // The end of file token has the trivia after the last token
        var index = builder._tokens.Count;
        children.Add(new Green.SyntaxToken((int)SyntaxKind.EndOfFileToken, "", builder.CreateTrivia(builder._leading[index]), trailingTrivia: null, isMissing: false));
        return new MarkdownNode(SyntaxKind.Document, [.. children]);
    }

    private void CollectTokens(BuildNode node)
    {
        foreach (var child in node.Children)
        {
            switch (child)
            {
                case BuildToken token:
                    var previousEnd = _tokens.Count == 0 ? 0 : _tokens[^1].End;
                    if (token.Start < previousEnd)
                    {
                        throw new InvalidOperationException($"The token {token.Kind} at {token.Start} starts before the end of the previous token, at {previousEnd}.");
                    }

                    _tokens.Add(token);
                    break;

                case BuildNode childNode:
                    CollectTokens(childNode);
                    break;
            }
        }
    }

    private void ComputeTrivia()
    {
        _leading = new (int, int)[_tokens.Count + 1];
        _trailing = new (int, int)[_tokens.Count];
        var position = 0;
        for (var i = 0; i <= _tokens.Count; i++)
        {
            var nextStart = i < _tokens.Count ? _tokens[i].Start : _text.Length;
            // The trailing trivia of the previous token goes up to the end of its line, unless the token ends the line itself (a
            // line ending, or a line of code)
            if (i > 0 && _tokens[i - 1] is var previous && (previous.End == previous.Start || _text[previous.End - 1] is not ('\r' or '\n')))
            {
                var trailingEnd = position;
                while (trailingEnd < nextStart && _text[trailingEnd] is not ('\r' or '\n'))
                {
                    trailingEnd++;
                }

                if (trailingEnd < nextStart)
                {
                    trailingEnd += _text[trailingEnd] == '\r' && trailingEnd + 1 < nextStart && _text[trailingEnd + 1] == '\n' ? 2 : 1;
                }

                _trailing[i - 1] = (position, trailingEnd);
                position = trailingEnd;
            }
            else if (i > 0)
            {
                _trailing[i - 1] = (position, position);
            }

            _leading[i] = (position, nextStart);
            if (i < _tokens.Count)
            {
                position = _tokens[i].End;
            }
        }
    }

    private GreenNode BuildElement(BuildElement element)
    {
        if (element is BuildToken token)
        {
            var index = _tokenIndex++;
            var leading = CreateTrivia(_leading[index]);
            var text = _text[token.Start..token.End];
            var trailing = CreateTrivia(_trailing[index]);
            if (token.Value != 0)
            {
                return new Green.SyntaxTokenWithValue<int>((int)token.Kind, text, token.Value, text, leading, trailing, isMissing: false);
            }

            return new Green.SyntaxToken((int)token.Kind, text, leading, trailing, isMissing: false);
        }

        var node = (BuildNode)element;
        var children = new GreenNode[node.Children.Count];
        for (var i = 0; i < children.Length; i++)
        {
            children[i] = BuildElement(node.Children[i]);
        }

        return new MarkdownNode(node.Kind, children);
    }

    private GreenNode? CreateTrivia((int Start, int End) range)
    {
        var (start, end) = range;
        if (start >= end)
        {
            return null;
        }

        var trivia = new List<GreenNode?>();
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
                trivia.Add(new Green.SyntaxTrivia((int)span.Kind, _text[position..spanEnd]));
                position = spanEnd;
                continue;
            }

            var nextSpanStart = _triviaSpanIndex < _triviaSpans.Count ? Math.Min(_triviaSpans[_triviaSpanIndex].Start, end) : end;
            var c = _text[position];
            var runEnd = position + 1;
            SyntaxKind kind;
            if (c is '\r' or '\n')
            {
                kind = SyntaxKind.EndOfLineTrivia;
                if (c == '\r' && runEnd < end && _text[runEnd] == '\n')
                {
                    runEnd++;
                }
            }
            else if (c is ' ' or '\t')
            {
                kind = SyntaxKind.WhitespaceTrivia;
                while (runEnd < nextSpanStart && _text[runEnd] is ' ' or '\t')
                {
                    runEnd++;
                }
            }
            else
            {
                // Text that no construct reads, which the parser should not leave: kept so that the tree has every character
                kind = SyntaxKind.SkippedTextTrivia;
                while (runEnd < nextSpanStart && _text[runEnd] is not (' ' or '\t' or '\r' or '\n'))
                {
                    runEnd++;
                }
            }

            trivia.Add(new Green.SyntaxTrivia((int)kind, _text[position..runEnd]));
            position = runEnd;
        }

        return Green.SyntaxList.List(trivia.ToArray());
    }
}
