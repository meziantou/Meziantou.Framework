using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Unicode;

/// <summary>The full case mappings (<see cref="CaseMapping"/>): the simple mappings and the unconditional mappings of SpecialCasing.txt.</summary>
public sealed class CaseMappingTests
{
    [Theory]
    [InlineData(0x61, "A")]
    [InlineData(0xDF, "SS")]
    [InlineData(0xFB03, "FFI")]
    [InlineData(0x149, "\U000002BCN")]
    [InlineData(0x390, "\U00000399\U00000308\U00000301")]
    [InlineData(0x10428, "\U00010400")]
    [InlineData(0x41, "A")]
    [InlineData(0x31, "1")]

    // The simple mappings that the invariant culture does not have
    [InlineData(0x17F, "S")]
    [InlineData(0x131, "I")]
    public void ToUpper(int codePoint, string expected)
    {
        Span<char> buffer = stackalloc char[CaseMapping.MaxLength];
        var length = CaseMapping.ToUpper(codePoint, buffer);
        Assert.Equal(expected, buffer[..length].ToString());
    }

    [Theory]
    [InlineData(0xFB01, "Fi")]
    [InlineData(0xDF, "Ss")]
    [InlineData(0x1F80, "\U00001F88")]
    [InlineData(0x61, "A")]

    // The titlecase forms of the Latin digraphs, and the Georgian letters, which are their own titlecase
    [InlineData(0x1C6, "\U000001C5")]
    [InlineData(0x1F1, "\U000001F2")]
    [InlineData(0x10D0, "\U000010D0")]
    public void ToTitle(int codePoint, string expected)
    {
        Span<char> buffer = stackalloc char[CaseMapping.MaxLength];
        var length = CaseMapping.ToTitle(codePoint, buffer);
        Assert.Equal(expected, buffer[..length].ToString());
    }

    [Theory]
    [InlineData(0x130, "i\U00000307")]
    [InlineData(0x41, "a")]
    [InlineData(0x10400, "\U00010428")]
    public void ToLower(int codePoint, string expected)
    {
        Span<char> buffer = stackalloc char[CaseMapping.MaxLength];
        var length = CaseMapping.ToLower(codePoint, buffer);
        Assert.Equal(expected, buffer[..length].ToString());
    }

    [Fact]
    public void ChangesWhenCased()
    {
        Assert.True(CaseMapping.ChangesWhenUppercased('a'));
        Assert.True(CaseMapping.ChangesWhenUppercased(0xDF));
        Assert.True(CaseMapping.ChangesWhenUppercased(0xFB01));
        Assert.False(CaseMapping.ChangesWhenUppercased('A'));
        Assert.False(CaseMapping.ChangesWhenUppercased('1'));
        Assert.True(CaseMapping.ChangesWhenLowercased('A'));
        Assert.False(CaseMapping.ChangesWhenLowercased('a'));
        Assert.False(CaseMapping.ChangesWhenLowercased(0x5D0));
    }

    [Fact]
    public void MaxLength()
    {
        // No mapping is longer than MaxLength
        Span<char> buffer = stackalloc char[CaseMapping.MaxLength];
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint is >= 0xD800 and <= 0xDFFF)
                continue;

            Assert.InRange(CaseMapping.ToUpper(codePoint, buffer), 1, CaseMapping.MaxLength);
            Assert.InRange(CaseMapping.ToTitle(codePoint, buffer), 1, CaseMapping.MaxLength);
            Assert.InRange(CaseMapping.ToLower(codePoint, buffer), 1, CaseMapping.MaxLength);
        }
    }
}
