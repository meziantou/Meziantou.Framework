using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// ---------------------------------------------------------------------------------------------------------------------
// JPEG fixtures: inputs encoded by libjpeg-turbo cjpeg; references decoded by libjpeg-turbo djpeg from the SAME encoded
// input; FFmpeg (libavcodec mjpeg, an independent implementation) measures the inter-decoder disagreement that justifies
// each tolerance.
// ---------------------------------------------------------------------------------------------------------------------
internal static partial class GoldenCorpus
{
    // Measured disagreement of the library decoder with each djpeg reference: (max absolute
    // error, mean absolute color error), printed by Tests/Conformance/JpegDecoderConformanceTests, which also fails when a
    // policy derived from these values is looser or tighter than the decoder needs. The values are reviewed measurements of
    // the library (never used as pixel references); a fixture without a measurement keeps the bound justified by the
    // independent cross-checks alone.
    private static readonly Dictionary<string, (int Max, double Mean)> LibraryJpegMeasurements = new(StringComparer.Ordinal)
    {
        ["jpeg/baseline-411-odd"] = (2, 0.0402),
        ["jpeg/baseline-420"] = (0, 0.0),
        ["jpeg/baseline-420-odd-restart-rows"] = (2, 0.0253),
        ["jpeg/baseline-422-odd-restart"] = (2, 0.0305),
        ["jpeg/baseline-440-odd"] = (2, 0.0459),
        ["jpeg/baseline-444"] = (2, 0.0156),
        ["jpeg/baseline-444-detail"] = (2, 0.0547),
        ["jpeg/exif-orientation-6"] = (0, 0.0),
        ["jpeg/extended-16bit-quantization"] = (0, 0.0),
        ["jpeg/gray-baseline-odd"] = (0, 0.0),
        ["jpeg/gray-restart-blocks-icc"] = (1, 0.0153),
        ["jpeg/metadata-density-xmp-comment"] = (0, 0.0),
        ["jpeg/metadata-icc-exif-dpcm"] = (0, 0.0),
        ["jpeg/multiscan-chroma-interleaved"] = (2, 0.0471),
        ["jpeg/multiscan-components-separate"] = (2, 0.0283),
        ["jpeg/rgb-adobe"] = (1, 0.0111),
        // Tiny frames (chroma planes of at most 2 samples replicated like libjpeg-turbo)
        ["jpeg/tiny-420-2x13"] = (2, 0.1538),
        ["jpeg/tiny-420-4x9"] = (1, 0.0278),
        ["jpeg/tiny-422-3x5"] = (0, 0.0),
        // Progressive decoding
        ["jpeg/progressive-411-separate-dc"] = (2, 0.0402),
        ["jpeg/progressive-420-odd-restart-rows-deep"] = (2, 0.0601),
        ["jpeg/progressive-420-partial-mcu"] = (1, 0.0125),
        ["jpeg/progressive-420-partial-refinement"] = (2, 0.0310),
        ["jpeg/progressive-422-odd-restart"] = (2, 0.0424),
        ["jpeg/progressive-440-spectral-selection"] = (2, 0.0432),
        ["jpeg/progressive-444-detail-refinement"] = (2, 0.0651),
        ["jpeg/progressive-gray-default"] = (0, 0.0),
        ["jpeg/progressive-gray-odd-refinement"] = (1, 0.0230),
        ["jpeg/progressive-rgb-adobe"] = (1, 0.0250),
        ["jpeg/progressive-420-tiny-4x6"] = (0, 0.0),
        // Baseline JPEGs written by FFmpeg's mjpeg encoder (the 1x1 frames are compared exactly: every decoder agrees)
        ["jpeg/ffmpeg-mjpeg-420-17x2"] = (1, 0.0882),
        ["jpeg/ffmpeg-mjpeg-420-37x23"] = (2, 0.0568),
        ["jpeg/ffmpeg-mjpeg-420-64x48"] = (2, 0.0567),
        ["jpeg/ffmpeg-mjpeg-422-17x2"] = (0, 0.0),
        ["jpeg/ffmpeg-mjpeg-422-37x23"] = (2, 0.0431),
        ["jpeg/ffmpeg-mjpeg-422-64x48"] = (3, 0.0414),
        ["jpeg/ffmpeg-mjpeg-444-17x2"] = (2, 0.0490),
        ["jpeg/ffmpeg-mjpeg-444-37x23"] = (2, 0.0474),
        ["jpeg/ffmpeg-mjpeg-444-64x48"] = (2, 0.0473),
    };

    // Hard ceilings enforced by the managed validator (ComparisonPolicy): maxAbsoluteError <= 12, maxMeanAbsoluteError <= 2
    private static Obj JpegPolicy(string fixtureId, IReadOnlyList<Obj> checks, string sampling)
    {
        var (maxError, meanError, justifiedMax, justifiedMean) = CommonCorpus.JpegJustifiedTolerance(checks);

        // Every independent decoder agrees exactly (e.g. a single-block frame): nothing justifies a tolerance
        if (maxError == 0)
            return new Obj { ["mode"] = "exact" };

        var upsampling = sampling is "4:4:4" or "gray" ? "" : $", chroma upsampling ({sampling})";
        var progressive = fixtureId.StartsWith("jpeg/progressive-", StringComparison.Ordinal);
        if (!LibraryJpegMeasurements.TryGetValue(fixtureId, out var measured))
        {
            return new Obj
            {
                ["mode"] = "tolerance",
                ["maxAbsoluteError"] = justifiedMax,
                ["maxMeanAbsoluteError"] = justifiedMean,
                ["justification"] =
                    "Lossy JPEG compared with an independent decoding of the same encoded input. Mature independent decoders " +
                    $"legitimately differ in IDCT rounding{upsampling} and YCbCr->RGB rounding; measured disagreement with the reference " +
                    $"(see reference.crossChecks): max {CommonCorpus.Str(maxError)}, mean {Py.FormatFixed(meanError, 4)}. Allowed: measured max + 1 and 1.5 x measured mean + 0.1 " +
                    "(floors 2 and 0.5, rounded up to 0.01). Alpha is compared exactly, and the policy is verified to reject channel swaps and " +
                    "flips of this reference. The library JPEG decoder must measure its own output and may only " +
                    "tighten this policy.",
            };
        }

        var (libraryMax, libraryMean) = measured;
        var allowedMax = Math.Min(justifiedMax, Math.Max(1, libraryMax));
        var allowedMean = Math.Min(justifiedMean, Math.Ceiling(Py.Round((libraryMean + 0.01) * 100, 6)) / 100);
        return new Obj
        {
            ["mode"] = "tolerance",
            ["maxAbsoluteError"] = allowedMax,
            ["maxMeanAbsoluteError"] = allowedMean,
            ["justification"] =
                "Lossy JPEG compared with an independent decoding of the same encoded input (libjpeg-turbo djpeg: islow integer " +
                $"IDCT, fancy upsampling{(progressive ? ", block smoothing inactive" : "")}). The library decoder " +
                $"({(progressive ? "the coefficients of every scan accumulated, then " : "")}double-precision IDCT rounded to " +
                "nearest, triangle chroma upsampling with the same edge replication and rounding biases, 16-bit fixed-point JFIF " +
                $"YCbCr->RGB) differs from it only by IDCT rounding{upsampling} (one unit on some component samples, amplified by the color " +
                $"conversion); measured: max {CommonCorpus.Str(libraryMax)}, mean {Py.FormatFixed(libraryMean, 4)}. Allowed: the measured max (at least 1) and the measured mean + 0.01 " +
                "(rounded up to 0.01), tighter than the disagreement between independent decoders (see reference.crossChecks: " +
                $"max {CommonCorpus.Str(maxError)}, mean {Py.FormatFixed(meanError, 4)}, which would justify max {CommonCorpus.Str(justifiedMax)} / mean {Py.FormatFixed(justifiedMean, 2)}). Alpha is compared exactly, and the policy is verified " +
                "to reject channel swaps and flips of this reference.",
        };
    }

