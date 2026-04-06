using System.Globalization;
using Meziantou.Framework.InlineSnapshotTesting;
using Xunit;

namespace Meziantou.Framework.Tests;

public class AvatarGeneratorTests
{
    [Fact]
    public void CreateSvg_ExtractBigramFromMultiWordName()
    {
        var svg = AvatarGenerator.CreateSvg("John Doe", new AvatarOptions());

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="JD"><circle cx="32" cy="32" r="32" fill="#cfdade"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#153037" font-family="monospace" font-weight="700" font-size="32">JD</text></svg>""");
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromThreeWordName()
    {
        var svg = AvatarGenerator.CreateSvg("John Michael Doe", new AvatarOptions());

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="JD"><circle cx="32" cy="32" r="32" fill="#1abc9c"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#080d14" font-family="monospace" font-weight="700" font-size="32">JD</text></svg>""");
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromTwoLetterWord()
    {
        var svg = AvatarGenerator.CreateSvg("JD", new AvatarOptions());

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="JD"><circle cx="32" cy="32" r="32" fill="#34495e"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#ffffff" font-family="monospace" font-weight="700" font-size="32">JD</text></svg>""");
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromSingleWord()
    {
        var svg = AvatarGenerator.CreateSvg("John", new AvatarOptions());

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="Jo"><circle cx="32" cy="32" r="32" fill="#27ae60"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#080d14" font-family="monospace" font-weight="700" font-size="32">Jo</text></svg>""");
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromSingleCharacter()
    {
        var svg = AvatarGenerator.CreateSvg("J", new AvatarOptions());

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="J"><circle cx="32" cy="32" r="32" fill="#2ecc71"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#080d14" font-family="monospace" font-weight="700" font-size="32">J</text></svg>""");
    }

    [Fact]
    public void CreateSvg_UsesExplicitBigram()
    {
        var options = new AvatarOptions
        {
            Bigram = "aB",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="aB"><circle cx="32" cy="32" r="32" fill="#cfdade"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#153037" font-family="monospace" font-weight="700" font-size="32">aB</text></svg>""");
    }

    [Fact]
    public void CreateSvg_ExtractBigram_UsesUnicodeComposedCharacter()
    {
        var svg = AvatarGenerator.CreateSvg("Éric Doe", new AvatarOptions());

        Assert.Equal("ÉD", GetRenderedBigram(svg));
    }

    [Fact]
    public void CreateSvg_ExtractBigram_UsesUnicodeDecomposedCharacterAsSingleTextElement()
    {
        var svg = AvatarGenerator.CreateSvg("E\u0301ric Doe", new AvatarOptions());
        var bigram = GetRenderedBigram(svg);

        Assert.Equal("E\u0301D", bigram);
        Assert.Equal(2, new StringInfo(bigram).LengthInTextElements);
    }

    [Fact]
    public void CreateSvg_ExtractBigram_HandlesGraphemeCluster()
    {
        var svg = AvatarGenerator.CreateSvg("👩🏽‍💻 Smith", new AvatarOptions());
        var bigram = GetRenderedBigram(svg);

        Assert.Equal("👩🏽‍💻S", bigram);
        Assert.Equal(2, new StringInfo(bigram).LengthInTextElements);
    }

    [Fact]
    public void CreateSvg_UsesExplicitBigram_GraphemeCluster()
    {
        var options = new AvatarOptions
        {
            Bigram = "👨‍👩‍👧‍👦",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);
        var bigram = GetRenderedBigram(svg);

        Assert.Equal("👨‍👩‍👧‍👦", bigram);
        Assert.Equal(1, new StringInfo(bigram).LengthInTextElements);
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
        var options = new AvatarOptions();

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="JD"><circle cx="32" cy="32" r="32" fill="#cfdade"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#153037" font-family="monospace" font-weight="700" font-size="32">JD</text></svg>""");
    }

    [Fact]
    public void CreateSvg_RendersSquareShape()
    {
        var options = new AvatarOptions();
        options.Shape = AvatarShape.Square;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="JD"><rect width="64" height="64" fill="#cfdade"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#153037" font-family="monospace" font-weight="700" font-size="32">JD</text></svg>""");
    }

    [Fact]
    public void CreateSvg_UsesDefaultSize()
    {
        var svg = AvatarGenerator.CreateSvg("John Doe", new AvatarOptions());

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="JD"><circle cx="32" cy="32" r="32" fill="#cfdade"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#153037" font-family="monospace" font-weight="700" font-size="32">JD</text></svg>""");
    }

    [Fact]
    public void CreateSvg_UsesConfiguredSize()
    {
        var options = new AvatarOptions();
        options.Size = 128;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        InlineSnapshot.Validate(svg, """<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128" role="img" aria-label="JD"><circle cx="64" cy="64" r="64" fill="#cfdade"/><text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" alignment-baseline="middle" dy=".05em" fill="#153037" font-family="monospace" font-weight="700" font-size="64">JD</text></svg>""");
    }

    [Fact]
    public void AvatarOptions_DefaultPaletteHasGoodContrast()
    {
        var options = new AvatarOptions();
        foreach (var pair in options.Palette)
        {
            var contrastRatio = GetContrastRatio(pair.BackgroundColor, pair.ForegroundColor);
            Assert.True(contrastRatio >= 4.5, $"Expected at least 4.5 contrast ratio for {pair.BackgroundColor}/{pair.ForegroundColor}, but got {contrastRatio:0.00}.");
        }
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

    private static string GetRenderedBigram(string svg)
    {
        var textStart = svg.IndexOf("<text ", StringComparison.Ordinal);
        Assert.True(textStart >= 0);

        textStart = svg.IndexOf('>', textStart);
        Assert.True(textStart >= 0);

        var textEnd = svg.IndexOf("</text>", textStart, StringComparison.Ordinal);
        Assert.True(textEnd > textStart);

        return svg[(textStart + 1)..textEnd];
    }

    private static double GetContrastRatio(string firstColor, string secondColor)
    {
        var firstLuminance = GetRelativeLuminance(firstColor);
        var secondLuminance = GetRelativeLuminance(secondColor);
        var brightest = Math.Max(firstLuminance, secondLuminance);
        var darkest = Math.Min(firstLuminance, secondLuminance);
        return (brightest + 0.05) / (darkest + 0.05);
    }

    private static double GetRelativeLuminance(string hexColor)
    {
        Assert.StartsWith("#", hexColor, StringComparison.Ordinal);
        Assert.Equal(7, hexColor.Length);

        var red = Convert.ToInt32(hexColor[1..3], fromBase: 16) / 255d;
        var green = Convert.ToInt32(hexColor[3..5], fromBase: 16) / 255d;
        var blue = Convert.ToInt32(hexColor[5..7], fromBase: 16) / 255d;
        return 0.2126 * ToLinear(red) + 0.7152 * ToLinear(green) + 0.0722 * ToLinear(blue);
    }

    private static double ToLinear(double component)
    {
        if (component <= 0.03928)
            return component / 12.92;

        return Math.Pow((component + 0.055) / 1.055, 2.4);
    }
}
