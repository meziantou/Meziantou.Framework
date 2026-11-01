using Meziantou.Framework.InlineSnapshotTesting.Utils;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class CSharpStringLiteralTests
{
    [Theory]
    [InlineData(CSharpStringFormats.Default, "line1", "\"line1\"")]
    [InlineData(CSharpStringFormats.Default, "line1\t", "@\"line1\t\"")]
    [InlineData(CSharpStringFormats.Default, "line1\nline2", "\"\"\"\n    line1\n    line2\n    \"\"\"")]
    public void ChooseFormat(CSharpStringFormats formats, string value, string expected)
    {
        var actual = CSharpStringLiteral.Create(value, formats, "    ", 0, "\n");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateQuotedString()
    {
        var result = CSharpStringLiteral.Create("line1\nline2", CSharpStringFormats.Quoted, "    ", 0, "\n");
        Assert.Equal("\"line1\\nline2\"", result);
    }

    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\u0085b")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    public void CreateQuotedString_EscapesNewLineCharacters(string value)
    {
        var result = CSharpStringLiteral.Create(value, CSharpStringFormats.Quoted, "    ", 0, "\n");

        var tree = CSharpSyntaxTree.ParseText("_ = " + result + ";");
        Assert.Empty(tree.GetDiagnostics());

        var literal = Assert.IsType<LiteralExpressionSyntax>(tree.GetRoot().DescendantNodes().Single(node => node is LiteralExpressionSyntax));
        Assert.Equal(value, literal.Token.ValueText);
    }

    [Theory]
    [InlineData("\u001b[31mred", "\"\\u001B[31mred\"")]
    [InlineData("a\u200Bb", "\"a\\u200Bb\"")]
    [InlineData("a\uFEFFb", "\"a\\uFEFFb\"")]
    [InlineData("a\uD83D\uDE00b", "\"a\uD83D\uDE00b\"")]
    [InlineData("line1\nline2\u0001", "\"line1\\nline2\\u0001\"")]
    [InlineData("line1\fline2", "\"line1\\fline2\"")]
    [InlineData("line1\nline2\u2028", "\"line1\\nline2\\u2028\"")]
    public void Create_InvisibleCharacters_UsesEscapedQuotedString(string value, string expected)
    {
        AssertEscapedQuotedString(value, expected);
    }

    // Attribute arguments are stored as UTF-8, which cannot represent an unpaired surrogate, so these cannot be InlineData.
    [Fact]
    public void Create_UnpairedSurrogates_UsesEscapedQuotedString()
    {
        AssertEscapedQuotedString("a\uD800b", "\"a\\uD800b\"");
        AssertEscapedQuotedString("a\uDC00b", "\"a\\uDC00b\"");
        AssertEscapedQuotedString("a\uD800", "\"a\\uD800\"");
    }

    private static void AssertEscapedQuotedString(string value, string expected)
    {
        var result = CSharpStringLiteral.Create(value, CSharpStringFormats.Default, "    ", 0, "\n");
        Assert.Equal(expected, result);

        var tree = CSharpSyntaxTree.ParseText("_ = " + result + ";");
        Assert.Empty(tree.GetDiagnostics());

        var literal = Assert.IsType<LiteralExpressionSyntax>(tree.GetRoot().DescendantNodes().Single(node => node is LiteralExpressionSyntax));
        Assert.Equal(value, literal.Token.ValueText);
    }

    [Fact]
    public void CreateRawString()
    {
        var result = CSharpStringLiteral.Create("line1\nline2", CSharpStringFormats.Raw, "    ", 0, "\n");
        Assert.Equal("\"\"\"\n    line1\n    line2\n    \"\"\"", result);
    }

    [Fact]
    public void CreateRawStringWithEmptyLine()
    {
        var result = CSharpStringLiteral.Create("line1\n    \nline2", CSharpStringFormats.Raw, "    ", 0, "\n");
        Assert.Equal("\"\"\"\n    line1\n\n    line2\n    \"\"\"", result);
    }

    [Fact]
    public void CreateLeftAlignedRawString()
    {
        var result = CSharpStringLiteral.Create("line1\nline2", CSharpStringFormats.LeftAlignedRaw, "    ", 0, "\n");
        Assert.Equal("\"\"\"\nline1\nline2\n\"\"\"", result);
    }

    [Fact]
    public void CreateRawString_DefaultComparer_NormalizesLineEndingsAndWhitespaceOnlyLines()
    {
        var result = CSharpStringLiteral.Create("line1\r\n  \nline2", CSharpStringFormats.Default, "    ", 0, "\n", SnapshotComparer.Default);
        Assert.Equal("\"\"\"\n    line1\n\n    line2\n    \"\"\"", result);
    }

    [Theory]
    [InlineData(CSharpStringFormats.Raw)]
    [InlineData(CSharpStringFormats.LeftAlignedRaw)]
    [InlineData(CSharpStringFormats.Verbatim)]
    [InlineData(CSharpStringFormats.Raw | CSharpStringFormats.Verbatim)]
    public void Create_UnpairedSurrogates_WithoutQuoted_Throws(CSharpStringFormats formats)
    {
        // Attribute arguments are stored as UTF-8, which cannot represent an unpaired surrogate
        foreach (var value in new[] { "a\uD800b", "a\uDC00b", "a\uD800", "\uDC00a", "line1\nline2\uD800" })
        {
            var ex = Assert.Throws<InlineSnapshotException>(() => CSharpStringLiteral.Create(value, formats, "    ", 0, "\n"));
            Assert.Contains("unpaired UTF-16 surrogate", ex.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(CSharpStringFormats.Quoted), ex.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(CSharpStringFormats.Raw)]
    [InlineData(CSharpStringFormats.LeftAlignedRaw)]
    [InlineData(CSharpStringFormats.Verbatim)]
    public void Create_PairedSurrogates_WithoutQuoted_RoundTrips(CSharpStringFormats formats)
    {
        AssertRoundTrip("a\uD83D\uDE00b", formats, "\n", SnapshotComparer.Ordinal);
    }

    [Theory]
    [InlineData("line1\r\nline2")]
    [InlineData("line1\rline2")]
    [InlineData("line1\nline2")]
    [InlineData("line1\n\tline2")]
    [InlineData("\tline1\nline2\t")]
    [InlineData("line1\n    \nline2")]
    [InlineData("line1\n\t\nline2")]
    [InlineData("line1\nline2\n")]
    [InlineData("line1\nline2\r\n")]
    [InlineData("line1\nline2\n\n")]
    [InlineData("line1\nline2\n  ")]
    [InlineData("line1\r\nline2\nline3\rline4")]
    [InlineData("line1\fline2")]
    [InlineData("line1\u2028line2")]
    public void Create_OrdinalComparer_RoundTrips(string value)
    {
        foreach (var eol in new[] { "\n", "\r\n" })
        {
            AssertRoundTrip(value, CSharpStringFormats.Default, eol, SnapshotComparer.Ordinal);
            AssertRoundTrip(value, CSharpStringFormats.Quoted | CSharpStringFormats.Raw, eol, SnapshotComparer.Ordinal);
            AssertRoundTrip(value, CSharpStringFormats.Quoted | CSharpStringFormats.LeftAlignedRaw, eol, SnapshotComparer.Ordinal);
            AssertRoundTrip(value, CSharpStringFormats.Quoted | CSharpStringFormats.Verbatim, eol, SnapshotComparer.Ordinal);
            AssertRoundTrip(value, CSharpStringFormats.Default, eol, SnapshotComparer.Default);
        }
    }

    [Theory]
    [InlineData("line1\nline2", "\n")]
    [InlineData("line1\r\nline2", "\r\n")]
    [InlineData("line1\n\tline2\n", "\n")]
    public void Create_OrdinalComparer_KeepsRawStringWhenLossless(string value, string eol)
    {
        var result = CSharpStringLiteral.Create(value, CSharpStringFormats.Default, "    ", 0, eol, SnapshotComparer.Ordinal);
        Assert.StartsWith("\"\"\"", result, StringComparison.Ordinal);
        AssertRoundTrip(value, CSharpStringFormats.Default, eol, SnapshotComparer.Ordinal);
    }

    [Theory]
    [InlineData("line1\r\nline2", "\n", "\"line1\\r\\nline2\"")]
    [InlineData("line1\nline2", "\r\n", "\"line1\\nline2\"")]
    [InlineData("line1\rline2", "\n", "\"line1\\rline2\"")]
    [InlineData("line1\nline2\n  ", "\n", "@\"line1\nline2\n  \"")]
    [InlineData("line1\n    \nline2", "\n", "@\"line1\n    \nline2\"")]
    public void Create_OrdinalComparer_FallsBackWhenRawStringIsLossy(string value, string eol, string expected)
    {
        var result = CSharpStringLiteral.Create(value, CSharpStringFormats.Default, "    ", 0, eol, SnapshotComparer.Ordinal);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(CSharpStringFormats.Raw)]
    [InlineData(CSharpStringFormats.LeftAlignedRaw)]
    [InlineData(CSharpStringFormats.Verbatim)]
    [InlineData(CSharpStringFormats.Raw | CSharpStringFormats.Verbatim)]
    public void Create_OrdinalComparer_WithoutQuoted_ThrowsWhenNoFormatIsExact(CSharpStringFormats formats)
    {
        var ex = Assert.Throws<InlineSnapshotException>(() => CSharpStringLiteral.Create("line1\r\nline2", formats, "    ", 0, "\n", SnapshotComparer.Ordinal));
        Assert.Contains(nameof(CSharpStringFormats.Quoted), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_OrdinalComparer_WithoutQuoted_UsesVerbatimForWhitespaceOnlyLines()
    {
        var result = CSharpStringLiteral.Create("line1\n    \nline2", CSharpStringFormats.Raw | CSharpStringFormats.Verbatim, "    ", 0, "\n", SnapshotComparer.Ordinal);
        Assert.Equal("@\"line1\n    \nline2\"", result);
    }

    private static void AssertRoundTrip(string value, CSharpStringFormats formats, string eol, SnapshotComparer comparer)
    {
        var literal = CSharpStringLiteral.Create(value, formats, "    ", 4, eol, comparer);

        // Save the generated code like the source file would be, convert its line endings like git or an editor may do,
        // then compile it again
        var source = "class Sample" + eol + "{" + eol + "    void M()" + eol + "    {" + eol + "        _ = " + literal + ";" + eol + "    }" + eol + "}" + eol;
        var savedSource = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(source)).ReplaceLineEndings(eol);
        var tree = CSharpSyntaxTree.ParseText(savedSource);
        Assert.Empty(tree.GetDiagnostics());

        var expression = Assert.IsType<LiteralExpressionSyntax>(tree.GetRoot().DescendantNodes().Single(node => node is LiteralExpressionSyntax));
        var compiledValue = expression.Token.ValueText;
        Assert.True(comparer.AreEqual(comparer.NormalizeValue(value), comparer.NormalizeValue(compiledValue)), $"The literal does not round-trip: {literal}");
    }
}