    /// <summary>APP2 ICC_PROFILE segments (ICC.1 Annex B: 1-based sequence number and total count, then the chunk), written
    /// in the given sequence order: decoders reassemble chunks by their sequence numbers.</summary>
    private static byte[] JpegIccSegments(byte[] profile, IReadOnlyList<int> order)
    {
        var count = order.Count;
        var size = (profile.Length + count - 1) / count;
        var chunks = Enumerable.Range(0, count).Select(i => Bytes.Slice(profile, i * size, (i + 1) * size)).ToList();
        var output = new ByteBuilder();
        foreach (var sequence in order)
        {
            var body = new ByteBuilder().Ascii("ICC_PROFILE\0").U8(sequence).U8(count).Bytes(chunks[sequence - 1]).ToArray();
            output.U8(0xFF).U8(0xE2).U16BE(body.Length + 2).Bytes(body);
        }

        return output.ToArray();
    }

    private static int JpegApp0End(byte[] data)
    {
        Py.Assert(Bytes.Slice(data, 2, 4).AsSpan().SequenceEqual(new byte[] { 0xFF, 0xE0 }) && Bytes.Slice(data, 6, 11).AsSpan().SequenceEqual("JFIF\0"u8));
        return 4 + Bytes.U16BE(data, 4);
    }

    // Progressive scan scripts (cjpeg -scans syntax "components: Ss-Se, Ah, Al;"), written for the progressive decoder:
    // deep successive approximation of both DC and AC with split luma bands, spectral selection without refinement, DC scans
    // that do not interleave the components, a grayscale progression, and a partial progression (the high frequencies
    // 10-63 not refined to bit 0, or never sent for Cr). libjpeg-turbo's djpeg interpolates incompletely sent coefficients
    // ("block smoothing", an optional display enhancement outside T.81, without a djpeg switch) only when the DC or one of the
    // first 9 AC coefficients (zig-zag order) is incomplete, so every progression here completes those.
    private const string ProgressiveDeepScript = "0,1,2: 0-0, 0, 3;\n0: 1-9, 0, 4;\n1: 1-63, 0, 1;\n2: 1-63, 0, 1;\n0: 10-63, 0, 2;\n0,1,2: 0-0, 3, 2;\n" +
                                                 "0: 1-9, 4, 3;\n0: 1-9, 3, 2;\n0: 1-63, 2, 1;\n0,1,2: 0-0, 2, 1;\n0,1,2: 0-0, 1, 0;\n2: 1-63, 1, 0;\n" +
                                                 "1: 1-63, 1, 0;\n0: 1-63, 1, 0;\n";

    private const string ProgressiveSpectralScript = "0,1,2: 0-0, 0, 0;\n0: 1-2, 0, 0;\n0: 3-9, 0, 0;\n0: 10-63, 0, 0;\n1: 1-63, 0, 0;\n2: 1-20, 0, 0;\n2: 21-63, 0, 0;\n";

    private const string ProgressiveSeparateDcScript = "0: 0-0, 0, 1;\n1: 0-0, 0, 0;\n2: 0-0, 0, 2;\n0: 1-63, 0, 1;\n1: 1-63, 0, 0;\n2: 1-63, 0, 0;\n" +
                                                       "2: 0-0, 2, 1;\n0: 0-0, 1, 0;\n2: 0-0, 1, 0;\n0: 1-63, 1, 0;\n";

    private const string ProgressiveGrayScript = "0: 0-0, 0, 2;\n0: 1-5, 0, 3;\n0: 6-63, 0, 1;\n0: 1-5, 3, 2;\n0: 0-0, 2, 1;\n0: 1-5, 2, 1;\n0: 0-0, 1, 0;\n0: 1-63, 1, 0;\n";

    private const string ProgressivePartialScript = "0,1,2: 0-0, 0, 1;\n0: 1-9, 0, 1;\n1: 1-9, 0, 0;\n2: 1-9, 0, 0;\n0: 10-63, 0, 2;\n1: 10-63, 0, 1;\n" +
                                                    "0,1,2: 0-0, 1, 0;\n0: 1-9, 1, 0;\n0: 10-63, 2, 1;\n";

    /// <summary>One JPEG fixture: id, image, cjpeg arguments (FFmpeg arguments when <paramref name="FfmpegEncoded"/>), sampling
    /// label, exif orientation, scan script, notes.</summary>
    private sealed record JpegCase(string Id, Img Image, string[] Arguments, string Sampling, int? Orientation, string? Scans, string? Notes, bool FfmpegEncoded = false);

