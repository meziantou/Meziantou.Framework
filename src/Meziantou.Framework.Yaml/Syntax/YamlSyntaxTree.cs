using System.Runtime.InteropServices;
using Meziantou.Framework.Yaml.Tokens;

namespace Meziantou.Framework.Yaml.Syntax;

/// <summary>Represents a parsed YAML syntax tree.</summary>
public sealed class YamlSyntaxTree
{
    private sealed class RootSyntaxNode : YamlSyntaxNode
    {
        public RootSyntaxNode(YamlSourceSpan span, YamlSourceSpan fullSpan)
            : base(span, fullSpan)
        {
        }
    }

    private YamlSyntaxTree(string text, YamlSyntaxNode root, IReadOnlyList<YamlSyntaxToken> tokens)
    {
        Text = text;
        Root = root;
        Tokens = tokens;
    }

    /// <summary>Gets the original source text.</summary>
    public string Text { get; }

    /// <summary>Gets the root syntax node.</summary>
    public YamlSyntaxNode Root { get; }

    /// <summary>Gets parsed syntax tokens.</summary>
    public IReadOnlyList<YamlSyntaxToken> Tokens { get; }

    /// <summary>Parses YAML text into a syntax tree.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="options">The syntax options.</param>
    /// <returns>A parsed syntax tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="YamlException">The YAML content is invalid.</exception>
    public static YamlSyntaxTree Parse(string yaml, YamlSyntaxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        options ??= new YamlSyntaxOptions();

        var scannerTokens = ValidateYaml(yaml);

        var tokens = new List<YamlSyntaxToken>(scannerTokens.Count);
        AddScannerTokens(yaml, scannerTokens, tokens);
        if (options.IncludeTrivia)
        {
            // Trivia is read from the text the scanner did not claim: a '#' inside a quoted or block scalar is
            // content, not the start of a comment.
            var trivia = new List<YamlSyntaxToken>();
            AddTriviaTokens(yaml, trivia, GetUncoveredRanges(yaml, tokens));
            tokens = MergeInSourceOrder(tokens, trivia);
        }
        else if (!IsInSourceOrder(tokens))
        {
            tokens = SortInSourceOrder(tokens);
        }

        var endMark = CreateMark(yaml, yaml.Length);
        var fullSpan = new YamlSourceSpan(new Mark(0, 0, 0), endMark);
        var span = BuildNodeSpan(tokens, fullSpan);
        return new YamlSyntaxTree(yaml, new RootSyntaxNode(span, fullSpan), tokens);
    }

    /// <summary>Converts this syntax tree back to full text.</summary>
    /// <returns>The full original text.</returns>
    public string ToFullString()
    {
        return Text;
    }

