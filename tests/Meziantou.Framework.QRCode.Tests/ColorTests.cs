namespace Meziantou.Framework.Tests;

public class ColorTests
{
    [Theory]
    [InlineData("rgba(255,0,0,1)", 255, 255, 0, 0)]
    [InlineData("rgba(0,0,0,1)", 255, 0, 0, 0)]
    [InlineData("rgba(255,0,0,0)", 0, 255, 0, 0)]
    [InlineData("rgba(17,34,51,0.5)", 128, 17, 34, 51)]
    [InlineData("rgba(17,34,51,0.502)", 128, 17, 34, 51)]
    [InlineData("rgba(1,2,3,0.25)", 64, 1, 2, 3)]
    [InlineData("rgba(1,2,3,.5)", 128, 1, 2, 3)]
    public void Parse_Rgba_TreatsAlphaAsAFraction(string value, byte alpha, byte red, byte green, byte blue)
    {
        var color = Color.Parse(value);

        Assert.Equal(Color.FromArgb(alpha, red, green, blue), color);
    }

    [Theory]
    [InlineData("rgba(0,0,0,2)")]
    [InlineData("rgba(0,0,0,255)")]
    [InlineData("rgba(0,0,0,-1)")]
    [InlineData("rgba(0,0,0,abc)")]
    public void TryParse_RgbaWithAlphaOutsideZeroToOne_Fails(string value)
    {
        Assert.False(Color.TryParse(value, out _));
    }

    [Fact]
    public void Parse_RgbaWithAlphaOne_IsOpaque()
    {
        // rgba(r,g,b,1) is the most common way to write an opaque colour. Parsing the 1 as a
        // 0-255 byte produced a colour that is 99.6% transparent, which renders an invisible
        // QR code with no error raised anywhere.
        Assert.Equal(Color.Black, Color.Parse("rgba(0,0,0,1)"));
        Assert.Equal("#000000", Color.Parse("rgba(0,0,0,1)").ToCssString());
    }

    [Theory]
    [InlineData("rgb(255,0,0)", 255, 0, 0)]
    [InlineData("rgb(1, 2, 3)", 1, 2, 3)]
    public void Parse_Rgb_IsOpaque(string value, byte red, byte green, byte blue)
    {
        Assert.Equal(Color.FromRgb(red, green, blue), Color.Parse(value));
    }

    [Theory]
    [InlineData("#f00", 255, 255, 0, 0)]
    [InlineData("#8f00", 136, 255, 0, 0)]
    [InlineData("#ff0000", 255, 255, 0, 0)]
    [InlineData("#80ff0000", 128, 255, 0, 0)]
    public void Parse_Hex(string value, byte alpha, byte red, byte green, byte blue)
    {
        Assert.Equal(Color.FromArgb(alpha, red, green, blue), Color.Parse(value));
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#80ff0000")]
    [InlineData("rgba(17,34,51,0.502)")]
    public void ToCssString_RoundTripsThroughParse(string value)
    {
        var color = Color.Parse(value);

        Assert.Equal(color, Color.Parse(color.ToCssString()));
    }

    [Fact]
    public void Parse_InvalidValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => Color.Parse("not a color"));
    }
}
