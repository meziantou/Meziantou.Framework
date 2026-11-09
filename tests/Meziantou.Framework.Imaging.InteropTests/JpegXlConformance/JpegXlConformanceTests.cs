using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.InteropTests.JpegXlConformance;

/// <summary>
/// JPEG XL decoding against the conformance corpus of the JPEG XL project (<see cref="JpegXlConformanceCorpus"/>): every
/// case is either decoded and compared with its reference decoding, or rejected as an unsupported feature that
/// <c>readme.md</c> documents.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is the one of the <c>--lax</c> mode of the corpus's <c>scripts/conformance.py</c>, meant for decoders
/// that do not return floating-point samples: both images are clamped to [0, 1], then the largest error of a sample and
/// the largest root mean square error of a channel are compared with the tolerances of each frame. The reference
/// decoding is in the color space of its <c>reference.icc</c>; the library returns its samples in the color encoding of
/// the file. That is the color space of the reference profile (the expected label is checked) except for the images stored
/// in XYB with an ICC profile, whose references are in linear sRGB: their samples are converted to it, as the script
/// converts with lcms2, through the colorants and the tone curves of both profiles (<see cref="IccMatrixShaperConversion"/>,
/// independent of the library).
/// </para>
/// <para>
/// A profile the library writes for an enumerated color encoding is compared with <c>reference.icc</c> by its colorants and
/// its curves, not by its bytes: the library adapts the white of the image to the illuminant of the connection space
/// (Z = 0.8249, so that the device white is that illuminant exactly, as ICC.1 defines it), where libjxl adapts it to the
/// D50 of ASTM E308 (Z = 0.82521). Their colorants differ by up to 4e-4, the tolerance of the comparison.
/// </para>
/// <para>
/// The library rounds every frame to the depth of the image (8 or 16 bits), and the tolerances of the corpus are smaller
/// than the rounding of 8-bit samples (1/510): the limits add half a step to the largest error and the root mean square
/// of a uniform rounding, a step divided by the square root of 12, to the other; both carried through the conversion of
/// the samples, if any, by its derivatives. djxl passes every case and jxl-oxide all but six (gamut mapping) under the
/// same comparison of floating-point outputs.
/// </para>
/// </remarks>
public sealed class JpegXlConformanceTests
{
    /// <summary>The color space in which the library returns the samples of a case.</summary>
    public enum OutputColor
    {
        /// <summary>sRGB, without profile: <c>reference.icc</c> is the sRGB profile of libjxl.</summary>
        Srgb,

        /// <summary>The sRGB primaries and the BT.709 transfer function, as a CICP description (1/1/0): so is <c>reference.icc</c>.</summary>
        Bt709,

        /// <summary>The ICC profile of the file, returned byte for byte: it is <c>original.icc</c> and <c>reference.icc</c>.</summary>
        EmbeddedIcc,

        /// <summary>A profile the library writes for the enumerated color encoding of the file: equivalent to <c>reference.icc</c>.</summary>
        SynthesizedIcc,

        /// <summary>
        /// The ICC profile of the file (<c>original.icc</c>), returned byte for byte, while <c>reference.icc</c> is another color
        /// space: the samples are converted to it.
        /// </summary>
        ConvertedIcc,
    }