    /// <summary>Writes the full original text to a writer.</summary>
    /// <param name="writer">The destination writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Text);
    }

    /// <summary>Parses <paramref name="yaml"/> to check that it is valid, and returns the tokens the scanner produced.</summary>
    /// <remarks>
    /// The parser reads every token of a valid stream, in the order a scanner alone produces them, so the tokens do
    /// not need to be scanned a second time.
    /// </remarks>
    private static List<Token> ValidateYaml(string yaml)
    {
        var parser = new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml));
        var tokens = new List<Token>();
        parser.Scanner.ConsumedTokens = tokens;
        while (parser.MoveNext())
        {
        }

        return tokens;
    }

    /// <summary>
    /// Returns the scanner tokens and the trivia tokens ordered by start, then by end. The order is stable, and the
    /// scanner tokens come before the trivia tokens that have the same position.
    /// </summary>
    /// <remarks>
    /// Each list is usually in source order already, so merging them gives the same result as sorting their
    /// concatenation, without the cost of a sort.
    /// </remarks>
    private static List<YamlSyntaxToken> MergeInSourceOrder(List<YamlSyntaxToken> scannerTokens, List<YamlSyntaxToken> triviaTokens)
    {
        if (!IsInSourceOrder(scannerTokens) || !IsInSourceOrder(triviaTokens))
        {
            scannerTokens.AddRange(triviaTokens);
            return SortInSourceOrder(scannerTokens);
        }

        var result = new List<YamlSyntaxToken>(scannerTokens.Count + triviaTokens.Count);
        var scannerIndex = 0;
        var triviaIndex = 0;
        while (scannerIndex < scannerTokens.Count && triviaIndex < triviaTokens.Count)
        {
            // On a tie, the scanner token comes first, as it does in the stable sort of the concatenation.
            if (CompareSourcePosition(triviaTokens[triviaIndex], scannerTokens[scannerIndex]) < 0)
            {
                result.Add(triviaTokens[triviaIndex++]);
            }
            else
            {
                result.Add(scannerTokens[scannerIndex++]);
            }
        }

        for (; scannerIndex < scannerTokens.Count; scannerIndex++)
        {
            result.Add(scannerTokens[scannerIndex]);
        }

        for (; triviaIndex < triviaTokens.Count; triviaIndex++)
        {
            result.Add(triviaTokens[triviaIndex]);
        }

        return result;
    }

    private static bool IsInSourceOrder(List<YamlSyntaxToken> tokens)
    {
        for (var index = 1; index < tokens.Count; index++)
        {
            if (CompareSourcePosition(tokens[index - 1], tokens[index]) > 0)
                return false;
        }

        return true;
    }

    private static int CompareSourcePosition(YamlSyntaxToken left, YamlSyntaxToken right)
    {
        var result = left.Span.Start.Index.CompareTo(right.Span.Start.Index);
        return result != 0 ? result : left.Span.End.Index.CompareTo(right.Span.End.Index);
    }

    /// <remarks>
    /// The sort must be stable: the scanner emits several empty tokens at the same position (KEY and
    /// BLOCK-MAPPING-START, BLOCK-END and STREAM-END), and their order must not depend on the trivia around them.
    /// </remarks>
    private static List<YamlSyntaxToken> SortInSourceOrder(List<YamlSyntaxToken> tokens)
    {
        return [.. tokens.OrderBy(token => token.Span.Start.Index).ThenBy(token => token.Span.End.Index)];
    }

    private static YamlSourceSpan BuildNodeSpan(List<YamlSyntaxToken> tokens, YamlSourceSpan fallback)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Kind is YamlSyntaxKind.WhitespaceTrivia or YamlSyntaxKind.NewLineTrivia or YamlSyntaxKind.CommentTrivia)
            {
                continue;
            }

            var start = tokens[index].Span.Start;
            var end = tokens[index].Span.End;
            for (var tail = tokens.Count - 1; tail >= index; tail--)
            {
                if (tokens[tail].Kind is YamlSyntaxKind.WhitespaceTrivia or YamlSyntaxKind.NewLineTrivia or YamlSyntaxKind.CommentTrivia)
                {
                    continue;
                }

                end = tokens[tail].Span.End;
                break;
            }

            return new YamlSourceSpan(start, end);
        }

        return fallback;
    }

    private static void AddScannerTokens(string yaml, List<Token> scannerTokens, List<YamlSyntaxToken> target)
    {
        foreach (var token in scannerTokens)
        {
            var start = token.Start;
            var end = token.End;
            var safeStart = Clamp(start.Index, 0, yaml.Length);
            var safeEnd = Clamp(end.Index, safeStart, yaml.Length);
            var text = safeEnd > safeStart ? yaml.Substring(safeStart, safeEnd - safeStart) : string.Empty;
            target.Add(new YamlSyntaxToken(
                MapTokenKind(token),
                new YamlSourceSpan(start, end),
                text));
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>Gets the ranges of <paramref name="yaml"/> that no scanner token covers, in source order.</summary>
    private static List<(int Start, int End)> GetUncoveredRanges(string yaml, List<YamlSyntaxToken> tokens)
    {
        var covered = new List<(int Start, int End)>(tokens.Count);
        foreach (var token in tokens)
        {
            var start = Clamp(token.Span.Start.Index, 0, yaml.Length);
            var end = Clamp(token.Span.End.Index, start, yaml.Length);
            if (end > start)
            {
                covered.Add((start, end));
            }
        }

        covered.Sort(static (left, right) => left.Start.CompareTo(right.Start));

        var result = new List<(int Start, int End)>();
        var cursor = 0;
        foreach (var (start, end) in covered)
        {
            if (start > cursor)
            {
                result.Add((cursor, start));
            }

            if (end > cursor)
            {
                cursor = end;
            }
        }

        if (cursor < yaml.Length)
        {
            result.Add((cursor, yaml.Length));
        }

        return result;
    }

    private static void AddTriviaTokens(string yaml, List<YamlSyntaxToken> target, List<(int Start, int End)> ranges)
    {
        // The ranges are in source order, so the position of each one is computed from the previous one. Computing it
        // from the start of the text for every range would make parsing quadratic in the size of the document.
        var cursor = new MarkCursor();
        foreach (var range in ranges)
        {
            AddTriviaTokens(yaml, target, range.Start, range.End, cursor.MoveTo(yaml, range.Start));
        }
    }

    private static void AddTriviaTokens(string yaml, List<YamlSyntaxToken> target, int rangeStart, int rangeEnd, Mark startMark)
    {
        var index = rangeStart;
        var line = startMark.Line;
        var column = startMark.Column;

        while (index < rangeEnd)
        {
            var start = new Mark(index, line, column);
            var current = yaml[index];
            if (current == '#')
            {
                var cursor = index + 1;
                var endLine = line;
                var endColumn = column + 1;
                while (cursor < rangeEnd && yaml[cursor] != '\r' && yaml[cursor] != '\n')
                {
                    cursor++;
                    endColumn++;
                }

                var end = new Mark(cursor, endLine, endColumn);
                target.Add(new YamlSyntaxToken(
                    YamlSyntaxKind.CommentTrivia,
                    new YamlSourceSpan(start, end),
                    yaml.Substring(index, cursor - index)));

                index = cursor;
                line = endLine;
                column = endColumn;
                continue;
            }

            if (current == '\r' || current == '\n')
            {
                var cursor = index;
                if (current == '\r' && cursor + 1 < rangeEnd && yaml[cursor + 1] == '\n')
                {
                    cursor += 2;
                }
                else
                {
                    cursor++;
                }

                var end = new Mark(cursor, line + 1, 0);
                target.Add(new YamlSyntaxToken(
                    YamlSyntaxKind.NewLineTrivia,
                    new YamlSourceSpan(start, end),
                    yaml.Substring(index, cursor - index)));

                index = cursor;
                line++;
                column = 0;
                continue;
            }

            if (current == ' ' || current == '\t')
            {
                var cursor = index + 1;
                var endColumn = column + 1;
                while (cursor < rangeEnd && (yaml[cursor] == ' ' || yaml[cursor] == '\t'))
                {
                    cursor++;
                    endColumn++;
                }

                var end = new Mark(cursor, line, endColumn);
                target.Add(new YamlSyntaxToken(
                    YamlSyntaxKind.WhitespaceTrivia,
                    new YamlSourceSpan(start, end),
                    yaml.Substring(index, cursor - index)));

                index = cursor;
                column = endColumn;
                continue;
            }

            index++;
            column++;
        }
    }

    private static Mark CreateMark(string yaml, int index)
    {
        return new MarkCursor().MoveTo(yaml, index);
    }

    /// <summary>Tracks the line and column of a position that only moves forward.</summary>
    /// <remarks>A <c>\r\n</c> pair is a single line break, and a lone <c>\r</c> or <c>\n</c> is one too.</remarks>
    [StructLayout(LayoutKind.Auto)]
    private struct MarkCursor
    {
        private int _index;
        private int _line;
        private int _column;

        public Mark MoveTo(string yaml, int index)
        {
            while (_index < index && _index < yaml.Length)
            {
                var ch = yaml[_index];
                if (ch == '\r' || (ch == '\n' && (_index == 0 || yaml[_index - 1] != '\r')))
                {
                    _line++;
                    _column = 0;
                }
                else if (ch != '\n')
                {
                    _column++;
                }

                _index++;
            }

            return new Mark(index, _line, _column);
        }
    }

    private static YamlSyntaxKind MapTokenKind(Token token)
    {
        return token switch
        {
            StreamStart => YamlSyntaxKind.StreamStart,
            StreamEnd => YamlSyntaxKind.StreamEnd,
            DocumentStart => YamlSyntaxKind.DocumentStart,
            DocumentEnd => YamlSyntaxKind.DocumentEnd,
            BlockSequenceStart => YamlSyntaxKind.BlockSequenceStart,
            BlockMappingStart => YamlSyntaxKind.BlockMappingStart,
            BlockEnd => YamlSyntaxKind.BlockEnd,
            FlowSequenceStart => YamlSyntaxKind.FlowSequenceStart,
            FlowSequenceEnd => YamlSyntaxKind.FlowSequenceEnd,
            FlowMappingStart => YamlSyntaxKind.FlowMappingStart,
            FlowMappingEnd => YamlSyntaxKind.FlowMappingEnd,
            FlowEntry => YamlSyntaxKind.FlowEntry,
            BlockEntry => YamlSyntaxKind.BlockEntry,
            Key => YamlSyntaxKind.Key,
            Value => YamlSyntaxKind.Value,
            Scalar => YamlSyntaxKind.Scalar,
            Tag => YamlSyntaxKind.Tag,
            Anchor => YamlSyntaxKind.Anchor,
            AnchorAlias => YamlSyntaxKind.AnchorAlias,
            VersionDirective => YamlSyntaxKind.VersionDirective,
            TagDirective => YamlSyntaxKind.TagDirective,
            _ => YamlSyntaxKind.Unknown,
        };
    }
}
