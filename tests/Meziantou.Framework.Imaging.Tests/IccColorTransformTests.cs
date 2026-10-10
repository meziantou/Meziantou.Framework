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
            IccColorTransform.Create(profile, identity).Convert([32768], actual);
            Assert.Equal(forward, actual[0]);
            IccColorTransform.Create(identity, profile).Convert([32768], actual);
            Assert.Equal(inverse, actual[0]);
        }
    }

    [Fact]
    public void InverseOfAParametricCurveCoversItsSegmentsAndItsJump()
    {
        // Type 3 with a jump: y = x/4 below 1/2 (up to 1/8), then x^2 from 1/2 (from 1/4): outputs in [1/8, 1/4] map to 1/2
        var identity = IccTestProfiles.Gray(IccTestProfiles.Curve());
        var jump = IccTestProfiles.Gray(IccTestProfiles.Parametric(3, 2.0, 1.0, 0.0, 0.25, 0.5));
        var transform = IccColorTransform.Create(identity, jump);
        var actual = new float[6];
        transform.Convert([0f, 0.0625f, 0.125f, 0.1875f, 0.25f, 0.5625f], actual);
        Assert.Equal([0f, 0.25f, 0.5f, 0.5f, 0.5f, 0.75f], actual);

        // Below the first output of the curve, and above its last one
        var offset = IccTestProfiles.Gray(IccTestProfiles.Parametric(2, 1.0, 0.5, 0.0, 0.25));
        IccColorTransform.Create(identity, offset).Convert([0f, 0.25f, 0.5f, 0.75f, 1f, 0.125f], actual);
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
        IccColorTransform.Create(identity, reversal).Convert([0, 16384, 32768, 49152], actual);
        Assert.Equal([0, 8192, 16384, 57343], actual);

        // A decreasing curve (1 - x) as source and as destination
        var decreasing = IccTestProfiles.Gray(IccTestProfiles.Curve(65535, 0));
        IccColorTransform.Create(decreasing, identity).Convert([0, 65535, 1000, 40000], actual);
        Assert.Equal([65535, 0, 64535, 25535], actual);
        IccColorTransform.Create(identity, decreasing).Convert([0, 65535, 1000, 40000], actual);
        Assert.Equal([65535, 0, 64535, 25535], actual);
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