    private static void BuildJpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        JpegCase[] cases =
        [
            // id, image, cjpeg arguments, sampling label, exif orientation, scan script, notes
            new("jpeg/baseline-420", CommonCorpus.PatternSmoothColor(16, 16), ["-quality", "90", "-sample", "2x2", "-baseline"], "4:2:0", null, null, null),
            new("jpeg/baseline-422-odd-restart", CommonCorpus.PatternSmoothColor(17, 9), ["-quality", "90", "-sample", "2x1", "-baseline", "-restart", "2B"], "4:2:2", null, null, null),
            new("jpeg/baseline-444", CommonCorpus.PatternSmoothColor(8, 8), ["-quality", "95", "-sample", "1x1", "-baseline"], "4:4:4", null, null, null),
            new("jpeg/progressive-420-partial-mcu", CommonCorpus.PatternSmoothColor(20, 12), ["-quality", "85", "-sample", "2x2", "-progressive"], "4:2:0", null, null, null),
            new("jpeg/gray-baseline-odd", CommonCorpus.PatternSmoothGray(9, 7), ["-quality", "90", "-baseline"], "gray", null, null, null),
            new("jpeg/exif-orientation-6", CommonCorpus.PatternSmoothColor(8, 6), ["-quality", "95", "-sample", "1x1", "-baseline"], "4:4:4", 6, null, null),
            new("jpeg/metadata-density-xmp-comment", CommonCorpus.PatternSmoothColor(8, 8), ["-quality", "90", "-sample", "1x1", "-baseline"], "4:4:4", null, null, null),
            // Baseline decoding: detailed content, odd sizes, every upsampling path, restart intervals, optimized Huffman
            // tables, extended sequential with 16-bit quantization tables, RGB, multi-scan sequential frames, ICC and EXIF
            new("jpeg/baseline-420-odd-restart-rows", CommonCorpus.PatternDetailColor(37, 21, 1), ["-quality", "80", "-sample", "2x2", "-baseline", "-optimize", "-restart", "1"], "4:2:0", null, null,
                "Odd width and height with 4:2:0 (partial MCUs on both edges), optimized Huffman tables, one restart interval per MCU row."),
            new("jpeg/baseline-440-odd", CommonCorpus.PatternDetailColor(13, 19, 2), ["-quality", "85", "-sample", "1x2", "-baseline"], "4:4:0", null, null,
                "Vertical-only chroma subsampling (h1v2 triangle filter) with an odd height."),
            new("jpeg/baseline-411-odd", CommonCorpus.PatternDetailColor(35, 9, 3), ["-quality", "85", "-sample", "4x1", "-baseline"], "4:1:1", null, null,
                "Horizontal chroma factor 4 (sample replication in libjpeg-turbo and in the library) with a width that is not a multiple of 4."),
            new("jpeg/baseline-444-detail", CommonCorpus.PatternDetailColor(24, 16, 4), ["-quality", "95", "-sample", "1x1", "-baseline"], "4:4:4", null, null,
                "High-frequency content: most blocks have many nonzero AC coefficients."),
            new("jpeg/gray-restart-blocks-icc", CommonCorpus.PatternDetailGray(23, 17, 5), ["-quality", "75", "-baseline", "-optimize", "-restart", "3B"], "gray", null, null,
                "Grayscale with a restart interval of 3 blocks (intervals cross block rows), optimized tables and a gray ICC profile in one APP2 segment."),
            new("jpeg/extended-16bit-quantization", CommonCorpus.PatternDetailColor(16, 12, 6), ["-quality", "3", "-sample", "2x2"], "4:2:0", null, null,
                "Quality 3 needs quantization values above 255: libjpeg-turbo writes 16-bit DQT tables and an extended sequential (SOF1) frame."),
            new("jpeg/rgb-adobe", CommonCorpus.PatternDetailColor(12, 10, 7), ["-quality", "90", "-rgb", "-baseline"], "4:4:4", null, null,
                "RGB samples (no color transform): Adobe APP14 transform 0, component identifiers R, G, B, no JFIF segment."),
            new("jpeg/multiscan-components-separate", CommonCorpus.PatternDetailColor(19, 13, 8), ["-quality", "85", "-sample", "2x2", "-baseline", "-restart", "2B"], "4:2:0", null, "1;\n0;\n2;\n",
                "Sequential frame coded in three non-interleaved scans (Cb, Y, then Cr: each component's own block grid), with Huffman tables between scans and a restart interval of 2 blocks."),
            new("jpeg/multiscan-chroma-interleaved", CommonCorpus.PatternDetailColor(18, 11, 9), ["-quality", "85", "-sample", "2x1", "-baseline"], "4:2:2", null, "0;\n1 2;\n",
                "Sequential frame coded in two scans: Y alone (non-interleaved), then Cb and Cr interleaved (the frame MCU grid)."),
            new("jpeg/metadata-icc-exif-dpcm", CommonCorpus.PatternSmoothColor(10, 6), ["-quality", "90", "-sample", "2x1", "-baseline"], "4:2:2", 3, null,
                "RGB ICC profile split into three APP2 chunks written out of order (2, 3, 1), big-endian EXIF (orientation 3, metadata only), JFIF density in dots per centimeter."),
            // Tiny frames: chroma planes of at most 2 samples with a horizontal factor of 2 are replicated in both
            // directions by libjpeg-turbo (its triangle filters need wider planes); the library follows the same rule
            //
            new("jpeg/tiny-420-4x9", CommonCorpus.PatternDetailColor(4, 9, 51), ["-quality", "90", "-sample", "2x2", "-baseline"], "4:2:0", null, null,
                "4:2:0 frame 4 pixels wide: the 2-sample chroma planes are replicated horizontally and vertically (no triangle filter), like libjpeg-turbo."),
            new("jpeg/tiny-422-3x5", CommonCorpus.PatternDetailColor(3, 5, 52), ["-quality", "90", "-sample", "2x1", "-baseline"], "4:2:2", null, null,
                "4:2:2 frame 3 pixels wide: the 2-sample chroma planes are replicated, like libjpeg-turbo."),
            new("jpeg/tiny-420-2x13", CommonCorpus.PatternDetailColor(2, 13, 53), ["-quality", "90", "-sample", "2x2", "-baseline"], "4:2:0", null, null,
                "4:2:0 frame 2 pixels wide and 13 rows high: the 1-sample chroma planes are replicated vertically too (no vertical triangle filter), like libjpeg-turbo."),
            // Progressive decoding: libjpeg-turbo's default progression (interleaved DC first scan with Al = 1, luma and
            // chroma AC bands with Al = 2/1, AC and DC refinements, per-scan optimized Huffman tables) and custom scan scripts.
            // Except jpeg/progressive-420-tiny-4x6, chroma planes are at least 3 samples wide (narrower planes with a horizontal
            // factor of 2 are replicated, by libjpeg-turbo and by the library).
            new("jpeg/progressive-444-detail-refinement", CommonCorpus.PatternDetailColor(24, 16, 41), ["-quality", "95", "-sample", "1x1", "-progressive"], "4:4:4", null, null,
                "Default libjpeg-turbo progression on high-frequency content: DC and AC successive approximation, spectral selection of the luma AC band."),
            new("jpeg/progressive-422-odd-restart", CommonCorpus.PatternDetailColor(29, 13, 42), ["-quality", "85", "-sample", "2x1", "-progressive", "-restart", "2B"], "4:2:2", null, null,
                "Default progression with odd dimensions and a restart interval of 2 MCUs (2 blocks in the non-interleaved AC scans): EOB runs end at every restart marker."),
            new("jpeg/progressive-420-odd-restart-rows-deep", CommonCorpus.PatternDetailColor(37, 21, 43), ["-quality", "80", "-sample", "2x2", "-restart", "1"], "4:2:0", null, ProgressiveDeepScript,
                "Custom scan script: interleaved DC scans refined from bit 3 to bit 0, a luma band 1-9 refined from bit 4, chroma AC refined from bit 1, one restart interval per MCU row (per block row in AC scans)."),
            new("jpeg/progressive-440-spectral-selection", CommonCorpus.PatternDetailColor(13, 19, 44), ["-quality", "85", "-sample", "1x2"], "4:4:0", null, ProgressiveSpectralScript,
                "Custom scan script: spectral selection only (Al = 0 everywhere, no refinement), luma AC in three bands, Cr AC in two bands, odd height with vertical chroma subsampling."),
            new("jpeg/progressive-411-separate-dc", CommonCorpus.PatternDetailColor(35, 9, 45), ["-quality", "85", "-sample", "4x1", "-restart", "3B"], "4:1:1", null, ProgressiveSeparateDcScript,
                "Custom scan script: one non-interleaved DC scan per component with different point transforms (Al = 1, 0, 2), DC refinements of single components, restart interval of 3 blocks."),
            new("jpeg/progressive-gray-odd-refinement", CommonCorpus.PatternDetailGray(23, 17, 46), ["-quality", "75", "-restart", "3B"], "gray", null, ProgressiveGrayScript,
                "Grayscale custom scan script: DC from bit 2, luma band 1-5 refined from bit 3, band 6-63 from bit 1, restart interval of 3 blocks crossing block rows."),
            new("jpeg/progressive-gray-default", CommonCorpus.PatternDetailGray(9, 7, 47), ["-quality", "90", "-progressive"], "gray", null, null,
                "Default grayscale progression on a frame smaller than two blocks."),
            new("jpeg/progressive-420-partial-refinement", CommonCorpus.PatternDetailColor(19, 13, 48), ["-quality", "85", "-sample", "2x2"], "4:2:0", null, ProgressivePartialScript,
                "Partial progression, which T.81 allows: the AC coefficients 10-63 of Y and Cb end at bit 1 and those of Cr are never sent (coefficient bits never sent are zero). " +
                "DC and the AC coefficients 1-9 are complete, so libjpeg-turbo's optional block smoothing of incomplete low frequencies is inactive and the djpeg reference is a plain decoding."),
            new("jpeg/progressive-rgb-adobe", CommonCorpus.PatternDetailColor(12, 10, 49), ["-quality", "90", "-rgb", "-progressive"], "4:4:4", null, null,
                "Progressive RGB samples (Adobe APP14 transform 0)."),
            new("jpeg/progressive-420-tiny-4x6", CommonCorpus.PatternDetailColor(4, 6, 54), ["-quality", "90", "-sample", "2x2", "-progressive"], "4:2:0", null, null,
                "Default progression on a 4:2:0 frame 4 pixels wide: the 2-sample chroma planes are replicated in both directions, like libjpeg-turbo."),
            // Baseline JPEGs written by FFmpeg's mjpeg encoder (independent of libjpeg-turbo: its own quantization and Huffman
            // tables), odd and tiny sizes in every sampling it writes
            new("jpeg/ffmpeg-mjpeg-444-37x23", CommonCorpus.PatternDetailColor(37, 23, 61), ["-q:v", "3", "-pix_fmt", "yuvj444p"], "4:4:4", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-444-64x48", CommonCorpus.PatternDetailColor(64, 48, 62), ["-q:v", "3", "-pix_fmt", "yuvj444p"], "4:4:4", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-444-1x1", CommonCorpus.PatternDetailColor(1, 1, 63), ["-q:v", "3", "-pix_fmt", "yuvj444p"], "4:4:4", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-444-17x2", CommonCorpus.PatternDetailColor(17, 2, 64), ["-q:v", "3", "-pix_fmt", "yuvj444p"], "4:4:4", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-422-37x23", CommonCorpus.PatternDetailColor(37, 23, 65), ["-q:v", "3", "-pix_fmt", "yuvj422p"], "4:2:2", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-422-64x48", CommonCorpus.PatternDetailColor(64, 48, 66), ["-q:v", "3", "-pix_fmt", "yuvj422p"], "4:2:2", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-422-1x1", CommonCorpus.PatternDetailColor(1, 1, 67), ["-q:v", "3", "-pix_fmt", "yuvj422p"], "4:2:2", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-422-17x2", CommonCorpus.PatternDetailColor(17, 2, 68), ["-q:v", "3", "-pix_fmt", "yuvj422p"], "4:2:2", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-420-37x23", CommonCorpus.PatternDetailColor(37, 23, 69), ["-q:v", "3", "-pix_fmt", "yuvj420p"], "4:2:0", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-420-64x48", CommonCorpus.PatternDetailColor(64, 48, 70), ["-q:v", "3", "-pix_fmt", "yuvj420p"], "4:2:0", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-420-1x1", CommonCorpus.PatternDetailColor(1, 1, 71), ["-q:v", "3", "-pix_fmt", "yuvj420p"], "4:2:0", null, null, null, FfmpegEncoded: true),
            new("jpeg/ffmpeg-mjpeg-420-17x2", CommonCorpus.PatternDetailColor(17, 2, 72), ["-q:v", "3", "-pix_fmt", "yuvj420p"], "4:2:0", null, null, null, FfmpegEncoded: true),
        ];
        var jpegXmp = Bytes.Utf8("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
                                 "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta><?xpacket end=\"r\"?>");
        foreach (var (fixtureId, img, arguments, sampling, orientation, scans, caseNotes, ffmpegEncoded) in cases)
        {
            var notes = caseNotes;
            var stem = fixtureId.Replace('/', '_');
            var path = c.Scratch / (stem + ".jpg");
            var (data, command) = ffmpegEncoded ? FfmpegJpegEncode(c, stem, img, arguments) : CjpegEncode(c, stem, img, arguments, scans);
            List<string> commands = [command];
            var iccLabel = "none";
            if (fixtureId == "jpeg/metadata-icc-exif-dpcm")
            {
                var app0End = JpegApp0End(data);
                data = (byte[])data.Clone();
                new ByteBuilder().U8(2).U16BE(118).U16BE(59).ToArray().CopyTo(data, 13); // JFIF units 2 (dots per centimeter)
                var exif = Bytes.Concat(Bytes.Ascii("Exif\0\0"), CommonCorpus.ExifTiff(littleEndian: false, orientation!.Value, software: "corpus generator"));
                data = Bytes.Concat(data[..app0End], new ByteBuilder().U8(0xFF).U8(0xE1).U16BE(exif.Length + 2).ToArray(), exif,
                    JpegIccSegments(CommonCorpus.IccProfile("RGB "), [2, 3, 1]), data[app0End..]);
                File.WriteAllBytes(path, data);
                iccLabel = "rgb";
                commands.Add("set the JFIF density to 118x59 dots per centimeter, then insert an APP1 Exif segment (big-endian " +
                             $"TIFF, Orientation={CommonCorpus.Str(orientation.Value)}, Software) and the RGB test ICC profile in three APP2 chunks (order 2, 3, 1) after APP0");
            }
            else if (fixtureId == "jpeg/gray-restart-blocks-icc")
            {
                var app0End = JpegApp0End(data);
                data = Bytes.Concat(data[..app0End], JpegIccSegments(CommonCorpus.IccProfile("GRAY"), [1]), data[app0End..]);
                File.WriteAllBytes(path, data);
                iccLabel = "gray";
                commands.Add("insert the gray test ICC profile in one APP2 segment after APP0");
            }
            else if (orientation is not null and not 0)
            {
                data = Bytes.Concat(data[..2], CommonCorpus.ExifOrientationSegment(orientation.Value), data[2..]);
                File.WriteAllBytes(path, data);
                commands.Add($"insert an APP1 Exif segment (little-endian TIFF, Orientation={CommonCorpus.Str(orientation.Value)}) right after SOI");
            }

            if (fixtureId == "jpeg/metadata-density-xmp-comment")
            {
                var app0End = JpegApp0End(data);
                data = (byte[])data.Clone();
                new ByteBuilder().U8(1).U16BE(300).U16BE(150).ToArray().CopyTo(data, 13); // JFIF units 1 (dots per inch), X density 300, Y density 150
                var xmpBody = Bytes.Concat(Bytes.Ascii("http://ns.adobe.com/xap/1.0/\0"), jpegXmp);
                var comment = Bytes.Ascii("Golden corpus JPEG comment");
                data = Bytes.Concat(data[..app0End], new ByteBuilder().U8(0xFF).U8(0xE1).U16BE(xmpBody.Length + 2).ToArray(), xmpBody,
                    new ByteBuilder().U8(0xFF).U8(0xFE).U16BE(comment.Length + 2).ToArray(), comment, data[app0End..]);
                File.WriteAllBytes(path, data);
                commands.Add("set the JFIF density to 300x150 dots per inch, then insert an APP1 XMP segment and a COM segment after APP0");
            }

            var (info, features) = CommonCorpus.JpegInspect(data);
            Py.Assert((info.Width, info.Height) == (img.Width, img.Height));
            var progressive = info.Sof == 0xC2;
            Py.Assert(progressive == fixtureId.StartsWith("jpeg/progressive-", StringComparison.Ordinal), fixtureId);
            // Reference: djpeg with explicit settings (accurate integer IDCT, default fancy upsampling); never rotates. Block
            // smoothing (progressive frames only) is inactive: DC and the AC coefficients 1-9 are complete in every fixture
            List<string> decode = [t.Djpeg, "-dct", "int", "-pnm", path.Value];
            var reference = CommonCorpus.ReadPnm(Proc.Run(decode));
            Py.Assert((reference.Width, reference.Height) == (img.Width, img.Height));
            var referenceRgba = reference.Raw("rgba8");
            List<string> layouts = img.Model == "gray" ? ["rgba8", "gray8"] : ["rgba8", "rgb8"];
            var checks = new List<Obj>();

            // Cross-check 1: FFmpeg (libavcodec mjpeg, independent implementation), no auto-rotation
            var (decoded, ffCommand) = CommonCorpus.FfmpegDecode(t, path, "rgba", img.Width, img.Height, 4, false);
            Py.Assert(decoded.Count == 1);
            var (maxError, meanError, alphaError) = CommonCorpus.Compare(referenceRgba, decoded[0], 1);
            Py.Assert(alphaError == 0);
            var check = new Obj
            {
                ["decoder"] = $"{t.FfmpegLabel} (libavcodec mjpeg decoder, swscale yuvj->rgba)",
                ["command"] = CommonCorpus.Display(ffCommand, t, [(path.Value, "{input}")]),
                ["result"] = maxError == 0 ? "exact" : "within-tolerance",
                ["maxAbsoluteError"] = maxError,
                ["meanAbsoluteError"] = Py.Round(meanError, 4),
            };
            var horizontal = sampling switch { "4:2:0" => 2, "4:2:2" => 2, "4:1:1" => 4, _ => 1 };
            var vertical = sampling switch { "4:2:0" => 2, "4:4:0" => 2, _ => 1 };
            if (img.Width % horizontal != 0 || img.Height % vertical != 0)
            {
                check["excludedFromTolerance"] = true;
                check["result"] = "differs";
                check["notes"] = $"swscale stretches the {CommonCorpus.Str(Py.CeilDiv(img.Width, horizontal))}x{CommonCorpus.Str(Py.CeilDiv(img.Height, vertical))} chroma planes to " +
                                 $"{CommonCorpus.Str(img.Width)}x{CommonCorpus.Str(img.Height)} pixels for odd dimensions instead of " +
                                 "centered upsampling by whole factors (error grows across the image): an FFmpeg conversion " +
                                 "artifact, not a legitimate decoder difference, so it does not justify the tolerance.";
            }

            checks.Add(check);

            // Cross-check 2: Apple ImageIO through sips (independent implementation; it honors EXIF orientation, which is
            // undone here to compare stored pixels)
            var bmp = c.Scratch / (stem + ".bmp");
            List<string> sipsCommand = [t.Sips, "-s", "format", "bmp", path.Value, "--out", bmp.Value];
            Proc.Run(sipsCommand);
            var apple = CommonCorpus.Unrotate(CommonCorpus.ReadBmp(File.ReadAllBytes(bmp)), orientation);
            Py.Assert((apple.Width, apple.Height) == (img.Width, img.Height), fixtureId);
            (maxError, meanError, _) = CommonCorpus.Compare(referenceRgba, apple.Model != "rgb" ? apple.Raw("rgba8") :
                new Img(apple.Width, apple.Height, "rgba", 8, apple.Rgba()).Raw("rgba8"), 1);
            check = new Obj
            {
                ["decoder"] = $"Apple ImageIO ({t.MacOS}, {t.SipsVersion})",
                ["command"] = CommonCorpus.Display(sipsCommand, t, [(path.Value, "{input}"), (bmp.Value, "{output}.bmp")]) +
                              (orientation is not null and not 0 ? $" (EXIF orientation {CommonCorpus.Str(orientation.Value)} applied by ImageIO is undone before comparing)" : ""),
                ["result"] = maxError == 0 ? "exact" : "within-tolerance",
                ["maxAbsoluteError"] = maxError,
                ["meanAbsoluteError"] = Py.Round(meanError, 4),
            };
            if (iccLabel != "none" && maxError != 0)
            {
                check["excludedFromTolerance"] = true;
                check["result"] = "differs";
                check["notes"] = $"ImageIO converts the samples from the embedded {iccLabel.ToUpperInvariant()} ICC test profile to its output color space " +
                                 "(color management); decoders of this library preserve profiles and never apply them, so this " +
                                 "is not a decoding difference and does not justify the tolerance.";
            }

            checks.Add(check);

            var policy = JpegPolicy(fixtureId, checks, sampling);
            var (_, _, justifiedMax, justifiedMean) = CommonCorpus.JpegJustifiedTolerance(checks);
            foreach (var item in checks)
            {
                if ((item.TryGetValue("excludedFromTolerance", out var excluded) && excluded is true) || (string)item["result"]! == "exact")
                    continue;
                var itemMax = (int)item["maxAbsoluteError"]!;
                var itemMean = (double)item["meanAbsoluteError"]!;
                Py.Assert(itemMax < justifiedMax && itemMean <= justifiedMean);
                if (itemMax > (int)policy["maxAbsoluteError"]! || itemMean > (double)policy["maxMeanAbsoluteError"]!)
                {
                    item["result"] = "differs";
                    item["notes"] = "Lossy inter-decoder differences (IDCT, chroma upsampling and YCbCr->RGB rounding) larger " +
                                    "than the library tolerance of this fixture; they bound the tolerance that independent " +
                                    "decoders justify.";
                }
            }

            var expected = CommonCorpus.StillExpected("jpeg", img, img.Model == "gray" ? "Gray8" : "Rgb24",
                img.Model == "gray" ? "Grayscale" : (arguments.Contains("-rgb", StringComparer.Ordinal) ? "Rgb" : "YCbCr"), 8, orientation is null or 0 ? 1 : orientation.Value);
            expected["iccProfile"] = iccLabel;
            var reference2 = new Obj
            {
                ["method"] = "decoded",
                ["description"] = "Decoded from the same encoded input by libjpeg-turbo djpeg (accurate integer IDCT, fancy upsampling, no rotation" +
                                  (progressive ? "; block smoothing inactive: DC and AC coefficients 1-9 complete" : "") + ").",
                ["decoder"] = $"libjpeg-turbo {t.DjpegVersion} djpeg",
                ["backend"] = $"libjpeg-turbo {t.DjpegVersion} (jidctint islow IDCT, h2v1/h1v2/h2v2 fancy upsampling, jdcolor YCbCr->RGB)",
                ["command"] = CommonCorpus.Display(decode, t, [(path.Value, "{input}")]),
                ["crossChecks"] = checks,
            };
            if (orientation is not null and not 0)
                notes = (!string.IsNullOrEmpty(notes) ? notes + " " : "") + $"EXIF orientation {CommonCorpus.Str(orientation.Value)} is metadata only: decoders never rotate, the expected pixels are the stored pixels.";
            c.AddValid(fixtureId, fixtureId + ".jpg", data, [reference], expected,
                c.Provenance("generated", (ffmpegEncoded ? "BuildJpegFfmpegDecoding " : "BuildJpeg ") + img.PatternName,
                    new Obj { ["width"] = img.Width, ["height"] = img.Height },
                    [.. ffmpegEncoded ? [t.FfmpegLabel, $"libjpeg-turbo {t.DjpegVersion} djpeg"] : new[] { t.LibjpegLabel, t.FfmpegLabel + " (cross-check only)" },
                     $"Apple ImageIO {t.MacOS} {t.SipsVersion} (cross-check only)", ToolSet.RuntimeLabel], commands),
                reference2, features, comparison: policy, layouts: layouts, notes: notes);
        }

