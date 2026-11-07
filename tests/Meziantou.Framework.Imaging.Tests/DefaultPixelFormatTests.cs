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
    [InlineData((byte)0, (byte)8, true, false, PixelFormat.La16)]
    [InlineData((byte)0, (byte)16, true, false, PixelFormat.La32)]
    [InlineData((byte)2, (byte)8, false, false, PixelFormat.Rgb24)]
    [InlineData((byte)2, (byte)8, true, false, PixelFormat.Rgba32)]
    [InlineData((byte)2, (byte)16, false, false, PixelFormat.Rgb48)]
    [InlineData((byte)2, (byte)16, true, false, PixelFormat.Rgba64)]
    [InlineData((byte)3, (byte)1, false, false, PixelFormat.Rgba32)]
    [InlineData((byte)3, (byte)8, true, false, PixelFormat.Rgba32)]
    [InlineData((byte)4, (byte)8, false, false, PixelFormat.La16)]
    [InlineData((byte)4, (byte)16, false, false, PixelFormat.La32)]
    [InlineData((byte)6, (byte)8, false, false, PixelFormat.Rgba32)]
    [InlineData((byte)6, (byte)16, false, false, PixelFormat.Rgba64)]
    [InlineData((byte)0, (byte)8, false, true, PixelFormat.Rgba32)]
    [InlineData((byte)2, (byte)8, false, true, PixelFormat.Rgba32)]
    [InlineData((byte)0, (byte)16, false, true, PixelFormat.Rgba64)]
    [InlineData((byte)2, (byte)16, false, true, PixelFormat.Rgba64)]
    [InlineData((byte)4, (byte)8, false, true, PixelFormat.Rgba32)]
    public void PngDefaults(byte colorType, byte bitDepth, bool transparency, bool animated, PixelFormat expected)
        => Assert.Equal(expected, DefaultPixelFormats.ForPng(colorType, bitDepth, transparency, animated));

    [Fact]
    public void AnAnimatedCursorHasOnePixelFormatThatStoresEveryFrameLosslessly()
    {
        Assert.Equal(PixelFormat.Rgba32, DefaultPixelFormats.ForAni(hasSixteenBitFrame: false));
        Assert.Equal(PixelFormat.Rgba64, DefaultPixelFormats.ForAni(hasSixteenBitFrame: true));
    }

    [Theory]
    [InlineData(true, false, false, PixelFormat.Gray8)]
    [InlineData(true, false, true, PixelFormat.Gray16)]
    [InlineData(false, false, false, PixelFormat.Rgb24)]
    [InlineData(false, true, false, PixelFormat.Rgba32)]
    [InlineData(true, true, false, PixelFormat.La16)]
    [InlineData(false, false, true, PixelFormat.Rgb48)]
    [InlineData(false, true, true, PixelFormat.Rgba64)]
    [InlineData(true, true, true, PixelFormat.La32)]
    public void JpegXlDefaults(bool isGray, bool hasAlpha, bool isSixteenBit, PixelFormat expected)
        => Assert.Equal(expected, DefaultPixelFormats.ForJpegXl(isGray, hasAlpha, isSixteenBit));

    [Theory]
    [InlineData(true, false, false, PixelFormat.Gray8)]
    [InlineData(true, false, true, PixelFormat.Gray16)]
    [InlineData(false, false, false, PixelFormat.Rgb24)]
    [InlineData(false, true, false, PixelFormat.Rgba32)]
    [InlineData(true, true, false, PixelFormat.La16)]
    [InlineData(false, false, true, PixelFormat.Rgb48)]
    [InlineData(false, true, true, PixelFormat.Rgba64)]
    [InlineData(true, true, true, PixelFormat.La32)]
    public void Jpeg2000Defaults(bool isGray, bool hasAlpha, bool isSixteenBit, PixelFormat expected)
        => Assert.Equal(expected, DefaultPixelFormats.ForJpeg2000(isGray, hasAlpha, isSixteenBit));

    [Fact]
    public void GifAndJpegDefaults()
    {
        Assert.Equal(PixelFormat.Rgba32, DefaultPixelFormats.Gif);
        Assert.Equal(PixelFormat.Gray8, DefaultPixelFormats.ForJpeg(1, precision: 8));
        Assert.Equal(PixelFormat.Rgb24, DefaultPixelFormats.ForJpeg(3, precision: 8));
        Assert.Equal(PixelFormat.Rgb24, DefaultPixelFormats.ForJpeg(4, precision: 8));

        // Deep color samples are scaled to 16 bits
        Assert.Equal(PixelFormat.Gray16, DefaultPixelFormats.ForJpeg(1, precision: 12));
        Assert.Equal(PixelFormat.Rgb48, DefaultPixelFormats.ForJpeg(3, precision: 12));
        Assert.Equal(PixelFormat.Rgb48, DefaultPixelFormats.ForJpeg(4, precision: 12));
    }

    [Theory]
    [InlineData(true, false, false, PixelFormat.Gray8)]
    [InlineData(true, true, false, PixelFormat.La16)]
    [InlineData(true, true, true, PixelFormat.La32)]
    [InlineData(false, false, true, PixelFormat.Rgb48)]
    [InlineData(false, true, true, PixelFormat.Rgba64)]
    public void NetpbmAndTiffDefaults(bool isGray, bool hasAlpha, bool is16Bit, PixelFormat expected)
    {
        Assert.Equal(expected, DefaultPixelFormats.ForPnm(isGray, hasAlpha, is16Bit));
        Assert.Equal(expected, DefaultPixelFormats.ForTiff(isGray, hasAlpha, is16Bit));
    }

    [Fact]
    public void AvifTgaDefaults()
    {
        Assert.Equal(PixelFormat.La16, DefaultPixelFormats.ForTga(isGrayscale: true, hasAlpha: true));
        Assert.Equal(PixelFormat.La16, DefaultPixelFormats.ForAvif(isMonochrome: true, hasAlpha: true, bitDepth: 8));
        Assert.Equal(PixelFormat.La32, DefaultPixelFormats.ForAvif(isMonochrome: true, hasAlpha: true, bitDepth: 10));
        Assert.Equal(PixelFormat.Rgb48, DefaultPixelFormats.ForHeic(isMonochrome: false, hasAlpha: false, bitDepth: 12));
    }

    [Fact]
    public void DecodersProduceTheRowsOfTheCompactFormatsInTheirWidenedLayout()
    {
        Assert.Equal(PixelFormat.Rgba32, PixelFormatWidening.GetWidenedFormat(PixelFormat.La16));
        Assert.Equal(PixelFormat.Rgba64, PixelFormatWidening.GetWidenedFormat(PixelFormat.La32));
        Assert.Equal(PixelFormat.Rgba64, PixelFormatWidening.GetWidenedFormat(PixelFormat.Rgb48));
        Assert.Equal(PixelFormat.Rgba32, PixelFormatWidening.GetWidenedFormat(PixelFormat.A8));
        Assert.Equal(PixelFormat.Gray16, PixelFormatWidening.GetWidenedFormat(PixelFormat.Gray16));
    }
}
