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
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.Net);

        var diagnostic = Assert.Single(tree.Diagnostics, candidate => candidate.Id == id, $"[{pattern}] reported {string.Join(", ", tree.Diagnostics.Select(d => d.Id))}");
        Assert.Equal(spanStart, diagnostic.Span.Start);
        Assert.Equal(RegexDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void NestingBeyondTheConfiguredDepthIsReportedAndStillRoundTrips()
    {
        var pattern = new string('(', 300) + "a" + new string(')', 300);
        var options = new RegexParseOptions(RegexFlavor.Net) { MaxRecursionDepth = 8 };

        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.Single(tree.Diagnostics, diagnostic => diagnostic.Id == "REGEX0200");
    }

    [Fact]
    public void DeeplyNestedInputDoesNotOverflowTheStack()
    {
        var pattern = new string('(', 5000) + "a" + new string(')', 5000);

        var tree = RegexSyntaxTree.ParseText(pattern, RegexFlavor.Net);

        Assert.Equal(pattern, tree.Root.ToFullString());
        Assert.NotEmpty(tree.Diagnostics);
    }

    [Fact]
    public void ALongChainOfTermsIsAcceptedAtAnyLength()
    {
        var pattern = new string('a', 20000);

        var tree = RegexSyntaxTree.ParseText(pattern, RegexFlavor.Net);

        Assert.Equal(pattern, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
    }
}
