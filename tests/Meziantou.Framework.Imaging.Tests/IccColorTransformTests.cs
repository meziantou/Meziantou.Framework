using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// ICC color conversion between profiles. Expected samples are literals computed outside the library, with 50-digit
/// decimal arithmetic, from the formulas of ICC.1:2022 (annex F.2 and F.3, sections 10.6 and 10.18) applied to the
/// s15Fixed16-rounded numbers stored in the profiles; none is within 1e-6 of a rounding boundary.
/// </summary>
public sealed class IccColorTransformTests
{
    private static IccProfile DisplayP3 => IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());

    // u8Fixed8 563 / 256 = 2.19921875
    private static IccProfile GrayGamma => IccTestProfiles.Gray(IccTestProfiles.Gamma(563));

    private static IccProfile SampledGray => IccTestProfiles.Gray(IccTestProfiles.Curve(0, 4096, 16384, 36864, 65535));

    /// <summary>For the tests of curves that do not start at black: their expected values are those of the curves alone.</summary>
    private static IccColorTransformOptions NoCompensation { get; } = new() { BlackPointCompensation = false };

    [Fact]
    public void BuiltInProfilesAreValidVersion4DisplayProfiles()
    {
        foreach (var (profile, colorSpace) in new[] { (IccProfile.Srgb, IccProfileColorSpace.Rgb), (IccProfile.SrgbGray, IccProfileColorSpace.Gray) })
        {
            Assert.Same(profile, colorSpace == IccProfileColorSpace.Rgb ? IccProfile.Srgb : IccProfile.SrgbGray);
            Assert.Equal(colorSpace, profile.ColorSpace);
            Assert.Equal(IccProfileClass.Display, profile.ProfileClass);
            Assert.Equal(new Version(4, 4, 0), profile.Version);
            Assert.Equal(IccRenderingIntent.Perceptual, profile.RenderingIntent);
            Assert.True(MetadataValidation.TryValidateIccProfile(profile.Data.Span, out _));
            Assert.Equal(0, profile.Data.Length % 4);

            // The profile connection space is CIEXYZ and the illuminant is D50 (ICC.1:2022 section 7.2.16)
            var data = profile.Data.Span;
            Assert.True(data.Slice(20, 4).SequenceEqual("XYZ "u8));
            Assert.Equal(0x0000F6D6, BinaryPrimitives.ReadInt32BigEndian(data[68..]));
            Assert.Equal(0x00010000, BinaryPrimitives.ReadInt32BigEndian(data[72..]));
            Assert.Equal(0x0000D32D, BinaryPrimitives.ReadInt32BigEndian(data[76..]));
        }

        // Untagged pixels are sRGB or sGray: the linear-light working space accepts the built-in profiles
        Assert.True(SrgbProfileRecognition.IsSrgb(IccProfile.Srgb, PixelFormat.Rgba32));
        Assert.True(SrgbProfileRecognition.IsSrgb(IccProfile.SrgbGray, PixelFormat.Gray16));
    }

    [Fact]
    public void BuiltInSrgbColorantsAreTheBradfordAdaptedPrimariesAndSumToTheIlluminant()
    {
        // IEC 61966-2-1 primaries and D65 white, adapted to D50 with the linear Bradford transform. The published values
        // adapt to the D50 of ASTM E308 (Z = 0.82521) while the ICC illuminant has Z = 0.8249, hence the tolerance
        double[][] expected = [[0.4360747, 0.2225045, 0.0139322], [0.3850649, 0.7168786, 0.0971045], [0.1430804, 0.0606169, 0.7141733]];
        string[] signatures = ["rXYZ", "gXYZ", "bXYZ"];
        var sum = new int[3];
        for (var i = 0; i < 3; i++)
        {
            var tag = GetTag(IccProfile.Srgb, signatures[i]);
            Assert.True(tag[..4].SequenceEqual("XYZ "u8));
            for (var component = 0; component < 3; component++)
            {
                var value = BinaryPrimitives.ReadInt32BigEndian(tag[(8 + (4 * component))..]);
                sum[component] += value;
                Assert.True(Math.Abs((value / 65536.0) - expected[i][component]) < 0.0005);
            }
        }

        Assert.Equal([0x0000F6D6, 0x00010000, 0x0000D32D], sum);
    }

    [Theory]
    [InlineData(255, 0, 0, 234, 51, 35)]
    [InlineData(0, 255, 0, 117, 251, 76)]
    [InlineData(0, 0, 255, 0, 0, 245)]
    [InlineData(255, 255, 255, 255, 255, 255)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(128, 128, 128, 128, 128, 128)]
    [InlineData(200, 100, 50, 187, 105, 62)]
    [InlineData(1, 2, 3, 1, 2, 3)]
    public void ConvertsSrgbToDisplayP3(byte r, byte g, byte b, byte expectedR, byte expectedG, byte expectedB)
    {
        var transform = IccColorTransform.Create(IccProfile.Srgb, DisplayP3);
        Assert.Equal(3, transform.SourceChannelCount);
        Assert.Equal(3, transform.DestinationChannelCount);
        var destination = new byte[3];
        transform.Convert([r, g, b], destination);
        Assert.Equal([expectedR, expectedG, expectedB], destination);
    }

    [Theory]
    [InlineData(255, 0, 0, 255, 0, 0)] // outside the sRGB gamut: clipped per channel
    [InlineData(0, 255, 0, 0, 255, 0)]
    [InlineData(0, 0, 255, 1, 0, 255)]
    [InlineData(255, 255, 255, 255, 255, 255)]
    [InlineData(128, 128, 128, 128, 128, 128)]
    [InlineData(200, 100, 50, 215, 93, 31)]
    public void ConvertsDisplayP3ToSrgb(byte r, byte g, byte b, byte expectedR, byte expectedG, byte expectedB)
    {
        var transform = IccColorTransform.Create(DisplayP3, IccProfile.Srgb);
        var destination = new byte[3];
        transform.Convert([r, g, b], destination);
        Assert.Equal([expectedR, expectedG, expectedB], destination);
    }

    [Fact]
    public void ConvertsSixteenBitAndFloatingPointSamplesWithoutGoingThroughEightBits()
    {
        var transform = IccColorTransform.Create(IccProfile.Srgb, DisplayP3);
        ushort[] source = [65535, 0, 0, 12345, 23456, 34567, 65535, 65535, 65535, 257, 514, 771];
        var destination = new ushort[source.Length];
        transform.Convert(source, destination);
        Assert.Equal([60127, 13124, 9094, 15033, 23185, 33666, 65535, 65535, 65535, 303, 505, 744], destination);

        var floats = new float[3];
        transform.Convert([0.25f, 0.5f, 0.75f], floats);
        Assert.True(Math.Abs(floats[0] - 0.3129205447076085) < 1e-6);
        Assert.True(Math.Abs(floats[1] - 0.49410537703248125) < 1e-6);
        Assert.True(Math.Abs(floats[2] - 0.7301380939246946) < 1e-6);

        // Floating-point samples are clipped to [0, 1] and a value that is not a number is read as 0
        transform.Convert([float.NaN, -1f, float.NegativeInfinity], floats);
        Assert.Equal([0f, 0f, 0f], floats);
        transform.Convert([2f, float.PositiveInfinity, 1.5f], floats);
        Assert.Equal([1f, 1f, 1f], floats);
    }

    [Fact]
    public void ConvertsMonochromeProfiles()
    {
        // curveType with one entry: y = x^(563/256), then the sRGB encoding
        AssertGray(GrayGamma, IccProfile.SrgbGray, [0, 1, 64, 128, 200, 255], [0, 0, 62, 129, 201, 255]);
        AssertGray(IccProfile.SrgbGray, GrayGamma, [0, 1, 64, 128, 200, 255], [0, 6, 66, 127, 199, 255]);

        // curveType with samples: linear interpolation, and its exact inverse as a destination
        AssertGray(SampledGray, IccProfile.SrgbGray, [0, 32, 100, 128, 200, 255], [0, 50, 114, 138, 207, 255]);
        AssertGray(IccProfile.SrgbGray, SampledGray, [0, 32, 100, 128, 200, 255], [0, 15, 86, 116, 193, 255]);

        // CIELAB connection space: the curve output is L* / 100 instead of Y (ICC.1:2022 annex F.2)
        var labGray = IccTestProfiles.Gray(IccTestProfiles.Curve(), "Lab ");
        AssertGray(labGray, IccProfile.SrgbGray, [0, 10, 64, 128, 255], [0, 14, 60, 119, 255]);
        AssertGray(IccProfile.SrgbGray, labGray, [0, 10, 64, 128, 255], [0, 7, 69, 137, 255]);

        static void AssertGray(IccProfile source, IccProfile destination, byte[] samples, byte[] expected)
        {
            var transform = IccColorTransform.Create(source, destination);
            Assert.Equal(1, transform.SourceChannelCount);
            Assert.Equal(1, transform.DestinationChannelCount);
            var actual = new byte[samples.Length];
            transform.Convert(samples, actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ConvertsBetweenMonochromeAndRgbProfiles()
    {
        // A gray value is the neutral color of the connection space with Y = curve(value): equal R, G and B in sRGB
        var toRgb = IccColorTransform.Create(GrayGamma, IccProfile.Srgb);
        Assert.Equal(1, toRgb.SourceChannelCount);
        Assert.Equal(3, toRgb.DestinationChannelCount);
        var rgb = new byte[12];
        toRgb.Convert([0, 64, 128, 255], rgb);
        Assert.Equal([0, 0, 0, 62, 62, 62, 129, 129, 129, 255, 255, 255], rgb);

        // RGB to gray keeps the luminance Y of the color (relative to D50), then applies the inverse curve
        var toGray = IccColorTransform.Create(IccProfile.Srgb, IccProfile.SrgbGray);
        Assert.Equal(3, toGray.SourceChannelCount);
        Assert.Equal(1, toGray.DestinationChannelCount);
        var gray = new byte[5];
        toGray.Convert([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 200, 100, 50], gray);
        Assert.Equal([130, 220, 70, 255, 130], gray);
    }

    [Fact]
    public void IdenticalProfilesCopyTheSamples()
    {
        // Same bytes in two instances: nothing is converted, so even a lossy curve keeps every sample
        var first = IccTestProfiles.Gray(IccTestProfiles.Curve(0, 0, 0, 65535));
        var second = IccTestProfiles.Gray(IccTestProfiles.Curve(0, 0, 0, 65535));
        var transform = IccColorTransform.Create(first, second);
        byte[] source = [0, 1, 2, 100, 254, 255];
        var destination = new byte[source.Length];
        transform.Convert(source, destination);
        Assert.Equal(source, destination);

        var floats = new float[2];
        IccColorTransform.Create(IccProfile.Srgb, IccProfile.Srgb).Convert([0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f], new float[6]);
        IccColorTransform.Create(first, second).Convert([0.25f, 0.75f], floats);
        Assert.Equal([0.25f, 0.75f], floats);
    }

    [Fact]
    public void ConvertsInPlaceWhenChannelCountsMatch()
    {
        var transform = IccColorTransform.Create(IccProfile.Srgb, DisplayP3);
        byte[] samples = [255, 0, 0, 200, 100, 50];
        transform.Convert(samples, samples);
        Assert.Equal([234, 51, 35, 187, 105, 62], samples);

        // Overlapping without being the same memory, or the same memory with different channel counts
        var buffer = new byte[12];
        Assert.Throws<ArgumentException>("destination", () => transform.Convert(buffer.AsSpan(0, 6), buffer.AsSpan(3, 6)));
        var toGray = IccColorTransform.Create(IccProfile.Srgb, IccProfile.SrgbGray);
        Assert.Throws<ArgumentException>("destination", () => toGray.Convert(buffer.AsSpan(0, 6), buffer.AsSpan(0, 2)));
        var toRgb = IccColorTransform.Create(IccProfile.SrgbGray, IccProfile.Srgb);
        Assert.Throws<ArgumentException>("destination", () => toRgb.Convert(buffer.AsSpan(0, 2), buffer.AsSpan(0, 6)));
    }

    [Fact]
    public void ValidatesArgumentsBeforeConverting()
    {
        Assert.Throws<ArgumentNullException>("source", () => IccColorTransform.Create(null!, IccProfile.Srgb));
        Assert.Throws<ArgumentNullException>("destination", () => IccColorTransform.Create(IccProfile.Srgb, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IccColorTransformOptions { Intent = (IccRenderingIntent)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new IccColorTransformOptions { Intent = (IccRenderingIntent)(-1) });

        var transform = IccColorTransform.Create(IccProfile.Srgb, IccProfile.SrgbGray);
        var destination = new byte[2];
        Assert.Throws<ArgumentException>("source", () => transform.Convert(new byte[5], destination));
        Assert.Throws<ArgumentException>("destination", () => transform.Convert(new byte[6], new byte[3]));
        Assert.Throws<ArgumentException>("destination", () => transform.Convert(new byte[6], new byte[1]));
        Assert.Throws<ArgumentException>("source", () => transform.Convert(new ushort[4], new ushort[1]));
        Assert.Throws<ArgumentException>("destination", () => transform.Convert(new float[3], new float[2]));
        Assert.Equal([0, 0], destination);

        // No color is a valid input
        transform.Convert(ReadOnlySpan<byte>.Empty, Span<byte>.Empty);
    }

    [Fact]
    public void MalformedProfilesAreInvalid()
    {
        var valid = IccTestProfiles.BuildBytes("GRAY", "XYZ ", [("kTRC", IccTestProfiles.Curve())]);
        AssertInvalid(new IccProfile(new MetadataBlob([1, 2, 3])));
        AssertInvalid(new IccProfile(new MetadataBlob(valid.AsSpan(0, valid.Length - 1))));

        // Wrong file signature, unknown connection space
        AssertInvalid(Modify(valid, data => data[36] = (byte)'x'));
        AssertInvalid(Modify(valid, data => "RGB "u8.CopyTo(data.AsSpan(20))));

        // Missing or malformed tags of the model
        AssertInvalid(IccTestProfiles.Build("GRAY", "XYZ "));
        AssertInvalid(IccTestProfiles.Build("GRAY", "XYZ ", ("kTRC", IccTestProfiles.Xyz(1, 1, 1))));
        AssertInvalid(IccTestProfiles.Build("GRAY", "XYZ ", ("kTRC", IccTestProfiles.Parametric(5, 1, 1, 1, 1, 1, 1, 1))));
        AssertInvalid(IccTestProfiles.Build("GRAY", "XYZ ", ("kTRC", IccTestProfiles.Parametric(3, 2.4, 1, 0))));
        AssertInvalid(IccTestProfiles.Build("GRAY", "XYZ ", ("kTRC", [.. "curv"u8, 0, 0, 0, 0, 0, 0, 0, 9, 0, 0])));
        AssertInvalid(IccTestProfiles.Build("RGB ", "XYZ ", ("rTRC", IccTestProfiles.Curve()), ("gTRC", IccTestProfiles.Curve()), ("bTRC", IccTestProfiles.Curve())));
        AssertInvalid(IccTestProfiles.Build("RGB ", "XYZ ", ("rXYZ", IccTestProfiles.Xyz(1, 0, 0)), ("gXYZ", IccTestProfiles.Xyz(0, 1, 0)), ("bXYZ", IccTestProfiles.Curve())));
        AssertInvalid(IccTestProfiles.Build("RGB ", "XYZ ", ("rXYZ", IccTestProfiles.Xyz(1, 0, 0)), ("gXYZ", IccTestProfiles.Xyz(0, 1, 0)), ("bXYZ", IccTestProfiles.Xyz(0, 0, 1))));

        // A matrix-based RGB profile cannot use the CIELAB connection space (ICC.1:2022 annex F.3)
        var srgbTags = IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.SrgbCurve()).Data.ToArray();
        AssertInvalid(Modify(srgbTags, data => "Lab "u8.CopyTo(data.AsSpan(20))));

        static IccProfile Modify(byte[] data, Action<byte[]> change)
        {
            var copy = (byte[])data.Clone();
            change(copy);
            return new IccProfile(new MetadataBlob(copy));
        }

        static void AssertInvalid(IccProfile profile)
        {
            var other = profile.ColorSpace == IccProfileColorSpace.Rgb ? IccProfile.Srgb : IccProfile.SrgbGray;
            Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(profile, other));
            Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(other, profile));
        }
    }

    [Fact]
    public void UnsupportedProfilesAreReportedAsUnsupported()
    {
        var gray = IccTestProfiles.BuildBytes("GRAY", "XYZ ", [("kTRC", IccTestProfiles.Curve())]);
        AssertUnsupported(Modify(gray, data => data[8] = 5));
        foreach (var profileClass in new[] { "link", "abst", "nmcl", "zzzz" })
        {
            AssertUnsupported(Modify(gray, data => Encoding.ASCII.GetBytes(profileClass).CopyTo(data, 12)));
        }

        foreach (var colorSpace in new[] { "Lab ", "XYZ ", "YCbr", "3CLR", "CMY " })
        {
            AssertUnsupported(Modify(gray, data => Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16)));
        }

        // Valid as a source, but the conversion from the connection space does not exist: a constant curve, colorants
        // that do not span a color space
        var constant = IccTestProfiles.Gray(IccTestProfiles.Curve(1000, 1000, 1000));
        _ = IccColorTransform.Create(constant, IccProfile.SrgbGray);
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(IccProfile.SrgbGray, constant));

        var singular = IccTestProfiles.Rgb([[0.4, 0.2, 0.1], [0.4, 0.2, 0.1], [0.1, 0.6, 0.7]], IccTestProfiles.Curve());
        _ = IccColorTransform.Create(singular, IccProfile.Srgb);
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(IccProfile.Srgb, singular));

        // Input, output and color space classes are accepted like display profiles
        foreach (var profileClass in new[] { "scnr", "prtr", "spac" })
        {
            var profile = Modify(gray, data => Encoding.ASCII.GetBytes(profileClass).CopyTo(data, 12));
            _ = IccColorTransform.Create(profile, IccProfile.SrgbGray);
            _ = IccColorTransform.Create(IccProfile.SrgbGray, profile);
        }

        static IccProfile Modify(byte[] data, Action<byte[]> change)
        {
            var copy = (byte[])data.Clone();
            change(copy);
            return new IccProfile(new MetadataBlob(copy));
        }

        static void AssertUnsupported(IccProfile profile)
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(profile, IccProfile.SrgbGray));
            Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(IccProfile.SrgbGray, profile));
        }
    }

    [Fact]
    public void ParametricCurvesFollowTheirDefinitionAndInvertExactly()
    {
        // ICC.1:2022 Table 68. Each curve is evaluated at 0.5 as a source and inverted as a destination; the other side is
        // the identity curve, so the 16-bit result is round(65535 * f(0.5)) with 0.5 stored as 32768 / 65535
        (int Function, double[] Parameters, ushort Forward, ushort Inverse)[] cases =
        [
            (0, [2.0], 16384, 46341), // x^2; sqrt(y)
            (1, [2.0, 0.5, 0.25], 16384, 59914), // (x/2 + 1/4)^2; 2 (sqrt(y) - 1/4)
            (2, [2.0, 0.5, 0.25, 0.125], 24576, 47497), // (x/2 + 1/4)^2 + 1/8; 2 (sqrt(y - 1/8) - 1/4)
            (3, [2.0, 1.0, 0.0, 0.5, 0.75], 16384, 49151), // x/2 below 3/4, x^2 from 3/4; the jump from 3/8 to 9/16 maps to 3/4
            (4, [2.0, 1.0, 0.0, 0.5, 0.25, 0.125, 0.0625], 24576, 40132), // x/2 + 1/16 below 1/4, x^2 + 1/8 from 1/4; sqrt(y - 1/8)
        ];

        var identity = IccTestProfiles.Gray(IccTestProfiles.Curve());
        foreach (var (function, parameters, forward, inverse) in cases)
        {
            var profile = IccTestProfiles.Gray(IccTestProfiles.Parametric((ushort)function, parameters));
            var actual = new ushort[1];
            IccColorTransform.Create(profile, identity, NoCompensation).Convert([32768], actual);
            Assert.Equal(forward, actual[0]);
            IccColorTransform.Create(identity, profile, NoCompensation).Convert([32768], actual);
            Assert.Equal(inverse, actual[0]);
        }
    }

    [Fact]
    public void InverseOfAParametricCurveCoversItsSegmentsAndItsJump()
    {
        // Type 3 with a jump: y = x/4 below 1/2 (up to 1/8), then x^2 from 1/2 (from 1/4): outputs in [1/8, 1/4] map to 1/2
        var identity = IccTestProfiles.Gray(IccTestProfiles.Curve());
        var jump = IccTestProfiles.Gray(IccTestProfiles.Parametric(3, 2.0, 1.0, 0.0, 0.25, 0.5));
        var transform = IccColorTransform.Create(identity, jump, NoCompensation);
        var actual = new float[6];
        transform.Convert([0f, 0.0625f, 0.125f, 0.1875f, 0.25f, 0.5625f], actual);
        Assert.Equal([0f, 0.25f, 0.5f, 0.5f, 0.5f, 0.75f], actual);

        // Below the first output of the curve, and above its last one
        var offset = IccTestProfiles.Gray(IccTestProfiles.Parametric(2, 1.0, 0.5, 0.0, 0.25));
        IccColorTransform.Create(identity, offset, NoCompensation).Convert([0f, 0.25f, 0.5f, 0.75f, 1f, 0.125f], actual);
        Assert.Equal([0f, 0f, 0.5f, 1f, 1f, 0f], actual);
    }

    [Fact]
    public void SampledCurvesInvertTheirRunningMaximumAndDecreasingCurves()
    {
        var identity = IccTestProfiles.Gray(IccTestProfiles.Curve());

        // A flat segment and a small reversal: entries 0, 0.5, 0.5, 0.25, 1 at inputs 0, 1/4, 1/2, 3/4, 1. The inverse is
        // the smallest input reaching the value on the running maximum 0, 0.5, 0.5, 0.5, 1
        var reversal = IccTestProfiles.Gray(IccTestProfiles.Curve(0, 32768, 32768, 16384, 65535));
        var actual = new ushort[4];
        IccColorTransform.Create(identity, reversal, NoCompensation).Convert([0, 16384, 32768, 49152], actual);
        Assert.Equal([0, 8192, 16384, 57343], actual);

        // A decreasing curve (1 - x) as source and as destination
        var decreasing = IccTestProfiles.Gray(IccTestProfiles.Curve(65535, 0));
        IccColorTransform.Create(decreasing, identity, NoCompensation).Convert([0, 65535, 1000, 40000], actual);
        Assert.Equal([65535, 0, 64535, 25535], actual);
        IccColorTransform.Create(identity, decreasing, NoCompensation).Convert([0, 65535, 1000, 40000], actual);
        Assert.Equal([65535, 0, 64535, 25535], actual);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Lookup tables. The expected values of the two interpolation tests were computed with exact fractions from the table
    // entries written in each test.
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A monochrome profile whose device value is L* / 100: converting to it reads the lightness of a color.</summary>
    private static IccProfile Lightness => IccTestProfiles.Gray(IccTestProfiles.Curve(), "Lab ");

    [Fact]
    public void DeviceLookupTablesAreInterpolatedOnTheSimplexOfTheMainDiagonal()
    {
        // lut8Type, RGB to CIELAB, 2 grid points per input: the L* entry (0-255) of each vertex, the first input (red)
        // varying least rapidly. Tetrahedral interpolation visits red, green and blue from the largest to the smallest:
        // (200, 100, 50) goes through the vertices 000, 100, 110, 111 with the weights 55, 100, 50, 50 (out of 255)
        byte[] lightness = [0, 50, 150, 180, 100, 120, 200, 255];
        var profile = IccTestProfiles.Lut("RGB ", "Lab ", ("A2B0", IccTestProfiles.Lut8(3, 3, 2, rgb => [lightness[(int)((4 * rgb[0]) + (2 * rgb[1]) + rgb[2])] / 255.0, 128 / 255.0, 128 / 255.0])));
        var transform = IccColorTransform.Create(profile, Lightness, new IccColorTransformOptions { BlackPointCompensation = false });
        (byte R, byte G, byte B, byte Expected8, ushort Expected16)[] cases =
        [
            (200, 100, 50, 128, 33007), // red > green > blue
            (200, 50, 100, 113, 28975), // red > blue > green
            (100, 200, 50, 148, 38046), // green > red > blue
            (50, 200, 100, 144, 37038), // green > blue > red
            (100, 50, 200, 93, 23936), // blue > red > green
            (50, 100, 200, 105, 26960), // blue > green > red
            (128, 128, 128, 128, 32896), // the diagonal: only the vertices 000 and 111
            (0, 0, 0, 0, 0),
            (255, 255, 255, 255, 65535),
            (255, 0, 0, 100, 25700),
            (0, 255, 255, 180, 46260),
            (128, 64, 191, 106, 27363),
        ];

        foreach (var (r, g, b, expected8, expected16) in cases)
        {
            var actual8 = new byte[1];
            transform.Convert([r, g, b], actual8);
            Assert.Equal(expected8, actual8[0]);

            var actual16 = new ushort[1];
            transform.Convert([(ushort)(r * 257), (ushort)(g * 257), (ushort)(b * 257)], actual16);
            Assert.Equal(expected16, actual16[0]);
        }
    }

    [Fact]
    public void LabLookupTablesAreInterpolatedMultilinearly()
    {
        // lut8Type, CIELAB to gray, 2 grid points per input: the gray entry of each vertex, L* varying least rapidly. A
        // neutral color has a* = b* = 0, encoded 128 / 255: every vertex contributes, with the products of the fractions
        // as weights (simplex interpolation would give 128, 109, 90, 107 and 120 instead)
        byte[] gray = [0, 40, 80, 255, 60, 200, 100, 180];
        var profile = IccTestProfiles.Lut("GRAY", "Lab ", ("B2A0", IccTestProfiles.Lut8(3, 1, 2, lab => [gray[(int)((4 * lab[0]) + (2 * lab[1]) + lab[2])] / 255.0])));
        var transform = IccColorTransform.Create(Lightness, profile, new IccColorTransformOptions { BlackPointCompensation = false });
        var actual8 = new byte[5];
        transform.Convert([0, 64, 128, 200, 255], actual8);
        Assert.Equal([94, 105, 115, 126, 135], actual8);

        var actual16 = new ushort[5];
        transform.Convert([(ushort)0, 64 * 257, 128 * 257, 200 * 257, 65535], actual16);
        Assert.Equal([24222, 26866, 29510, 32484, 34755], actual16);
    }

    [Fact]
    public void RenderingIntentSelectsTheLookupTableThenThePerceptualTableThenTheToneCurve()
    {
        // Constant tables: L* / 100 is 51 / 255 (perceptual, AToB0), 102 / 255 (colorimetric, AToB1), 153 / 255 (saturation, AToB2)
        static byte[] Constant(byte lightness) => IccTestProfiles.Lut8(1, 3, 2, _ => [lightness / 255.0, 128 / 255.0, 128 / 255.0]);
        var table0 = ("A2B0", Constant(51));
        var table1 = ("A2B1", Constant(102));
        var table2 = ("A2B2", Constant(153));

        // The tone curve x^2 gives L* / 100 = 0.25 for 0.5 (the profile connection space is CIELAB): 64 / 255
        var curve = ("kTRC", IccTestProfiles.Parametric(0, 2.0));

        Assert.Equal([51, 102, 153, 102], Convert(IccTestProfiles.Lut("GRAY", "Lab ", table0, table1, table2, curve)));
        Assert.Equal([51, 51, 153, 51], Convert(IccTestProfiles.Lut("GRAY", "Lab ", table0, table2)));
        Assert.Equal([51, 51, 51, 51], Convert(IccTestProfiles.Lut("GRAY", "Lab ", table0, curve)));
        Assert.Equal([64, 102, 64, 102], Convert(IccTestProfiles.Lut("GRAY", "Lab ", table1, curve)));
        Assert.Equal([64, 64, 153, 64], Convert(IccTestProfiles.Lut("GRAY", "Lab ", table2, curve)));

        // Tags may share their data
        var shared = Constant(77);
        Assert.Equal([77, 77, 153, 77], Convert(IccTestProfiles.Lut("GRAY", "Lab ", ("A2B0", shared), ("A2B1", shared), table2)));

        static byte[] Convert(IccProfile profile)
        {
            var result = new byte[4];
            IccRenderingIntent[] intents = [IccRenderingIntent.Perceptual, IccRenderingIntent.RelativeColorimetric, IccRenderingIntent.Saturation, IccRenderingIntent.AbsoluteColorimetric];
            for (var i = 0; i < intents.Length; i++)
            {
                var transform = IccColorTransform.Create(profile, Lightness, new IccColorTransformOptions { Intent = intents[i], BlackPointCompensation = false });
                transform.Convert([128], result.AsSpan(i, 1));
            }

            return result;
        }
    }

    [Fact]
    public void CmykProfilesConvertFourChannels()
    {
        var cmyk = IccTestProfiles.CmykLabLut16();
        var toRgb = IccColorTransform.Create(cmyk, IccProfile.Srgb);
        Assert.Equal(4, toRgb.SourceChannelCount);
        Assert.Equal(3, toRgb.DestinationChannelCount);
        var toCmyk = IccColorTransform.Create(IccProfile.Srgb, cmyk);
        Assert.Equal(3, toCmyk.SourceChannelCount);
        Assert.Equal(4, toCmyk.DestinationChannelCount);
        var betweenCmyk = IccColorTransform.Create(cmyk, IccTestProfiles.CmykLabLutAToB());
        Assert.Equal(4, betweenCmyk.SourceChannelCount);
        Assert.Equal(4, betweenCmyk.DestinationChannelCount);

        // In the device model of the perceptual tables of the test profile, no ink is white (up to the 16-bit rounding of
        // the table entries) and full black ink is black
        var rgb = new byte[6];
        IccColorTransform.Create(cmyk, IccProfile.Srgb, new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual }).Convert([0, 0, 0, 0, 0, 0, 0, 255], rgb);
        Assert.All(rgb[..3], value => Assert.True(value >= 254));
        Assert.Equal([0, 0, 0], rgb[3..]);

        // In place for CMYK to CMYK
        var samples = new ushort[] { 0, 0, 0, 65535, 1000, 2000, 3000, 4000 };
        betweenCmyk.Convert(samples, samples);
        Assert.Throws<ArgumentException>("source", () => toRgb.Convert(new byte[6], new byte[3]));
        Assert.Throws<ArgumentException>("destination", () => toCmyk.Convert(new byte[6], new byte[6]));
    }

    [Fact]
    public void MalformedLookupTablesAreInvalid()
    {
        var valid8 = IccTestProfiles.Lut8(3, 3, 2, rgb => rgb);
        var valid16 = IccTestProfiles.Lut16(3, 3, 2, rgb => rgb);
        _ = IccColorTransform.Create(RgbLab("A2B0", valid8), Lightness);
        _ = IccColorTransform.Create(RgbLab("A2B0", valid16), Lightness);

        // Grid points, channel counts, table sizes
        AssertInvalidSource(RgbLab("A2B0", Modify(valid8, data => data[10] = 0)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid8, data => data[10] = 1)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid8, data => data[10] = 3)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid8, data => data[8] = 4)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid8, data => data[9] = 4)));
        AssertInvalidSource(RgbLab("A2B0", valid8[..^1]));
        AssertInvalidSource(RgbLab("A2B0", valid8[..47]));
        AssertInvalidSource(RgbLab("A2B0", valid8[..11]));
        AssertInvalidSource(RgbLab("A2B0", valid16[..^1]));
        AssertInvalidSource(RgbLab("A2B0", valid16[..51]));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid16, data => data[49] = 1)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid16, data => (data[50], data[51]) = (0x10, 0x01))));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid16, data => (data[48], data[49]) = (0, 0))));

        // 255 grid points for 4 inputs would need 4 * 255^4 entries: rejected from the sizes, nothing is allocated
        var huge = IccTestProfiles.Lut16(4, 3, 2, cmyk => [cmyk[0], cmyk[1], cmyk[2]]);
        AssertInvalidSource(IccTestProfiles.Lut("CMYK", "Lab ", ("A2B0", Modify(huge, data => data[10] = 255))));

        // A tag of another type, an 8-bit table with the CIEXYZ connection space (it has no 8-bit encoding)
        AssertInvalidSource(RgbLab("A2B0", IccTestProfiles.Curve()));
        AssertInvalidSource(RgbLab("A2B0", IccTestProfiles.Xyz(1, 1, 1)));
        AssertInvalidSource(IccTestProfiles.Lut("RGB ", "XYZ ", ("A2B0", valid8)));
        Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(Lightness, IccTestProfiles.Lut("RGB ", "XYZ ", ("B2A0", valid8))));

        // A CMYK profile needs lookup tables: none to the connection space is invalid, none from it cannot be a destination
        AssertInvalidSource(IccTestProfiles.Lut("CMYK", "Lab "));
        var sourceOnly = IccTestProfiles.Lut("CMYK", "Lab ", ("A2B0", huge));
        _ = IccColorTransform.Create(sourceOnly, Lightness);
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(Lightness, sourceOnly));

        static IccProfile RgbLab(string signature, byte[] tag) => IccTestProfiles.Lut("RGB ", "Lab ", (signature, tag));

        static byte[] Modify(byte[] data, Action<byte[]> change)
        {
            var copy = (byte[])data.Clone();
            change(copy);
            return copy;
        }

        static void AssertInvalidSource(IccProfile profile)
            => Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(profile, Lightness));
    }

    [Fact]
    public void MalformedLutAToBTablesAreInvalid()
    {
        byte[][] curves = [IccTestProfiles.Curve(), IccTestProfiles.Curve(), IccTestProfiles.Curve()];
        (int[], int, Func<double[], double[]>) clut = ([2, 2, 2], 1, values => values);
        var valid = IccTestProfiles.LutAToB("mAB ", 3, 3, curvesA: curves, clut: clut, curvesM: curves, matrix: [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], curvesB: curves);
        _ = IccColorTransform.Create(RgbLab("A2B0", valid), Lightness);

        // Every element is optional: B curves only, or nothing at all, is the identity of three channels
        _ = IccColorTransform.Create(RgbLab("A2B0", IccTestProfiles.LutAToB("mAB ", 3, 3, curvesB: curves)), Lightness);
        _ = IccColorTransform.Create(RgbLab("A2B0", IccTestProfiles.LutAToB("mAB ", 3, 3)), Lightness);

        // The wrong type for the direction
        AssertInvalidSource(RgbLab("A2B0", IccTestProfiles.LutAToB("mBA ", 3, 3, curvesB: curves)));
        Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(Lightness, RgbLab("B2A0", valid)));

        // Element offsets outside the tag (B curves at 12, matrix at 16, M curves at 20, lookup table at 24, A curves at 28)
        foreach (var position in new[] { 12, 16, 20, 24, 28 })
        {
            AssertInvalidSource(RgbLab("A2B0", Modify(valid, data => (data[position], data[position + 1]) = (0x7F, 0xFF))));
            AssertInvalidSource(RgbLab("A2B0", Modify(valid, data => (data[position + 2], data[position + 3]) = ((byte)(data.Length >> 8), (byte)(data.Length - 1)))));
        }

        // Lookup table: entry size, grid points, truncated entries
        var clutOffset = valid[27];
        AssertInvalidSource(RgbLab("A2B0", Modify(valid, data => data[clutOffset + 16] = 0)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid, data => data[clutOffset + 16] = 3)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid, data => data[clutOffset + 1] = 1)));
        AssertInvalidSource(RgbLab("A2B0", Modify(valid, data => data[clutOffset] = 200)));

        // Channel counts: the header, and a missing lookup table when the counts differ
        AssertInvalidSource(RgbLab("A2B0", Modify(valid, data => data[8] = 4)));
        AssertInvalidSource(RgbLab("A2B0", valid[..31]));
        AssertInvalidSource(IccTestProfiles.Lut("CMYK", "Lab ", ("A2B0", IccTestProfiles.LutAToB("mAB ", 4, 3, curvesB: curves))));

        // A curve that is not a curve, or is truncated
        AssertInvalidSource(RgbLab("A2B0", IccTestProfiles.LutAToB("mAB ", 3, 3, curvesB: [IccTestProfiles.Curve(), IccTestProfiles.Xyz(0, 0, 0), IccTestProfiles.Curve()])));
        AssertInvalidSource(RgbLab("A2B0", IccTestProfiles.LutAToB("mAB ", 3, 3, curvesB: [IccTestProfiles.Curve(), IccTestProfiles.Curve()])));

        static IccProfile RgbLab(string signature, byte[] tag) => IccTestProfiles.Lut("RGB ", "Lab ", (signature, tag));

        static byte[] Modify(byte[] data, Action<byte[]> change)
        {
            var copy = (byte[])data.Clone();
            change(copy);
            return copy;
        }

        static void AssertInvalidSource(IccProfile profile)
            => Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(profile, Lightness));
    }

    [Fact]
    public void DegenerateCurvesNeverProduceInvalidSamples()
    {
        // A zero slope makes the breakpoint -b / a of a parametric curve infinite or undefined, a negative base makes the
        // power undefined: samples stay in range, and a destination is either usable or reported as unsupported
        byte[][] curves =
        [
            IccTestProfiles.Parametric(1, 2.0, 0.0, 0.5),
            IccTestProfiles.Parametric(1, 2.0, 0.0, 0.0),
            IccTestProfiles.Parametric(2, 0.5, 0.0, -0.5, 0.25),
            IccTestProfiles.Parametric(3, 0.5, -1.0, 0.25, 2.0, 0.5),
            IccTestProfiles.Parametric(4, 2.0, -1.0, 2.0, -1.0, 0.5, 0.5, 0.75),
            IccTestProfiles.Parametric(0, 0.0),
            IccTestProfiles.Parametric(0, -1.0),
            IccTestProfiles.Gamma(0),
            IccTestProfiles.Curve(65535, 0, 65535, 0),
        ];

        float[] samples = [0f, 0.001f, 0.25f, 0.5f, 0.75f, 0.999f, 1f];
        foreach (var curve in curves)
        {
            var profile = IccTestProfiles.Gray(curve);
            var converted = new float[samples.Length];
            IccColorTransform.Create(profile, IccProfile.SrgbGray).Convert(samples, converted);
            Assert.All(converted, value => Assert.True(value is >= 0f and <= 1f));

            try
            {
                IccColorTransform.Create(IccProfile.SrgbGray, profile).Convert(samples, converted);
                Assert.All(converted, value => Assert.True(value is >= 0f and <= 1f));
            }
            catch (UnsupportedImageFeatureException)
            {
                // A constant curve has no inverse
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Rendering intents and black point compensation. The test profile is a monochrome output profile whose darkest
    // device value is L* = 20 (tone curve from 0.2 to 1 with the CIELAB connection space) and whose media white point has
    // Y = 0.88 (57672 / 65536 as stored). Expected values: 50-digit decimal arithmetic outside the library.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void BlackPointCompensationMapsTheSourceBlackToTheDestinationBlack()
    {
        var paper = IccTestProfiles.GrayPaper();
        byte[] samples = [0, 1, 64, 128, 200, 255];

        // Without compensation the black of the paper (L* = 20, luminance 0.02989) stays a dark gray in sGray
        Assert.Equal([48, 49, 94, 145, 206, 255], Convert(paper, IccProfile.SrgbGray, IccRenderingIntent.RelativeColorimetric, compensation: false));

        // With compensation, luminance is scaled toward white so that 0.02989 becomes 0: out = (in - 0.02989) / (1 - 0.02989)
        Assert.Equal([0, 3, 83, 140, 204, 255], Convert(paper, IccProfile.SrgbGray, IccRenderingIntent.RelativeColorimetric, compensation: true));

        // The reverse: sGray black is lifted to the black of the paper instead of being clipped with the darkest grays
        Assert.Equal([0, 0, 23, 107, 193, 255], Convert(IccProfile.SrgbGray, paper, IccRenderingIntent.RelativeColorimetric, compensation: false));
        Assert.Equal([0, 0, 44, 115, 195, 255], Convert(IccProfile.SrgbGray, paper, IccRenderingIntent.RelativeColorimetric, compensation: true));

        // Compensation is the default, applies to the perceptual and saturation intents too (a matrix-based or monochrome
        // profile gives the same result for the three), and never to the absolute colorimetric intent
        var defaults = new byte[samples.Length];
        IccColorTransform.Create(paper, IccProfile.SrgbGray).Convert(samples, defaults);
        Assert.Equal([0, 3, 83, 140, 204, 255], defaults);
        Assert.Equal([0, 3, 83, 140, 204, 255], Convert(paper, IccProfile.SrgbGray, IccRenderingIntent.Perceptual, compensation: true));
        Assert.Equal([0, 3, 83, 140, 204, 255], Convert(paper, IccProfile.SrgbGray, IccRenderingIntent.Saturation, compensation: true));
        Assert.Equal(
            Convert(paper, IccProfile.SrgbGray, IccRenderingIntent.AbsoluteColorimetric, compensation: false),
            Convert(paper, IccProfile.SrgbGray, IccRenderingIntent.AbsoluteColorimetric, compensation: true));

        var words = new ushort[4];
        IccColorTransform.Create(paper, IccProfile.SrgbGray).Convert([(ushort)0, 1000, 32768, 65535], words);
        Assert.Equal([0, 2742, 35781, 65535], words);

        // Profiles whose blacks are both black: nothing changes
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        byte[] colors = [0, 0, 0, 200, 100, 50, 1, 2, 3];
        var with = new byte[colors.Length];
        var without = new byte[colors.Length];
        IccColorTransform.Create(IccProfile.Srgb, displayP3, new IccColorTransformOptions { BlackPointCompensation = true }).Convert(colors, with);
        IccColorTransform.Create(IccProfile.Srgb, displayP3, new IccColorTransformOptions { BlackPointCompensation = false }).Convert(colors, without);
        Assert.Equal(without, with);

        byte[] Convert(IccProfile source, IccProfile destination, IccRenderingIntent intent, bool compensation)
        {
            var result = new byte[samples.Length];
            IccColorTransform.Create(source, destination, new IccColorTransformOptions { Intent = intent, BlackPointCompensation = compensation }).Convert(samples, result);
            return result;
        }
    }

    [Fact]
    public void AbsoluteColorimetricIntentKeepsTheMediaWhitePoint()
    {
        // ICC.1:2022 section 6.3.2: the colors relative to the media white are scaled by the media white point. The white
        // of the paper (Y = 0.88) is therefore a light gray on a display, whose media white is the illuminant
        var paper = IccTestProfiles.GrayPaper();
        byte[] samples = [0, 1, 64, 128, 200, 255];
        var options = new IccColorTransformOptions { Intent = IccRenderingIntent.AbsoluteColorimetric };
        var converted = new byte[samples.Length];
        IccColorTransform.Create(paper, IccProfile.SrgbGray, options).Convert(samples, converted);
        Assert.Equal([45, 46, 89, 137, 195, 241], converted);

        // The reverse: display grays lighter than the paper are clipped to the paper white
        IccColorTransform.Create(IccProfile.SrgbGray, paper, options).Convert(samples, converted);
        Assert.Equal([0, 0, 29, 117, 207, 255], converted);

        // A display profile is viewed fully adapted: its media white point tag is not used
        var display = IccTestProfiles.Build("GRAY", "XYZ ", ("kTRC", IccTestProfiles.Curve()), ("wtpt", IccTestProfiles.Xyz(0.5, 0.5, 0.5)));
        var plain = IccTestProfiles.Build("GRAY", "XYZ ", ("kTRC", IccTestProfiles.Curve()));
        var expected = new byte[samples.Length];
        IccColorTransform.Create(display, IccProfile.SrgbGray, options).Convert(samples, converted);
        IccColorTransform.Create(plain, IccProfile.SrgbGray, options).Convert(samples, expected);
        Assert.Equal(expected, converted);

        // A media white point that is not a color is invalid, but only this intent reads it
        foreach (var white in new[] { IccTestProfiles.Xyz(0, 1, 1), IccTestProfiles.Xyz(0.9, -0.5, 0.8), IccTestProfiles.Curve() })
        {
            var broken = new IccProfile(new MetadataBlob(IccTestProfiles.BuildBytes("GRAY", "XYZ ", [("kTRC", IccTestProfiles.Curve()), ("wtpt", white)], static data => "prtr"u8.CopyTo(data.AsSpan(12)))));
            _ = IccColorTransform.Create(broken, IccProfile.SrgbGray);
            Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(broken, IccProfile.SrgbGray, options));
            Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(IccProfile.SrgbGray, broken, options));
        }
    }

    [Fact]
    public void BlackPointCompensationOfTableBasedDestinationsFollowsTheRoundTripOfALightnessRamp()
    {
        // A table-based monochrome destination whose device black is L* = 30: lightness = 30 + 70 * device (17 grid
        // points), and the inverse table, which clips darker colors to the device black. The round trip of an L* ramp is
        // constant up to 30 and the identity above, so the mid-range is straight and the black point is the device
        // black: 30, luminance 0.0623. sGray black must become device 0, and a compensated mid gray is lighter than
        // without compensation
        var printer = IccTestProfiles.Lut(
            "GRAY",
            "Lab ",
            ("A2B0", IccTestProfiles.Lut8(1, 3, 17, static gray => [0.3 + (0.7 * gray[0]), 128 / 255.0, 128 / 255.0])),
            ("B2A0", IccTestProfiles.Lut8(3, 1, 21, static lab => [Math.Clamp((lab[0] - 0.3) / 0.7, 0, 1)])));

        byte[] samples = [0, 30, 60, 128, 255];
        var with = new byte[samples.Length];
        var without = new byte[samples.Length];
        IccColorTransform.Create(IccProfile.SrgbGray, printer, new IccColorTransformOptions { BlackPointCompensation = true }).Convert(samples, with);
        IccColorTransform.Create(IccProfile.SrgbGray, printer, new IccColorTransformOptions { BlackPointCompensation = false }).Convert(samples, without);

        // Without compensation every gray darker than L* = 30 (sGray 71) is the device black. With compensation black
        // is the device black too (up to the 8-bit rounding of the table entries) and the dark grays are distinct
        Assert.Equal([0, 0, 0], without[..3]);
        Assert.True(with[0] <= 1);
        Assert.True(with[1] > with[0] && with[2] > with[1] && with[3] > without[3]);
        Assert.Equal(255, with[4]);
        Assert.Equal(255, without[4]);
    }

    private static ReadOnlySpan<byte> GetTag(IccProfile profile, string signature)
    {
        var data = profile.Data.Span;
        var count = BinaryPrimitives.ReadInt32BigEndian(data[128..]);
        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(132 + (12 * i), 12);
            if (entry[..4].SequenceEqual(Encoding.ASCII.GetBytes(signature)))
                return data.Slice(BinaryPrimitives.ReadInt32BigEndian(entry[4..]), BinaryPrimitives.ReadInt32BigEndian(entry[8..]));
        }

        throw new InvalidOperationException($"Tag '{signature}' not found.");
    }
}
