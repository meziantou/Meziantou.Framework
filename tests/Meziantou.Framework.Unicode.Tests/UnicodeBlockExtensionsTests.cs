namespace Meziantou.Framework.Tests;

public sealed class UnicodeBlockExtensionsTests
{
    [Fact]
    public void ToDisplayString_AllEnumValuesReturnNonEmptyStrings()
    {
        var allBlocks = Enum.GetValues<UnicodeBlock>();

        foreach (var block in allBlocks)
        {
            var displayString = block.ToDisplayString();

            Assert.False(string.IsNullOrWhiteSpace(displayString), $"Block {block} returned empty display string");
        }
    }

    [Theory]
    [InlineData(UnicodeBlock.Unknown, "Unknown")]
    [InlineData(UnicodeBlock.BasicLatin, "Basic Latin")]
    [InlineData(UnicodeBlock.Latin1Supplement, "Latin-1 Supplement")]
    [InlineData(UnicodeBlock.LatinExtendedA, "Latin Extended-A")]
    [InlineData(UnicodeBlock.HanifiRohingya, "Hanifi Rohingya")]
    [InlineData(UnicodeBlock.TuluTigalari, "Tulu-Tigalari")]
    [InlineData(UnicodeBlock.MyanmarExtendedC, "Myanmar Extended-C")]
    [InlineData(UnicodeBlock.CjkUnifiedIdeographsExtensionJ, "CJK Unified Ideographs Extension J")]
    public void ToDisplayString_ReturnsExpectedValue(UnicodeBlock block, string expected)
    {
        var actual = block.ToDisplayString();

        Assert.Equal(expected, actual);
    }
}
