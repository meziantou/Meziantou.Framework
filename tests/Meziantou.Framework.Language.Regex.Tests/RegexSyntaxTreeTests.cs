using System.Text.RegularExpressions;

namespace Meziantou.Framework.Language.Regex.Tests;

public sealed class RegexSyntaxTreeTests
{
    [Fact]
    public void ReadmeExample_Parsing()
    {
        #region RegexSyntaxTree_Parsing
        const string Pattern = """
            (?x)                                   # free-spacing mode
            ^
            (?<ip> \d{1,3} (?: \. \d{1,3} ){3} )   # client address
            \s+ (?<status> [1-5]\d{2} )
            $
            """;

        var tree = RegexSyntaxTree.ParseText(Pattern, RegexDialect.Net);

        // Nothing is lost: the tree reproduces the input character for character.
        Assert.Equal(Pattern, tree.GetRoot().ToFullString());
        Assert.Empty(tree.GetDiagnostics());

        // Invalid input produces diagnostics instead of exceptions.
        var invalid = RegexSyntaxTree.ParseText("(a", RegexDialect.Net);
        var diagnostic = Assert.Single(invalid.GetDiagnostics());
        Assert.Equal("REGEX0002 at (0,0)-(0,2): Unterminated group: expected ')'.", $"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
        #endregion
    }

    [Fact]
    public void ReadmeExample_DescendantNodes()
    {
        #region RegexSyntaxTree_DescendantNodes
        var tree = RegexSyntaxTree.ParseText(@"(?<year>\d{4})-[01]\d-[0-3]\d", RegexDialect.Net);

        var classes = tree.GetRoot().DescendantNodes().OfType<RegexCharacterClassSyntax>();
        var groups = tree.GetRoot().DescendantNodes().OfType<RegexGroupSyntax>();

        Assert.Equal(["[01]", "[0-3]"], classes.Select(node => node.ToString()));
        Assert.Equal([@"(?<year>\d{4})"], groups.Select(node => node.ToString()));
        #endregion
    }

    [Fact]
    public void ReadmeExample_JavaScriptLiteral()
    {
        #region RegexSyntaxTree_JavaScriptLiteral
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/a+b/giu");

        Assert.Equal("giu", tree.GetRoot().FlagsToken.Text);
        Assert.Equal(RegexPatternOptions.IgnoreCase | RegexPatternOptions.Unicode | RegexPatternOptions.Global, tree.PatternOptions);
        Assert.Equal("/a+b/giu", tree.GetRoot().ToFullString());
        #endregion
    }

    [Fact]
    public void ReadmeExample_ParseOptions()
    {
        #region RegexSyntaxTree_ParseOptions
        var pattern = "a b # comment";
        var options = new RegexParseOptions(RegexDialect.Net)
        {
            PatternOptions = RegexPatternOptions.IgnorePatternWhitespace,
            MaxRecursionDepth = 64,
        };

        var tree = RegexSyntaxTree.ParseText(pattern, options);

        // In extended mode, the whitespace and the comment are trivia
        Assert.HasCount(2, tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>());
        #endregion
    }

    [Fact]
    public void ReadmeExample_RegexOptionsInterop()
    {
        #region RegexSyntaxTree_RegexOptionsInterop
        var patternOptions = RegexOptionsInterop.ToPatternOptions(RegexOptions.IgnoreCase | RegexOptions.Compiled);
        Assert.Equal(RegexPatternOptions.IgnoreCase, patternOptions);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Branches()
    {
        #region RegexSyntaxTree_Branches
        var tree = RegexSyntaxTree.ParseText("ab|c", RegexDialect.Net);
        var branches = tree.GetRoot().Alternation.Branches;

        Assert.Equal(2, branches.Count);
        Assert.Equal(1, branches.SeparatorCount);
        Assert.Equal(2, branches[0].Terms.Count);
        Assert.Equal("|", branches.GetSeparator(0).ToString());
        #endregion
    }

    [Fact]
    public void ReadmeExample_NodeOptions()
    {
        #region RegexSyntaxTree_NodeOptions
        var tree = RegexSyntaxTree.ParseText("a(?i)b", RegexDialect.Net);
        var literals = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();

        Assert.Equal(RegexPatternOptions.None, literals[0].Options);
        Assert.Equal(RegexPatternOptions.IgnoreCase, literals[1].Options);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Captures()
    {
        #region RegexSyntaxTree_Captures
        var tree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexDialect.Net);

        var captures = new List<string>();
        foreach (var capture in tree.Captures)
        {
            captures.Add($"{capture.Number}: {capture.Name}");
        }

        Assert.Equal(["1: 1", "2: 2", "3: x"], captures);

        // The same pattern in JavaScript or PCRE
        var javaScriptTree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexDialect.JavaScript);
        Assert.Equal(["1: 1", "2: x", "3: 3"], javaScriptTree.Captures.Select(capture => $"{capture.Number}: {capture.Name}"));
        #endregion
    }

    [Fact]
    public void ReadmeExample_Comments()
    {
        #region RegexSyntaxTree_Comments
        var tree = RegexSyntaxTree.ParseText("a(?#note)b", RegexDialect.Net);

        var comments = new List<string>();
        foreach (var comment in tree.GetRoot().DescendantComments())
        {
            comments.Add($"{comment.Span.Start}: {comment}");
        }

        Assert.Equal(["1: (?#note)"], comments);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ReplaceNode()
    {
        #region RegexSyntaxTree_ReplaceNode
        var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
        var tree = RegexSyntaxTree.ParseText("a   b # keep this\n", options);
        var second = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().Last();
        var replacement = SyntaxFactory.Literal('z', RegexDialect.Net);

        Assert.Equal("az # keep this\n", tree.GetRoot().ReplaceNode(second, replacement).ToFullString());

        Assert.Equal("a   z # keep this\n", tree.GetRoot().ReplaceNode(second, replacement.WithTriviaFrom(second)).ToFullString());
        #endregion
    }

    [Fact]
    public void ReadmeExample_WithChanges()
    {
        #region RegexSyntaxTree_WithChanges
        var tree = RegexSyntaxTree.ParseText("ab+c", RegexDialect.Net);
        var updated = tree.WithChanges(new TextChange(new TextSpan(2, 1), "*"));

        Assert.Equal("ab*c", updated.GetText().Text);
        #endregion
    }

    [Fact]
    public void ReadmeExample_IsEquivalentTo()
    {
        #region RegexSyntaxTree_IsEquivalentTo
        var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
        var spaced = RegexSyntaxTree.ParseText("a  b   # note\n", options);
        var tight = RegexSyntaxTree.ParseText("ab", options);

        Assert.True(spaced.IsEquivalentTo(tight));
        #endregion
    }

    [Fact]
    public void ReadmeExample_Annotations()
    {
        #region RegexSyntaxTree_Annotations
        var tree = RegexSyntaxTree.ParseText("a|b", RegexDialect.Net);
        var marker = new SyntaxAnnotation();
        var branch = tree.GetRoot().Alternation.Branches[1];

        var marked = tree.GetRoot().ReplaceNode(branch, branch.WithAdditionalAnnotations(marker));
        var edited = marked.ReplaceNode(marked.Alternation.Branches[0], SyntaxFactory.LiteralText("xy", RegexDialect.Net));

        var found = edited.GetAnnotatedNodes(marker).Single();
        Assert.Equal("b", found.ToFullString());
        #endregion
    }

    [Fact]
    public void ReadmeExample_SyntaxFactory()
    {
        #region RegexSyntaxTree_SyntaxFactory
        var pattern = SyntaxFactory.Sequence(
            SyntaxFactory.Anchor(RegexAnchorKind.Caret),
            SyntaxFactory.Quantified(SyntaxFactory.ClassEscape('d'), min: 1, max: 3),
            SyntaxFactory.Literal('.', RegexDialect.Net));

        Assert.Equal(@"^\d{1,3}\.", pattern.ToFullString());
        #endregion
    }

    [Fact]
    public void ReadmeExample_Rewriter()
    {
        #region RegexSyntaxTree_RewriterUsage
        var tree = RegexSyntaxTree.ParseText(@"(?<year>\d{4})-(?<month>\d{2})", RegexDialect.Net);

        var updated = new RenameGroup("year", "y").Visit(tree.GetRoot());

        Assert.Equal(@"(?<y>\d{4})-(?<month>\d{2})", updated?.ToFullString());
        #endregion
    }

    public static TheoryData<string> Patterns =>
    [
        "",
        "a",
        "abc",
        "a|b",
        "a|b|c",
        "|",
        "a*",
        "a+?",
        "a{2}",
        "a{2,}",
        "a{2,5}?",
        "a{2 , 3}",
        ".",
        "^abc$",
        @"\bword\b",
        @"\d+\.\d+",
        "[abc]",
        "[^a-z]",
        "[]]",
        "[a-]",
        "[-a]",
        @"[\]]",
        "(a)(b)",
        "(?:a)",
        "(?<name>a)",
        "(?'name'a)",
        "(?=a)",
        "(?!a)",
        "(?<=a)",
        "(?<!a)",
        "(?>a)",
        "(?i)a",
        "(?i:a)",
        "(?#comment)a",
        @"(a)\1",
        @"\1(a)",
        @"(?<n>a)\k<n>",
        @"\p{L}+",
        @"\P{IsGreek}",
        @"\x41A\cA\052",
        "(?(1)a|b)(x)",
        "(?(?=a)b|c)",
        "[a-z-[aeiou]]",
    ];

    [Theory]
    [MemberData(nameof(Patterns))]
    public void ParseText_RoundTripsExactly(string pattern) => RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.Net);

    [Theory]
    [MemberData(nameof(Patterns))]
    public void ParseText_ReportsNothingForValidPatterns(string pattern)
    {
        var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseText_ExposesTheDialectAndTheText()
    {
        var tree = RegexSyntaxTree.ParseText("a+", RegexDialect.Net);

        Assert.Equal("a+", tree.GetText().Text);
        Assert.Equal(RegexDialect.Net, tree.Dialect);
        Assert.Equal(SyntaxKind.Pattern, tree.GetRoot().Kind());
        Assert.Same(tree.GetRoot(), tree.GetRoot());
    }

    [Fact]
    public void ParseText_TreatsNullAsAnEmptyPattern()
    {
        var tree = RegexSyntaxTree.ParseText(null!, RegexDialect.Net);

        Assert.Equal("", tree.GetText().Text);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void Root_AlwaysHasOneAlternationOfSequences()
    {
        var tree = RegexSyntaxTree.ParseText("ab", RegexDialect.Net);

        var branch = Assert.Single(tree.GetRoot().Alternation.Branches);
        Assert.False(tree.GetRoot().Alternation.HasAlternatives);
        Assert.Equal(2, branch.Terms.Count);
    }

    [Fact]
    public void Alternation_KeepsOneBranchPerBar()
    {
        var tree = RegexSyntaxTree.ParseText("a|b|c", RegexDialect.Net);

        Assert.Equal(3, tree.GetRoot().Alternation.Branches.Count);
        Assert.Equal(2, tree.GetRoot().Alternation.Branches.SeparatorCount);
        Assert.True(tree.GetRoot().Alternation.HasAlternatives);
    }

    [Fact]
    public void Quantifier_ReportsItsBounds()
    {
        var tree = RegexSyntaxTree.ParseText("a{2,5}?", RegexDialect.Net);

        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.GetRoot().Alternation.Branches[0].Terms[0]);
        var quantifier = Assert.IsType<RegexRangeQuantifierSyntax>(quantified.Quantifier);
        Assert.Equal(2, quantifier.MinCount);
        Assert.Equal(5, quantifier.MaxCount);
        Assert.Equal(RegexQuantifierMode.Lazy, quantified.Mode);
    }

    [Theory]
    [InlineData("a*", 0, null)]
    [InlineData("a+", 1, null)]
    [InlineData("a?", 0, 1)]
    [InlineData("a{3}", 3, 3)]
    [InlineData("a{3,}", 3, null)]
    public void Quantifier_ReportsSimpleBounds(string pattern, int min, int? max)
    {
        var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.GetRoot().Alternation.Branches[0].Terms[0]);
        Assert.Equal(min, quantified.Quantifier.MinCount);
        Assert.Equal(max, quantified.Quantifier.MaxCount);
    }

    [Fact]
    public void IsEquivalentTo_IgnoresExtendedModeWhitespace()
    {
        var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
        var spaced = RegexSyntaxTree.ParseText("a  b   # note\n", options);
        var tight = RegexSyntaxTree.ParseText("ab", options);

        Assert.True(spaced.IsEquivalentTo(tight));
    }

    [Fact]
    public void IsEquivalentTo_IsFalseAcrossDialects()
    {
        var net = RegexSyntaxTree.ParseText("a", RegexDialect.Net);
        var javaScript = RegexSyntaxTree.ParseText("a", RegexDialect.JavaScript);

        Assert.False(net.IsEquivalentTo(javaScript));
    }

    [Fact]
    public void GetChanges_TrimsTheCommonPrefixAndSuffix()
    {
        var before = RegexSyntaxTree.ParseText("ab+c", RegexDialect.Net);
        var after = before.WithChanges(new TextChange(new TextSpan(2, 1), "*"));

        var change = Assert.Single(after.GetChanges(before));
        Assert.Equal(new TextSpan(2, 1), change.Span);
        Assert.Equal("*", change.NewText);
        Assert.Equal("ab*c", after.GetText().Text);
    }

    [Fact]
    public void Captures_AreNumberedTheWayTheEngineNumbersThem()
    {
        var tree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexDialect.Net);

        Assert.Equal([1, 2, 3], tree.Captures.Select(capture => capture.Number));

        // Named groups take the first free numbers after every numbered one, so "x" is group 3 rather than group 2.
        Assert.Equal(["1", "2", "x"], tree.Captures.Select(capture => capture.Name));
    }

