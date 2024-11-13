using FluentAssertions;
using Meziantou.Framework.InlineSnapshotTesting.Utils;
using Xunit;

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
        actual.Should().Be(expected);
    }

    [Fact]
    public void CreateQuotedString()
    {
        var result = CSharpStringLiteral.Create("line1\nline2", CSharpStringFormats.Quoted, "    ", 0, "\n");
        result.Should().Be("\"line1\\nline2\"");
    }

    [Fact]
    public void CreateRawString()
    {
        var result = CSharpStringLiteral.Create("line1\nline2", CSharpStringFormats.Raw, "    ", 0, "\n");
        result.Should().Be("\"\"\"\n    line1\n    line2\n    \"\"\"");
    }

    [Fact]
    public void CreateRawStringWithEmptyLine()
    {
        var result = CSharpStringLiteral.Create("line1\n    \nline2", CSharpStringFormats.Raw, "    ", 0, "\n");
        result.Should().Be("\"\"\"\n    line1\n\n    line2\n    \"\"\"");
    }

    [Fact]
    public void CreateLeftAlignedRawString()
    {
        var result = CSharpStringLiteral.Create("line1\nline2", CSharpStringFormats.LeftAlignedRaw, "    ", 0, "\n");
        result.Should().Be("\"\"\"\nline1\nline2\n\"\"\"");
    }
}