    public static TheoryData<string, OutputColor> DecodedCases => new()
    {
        { "alpha_nonpremultiplied", OutputColor.Srgb },
        { "alpha_triangles", OutputColor.Srgb },
        { "animation_icos4d", OutputColor.Srgb },
        { "animation_icos4d_5", OutputColor.Srgb },
        { "animation_newtons_cradle", OutputColor.Srgb },
        { "animation_spline", OutputColor.Srgb },
        { "animation_spline_5", OutputColor.Srgb },
        { "bench_oriented_brg", OutputColor.EmbeddedIcc },
        { "bench_oriented_brg_5", OutputColor.EmbeddedIcc },
        { "bicycles", OutputColor.Srgb },
        { "bike", OutputColor.Bt709 },
        { "bike_5", OutputColor.Bt709 },
        { "blendmodes", OutputColor.Srgb },
        { "blendmodes_5", OutputColor.Srgb },
        { "cafe", OutputColor.EmbeddedIcc },
        { "cafe_5", OutputColor.EmbeddedIcc },
        { "delta_palette", OutputColor.Srgb },
        { "grayscale", OutputColor.ConvertedIcc },
        { "grayscale_5", OutputColor.ConvertedIcc },
        { "grayscale_jpeg", OutputColor.EmbeddedIcc },
        { "grayscale_jpeg_5", OutputColor.EmbeddedIcc },
        { "lossless_pfm", OutputColor.Srgb },
        { "lz77_flower", OutputColor.SynthesizedIcc },
        { "mul_no_extra_channels", OutputColor.Srgb },
        { "noise", OutputColor.Srgb },
        { "noise_5", OutputColor.Srgb },
        { "opsin_inverse", OutputColor.Srgb },
        { "opsin_inverse_5", OutputColor.Srgb },
        { "patches", OutputColor.ConvertedIcc },
        { "patches_5", OutputColor.ConvertedIcc },
        { "patches_lossless", OutputColor.EmbeddedIcc },
        { "progressive", OutputColor.ConvertedIcc },
        { "progressive_5", OutputColor.ConvertedIcc },
        { "sunset_logo", OutputColor.Srgb },
        { "upsampling", OutputColor.Srgb },
        { "upsampling_5", OutputColor.Srgb },
    };

    /// <summary>The cases the library rejects, with a part of the message that names the feature (see the limitations of <c>readme.md</c>).</summary>
    public static TheoryData<string, string> UnsupportedCases => new()
    {
        { "alpha_premultiplied", "premultiplied alpha" },
        { "grayscale_public_university", "grayscale Modular frame with restoration filters" },
        { "spot", "spot color" },
    };

    public static TheoryData<string> CasesWithReconstructedJpeg => ["bench_oriented_brg", "bench_oriented_brg_5", "cafe", "cafe_5", "grayscale_jpeg", "grayscale_jpeg_5"];

    /// <summary>The CMYK cases: their references hold the inks, in the CMYK color space of <c>reference.icc</c>.</summary>
    public static TheoryData<string> CmykCases => ["cmyk_layers"];

