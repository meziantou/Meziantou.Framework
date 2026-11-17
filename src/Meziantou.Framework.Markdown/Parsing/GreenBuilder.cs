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

    // The tokens of one ASCII character without trivia, such as line endings, brackets and delimiters, by kind then character:
    // the trees of every document share them
    private static readonly Green.SyntaxToken?[]?[] AsciiTokens = new Green.SyntaxToken?[]?[(int)SyntaxKind.Document];

    private readonly string _text;
    private readonly MarkdownSyntaxExtension[]? _syntaxExtensions;
    private readonly List<TriviaSpan> _triviaSpans;
    private readonly TreeBuffer _elements;
    private readonly GreenNodeStack _trivia = new();

    // The children of the nodes being built: the children of a node are at the end, from where the node started
    private readonly GreenNodeStack _children = new();

    // The recent tokens of a thread, by a hash of their kind and text: a token of the same kind and text reuses the string of
    // the cached one, and the cached token itself when it has no trivia and no value. Documents repeat many tokens, such as the
    // fences and the info strings of the code blocks, the lines of code, and the delimiters. The cache is lossy, as the string
    // table of Roslyn: a token replaces the one of the same hash, so that it costs a fixed array per thread, not an entry per
    // distinct text. The tokens are short, so that a thread keeps at most about 1 MB: the ones that repeat, such as fences,
    // lines of code and delimiters, are rarely longer.
    private const int TokenCacheSize = 4096;
    private const int MaximumCachedTokenLength = 64;

    [ThreadStatic]
    private static CachedToken[]? s_tokenCache;

    // The token before the one being built: where it starts and ends
    private bool _hasPrevious;
    private int _previousStart;
    private int _previousEnd;
    private int _triviaSpanIndex;

    private GreenBuilder(string text, TreeBuffer elements, List<TriviaSpan> triviaSpans, MarkdownSyntaxExtension[]? syntaxExtensions)
    {
        _text = text;
        _syntaxExtensions = syntaxExtensions;
        _triviaSpans = triviaSpans;
        _elements = elements;
    }

    /// <summary>Builds the tree of a document, the first element, which the other elements are the descendants of.</summary>
    public static GreenNode Build(string text, TreeBuffer elements, List<TriviaSpan> triviaSpans, MarkdownSyntaxExtension[]? syntaxExtensions)
    {
        var builder = new GreenBuilder(text, elements, triviaSpans, syntaxExtensions);
        var document = builder._elements[0];
        var end = 1 + document.Value;
        for (var index = 1; index < end;)
        {
            builder._children.Push(builder.BuildElement(ref index));
        }

        // The end of file token has the trivia after the last token
        builder._children.Push(new Green.SyntaxToken((int)SyntaxKind.EndOfFileToken, "", builder.CreateTrivia(builder.GetTriviaSplit(text.Length), text.Length), trailingTrivia: null, isMissing: false));
        return new MarkdownNode(SyntaxKind.Document, builder._children.From(0));
    }

    // Where the trivia between the previous token and a token that starts at nextStart is split: the trailing trivia of the
    // previous token goes up to the end of its line, unless the token ends the line itself (a line ending, or a line of code)
    private int GetTriviaSplit(int nextStart)
    {
        var start = _previousEnd;
        if (!_hasPrevious || (_previousEnd > _previousStart && _text[_previousEnd - 1] is '\r' or '\n'))
        {
            return start;
        }

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

    // Where the token after the element at index starts, or where the text ends
    private int GetNextTokenStart(int index)
    {
        for (var i = index + 1; i < _elements.Count; i++)
        {
            if (_elements[i].IsToken)
            {
                return _elements[i].Start;
            }
        }

        return _text.Length;
    }

    private GreenNode BuildElement(ref int index)
    {
        var element = _elements[index];
        if (element.IsToken)
        {
            if (element.Start < _previousEnd)
            {
                throw new InvalidOperationException($"The token {element.Kind} at {element.Start} starts before the end of the previous token, at {_previousEnd}.");
            }

            var leading = CreateTrivia(GetTriviaSplit(element.Start), element.Start);
            _hasPrevious = true;
            _previousStart = element.Start;
            _previousEnd = element.End;
            var nextStart = GetNextTokenStart(index);
            var trailing = CreateTrivia(element.End, GetTriviaSplit(nextStart));
            index++;

            // The tokens without trivia, such as line endings, brackets and delimiters, are shared: green nodes do not know their
            // position
            var length = element.End - element.Start;
            var shareable = leading is null && trailing is null && element.Value == 0;
            if (shareable && length == 1 && _text[element.Start] is var c && char.IsAscii(c))
            {
                var tokens = Volatile.Read(ref AsciiTokens[(int)element.Kind]);
                if (tokens is null)
                {
                    tokens = new Green.SyntaxToken?[128];
                    tokens = Interlocked.CompareExchange(ref AsciiTokens[(int)element.Kind], tokens, comparand: null) ?? tokens;
                }

                if (Volatile.Read(ref tokens[c]) is not { } token)
                {
                    token = new Green.SyntaxToken((int)element.Kind, AsciiStrings[c], leadingTrivia: null, trailingTrivia: null, isMissing: false);
                    token = Interlocked.CompareExchange(ref tokens[c], token, comparand: null) ?? token;
                }

                return token;
            }

            string text;
            if (length is > 1 and <= MaximumCachedTokenLength)
            {
                var span = _text.AsSpan(element.Start, length);
                ref var entry = ref GetCachedToken(element.Kind, span);
                if (entry.Text is not { } cachedText || entry.Kind != element.Kind || !span.SequenceEqual(cachedText))
                {
                    cachedText = span.ToString();
                    entry = new CachedToken(element.Kind, cachedText, Token: null);
                }

                if (shareable)
                {
                    return entry.Token ??= new Green.SyntaxToken((int)element.Kind, cachedText, leadingTrivia: null, trailingTrivia: null, isMissing: false);
                }

                text = cachedText;
            }
            else
            {
                text = GetString(element.Start, element.End);
            }

            // The first token of the construct of a syntax extension has the name of the extension, whose index the value is
            if (element.Value != 0 && element.Kind is SyntaxKind.ExtensionMarkerToken or SyntaxKind.ExtensionTextToken or SyntaxKind.ExtensionFenceToken)
            {
                return new Green.SyntaxTokenWithValue<string>((int)element.Kind, text, _syntaxExtensions![element.Value - 1].Name, text, leading, trailing, isMissing: false);
            }

            if (element.Value != 0)
            {
                return new Green.SyntaxTokenWithValue<int>((int)element.Kind, text, element.Value, text, leading, trailing, isMissing: false);
            }

            return new Green.SyntaxToken((int)element.Kind, text, leading, trailing, isMissing: false);
        }

        var end = index + 1 + element.Value;
        var first = _children.Count;
        index++;
        while (index < end)
        {
            _children.Push(BuildElement(ref index));
        }

        var node = new MarkdownNode(element.Kind, _children.From(first));
        _children.Truncate(first);
        return node;
    }

    private static ref CachedToken GetCachedToken(SyntaxKind kind, ReadOnlySpan<char> text)
    {
        // The hash reads a few characters, not the whole text: a collision only costs a miss, as the text is compared
        var cache = s_tokenCache ??= new CachedToken[TokenCacheSize];
        var hash = (uint)kind;
        hash = (hash * 31) + (uint)text.Length;
        hash = (hash * 31) + text[0];
        hash = (hash * 31) + text[1];
        hash = (hash * 31) + text[text.Length / 4];
        hash = (hash * 31) + text[text.Length / 2];
        hash = (hash * 31) + text[text.Length * 3 / 4];
        hash = (hash * 31) + text[^1];
        hash = (hash * 31) + text[^2];
        hash ^= hash >> 15;
        hash *= 0x2C1B3C6D;
        hash ^= hash >> 12;
        return ref cache[hash & (TokenCacheSize - 1)];
    }

    private string GetString(int start, int end)
    {
        if (end - start == 1 && _text[start] < AsciiStrings.Length)
        {
            return AsciiStrings[_text[start]];
        }

        return _text[start..end];
    }

    private GreenNode? CreateTrivia(int start, int end)
    {
        if (start >= end)
        {
            return null;
        }

        _trivia.Truncate(0);
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
                _trivia.Push(new Green.SyntaxTrivia((int)span.Kind, GetString(position, spanEnd)));
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
                    _trivia.Push(CarriageReturnLineFeed);
                }
                else
                {
                    _trivia.Push(c == '\n' ? LineFeed : new Green.SyntaxTrivia((int)SyntaxKind.EndOfLineTrivia, "\r"));
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

                _trivia.Push(onlySpaces && runEnd - position <= Spaces.Length ? Spaces[runEnd - position - 1] : new Green.SyntaxTrivia((int)SyntaxKind.WhitespaceTrivia, GetString(position, runEnd)));
            }
            else
            {
                // Text that no construct reads, which the parser should not leave: kept so that the tree has every character
                while (runEnd < nextSpanStart && _text[runEnd] is not (' ' or '\t' or '\v' or '\f' or '\r' or '\n'))
                {
                    runEnd++;
                }

                _trivia.Push(new Green.SyntaxTrivia((int)SyntaxKind.SkippedTextTrivia, GetString(position, runEnd)));
            }

            position = runEnd;
        }

        return Green.SyntaxList.List(_trivia.From(0));
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

    private record struct CachedToken(SyntaxKind Kind, string? Text, Green.SyntaxToken? Token);
}
