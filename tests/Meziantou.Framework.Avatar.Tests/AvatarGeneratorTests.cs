using Xunit;

namespace Meziantou.Framework.Tests;

public class AvatarGeneratorTests
{
    [Theory]
    [InlineData("John Doe", "JD")]
    [InlineData("John Michael Doe", "JD")]
    [InlineData("JD", "JD")]
    [InlineData("John", "Jo")]
    [InlineData("J", "J")]
    public void CreateSvg_ExtractBigramFromName(string name, string expectedBigram)
    {
        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        Assert.Contains($">{expectedBigram}</text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSvg_UsesExplicitBigram()
    {
        var options = new AvatarOptions
        {
            Bigram = "aB",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains(">aB</text>", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("abC")]
    [InlineData("a b")]
    public void CreateSvg_ThrowsWhenExplicitBigramIsInvalid(string bigram)
    {
        var options = new AvatarOptions
        {
            Bigram = bigram,
        };

        Assert.Throws<ArgumentException>(() => AvatarGenerator.CreateSvg("John Doe", options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateSvg_ThrowsWhenNameIsInvalid(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => AvatarGenerator.CreateSvg(name!, new AvatarOptions()));
    }

    [Fact]
    public void CreateSvg_UsesNameForPaletteSelectionWhenBigramProvided()
    {
        var optionsWithFirstBigram = new AvatarOptions
        {
            Bigram = "XY",
        };
        optionsWithFirstBigram.Palette.Clear();
        optionsWithFirstBigram.Palette.Add(new AvatarColorPair("#010101", "#fefefe"));
        optionsWithFirstBigram.Palette.Add(new AvatarColorPair("#020202", "#ededed"));
        optionsWithFirstBigram.Palette.Add(new AvatarColorPair("#030303", "#dcdcdc"));

        var optionsWithSecondBigram = new AvatarOptions
        {
            Bigram = "AB",
        };
        optionsWithSecondBigram.Palette.Clear();
        optionsWithSecondBigram.Palette.Add(new AvatarColorPair("#010101", "#fefefe"));
        optionsWithSecondBigram.Palette.Add(new AvatarColorPair("#020202", "#ededed"));
        optionsWithSecondBigram.Palette.Add(new AvatarColorPair("#030303", "#dcdcdc"));

        var name = "John Michael Doe";
        var svg1 = AvatarGenerator.CreateSvg(name, optionsWithFirstBigram);
        var svg2 = AvatarGenerator.CreateSvg(name, optionsWithSecondBigram);

        Assert.Equal(GetBackgroundFill(svg1), GetBackgroundFill(svg2));
    }

    [Fact]
    public void CreateSvg_RendersRoundShape()
    {
        var options = new AvatarOptions
        {
            Shape = AvatarShape.Round,
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("<circle", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<rect", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSvg_RendersSquareShape()
    {
        var options = new AvatarOptions
        {
            Shape = AvatarShape.Square,
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("<rect", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<circle", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSvg_UsesDefaultSize()
    {
        var svg = AvatarGenerator.CreateSvg("John Doe", new AvatarOptions());

        Assert.Contains("width=\"64\"", svg, StringComparison.Ordinal);
        Assert.Contains("height=\"64\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSvg_UsesConfiguredSize()
    {
        var options = new AvatarOptions
        {
            Size = 128,
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("width=\"128\"", svg, StringComparison.Ordinal);
        Assert.Contains("height=\"128\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-size=\"64\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSvg_UsesMonospaceFont()
    {
        var svg = AvatarGenerator.CreateSvg("John Doe", new AvatarOptions());

        Assert.Contains("font-family=\"monospace\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSvg_ThrowsWhenSizeIsInvalid()
    {
        var options = new AvatarOptions
        {
            Size = 0,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => AvatarGenerator.CreateSvg("John Doe", options));
    }

    private static string GetBackgroundFill(string svg)
    {
        const string fill = "fill=\"";
        var startIndex = svg.IndexOf(fill, StringComparison.Ordinal);
        Assert.True(startIndex >= 0);

        startIndex += fill.Length;
        var endIndex = svg.IndexOf('"', startIndex);
        Assert.True(endIndex > startIndex);

        return svg[startIndex..endIndex];
    }
}
