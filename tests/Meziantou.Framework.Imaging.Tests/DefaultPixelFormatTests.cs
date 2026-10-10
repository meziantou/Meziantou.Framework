using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>The default working representations of the library.</summary>
public sealed class DefaultPixelFormatTests
{
    [Theory]
    [InlineData((byte)0, (byte)1, false, false, PixelFormat.Gray8)]
    [InlineData((byte)0, (byte)2, false, false, PixelFormat.Gray8)]
    [InlineData((byte)0, (byte)4, false, false, PixelFormat.Gray8)]
    [InlineData((byte)0, (byte)8, false, false, PixelFormat.Gray8)]
    [InlineData((byte)0, (byte)16, false, false, PixelFormat.Gray16)]
    [InlineData((byte)0, (byte)8, true, false, PixelFormat.Rgba32)]
    [InlineData((byte)0, (byte)16, true, false, PixelFormat.Rgba64)]
    [InlineData((byte)2, (byte)8, false, false, PixelFormat.Rgb24)]
    [InlineData((byte)2, (byte)8, true, false, PixelFormat.Rgba32)]
    [InlineData((byte)2, (byte)16, false, false, PixelFormat.Rgba64)]
    [InlineData((byte)3, (byte)1, false, false, PixelFormat.Rgba32)]
    [InlineData((byte)3, (byte)8, true, false, PixelFormat.Rgba32)]
    [InlineData((byte)4, (byte)8, false, false, PixelFormat.Rgba32)]
    [InlineData((byte)4, (byte)16, false, false, PixelFormat.Rgba64)]
    [InlineData((byte)6, (byte)8, false, false, PixelFormat.Rgba32)]
    [InlineData((byte)6, (byte)16, false, false, PixelFormat.Rgba64)]
    [InlineData((byte)0, (byte)8, false, true, PixelFormat.Rgba32)]
    [InlineData((byte)2, (byte)8, false, true, PixelFormat.Rgba32)]
    [InlineData((byte)0, (byte)16, false, true, PixelFormat.Rgba64)]
    [InlineData((byte)2, (byte)16, false, true, PixelFormat.Rgba64)]
    public void PngDefaults(byte colorType, byte bitDepth, bool transparency, bool animated, PixelFormat expected)
        => Assert.Equal(expected, DefaultPixelFormats.ForPng(colorType, bitDepth, transparency, animated));

    [Fact]
    public void AnAnimatedCursorHasOnePixelFormatThatStoresEveryFrameLosslessly()
    {
        Assert.Equal(PixelFormat.Rgba32, DefaultPixelFormats.ForAni(hasSixteenBitFrame: false));
        Assert.Equal(PixelFormat.Rgba64, DefaultPixelFormats.ForAni(hasSixteenBitFrame: true));
    }

    [Fact]
    public void GifAndJpegDefaults()
    {
        Assert.Equal(PixelFormat.Rgba32, DefaultPixelFormats.Gif);
        Assert.Equal(PixelFormat.Gray8, DefaultPixelFormats.ForJpeg(1));
        Assert.Equal(PixelFormat.Rgb24, DefaultPixelFormats.ForJpeg(3));
    }
}
