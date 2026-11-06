using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Color;

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
/// the file, which for every case but one is the color space of that profile (the expected label is checked); the other
/// one is converted with the independent <see cref="ReferenceIccTransform"/>, as the script converts with lcms2.
/// </para>
/// <para>
/// The library rounds every frame to the depth of the image (8 or 16 bits), and the tolerances of the corpus are smaller
/// than the rounding of 8-bit samples (1/510): the limits add half a step to the largest error and the root mean square
/// of a uniform rounding, a step divided by the square root of 12, to the other. djxl passes every case and jxl-oxide
/// all but six (gamut mapping) under the same comparison of floating-point outputs.
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

        /// <summary>A profile the library writes for the enumerated color encoding of the file: converted to <c>reference.icc</c>.</summary>
        SynthesizedIcc,
    }

    /// <summary>
    /// The cases that fail on purpose, and why. The test asserts that they still fail, so that a fix of the library or
    /// of the corpus shows up as a failure that tells to remove the entry.
    /// </summary>
    private static readonly Dictionary<string, string> ExpectedFailures = new(StringComparer.Ordinal)
    {
        // The samples equal the reference. The profile the library writes for the enumerated encoding (sRGB primaries,
        // gamma 0.45455) adapts D65 to the illuminant of the profile connection space, Z = 0.8249 (IccColorimetry.D50Z),
        // where libjxl, and the XYB path of the library (JpegXlOutputColor), use Z = 0.82521: the colorants differ by 4e-4,
        // which turns 3 near-black blue samples negative once converted to reference.icc (error 1/255)
        ["lz77_flower"] = "the synthesized profile uses another D50 white than libjxl",
    };

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
        { "grayscale_jpeg", OutputColor.EmbeddedIcc },
        { "grayscale_jpeg_5", OutputColor.EmbeddedIcc },
        { "lz77_flower", OutputColor.SynthesizedIcc },
        { "mul_no_extra_channels", OutputColor.Srgb },
        { "noise", OutputColor.Srgb },
        { "noise_5", OutputColor.Srgb },
        { "opsin_inverse", OutputColor.Srgb },
        { "opsin_inverse_5", OutputColor.Srgb },
        { "patches_lossless", OutputColor.EmbeddedIcc },
        { "sunset_logo", OutputColor.Srgb },
        { "upsampling", OutputColor.Srgb },
        { "upsampling_5", OutputColor.Srgb },
    };

    /// <summary>The cases the library rejects, with a part of the message that names the feature (see the limitations of <c>readme.md</c>).</summary>
    public static TheoryData<string, string> UnsupportedCases => new()
    {
        { "alpha_premultiplied", "premultiplied alpha" },
        { "cmyk_layers", "black channel" },
        { "grayscale", "XYB color space and its ICC profile is not sRGB" },
        { "grayscale_5", "XYB color space and its ICC profile is not sRGB" },
        { "grayscale_public_university", "grayscale Modular frame with restoration filters" },
        { "lossless_pfm", "floating-point color samples" },
        { "patches", "XYB color space and its ICC profile is not sRGB" },
        { "patches_5", "XYB color space and its ICC profile is not sRGB" },
        { "progressive", "XYB color space and its ICC profile is not sRGB" },
        { "progressive_5", "XYB color space and its ICC profile is not sRGB" },
        { "spot", "spot color" },
    };

    public static TheoryData<string> CasesWithReconstructedJpeg => ["bench_oriented_brg", "bench_oriented_brg_5", "cafe", "cafe_5", "grayscale_jpeg", "grayscale_jpeg_5"];

    [Fact]
    public void EveryCaseOfTheCorpusIsListed()
    {
        var listed = DecodedCases.Select(row => row.Data.Item1).Concat(UnsupportedCases.Select(row => row.Data.Item1)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(JpegXlConformanceCorpus.CaseNames.Order(StringComparer.Ordinal).ToArray(), listed);
        foreach (var name in ExpectedFailures.Keys)
        {
            Assert.Contains(name, listed);
        }
    }

    [Theory]
    [MemberData(nameof(DecodedCases))]
    public async Task DecodesWithinTheToleranceOfTheReference(string name, OutputColor color)
    {
        var testCase = await JpegXlConformanceCorpus.GetAsync(name, XunitCancellationToken);
        using var image = Image.Load(testCase.InputPath);
        var referenceIcc = await File.ReadAllBytesAsync(await testCase.GetReferenceIccAsync(XunitCancellationToken), XunitCancellationToken);
        var metadata = image.Metadata;
        ReferenceIccTransform? transform = null;
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
                transform = ReferenceIccTransform.Create(metadata.IccProfile.Data.Span, referenceIcc);
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
        var errors = Compare(testCase, image, referencePath, transform);
        if (ExpectedFailures.TryGetValue(name, out var reason))
        {
            Assert.NotEmpty(errors, $"{name} now matches its reference ({reason} no longer applies): remove it from {nameof(ExpectedFailures)}.");

            // The samples themselves are within the tolerance: only the color conversion fails
            Assert.Empty(Compare(testCase, image, referencePath, transform: null));
            return;
        }

        Assert.Empty(errors);
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
    /// <returns>A description of each frame that exceeds its tolerance; empty when every frame is within it.</returns>
    private static List<string> Compare(JpegXlConformanceCase testCase, Image image, FullPath referencePath, ReferenceIccTransform? transform)
    {
        using var reference = new JpegXlConformanceCase.NpyFrameReader(referencePath);
        Assert.Equal((testCase.Frames.Count, image.Height, image.Width), (reference.FrameCount, reference.Height, reference.Width));
        Assert.Equal(testCase.Frames.Count, image.Frames.Count);
        var channels = reference.ChannelCount;
        Assert.True(channels is 1 or 3 or 4, $"Unexpected channel count {channels}.");

        var format = image.PixelFormat;
        var (bytesPerPixel, is16Bit) = format switch
        {
            PixelFormat.Gray8 => (1, false),
            PixelFormat.La16 => (2, false),
            PixelFormat.Rgb24 => (3, false),
            PixelFormat.Rgba32 => (4, false),
            PixelFormat.Gray16 => (2, true),
            PixelFormat.La32 => (4, true),
            PixelFormat.Rgb48 => (6, true),
            PixelFormat.Rgba64 => (8, true),
            _ => throw new InvalidOperationException($"Unexpected pixel format {format}."),
        };
        var isGray = format is PixelFormat.Gray8 or PixelFormat.La16 or PixelFormat.Gray16 or PixelFormat.La32;
        Assert.Equal(channels == 1, isGray);

        var step = 1.0 / (is16Bit ? ushort.MaxValue : byte.MaxValue);
        var pixelCount = image.Width * image.Height;
        var raw = new byte[pixelCount * bytesPerPixel];
        var referenceSamples = new float[pixelCount * channels];
        var actual = new float[4];
        var converted = new Dictionary<(float, float, float), float[]>();
        var errors = new List<string>();
        Span<double> squares = stackalloc double[4];
        for (var frame = 0; frame < testCase.Frames.Count; frame++)
        {
            reference.ReadFrame(referenceSamples);
            image.Frames[frame].CopyPixelBytesTo(raw);
            var peak = 0.0;
            squares.Clear();
            for (var pixel = 0; pixel < pixelCount; pixel++)
            {
                ReadPixel(raw.AsSpan(pixel * bytesPerPixel, bytesPerPixel), format, actual);
                if (transform is not null)
                {
                    var key = (actual[0], actual[1], actual[2]);
                    if (!converted.TryGetValue(key, out var color))
                    {
                        var result = transform.Convert([(decimal)actual[0], (decimal)actual[1], (decimal)actual[2]]);
                        color = [(float)result[0], (float)result[1], (float)result[2]];
                        converted.Add(key, color);
                    }

                    color.CopyTo(actual, 0);
                }

                if (channels == 3 && actual[3] != 1)
                {
                    errors.Add($"frame {frame}: the reference has no alpha channel, the decoded pixel {pixel % image.Width}, {pixel / image.Width} has the alpha {actual[3]}");
                    return errors;
                }

                for (var channel = 0; channel < channels; channel++)
                {
                    var expected = Math.Clamp(referenceSamples[(pixel * channels) + channel], 0f, 1f);
                    var value = channels == 1 ? actual[0] : actual[channel];
                    var error = Math.Abs(expected - (double)value);
                    peak = Math.Max(peak, error);
                    squares[channel] += error * error;
                }
            }

            var rms = 0.0;
            for (var channel = 0; channel < channels; channel++)
            {
                rms = Math.Max(rms, Math.Sqrt(squares[channel] / pixelCount));
            }

            var tolerance = testCase.Frames[frame];
            var peakLimit = tolerance.Peak + (step / 2) + 1e-6;
            var rmsLimit = tolerance.Rms + (step / Math.Sqrt(12));
            if (peak > peakLimit || rms > rmsLimit)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"frame {frame}: peak error {peak:G6} (limit {peakLimit:G6}), RMSE {rms:G6} (limit {rmsLimit:G6})"));
            }
        }

        return errors;
    }

    /// <summary>Reads one pixel as straight RGBA in [0, 1]; a gray level is in the three colors.</summary>
    private static void ReadPixel(ReadOnlySpan<byte> pixel, PixelFormat format, float[] rgba)
    {
        static float U8(ReadOnlySpan<byte> data, int index) => data[index] / 255f;
        static float U16(ReadOnlySpan<byte> data, int index) => BinaryPrimitives.ReadUInt16LittleEndian(data[(index * 2)..]) / 65535f;

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
            _ => throw new InvalidOperationException($"Unexpected pixel format {format}."),
        };
    }
}