    [Fact]
    public void EveryCaseOfTheCorpusIsListed()
    {
        var listed = DecodedCases.Select(row => row.Data.Item1).Concat(UnsupportedCases.Select(row => row.Data.Item1)).Concat(CmykCases.Select(row => row.Data)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(JpegXlConformanceCorpus.CaseNames.Order(StringComparer.Ordinal).ToArray(), listed);
    }

    [Theory]
    [MemberData(nameof(DecodedCases))]
    public async Task DecodesWithinTheToleranceOfTheReference(string name, OutputColor color)
    {
        var testCase = await JpegXlConformanceCorpus.GetAsync(name, XunitCancellationToken);
        using var image = Image.Load(testCase.InputPath);
        var referenceIcc = await File.ReadAllBytesAsync(await testCase.GetReferenceIccAsync(XunitCancellationToken), XunitCancellationToken);
        var metadata = image.Metadata;
        IccMatrixShaperConversion? conversion = null;
        switch (color)
        {
            case OutputColor.Srgb:
                Assert.Null(metadata.IccProfile);
                Assert.Equal(ColorTransferFunction.Srgb, metadata.TransferFunction);
                Assert.True(metadata.Cicp is null || (metadata.Cicp.ColorPrimaries, metadata.Cicp.TransferCharacteristics) == (1, 13), $"Unexpected CICP description {metadata.Cicp}.");
                break;

            case OutputColor.Bt709:
                Assert.Null(metadata.IccProfile);
                Assert.Equal(new CicpColorDescription(1, 1, 0, fullRange: true), metadata.Cicp);
                break;

            case OutputColor.EmbeddedIcc:
                Assert.NotNull(metadata.IccProfile);
                var originalIcc = await File.ReadAllBytesAsync(await testCase.GetOriginalIccAsync(XunitCancellationToken), XunitCancellationToken);
                Assert.Equal(originalIcc, metadata.IccProfile.Data.ToArray());
                Assert.Equal(referenceIcc, originalIcc);
                break;

            case OutputColor.SynthesizedIcc:
                Assert.NotNull(metadata.IccProfile);
                AssertEquivalentProfiles(referenceIcc, metadata.IccProfile.Data.ToArray());
                break;

            case OutputColor.ConvertedIcc:
                Assert.NotNull(metadata.IccProfile);
                var fileIcc = await File.ReadAllBytesAsync(await testCase.GetOriginalIccAsync(XunitCancellationToken), XunitCancellationToken);
                Assert.Equal(fileIcc, metadata.IccProfile.Data.ToArray());
                conversion = IccMatrixShaperConversion.Create(fileIcc, referenceIcc, IsSixteenBit(image.PixelFormat) ? ushort.MaxValue : byte.MaxValue);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(color), color, message: null);
        }

        // The reference decoding is turned as the header says
        if (metadata.Orientation != ExifOrientation.TopLeft)
        {
            image.AutoOrient(XunitCancellationToken);
        }

        var referencePath = await testCase.GetReferenceImageAsync(XunitCancellationToken);
        Assert.Empty(Compare(testCase, image, referencePath, conversion));
    }

    /// <summary>
    /// The library converts the inks of a CMYK image with the options of the load: the reference inks (1 is no ink in JPEG
    /// XL, 0 in ICC), rounded to the depth of the image, go through the same conversion, from <c>reference.icc</c> (the
    /// profile of the file) to sRGB, and must give the colors of the library exactly. The conversion is the color management
    /// of the library, tested on its own; this compares the inks the decoder gives it, layers composited.
    /// </summary>
    [Theory]
    [MemberData(nameof(CmykCases))]
    public async Task DecodesTheInksOfTheReference(string name)
    {
        var testCase = await JpegXlConformanceCorpus.GetAsync(name, XunitCancellationToken);
        var conversion = new CmykConversionOptions();
        using var image = Image.Load(testCase.InputPath, new ImageDecodeOptions { CmykConversion = conversion });
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        Assert.Equal(conversion.DestinationProfile.Data.ToArray(), image.Metadata.IccProfile!.Data.ToArray());

        var referenceIcc = await File.ReadAllBytesAsync(await testCase.GetReferenceIccAsync(XunitCancellationToken), XunitCancellationToken);
        var transform = IccColorTransform.Create(new IccProfile(new MetadataBlob(referenceIcc)), conversion.DestinationProfile, conversion.Transform);
        using var reference = new JpegXlConformanceCase.NpyFrameReader(await testCase.GetReferenceImageAsync(XunitCancellationToken));
        Assert.Equal((testCase.Frames.Count, image.Height, image.Width, 5), (reference.FrameCount, reference.Height, reference.Width, reference.ChannelCount));
        Assert.Equal(testCase.Frames.Count, image.Frames.Count);

        var pixelCount = image.Width * image.Height;
        var samples = new float[pixelCount * 5];
        var inks = new byte[pixelCount * 4];
        var colors = new byte[pixelCount * 3];
        var actual = new byte[pixelCount * 4];
        for (var frame = 0; frame < testCase.Frames.Count; frame++)
        {
            reference.ReadFrame(samples);
            for (var pixel = 0; pixel < pixelCount; pixel++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    inks[(pixel * 4) + channel] = (byte)(byte.MaxValue - ToByte(samples[(pixel * 5) + channel]));
                }
            }

            transform.Convert(inks, colors);
            image.Frames[frame].CopyPixelBytesTo(actual);
            for (var pixel = 0; pixel < pixelCount; pixel++)
            {
                var expected = (colors[pixel * 3], colors[(pixel * 3) + 1], colors[(pixel * 3) + 2], ToByte(samples[(pixel * 5) + 4]));
                var decoded = (actual[pixel * 4], actual[(pixel * 4) + 1], actual[(pixel * 4) + 2], actual[(pixel * 4) + 3]);
                if (decoded != expected)
                {
                    Assert.Fail($"frame {frame}: the pixel {pixel % image.Width}, {pixel / image.Width} is {decoded}; the reference inks give {expected}.");
                }
            }
        }

