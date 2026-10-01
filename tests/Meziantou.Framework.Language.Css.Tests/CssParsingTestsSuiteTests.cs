using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Language.Css.Internals;

namespace Meziantou.Framework.Language.Css.Tests;

/// <summary>Runs the css-parsing-tests corpus, which tinycss2 and rust-cssparser are tested against, on this parser.</summary>
/// <remarks>
/// <para>
/// The corpus describes its results as JSON: tokens, component values, rules, and declarations, as CSS Syntax Level 3
/// defined them in 2021. This parser keeps whitespace as trivia and gives its rules more structure than that, so each
/// tree is projected onto the token stream the corpus speaks of: the tokens of the tree, with a whitespace token for
/// each run of whitespace that no comment interrupts, regrouped into component values the way the specification does.
/// The rules and declarations of the tree say where each one starts and ends.
/// </para>
/// <para>
/// The corpus predates a few changes to the specification, which <see cref="KnownDeviations"/> lists with the reason
/// for each. See <c>files/css-parsing-tests/README.md</c> for where the corpus comes from.
/// </para>
/// </remarks>
public sealed class CssParsingTestsSuiteTests
{
    /// <summary>The cases where this parser follows the current specification rather than the 2021 one the corpus was written for.</summary>
    private static readonly Dictionary<string, string> KnownDeviations = new(StringComparer.Ordinal)
    {
        ["component_value_list.json:" + "u+1 U+10 U+100 U+1000 U+10000 U+100000 U+1000000"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "u+? u+1? U+10? U+100? U+1000? U+10000? U+100000?"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "u+?? U+1?? U+10?? U+100?? U+1000?? U+10000??"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "u+??? U+1??? U+10??? U+100??? U+1000???"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "u+???? U+1???? U+10???? U+100????"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "u+????? U+1????? U+10?????"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "u+?????? U+1??????"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "u+1-2 U+100000-2 U+1000000-2 U+10-200000"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["component_value_list.json:" + "\u00f9+12 \u00dc+12 u +12 U+ 12 U+12 - 20 U+1?2 U+1?-50"] = "CSS Syntax no longer has unicode-range tokens: only the value of a unicode-range descriptor is read with them.",
        ["one_rule.json:" + " /* CDO/CDC are not special */ <!-- --> {"] = "A rule is read the way the top level of a style sheet reads it, which ignores <!-- and -->.",
        ["one_rule.json:" + "div {} -->"] = "A rule is read the way the top level of a style sheet reads it, which ignores <!-- and -->.",
    };

    private static readonly string[] Files =
    [
        "component_value_list.json",
        "one_component_value.json",
        "one_declaration.json",
        "blocks_contents.json",
        "one_rule.json",
        "stylesheet.json",
        "An+B.json",
    ];

    public static TheoryData<string, int> Cases()
    {
        var data = new TheoryData<string, int>();
        foreach (var file in Files)
        {
            var count = Load(file).GetArrayLength() / 2;
            for (var i = 0; i < count; i++)
            {
                data.Add(file, i);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Corpus(string file, int index)
    {
        var cases = Load(file);
        var input = cases[index * 2].GetString()!;
        var expected = cases[(index * 2) + 1];
        var actual = Project(file, input);

        var matches = JsonEquals(expected, actual);
        var key = file + ":" + input;
        if (KnownDeviations.TryGetValue(key, out var reason))
        {
            Assert.False(matches, $"The known deviation now matches the corpus, so it can be removed: {reason}");
            return;
        }

        Assert.True(matches, $"Input: {JsonSerializer.Serialize(input)} Expected: {JsonSerializer.Serialize(expected)} Actual: {actual?.ToJsonString() ?? "null"}");
    }

    public static TheoryData<int> UnicodeRangeCases()
    {
        var data = new TheoryData<int>();
        var cases = Load("component_value_list.json");
        for (var i = 0; i < cases.GetArrayLength() / 2; i++)
        {
            if (KnownDeviations.TryGetValue("component_value_list.json:" + cases[i * 2].GetString(), out var reason) && reason.Contains("unicode-range", StringComparison.Ordinal))
            {
                data.Add(i);
            }
        }

        return data;
    }

    /// <summary>The value of a unicode-range descriptor is read with unicode-range tokens, which the tests of the corpus expect everywhere.</summary>
    [Theory]
    [MemberData(nameof(UnicodeRangeCases))]
    public void UnicodeRangeDescriptor_MatchesCorpus(int index)
    {
        var cases = Load("component_value_list.json");
        var input = cases[index * 2].GetString();
        var expected = cases[(index * 2) + 1];

        var declaration = Assert.IsType<JsonArray>(Project("one_declaration.json", "unicode-range:" + input));

        Assert.True(JsonEquals(expected, declaration[2]), $"Input: {JsonSerializer.Serialize(input)} Expected: {JsonSerializer.Serialize(expected)} Actual: {declaration[2]?.ToJsonString()}");
    }

    [Fact]
    public void KnownDeviations_AreAllCases()
    {
        var keys = Files.SelectMany(file =>
        {
            var cases = Load(file);
            return Enumerable.Range(0, cases.GetArrayLength() / 2).Select(i => file + ":" + cases[i * 2].GetString());
        }).ToHashSet(StringComparer.Ordinal);

        Assert.All(KnownDeviations.Keys, key => Assert.Contains(key, keys));
    }

    private static JsonNode? Project(string file, string input)
    {
        switch (file)
        {
            case "component_value_list.json":
            {
                var root = CssTestHooks.ParseComponentValueList(input);
                var projector = new Projector(root);
                return projector.ProjectRange(0, projector.EndOfFile, leadingGap: true, trailingGap: true);
            }

            case "one_component_value.json":
            {
                var root = CssTestHooks.ParseComponentValueList(input);
                var values = root.Statements.OfType<CssBadDeclarationSyntax>().SelectMany(statement => statement.Values).ToList();
                if (values.Count == 0)
                    return Error("empty");

                if (values.Count > 1)
                    return Error("extra-input");

                var projector = new Projector(root);
                var list = new JsonArray();
                projector.ProjectValue(list, projector.IndexOf(values[0].GetFirstToken()), projector.EndOfFile);
                return list[0]?.DeepClone();
            }

            case "one_declaration.json":
            {
                var root = CssTestHooks.ParseSingleDeclaration(input);
                return root.Statements.Count switch
                {
                    0 => Error("empty"),
                    _ => new Projector(root).ProjectStatement(root.Statements[0]),
                };
            }

            case "blocks_contents.json":
            {
                var tree = CssSyntaxTree.ParseText(input, new CssParseOptions { SourceKind = CssSourceKind.DeclarationList });
                CssSyntaxAssert.TextIsFaithful(input, tree);
                return new Projector(tree.GetRoot()).ProjectStatements(tree.GetRoot().Statements);
            }

            case "one_rule.json":
            {
                var tree = CssSyntaxAssert.TextIsFaithful(input);
                var statements = tree.GetRoot().Statements.Where(statement => statement is not CssIgnoredTokenSyntax).ToList();
                return statements.Count switch
                {
                    0 => Error("empty"),
                    1 => new Projector(tree.GetRoot()).ProjectStatement(statements[0]),
                    _ => Error("extra-input"),
                };
            }

            case "stylesheet.json":
            {
                var tree = CssSyntaxAssert.TextIsFaithful(input);
                return new Projector(tree.GetRoot()).ProjectStatements(tree.GetRoot().Statements);
            }

            case "An+B.json":
                return CssTestHooks.TryParseAnPlusB(input, out var a, out var b) ? new JsonArray(a, b) : null;

            default:
                throw new ArgumentOutOfRangeException(nameof(file), file, "Unknown corpus file.");
        }
    }

    private static JsonArray Error(string kind) => new("error", kind);

    private static JsonElement Load(string file)
    {
        using var stream = typeof(CssParsingTestsSuiteTests).Assembly.GetManifestResourceStream("Meziantou.Framework.Language.Css.Tests.files.css-parsing-tests." + file)
            ?? throw new InvalidOperationException($"The corpus file '{file}' is not embedded.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return document.RootElement.Clone();
    }

    /// <summary>Compares a result of the corpus with a projection, numbers by value.</summary>
    private static bool JsonEquals(JsonElement expected, JsonNode? actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                return actual is null;

            case JsonValueKind.String:
                return actual is JsonValue value && value.TryGetValue<string>(out var text) && string.Equals(text, expected.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                if (actual is not JsonValue number || number.GetValueKind() != JsonValueKind.Number)
                    return false;

                var actualNumber = double.Parse(number.ToJsonString(), CultureInfo.InvariantCulture);

                var expectedNumber = expected.GetDouble();
                return expectedNumber == actualNumber || Math.Abs(expectedNumber - actualNumber) <= 1e-9 * Math.Max(1, Math.Abs(expectedNumber));

            case JsonValueKind.True or JsonValueKind.False:
                return actual is JsonValue boolean && boolean.TryGetValue<bool>(out var flag) && flag == expected.GetBoolean();

            case JsonValueKind.Array:
                if (actual is not JsonArray array || array.Count != expected.GetArrayLength())
                    return false;

                var index = 0;
                foreach (var item in expected.EnumerateArray())
                {
                    if (!JsonEquals(item, array[index]))
                        return false;

                    index++;
                }

                return true;

            default:
                return false;
        }
    }

    /// <summary>Projects a tree onto the tokens and component values of CSS Syntax Level 3, as the corpus writes them.</summary>
    private sealed class Projector
    {
        private readonly SyntaxToken[] _tokens;
        private readonly Dictionary<int, int> _indexByStart = [];

        public Projector(CssStyleSheetSyntax root)
        {
            // A missing token stands for text the source does not have, so it is not part of the token stream.
            _tokens = [.. root.DescendantTokens().Where(token => !token.IsMissing)];
            for (var i = 0; i < _tokens.Length; i++)
            {
                _indexByStart[_tokens[i].FullSpan.Start] = i;
            }
        }

        public int EndOfFile => _tokens.Length - 1;

        public int IndexOf(SyntaxToken token) => _indexByStart[token.FullSpan.Start];

        /// <summary>Gets the index of the first token after <paramref name="node"/>.</summary>
        private int IndexAfter(SyntaxNode node) => _indexByStart[node.FullSpan.End];

        public JsonArray ProjectStatements(IEnumerable<CssStatementSyntax> statements)
        {
            var list = new JsonArray();
            foreach (var statement in statements)
            {
                if (ProjectStatement(statement) is { } projected)
                {
                    list.Add(projected);
                }
            }

            return list;
        }

        public JsonArray? ProjectStatement(CssStatementSyntax statement)
        {
            switch (statement)
            {
                case CssIgnoredTokenSyntax:
                    return null;

                case CssQualifiedRuleSyntax rule:
                {
                    if (rule.Block.OpenBraceToken.IsMissing)
                        return Error("invalid");

                    var open = IndexOf(rule.Block.OpenBraceToken);
                    var start = rule.Prelude is null ? open : IndexOf(rule.GetFirstToken());
                    return new JsonArray("qualified rule", ProjectRange(start, open, leadingGap: false, trailingGap: true), ProjectBlock(rule.Block));
                }

                case CssAtRuleSyntax rule:
                {
                    var atKeyword = IndexOf(rule.AtKeywordToken);
                    var preludeEnd = rule.Block is not null ? IndexOf(rule.Block.OpenBraceToken)
                        : rule.SemicolonToken.IsPresent() ? IndexOf(rule.SemicolonToken)
                        : IndexAfter(rule);
                    var block = rule.Block is null ? null : ProjectBlock(rule.Block);
                    return new JsonArray("at-rule", rule.Name, ProjectRange(atKeyword + 1, preludeEnd, leadingGap: true, trailingGap: true), block);
                }

                case CssDeclarationSyntax declaration:
                {
                    var colon = IndexOf(declaration.ColonToken);
                    var valueEnd = declaration.Important is not null ? IndexOf(declaration.Important.ExclamationToken)
                        : declaration.SemicolonToken.IsPresent() ? IndexOf(declaration.SemicolonToken)
                        : IndexAfter(declaration);
                    return new JsonArray("declaration", declaration.Name, ProjectRange(colon + 1, valueEnd, leadingGap: true, trailingGap: true), declaration.IsImportant);
                }

                default:
                    return Error("invalid");
            }
        }

        private JsonArray ProjectBlock(CssBlockSyntax block)
        {
            var open = IndexOf(block.OpenBraceToken);
            var close = block.CloseBraceToken.IsMissing ? IndexAfter(block) : IndexOf(block.CloseBraceToken);
            return ProjectRange(open + 1, close, leadingGap: true, trailingGap: true);
        }

        /// <summary>Projects the tokens from <paramref name="from"/> up to <paramref name="to"/> as component values.</summary>
        /// <param name="from">The first token.</param>
        /// <param name="to">The token after the last one.</param>
        /// <param name="leadingGap">Whether the whitespace before the first token counts.</param>
        /// <param name="trailingGap">Whether the whitespace after the last token counts.</param>
        public JsonArray ProjectRange(int from, int to, bool leadingGap, bool trailingGap)
        {
            var list = new JsonArray();
            if (from >= to)
            {
                if (leadingGap && trailingGap)
                {
                    AddWhitespace(list, from - 1, to);
                }

                return list;
            }

            if (leadingGap)
            {
                AddWhitespace(list, from - 1, from);
            }

            var index = from;
            var endsWithUnclosedBlock = false;
            while (index < to)
            {
                if (index > from)
                {
                    AddWhitespace(list, index - 1, index);
                }

                var next = ProjectValue(list, index, to);
                endsWithUnclosedBlock = next == to && SyntaxFacts.GetClosingKind(_tokens[index].Kind()) != SyntaxKind.None && FindClose(index, to) < 0;
                index = next;
            }

            // The whitespace after the last value of a block that runs to the end belongs to that block.
            if (trailingGap && !endsWithUnclosedBlock)
            {
                AddWhitespace(list, index - 1, to);
            }

            return list;
        }

        /// <summary>Projects the component value at <paramref name="index"/>, and returns the index of the token after it.</summary>
        public int ProjectValue(JsonArray list, int index, int to)
        {
            var token = _tokens[index];
            var kind = token.Kind();
            switch (kind)
            {
                case SyntaxKind.FunctionToken or SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken:
                {
                    var close = FindClose(index, to);
                    var block = kind switch
                    {
                        SyntaxKind.FunctionToken => new JsonArray("function", token.ValueText),
                        SyntaxKind.OpenParenToken => new JsonArray("()"),
                        SyntaxKind.OpenBracketToken => new JsonArray("[]"),
                        _ => new JsonArray("{}"),
                    };

                    foreach (var item in ProjectRange(index + 1, close < 0 ? to : close, leadingGap: true, trailingGap: true).ToList())
                    {
                        block.Add(item?.DeepClone());
                    }

                    list.Add(block);
                    return close < 0 ? to : close + 1;
                }

                case SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken:
                    list.Add(Error(token.Text));
                    return index + 1;

                case SyntaxKind.TildeToken or SyntaxKind.BarToken or SyntaxKind.CaretToken or SyntaxKind.DollarToken or SyntaxKind.AsteriskToken
                    when index + 1 < to && IsAdjacent(index) && (_tokens[index + 1].Kind() == SyntaxKind.EqualsToken || (kind == SyntaxKind.BarToken && _tokens[index + 1].Kind() == SyntaxKind.BarToken)):
                    // The 2021 tokenizer had tokens for "~=", "|=", "^=", "$=", "*=", and "||", which are two delims now.
                    list.Add(token.Text + _tokens[index + 1].Text);
                    return index + 2;
            }

            list.Add(ProjectToken(token));

            // The 2021 tests mark a string or a URL that the end of the text ends as a parse error.
            if (kind is SyntaxKind.StringToken or SyntaxKind.UrlToken && !IsClosed(token.Text, kind == SyntaxKind.StringToken ? token.Text[0] : ')'))
            {
                list.Add(Error(kind == SyntaxKind.StringToken ? "eof-in-string" : "eof-in-url"));
            }

            return index + 1;

            static bool IsClosed(string text, char end)
            {
                if (text.Length < 2 || text[^1] != end)
                    return false;

                var backslashes = 0;
                for (var i = text.Length - 2; i >= 0 && text[i] == '\\'; i--)
                {
                    backslashes++;
                }

                return backslashes % 2 == 0;
            }
        }

        private bool IsAdjacent(int index) => !_tokens[index].HasTrailingTrivia && !_tokens[index + 1].HasLeadingTrivia;

        /// <summary>Finds the token that closes the block opened at <paramref name="open"/>, the way "consume a simple block" does.</summary>
        private int FindClose(int open, int to)
        {
            var expected = new Stack<SyntaxKind>();
            expected.Push(SyntaxFacts.GetClosingKind(_tokens[open].Kind()));
            for (var i = open + 1; i < to; i++)
            {
                var kind = _tokens[i].Kind();
                var closing = SyntaxFacts.GetClosingKind(kind);
                if (closing != SyntaxKind.None)
                {
                    expected.Push(closing);
                }
                else if (kind == expected.Peek())
                {
                    expected.Pop();
                    if (expected.Count == 0)
                        return i;
                }
            }

            return -1;
        }

        private static JsonNode ProjectToken(SyntaxToken token)
        {
            var text = token.Text;
            switch (token.Kind())
            {
                case SyntaxKind.IdentToken:
                    return new JsonArray("ident", token.ValueText);
                case SyntaxKind.AtKeywordToken:
                    return new JsonArray("at-keyword", token.ValueText);
                case SyntaxKind.HashToken:
                    return new JsonArray("hash", token.ValueText, token.IsIdHash() ? "id" : "unrestricted");
                case SyntaxKind.StringToken:
                    return new JsonArray("string", token.ValueText);
                case SyntaxKind.BadStringToken:
                    return Error("bad-string");
                case SyntaxKind.UrlToken:
                    return new JsonArray("url", token.ValueText);
                case SyntaxKind.BadUrlToken:
                    return Error("bad-url");
                case SyntaxKind.NumberToken:
                    return new JsonArray("number", text, Normalize(token.GetNumericValue()!.Value), token.IsInteger() ? "integer" : "number");
                case SyntaxKind.PercentageToken:
                    return new JsonArray("percentage", text[..^1], Normalize(token.GetNumericValue()!.Value), token.IsInteger() ? "integer" : "number");
                case SyntaxKind.DimensionToken:
                    var number = text[..CssIdentifier.ScanNumber(text, out _)];
                    return new JsonArray("dimension", number, Normalize(token.GetNumericValue()!.Value), token.IsInteger() ? "integer" : "number", token.GetUnit());
                case SyntaxKind.UnicodeRangeToken:
                    var range = token.GetUnicodeRange()!.Value;
                    return new JsonArray("unicode-range", range.Start, range.End);
                default:
                    return JsonValue.Create(text)!;
            }

            static double Normalize(double value) => value == 0 ? 0 : value;
        }

        /// <summary>Adds a whitespace token for each run of whitespace between two tokens that no comment interrupts.</summary>
        private void AddWhitespace(JsonArray list, int before, int after)
        {
            var inRun = false;
            IEnumerable<SyntaxTrivia> trivia = after < _tokens.Length ? _tokens[after].LeadingTrivia : [];
            if (before >= 0)
            {
                trivia = _tokens[before].TrailingTrivia.Concat(trivia);
            }

            foreach (var item in trivia)
            {
                if (item.Kind() is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia)
                {
                    if (!inRun)
                    {
                        list.Add(" ");
                        inRun = true;
                    }
                }
                else
                {
                    inRun = false;
                }
            }
        }
    }
}
