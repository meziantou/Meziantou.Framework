namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class SnapshotComparerTests
{
    [Fact]
    public void NormalizeLineEndingAndTabs()
    {
        var actual = SnapshotComparer.Default.NormalizeValue("ab\r\n  \n\tcd");
        Assert.Equal("ab\n\n    cd", actual);
    }

    [Theory]
    // Already normalized: LF only, no tabs, no whitespace-only lines
    [InlineData("", "")]
    [InlineData("ab", "ab")]
    [InlineData("ab\ncd", "ab\ncd")]
    [InlineData("ab\n\ncd", "ab\n\ncd")]
    [InlineData("ab\n", "ab\n")]
    [InlineData("a b  c", "a b  c")]
    // Line endings
    [InlineData("ab\r\ncd", "ab\ncd")]
    [InlineData("ab\rcd", "ab\ncd")]
    [InlineData("ab\u2028cd", "ab\ncd")]
    // Tabs become four spaces
    [InlineData("\tab", "    ab")]
    [InlineData("a\tb", "a    b")]
    // Whitespace-only lines keep their line break but lose their content
    [InlineData("ab\n  \ncd", "ab\n\ncd")]
    [InlineData("ab\n\t\ncd", "ab\n\ncd")]
    [InlineData("   ", "")]
    public void NormalizeValue_Cases(string value, string expected)
    {
        Assert.Equal(expected, SnapshotComparer.Default.NormalizeValue(value));
    }

    [Fact]
    public void NormalizeValue_ReturnsNullForNull()
    {
        Assert.Null(SnapshotComparer.Default.NormalizeValue(value: null));
    }

    [Fact]
    public void NormalizeValue_IsIdempotent()
    {
        foreach (var value in new[] { "ab\r\n  \n\tcd", "ab\ncd", "", "   ", "a\tb\r\n\r\n" })
        {
            var once = SnapshotComparer.Default.NormalizeValue(value);
            Assert.Equal(once, SnapshotComparer.Default.NormalizeValue(once));
        }
    }
}
