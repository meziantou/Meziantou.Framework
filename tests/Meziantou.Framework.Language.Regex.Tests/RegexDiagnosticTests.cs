namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>One case per diagnostic identifier, with the span it points at.</summary>
public sealed class RegexDiagnosticTests
{
    [Theory]
    [InlineData("a)", "REGEX0001", 1)]
    [InlineData("(a", "REGEX0002", 0)]
    [InlineData("[abc", "REGEX0003", 0)]
    [InlineData(@"a\", "REGEX0004", 1)]
    [InlineData("*a", "REGEX0005", 0)]
    [InlineData("a**", "REGEX0006", 2)]
    [InlineData("a{5,2}", "REGEX0007", 1)]
    [InlineData("a{2147483648}", "REGEX0008", 2)]
    [InlineData("[z-a]", "REGEX0009", 1)]
    [InlineData("(?<a b>x)", "REGEX0017", 3)]
    [InlineData("(?#unterminated", "REGEX0011", 0)]
    [InlineData(@"\x4", "REGEX0012", 0)]
    [InlineData(@"\c", "REGEX0013", 0)]
    [InlineData(@"\c1", "REGEX0014", 0)]
    [InlineData(@"\q", "REGEX0015", 0)]
    [InlineData(@"[a-\d]", "REGEX0016", 3)]
    [InlineData("(?<0>x)", "REGEX0018", 3)]
    [InlineData(@"\k", "REGEX0019", 0)]
    [InlineData(@"\1", "REGEX0030", 0)]
    [InlineData(@"\k<none>", "REGEX0031", 3)]
    [InlineData(@"\p{L", "REGEX0032", 0)]
    [InlineData(@"\pLxy", "REGEX0033", 0)]
    [InlineData(@"\p{Bogus}", "REGEX0034", 3)]
    [InlineData("[a-z-[b]c]", "REGEX0050", 5)]
    [InlineData("(?(1)a|b|c)(x)", "REGEX0051", 5)]
    [InlineData("(?(?#c)a|b)", "REGEX0054", 2)]
    [InlineData("(?(?<n>x)a|b)", "REGEX0053", 2)]
    [InlineData("(?(1)a|b)", "REGEX0056", 3)]
    public void PatternReportsTheExpectedDiagnostic(string pattern, string id, int spanStart)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.Net);

        var diagnostic = Assert.Single(tree.GetDiagnostics(), candidate => candidate.Id == id, $"[{pattern}] reported {string.Join(", ", tree.GetDiagnostics().Select(d => d.Id))}");
        Assert.Equal(spanStart, diagnostic.Location.SourceSpan.Start);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>The diagnostics only some dialects have, with the span each points at.</summary>
    [Theory]
    [InlineData("javascript", "(?<a>x)(?<a>y)", 0, "REGEX0035", 10)]
    [InlineData("javascript", "[(]", 128, "REGEX0073", 1)]
    [InlineData("javascript", @"[^\q{ab}]", 128, "REGEX0074", 0)]
    [InlineData("javascript", "(?ii:a)", 0, "REGEX0075", 3)]
    [InlineData("javascript", @"\p{Greek}", 64, "REGEX0034", 3)]
    [InlineData("pcre", "a(*BOGUS)", 0, "REGEX0090", 1)]
    [InlineData("pcre", "(?C256)", 0, "REGEX0091", 3)]
    [InlineData("pcre", "x(?<=a+)", 0, "REGEX0092", 1)]
    [InlineData("pcre", "(?(?:a)b)", 0, "REGEX0093", 2)]
    [InlineData("pcre", "[[:nope:]]", 0, "REGEX0110", 3)]
    [InlineData("ere", "ab{1", 0, "REGEX0111", 2)]
    [InlineData("ere", "(a)|\\1", 0, "REGEX0030", 4)]
    public void DialectPatternReportsTheExpectedDiagnostic(string dialectName, string pattern, int options, string id, int spanStart)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));
        var patternOptions = (RegexPatternOptions)options;
        if (patternOptions.HasFlag(RegexPatternOptions.UnicodeSets))
        {
            patternOptions |= RegexPatternOptions.Unicode;
        }

        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, new RegexParseOptions(dialect) { PatternOptions = patternOptions });

        var diagnostic = Assert.Single(tree.GetDiagnostics(), candidate => candidate.Id == id, $"[{pattern}] reported {string.Join(", ", tree.GetDiagnostics().Select(d => d.Id))}");
        Assert.Equal(spanStart, diagnostic.Location.SourceSpan.Start);
    }

    /// <summary>
    /// An error does not end the parse: what follows it is read the same way it would be without the error, so every
    /// later construct is still in the tree and every later problem is still reported.
    /// </summary>
    [Theory]
    [InlineData("net", @"a{5,2}(b)\q[c-a]d+", "REGEX0007,REGEX0015,REGEX0009")]
    [InlineData("javascript", @"\a(b)[\d-z]d+", "REGEX0015,REGEX0016")]
    [InlineData("pcre", @"(*BOGUS)(b)\p{Nope}d+", "REGEX0090,REGEX0034")]
    [InlineData("ere", "{1(b)[[:nope:]]d+", "REGEX0005,REGEX0110")]
    [InlineData("bre", @"\{1\(b\)[[:nope:]]d\+", "REGEX0005,REGEX0110")]
    public void ParsingContinuesPastAnError(string dialectName, string pattern, string ids)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));
        var options = new RegexParseOptions(dialect) { PatternOptions = dialect == RegexDialect.JavaScript ? RegexPatternOptions.Unicode : RegexPatternOptions.None };

        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.Equal(ids, string.Join(",", tree.GetDiagnostics().Select(d => d.Id)));
        var group = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCapturingGroupSyntax>());
        Assert.Equal(1, Assert.Single(tree.Captures).Number);
        Assert.True(group.ToString().Contains('b', StringComparison.Ordinal), $"The group is '{group}'.");
        Assert.IsType<RegexQuantifiedSyntax>(tree.GetRoot().Alternation.Branches[^1].Terms[^1]);
    }

    [Fact]
    public void AnUnterminatedGroupKeepsItsBranchesAndItsClass()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(a|b[c", RegexDialect.Net);

        Assert.Equal(["REGEX0003", "REGEX0002"], tree.GetDiagnostics().Select(d => d.Id));
        var group = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCapturingGroupSyntax>());
        Assert.True(group.CloseParenToken.IsMissing);
        Assert.Equal(2, group.Alternation.Branches.Count);
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCharacterClassSyntax>());
    }

    [Theory]
    [InlineData("net")]
    [InlineData("javascript")]
    [InlineData("pcre")]
    public void AnUnmatchedCloseParenthesisDoesNotEndThePattern(string dialectName)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));

        var tree = RegexSyntaxAssert.TextIsFaithful(@"a)b(c)\1", dialect);

        Assert.Equal("REGEX0001", Assert.Single(tree.GetDiagnostics()).Id);
        Assert.Equal(1, Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexBackreferenceSyntax>()).Number);
    }

    /// <summary>
    /// The pattern is read twice, once to number its groups and once to build the tree. Only the second reading
    /// reports, so a problem is never reported twice.
    /// </summary>
    [Theory]
    [InlineData("net", @"(?<a>x)\k<b>[z-a](?<a>y)\p{Nope}(")]
    [InlineData("javascript", @"(?<a>x)\k<b>[z-a](?<a>y)\p{Nope}(")]
    [InlineData("pcre", @"(?<a>x)\k<b>[z-a](?<a>y)\p{Nope}(?<=a+)(")]
    [InlineData("ere", @"[z-a](a\1)[[:nope:]]a{1(")]
    public void EveryProblemIsReportedOnce(string dialectName, string pattern)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));
        var options = new RegexParseOptions(dialect) { PatternOptions = dialect == RegexDialect.JavaScript ? RegexPatternOptions.Unicode : RegexPatternOptions.None };

        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        var diagnostics = tree.GetDiagnostics();
        Assert.True(diagnostics.Count >= 3, $"[{pattern}] reported {string.Join(", ", diagnostics.Select(d => d.Id))}");
        Assert.HasCount(diagnostics.Count, diagnostics.Select(d => (d.Id, d.Location.SourceSpan)).Distinct().ToArray());
    }

    [Fact]
    public void NestingBeyondTheConfiguredDepthIsReportedAndStillRoundTrips()
    {
        var pattern = new string('(', 300) + "a" + new string(')', 300);
        var options = new RegexParseOptions(RegexDialect.Net) { MaxRecursionDepth = 8 };

        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.Single(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "REGEX0200");
    }

    [Fact]
    public void DeeplyNestedInputDoesNotOverflowTheStack()
    {
        var pattern = new string('(', 5000) + "a" + new string(')', 5000);

        var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

        Assert.Equal(pattern, tree.GetRoot().ToFullString());
        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Fact]
    public void ALongChainOfTermsIsAcceptedAtAnyLength()
    {
        var pattern = new string('a', 20000);

        var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

        Assert.Equal(pattern, tree.GetRoot().ToFullString());
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void Diagnostics_AreLocatedInTheTreesOwnSourceText()
    {
        var tree = RegexSyntaxTree.ParseText("a(b", RegexDialect.Net);
        var diagnostic = tree.GetDiagnostics()[0];

        Assert.Same(tree.GetText(), diagnostic.Location.SourceText);
        Assert.Equal(0, diagnostic.Location.GetLineSpan().Start.Line);
    }
}