        static byte ToByte(float value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue, MidpointRounding.AwayFromZero);
    }

    [Theory]
    [MemberData(nameof(UnsupportedCases))]
    public async Task RejectsTheDocumentedUnsupportedFeatures(string name, string feature)
    {
        var testCase = await JpegXlConformanceCorpus.GetAsync(name, XunitCancellationToken);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(testCase.InputPath));
        Assert.Contains(feature, exception.Message);
    }

    [Theory]
    [MemberData(nameof(CasesWithReconstructedJpeg))]
    public async Task RebuildsTheJpegFileByteForByte(string name)
    {
        var testCase = await JpegXlConformanceCorpus.GetAsync(name, XunitCancellationToken);
        Assert.True(testCase.HasReconstructedJpeg);
        var expected = await File.ReadAllBytesAsync(await testCase.GetReconstructedJpegAsync(XunitCancellationToken), XunitCancellationToken);
        using var output = new MemoryStream();
        Assert.True(JpegXlTranscoder.TryReconstructJpeg(await File.ReadAllBytesAsync(testCase.InputPath, XunitCancellationToken), output));
        Assert.Equal(expected, output.ToArray());
    }

    /// <summary>Compares every frame with the reference decoding.</summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="image">The decoded image.</param>
    /// <param name="referencePath">The reference decoding.</param>
    /// <param name="conversion">The conversion of the decoded colors to the color space of the reference, or <see langword="null"/> when they are in it.</param>
    /// <returns>A description of each frame that exceeds its tolerance; empty when every frame is within it.</returns>
    private static List<string> Compare(JpegXlConformanceCase testCase, Image image, FullPath referencePath, IccMatrixShaperConversion? conversion)
    {
        using var reference = new JpegXlConformanceCase.NpyFrameReader(referencePath);
        Assert.Equal((testCase.Frames.Count, image.Height, image.Width), (reference.FrameCount, reference.Height, reference.Width));
        Assert.Equal(testCase.Frames.Count, image.Frames.Count);
        var channels = reference.ChannelCount;
        Assert.True(channels is 1 or 3 or 4, $"Unexpected channel count {channels}.");

        var format = image.PixelFormat;
        var bytesPerPixel = format switch
        {
            PixelFormat.Gray8 => 1,
            PixelFormat.La16 or PixelFormat.Gray16 => 2,
            PixelFormat.Rgb24 => 3,
            PixelFormat.Rgba32 or PixelFormat.La32 => 4,
            PixelFormat.Rgb48 => 6,
            PixelFormat.Rgba64 => 8,
            PixelFormat.RgbaFloat => 16,
            _ => throw new InvalidOperationException($"Unexpected pixel format {format}."),
        };
        var isGray = format is PixelFormat.Gray8 or PixelFormat.La16 or PixelFormat.Gray16 or PixelFormat.La32;
        Assert.True(channels == 1 ? isGray || format == PixelFormat.RgbaFloat : !isGray, $"The pixel format {format} does not have the channels of the reference ({channels}).");
        var colorChannels = isGray ? 1 : 3;
        Assert.True(conversion is null || conversion.ChannelCount == colorChannels, "The conversion does not have the channels of the image.");

        // Floating-point samples are not rounded, and are compared without clamping them
        var isFloat = format == PixelFormat.RgbaFloat;
        var maxLevel = IsSixteenBit(format) ? ushort.MaxValue : byte.MaxValue;
        var step = isFloat ? 0 : 1.0 / maxLevel;
        var pixelCount = image.Width * image.Height;
        var raw = new byte[pixelCount * bytesPerPixel];
        var referenceSamples = new float[pixelCount * channels];
        var actual = new float[4];
        var errors = new List<string>();
        Span<int> levels = stackalloc int[3];
        Span<double> values = stackalloc double[4];
        Span<double> gains = stackalloc double[4];
        Span<double> squaredGains = stackalloc double[4];
        Span<double> squares = stackalloc double[4];
        Span<double> squaredGainSums = stackalloc double[4];
        for (var frame = 0; frame < testCase.Frames.Count; frame++)
        {
            reference.ReadFrame(referenceSamples);
            image.Frames[frame].CopyPixelBytesTo(raw);

            // The largest error once the rounding of the sample is taken away, and the sums of the squares of the errors
            // and of the gains of the conversion, for the root mean squares
            var peakExcess = 0.0;
            var peak = 0.0;
            squares.Clear();
            squaredGainSums.Clear();
            for (var pixel = 0; pixel < pixelCount; pixel++)
            {
                ReadPixel(raw.AsSpan(pixel * bytesPerPixel, bytesPerPixel), format, actual);
                if (channels == 3 && actual[3] != 1)
                {
                    errors.Add($"frame {frame}: the reference has no alpha channel, the decoded pixel {pixel % image.Width}, {pixel / image.Width} has the alpha {actual[3]}");
                    return errors;
                }

                // A sample is rounded by up to half a step; a conversion scales that by its derivatives
                for (var channel = 0; channel < 4; channel++)
                {
                    values[channel] = actual[channel];
                    gains[channel] = 1;
                    squaredGains[channel] = 1;
                }

                if (conversion is not null)
                {
                    for (var channel = 0; channel < colorChannels; channel++)
                    {
                        levels[channel] = (int)Math.Round(actual[channel] * maxLevel, MidpointRounding.AwayFromZero);
                    }

                    conversion.Convert(levels[..colorChannels], values, gains, squaredGains);
                }

                for (var channel = 0; channel < channels; channel++)
                {
                    var expected = isFloat ? referenceSamples[(pixel * channels) + channel] : Math.Clamp(referenceSamples[(pixel * channels) + channel], 0f, 1f);
                    var error = Math.Abs(expected - (isFloat ? values[channel] : Math.Clamp(values[channel], 0, 1)));
                    peak = Math.Max(peak, error);
                    peakExcess = Math.Max(peakExcess, error - (gains[channel] * step / 2));
                    squares[channel] += error * error;
                    squaredGainSums[channel] += squaredGains[channel];
                }
            }

            var tolerance = testCase.Frames[frame];
            for (var channel = 0; channel < channels; channel++)
            {
                var rms = Math.Sqrt(squares[channel] / pixelCount);
                var rmsLimit = tolerance.Rms + (Math.Sqrt(squaredGainSums[channel] / pixelCount) * step / Math.Sqrt(12));
                if (rms > rmsLimit)
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"frame {frame}, channel {channel}: RMSE {rms:G6} (limit {rmsLimit:G6})"));
                }
            }

            if (peakExcess > tolerance.Peak + 1e-6)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"frame {frame}: peak error {peak:G6}, {peakExcess:G6} beyond the rounding of the samples (limit {tolerance.Peak:G6})"));
            }
        }

        return errors;
    }

    private static bool IsSixteenBit(PixelFormat format) => format is PixelFormat.Gray16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.Rgba64;

    /// <summary>
    /// Asserts that two matrix/TRC profiles describe the same color space: the same colorants (or gray), within the
    /// difference between the two D50 whites, and the same curves, at samples from black to white.
    /// </summary>
    private static void AssertEquivalentProfiles(byte[] expected, byte[] actual)
    {
        var expectedTags = IccTags.Read(expected);
        var actualTags = IccTags.Read(actual);
        Assert.Equal(expected.AsSpan(16, 4).ToArray(), actual.AsSpan(16, 4).ToArray());
        var isGray = expectedTags.ContainsKey("kTRC");
        foreach (var signature in isGray ? (string[])["kTRC"] : ["rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC"])
        {
            Assert.Contains(signature, actualTags.Keys);
            var expectedTag = expectedTags[signature];
            var actualTag = actualTags[signature];
            if (signature.EndsWith("XYZ", StringComparison.Ordinal))
            {
                for (var i = 0; i < 3; i++)
                {
                    var expectedValue = BinaryPrimitives.ReadInt32BigEndian(expectedTag.Span[(8 + (4 * i))..]) / 65536.0;
                    var actualValue = BinaryPrimitives.ReadInt32BigEndian(actualTag.Span[(8 + (4 * i))..]) / 65536.0;
                    Assert.True(Math.Abs(expectedValue - actualValue) <= 0.0004, $"{signature}[{i}]: {actualValue} instead of {expectedValue}");
                }
            }
            else
            {
                for (var step = 0; step <= 100; step++)
                {
                    var x = step / 100.0;
                    var expectedValue = IccTags.EvaluateCurve(expectedTag.Span, x);
                    var actualValue = IccTags.EvaluateCurve(actualTag.Span, x);
                    Assert.True(Math.Abs(expectedValue - actualValue) <= 0.0001, $"{signature}({x}): {actualValue} instead of {expectedValue}");
                }
            }
        }
    }

    /// <summary>Reads one pixel as straight RGBA in [0, 1]; a gray level is in the three colors.</summary>
    private static void ReadPixel(ReadOnlySpan<byte> pixel, PixelFormat format, float[] rgba)
    {
        static float U8(ReadOnlySpan<byte> data, int index) => data[index] / 255f;
        static float U16(ReadOnlySpan<byte> data, int index) => BinaryPrimitives.ReadUInt16LittleEndian(data[(index * 2)..]) / 65535f;
        static float F32(ReadOnlySpan<byte> data, int index) => BinaryPrimitives.ReadSingleLittleEndian(data[(index * 4)..]);

        (rgba[0], rgba[1], rgba[2], rgba[3]) = format switch
        {
            PixelFormat.Gray8 => (U8(pixel, 0), U8(pixel, 0), U8(pixel, 0), 1f),
            PixelFormat.La16 => (U8(pixel, 0), U8(pixel, 0), U8(pixel, 0), U8(pixel, 1)),
            PixelFormat.Rgb24 => (U8(pixel, 0), U8(pixel, 1), U8(pixel, 2), 1f),
            PixelFormat.Rgba32 => (U8(pixel, 0), U8(pixel, 1), U8(pixel, 2), U8(pixel, 3)),
            PixelFormat.Gray16 => (U16(pixel, 0), U16(pixel, 0), U16(pixel, 0), 1f),
            PixelFormat.La32 => (U16(pixel, 0), U16(pixel, 0), U16(pixel, 0), U16(pixel, 1)),
            PixelFormat.Rgb48 => (U16(pixel, 0), U16(pixel, 1), U16(pixel, 2), 1f),
            PixelFormat.Rgba64 => (U16(pixel, 0), U16(pixel, 1), U16(pixel, 2), U16(pixel, 3)),
            PixelFormat.RgbaFloat => (F32(pixel, 0), F32(pixel, 1), F32(pixel, 2), F32(pixel, 3)),
            _ => throw new InvalidOperationException($"Unexpected pixel format {format}."),
        };
    }
}
