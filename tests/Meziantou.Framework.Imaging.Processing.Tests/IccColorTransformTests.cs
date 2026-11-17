using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
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
    public void BuiltInCmykProfileIsASourceOnlyInputProfile()
    {
        // CGATS001Compat-v2-micro.icc of the Compact ICC Profiles collection (CC0-1.0), stored unmodified
        var profile = IccProfile.Cgats001CompatibleCmyk;
        Assert.Same(profile, IccProfile.Cgats001CompatibleCmyk);
        Assert.Equal(8464, profile.Data.Length);
        Assert.Equal("73e1ba37d2bad5bab2a964f40a9eed96209666efc067c3322626214bbef234a0", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(profile.Data.Span)));
        Assert.Equal(IccProfileColorSpace.Cmyk, profile.ColorSpace);
        Assert.Equal(IccProfileClass.Input, profile.ProfileClass);
        Assert.Equal(2, profile.Version.Major);
        Assert.True(MetadataValidation.TryValidateIccProfile(profile.Data.Span, out _));

        // Its only table converts from the inks: every intent uses it, and it cannot be a destination
        var samples = new byte[] { 0, 0, 0, 0, 255, 255, 255, 255, 64, 0, 0, 0, 0, 128, 255, 32 };
        var perceptual = new byte[12];
        IccColorTransform.Create(profile, IccProfile.Srgb, new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual, BlackPointCompensation = false }).Convert(samples, perceptual);
        foreach (var intent in new[] { IccRenderingIntent.RelativeColorimetric, IccRenderingIntent.Saturation })
        {
            var converted = new byte[12];
            IccColorTransform.Create(profile, IccProfile.Srgb, new IccColorTransformOptions { Intent = intent, BlackPointCompensation = false }).Convert(samples, converted);
            Assert.Equal(perceptual, converted);
        }

        // No ink is the white of the paper, every ink is close to black, cyan alone is a light cyan
        Assert.True(perceptual.AsSpan(0, 3).IndexOfAnyExcept((byte)255) < 0, Convert.ToHexString(perceptual));
        Assert.All(perceptual[3..6], value => Assert.True(value < 64));
        Assert.True(perceptual[6] < perceptual[7] && perceptual[6] < perceptual[8]);

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(IccProfile.Srgb, profile));
        Assert.Contains("cannot be a destination", exception.Message, StringComparison.Ordinal);
    }

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

    [Fact]
    public void AProfileWrittenForTheSrgbColorSpaceIsTheBuiltInOne()
    {
        // IEC 61966-2-1: the primaries, the D65 white point and the transfer function
        var profile = BuiltInIccProfiles.TryCreate(gray: false, [0.64, 0.33, 0.30, 0.60, 0.15, 0.06], 0.3127, 0.3290, BuiltInIccCurve.Srgb, exponent: 1, renderingIntent: 0, "sRGB IEC61966-2.1");
        Assert.NotNull(profile);
        Assert.Equal(IccProfile.Srgb.Data.Length, profile.Data.Length);
        Assert.True(MetadataValidation.TryValidateIccProfile(profile.Data.Span, out _));
        Assert.True(SrgbProfileRecognition.IsSrgb(profile, PixelFormat.Rgb24));
        foreach (var signature in (string[])["wtpt", "rTRC", "gTRC", "bTRC", "desc", "cprt"])
        {
            Assert.True(GetTag(profile, signature).SequenceEqual(GetTag(IccProfile.Srgb, signature)), signature);
        }

        // The numbers computed from the chromaticities are those of the built-in profile, to the last digit or the one before
        foreach (var signature in (string[])["rXYZ", "gXYZ", "bXYZ", "chad"])
        {
            var expected = GetTag(IccProfile.Srgb, signature);
            var actual = GetTag(profile, signature);
            Assert.True(expected[..8].SequenceEqual(actual[..8]), signature);
            for (var offset = 8; offset < expected.Length; offset += 4)
            {
                Assert.True(Math.Abs(BinaryPrimitives.ReadInt32BigEndian(expected[offset..]) - BinaryPrimitives.ReadInt32BigEndian(actual[offset..])) <= 2, $"{signature} at {offset}");
            }
        }
    }

    [Fact]
    public void AMatrixBasedProfileConvertsFromTheConnectionSpaceWithItsInverseColorantsAndCurves()
    {
        var model = IccProfileModel.Create(DisplayP3, "test");
        Span<double> matrix = stackalloc double[9];
        Assert.True(model.TryGetMatrixShaperFromConnectionSpace(matrix, out var curves));
        Assert.HasCount(3, curves);

        // Each colorant converts back to a unit device value (the colorants are stored as s15Fixed16 numbers)
        var colorants = IccTestProfiles.DisplayP3Colorants;
        for (var column = 0; column < 3; column++)
        {
            for (var row = 0; row < 3; row++)
            {
                var value = (matrix[row * 3] * colorants[column][0]) + (matrix[(row * 3) + 1] * colorants[column][1]) + (matrix[(row * 3) + 2] * colorants[column][2]);
                Assert.True(Math.Abs(value - (row == column ? 1 : 0)) < 1e-4, $"row {row}, column {column}: {value}");
            }
        }

        // The sRGB curve, inverted: the linear light of 0.5 is 0.21404
        foreach (var curve in curves)
        {
            Assert.True(Math.Abs(curve.Evaluate(0.2140411) - 0.5) < 1e-5);
        }
    }

    [Fact]
    public void AMonochromeProfileConvertsTheLuminanceWithItsInverseCurve()
    {
        Span<double> matrix = stackalloc double[9];
        Assert.True(IccProfileModel.Create(GrayGamma, "test").TryGetMatrixShaperFromConnectionSpace(matrix, out var curves));
        Assert.Equal([1, 0, 0, 0, 1, 0, 0, 0, 1], matrix.ToArray());
        var curve = Assert.Single(curves);
        Assert.True(Math.Abs(curve.Evaluate(Math.Pow(0.5, 2.19921875)) - 0.5) < 1e-9);
    }

    [Fact]
    public void AProfileThatConvertsWithLookupTablesHasNoMatrixShaperConversion()
    {
        Span<double> matrix = stackalloc double[9];

        // Lookup tables take precedence over the colorants and curves of the same profile (ICC.1:2022 section 8.10.2)
        Assert.False(IccProfileModel.Create(IccTestProfiles.MatrixWithLookupTables(), "test").TryGetMatrixShaperFromConnectionSpace(matrix, out _));
        Assert.False(IccProfileModel.Create(IccTestProfiles.RgbXyzLut16(), "test").TryGetMatrixShaperFromConnectionSpace(matrix, out _));

        // A monochrome profile with CIELAB as connection space, and a profile without curves
        Assert.False(IccProfileModel.Create(IccTestProfiles.GrayPaper(), "test").TryGetMatrixShaperFromConnectionSpace(matrix, out _));
        Assert.False(IccProfileModel.Create(IccTestProfiles.RgbLabLut8(), "test").TryGetMatrixShaperFromConnectionSpace(matrix, out _));
    }

    [Fact]
    public void AProfileIsNotWrittenForChromaticitiesThatDefineNoColorSpace()
    {
        double[] srgb = [0.64, 0.33, 0.30, 0.60, 0.15, 0.06];
        Assert.NotNull(BuiltInIccProfiles.TryCreate(gray: false, srgb, 0.3127, 0.3290, BuiltInIccCurve.Power, exponent: 2.2, renderingIntent: 1, "Test"));

        // Primaries on a line, a white point without luminance, and numbers an s15Fixed16Number cannot hold
        Assert.Null(BuiltInIccProfiles.TryCreate(gray: false, [0.6, 0.3, 0.4, 0.3, 0.2, 0.3], 0.3127, 0.3290, BuiltInIccCurve.Srgb, exponent: 1, renderingIntent: 0, "Test"));
        Assert.Null(BuiltInIccProfiles.TryCreate(gray: false, srgb, 0.3127, 0, BuiltInIccCurve.Srgb, exponent: 1, renderingIntent: 0, "Test"));
        Assert.Null(BuiltInIccProfiles.TryCreate(gray: true, srgb, 0.3127, 0, BuiltInIccCurve.Srgb, exponent: 1, renderingIntent: 0, "Test"));
        Assert.Null(BuiltInIccProfiles.TryCreate(gray: false, srgb, 0.3127, 0.3290, BuiltInIccCurve.Power, exponent: 40000, renderingIntent: 0, "Test"));
        Assert.Null(BuiltInIccProfiles.TryCreate(gray: false, srgb, 0.3127, 0.3290, BuiltInIccCurve.Power, exponent: 0, renderingIntent: 0, "Test"));

        // The primaries are not used for grayscale
        Assert.NotNull(BuiltInIccProfiles.TryCreate(gray: true, [0.6, 0.3, 0.4, 0.3, 0.2, 0.3], 0.3127, 0.3290, BuiltInIccCurve.Bt709, exponent: 1, renderingIntent: 0, "Test"));
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
    public void FloatingPointSamplesOutsideTheUnitRangeAreKeptOnRequest()
    {
        // Two matrix-based profiles with the parametric sRGB curve. The red of Display P3 is outside the gamut of sRGB:
        // its linear sRGB components, from the colorants (columns of the matrices to the connection space), are encoded
        // with the sRGB function extended to the whole real line (mirrored below 0), as CSS Color 4 does
        var srgb = IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.SrgbCurve());
        var options = new IccColorTransformOptions { ClipFloatingPointSamples = false };
        Assert.True(IccColorTransformOptions.Default.ClipFloatingPointSamples);
        var toSrgb = IccColorTransform.Create(DisplayP3, srgb, options);
        var toDisplayP3 = IccColorTransform.Create(srgb, DisplayP3, options);

        var p3Red = IccTestProfiles.DisplayP3Colorants[0];
        var linear = Solve(IccTestProfiles.SrgbColorants, p3Red);
        var destination = new float[3];
        toSrgb.Convert([1f, 0f, 0f], destination);
        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(EncodeExtendedSrgb(linear[c]), destination[c], 1e-3);
        }

        Assert.True(destination[0] > 1 && destination[1] < 0 && destination[2] < 0, "the red of Display P3 has components outside [0, 1] in sRGB");

        // The default clips them
        var clipped = new float[3];
        IccColorTransform.Create(DisplayP3, srgb).Convert([1f, 0f, 0f], clipped);
        Assert.Equal([1f, 0f, 0f], clipped);

        // Light above the white stays above it, and a round trip gives the samples back
        float[] samples = [2f, 2f, 2f, -0.25f, 0.5f, 1.75f, 0.3f, 1.2f, -0.1f];
        var converted = new float[samples.Length];
        var back = new float[samples.Length];
        toDisplayP3.Convert(samples, converted);
        toSrgb.Convert(converted, back);
        Assert.Equal(2f, converted[0], 1e-3f);
        Assert.Equal(2f, converted[1], 1e-3f);
        Assert.Equal(2f, converted[2], 1e-3f);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i], back[i], 1e-4f);
        }

        // A value that is not a number is read as 0; the same profile keeps every sample
        toSrgb.Convert([float.NaN, 0f, 0f], destination);
        Assert.Equal([0f, 0f, 0f], destination);
        IccColorTransform.Create(srgb, srgb, options).Convert([1.5f, -0.5f, 3f], destination);
        Assert.Equal([1.5f, -0.5f, 3f], destination);

        // A sampled tone curve continues with the slope of its last interval, and its inverse with the inverse slope
        var sampled = IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.Curve(0, 16384, 65535));
        var throughTable = IccColorTransform.Create(sampled, srgb, options);
        var backThroughTable = IccColorTransform.Create(srgb, sampled, options);
        throughTable.Convert([1.5f, 1.5f, 1.5f], destination);
        backThroughTable.Convert(destination, back.AsSpan(0, 3));
        Assert.True(destination[0] > 1);
        for (var c = 0; c < 3; c++)
        {
            Assert.Equal(1.5f, back[c], 1e-4f);
        }

        // Grayscale profiles too
        var gray = IccColorTransform.Create(GrayGamma, IccProfile.SrgbGray, options);
        var grayResult = new float[1];
        gray.Convert([1.5f], grayResult);
        Assert.True(grayResult[0] > 1);

        static double EncodeExtendedSrgb(double value)
        {
            var magnitude = Math.Abs(value);
            var encoded = magnitude <= 0.0031308 ? 12.92 * magnitude : (1.055 * Math.Pow(magnitude, 1 / 2.4)) - 0.055;
            return Math.CopySign(encoded, value);
        }

        // Solves colorants (columns) x result = target, by Cramer's rule
        static double[] Solve(double[][] columns, double[] target)
        {
            static double Determinant(double[] a, double[] b, double[] c) => (a[0] * ((b[1] * c[2]) - (b[2] * c[1]))) - (b[0] * ((a[1] * c[2]) - (a[2] * c[1]))) + (c[0] * ((a[1] * b[2]) - (a[2] * b[1])));
            var determinant = Determinant(columns[0], columns[1], columns[2]);
            return [Determinant(target, columns[1], columns[2]) / determinant, Determinant(columns[0], target, columns[2]) / determinant, Determinant(columns[0], columns[1], target) / determinant];
        }
    }

    [Fact]
    public void FloatingPointSamplesOutsideTheUnitRangeNeedMatrixBasedProfiles()
    {
        // A lookup table has no values outside its grid: such a conversion cannot keep the samples, and says so
        var options = new IccColorTransformOptions { ClipFloatingPointSamples = false };
        foreach (var (source, destination) in new[] { (IccTestProfiles.RgbLabLut8(), IccProfile.Srgb), (IccProfile.Srgb, IccTestProfiles.CmykLabLut16()) })
        {
            IccColorTransform.Create(source, destination);
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(source, destination, options));
            Assert.Equal("Unbounded conversion through a lookup table", exception.Feature);
        }

        // Nor can a gamut mapping, which brings the colors into the range of the destination, even between matrix-based profiles
        var mapped = new IccColorTransformOptions { ClipFloatingPointSamples = false, GamutMapping = GamutMapping.ReduceChroma };
        IccColorTransform.Create(DisplayP3, IccProfile.Srgb, options);
        IccColorTransform.Create([DisplayP3, IccProfile.Srgb], options);
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(DisplayP3, IccProfile.Srgb, mapped));
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create([DisplayP3, IccProfile.Srgb], mapped));
    }

    [Fact]
    public void GainMapMetadataValidatesItsValuesAndComputesTheWeight()
    {
        GainMapChannel[] channel = [new GainMapChannel(-1, 2, 1, 0, 0)];
        Assert.Throws<ArgumentException>("channels", () => new GainMapMetadata(0, 1, []));
        Assert.Throws<ArgumentException>("channels", () => new GainMapMetadata(0, 1, [channel[0], channel[0]]));
        Assert.Throws<ArgumentNullException>("channels", () => new GainMapMetadata(0, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>("baseHdrHeadroom", () => new GainMapMetadata(-1, 1, channel));
        Assert.Throws<ArgumentOutOfRangeException>("alternateHdrHeadroom", () => new GainMapMetadata(0, double.PositiveInfinity, channel));
        Assert.Throws<ArgumentOutOfRangeException>("gamma", () => new GainMapChannel(0, 1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>("gainMapMax", () => new GainMapChannel(1, 0, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>("baseOffset", () => new GainMapChannel(0, 1, 1, double.NaN, 0));

        var metadata = new GainMapMetadata(1, 3, channel);
        Assert.Equal(new GainMapMetadata(1, 3, [new GainMapChannel(-1, 2, 1, 0, 0)]), metadata);
        Assert.Equal(new GainMapMetadata(1, 3, [new GainMapChannel(-1, 2, 1, 0, 0)]).GetHashCode(), metadata.GetHashCode());
        Assert.NotEqual(new GainMapMetadata(1, 3, channel, useBaseColorSpace: false), metadata);
        Assert.Equal(0, metadata.GetWeight(0));
        Assert.Equal(0, metadata.GetWeight(1));
        Assert.Equal(0.5, metadata.GetWeight(2));
        Assert.Equal(1, metadata.GetWeight(10));
        Assert.Throws<ArgumentOutOfRangeException>("hdrHeadroom", () => metadata.GetWeight(-0.5));

        // A high dynamic range base rendition: the weight decreases with the headroom of the display
        var hdrBase = new GainMapMetadata(3, 1, channel);
        Assert.Equal(1, hdrBase.GetWeight(0));
        Assert.Equal(0.5, hdrBase.GetWeight(2));
        Assert.Equal(0, hdrBase.GetWeight(4));

        // Equal headrooms: the alternate rendition from that headroom on
        var equal = new GainMapMetadata(2, 2, channel);
        Assert.Equal(0, equal.GetWeight(1.9));
        Assert.Equal(1, equal.GetWeight(2));
    }

    [Fact]
    public void GainMapApplicationFollowsIso21496()
    {
        // Three channels of their own, a gamma and offsets; a gain map of 2x1 samples over a base image of 4x1 pixels,
        // sampled at the centers of the pixels (bilinear, clamped at the edges)
        var metadata = new GainMapMetadata(0, 2,
        [
            new GainMapChannel(-1, 3, 2, 0.015625, 0.03125),
            new GainMapChannel(0, 2, 1, 0, 0),
            new GainMapChannel(-0.5, 1.5, 0.5, 0.25, 0),
        ]);
        using var baseImage = new Image<Rgba32>(4, 1, default);
        Rgba32[] colors = [new(255, 128, 0, 255), new(10, 200, 90, 128), new(64, 64, 64, 0), new(0, 0, 255, 255)];
        for (var x = 0; x < 4; x++)
        {
            baseImage.Frames[0][x, 0] = colors[x];
        }

        using var gainMap = new Image<Rgb24>(2, 1, default);
        gainMap.Frames[0][0, 0] = new Rgb24(0, 51, 255);
        gainMap.Frames[0][1, 0] = new Rgb24(255, 204, 102);
        double[] positions = [0, 0.25, 0.75, 1]; // where each pixel center falls between the two samples

        foreach (var headroom in new[] { 0.0, 0.5, 1, 2, 5 })
        {
            using var result = metadata.Apply(baseImage, gainMap, headroom);
            Assert.Equal(new Size(4, 1), result.Size);
            Assert.Equal(ColorTransferFunction.Linear, result.Metadata.TransferFunction);
            Assert.Null(result.Metadata.Cicp);
            var weight = Math.Min(headroom / 2, 1);
            for (var x = 0; x < 4; x++)
            {
                var pixel = result.Frames[0][x, 0];
                ReadOnlySpan<byte> baseSamples = [colors[x].R, colors[x].G, colors[x].B];
                ReadOnlySpan<byte> first = [0, 51, 255];
                ReadOnlySpan<byte> second = [255, 204, 102];
                ReadOnlySpan<float> actual = [pixel.R, pixel.G, pixel.B];
                for (var c = 0; c < 3; c++)
                {
                    var channel = metadata.Channels[c];
                    var g = ((first[c] * (1 - positions[x])) + (second[c] * positions[x])) / 255;
                    var gain = channel.GainMapMin + ((channel.GainMapMax - channel.GainMapMin) * Math.Pow(g, 1 / channel.Gamma));
                    var linear = baseSamples[c] / 255.0 is var v && v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                    var expected = ((linear + channel.BaseOffset) * Math.Pow(2, gain * weight)) - channel.AlternateOffset;
                    Assert.Equal(expected, actual[c], 1e-5 * Math.Max(1, Math.Abs(expected)));
                }

                Assert.Equal(colors[x].A / 255f, pixel.A, 1e-6f);
            }
        }
    }

    [Fact]
    public void GainMapApplicationNeedsAKnownLinearColorSpace()
    {
        var metadata = new GainMapMetadata(0, 2, [new GainMapChannel(0, 2, 1, 0, 0)]);
        using var gainMap = new Image<Gray8>(2, 2, new Gray8(128));
        using var baseImage = new Image<Rgb24>(2, 2, new Rgb24(100, 150, 200));

        // The primaries of the description label the result: Display P3, as a CICP description or as an ICC profile
        baseImage.Metadata.Cicp = new CicpColorDescription(12, 13, 0, fullRange: true);
        using (var result = metadata.Apply(baseImage, gainMap, 2))
            Assert.Equal(new CicpColorDescription(12, 8, 0, fullRange: true), result.Metadata.Cicp);

        baseImage.Metadata.Cicp = null;
        baseImage.Metadata.IccProfile = DisplayP3;
        using (var result = metadata.Apply(baseImage, gainMap, 2))
        {
            Assert.Equal(new CicpColorDescription(12, 8, 0, fullRange: true), result.Metadata.Cicp);
            Assert.Null(result.Metadata.IccProfile);
            var expected = Math.Pow((150 / 255.0 + 0.055) / 1.055, 2.4) * Math.Pow(2, 2 * 128 / 255.0);
            Assert.Equal(expected, result.Frames[0][1, 1].G, 2e-3);
        }

        // High dynamic range samples, primaries that are not those of a CICP description, a gain map in the color space of
        // the alternate rendition
        baseImage.Metadata.IccProfile = null;
        baseImage.Metadata.Cicp = new CicpColorDescription(9, 16, 0, fullRange: true);
        Assert.Throws<UnsupportedImageFeatureException>(() => metadata.Apply(baseImage, gainMap, 2));
        baseImage.Metadata.Cicp = null;
        baseImage.Metadata.IccProfile = IccTestProfiles.Rgb([[0.6, 0.3, 0.01], [0.25, 0.65, 0.1], [0.11, 0.05, 0.71]], IccTestProfiles.SrgbCurve());
        Assert.Throws<UnsupportedImageFeatureException>(() => metadata.Apply(baseImage, gainMap, 2));
        baseImage.Metadata.IccProfile = null;
        Assert.Throws<UnsupportedImageFeatureException>(() => new GainMapMetadata(0, 2, metadata.Channels, useBaseColorSpace: false).Apply(baseImage, gainMap, 2));
        Assert.Throws<ArgumentOutOfRangeException>("hdrHeadroom", () => metadata.Apply(baseImage, gainMap, double.NaN));
        Assert.Throws<ArgumentNullException>("gainMap", () => metadata.Apply(baseImage, null!, 1));

        // A grayscale base image gives gray colors
        using var grayBase = new Image<Gray8>(2, 2, new Gray8(200));
        using var gray = metadata.Apply(grayBase, gainMap, 2);
        var pixel = gray.Frames[0][0, 0];
        Assert.Equal(pixel.R, pixel.G);
        Assert.Equal(pixel.R, pixel.B);
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
        AssertUnsupported(Modify(gray, data => data[8] = 6));
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
    public void CmykLookupTablesAreInterpolatedMultilinearly()
    {
        // lut8Type, CMYK to CIELAB, 2 grid points per input: the L* entry of each of the 16 vertices, cyan varying least
        // rapidly and black most rapidly. The neutral axis of CMYK is not the diagonal of the cell: every vertex
        // contributes, with the product of the fractions as weight (simplex interpolation would give 127 for the
        // third color, 148 for the fourth)
        byte[] lightness = [255, 40, 200, 30, 180, 25, 150, 20, 160, 22, 130, 18, 110, 15, 90, 0];
        var profile = IccTestProfiles.Lut("CMYK", "Lab ", ("A2B0", IccTestProfiles.Lut8(4, 3, 2, cmyk => [lightness[(int)((8 * cmyk[0]) + (4 * cmyk[1]) + (2 * cmyk[2]) + cmyk[3])] / 255.0, 128 / 255.0, 128 / 255.0])));
        var transform = IccColorTransform.Create(profile, Lightness, new IccColorTransformOptions { BlackPointCompensation = false });
        byte[] samples = [0, 0, 0, 0, 255, 255, 255, 255, 128, 128, 128, 128, 200, 100, 50, 25, 25, 50, 100, 200, 64, 0, 255, 128, 0, 0, 0, 128];
        var actual8 = new byte[7];
        transform.Convert(samples, actual8);
        Assert.Equal([255, 0, 90, 140, 71, 104, 147], actual8);

        var actual16 = new ushort[7];
        transform.Convert(samples.Select(static value => (ushort)(value * 257)).ToArray(), actual16);
        Assert.Equal([65535, 0, 23091, 35955, 18243, 26832, 37799], actual16);
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

        // A tag too short to have a type (found by fuzzing: the type selects the encoding before the tag is parsed), a tag
        // of another type, an 8-bit table with the CIEXYZ connection space (it has no 8-bit encoding)
        AssertInvalidSource(RgbLab("A2B0", []));
        AssertInvalidSource(RgbLab("A2B0", [0x6D, 0x66, 0x74]));
        Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(Lightness, RgbLab("B2A0", [])));
        Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(Lightness, RgbLab("B2A0", [0x6D, 0x66, 0x74])));
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

    [Fact]
    public void Version4PerceptualReferenceMediumBlackIsMappedByBlackPointCompensationOnly()
    {
        // The perceptual tables of a version 4 profile give device black the black of the perceptual reference medium,
        // L* = 3.1373 (ICC White Paper 26); version 2 tables usually give it L* = 0, and so do matrix-based profiles.
        // With compensation, the estimated black points map one to the other in both directions. Without it, nothing
        // adjusts them, whatever the versions: the reference medium black stays a dark gray in a version 2 or
        // matrix-based destination, and the darkest grays of such a source are clipped to the device black of a version
        // 4 one. ICC White Paper 26 recommends against connecting version 2 and version 4 perceptual tables, and says
        // that without compensation the reference medium black is left unchanged.
        var version4 = PerceptualPrinter(version: 4, perceptualBlack: 3.1373);
        var version2 = PerceptualPrinter(version: 2, perceptualBlack: 0);
        ushort[] samples = [0, 257, 1028, 4112, 16448, 65535];

        // Expected values: LittleCMS 2.17 (double precision, no optimization), which reads the same tables. Tolerance:
        // 24 units of 16 bits (a tenth of an 8-bit level; the differences measured are at most 13), for the interpolation
        // of the tables; the reference medium black moves black by more than 2000 units
        AssertClose([0, 253, 994, 4046, 17381, 65533], Convert(version4, version2, compensation: true));
        AssertClose([3, 265, 1067, 4186, 15533, 65533], Convert(version2, version4, compensation: true));
        AssertClose([5, 368, 1431, 5055, 16114, 65535], Convert(version4, IccProfile.SrgbGray, compensation: true));
        AssertClose([3, 190, 731, 3156, 16820, 65533], Convert(IccProfile.SrgbGray, version4, compensation: true));

        // Without compensation toward a version 2 destination, LittleCMS does not adjust the black either
        AssertClose([2060, 2312, 3053, 6046, 17989, 65533], Convert(version4, version2, compensation: false));

        // LittleCMS forces compensation for a version 4 destination with the perceptual and saturation intents; here
        // the expected values are those of the tables alone (computed outside the library from their formulas): the
        // lightness 3.1373 + 96.8627 * device in sGray, and the lightness 100 * device of version 2 in version 4
        AssertClose([2927, 3246, 4096, 6657, 16654, 65535], Convert(version4, IccProfile.SrgbGray, compensation: false));
        AssertClose([0, 0, 0, 2123, 14858, 65535], Convert(version2, version4, compensation: false));

        ushort[] Convert(IccProfile source, IccProfile destination, bool compensation)
        {
            var result = new ushort[samples.Length];
            IccColorTransform.Create(source, destination, new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual, BlackPointCompensation = compensation }).Convert(samples, result);
            return result;
        }

        static void AssertClose(ushort[] expected, ushort[] actual)
        {
            Assert.HasCount(expected.Length, actual);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.True(Math.Abs(expected[i] - actual[i]) <= 24, $"Sample {i}: expected {expected[i]}, got {actual[i]} ([{string.Join(", ", actual)}]).");
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Sequences of profiles: device link and abstract profiles. Expected samples: LittleCMS 2.19
    // (cmsCreateMultiprofileTransform, 16-bit or double samples, no optimization), which reads the same synthetic
    // profiles. Its black point compensation and absolute colorimetry are computed per pair of profiles, so the
    // sequences compared with it have matrix-based ends, whose black is black.
    // -----------------------------------------------------------------------------------------------------------------

    private static IccProfile SrgbMatrix => IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.SrgbCurve());

    private static readonly ushort[] SequenceSamples =
    [
        0, 0, 0, 65535, 65535, 65535, 65535, 0, 0, 0, 65535, 0, 0, 0, 65535,
        13107, 39321, 52428, 52428, 26214, 6553, 32768, 32768, 32768, 60000, 45000, 30000, 5000, 10000, 20000,
    ];

    [Fact]
    public void AbstractProfilesConvertTheConnectionSpaceAsLittleCmsDoes()
    {
        // A CIELAB lutAToBType that warms and desaturates (every color changes, black and white included)
        AssertWithin(
            [
                8585, 5351, 1273, 65535, 63106, 59116, 58714, 23034, 12167, 40860, 61318, 24977, 28492, 12531, 44957,
                33323, 38871, 43006, 50400, 30288, 15459, 38073, 33673, 30122, 59146, 45641, 32882, 15624, 12731, 16030,
            ],
            Convert([SrgbMatrix, IccTestProfiles.LabAbstractLutAToB(), SrgbMatrix], IccColorTransformOptions.Default),
            tolerance: 4);

        // A CIEXYZ lut16Type whose matrix mixes the components, toward another destination. LittleCMS evaluates the tables
        // of a lut16Type with 16-bit values, even for double samples: the dark red sample (5782) is 12 units away
        AssertWithin(
            [
                0, 0, 0, 63283, 65535, 62710, 57479, 17371, 11816, 30831, 63295, 23088, 0, 15573, 58588,
                20553, 39629, 48264, 47025, 28424, 14050, 31586, 33175, 31283, 55536, 46035, 31999, 5782, 10633, 17959,
            ],
            Convert([SrgbMatrix, IccTestProfiles.XyzAbstractLut16(), DisplayP3], IccColorTransformOptions.Default),
            tolerance: 16);

        // Both in a row, with the perceptual intent (the same conversion for matrix-based profiles)
        AssertWithin(
            [
                8108, 5431, 1428, 65133, 63941, 56536, 56015, 24996, 13795, 39825, 60586, 26510, 26400, 16926, 41879,
                31656, 39495, 40643, 48144, 31036, 16389, 36328, 34136, 28839, 56567, 46233, 32277, 14726, 13211, 15022,
            ],
            Convert([SrgbMatrix, IccTestProfiles.LabAbstractLutAToB(), IccTestProfiles.XyzAbstractLut16(), SrgbMatrix], new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual }),
            tolerance: 16);

        static ushort[] Convert(IccProfile[] profiles, IccColorTransformOptions options)
        {
            var transform = IccColorTransform.Create(profiles, options);
            Assert.Equal(3, transform.SourceChannelCount);
            Assert.Equal(3, transform.DestinationChannelCount);
            var result = new ushort[SequenceSamples.Length];
            transform.Convert(SequenceSamples, result);
            return result;
        }
    }

    [Fact]
    public void DeviceLinkProfilesConvertDeviceValuesAsLittleCmsDoes()
    {
        // RGB to CMYK, a lut16Type with input tables (interpolated with 16-bit values by LittleCMS): the link alone
        var rgbToCmyk = IccColorTransform.Create([IccTestProfiles.RgbToCmykDeviceLinkLut16()]);
        Assert.Equal(3, rgbToCmyk.SourceChannelCount);
        Assert.Equal(4, rgbToCmyk.DestinationChannelCount);
        var cmyk = new ushort[SequenceSamples.Length / 3 * 4];
        rgbToCmyk.Convert(SequenceSamples, cmyk);
        AssertWithin(
            [
                0, 0, 0, 65535, 0, 0, 0, 0, 0, 65535, 65535, 0, 65535, 0, 65535, 0, 65535, 65535, 0, 0,
                61603, 28572, 0, 23593, 0, 49281, 64662, 23593, 0, 0, 0, 49150, 0, 28741, 49381, 10602, 45776, 36624, 0, 59431,
            ],
            cmyk,
            tolerance: 4);

        // CMYK to RGB, a lutAToBType with B curves, at the grid points of its table (the interpolation of four inputs
        // differs between LittleCMS and the library, as for CMYK profiles); the rendering intent does not matter
        float[] grid = [0, 0, 0, 0, 0.25f, 0.5f, 0.75f, 0, 1, 0, 0, 0, 0, 1, 0, 0.25f, 0.5f, 0.5f, 0.5f, 0.5f, 0.75f, 0.25f, 0, 1, 0, 0, 1, 0.75f];
        float[] expected =
        [
            1, 1, 1, 0.8774226307868958f, 0.7297458052635193f, 0.5325252413749695f, 0, 1, 1, 0.8774226307868958f, 0, 0.8774226307868958f,
            0.5325252413749695f, 0.5325252413749695f, 0.5325252413749695f, 0, 0, 0, 0.5325252413749695f, 0.5325252413749695f, 0,
        ];
        foreach (var intent in Enum.GetValues<IccRenderingIntent>())
        {
            var rgb = new float[grid.Length / 4 * 3];
            IccColorTransform.Create([IccTestProfiles.CmykToRgbDeviceLinkLutAToB()], new IccColorTransformOptions { Intent = intent }).Convert(grid, rgb);
            for (var i = 0; i < rgb.Length; i++)
            {
                Assert.True(Math.Abs(expected[i] - rgb[i]) <= 1e-4, $"{intent}, sample {i}: expected {expected[i]}, got {rgb[i]}.");
            }
        }
    }

    [Fact]
    public void SequencesThroughDeviceProfilesAreTheCompositionOfTheirConversions()
    {
        // A soft proof (sRGB, a CMYK printer twice, sRGB) and a device link followed by its printer: the colors go through
        // the device values in the middle, clipped, as if converted twice. Floating-point samples keep the intermediate
        // values to the precision of a float.
        var printer = IccTestProfiles.CmykLabLutAToB();
        float[] samples = [0, 0, 0, 1, 1, 1, 1, 0, 0, 0.2f, 0.6f, 0.8f, 0.8f, 0.4f, 0.1f, 0.5f, 0.5f, 0.5f, 0.05f, 0.95f, 0.3f];
        foreach (var options in new[] { IccColorTransformOptions.Default, new IccColorTransformOptions { Intent = IccRenderingIntent.Perceptual, BlackPointCompensation = false }, new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma } })
        {
            AssertComposition([SrgbMatrix, printer, printer, SrgbMatrix], [SrgbMatrix, printer], [printer, SrgbMatrix], options);
            AssertComposition([IccTestProfiles.RgbToCmykDeviceLinkLut16(), printer, DisplayP3], [IccTestProfiles.RgbToCmykDeviceLinkLut16()], [printer, DisplayP3], options);
            AssertComposition([SrgbMatrix, DisplayP3, IccTestProfiles.RgbToCmykDeviceLinkLut16()], [SrgbMatrix, DisplayP3], [IccTestProfiles.RgbToCmykDeviceLinkLut16()], options);
        }

        // Two device profiles are exactly the conversion between them, identical profiles included
        var direct = new float[samples.Length];
        var sequence = new float[samples.Length];
        IccColorTransform.Create(SrgbMatrix, DisplayP3).Convert(samples, direct);
        IccColorTransform.Create([SrgbMatrix, DisplayP3]).Convert(samples, sequence);
        Assert.Equal(direct, sequence);
        Assert.True(IccColorTransform.Create([SrgbMatrix, SrgbMatrix]).Pipeline.IsIdentity);

        void AssertComposition(IccProfile[] profiles, IccProfile[] first, IccProfile[] second, IccColorTransformOptions options)
        {
            var whole = IccColorTransform.Create(profiles, options);
            var firstTransform = IccColorTransform.Create(first, options);
            var secondTransform = IccColorTransform.Create(second, options);
            var count = samples.Length / 3;
            var middle = new float[count * firstTransform.DestinationChannelCount];
            var expected = new float[count * secondTransform.DestinationChannelCount];
            var actual = new float[expected.Length];
            firstTransform.Convert(samples, middle);
            secondTransform.Convert(middle, expected);
            whole.Convert(samples, actual);
            for (var i = 0; i < actual.Length; i++)
            {
                Assert.True(Math.Abs(expected[i] - actual[i]) <= 2e-5, $"Sample {i}: expected {expected[i]}, got {actual[i]}.");
            }
        }
    }

    [Fact]
    public void SequencesThatDoNotConnectAreUnsupported()
    {
        var link = IccTestProfiles.RgbToCmykDeviceLinkLut16();
        var lab = IccTestProfiles.LabAbstractLutAToB();
        Assert.Throws<ArgumentNullException>(() => IccColorTransform.Create((IReadOnlyList<IccProfile>)null!));
        Assert.Throws<ArgumentNullException>(() => IccColorTransform.Create([SrgbMatrix, null!]));
        Assert.Throws<ArgumentException>(() => IccColorTransform.Create(Array.Empty<IccProfile>()));

        // A device profile alone, or followed by abstract profiles only, ends in the connection space
        AssertUnconnected("ends in the profile connection space", SrgbMatrix);
        AssertUnconnected("ends in the profile connection space", SrgbMatrix, lab);

        // An abstract profile needs colors of the connection space, a device link profile device values of its input
        AssertUnconnected("an abstract profile converts colors of the profile connection space", lab, SrgbMatrix);
        AssertUnconnected("an abstract profile converts colors of the profile connection space", link, lab, SrgbMatrix);
        AssertUnconnected("a device link profile converts device values", SrgbMatrix, link);
        AssertUnconnected("is not the color space of the colors there, 'CMYK'", link, SrgbMatrix);
        AssertUnconnected("is not the color space of the colors there, 'CMYK'", link, link);

        // A profile in the middle is entered once: to go back to the connection space, it appears a second time
        var printer = IccTestProfiles.CmykLabLutAToB();
        AssertUnconnected("is not the color space of the colors there, 'CMYK'", SrgbMatrix, printer, SrgbMatrix);
        _ = IccColorTransform.Create([SrgbMatrix, printer, printer, SrgbMatrix]);

        // Outside a sequence, device link and abstract profiles do not describe colors
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(link, SrgbMatrix));
        Assert.Contains("use it in a sequence of profiles", exception.Message, StringComparison.Ordinal);
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(SrgbMatrix, lab));

        static void AssertUnconnected(string message, params IccProfile[] profiles)
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(profiles));
            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MalformedDeviceLinkAndAbstractProfilesAreReported()
    {
        var curve = IccTestProfiles.Curve();

        // A device link profile without table, or with a table of other channel counts
        AssertInvalid("has no 'A2B0' tag", Link("RGB ", "CMYK", ("rTRC", curve)));
        AssertInvalid("3 and 4 are expected", Link("RGB ", "CMYK", ("A2B0", IccTestProfiles.Lut16(3, 3, 2, rgb => rgb))));
        AssertInvalid("a lutBToAType cannot convert to the profile connection space", Link("RGB ", "RGB ", ("A2B0", IccTestProfiles.LutAToB("mBA ", 3, 3, curvesB: [curve, curve, curve]))));

        // A device link profile whose color spaces are not device color spaces of the library
        AssertUnsupported("output color space is 'Lab '", Link("RGB ", "Lab ", ("A2B0", IccTestProfiles.Lut16(3, 3, 2, rgb => rgb))));
        AssertUnsupported("input color space is 'YCbr'", Link("YCbr", "RGB ", ("A2B0", IccTestProfiles.Lut16(3, 3, 2, rgb => rgb))));

        // An abstract profile between device color spaces, without table, with a table of the wrong type or encoding
        var identity = IccTestProfiles.Lut16(3, 3, 2, lab => lab);
        AssertInvalid("an abstract profile converts between connection spaces", Abstract("RGB ", "Lab ", ("A2B0", identity)));
        AssertInvalid("has no 'A2B0' tag", Abstract("Lab ", "Lab ", ("B2A0", identity)));
        AssertInvalid("a lutBToAType cannot be the 'A2B0' tag of an abstract profile", Abstract("Lab ", "Lab ", ("A2B0", IccTestProfiles.LutAToB("mBA ", 3, 3, curvesB: [curve, curve, curve]))));
        AssertInvalid("cannot be used with the 'XYZ ' profile connection space", Abstract("XYZ ", "XYZ ", ("A2B0", IccTestProfiles.Lut8(3, 3, 2, xyz => xyz))));
        AssertInvalid("the tables of the lookup table exceed the tag", Abstract("Lab ", "Lab ", ("A2B0", identity[..60])));

        static IccProfile Link(string input, string output, params (string Signature, byte[] Data)[] tags)
            => new(new MetadataBlob(IccTestProfiles.BuildBytes(input, output, tags, data => "link"u8.CopyTo(data.AsSpan(12)))));

        static IccProfile Abstract(string input, string output, params (string Signature, byte[] Data)[] tags)
            => new(new MetadataBlob(IccTestProfiles.BuildBytes(input, output, tags, data => "abst"u8.CopyTo(data.AsSpan(12)))));

        static void AssertInvalid(string message, IccProfile profile)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Create(profile));
            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        }

        static void AssertUnsupported(string message, IccProfile profile)
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Create(profile));
            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        }

        // A device link profile alone, an abstract profile between two device profiles
        static IccColorTransform Create(IccProfile profile)
            => profile.ProfileClass == IccProfileClass.DeviceLink ? IccColorTransform.Create([profile]) : IccColorTransform.Create([SrgbMatrix, profile, SrgbMatrix]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Gamut mapping (GamutMapping.ReduceChroma). The expected samples of Display P3 to sRGB: LittleCMS 2.19 converts the
    // samples to CIEXYZ and back, and between them a Python transcription of the algorithm of CSS Color 4 section 13.2
    // (with the matrices of its section 18 and the colorants of the profile) maps the color.
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ReduceChromaFollowsTheGamutMappingOfCssColor4()
    {
        ushort[] samples =
        [
            65535, 0, 0, 0, 65535, 0, 0, 0, 65535, 65535, 65535, 0, 0, 65535, 65535, 65535, 0, 65535,
            60000, 10000, 5000, 20000, 50000, 30000, 10000, 20000, 60000, 60000, 60000, 20000, 30000, 30000, 30000,
            32768, 20000, 15000, 65535, 65535, 65535, 0, 0, 0,
        ];
        ushort[] clipped =
        [
            65535, 0, 0, 0, 65535, 0, 214, 0, 65535, 65529, 65535, 0, 0, 65535, 65535, 65535, 0, 65535,
            65477, 0, 0, 0, 50800, 27774, 5592, 20299, 62329, 59995, 60001, 2337, 30001, 30000, 29994,
            34881, 19229, 13868, 65535, 65535, 65523, 0, 0, 0,
        ];

        // The primaries and secondaries of Display P3 lose chroma; the other colors are in the gamut or within a just
        // noticeable difference of their clipped value, which they keep
        ushort[] mapped =
        [
            65535, 2793, 2870, 0, 64596, 10538, 214, 0, 65535, 65283, 65471, 0, 0, 65306, 64529, 65535, 10968, 64387,
            65477, 0, 0, 0, 50800, 27774, 5592, 20299, 62329, 59995, 60001, 2337, 30001, 30000, 29994,
            34881, 19229, 13868, 65535, 65535, 65523, 0, 0, 0,
        ];

        AssertWithin(clipped, Convert(GamutMapping.Clip), tolerance: 4);
        AssertWithin(mapped, Convert(GamutMapping.ReduceChroma), tolerance: 4);

        ushort[] Convert(GamutMapping mapping)
        {
            var result = new ushort[samples.Length];
            IccColorTransform.Create(DisplayP3, SrgbMatrix, new IccColorTransformOptions { GamutMapping = mapping }).Convert(samples, result);
            return result;
        }
    }

    [Fact]
    public void ReduceChromaKeepsLightnessAndHueAndLeavesInGamutColorsUnchanged()
    {
        // Every combination of 9 levels: Display P3 colors, most of them outside the gamut of sRGB
        var levels = new ushort[] { 0, 8192, 16384, 24576, 32768, 40960, 49152, 57344, 65535 };
        var samples = new List<ushort>();
        foreach (var r in levels)
        {
            foreach (var g in levels)
            {
                foreach (var b in levels)
                {
                    samples.AddRange([r, g, b]);
                }
            }
        }

        var source = samples.ToArray();
        var clipped = new ushort[source.Length];
        var mapped = new ushort[source.Length];
        IccColorTransform.Create(DisplayP3, SrgbMatrix).Convert(source, clipped);
        IccColorTransform.Create(DisplayP3, SrgbMatrix, new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma }).Convert(source, mapped);

        var p3 = RgbColorSpace.FromIccProfile(DisplayP3);
        var srgb = RgbColorSpace.FromIccProfile(SrgbMatrix);
        double clippedHueError = 0;
        double mappedHueError = 0;
        for (var i = 0; i < source.Length; i += 3)
        {
            var origin = Oklab.FromRgb(new Rgba64(source[i], source[i + 1], source[i + 2], 65535), p3);
            var withClip = Oklab.FromRgb(new Rgba64(clipped[i], clipped[i + 1], clipped[i + 2], 65535), srgb);
            var withMapping = Oklab.FromRgb(new Rgba64(mapped[i], mapped[i + 1], mapped[i + 2], 65535), srgb);
            if (clipped.AsSpan(i, 3).SequenceEqual(mapped.AsSpan(i, 3)))
                continue;

            // A mapped color is at most a just noticeable difference (0.02) from the color of the same lightness and hue
            // with less chroma, which it never exceeds
            var chroma = Math.Sqrt((origin.A * origin.A) + (origin.B * origin.B));
            var mappedChroma = Math.Sqrt((withMapping.A * withMapping.A) + (withMapping.B * withMapping.B));
            var scale = Math.Min(1, mappedChroma / chroma);
            var target = (L: origin.L, A: origin.A * scale, B: origin.B * scale);
            var distance = Math.Sqrt(Square(withMapping.L - target.L) + Square(withMapping.A - target.A) + Square(withMapping.B - target.B));
            Assert.True(distance <= 0.0205, $"Color {i / 3}: {distance} from the reduced color.");
            Assert.True(mappedChroma <= chroma + 0.001, $"Color {i / 3}: chroma {mappedChroma} > {chroma}.");
            clippedHueError += HueError(origin, withClip);
            mappedHueError += HueError(origin, withMapping);
        }

        Assert.True(mappedHueError < clippedHueError * 0.75, $"Hue errors: {mappedHueError} mapped, {clippedHueError} clipped.");

        // sRGB is inside Display P3: no color is mapped, every sample is the one of the clipping conversion
        var reverseClipped = new ushort[source.Length];
        var reverseMapped = new ushort[source.Length];
        IccColorTransform.Create(SrgbMatrix, DisplayP3).Convert(source, reverseClipped);
        IccColorTransform.Create(SrgbMatrix, DisplayP3, new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma }).Convert(source, reverseMapped);
        Assert.Equal(reverseClipped, reverseMapped);

        // 8-bit, 16-bit and floating-point samples are mapped alike
        var bytes = Array.ConvertAll(source, value => (byte)(value >> 8));
        var mappedBytes = new byte[bytes.Length];
        var floats = Array.ConvertAll(bytes, value => value / 255f);
        var mappedFloats = new float[floats.Length];
        var transform = IccColorTransform.Create(DisplayP3, SrgbMatrix, new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma });
        transform.Convert(bytes, mappedBytes);
        transform.Convert(floats, mappedFloats);
        for (var i = 0; i < bytes.Length; i++)
        {
            Assert.True(Math.Abs((mappedFloats[i] * 255) - mappedBytes[i]) <= 0.5001, $"Sample {i}: {mappedFloats[i] * 255} and {mappedBytes[i]}.");
        }

        static double Square(double value) => value * value;

        // The distance between the hues, weighted by the chroma: the chord between the two directions
        static double HueError(Oklab origin, Oklab color)
        {
            var originHue = Math.Atan2(origin.B, origin.A);
            var hue = Math.Atan2(color.B, color.A);
            return Math.Abs(Math.Sin((hue - originHue) / 2)) * Math.Sqrt((origin.A * origin.A) + (origin.B * origin.B));
        }
    }

    [Fact]
    public void ReduceChromaOfATableBasedDestinationUsesItsColorimetricRoundTrip()
    {
        // Saturated sRGB colors to a CMYK printer: the colorimetric tables of the destination clip them, the mapping
        // reduces their chroma until their round trip through the printer is within a just noticeable difference
        var printer = IccTestProfiles.CmykLabLutAToB();
        byte[] samples = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0, 0, 255, 255, 255, 0, 255, 128, 128, 128, 200, 150, 120];
        var clipped = Convert(GamutMapping.Clip, IccRenderingIntent.RelativeColorimetric);
        var mapped = Convert(GamutMapping.ReduceChroma, IccRenderingIntent.RelativeColorimetric);
        Assert.NotEqual(clipped, mapped);

        var toConnectionSpace = IccPipeline.CreateToConnectionSpace(printer, IccRenderingIntent.RelativeColorimetric);
        var fromSrgb = IccPipeline.CreateToConnectionSpace(SrgbMatrix, IccRenderingIntent.RelativeColorimetric);
        Span<double> values = stackalloc double[4];
        for (var color = 0; color < samples.Length / 3; color++)
        {
            var origin = ToOklab(fromSrgb, samples.AsSpan(color * 3, 3), values);
            var printed = ToOklab(toConnectionSpace, mapped.AsSpan(color * 4, 4), values);
            var distance = Math.Sqrt(Square(origin.L - printed.L) + Square(origin.A - printed.A) + Square(origin.B - printed.B));
            var chroma = Math.Sqrt(Square(origin.A) + Square(origin.B));
            var printedChroma = Math.Sqrt(Square(printed.A) + Square(printed.B));
            if (clipped.AsSpan(color * 4, 4).SequenceEqual(mapped.AsSpan(color * 4, 4)))
            {
                // In the gamut: unchanged
                Assert.True(distance < 0.03, $"Color {color}: {distance}.");
                continue;
            }

            // The lightness and the hue of the origin, less chroma
            Assert.True(Math.Abs(origin.L - printed.L) < 0.03, $"Color {color}: lightness {printed.L} instead of {origin.L}.");
            Assert.True(printedChroma < chroma, $"Color {color}: chroma {printedChroma} >= {chroma}.");
            var scale = printedChroma / chroma;
            var hueDistance = Math.Sqrt(Square(printed.A - (origin.A * scale)) + Square(printed.B - (origin.B * scale)));
            Assert.True(hueDistance < 0.03, $"Color {color}: {hueDistance} from the hue of the origin ({origin} printed as {printed}, clipped {System.Convert.ToHexString(clipped.AsSpan(color * 4, 4))}, mapped {System.Convert.ToHexString(mapped.AsSpan(color * 4, 4))}).");
        }

        // The perceptual tables map the gamut themselves: nothing changes
        Assert.Equal(Convert(GamutMapping.Clip, IccRenderingIntent.Perceptual), Convert(GamutMapping.ReduceChroma, IccRenderingIntent.Perceptual));

        // A table-based destination without table to the connection space has no known gamut
        var destinationOnly = IccTestProfiles.Lut("RGB ", "Lab ", ("B2A0", GetTag(IccTestProfiles.RgbLabLut8(), "B2A0").ToArray()));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(SrgbMatrix, destinationOnly, new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma }));
        Assert.Contains("its gamut is unknown", exception.Message, StringComparison.Ordinal);
        _ = IccColorTransform.Create(SrgbMatrix, destinationOnly);
        _ = IccColorTransform.Create(SrgbMatrix, destinationOnly, new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma, Intent = IccRenderingIntent.Perceptual });

        // A grayscale destination has no chroma
        var gray = new byte[samples.Length / 3];
        var grayMapped = new byte[gray.Length];
        IccColorTransform.Create(SrgbMatrix, IccProfile.SrgbGray).Convert(samples, gray);
        IccColorTransform.Create(SrgbMatrix, IccProfile.SrgbGray, new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma }).Convert(samples, grayMapped);
        Assert.Equal(gray, grayMapped);

        byte[] Convert(GamutMapping mapping, IccRenderingIntent intent)
        {
            var result = new byte[samples.Length / 3 * 4];
            IccColorTransform.Create(SrgbMatrix, printer, new IccColorTransformOptions { GamutMapping = mapping, Intent = intent }).Convert(samples, result);
            return result;
        }

        static Oklab ToOklab(IccPipeline pipeline, ReadOnlySpan<byte> device, Span<double> values)
        {
            values.Clear();
            for (var i = 0; i < device.Length; i++)
            {
                values[i] = device[i] / 255.0;
            }

            pipeline.Apply(values);
            return Oklab.FromXyz(new CieXyz(values[0], values[1], values[2]));
        }

        static double Square(double value) => value * value;
    }

    [Fact]
    public void ConvertColorProfileMapsTheGamutAndKeepsAlpha()
    {
        var options = new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma };
        using var image = new Image<Rgba64>(3, 1, new Rgba64(65535, 0, 0, 1234));
        image.Frames[0][1, 0] = new Rgba64(0, 65535, 0, 0);
        image.Frames[0][2, 0] = new Rgba64(30000, 30000, 30000, 65535);
        image.Metadata.IccProfile = DisplayP3;
        image.ConvertColorProfile(SrgbMatrix, options, TestContext.Current.CancellationToken);

        ushort[] expected = [65535, 0, 0, 0, 65535, 0, 30000, 30000, 30000];
        IccColorTransform.Create(DisplayP3, SrgbMatrix, options).Convert(expected, expected);
        Assert.Equal(new Rgba64(expected[0], expected[1], expected[2], 1234), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba64(expected[3], expected[4], expected[5], 0), image.Frames[0][1, 0]);
        Assert.Equal(new Rgba64(expected[6], expected[7], expected[8], 65535), image.Frames[0][2, 0]);

        // The primaries of Display P3 are outside sRGB: the mapping changed them
        ushort[] clipped = [65535, 0, 0, 0, 65535, 0];
        IccColorTransform.Create(DisplayP3, SrgbMatrix).Convert(clipped, clipped);
        Assert.NotEqual(clipped, expected[..6]);
    }

    [Fact]
    public void SoftProofIsTheConversionToTheInksThenBack()
    {
        var printer = IccTestProfiles.CmykLabLutAToB();
        ushort[] rgb = [65535, 0, 0, 30000, 40000, 50000];
        var options = new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma };
        var preview = new ushort[rgb.Length];
        IccColorTransform.Create([SrgbMatrix, printer, printer, SrgbMatrix], options).Convert(rgb, preview);

        // The same as converting to the inks, then back to the display, up to the 16-bit rounding of the inks
        var inks = new ushort[rgb.Length / 3 * 4];
        var expected = new ushort[rgb.Length];
        IccColorTransform.Create(SrgbMatrix, printer, options).Convert(rgb, inks);
        IccColorTransform.Create(printer, SrgbMatrix, options).Convert(inks, expected);
        AssertWithin(expected, preview, tolerance: 2);
        Assert.NotEqual(rgb, preview);
    }

    private static void AssertWithin(ushort[] expected, ushort[] actual, int tolerance)
    {
        Assert.HasCount(expected.Length, actual);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, $"Sample {i}: expected {expected[i]}, got {actual[i]} ([{string.Join(", ", actual)}]).");
        }
    }

    /// <summary>
    /// A monochrome output profile with <c>lut16Type</c> tables and the CIEXYZ connection space, whose encoding is the same
    /// in versions 2 and 4. The perceptual tables give device black the lightness <paramref name="perceptualBlack"/>, the
    /// media-relative colorimetric tables L* = 10; both are linear in lightness. The tables from the connection space put
    /// a grid point at the black lightness, so that the darkest colors are not blurred by the interpolation.
    /// </summary>
    private static IccProfile PerceptualPrinter(int version, double perceptualBlack)
    {
        return new IccProfile(new MetadataBlob(IccTestProfiles.BuildBytes(
            "GRAY",
            "XYZ ",
            [
                ("A2B0", ToConnectionSpace(perceptualBlack)),
                ("B2A0", FromConnectionSpace(perceptualBlack)),
                ("A2B1", ToConnectionSpace(10)),
                ("B2A1", FromConnectionSpace(10)),
            ],
            data =>
            {
                data[8] = (byte)version;
                data[9] = version == 2 ? (byte)0x10 : (byte)0x40;
                "prtr"u8.CopyTo(data.AsSpan(12));
            })));

        // XYZ is encoded with 1 at 0x8000
        static double Encode(double value) => value * 32768 / 65535;

        static byte[] ToConnectionSpace(double black)
            => IccTestProfiles.Lut16(1, 3, 255, gray =>
            {
                var y = Luminance(black + ((100 - black) * gray[0]));
                return [Encode(0.9642 * y), Encode(y), Encode(0.8249 * y)];
            });

        static byte[] FromConnectionSpace(double black)
        {
            // The input table of Y gives the grid coordinate of its lightness, linear between grid points 0 (L* = 0), 1 (of
            // 33: the black, or L* = 1), 32 (L* = 100) and the largest encoded Y
            double[] lightnesses = [0, black > 0 ? black : 1, 100, Lightness(65535 / 32768.0)];
            double[] coordinates = [0, 1 / 32.0, 31 / 32.0, 1];
            return IccTestProfiles.Lut16(
                3,
                1,
                33,
                xyz => [Math.Clamp((Interpolate(xyz[1], coordinates, lightnesses) - black) / (100 - black), 0, 1)],
                inputEntries: 4096,
                inputTable: (channel, input) => channel == 1 ? Interpolate(Lightness(input * 65535 / 32768), lightnesses, coordinates) : input);
        }

        static double Interpolate(double value, double[] from, double[] to)
        {
            var segment = 1;
            while (segment < from.Length - 1 && value > from[segment])
            {
                segment++;
            }

            return to[segment - 1] + ((value - from[segment - 1]) / (from[segment] - from[segment - 1]) * (to[segment] - to[segment - 1]));
        }

        static double Lightness(double y) => y > 216.0 / 24389 ? (116 * Math.Cbrt(y)) - 16 : y * 24389 / 27;

        static double Luminance(double lightness) => lightness > 8 ? Math.Pow((lightness + 16) / 116, 3) : lightness * 27 / 24389;
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

    // Named color profiles (ICC.1:2022 section 10.17, ICC.2:2023 section 12.2.5) and iccMAX processing elements
    // (ICC.2:2023 clause 11). The expected values are computed by hand from the formulas of the specifications, except the
    // CIELAB values of 'tLab', printed by iccApplyNamedCmm --debugcalc (iccDEV 2.4.2.5)

    [Fact]
    public void NamedColorsOfAVersion2ProfileUseTheLegacyCieLabEncoding()
    {
        var colors = IccNamedColorCollection.Create(IccTestProfiles.NamedColorLabV2());
        Assert.HasCount(10, colors);
        Assert.Equal(4, colors.DeviceChannelCount);
        Assert.Equal("PMS Rubine Red C", colors[2].Name);
        Assert.Same(colors[2], colors["PMS Rubine Red C"]);
        Assert.Equal(2, colors.IndexOf("PMS Rubine Red C"));
        Assert.Equal(-1, colors.IndexOf("Rubine Red"));
        Assert.False(colors.TryGetValue("pms rubine red c", out _));
        Assert.Throws<KeyNotFoundException>(() => colors["Rubine Red"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => colors[10]);

        // L* 48 is stored as round(48 * 652.8) = 31334, a* 74 as (74 + 128) * 256 and b* 15 as (15 + 128) * 256 (Tables 42, 43)
        var lab = colors[2].ConnectionSpaceLab;
        Assert.Equal(31334 * 100.0 / 65280, lab.L, 1e-12);
        Assert.Equal(74, lab.A, 1e-12);
        Assert.Equal(15, lab.B, 1e-12);

        // The inks 0.06, 1, 0.45, 0.02 are stored as round(x * 65535)
        Assert.Equal([3932 / 65535.0, 1, 29491 / 65535.0, 1311 / 65535.0], colors[2].DeviceValues);
    }

    [Fact]
    public void NamedColorsInTheXyzConnectionSpaceAreU1Fixed15Numbers()
    {
        // ICC.1:2022 Table 66: the PCSXYZ coordinates are u1Fixed15Numbers (1.0 is 8000h). LittleCMS 2.19 and iccDEV 2.4.2.5
        // scale them otherwise, so the expected values are computed here from the stored numbers
        var profile = IccTestProfiles.NamedColorXyzV4();
        var colors = IccNamedColorCollection.Create(profile);
        Assert.Equal(0, colors.DeviceChannelCount);
        Assert.Empty(colors[4].DeviceValues);
        var tag = GetTag(profile, "ncl2");
        var stored = BinaryPrimitives.ReadUInt16BigEndian(tag[(84 + (4 * 38) + 34)..]);
        var y = stored / 32768.0;
        Assert.Equal("Orange 021", colors[4].Name);
        Assert.Equal(y, colors[4].ConnectionSpaceColor.Y, 1e-15);
        Assert.Equal(69, colors[4].ConnectionSpaceLab.L, 0.001);

        // To the sRGB gray: the encoded fraction of the media white, relative colorimetric intent
        var gray = new float[1];
        IccNamedColorTransform.Create(profile, IccProfile.SrgbGray, NoCompensation).Convert(colors[4], gray);
        var expected = y <= 0.0031308 ? 12.92 * y : (1.055 * Math.Pow(y, 1 / 2.4)) - 0.055;
        Assert.Equal(expected, gray[0], 1e-4);

        // ICC-absolute: rescaled by the media white point of the named color profile ('wtpt' 0.9505, 1, 1.089), whose Y
        // is the illuminant's: the gray does not change
        IccNamedColorTransform.Create(profile, IccProfile.SrgbGray, new IccColorTransformOptions { Intent = IccRenderingIntent.AbsoluteColorimetric }).Convert(colors[4], gray);
        Assert.Equal(expected, gray[0], 1e-4);
    }

    [Fact]
    public void NamedColorTransformsConvertTheColorimetryOfTheColors()
    {
        // PMS Warm Red C: L* 59, a* 66.49, b* 59.38 (legacy encoding), to sRGB: a saturated red-orange
        var transform = IccNamedColorTransform.Create(IccTestProfiles.NamedColorLabV2(), IccProfile.Srgb);
        Assert.Equal(3, transform.DestinationChannelCount);
        var rgb = new byte[3];
        transform.Convert(transform.Colors["PMS Warm Red C"], rgb);
        Assert.True(rgb[0] > 240 && rgb[1] < 100 && rgb[2] < 60, string.Join(' ', rgb));

        // The paper is the media white: the white of the destination with the relative colorimetric intent
        transform.Convert(transform.Colors["PMS Paper C"], rgb);
        Assert.Equal([255, 255, 255], rgb);

        // Through a sequence: the colors enter the connection space, an abstract profile changes them, then the destination
        var throughAbstract = IccNamedColorTransform.Create([IccTestProfiles.NamedColorLabV2(), IccTestProfiles.LabAbstractLutAToB(), IccProfile.Srgb]);
        var sixteen = new ushort[3];
        throughAbstract.Convert(throughAbstract.Colors["PMS Black C"], sixteen);
        Assert.All(sixteen, value => value > 0);
    }

    [Fact]
    public void NamedColorProfilesAreRejectedWhereSamplesAreConverted()
    {
        var named = IccTestProfiles.NamedColorLabV2();
        Assert.Throws<ArgumentException>("profile", () => IccNamedColorCollection.Create(IccProfile.Srgb));
        Assert.Throws<ArgumentException>("profiles", () => IccNamedColorTransform.Create([IccProfile.Srgb, IccProfile.Srgb]));
        Assert.Throws<ArgumentException>("profiles", () => IccNamedColorTransform.Create([named]));
        Assert.Throws<ArgumentNullException>("profiles", () => IccNamedColorTransform.Create([named, null!]));
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(named, IccProfile.Srgb));
        Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create([IccProfile.Srgb, named]));

        // A named color profile is only a source: one in the middle of a sequence is rejected
        Assert.Throws<UnsupportedImageFeatureException>(() => IccNamedColorTransform.Create([named, IccProfile.Srgb, named, IccProfile.Srgb]));

        var transform = IccNamedColorTransform.Create(named, IccProfile.Srgb);
        Assert.Throws<ArgumentException>("destination", () => transform.Convert(transform.Colors[0], new float[4]));
        Assert.Throws<ArgumentNullException>("color", () => transform.Convert(null!, new float[3]));
    }

    [Fact]
    public void MalformedNamedColorTagsAreInvalid()
    {
        var tag = IccProfileBuilder.NamedColor2("", "", 0, ("Red", [1, 2, 3], []));
        Assert.Equal("Red", IccNamedColorCollection.Create(new IccProfile(new MetadataBlob(NamedProfile("Lab ", tag))))[0].Name);
        AssertInvalid(NamedProfile("Lab ", tag[..^1]));                                           // the color does not fit
        AssertInvalid(NamedProfile("Lab ", With(tag, data => data.AsSpan(84, 32).Fill((byte)'a'))));   // no null character
        AssertInvalid(NamedProfile("Lab ", With(tag, data => data[84] = 0xE9)));                    // not 7-bit ASCII
        AssertInvalid(NamedProfile("Lab ", With(tag, data => data[19] = 16)));                      // more than 15 colorants
        AssertInvalid(NamedProfile("XYZ ", IccProfileBuilder.NamedColor2("", "", 3, ("Red", [1, 2, 3], [1, 2, 3])), "CMYK"));
        AssertInvalid(NamedProfile("RGB ", tag));                                                   // not a connection space
        AssertInvalid(IccTestProfiles.BuildBytes("CMYK", "Lab ", [("wtpt", IccTestProfiles.Xyz(0.9642, 1, 0.8249))], header => "nmcl"u8.CopyTo(header.AsSpan(12))));

        static void AssertInvalid(byte[] profile) => Assert.Throws<InvalidImageContentException>(() => IccNamedColorCollection.Create(new IccProfile(new MetadataBlob(profile))));

        static byte[] With(byte[] tag, Action<byte[]> change)
        {
            var copy = tag.ToArray();
            change(copy);
            return copy;
        }
    }

    [Fact]
    public void IccMaxNamedColorsAreReadAtFullTint()
    {
        // A namedColorArray: the tint zero structure, then a color whose tints are 0.5 and 1, and one without tint values
        // (equally spaced tints: the last vector is the full tint)
        var profile = IccMaxNamedProfile(
            Struct("nmcl", ("name", Utf8("Rouge é")), ("tint", Floats("fl32", 0.5f, 1)), ("pcs ", Floats("fl32", 75, 20, 10, 50, 40, 20)), ("dev ", Floats("ui16", 0.5f, 0, 0, 0, 1, 0, 0, 0))),
            Struct("nmcl", ("name", Utf8("Gray")), ("pcs ", Floats("fl16", 80, 0, 0, 60, 0, 0)), ("dev ", Floats("fl32", 0, 0, 0, 0.2f, 0, 0, 0, 0.4f))));
        var colors = IccNamedColorCollection.Create(profile);
        Assert.HasCount(2, colors);
        Assert.Equal(4, colors.DeviceChannelCount);
        Assert.Equal("Rouge é", colors[0].Name);
        Assert.Equal(new CieLab(50, 40, 20), colors[0].ConnectionSpaceLab);
        Assert.Equal([1, 0, 0, 0], colors[0].DeviceValues);
        Assert.Equal(60, colors[1].ConnectionSpaceLab.L);
        Assert.Equal(0.4, colors[1].DeviceValues[3], 1e-7);

        // Not supported: no value at full tint, integer PCS values, a spectral connection space only
        Assert.Throws<UnsupportedImageFeatureException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile(Struct("nmcl", ("name", Utf8("A")), ("tint", Floats("fl32", 0.5f)), ("pcs ", Floats("fl32", 75, 20, 10))))));
        Assert.Throws<UnsupportedImageFeatureException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile(Struct("nmcl", ("name", Utf8("A")), ("pcs ", Floats("ui16", 0.5f, 0.5f, 0.5f))))));
        Assert.Throws<UnsupportedImageFeatureException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile("\0\0\0\0", Struct("nmcl", ("name", Utf8("A")), ("pcs ", Floats("fl32", 75, 20, 10))))));
        Assert.Throws<UnsupportedImageFeatureException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile("Lab ", includeTintZero: true, [("c2sp", Mpe(3, 3, Element("bACS", 3, 3, U32(0))))], Struct("nmcl", ("name", Utf8("A")), ("pcs ", Floats("fl32", 75, 20, 10))))));

        // Malformed: no name, values that are not vectors of the connection space, a first element that is not the tint
        // zero, device values (amounts of the colorants) outside [0, 1]
        Assert.Throws<InvalidImageContentException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile(Struct("nmcl", ("pcs ", Floats("fl32", 75, 20, 10))))));
        Assert.Throws<InvalidImageContentException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile(Struct("nmcl", ("name", Utf8("A")), ("pcs ", Floats("fl32", 75, 20, 10)), ("dev ", Floats("fl32", -1, 2, 0, 0))))));
        Assert.Throws<InvalidImageContentException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile(Struct("nmcl", ("name", Utf8("A")), ("pcs ", Floats("fl32", 75, 20, 10)), ("dev ", Floats("fl16", 0, 0, 0, 1.5f))))));
        Assert.Throws<InvalidImageContentException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile(Struct("nmcl", ("name", Utf8("A")), ("pcs ", Floats("fl32", 75, 20))))));
        Assert.Throws<InvalidImageContentException>(() => IccNamedColorCollection.Create(IccMaxNamedProfile("Lab ", includeTintZero: false, Struct("nmcl", ("name", Utf8("A")), ("pcs ", Floats("fl32", 75, 20, 10))))));
    }

    [Fact]
    public void IccMaxNamedColorsSharingTheirStructureAreReadOnce()
    {
        // 5,000 colors that all refer to the same structure, whose 20,000 members are unknown but the last two (a name
        // and PCS values)
        var colors = IccNamedColorCollection.Create(IccMaxNamedProfileWithArray("Lab ", SharedStructureArray(5000, 20000, sizeStep: 0), []));
        Assert.HasCount(5000, colors);
        Assert.Equal("A", colors[4999].Name);
        Assert.Equal(new CieLab(50, 40, 20), colors[4999].ConnectionSpaceLab);

        // Structures at the same offset but of different sizes would have their members scanned again and again
        Assert.Throws<UnsupportedImageFeatureException>(() => IccNamedColorCollection.Create(IccMaxNamedProfileWithArray("Lab ", SharedStructureArray(5000, 20000, sizeStep: 1), [])));

        // A namedColorArray: the tint zero structure, then a single structure that every color refers to
        static byte[] SharedStructureArray(int count, int unknownMembers, int sizeStep)
        {
            var tintZero = Struct("tnt0", ("pcs ", Floats("fl32", 100, 0, 0)));
            var name = Utf8("A");
            var pcs = Floats("fl32", 50, 40, 20);
            var header = 16 + (8 * (count + 1));
            var structOffset = header + tintZero.Length;
            var table = 16 + (12 * (unknownMembers + 2));
            var structSize = table + name.Length + pcs.Length;
            var data = new List<byte>([.. "tary"u8, 0, 0, 0, 0, .. "ncol"u8, .. U32((uint)count + 1), .. U32((uint)header), .. U32((uint)tintZero.Length)]);
            for (var i = 0; i < count; i++)
            {
                data.AddRange([.. U32((uint)structOffset), .. U32((uint)(structSize + (sizeStep * i)))]);
            }

            data.AddRange(tintZero);
            data.AddRange([.. "tstr"u8, 0, 0, 0, 0, .. "nmcl"u8, .. U32((uint)unknownMembers + 2)]);
            for (var i = 0; i < unknownMembers; i++)
            {
                data.AddRange([.. "junk"u8, .. U32(0), .. U32(0)]);
            }

            data.AddRange([.. "name"u8, .. U32((uint)table), .. U32((uint)name.Length)]);
            data.AddRange([.. "pcs "u8, .. U32((uint)(table + name.Length)), .. U32((uint)pcs.Length)]);
            data.AddRange(name);
            data.AddRange(pcs);
            data.AddRange(new byte[sizeStep * count]);
            return [.. data];
        }
    }

    [Fact]
    public void IccMaxNamedColorsSharingTheirNameAreReadOnce()
    {
        // 1,000 colors whose name members all refer to the same name of 64 KiB
        var name = new string('n', 65536);
        var colors = IccNamedColorCollection.Create(IccMaxNamedProfileWithArray("Lab ", SharedNameArray(name, 1000, sizeStep: 0), []));
        Assert.HasCount(1000, colors);
        Assert.Same(colors[0].Name, colors[999].Name);
        Assert.Equal(0, colors.IndexOf(name));
        Assert.Same(colors[0], colors[name]);

        // Names that appear several times as distinct strings refer to their first color too
        var duplicates = IccNamedColorCollection.Create(new IccProfile(new MetadataBlob(NamedProfile("Lab ", IccProfileBuilder.NamedColor2("", "", 0, ("Red", [1, 2, 3], []), ("Red", [4, 5, 6], []))))));
        Assert.NotSame(duplicates[0].Name, duplicates[1].Name);
        Assert.Equal(0, duplicates.IndexOf("Red"));
        Assert.Equal(new CieLab(50, 40, 20), colors[999].ConnectionSpaceLab);

        // Names at the same offset but of different sizes would be read and stored again and again
        Assert.Throws<UnsupportedImageFeatureException>(() => IccNamedColorCollection.Create(IccMaxNamedProfileWithArray("Lab ", SharedNameArray(name, 1000, sizeStep: 1), [])));

        // A namedColorArray: the tint zero structure, then the members of a structure per color (a name and PCS values),
        // then the name and the PCS values they refer to
        static byte[] SharedNameArray(string name, int count, int sizeStep)
        {
            var tintZero = Struct("tnt0", ("pcs ", Floats("fl32", 100, 0, 0)));
            var nameData = Utf8(name);
            var pcs = Floats("fl32", 50, 40, 20);
            const int StructSize = 16 + (2 * 12);
            var header = 16 + (8 * (count + 1));
            var firstStruct = header + tintZero.Length;
            var nameOffset = firstStruct + (count * StructSize);
            var pcsOffset = nameOffset + nameData.Length;
            var data = new List<byte>([.. "tary"u8, 0, 0, 0, 0, .. "ncol"u8, .. U32((uint)count + 1), .. U32((uint)header), .. U32((uint)tintZero.Length)]);
            for (var i = 0; i < count; i++)
            {
                // Each structure extends to the end of the shared data, which its members refer to
                var structOffset = firstStruct + (i * StructSize);
                data.AddRange([.. U32((uint)structOffset), .. U32((uint)(pcsOffset + pcs.Length - structOffset))]);
            }

            data.AddRange(tintZero);
            for (var i = 0; i < count; i++)
            {
                var structOffset = firstStruct + (i * StructSize);
                data.AddRange([.. "tstr"u8, 0, 0, 0, 0, .. "nmcl"u8, .. U32(2)]);
                data.AddRange([.. "name"u8, .. U32((uint)(nameOffset - structOffset)), .. U32((uint)(nameData.Length - (sizeStep * i)))]);
                data.AddRange([.. "pcs "u8, .. U32((uint)(pcsOffset - structOffset)), .. U32((uint)pcs.Length)]);
            }

            data.AddRange(nameData);
            data.AddRange(pcs);
            return [.. data];
        }
    }

    [Fact]
    public void CalculatorOperationsFollowIcc2Table102()
    {
        // Arithmetic, rounded to single precision; 'sign' gives 1 for positive values (Table 102 prints -1, a typographical error)
        Assert.Equal([1], Calculate(1, "7 3 mod out"));
        Assert.Equal([-1], Calculate(1, "-7 3 mod out"));
        Assert.Equal([3, -3, 2, 3, -2], Calculate(5, "2.5 rond -2.5 rond 2.7 flor 2.2 ceil -2.7 trnc out:0:4"));
        Assert.Equal([-1, 0, 1], Calculate(3, "-3 sign 0 sign 3 sign out:0:2"));
        Assert.Equal([8, 4, 5, -1, 6], Calculate(5, "2 3 gama 8 2 sdiv 2 3 sadd 2 3 ssub 2 3 smul out:0:4"));
        Assert.Equal([1024, 3, 3, 9, 8, 2.5, -2.5], Calculate(7, "2 10 pow 9 sqrt 27 cbrt 3 sq 2 cb -2.5 abs 2.5 neg out:0:6"));
        Assert.Equal([1, 2, 0], Calculate(3, "0 exp 100 log 1 ln out:0:2"));
        Assert.Equal(Math.PI / 4, Calculate(1, "1 1 atn2 out")[0], 1e-7);

        // Two values that are not numbers are equal, so that a value can be compared with 'NaN ' (section 11.2.1.1)
        Assert.Equal([1, 0, 0, 0, 1], Calculate(5, "NaN NaN eq NaN 1 eq NaN 1 lt NaN rnum 1 rnum out:0:4"));
        Assert.Equal([1, 0], Calculate(2, "1 1.000000001 near 1 1.001 near out:0:1"));
        Assert.Equal([1, 2, 0, 1, 0, 1], Calculate(6, "1 2 vmin 1 2 vmax 1 0 vand 1 0 vor 1 1 vxor 0.2 not out:0:5"));
        Assert.Equal([6, 24, 3, 5, 0, 1], Calculate(6, "1 2 3 sum:1 2 3 4 prod:1 5 3 4 min:1 5 3 4 max:1 1 0 and 1 0 or out:0:5"));

        // Polar coordinates in degrees, in [0, 360)
        var polar = Calculate(4, "3 4 ctop 5 60 ptoc out:0:3");
        AssertClose([5, 53.130102354155978, 2.5, 4.3301270189221932], polar, 1e-5);
        Assert.Equal(270, Calculate(2, "0 -1 ctop out:0:1")[1], 1e-5);

        // CIELAB of normalized CIEXYZ and back (iccDEV prints 57.0754 81.8700 -55.7199)
        AssertClose([57.0754, 81.8700, -55.7199], Calculate(3, "0.5 0.25 0.75 tLab out:0:2"), 1e-4);
        AssertClose([0.5, 0.25, 0.75], Calculate(3, "0.5 0.25 0.75 tLab tXYZ out:0:2"), 1e-6);
    }

    [Fact]
    public void CalculatorStackOperationsFollowIcc2Table96()
    {
        Assert.Equal([18], Calculate(1, "1 2 3 copy:2:1 sum:7 out"));
        Assert.Equal([3, 4, 5, 1, 2], Calculate(5, "1 2 3 4 5 rotl:4:1 out:0:4"));
        Assert.Equal([4, 5, 1, 2, 3], Calculate(5, "1 2 3 4 5 rotr:4:1 out:0:4"));
        Assert.Equal([1, 2, 3, 4, 5, 2, 2], Calculate(7, "1 2 3 4 5 posd:3:1 out:0:6"));
        Assert.Equal([3, 2, 1], Calculate(3, "1 2 3 flip:1 out:0:2"));
        Assert.Equal([1], Calculate(1, "1 2 3 pop:1 out"));
        Assert.Equal([1, 2, 2], Calculate(3, "1 2 scnt out:0:2"));
        Assert.Equal([1, 4, 2, 5, 3, 6], Calculate(6, "1 2 3 4 5 6 tran:1:2 out:0:5"));

        // Systems of linear equations: [2 1; 1 3] x = [3 5] has x = (0.8, 1.4); a singular or not square system reports a failure
        AssertClose([0.8, 1.4, 1], Calculate(3, "2 1 1 3 3 5 solv:1:1 out:0:2"), 1e-6);
        Assert.Equal([0, 0, 0], Calculate(3, "1 2 2 4 1 1 solv:1:1 out:0:2"));
        Assert.Equal([0, 0], Calculate(2, "1 2 3 4 solv:1:0 out:0:1"));

        // Channels: the inputs, the temporary channels (zero until set) and the outputs
        Assert.Equal([0.25, 0.5], Calculate(2, "in:0:1 out:0:1", [0.25, 0.5]));
        Assert.Equal([0, 2, 1, 6], Calculate(4, "tget:3 1 2 tput:4:1 tget:5 tget:4 3 tsav:0 tget:0 add out:0:3"));
        Assert.Equal([0, 7], Calculate(2, "7 out:1"));
    }

    [Fact]
    public void CalculatorConditionsAndSelectionsFollowIcc2Section11_2_1_10()
    {
        Assert.Equal([5, 3], Calculate(2, "3 1 if:2 pop 5 3 0 if:2 pop 5 out:0:1"));
        Assert.Equal([7], Calculate(1, "0.49 if:1 else:1 5 7 out"));
        Assert.Equal([5], Calculate(1, "NaN if:1 else:1 5 7 out"));
        Assert.Equal([9], Calculate(1, "9 0 if:2 1 pop out"));

        // The selector is rounded like 'rond'; the default runs outside the cases, nothing runs without a default
        Assert.Equal([30, 20, 40, 40], Calculate(4, "2 sel case:1 case:1 case:1 dflt:1 10 20 30 40 1.4 sel case:1 case:1 case:1 dflt:1 10 20 30 40 3 sel case:1 case:1 case:1 dflt:1 10 20 30 40 -1 sel case:1 case:1 case:1 dflt:1 10 20 30 40 out:0:3"));
        Assert.Equal([2, 9], Calculate(2, "9 1 sel case:2 case:2 pop 1 pop 2 9 5 sel case:2 case:2 pop 1 pop 2 out:0:1"));

        // A selection nested in a case of another one: the outer selection runs its default or its case
        Assert.Equal([0.75, 0.25], Calculate(2, "1 sel case:6 dflt:1 0 sel case:1 dflt:1 0.25 0.5 0.75 0 sel case:6 dflt:1 0 sel case:1 dflt:1 0.25 0.5 0.75 out:0:1"));

        // The CMM environment variables: 'true' is available with the value 1; the others are not available
        Assert.Equal([1, 1, 0, 0, 0, 0], Calculate(6, "env:true env:ndef env:zzzz out:0:5"));
    }

    [Fact]
    public void CalculatorSubElementsAreAppliedToTheStack()
    {
        byte[][] subElements =
        [
            Element("matf", 2, 2, Floats(2, 0, 0, 3, 1, 0)),                                 // [2 0; 0 3] + (1, 0)
            CurveSetElement(Segmented([], Formula(0, 2, 1, 0, 0))),                          // x^2
            ClutElement([2], 1, 0, 0, 10),                                                   // 10 x
            Element("tint", 1, 2, U32(0), U32(0), Floats(0, 0, 1, 2)),                       // (t, 2 t)
            CalculatorElement(1, 1, [], "in sq 1 add out"),                                  // x^2 + 1
        ];

        Assert.Equal([3, 3], Calculate(2, "1 1 mtx:0 out:0:1", subElements: subElements));
        Assert.Equal([0.25, 2.5, 0.5, 1, 1.25, 3, 3], Calculate(7, "0.5 curv:1 0.25 clut:2 0.5 tint:3 0.5 calc:4 1 1 elem:0 out:0:6", subElements: subElements));

        // The operation names the type of the sub-element it applies; 'elem' applies any
        Assert.Throws<InvalidImageContentException>(() => Calculate(1, "1 curv:0 out", subElements: subElements));
        Assert.Throws<InvalidImageContentException>(() => Calculate(1, "1 mtx:9 out", subElements: subElements));
    }

    [Fact]
    public void RepeatedProcessingElementsAreReadOnce()
    {
        // A tint array of 65,536 values, which every element of a multiProcessElementsType refers to
        var tint = Element("tint", 1, 1, U32(0), U32(0), Floats([.. Enumerable.Range(0, 65536).Select(i => i / 65535f)]));

        // The same position many times: the element is read once
        var context = new IccMpeParseContext("source", 0x41324231);
        var stage = IccMpeParser.CreateStage(Stage(tint, [.. Enumerable.Repeat(tint.Length, 1000)]), 1, 1, context);
        Assert.Equal(65536 + 2, context.Allocated); // the values, then the element and its channel
        var values = new double[16];
        values[0] = 0.5;
        stage.Apply(values);
        Assert.Equal(0.5, values[0], 1e-6);

        // Overlapping positions (the same offset, shorter sizes) are different elements, which would store the same data
        // again and again
        Assert.Throws<UnsupportedImageFeatureException>(() => IccMpeParser.CreateStage(Stage(tint, [.. Enumerable.Range(0, 32).Select(i => tint.Length - (4 * i))]), 1, 1, new IccMpeParseContext("source", 0x41324231)));
    }

    [Fact]
    public void SegmentedCurvesCountTheSearchOfTheirSegment()
    {
        // 1,024 segments: the segment k gives k, between the break-points (k - 1) / 1024 and k / 1024
        var element = CurveSetElement(Segmented([.. Enumerable.Range(1, 1023).Select(i => i / 1024f)], [.. Enumerable.Range(0, 1024).Select(k => Formula(0, 1, 0, 0, k))]));
        foreach (var k in new[] { 0, 1, 511, 512, 1022, 1023 })
        {
            Assert.Equal(k, Evaluate(element, (k + 0.5) / 1024)[0]);
        }

        // The 10 steps of the binary search are part of the cost: a curve repeated by many positions exceeds the work per color
        Assert.Equal(4 + 10 + 1, ParseElement(element).Cost);
        IccMpeParser.CreateStage(Stage(element, [.. Enumerable.Repeat(element.Length, 1000)]), 1, 1, new IccMpeParseContext("source", 0x41324231));
        Assert.Throws<UnsupportedImageFeatureException>(() => IccMpeParser.CreateStage(Stage(element, [.. Enumerable.Repeat(element.Length, 100_000)]), 1, 1, new IccMpeParseContext("source", 0x41324231)));
    }

    [Fact]
    public void OverlappingCurveSetsChargeTheirChannels()
    {
        // A calculator whose sub-elements are the same curve set of 65,535 channels, which all share one curve: once is
        // supported, 32 overlapping positions (the same offset, 32 sizes) would store 32 arrays of 65,535 curves
        var once = CalculatorOfOverlappingCurveSets(1);
        IccMpeParser.CreateStage(Stage(once, [once.Length]), 1, 1, new IccMpeParseContext("source", 0x41324231));

        var overlapping = CalculatorOfOverlappingCurveSets(32);
        Assert.Throws<UnsupportedImageFeatureException>(() => IccMpeParser.CreateStage(Stage(overlapping, [overlapping.Length]), 1, 1, new IccMpeParseContext("source", 0x41324231)));

        static byte[] CalculatorOfOverlappingCurveSets(int subCount)
        {
            const int Channels = 65535;
            var curve = Segmented([], Formula(0, 1, 1, 0, 0));
            var curveSet = new List<byte>([.. "cvst"u8, 0, 0, 0, 0, .. U16(Channels), .. U16(Channels)]);
            for (var i = 0; i < Channels; i++)
            {
                curveSet.AddRange([.. U32((uint)(12 + (8 * Channels))), .. U32((uint)curve.Length)]);
            }

            curveSet.AddRange(curve);
            var main = new List<byte>([.. "func"u8, 0, 0, 0, 0, .. U32(2), .. "in  "u8, 0, 0, 0, 0, .. "out "u8, 0, 0, 0, 0]);
            var curveSetOffset = 24 + (8 * subCount) + main.Count;
            var data = new List<byte>([.. "calc"u8, 0, 0, 0, 0, .. U16(1), .. U16(1), 0, 0, .. U16(subCount), .. U32((uint)(24 + (8 * subCount))), .. U32((uint)main.Count)]);
            for (var i = 0; i < subCount; i++)
            {
                data.AddRange([.. U32((uint)curveSetOffset), .. U32((uint)(curveSet.Count + i))]);
            }

            data.AddRange(main);
            data.AddRange(curveSet);
            data.AddRange(new byte[subCount - 1]);
            return [.. data];
        }
    }

    [Fact]
    public void NestedCalculatorsReferringToOverlappingSubElementsAreBounded()
    {
        // Each calculator has 8 sub-elements at the position of the same nested calculator, with 8 different sizes: they
        // are 8 distinct elements, so that 2 levels parse 8 + 64 calculators
        var shallow = FanOut(2);
        IccMpeParser.CreateStage(Stage(shallow, [shallow.Length]), 1, 1, new IccMpeParseContext("source", 0x41324231));

        // 9 levels would parse 8^9 calculators from a few kilobytes
        var deep = FanOut(9);
        Assert.Throws<UnsupportedImageFeatureException>(() => IccMpeParser.CreateStage(Stage(deep, [deep.Length]), 1, 1, new IccMpeParseContext("source", 0x41324231)));

        static byte[] FanOut(int levels)
        {
            const int SubCount = 8;
            var element = CalculatorElement(1, 1, [], "in out");
            for (var level = 0; level < levels; level++)
            {
                var main = new List<byte>([.. "func"u8, 0, 0, 0, 0, .. U32(2), .. "in  "u8, 0, 0, 0, 0, .. "out "u8, 0, 0, 0, 0]);
                var childOffset = 24 + (8 * SubCount) + main.Count;
                var data = new List<byte>([.. "calc"u8, 0, 0, 0, 0, .. U16(1), .. U16(1), 0, 0, .. U16(SubCount), .. U32((uint)(24 + (8 * SubCount))), .. U32((uint)main.Count)]);
                for (var i = 0; i < SubCount; i++)
                {
                    data.AddRange([.. U32((uint)childOffset), .. U32((uint)(element.Length + i))]);
                }

                data.AddRange(main);
                data.AddRange(element);
                data.AddRange(new byte[SubCount - 1]);
                element = [.. data];
            }

            return element;
        }
    }

    [Fact]
    public void CalculatorCopiesCountTheirValues()
    {
        // tget 10 values, pop, in, out, then the 10 temporary channels and the output cleared before each evaluation
        Assert.Equal(10 + 1 + 1 + 1 + 10 + 1, ParseElement(CalculatorElement(1, 1, [], "tget:0:9 pop:9 in out")).Cost);

        // 20 copies of 65,535 temporary channels exceed the work per color
        var copies = CalculatorElement(1, 1, [], string.Join(' ', Enumerable.Repeat("tget:0:65534 pop:65534", 20)) + " in out");
        Assert.Throws<UnsupportedImageFeatureException>(() => IccMpeParser.CreateStage(Stage(copies, [copies.Length]), 1, 1, new IccMpeParseContext("source", 0x41324231)));
    }

    [Fact]
    public void CalculatorFunctionsAreCheckedBeforeTheyRun()
    {
        // Malformed: the stack underflows, a channel does not exist, an operation of a structure is alone or goes beyond
        // the function, the streams of a condition leave different numbers of values
        foreach (var function in new[] { "add out", "1 out:1", "in:1 out", "tget:0 out:0:1", "else:0 1 out", "case:0 1 out", "1 sel 1 out", "1 if:5 out", "1 if:1 else:0 5 out", "1 1 sel case:1 case:0 dflt:0 5 out", "1 2 3 4 solv:1:1 out", "tran:65535:65535 1 out" })
        {
            Assert.Throws<InvalidImageContentException>(() => Calculate(1, function));
        }

        // Not supported: an unknown operation, the color appearance operations, a stream that may not run and changes the
        // number of values on the stack (valid, but the number of values would depend on the colors)
        Assert.Throws<UnsupportedImageFeatureException>(() => Calculate(1, "1 zzzz out"));
        Assert.Throws<UnsupportedImageFeatureException>(() => Calculate(1, "1 if:1 5 out"));
        Assert.Throws<UnsupportedImageFeatureException>(() => Calculate(1, "9 0 sel case:1 pop out"));
        Assert.Throws<UnsupportedImageFeatureException>(() => Calculate(3, "1 1 1 tJab out:0:2"));

        // Calculators nested deeper than 16 levels
        var nested = CalculatorElement(1, 1, [], "in out");
        for (var i = 0; i < 17; i++)
        {
            nested = CalculatorElement(1, 1, [nested], "in calc:0 out");
        }

        Assert.Throws<UnsupportedImageFeatureException>(() => ParseElement(nested));
    }

    [Fact]
    public void CurveSetElementsEvaluateTheirSegments()
    {
        // ICC.2:2023 Table 111 at x = 0.5, with parameters chosen for round results
        (int Function, float[] Parameters, double Expected)[] formulas =
        [
            (0, [2, 1, 0.5f, 0.25f], 1.25),                                  // (a x + b)^g + c
            (1, [2, 1, 4, 1, 0.5f], Math.Log10(2) + 0.5),                    // a log10(b x^g + c) + d
            (2, [2, 3, 2, -1, 0.5f], 2.5),                                   // a b^(c x + d) + e
            (3, [2, 2, 2, 1, -1], 7),                                        // a (b x + c)^g + d
            (4, [1, 2, -1, 1, 2], (2 * Math.Log(2)) + 1),                    // a ln(d x^g - b) + c
            (5, [1, 2, 1, 0, 2, 3], (3 * Math.Exp(0.5)) + 1),                // e exp((d x^g - c) / a) + b
            (6, [2, 1, 0.25f, 1, 1, 4, 1], 1),                               // d (max(e x^g - a, 0) / (b - c x^g))^w
            (7, [2, 1, 1, 2, 1, 3], 3 * Math.Pow(2 / 1.5, 2)),               // d ((a + b x^g) / (1 + c x^g))^w
        ];
        foreach (var (function, parameters, expected) in formulas)
        {
            Assert.Equal(expected, Evaluate(CurveSetElement(Segmented([], Formula(function, parameters))), 0.5)[0], 1e-6);
        }

        // A sampled segment between the break-points 0 and 1: its first value is the one of the previous segment at 0
        var segmented = CurveSetElement(Segmented([0, 1], Formula(0, 1, 1, 0, 0), Sampled(0.1f, 1), Formula(0, 1, 0, 1, 0)));
        AssertClose([-1, 0, 0.05, 0.1, 0.55, 1, 1], new[] { -1, 0, 0.25, 0.5, 0.75, 1, 5 }.Select(x => Evaluate(segmented, x)[0]), 1e-7);

        // Single sampled curves: clipped or extended linearly outside their range; integers encode [0, 1]
        AssertClose([0, 1.25, 2], new[] { -1, 0.75, 2 }.Select(x => Evaluate(CurveSetElement(SingleSampled(0, 0, 0, 0.5f, 2)), x)[0]), 1e-7);
        AssertClose([-1, 5], new[] { -1.0, 2 }.Select(x => Evaluate(CurveSetElement(SingleSampled(1, 0, 0, 0.5f, 2)), x)[0]), 1e-6);
        Assert.Equal(32768 / 65535.0, Evaluate(CurveSetElement(SingleSampled(0, 2, 0, 32768, 65535)), 0.5)[0], 1e-12);

        // A sampled calculator curve: the calculator sampled at 3 points, 0, 0.5 and 1, interpolated linearly
        Assert.Equal(0.625, Evaluate(CurveSetElement(SampledCalculator(3, CalculatorElement(1, 1, [], "in sq out"))), 0.75)[0], 1e-7);

        // Malformed: decreasing break-points, a sampled first segment, an unknown function type
        Assert.Throws<InvalidImageContentException>(() => ParseElement(CurveSetElement(Segmented([1, 0], Formula(0, 1, 1, 0, 0), Formula(0, 1, 1, 0, 0), Formula(0, 1, 1, 0, 0)))));
        Assert.Throws<InvalidImageContentException>(() => ParseElement(CurveSetElement(Segmented([0], Sampled(1), Formula(0, 1, 1, 0, 0)))));
        Assert.Throws<InvalidImageContentException>(() => ParseElement(CurveSetElement(Segmented([], Formula(8, 1, 1, 0, 0)))));
    }

    [Fact]
    public void MatrixLookupTableAndTintElementsFollowIcc2Clause11()
    {
        // Q rows of P coefficients, then the offsets
        Assert.Equal([1.75], Evaluate(Element("matf", 2, 1, Floats(1, 2, 0.5f)), 0.25, 0.5));

        // A table that is 1 at (1, 1, 1) only: multilinear without hint, tetrahedral (along the main diagonal) with hint 2
        float[] corner = [0, 0, 0, 0, 0, 0, 0, 1];
        AssertClose([0.09375], Evaluate(ClutElement([2, 2, 2], 1, 0, corner), 0.5, 0.25, 0.75), 1e-12);
        AssertClose([0.25], Evaluate(ClutElement([2, 2, 2], 1, 2, corner), 0.5, 0.25, 0.75), 1e-12);
        Assert.Equal([1], Evaluate(ClutElement([2, 2, 2], 1, 0, corner), 2, 2, 2));

        // Tint arrays: equally spaced vectors from tint 0 to tint 1, clipped outside
        var tint = Element("tint", 1, 2, U32(0), U32(0), Floats(0, 0, 1, 2, 2, 0));
        Assert.Equal([0.5, 1], Evaluate(tint, 0.25));
        Assert.Equal([1.5, 1], Evaluate(tint, 0.75));
        Assert.Equal([0, 0], Evaluate(tint, -1));

        // 'bACS' and 'eACS' pass their values through; the elements of the other domains are not supported
        Assert.Equal([0.25, 0.5], Evaluate(Element("bACS", 2, 2, U32(0)), 0.25, 0.5));
        foreach (var signature in new[] { "eclt", "emtx", "iemx", "rclt", "robs", "eobs", "xclt", "smet", "tmap", "JtoX", "XtoJ", "zzzz" })
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => ParseElement(Element(signature, 3, 3, new byte[64])));
        }

        Assert.Throws<InvalidImageContentException>(() => ParseElement(Element("matf", 3, 3, Floats(1, 2))));
        Assert.Throws<InvalidImageContentException>(() => ParseElement(ClutElement([2, 1], 1, 0, 0, 0)));
    }

    [Fact]
    public void IccMaxProfilesConvertWithTheirProcessingElements()
    {
        // A version 5 display profile whose AToB1 and BToA1 tags are the matrix of the sRGB primaries (no tone curve): the
        // same colors as the version 4 matrix-based profile with identity curves
        var colorants = IccTestProfiles.SrgbColorants;
        float[] matrix = [.. new[] { colorants[0][0], colorants[1][0], colorants[2][0], colorants[0][1], colorants[1][1], colorants[2][1], colorants[0][2], colorants[1][2], colorants[2][2] }.Select(value => (float)value)];
        var inverse = new double[9];
        Assert.True(IccMatrixStage.TryInvert([.. matrix.Select(value => (double)value)], inverse));
        var v5 = IccMaxProfile("mntr", "RGB ", "XYZ ", ("A2B1", Mpe(3, 3, Element("matf", 3, 3, Floats([.. matrix, 0, 0, 0])))), ("B2A1", Mpe(3, 3, Element("matf", 3, 3, Floats([.. inverse.Select(value => (float)value), 0, 0, 0])))));
        var v4 = IccTestProfiles.Rgb(colorants, IccTestProfiles.Curve());
        float[] colors = [0, 0, 0, 1, 1, 1, 0.25f, 0.5f, 0.75f, 1, 0, 0];
        var fromV5 = new float[colors.Length];
        IccColorTransform.Create(v5, v4, NoCompensation).Convert(colors, fromV5);
        AssertClose(colors, fromV5, 1e-4);
        var toV5 = new float[colors.Length];
        IccColorTransform.Create(v4, v5, NoCompensation).Convert(colors, toV5);
        AssertClose(colors, toV5, 1e-4);

        // Not supported: a spectral connection space, custom connection conditions, the classes of iccMAX only, version 6
        AssertUnsupported(IccMaxProfile("mntr", "RGB ", "\0\0\0\0", ("A2B1", Mpe(3, 3, Element("matf", 3, 3, Floats([.. matrix, 0, 0, 0]))))));
        AssertUnsupported(IccMaxProfile("mntr", "RGB ", "XYZ ", ("A2B1", Mpe(3, 3, Element("matf", 3, 3, Floats([.. matrix, 0, 0, 0])))), ("c2sp", Mpe(3, 3, Element("bACS", 3, 3, U32(0))))));
        foreach (var profileClass in new[] { "cenc", "mid ", "mlnk", "mvis" })
        {
            AssertUnsupported(IccMaxProfile(profileClass, "RGB ", "XYZ ", ("A2B1", Mpe(3, 3, Element("matf", 3, 3, Floats([.. matrix, 0, 0, 0]))))));
        }

        // Malformed: a multiProcessElementsType whose channels are not the ones of the profile
        Assert.Throws<InvalidImageContentException>(() => IccColorTransform.Create(IccMaxProfile("mntr", "RGB ", "XYZ ", ("A2B1", Mpe(1, 3, Element("matf", 1, 3, Floats(1, 1, 1, 0, 0, 0))))), IccProfile.Srgb));

        static void AssertUnsupported(IccProfile profile) => Assert.Throws<UnsupportedImageFeatureException>(() => IccColorTransform.Create(profile, IccProfile.Srgb));
    }

    [Fact]
    public void IccMaxRenderingIntentsSelectTheirTagsAsIcc2Section8_14_2()
    {
        // Gray output profiles whose tags give Y = k x for a gray x, with the media white point Y 0.75; the destination is
        // a linear gray display, so the converted gray is Y. Each tag has its own k: 0.2 (AToB0), 0.4 (AToB1), 0.3 (AToB3)
        var a2b0 = ("A2B0", GrayTag(0.2f));
        var a2b1 = ("A2B1", GrayTag(0.4f));
        var a2b3 = ("A2B3", GrayTag(0.3f));
        var all = IccMaxProfile("prtr", "GRAY", "XYZ ", a2b0, a2b1, a2b3);
        Assert.Equal(0.2, Convert(all, IccRenderingIntent.Perceptual), 1e-6);
        Assert.Equal(0.4, Convert(all, IccRenderingIntent.RelativeColorimetric), 1e-6);
        Assert.Equal(0.2, Convert(all, IccRenderingIntent.Saturation), 1e-6);

        // ICC-absolute: the AToB3 tag, converted to the media white of the destination (the illuminant)
        Assert.Equal(0.3, Convert(all, IccRenderingIntent.AbsoluteColorimetric), 1e-6);

        // Without the tag of the intent: the media-relative colorimetry of the AToB3 tag (divided by the media white),
        // the ICC-absolute colorimetry of the AToB1 tag (multiplied by it), the colorimetric tags for the perceptual intent
        var absoluteOnly = IccMaxProfile("prtr", "GRAY", "XYZ ", a2b3);
        Assert.Equal(0.4, Convert(absoluteOnly, IccRenderingIntent.RelativeColorimetric), 1e-6);
        Assert.Equal(0.4, Convert(absoluteOnly, IccRenderingIntent.Perceptual), 1e-6);
        Assert.Equal(0.3, Convert(absoluteOnly, IccRenderingIntent.AbsoluteColorimetric), 1e-6);
        var relativeOnly = IccMaxProfile("prtr", "GRAY", "XYZ ", a2b1);
        Assert.Equal(0.3, Convert(relativeOnly, IccRenderingIntent.AbsoluteColorimetric), 1e-6);
        Assert.Equal(0.4, Convert(relativeOnly, IccRenderingIntent.Saturation), 1e-6);

        // A version 4 profile has no AToB3 tag: it is ignored
        var version4 = IccTestProfiles.BuildBytes("GRAY", "XYZ ", [("wtpt", IccTestProfiles.Xyz(0.75 * 0.9642, 0.75, 0.75 * 0.8249)), a2b0, a2b3], header => "prtr"u8.CopyTo(header.AsSpan(12)));
        Assert.Equal(0.2, Convert(new IccProfile(new MetadataBlob(version4)), IccRenderingIntent.RelativeColorimetric), 1e-6);

        static byte[] GrayTag(float k) => Mpe(1, 3, Element("matf", 1, 3, Floats(0.9642f * k, k, 0.8249f * k, 0, 0, 0)));

        static double Convert(IccProfile profile, IccRenderingIntent intent)
        {
            var output = new float[1];
            IccColorTransform.Create(profile, IccTestProfiles.Gray(IccTestProfiles.Curve()), new IccColorTransformOptions { Intent = intent, BlackPointCompensation = false }).Convert([1f], output);
            return output[0];
        }
    }

    private static IccProfile IccMaxProfile(string profileClass, string colorSpace, string connectionSpace, params (string Signature, byte[] Data)[] tags)
    {
        var data = IccTestProfiles.BuildBytes(colorSpace, connectionSpace, [("wtpt", IccTestProfiles.Xyz(0.75 * 0.9642, 0.75, 0.75 * 0.8249)), .. tags], header =>
        {
            header[8] = 5;
            header[9] = 0;
            Encoding.ASCII.GetBytes(profileClass).CopyTo(header, 12);
        });
        return new IccProfile(new MetadataBlob(data));
    }

    private static byte[] NamedProfile(string connectionSpace, byte[] tag, string colorSpace = "RGB ")
        => IccTestProfiles.BuildBytes(colorSpace, connectionSpace, [("wtpt", IccTestProfiles.Xyz(0.9642, 1, 0.8249)), ("ncl2", tag)], header => "nmcl"u8.CopyTo(header.AsSpan(12)));

    private static IccProfile IccMaxNamedProfile(params byte[][] colors) => IccMaxNamedProfile("Lab ", includeTintZero: true, colors);

    private static IccProfile IccMaxNamedProfile(string connectionSpace, params byte[][] colors) => IccMaxNamedProfile(connectionSpace, includeTintZero: true, colors);

    // A namedColorTag (ICC.2:2023 section 9.2.104): a tagArrayType of tagStructType elements
    private static IccProfile IccMaxNamedProfile(string connectionSpace, bool includeTintZero, params byte[][] colors) => IccMaxNamedProfile(connectionSpace, includeTintZero, [], colors);

    private static IccProfile IccMaxNamedProfile(string connectionSpace, bool includeTintZero, (string Signature, byte[] Data)[] otherTags, params byte[][] colors)
    {
        byte[][] elements = includeTintZero ? [Struct("tnt0", ("pcs ", Floats("fl32", 100, 0, 0))), .. colors] : colors;
        return IccMaxNamedProfileWithArray(connectionSpace, Container("tary", "ncol", elements.Select(element => ((string?)null, element)).ToArray()), otherTags);
    }

    private static IccProfile IccMaxNamedProfileWithArray(string connectionSpace, byte[] array, (string Signature, byte[] Data)[] otherTags)
    {
        var data = IccTestProfiles.BuildBytes("CMYK", connectionSpace, [("wtpt", IccTestProfiles.Xyz(0.9642, 1, 0.8249)), ("nmcl", array), .. otherTags], header =>
        {
            header[8] = 5;
            header[9] = 0;
            "nmcl"u8.CopyTo(header.AsSpan(12));
        });
        return new IccProfile(new MetadataBlob(data));
    }

    private static byte[] Struct(string structType, params (string Signature, byte[] Data)[] members) => Container("tstr", structType, members.Select(member => ((string?)member.Signature, member.Data)).ToArray());

    // A tagArrayType (positions only) or a tagStructType (signature and position of each member), then the data, 4-byte aligned
    private static byte[] Container(string type, string identifier, (string? Signature, byte[] Data)[] items)
    {
        var entrySize = items.Length > 0 && items[0].Signature is not null ? 12 : 8;
        var data = new List<byte>();
        data.AddRange(Encoding.ASCII.GetBytes(type));
        data.AddRange(new byte[4]);
        data.AddRange(Encoding.ASCII.GetBytes(identifier));
        data.AddRange(U32((uint)items.Length));
        var table = data.Count;
        data.AddRange(new byte[entrySize * items.Length]);
        var bytes = data;
        for (var i = 0; i < items.Length; i++)
        {
            while (bytes.Count % 4 != 0)
            {
                bytes.Add(0);
            }

            var entry = new List<byte>();
            if (items[i].Signature is { } signature)
            {
                entry.AddRange(Encoding.ASCII.GetBytes(signature));
            }

            entry.AddRange(U32((uint)bytes.Count));
            entry.AddRange(U32((uint)items[i].Data.Length));
            for (var j = 0; j < entry.Count; j++)
            {
                bytes[table + (entrySize * i) + j] = entry[j];
            }

            bytes.AddRange(items[i].Data);
        }

        return [.. bytes];
    }

    private static byte[] Utf8(string text) => [.. "utf8"u8, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)];

    // A numeric array type: fl32, fl16, ui16 (the values are fractions of 65535) or ui08
    private static byte[] Floats(string type, params float[] values)
    {
        var data = new List<byte>(Encoding.ASCII.GetBytes(type)) { 0, 0, 0, 0 };
        foreach (var value in values)
        {
            var bytes = new byte[type switch { "fl32" => 4, "ui08" => 1, _ => 2 }];
            switch (type)
            {
                case "fl32":
                    BinaryPrimitives.WriteSingleBigEndian(bytes, value);
                    break;
                case "fl16":
                    BinaryPrimitives.WriteHalfBigEndian(bytes, (Half)value);
                    break;
                case "ui16":
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)Math.Round(value * 65535, MidpointRounding.AwayFromZero));
                    break;
                default:
                    bytes[0] = (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
                    break;
            }

            data.AddRange(bytes);
        }

        return [.. data];
    }

    private static byte[] Floats(params float[] values)
    {
        var data = new byte[4 * values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(4 * i), values[i]);
        }

        return data;
    }

    private static byte[] U32(uint value)
    {
        var data = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        return data;
    }

    private static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];

    // A processing element: its signature, reserved bytes, its channels, then its data (ICC.2:2023 Table 84)
    private static byte[] Element(string signature, int inputs, int outputs, params byte[][] parts)
        => [.. Encoding.ASCII.GetBytes(signature), 0, 0, 0, 0, .. U16(inputs), .. U16(outputs), .. parts.SelectMany(part => part)];

    // A multiProcessElementsType (ICC.2:2023 Table 63)
    private static byte[] Mpe(int inputs, int outputs, params byte[][] elements) => Positioned([.. "mpet"u8, 0, 0, 0, 0, .. U16(inputs), .. U16(outputs), .. U32((uint)elements.Length)], elements);

    // A multiProcessElementsType of one channel whose elements all start at the same offset, with these sizes
    private static byte[] Stage(byte[] element, int[] sizes)
    {
        var offset = 16 + (8 * sizes.Length);
        var data = new List<byte>([.. "mpet"u8, 0, 0, 0, 0, .. U16(1), .. U16(1), .. U32((uint)sizes.Length)]);
        foreach (var size in sizes)
        {
            data.AddRange([.. U32((uint)offset), .. U32((uint)size)]);
        }

        data.AddRange(element);
        return [.. data];
    }

    // A header, a table of positions (offset and size relative to the start), then the items, 4-byte aligned
    private static byte[] Positioned(byte[] header, byte[][] items, byte[]? beforeItems = null)
    {
        var data = new List<byte>(header);
        var table = data.Count;
        data.AddRange(new byte[8 * items.Length]);
        data.AddRange(beforeItems ?? []);
        for (var i = 0; i < items.Length; i++)
        {
            while (data.Count % 4 != 0)
            {
                data.Add(0);
            }

            var position = U32((uint)data.Count).Concat(U32((uint)items[i].Length)).ToArray();
            for (var j = 0; j < 8; j++)
            {
                data[table + (8 * i) + j] = position[j];
            }

            data.AddRange(items[i]);
        }

        return [.. data];
    }

    private static byte[] CurveSetElement(params byte[][] curves) => Positioned([.. "cvst"u8, 0, 0, 0, 0, .. U16(curves.Length), .. U16(curves.Length)], curves);

    private static byte[] Segmented(float[] breakPoints, params byte[][] segments)
        => [.. "curf"u8, 0, 0, 0, 0, .. U16(segments.Length), 0, 0, .. Floats(breakPoints), .. segments.SelectMany(segment => segment)];

    private static byte[] Formula(int function, params float[] parameters) => [.. "parf"u8, 0, 0, 0, 0, .. U16(function), 0, 0, .. Floats(parameters)];

    private static byte[] Sampled(params float[] values) => [.. "samf"u8, 0, 0, 0, 0, .. U32((uint)values.Length), .. Floats(values)];

    // A single sampled curve over [0, 1] (ICC.2:2023 Table 108); the encoding 2 stores the values as 16-bit integers
    private static byte[] SingleSampled(int extension, int encoding, params float[] values)
        => [.. "sngf"u8, 0, 0, 0, 0, .. U32((uint)values.Length), .. Floats(0, 1), .. U16(extension), .. U16(encoding), .. encoding == 2 ? values.SelectMany(value => U16((int)value)) : Floats(values)];

    private static byte[] SampledCalculator(int samples, byte[] calculator) => [.. "clcf"u8, 0, 0, 0, 0, .. U32((uint)samples), .. Floats(0, 1), 0, 0, 0, 0, .. calculator];

    private static byte[] ClutElement(int[] gridPoints, int outputs, byte hint, params float[] entries)
    {
        var grid = new byte[16];
        for (var i = 0; i < gridPoints.Length; i++)
        {
            grid[i] = (byte)gridPoints[i];
        }

        return [.. "clut"u8, 0, 0, 0, hint, .. U16(gridPoints.Length), .. U16(outputs), .. grid, .. Floats(entries)];
    }

    // A calculator element (ICC.2:2023 Table 85) whose main function is written as operations separated by spaces: a
    // number is a 'data' operation, "op:S:T" is an operation and its two 16-bit parameters (S and T of Tables 88 to 102,
    // not the counts of the text form of iccDEV), "if:N", "else:N", "case:N" and "dflt:N" a number of operations, and
    // "env:name" a CMM environment variable
    private static byte[] CalculatorElement(int inputs, int outputs, byte[][] subElements, string function)
    {
        var operations = function.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var main = new List<byte>([.. "func"u8, 0, 0, 0, 0, .. U32((uint)operations.Length)]);
        foreach (var operation in operations)
        {
            if (operation == "NaN" || float.TryParse(operation, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                main.AddRange([.. "data"u8, .. Floats(float.Parse(operation, NumberStyles.Float, CultureInfo.InvariantCulture))]);
                continue;
            }

            var parts = operation.Split(':');
            main.AddRange(Encoding.ASCII.GetBytes(parts[0].PadRight(4)));
            if (parts[0] == "env")
            {
                main.AddRange(Encoding.ASCII.GetBytes(parts[1]));
            }
            else if (parts[0] is "if" or "else" or "case" or "dflt")
            {
                main.AddRange(U32(uint.Parse(parts[1], CultureInfo.InvariantCulture)));
            }
            else
            {
                main.AddRange(U16(parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0));
                main.AddRange(U16(parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0));
            }
        }

        // The main function, then the sub-elements
        var header = new List<byte>([.. "calc"u8, 0, 0, 0, 0, .. U16(inputs), .. U16(outputs), 0, 0, .. U16(subElements.Length)]);
        var data = Positioned([.. header, .. U32((uint)(24 + (8 * subElements.Length))), .. U32((uint)main.Count)], subElements, [.. main]);
        return data;
    }

    private static IccMpeElement ParseElement(byte[] element) => IccMpeParser.ParseElement(element, new IccMpeParseContext("source", 0x41324231));

    private static double[] Evaluate(byte[] element, params double[] input)
    {
        var parsed = ParseElement(element);
        var output = new double[parsed.OutputCount];
        parsed.Apply(input, output, new double[parsed.ScratchSize]);
        return output;
    }

    private static void AssertClose(IEnumerable<double> expected, IEnumerable<double> actual, double tolerance)
    {
        var expectedValues = expected.ToArray();
        var actualValues = actual.ToArray();
        Assert.HasCount(expectedValues.Length, actualValues);
        for (var i = 0; i < expectedValues.Length; i++)
        {
            Assert.Equal(expectedValues[i], actualValues[i], tolerance);
        }
    }

    private static void AssertClose(IEnumerable<float> expected, IEnumerable<float> actual, double tolerance) => AssertClose(expected.Select(value => (double)value), actual.Select(value => (double)value), tolerance);

    private static double[] Calculate(int outputs, string function, double[]? input = null, byte[][]? subElements = null)
        => Evaluate(CalculatorElement(Math.Max(1, input?.Length ?? 1), outputs, subElements ?? [], function), input ?? [0]);
}
