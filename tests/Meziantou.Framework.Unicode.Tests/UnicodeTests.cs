using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Tests;

public sealed class UnicodeTests
{
    [Fact]
    public void ReplaceConfusablesCharacters_ReplacesMappedCharacters()
    {
        var input = "\u0410\u0430\u03B1";

        var output = Unicode.ReplaceConfusablesCharacters(input);

        Assert.Equal("Aaa", output);
    }

    [Fact]
    public void ReplaceConfusablesCharacters_NoChange()
    {
        var input = "Hello";

        var output = Unicode.ReplaceConfusablesCharacters(input);

        Assert.Same(input, output);
    }

    [Fact]
    public void ReplaceConfusablesCharacters_CharOverload()
    {
        var output = Unicode.ReplaceConfusablesCharacters('\u0410');

        Assert.Equal("A", output);
    }

    [Fact]
    public void ReplaceConfusablesCharacters_CharOverload_IgnoresSurrogates()
    {
        var output = Unicode.ReplaceConfusablesCharacters('\uD800');

        Assert.Equal("\uD800", output);
    }

    [Fact]
    public void IsConfusableCharacter_ReturnsExpectedValue()
    {
        Assert.True(Unicode.IsConfusableCharacter(new Rune('\u0410')));
        Assert.False(Unicode.IsConfusableCharacter(new Rune('A')));
    }

    [Fact]
    public void GetCharacterInfo_ReturnsExpectedMetadata()
    {
        var info = Unicode.GetCharacterInfo(new Rune('A'));

        Assert.NotNull(info);
        Assert.Equal(new Rune('A'), info.Value.Rune);
        Assert.Equal("LATIN CAPITAL LETTER A", info.Value.Name);
        Assert.Equal(UnicodeCategory.UppercaseLetter, info.Value.Category);
        Assert.Equal(UnicodeBidirectionalCategory.LeftToRight, info.Value.BidiCategory);

        var digitInfo = Unicode.GetCharacterInfo(new Rune('0'));

        Assert.NotNull(digitInfo);
        Assert.Equal(0, digitInfo.Value.DecimalDigitValue);
        Assert.Equal(0, digitInfo.Value.DigitValue);
    }

    [Fact]
    public void GetCharacterInfo_CharOverload_HandlesSurrogates()
    {
        var info = Unicode.GetCharacterInfo('A');

        Assert.NotNull(info);
        Assert.Equal(new Rune('A'), info.Value.Rune);

        var surrogateInfo = Unicode.GetCharacterInfo('\uD800');

        Assert.Null(surrogateInfo);
    }

    [Fact]
    public void TryGetCharacterInfo_CharOverload_HandlesSurrogates()
    {
        Assert.True(Unicode.TryGetCharacterInfo('A', out var info));
        Assert.Equal(new Rune('A'), info.Rune);

        Assert.False(Unicode.TryGetCharacterInfo('\uD800', out var surrogateInfo));
        Assert.Equal(default, surrogateInfo);
    }
}