    [Theory]
    [InlineData("|")]
    [InlineData("||")]
    [InlineData("a|")]
    [InlineData("|a")]
    [InlineData("(|)")]
    [InlineData("(?:|)|")]
    public void TokenNavigation_StepsOverEmptyBranches(string pattern)
    {
        var root = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net).GetRoot();
        var tokens = root.DescendantTokens().ToArray();

        Assert.Equal(tokens[0], root.GetFirstToken());
        Assert.Equal(tokens[^1], root.GetLastToken());

        var forward = new List<SyntaxToken>();
        for (var token = root.GetFirstToken(); token != default; token = token.GetNextToken())
        {
            forward.Add(token);
        }

        var backward = new List<SyntaxToken>();
        for (var token = root.GetLastToken(); token != default; token = token.GetPreviousToken())
        {
            backward.Add(token);
        }

        Assert.Equal(tokens, forward);
        Assert.Equal(tokens.Reverse(), backward);

        foreach (var node in root.DescendantNodesAndSelf())
        {
            var first = node.GetFirstToken();
            var last = node.GetLastToken();
            Assert.Equal(first == default ? node.FullSpan : TextSpan.FromBounds(first.Span.Start, last.Span.End), node.Span);
        }
    }

    [Fact]
    public void GetFirstToken_OfAnAlternationWithEmptyBranches_IsTheBar()
    {
        var root = RegexSyntaxTree.ParseText("|", RegexDialect.Net).GetRoot();

        Assert.Equal("|", root.GetFirstToken().Text);
    }

    [Theory]
    [InlineData("(?(?#")]
    [InlineData("(?(?")]
    [InlineData("(?(")]
    [InlineData("(?(?#c)")]
    public void AnUnterminatedConditionAtTheEndOfThePattern_IsReportedRatherThanThrown(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PcrePerl);

        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Theory]
    [InlineData("/[[[]]]/v", 7)]
    [InlineData("/[[[]]/v]]", 6)]
    [InlineData("/[[a]b-/v]", 7)]
    [InlineData("/[[\\q{]/v}]", 7)]
    [InlineData("/[[\\p{]/v}]", 7)]
    [InlineData("/[\\p{]/u}", 6)]
    public void AClassLeftOpenByTheLiteral_EndsAtTheClosingSlash(string literal, int closeSlashPosition)
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(literal);

        RegexSyntaxAssert.TextIsFaithful(literal, tree);
        Assert.Equal(new TextSpan(closeSlashPosition, 1), tree.GetRoot().CloseSlashToken.Span);
    }

    [Theory]
    [InlineData("(?<")]
    [InlineData("(?<a")]
    [InlineData("(?P<")]
    public void GetDiagnostics_IsInSourceOrder(string pattern)
    {
        foreach (var dialect in new[] { RegexDialect.Net, RegexDialect.PcrePerl, RegexDialect.JavaScript })
        {
            var starts = RegexSyntaxTree.ParseText(pattern, dialect).GetDiagnostics().Select(diagnostic => diagnostic.Location.SourceSpan.Start).ToArray();

            Assert.Equal(starts.Order(), starts);
        }
    }

    #region RegexSyntaxTree_Rewriter
    private sealed class RenameGroup(string oldName, string newName) : RegexSyntaxRewriter
    {
        public override SyntaxNode? VisitNamedGroup(RegexNamedGroupSyntax node)
        {
            if (node.Name != oldName)
            {
                return base.VisitNamedGroup(node);
            }

            // WithTriviaFrom keeps whatever stood in front of the old name.
            var renamed = SyntaxFactory.Token(SyntaxKind.NameToken, newName).WithTriviaFrom(node.NameToken);

            return node.WithNameToken(renamed);
        }
    }
    #endregion
}