        BuildUnsupportedJpeg(c);
    }

    /// <summary>A part of a JPEG file: the marker byte of a marker segment (SOI/EOI included) or <see cref="JpegEcs"/> for
    /// entropy-coded data, with its start and end offsets.</summary>
    private sealed record JpegPart(int Kind, int Start, int End);

    private const int JpegEcs = -1;

    /// <summary>Splits a JPEG file into its parts: (kind, start, end) with kind the marker byte for marker segments (SOI/EOI
    /// included) and 'ecs' (<see cref="JpegEcs"/>) for entropy-coded data (restart markers and stuffing included).</summary>
    private static List<JpegPart> JpegSegments(byte[] data)
    {
        var parts = new List<JpegPart> { new(0xD8, 0, 2) };
        var offset = 2;
        while (true)
        {
            var marker = data[offset + 1];
            if (marker == 0xD9)
            {
                parts.Add(new(0xD9, offset, offset + 2));
                return parts;
            }

            var length = Bytes.U16BE(data, offset + 2);
            parts.Add(new(marker, offset, offset + 2 + length));
            offset += 2 + length;
            if (marker == 0xDA)
            {
                var start = offset;
                while (!(data[offset] == 0xFF && data[offset + 1] != 0x00 && !(data[offset + 1] is >= 0xD0 and <= 0xD7)))
                    offset++;
                parts.Add(new(JpegEcs, start, offset));
            }
        }
    }

    private static byte[] JpegSegment(int marker, byte[] body) => new ByteBuilder().U8(0xFF).U8(marker).U16BE(body.Length + 2).Bytes(body).ToArray();

    /// <summary>Encodes an RGB pattern with FFmpeg's mjpeg encoder (bit-exact: no encoder version in the output); returns
    /// (data, display command).</summary>
    private static (byte[] Data, string Command) FfmpegJpegEncode(Corpus<GoldenTools> c, string stem, Img img, IReadOnlyList<string> arguments)
    {
        var t = c.Tools;
        var path = c.Scratch / (stem + ".jpg");
        var source = CommonCorpus.WithSuffix(path, ".source.raw");
        File.WriteAllBytes(source, img.FfmpegRaw().Data);
        var command = t.FfmpegCmd(["-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", $"{CommonCorpus.Str(img.Width)}x{CommonCorpus.Str(img.Height)}", "-i", source.Value,
            "-frames:v", "1", "-c:v", "mjpeg", "-fflags", "+bitexact", "-flags:v", "+bitexact", .. arguments, path.Value]);
        Proc.Run(command);
        File.Delete(source);
        return (File.ReadAllBytes(path), CommonCorpus.Display(command, t, [(source.Value, "{source}.raw"), (path.Value, "{input}")]));
    }

    /// <summary>Encodes a pattern with cjpeg; returns (data, display command).</summary>
    private static (byte[] Data, string Command) CjpegEncode(Corpus<GoldenTools> c, string stem, Img img, IReadOnlyList<string> arguments, string? scans = null)
    {
        var t = c.Tools;
        var source = c.Scratch / (stem + (img.Model == "gray" ? ".pgm" : ".ppm"));
        var path = c.Scratch / (stem + ".jpg");
        CommonCorpus.WritePnm(source, img);
        List<(string Path, string Symbol)> substitutions = [(source.Value, "{source}.pnm"), (path.Value, "{input}")];
        if (!string.IsNullOrEmpty(scans))
        {
            var script = c.Scratch / (stem + ".scans.txt");
            File.WriteAllText(script, scans);
            arguments = [.. arguments, "-scans", script.Value];
            substitutions.Add((script.Value, $"{{scans}} ({scans.Replace("\n", " ", StringComparison.Ordinal).Trim()})"));
        }

        List<string> command = [t.Cjpeg, "-dct", "int", .. arguments, "-outfile", path.Value, source.Value];
        Proc.Run(command);
        return (File.ReadAllBytes(path), CommonCorpus.Display(command, t, substitutions));
    }

    /// <summary>Recognized JPEG modes outside the first release: UnsupportedImageFeatureException,
    /// also from a header identification (the frame header precedes the first scan).</summary>
    private static void BuildUnsupportedJpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;

        // Arithmetic coding is recognized and rejected (UnsupportedImageFeatureException)
        var img = CommonCorpus.PatternSmoothColor(8, 8);
        var source = c.Scratch / "arith.ppm";
        var path = c.Scratch / "arith.jpg";
        CommonCorpus.WritePnm(source, img);
        List<string> command = [t.Cjpeg, "-arithmetic", "-quality", "90", "-outfile", path.Value, source.Value];
        Proc.Run(command);
        var data = File.ReadAllBytes(path);
        c.AddError("invalid/jpeg/arithmetic-coding", "unsupported", "invalid/jpeg/arithmetic-coding.jpg", data, "jpeg",
            c.Provenance("generated", "BuildJpeg PatternSmoothColor", new Obj { ["width"] = 8, ["height"] = 8 },
                [t.LibjpegLabel, ToolSet.RuntimeLabel], [CommonCorpus.Display(command, t, [(source.Value, "{source}.ppm"), (path.Value, "{input}")])]),
            new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Jpeg" },
            features: CommonCorpus.JpegInspect(data).Features, notes: "Arithmetic-coded JPEG (SOF9) is out of scope.");

        void Add(string fixtureId, byte[] data, IList<string> commands, string feature, string notes, string function = "BuildUnsupportedJpeg PatternDetailColor", Obj? parameters = null,
            string origin = "generated") =>
            c.AddError(fixtureId, "unsupported", fixtureId + ".jpg", data, "jpeg",
                c.Provenance(origin, function, parameters, [t.LibjpegLabel, ToolSet.RuntimeLabel], commands),
                new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Jpeg", ["feature"] = feature },
                features: CommonCorpus.JpegInspect(data).Features, notes: notes);

        img = CommonCorpus.PatternDetailColor(12, 8, 21);
        (data, var encodeCommand) = CjpegEncode(c, "lossless", img, ["-lossless", "1"]);
        Add("invalid/jpeg/lossless", data, [encodeCommand], "JPEG lossless", "Lossless JPEG (SOF3) is out of scope.", parameters: new Obj { ["width"] = 12, ["height"] = 8 });
        (data, encodeCommand) = CjpegEncode(c, "precision12", img, ["-precision", "12", "-quality", "90"]);
        Add("invalid/jpeg/precision-12", data, [encodeCommand], "JPEG 12-bit precision", "12-bit JPEG (extended sequential, P = 12) is out of scope: only 8-bit samples are decoded.",
            parameters: new Obj { ["width"] = 12, ["height"] = 8 });

        // Hand-modified frame headers of a valid 4:2:2 file
        (data, encodeCommand) = CjpegEncode(c, "unsupported-base", img, ["-quality", "90", "-sample", "2x1", "-baseline"]);
        var sof = JpegSegments(data).First(p => p.Kind == 0xC0);
        var body = data[(sof.Start + 4)..sof.End];
        Py.Assert(body[5] == 3 && body[7] == 0x21);
        body[7] = 0x31; // luma 3x1 with chroma 2x1: 3 is not a multiple of 2
        body[10] = 0x21;
        body[13] = 0x21;
        var mutated = Bytes.Concat(data[..sof.Start], JpegSegment(0xC0, body), data[sof.End..]);
        Add("invalid/jpeg/non-integral-sampling", mutated, [encodeCommand, "set the sampling factors of the frame header to Y 3x1, Cb 2x1, Cr 2x1"],
            "JPEG non-integral sampling ratios", "Sampling factors that do not divide the largest factors need fractional upsampling, which is not supported (as in libjpeg-turbo).",
            parameters: new Obj { ["width"] = 12, ["height"] = 8 });

        // CMYK: a frame with four components (the walker rejects it at the frame header; the scan is never decoded)
        var dqt = JpegSegments(data).First(p => p.Kind == 0xDB);
        var four = new ByteBuilder().Bytes(body.AsSpan(0, 5)).U8(4);
        for (var i = 0; i < 4; i++)
            four.U8(i + 1).U8(0x11).U8(0);
        var header = Bytes.Concat([0xFF, 0xD8], JpegSegment(0xEE, Bytes.Concat(Bytes.Ascii("Adobe"), [0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x00])), data[dqt.Start..dqt.End],
            JpegSegment(0xC0, four.ToArray()), JpegSegment(0xDA, [0x04, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x00, 0x3F, 0x00]), [0x00, 0xFF, 0xD9]);
        c.AddError("invalid/jpeg/cmyk-four-components", "unsupported", "invalid/jpeg/cmyk-four-components.jpg", header, "jpeg",
            c.Provenance("hand-authored", "BuildUnsupportedJpeg", null, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Jpeg", ["feature"] = "JPEG CMYK/YCCK" },
            features: CommonCorpus.JpegInspect(header).Features,
            notes: "Adobe APP14 (transform 0) and a four-component (CMYK) frame header: CMYK/YCCK are out of scope.");
    }

    /// <summary>Malformed JPEG files derived from valid cjpeg outputs (InvalidImageContentException). Table and entropy-coded
    /// data defects lie in a valid container: only decoding finds them (identification does not parse table contents or
    /// decode scans); the frame-pixel limit is checked from the frame header before any allocation.</summary>
    private static void BuildInvalidJpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        var img = CommonCorpus.PatternDetailColor(32, 32, 31);
        var (plain, plainCommand) = CjpegEncode(c, "invalid-plain", img, ["-quality", "85", "-sample", "2x2", "-baseline"]);
        var (restart, restartCommand) = CjpegEncode(c, "invalid-restart", img, ["-quality", "85", "-sample", "2x2", "-baseline", "-restart", "1B"]);
        var (separate, separateCommand) = CjpegEncode(c, "invalid-separate", img, ["-quality", "85", "-sample", "2x2", "-baseline"], scans: "0;\n1;\n2;\n");

        void Add(string fixtureId, byte[] data, string baseCommand, string defect, string? notes = null, string kind = "invalid", Obj? expected = null, Obj? decodeOptions = null)
        {
            List<string>? features;
            try
            {
                features = CommonCorpus.JpegInspect(data).Features;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IndexOutOfRangeException or ArgumentException or KeyNotFoundException)
            {
                features = null;
            }

            c.AddError(fixtureId, kind, fixtureId + ".jpg", data, "jpeg",
                c.Provenance("generated", "BuildInvalidJpeg PatternDetailColor", new Obj { ["width"] = 32, ["height"] = 32, ["defect"] = defect },
                    [t.LibjpegLabel, ToolSet.RuntimeLabel], [baseCommand, defect]),
                expected ?? new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Jpeg" },
                features: features, notes: notes, decodeOptions: decodeOptions);
        }

        var parts = JpegSegments(plain);
        var ecs = parts.First(p => p.Kind == JpegEcs);
        var middle = ecs.Start + (ecs.End - ecs.Start) / 2;
        if (plain[middle - 1] == 0xFF)
            middle--;
        Add("invalid/jpeg/truncated-entropy-data", Bytes.Concat(plain[..middle], plain[ecs.End..]), plainCommand,
            "cut the entropy-coded data of the scan in the middle (EOI kept)",
            notes: "The scan ends before its last MCU: never decoded as a partial image.");
        Add("invalid/jpeg/invalid-huffman-code", Bytes.Concat(plain[..ecs.Start], [0xFF, 0x00, 0xFF, 0x00], plain[ecs.Start..]), plainCommand,
            "insert two stuffed 0xFF bytes (16 one-bits, a code the Annex K DC table does not assign) at the start of the scan");

        var dqt = parts.First(p => p.Kind == 0xDB);
        var body = plain[(dqt.Start + 4)..dqt.End];
        Py.Assert(body[0] == 0x00);
        body[64] = 0;
        Add("invalid/jpeg/zero-quantization-value", Bytes.Concat(plain[..dqt.Start], JpegSegment(0xDB, body), plain[dqt.End..]), plainCommand,
            "set the last value of quantization table 0 to zero");
        Add("invalid/jpeg/truncated-quantization-table", Bytes.Concat(plain[..dqt.Start], JpegSegment(0xDB, body[..40]), plain[dqt.End..]), plainCommand,
            "shorten the DQT segment to 39 of the 64 values of table 0");

        var dhts = parts.Where(p => p.Kind == 0xC4).ToList();
        Py.Assert(dhts.Select(p => (int)plain[p.Start + 4]).SequenceEqual([0x00, 0x10, 0x01, 0x11]));
        Add("invalid/jpeg/undefined-huffman-table", Bytes.Concat(plain[..dhts[3].Start], plain[dhts[3].End..]), plainCommand,
            "remove the DHT segment of AC table 1 (used by the chroma components)");
        body = plain[(dhts[0].Start + 4)..dhts[0].End];
        var total = body.AsSpan(1, 16).ToArray().Sum(v => (int)v);
        byte[] counts = [3, .. new byte[14], (byte)(total - 3)];
        counts.CopyTo(body, 1);
        Add("invalid/jpeg/oversubscribed-huffman-table", Bytes.Concat(plain[..dhts[0].Start], JpegSegment(0xC4, body), plain[dhts[0].End..]), plainCommand,
            "give DC table 0 three codes of length 1 (the code lengths no longer form a prefix code)");

        var sof = parts.First(p => p.Kind == 0xC0);
        body = plain[(sof.Start + 4)..sof.End];
        Py.Assert(body[7] == 0x22);
        body[7] = 0x44;
        Add("invalid/jpeg/interleaved-scan-too-many-blocks", Bytes.Concat(plain[..sof.Start], JpegSegment(0xC0, body), plain[sof.End..]), plainCommand,
            "set the luma sampling factors to 4x4: the interleaved scan has 18 blocks per MCU (T.81 allows 10)",
            notes: "Detected from the scan header by a full scan (a header identification stops before the first scan).");

        var restarts = Enumerable.Range(0, restart.Length - 1).Where(i => restart[i] == 0xFF && restart[i + 1] is >= 0xD0 and <= 0xD7).ToList();
        Py.Assert(restarts.Count == 3);
        var swapped = (byte[])restart.Clone();
        swapped[restarts[1] + 1] = 0xD2;
        Add("invalid/jpeg/restart-out-of-sequence", swapped, restartCommand, "renumber the second restart marker RST1 as RST2");
        Add("invalid/jpeg/restart-marker-missing", Bytes.Concat(restart[..restarts[2]], restart[(restarts[2] + 2)..]), restartCommand,
            "remove the last restart marker (RST2): the last restart interval has no marker");

        parts = JpegSegments(separate);
        var scans = Enumerable.Range(0, parts.Count).Where(i => parts[i].Kind == 0xDA).ToList();
        Py.Assert(scans.Count == 3);
        var last = parts[scans[2]];
        var data = Bytes.Concat(separate[..last.Start], separate[parts[scans[2] + 1].End..]);
        Add("invalid/jpeg/missing-component-scan", data, separateCommand, "remove the third scan (component Cr) of a frame coded in separate scans",
            notes: "Every component of a sequential frame must be coded: a missing scan is never decoded as zero samples.");

        // Excessive allocation: a frame header declaring 20000x20000 pixels is rejected before any pixel is allocated
        var sofBody = new ByteBuilder().U8(8).U16BE(20000).U16BE(20000).U8(1).Bytes([0x01, 0x11, 0x00]).ToArray();
        var huge = Bytes.Concat([0xFF, 0xD8], plain[dqt.Start..dqt.End], JpegSegment(0xC0, sofBody), plain[dhts[0].Start..dhts[0].End], plain[dhts[1].Start..dhts[1].End],
            JpegSegment(0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]), [0x00, 0xFF, 0xD9]);
        c.AddError("limit/jpeg/frame-pixels-excessive", "limit", "invalid/jpeg/frame-pixels-excessive.jpg", huge, "jpeg",
            c.Provenance("hand-authored", "BuildInvalidJpeg", new Obj { ["width"] = 20000, ["height"] = 20000 }, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "FramePixels" },
            features: CommonCorpus.JpegInspect(huge).Features, decodeOptions: new Obj { ["limits"] = new Obj { ["MaxFramePixels"] = 100000000 } },
            notes: $"A {CommonCorpus.Str(huge.Length)}-byte file declaring a 20000x20000 grayscale frame (400,000,000 pixels) with the default MaxFramePixels " +
                   "(100,000,000, explicit here): the limit is checked from the frame header, before any pixel or decoder state is allocated.");
    }

    /// <summary>Malformed progressive JPEG files derived from valid cjpeg outputs. Scan-parameter and progression defects are
    /// found by the walker from the scan headers (a full identification fails); entropy-coded defects and a component
    /// without any scan lie in a valid container and are only found by decoding.</summary>
    private static void BuildInvalidProgressiveJpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        var img = CommonCorpus.PatternDetailColor(32, 24, 51);
        var (@default, defaultCommand) = CjpegEncode(c, "invalid-progressive", img, ["-quality", "85", "-sample", "2x2", "-progressive"]);
        const string SeparateScript = "0: 0-0, 0, 0;\n1: 0-0, 0, 0;\n2: 0-0, 0, 0;\n0: 1-63, 0, 0;\n1: 1-63, 0, 0;\n2: 1-63, 0, 0;\n";
        var (separate, separateCommand) = CjpegEncode(c, "invalid-progressive-separate", img, ["-quality", "85", "-sample", "2x2"], scans: SeparateScript);

        void Add(string fixtureId, byte[] data, string baseCommand, string defect, string notes) =>
            c.AddError(fixtureId, "invalid", fixtureId + ".jpg", data, "jpeg",
                c.Provenance("generated", "BuildInvalidProgressiveJpeg PatternDetailColor", new Obj { ["width"] = 32, ["height"] = 24, ["defect"] = defect },
                    [t.LibjpegLabel, ToolSet.RuntimeLabel], [baseCommand, defect]),
                new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Jpeg" },
                features: CommonCorpus.JpegInspect(data).Features, notes: notes);

        // (SOS part, entropy-coded part) pairs of every scan, in order
        static List<(JpegPart Sos, JpegPart Ecs)> ScansOf(byte[] data)
        {
            var parts = JpegSegments(data);
            return [.. Enumerable.Range(0, parts.Count).Where(i => parts[i].Kind == 0xDA).Select(i => (parts[i], parts[i + 1]))];
        }

        static JpegScan ScanParameters(byte[] data, JpegPart sos)
        {
            var body = data[(sos.Start + 4)..sos.End];
            var n = body[0];
            return new JpegScan([.. Enumerable.Range(0, n).Select(i => (int)body[1 + 2 * i])], body[1 + 2 * n], body[2 + 2 * n], body[3 + 2 * n] >> 4, body[3 + 2 * n] & 15);
        }

        static int IndexOfScan(List<JpegScan> described, int[] components, int start, int end, int high, int low)
        {
            var index = described.FindIndex(s => s.Components.SequenceEqual(components) && (s.Start, s.End, s.High, s.Low) == (start, end, high, low));
            Py.Assert(index >= 0, "scan not found");
            return index;
        }

        var scans = ScansOf(@default);
        var described = scans.Select(s => ScanParameters(@default, s.Sos)).ToList();

        // Spectral selection: the luma AC band 1-5 rewritten as 6-5 (Ss > Se)
        var index = IndexOfScan(described, [1], 1, 5, 0, 2);
        var sos = scans[index].Sos;
        var body = @default[(sos.Start + 4)..sos.End];
        body[^3] = 6;
        Add("invalid/jpeg/progressive-invalid-spectral-range", Bytes.Concat(@default[..sos.Start], JpegSegment(0xDA, body), @default[sos.End..]), defaultCommand,
            "set Ss of the luma AC scan 1-5 (Ah 0, Al 2) to 6 (Ss > Se)",
            "Invalid spectral selection of a progressive scan: rejected from the scan header, also by a full identification.");

        // Successive approximation: the luma AC refinement 1-63 (Ah 2, Al 1) moved before the first scan of the band 6-63
        var refinement = IndexOfScan(described, [1], 1, 63, 2, 1);
        var first = IndexOfScan(described, [1], 6, 63, 0, 2);
        Py.Assert(first < refinement);
        (int Start, int End) ScanBytes(int i)
        {
            // The scan with the DHT segments written right before it (cjpeg writes the tables each scan uses)
            var start = scans[i].Sos.Start;
            var parts = JpegSegments(@default);
            var k = parts.FindIndex(p => p.Start == start);
            while (parts[k - 1].Kind == 0xC4)
                k--;
            return (parts[k].Start, scans[i].Ecs.End);
        }

        var (rStart, rEnd) = ScanBytes(refinement);
        var (fStart, _) = ScanBytes(first);
        var moved = Bytes.Concat(@default[..fStart], @default[rStart..rEnd], @default[fStart..rStart], @default[rEnd..]);
        Add("invalid/jpeg/progressive-refinement-before-first-scan", moved, defaultCommand,
            "move the luma AC refinement scan 1-63 (Ah 2, Al 1), with its DHT, before the first scan of the luma band 6-63",
            "A refinement scan of coefficients that no first scan coded yet: an invalid progression, rejected from the scan headers.");

        // Truncated scan: the entropy-coded data of the largest scan before the last one cut in the middle (the following scans kept)
        var largest = 0;
        for (var i = 1; i < scans.Count - 1; i++)
        {
            if (scans[i].Ecs.End - scans[i].Ecs.Start > scans[largest].Ecs.End - scans[largest].Ecs.Start)
                largest = i;
        }

        var ecs = scans[largest].Ecs;
        var middle = ecs.Start + (ecs.End - ecs.Start) / 2;
        if (@default[middle - 1] == 0xFF)
            middle--;
        var scan = described[largest];
        Add("invalid/jpeg/progressive-truncated-scan", Bytes.Concat(@default[..middle], @default[ecs.End..]), defaultCommand,
            $"cut the entropy-coded data of the largest scan ({string.Join(',', scan.Components)}: {CommonCorpus.Str(scan.Start)}-{CommonCorpus.Str(scan.End)}, Ah {CommonCorpus.Str(scan.High)}, Al {CommonCorpus.Str(scan.Low)}) in the middle, keeping the following scans",
            "A truncated scan is never decoded as a partial image, even when later scans follow.");

        // Entropy-coded defect in an AC refinement scan: 16 one-bits (a code that optimized tables never assign)
        ecs = scans[refinement].Ecs;
        Add("invalid/jpeg/progressive-refinement-invalid-code", Bytes.Concat(@default[..ecs.Start], [0xFF, 0x00, 0xFF, 0x00], @default[ecs.Start..]), defaultCommand,
            "insert two stuffed 0xFF bytes at the start of the luma AC refinement scan 1-63 (Ah 2, Al 1)",
            "An invalid Huffman code in a refinement scan: only decoding finds it.");

        // A component without any scan: the Cr DC and AC scans removed from a progression with separate scans
        scans = ScansOf(separate);
        described = scans.Select(s => ScanParameters(separate, s.Sos)).ToList();
        var removed = separate;
        foreach (var i in Enumerable.Range(0, described.Count).Where(i => described[i].Components.SequenceEqual([3])).OrderDescending())
        {
            var (scanSos, scanEcs) = scans[i];
            removed = Bytes.Concat(removed[..scanSos.Start], removed[scanEcs.End..]);
        }

        Add("invalid/jpeg/progressive-missing-component", removed, separateCommand,
            "remove both scans of the component Cr (DC 0-0 and AC 1-63, Al 0) from a progression with one scan per component and band",
            "Every component of a progressive frame needs at least its DC first scan: found by decoding (the remaining scans form a valid progression).");
    }
}
