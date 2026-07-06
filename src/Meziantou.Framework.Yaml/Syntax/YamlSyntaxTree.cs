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

        ValidateYaml(yaml);

        var tokens = new List<YamlSyntaxToken>();
        if (options.IncludeTrivia)
        {
            AddTriviaTokens(yaml, tokens);
        }

        AddScannerTokens(yaml, tokens);
        tokens.Sort(static (left, right) =>
        {
            var position = left.Span.Start.Index.CompareTo(right.Span.Start.Index);
            if (position != 0)
            {
                return position;
            }

            return left.Span.End.Index.CompareTo(right.Span.End.Index);
        });

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

    private static void ValidateYaml(string yaml)
    {
        var parser = Parser.CreateParser(new StringReader(yaml));
        while (parser.MoveNext())
        {
        }
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

    private static void AddScannerTokens(string yaml, List<YamlSyntaxToken> target)
    {
        var scanner = new Scanner<StringLookAheadBuffer>(new StringLookAheadBuffer(yaml));
        while (scanner.MoveNext())
        {
            var token = scanner.Current;
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

    private static void AddTriviaTokens(string yaml, List<YamlSyntaxToken> target)
    {
        var index = 0;
        var line = 0;
        var column = 0;

        while (index < yaml.Length)
        {
            var start = new Mark(index, line, column);
            var current = yaml[index];
            if (current == '#')
            {
                var cursor = index + 1;
                var endLine = line;
                var endColumn = column + 1;
                while (cursor < yaml.Length && yaml[cursor] != '\r' && yaml[cursor] != '\n')
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
                if (current == '\r' && cursor + 1 < yaml.Length && yaml[cursor + 1] == '\n')
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
                while (cursor < yaml.Length && (yaml[cursor] == ' ' || yaml[cursor] == '\t'))
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
        var line = 0;
        var column = 0;
        for (var cursor = 0; cursor < index && cursor < yaml.Length; cursor++)
        {
            var ch = yaml[cursor];
            if (ch == '\r')
            {
                if (cursor + 1 < index && cursor + 1 < yaml.Length && yaml[cursor + 1] == '\n')
                {
                    cursor++;
                }

                line++;
                column = 0;
                continue;
            }

            if (ch == '\n')
            {
                line++;
                column = 0;
                continue;
            }

            column++;
        }

        return new Mark(index, line, column);
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
