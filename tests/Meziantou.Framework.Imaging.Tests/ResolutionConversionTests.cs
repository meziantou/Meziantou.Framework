using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

public sealed class ResolutionConversionTests
{
    [Fact]
    public void PngPhysConvertsPixelsPerMeterToDpi()
    {
        var resolution = ResolutionConversion.FromPngPhys(3780, 2835, 1);
        Assert.NotNull(resolution);
        Assert.Equal(3780 * 0.0254, resolution.HorizontalDpi);
        Assert.Equal(2835 * 0.0254, resolution.VerticalDpi);
        Assert.Equal(96.012, resolution.HorizontalDpi, 9);
    }

    [Theory]
    [InlineData(2835u, 2835u, (byte)0)] // aspect ratio only
    [InlineData(0u, 2835u, (byte)1)]
    [InlineData(2835u, 0u, (byte)1)]
    [InlineData(2835u, 2835u, (byte)2)] // unknown unit
    public void PngPhysWithoutPhysicalResolutionIsUnknown(uint x, uint y, byte unit) => Assert.Null(ResolutionConversion.FromPngPhys(x, y, unit));

    [Fact]
    public void PngPhysRoundTripsEveryEncodedValue()
    {
        for (uint ppm = 1; ppm < 200_000; ppm += 7)
        {
            Assert.Equal((ppm, ppm + 1), ResolutionConversion.ToPngPhys(ResolutionConversion.FromPngPhys(ppm, ppm + 1, 1)!));
        }

        Assert.Equal((ResolutionConversion.MaxPngInteger, 1u), ResolutionConversion.ToPngPhys(ResolutionConversion.FromPngPhys(ResolutionConversion.MaxPngInteger, 1, 1)!));
    }

    [Fact]
    public void PngPhysExportRoundsToNearestAndRejectsUnrepresentableValues()
    {
        Assert.Equal((3780u, 3780u), ResolutionConversion.ToPngPhys(new ImageResolution(96, 96))); // 3779.53 -> 3780
        Assert.Equal((2835u, 2835u), ResolutionConversion.ToPngPhys(new ImageResolution(72, 72))); // 2834.65 -> 2835
        Assert.Throws<UnsupportedImageFeatureException>(() => ResolutionConversion.ToPngPhys(new ImageResolution(0.001, 72))); // rounds to 0
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => ResolutionConversion.ToPngPhys(new ImageResolution(1e12, 72)));
        Assert.Equal(ImageFormat.Png, exception.Format);
    }

    [Fact]
    public void JfifDensityConvertsUnits()
    {
        Assert.Null(ResolutionConversion.FromJfifDensity(0, 1, 1)); // aspect ratio only
        Assert.Null(ResolutionConversion.FromJfifDensity(1, 0, 72));
        Assert.Null(ResolutionConversion.FromJfifDensity(3, 72, 72));
        var inch = ResolutionConversion.FromJfifDensity(1, 300, 150)!;
        Assert.Equal((300.0, 150.0), (inch.HorizontalDpi, inch.VerticalDpi));
        var centimeter = ResolutionConversion.FromJfifDensity(2, 118, 59)!;
        Assert.Equal((118 * 2.54, 59 * 2.54), (centimeter.HorizontalDpi, centimeter.VerticalDpi));
    }

    [Fact]
    public void JfifDensityRoundTripsAndPrefersExactUnits()
    {
        for (ushort density = 1; density < 5000; density += 13)
        {
            Assert.Equal(((byte)1, density, density), ResolutionConversion.ToJfifDensity(ResolutionConversion.FromJfifDensity(1, density, density)!));
            var fromCentimeters = ResolutionConversion.FromJfifDensity(2, density, density)!;
            var (units, x, y) = ResolutionConversion.ToJfifDensity(fromCentimeters);
            Assert.Equal(fromCentimeters, ResolutionConversion.FromJfifDensity(units, x, y)); // exact either way
        }

        Assert.Equal(((byte)1, (ushort)72, (ushort)73), ResolutionConversion.ToJfifDensity(new ImageResolution(72.4, 72.5))); // nearest dpi
        Assert.Throws<UnsupportedImageFeatureException>(() => ResolutionConversion.ToJfifDensity(new ImageResolution(65535.5, 72)));
        Assert.Throws<UnsupportedImageFeatureException>(() => ResolutionConversion.ToJfifDensity(new ImageResolution(0.4, 72)));
    }
}
