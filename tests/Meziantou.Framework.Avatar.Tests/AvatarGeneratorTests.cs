using System.Xml;
using Meziantou.Framework.SnapshotTesting;
using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public class AvatarGeneratorTests
{
    [Fact]
    public void CreateSvg_ExtractBigramFromMultiWordName()
    {
        var svg = AvatarGenerator.CreateSvg("John Doe", new AvatarOptions());

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_UsesDefaultOptionsWhenNoneProvided()
    {
        Assert.Equal(AvatarGenerator.CreateSvg("John Doe", new AvatarOptions()), AvatarGenerator.CreateSvg("John Doe"));
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromThreeWordName()
    {
        var svg = AvatarGenerator.CreateSvg("John Michael Doe", new AvatarOptions());

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromTwoLetterWord()
    {
        var svg = AvatarGenerator.CreateSvg("JD", new AvatarOptions());

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromSingleWord()
    {
        var svg = AvatarGenerator.CreateSvg("John", new AvatarOptions());

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_ExtractBigramFromSingleCharacter()
    {
        var svg = AvatarGenerator.CreateSvg("J", new AvatarOptions());

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_UsesExplicitBigram()
    {
        var options = new AvatarOptions
        {
            Bigram = "aB",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Snapshot.Validate(svg, SnapshotType.Svg);
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
        options.Shape = AvatarShape.Round;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("<circle cx=\"32\" cy=\"32\" r=\"32\" fill=\"#cfdade\"/>", svg);
        Assert.DoesNotContain("<rect", svg);
    }

    [Fact]
    public void CreateSvg_RendersSquareShape()
    {
        var options = new AvatarOptions();
        options.Shape = AvatarShape.Square;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_RendersRoundedSquareShape()
    {
        var options = new AvatarOptions();
        options.Shape = AvatarShape.RoundedSquare;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_UsesDefaultSize()
    {
        var svg = AvatarGenerator.CreateSvg("John Doe", new AvatarOptions());

        Assert.Contains("width=\"64\" height=\"64\" viewBox=\"0 0 64 64\"", svg);
        Assert.Contains("font-size=\"32\"", svg);
    }

    [Fact]
    public void CreateSvg_UsesConfiguredSize()
    {
        var options = new AvatarOptions();
        options.Size = 128;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Snapshot.Validate(svg, SnapshotType.Svg);
    }

    [Fact]
    public void CreateSvg_UsesFractionalGeometryForOddSize()
    {
        var options = new AvatarOptions();
        options.Size = 65;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("width=\"65\" height=\"65\" viewBox=\"0 0 65 65\"", svg);
        Assert.Contains("cx=\"32.5\" cy=\"32.5\" r=\"32.5\"", svg);
        Assert.Contains("font-size=\"32.5\"", svg);
    }

    [Fact]
    public void CreateSvg_UsesFractionalCornerRadiusForOddSize()
    {
        var options = new AvatarOptions();
        options.Size = 65;
        options.Shape = AvatarShape.RoundedSquare;

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("rx=\"16.25\" ry=\"16.25\"", svg);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void CreateSvg_ThrowsWhenSizeIsInvalid(int size)
    {
        var options = new AvatarOptions
        {
            Size = size,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => AvatarGenerator.CreateSvg("John Doe", options));
    }

    [Fact]
    public void CreateSvg_ThrowsWhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AvatarGenerator.CreateSvg("John Doe", options: null!));
    }

    [Fact]
    public void CreateSvg_ThrowsWhenPaletteIsEmpty()
    {
        var options = new AvatarOptions();
        options.Palette.Clear();

        var exception = Assert.Throws<ArgumentException>(() => AvatarGenerator.CreateSvg("John Doe", options));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void CreateSvg_ThrowsWhenShapeIsNotSupported()
    {
        var options = new AvatarOptions
        {
            Shape = (AvatarShape)42,
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => AvatarGenerator.CreateSvg("John Doe", options));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void CreateSvg_ThrowsWhenPaletteEntryHasNoColor()
    {
        var options = new AvatarOptions();
        options.Palette.Clear();
        options.Palette.Add(default);

        var exception = Assert.Throws<ArgumentException>(() => AvatarGenerator.CreateSvg("John Doe", options));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void CreateSvg_ReportsInvalidBigramOnTheOptionsParameter()
    {
        var options = new AvatarOptions
        {
            Bigram = "abc",
        };

        var exception = Assert.Throws<ArgumentException>(() => AvatarGenerator.CreateSvg("John Doe", options));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void CreateSvg_EscapesMarkupInTheNameDerivedBigram()
    {
        var svg = AvatarGenerator.CreateSvg("<& Doe", new AvatarOptions());

        Assert.Contains("aria-label=\"&lt;D\"", svg);
        Assert.Contains(">&lt;D</text>", svg);
    }

    [Fact]
    public void CreateSvg_EscapesMarkupInTheExplicitBigram()
    {
        var options = new AvatarOptions
        {
            Bigram = "<\"",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("aria-label=\"&lt;&quot;\"", svg);
        Assert.Contains(">&lt;&quot;</text>", svg);
    }

    [Fact]
    public void CreateSvg_EscapesMarkupInPaletteColors()
    {
        var options = new AvatarOptions();
        options.Palette.Clear();
        options.Palette.Add(new AvatarColorPair("red\"/><script>x</script><x y=\"", "#fff"));

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("fill=\"red&quot;/&gt;&lt;script&gt;x&lt;/script&gt;&lt;x y=&quot;\"", svg);
        Assert.DoesNotContain("<script>", svg);
    }

    [Theory]
    [InlineData("John Doe")]
    [InlineData("<& Doe")]
    [InlineData("\u0007Bob Smith")]
    [InlineData("\u0001\u0002")]
    [InlineData("\uFFFEA Doe")]
    [InlineData("\U0001F469\U0001F3FD‍\U0001F4BB Smith")]
    public void CreateSvg_ProducesWellFormedXml(string name)
    {
        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        var document = new XmlDocument();
        document.LoadXml(svg);
    }

    [Theory]
    [InlineData("\u0007Bob Smith", "BS")]
    [InlineData("\u0000\u0000Bob Smith", "BS")]
    [InlineData("\uFFFEAlice Smith", "AS")]
    public void CreateSvg_RemovesCharactersThatAreInvalidInXmlFromTheName(string name, string expectedBigram)
    {
        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        Assert.Equal(expectedBigram, GetRenderedBigram(svg));
    }

    [Fact]
    public void CreateSvg_RemovesAnUnpairedSurrogateFromTheName()
    {
        // A name truncated by char count splits an emoji and leaves a lone surrogate.
        var svg = AvatarGenerator.CreateSvg("Bob \ud83d", new AvatarOptions());

        Assert.Equal("Bo", GetRenderedBigram(svg));

        var document = new XmlDocument();
        document.LoadXml(svg);
    }

    [Fact]
    public void CreateSvg_ThrowsWhenExplicitBigramContainsAnUnpairedSurrogate()
    {
        var options = new AvatarOptions
        {
            Bigram = "\ud800",
        };

        var exception = Assert.Throws<ArgumentException>(() => AvatarGenerator.CreateSvg("John Doe", options));
        Assert.Equal("options", exception.ParamName);
    }

    [Theory]
    [InlineData("\u0001")]
    [InlineData("A\u0001")]
    [InlineData("\uFFFE")]
    public void CreateSvg_ThrowsWhenExplicitBigramContainsCharactersInvalidInXml(string bigram)
    {
        var options = new AvatarOptions
        {
            Bigram = bigram,
        };

        var exception = Assert.Throws<ArgumentException>(() => AvatarGenerator.CreateSvg("John Doe", options));
        Assert.Equal("options", exception.ParamName);
    }

    [Theory]
    [InlineData("\u0001\u0002")]
    [InlineData("\u200B\u200B")]
    [InlineData("\u202E")]
    public void CreateSvg_RendersAPlaceholderWhenTheNameHasNothingVisible(string name)
    {
        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        Assert.Equal("?", GetRenderedBigram(svg));
    }

    [Theory]
    [InlineData("O'Brien", "OB")]
    [InlineData("Jean-Pierre", "JP")]
    [InlineData("Mary-Jane Watson", "MW")]
    [InlineData("J.R.R. Tolkien", "JT")]
    [InlineData("O’Brien", "OB")]
    [InlineData("van der Berg", "vB")]
    [InlineData("山田太郎", "山田")]
    public void CreateSvg_SplitsNamesOnConnectorsAsWellAsWhitespace(string name, string expectedBigram)
    {
        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        Assert.Equal(expectedBigram, GetRenderedBigram(svg));
    }

    [Theory]
    [InlineData("\u200BAlice Smith", "AS")]
    [InlineData("\u202EAlice Smith", "AS")]
    [InlineData("\u0301abc", "ab")]
    public void CreateSvg_SkipsZeroWidthAndFormatCharactersWhenPickingTheBigram(string name, string expectedBigram)
    {
        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        Assert.Equal(expectedBigram, GetRenderedBigram(svg));
    }

    [Fact]
    public void CreateSvg_ExtractBigram_TakesTwoGraphemeClustersFromASingleWord()
    {
        var svg = AvatarGenerator.CreateSvg("\U0001F469\U0001F3FD‍\U0001F4BB\U0001F600x", new AvatarOptions());
        var bigram = GetRenderedBigram(svg);

        Assert.Equal("\U0001F469\U0001F3FD‍\U0001F4BB\U0001F600", bigram);
        Assert.Equal(2, new StringInfo(bigram).LengthInTextElements);
    }

    [Fact]
    public void CreateSvg_ReducesAPathologicalGraphemeClusterToItsBaseCharacter()
    {
        var name = "A" + new string('\u0301', 100_000) + " Doe";

        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        Assert.Equal("AD", GetRenderedBigram(svg));
        Assert.HasCountLessThan(512, svg);
    }

    [Fact]
    public void CreateSvg_UsesTheAccessibleLabelWhenProvided()
    {
        var options = new AvatarOptions
        {
            AccessibleLabel = "John Doe",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("role=\"img\" aria-label=\"John Doe\"", svg);
        Assert.Equal("JD", GetRenderedBigram(svg));
    }

    [Fact]
    public void CreateSvg_RemovesCharactersThatAreInvalidInXmlFromTheAccessibleLabel()
    {
        var options = new AvatarOptions
        {
            AccessibleLabel = "John\u0001 Doe",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("aria-label=\"John Doe\"", svg);
        var document = new XmlDocument();
        document.LoadXml(svg);
    }

    [Fact]
    public void CreateSvg_HidesTheAvatarFromAssistiveTechnologiesWhenDecorative()
    {
        var options = new AvatarOptions
        {
            IsDecorative = true,
            AccessibleLabel = "ignored",
        };

        var svg = AvatarGenerator.CreateSvg("John Doe", options);

        Assert.Contains("aria-hidden=\"true\" focusable=\"false\"", svg);
        Assert.DoesNotContain("aria-label", svg);
        Assert.DoesNotContain("role=\"img\"", svg);
    }

    [Theory]
    [InlineData("John Doe", "#cfdade")]
    [InlineData("John Michael Doe", "#1abc9c")]
    [InlineData("JD", "#34495e")]
    [InlineData("John", "#27ae60")]
    [InlineData("J", "#2ecc71")]
    [InlineData("Jane Roe", "#1abc9c")]
    public void CreateSvg_MapsANameToAStableColor(string name, string expectedBackgroundColor)
    {
        // The name-to-color mapping is the package's contract. It changes if the hash algorithm,
        // the palette order, or the palette size changes.
        var svg = AvatarGenerator.CreateSvg(name, new AvatarOptions());

        Assert.Equal(expectedBackgroundColor, GetBackgroundFill(svg));
    }

    [Fact]
    public void CreateSvg_IgnoresSurroundingWhitespaceWhenSelectingTheColor()
    {
        var padded = AvatarGenerator.CreateSvg("  John Doe  ", new AvatarOptions());

        Assert.Equal(GetBackgroundFill(AvatarGenerator.CreateSvg("John Doe", new AvatarOptions())), GetBackgroundFill(padded));
    }

    [Fact]
    public void CreateSvg_UsesDifferentColorsForDifferentNames()
    {
        var options = new AvatarOptions();
        options.Palette.Clear();
        options.Palette.Add(new AvatarColorPair("#010101", "#fefefe"));
        options.Palette.Add(new AvatarColorPair("#020202", "#ededed"));
        options.Palette.Add(new AvatarColorPair("#030303", "#dcdcdc"));

        var first = GetBackgroundFill(AvatarGenerator.CreateSvg("John Doe", options));
        var second = GetBackgroundFill(AvatarGenerator.CreateSvg("Jane Roe", options));

        Assert.NotEqual(first, second);
    }

    [Fact]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void CreateSvg_UsesTheSameColorForComposedAndDecomposedNames()
    {
        // Normalization needs ICU. Under InvariantGlobalization these two names select different
        // palette entries, which is why this test does not run in that mode. See the readme.
        var composed = AvatarGenerator.CreateSvg("Éric Doe", new AvatarOptions());
        var decomposed = AvatarGenerator.CreateSvg("E\u0301ric Doe", new AvatarOptions());

        Assert.Equal(GetBackgroundFill(composed), GetBackgroundFill(decomposed));
    }


    private static string GetBackgroundFill(string svg)
    {
        const string Fill = "fill=\"";
        var startIndex = svg.IndexOf(Fill, StringComparison.Ordinal);
        Assert.True(startIndex >= 0);

        startIndex += Fill.Length;
        var endIndex = svg.IndexOf('"', startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex);

        return svg[startIndex..endIndex];
    }

    private static string GetRenderedBigram(string svg)
    {
        var textStart = svg.IndexOf("<text ", StringComparison.Ordinal);
        Assert.True(textStart >= 0);

        textStart = svg.IndexOf('>', textStart, StringComparison.Ordinal);
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
        Assert.StartsWith("#", hexColor);
        Assert.HasCount(7, hexColor);

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
