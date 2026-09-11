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
}
