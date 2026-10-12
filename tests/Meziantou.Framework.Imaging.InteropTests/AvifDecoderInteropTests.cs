using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Avif;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// AVIF decoding against the libavif reference tools: deterministic sources are written as PNG by the library, encoded by
/// <c>avifenc</c> over a matrix of options, and decoded by the library and by <c>avifdec</c> (to 16-bit PNG, read back by
/// <see cref="AvifDecImage"/> without the library). <c>avifdec --info</c> is the reference for the container: sample format,
/// color description, transformations, frame count, durations and repetition count.
/// <list type="bullet">
/// <item><description>Samples that are not matrixed (identity matrix, monochrome in full range) and alpha are compared exactly at 8 bits, and
/// within one unit of the 16-bit scale at 10 and 12 bits, where avifdec scales in single precision (<see cref="ColorBound.Scaled16"/>).</description></item>
/// <item><description>Matrixed samples are compared with the bound of their case (<see cref="ColorBound"/>): the AV1 planes are the same on both
/// sides, only the conversion to RGB differs (rounding of the output, of the coefficients and of the upsampled chroma).</description></item>
/// <item><description><c>irot</c> and <c>imir</c> are reported as <see cref="ImageMetadata.Orientation"/> and never applied: avifdec applies them,
/// so its output is compared with the pixels of the library after <c>AutoOrient()</c>, and the pixels as loaded are compared
/// with avifdec's decoding of the same image encoded without transformation.</description></item>
/// <item><description>Image sequences (written by avifenc and by FFmpeg's <c>avif</c> muxer from libaom and SVT-AV1) are compared frame by
/// frame, with exact durations and play counts, through <c>Image.Load</c>, <c>Image.Identify</c> and <c>Image.OpenReader</c>.</description></item>
/// <item><description>Apple ImageIO decodes a few 8-bit files on macOS (<see cref="EightBitImagesDecodeAsAppleImageIO"/>).</description></item>
/// </list>
/// The theories of this class run concurrently: every case writes files of its own name to the artifacts directory.
/// </summary>
public sealed class AvifDecoderInteropTests
{
    /// <summary>
    /// The bound of matrixed 4:4:4 samples against Apple ImageIO, which returns 8-bit samples computed with its own
    /// fixed-point matrix: three 8-bit steps, which is the measured difference (2 steps on macOS 27, where the library is
    /// within half a step of avifdec on the same files) plus one, the margin of the tolerances of the corpus.
    /// </summary>
    private const int ImageIOMatrixBound = 3 * 257;

    private static readonly Dictionary<string, StillCase> Stills = CreateStillCases().ToDictionary(item => item.Name, StringComparer.Ordinal);
    private static readonly Dictionary<string, SequenceCase> Sequences = CreateSequenceCases().ToDictionary(item => item.Name, StringComparer.Ordinal);

    public static TheoryData<string> StillCases => [.. Stills.Keys];

    public static TheoryData<string> SequenceCases => [.. Sequences.Keys];

    /// <summary>
    /// The largest difference accepted between a color sample of the library and the same sample of avifdec, in units of the
    /// 16-bit scale (257 units are one 8-bit step). Alpha never has a tolerance. The AV1 planes are identical on both sides
    /// (the differential and conformance tests cover them): a difference comes from the conversion to RGB only, and each bound
    /// is the rounding that the two conversions can differ by, checked against the largest difference measured over the cases
    /// of its kind with libavif 1.4.2 (the measure of every case is written to the test output).
    /// </summary>
    private static class ColorBound
    {
        /// <summary>8-bit samples that are not matrixed (identity matrix, monochrome in full range): 16-bit samples are 257 times the 8-bit ones on both sides.</summary>
        public const int Exact = 0;

        /// <summary>
        /// 10 and 12-bit samples that are not matrixed. The library scales a sample exactly, <c>round(v * 65535 / max)</c>;
        /// avifdec computes <c>v / max * 65535 + 0.5</c> in single precision, which is one unit too high for the sample values
        /// whose exact product ends in .498 (measured on macOS arm64: 820 at 10 bits; 2320, 2593, 2866, 3139, 3412, 3685 and
        /// 3958 at 12 bits). <see cref="EverySampleValueIsScaledToSixteenBitsAsAvifDec"/> checks the library against the exact
        /// value, and avifdec against its neighbors, for every sample value.
        /// </summary>
        public const int Scaled16 = 1;

        /// <summary>
        /// 8-bit planes without chroma upsampling (4:4:4, or monochrome in limited range): the library rounds each component
        /// once to 8 bits, at most half an 8-bit step (128.5 units) away from the 16-bit sample avifdec computes in floating
        /// point. Measured: 129 (0.502/255).
        /// </summary>
        public const int Matrix8 = 130;

        /// <summary>10 and 12-bit planes without chroma upsampling: both sides produce 16-bit samples; the library uses integer coefficients with 16 fractional bits, avifdec single-precision floating point. Measured: 1.</summary>
        public const int Matrix16 = 2;

        /// <summary>
        /// 8-bit planes with subsampled chroma. Both sides upsample chroma with the same bilinear filter (libyuv in avifdec),
        /// but the library rounds the upsampled chroma to the precision of the planes before the matrix: half a chroma step,
        /// multiplied by the largest chroma coefficient (2.142 for the blue of BT.2020 in limited range, 1.772 for BT.601 in
        /// full range), on top of the half step of <see cref="Matrix8"/>: at most 0.5 + 0.5 x 2.142 = 1.571 8-bit steps.
        /// Measured: 400 (1.556/255) for BT.709 and BT.2020 in limited range, 356 (1.385/255) for BT.601 in full range.
        /// </summary>
        public const int Subsampled8 = 405;

        /// <summary>10-bit planes with subsampled chroma: half a 10-bit chroma step (32 units) times the largest chroma coefficient (2.142), plus the rounding of both outputs. Measured: 69.</summary>
        public const int Subsampled10 = 71;

        /// <summary>12-bit planes with subsampled chroma: half a 12-bit chroma step (8 units) times the largest chroma coefficient (2.142), plus the rounding of both outputs. Measured: 15.</summary>
        public const int Subsampled12 = 19;

        /// <summary>The bound of subsampled chroma for a bit depth.</summary>
        public static int Subsampled(int depth) => depth switch
        {
            8 => Subsampled8,
            10 => Subsampled10,
            _ => Subsampled12,
        };

        /// <summary>The bound of matrixed 4:4:4 samples for a bit depth.</summary>
        public static int Matrix(int depth) => depth == 8 ? Matrix8 : Matrix16;

        /// <summary>The bound of samples that are not matrixed for a bit depth.</summary>
        public static int Unmatrixed(int depth) => depth == 8 ? Exact : Scaled16;
    }

    private enum SourceKind
    {
        Rgb8,
        Rgba8,
        Rgb16,
        Rgba16,
        Gray8,
        Gray16,
    }

    /// <param name="Name">The name of the case and of its files.</param>
    /// <param name="Source">The layout of the PNG source.</param>
    /// <param name="Width">The width of the source.</param>
    /// <param name="Height">The height of the source.</param>
    /// <param name="Options">The avifenc options.</param>
    /// <param name="MaxColorError">The bound of the color samples (<see cref="ColorBound"/>).</param>
    private sealed record StillCase(string Name, SourceKind Source, int Width, int Height, string[] Options, int MaxColorError)
    {
        /// <summary>The color description passed to avifenc (<c>--cicp</c> and <c>--range</c>), which the library must report.</summary>
        public CicpColorDescription? Cicp { get; init; }

        /// <summary>Whether the source is stored without loss and without conversion: avifdec must return its samples.</summary>
        public bool SourceIsPreserved { get; init; }

        /// <summary>
        /// The bit depth of the primary item when it is not the depth <c>avifdec --info</c> prints: with a sample transform,
        /// avifdec describes the 16-bit image it could reconstruct and decodes the primary item.
        /// </summary>
        public int? PrimaryItemDepth { get; init; }

        /// <summary>The content light level passed to avifenc (<c>--clli</c>), which the library must report.</summary>
        public ContentLightLevel? ContentLightLevel { get; init; }

        /// <summary>The pixel aspect ratio passed to avifenc (<c>--pasp</c>), which the library must report.</summary>
        public PixelAspectRatio? PixelAspectRatio { get; init; }
    }

    /// <param name="Name">The name of the case and of its files.</param>
    /// <param name="Source">The layout of the PNG sources.</param>
    /// <param name="Width">The width of the frames.</param>
    /// <param name="Height">The height of the frames.</param>
    /// <param name="Options">The avifenc options that apply to the whole sequence.</param>
    /// <param name="TimeScale">The time scale (<c>--timescale</c>).</param>
    /// <param name="Durations">The duration of each frame in units of the time scale (<c>--duration</c>).</param>
    /// <param name="RepetitionCount">The repetition count (<c>--repetition-count</c>): a number, or <see langword="null"/> for <c>infinite</c>.</param>
    /// <param name="MaxColorError">The bound of the color samples (<see cref="ColorBound"/>).</param>
    private sealed record SequenceCase(string Name, SourceKind Source, int Width, int Height, string[] Options, int TimeScale, int[] Durations, int? RepetitionCount, int MaxColorError)
    {
        /// <summary>Whether <c>--repetition-count</c> is passed; avifenc repeats forever otherwise.</summary>
        public bool PassRepetitionCount { get; init; } = true;

        /// <summary>The option that sets the time scale: <c>--timescale</c> or its other name, <c>--fps</c>.</summary>
        public string TimeScaleOption { get; init; } = "--timescale";
    }

    private static IEnumerable<StillCase> CreateStillCases()
    {
        // Lossless: identity matrix for color, monochrome for gray; the AV1 planes hold the samples avifenc read
        yield return new("lossless-rgb-8", SourceKind.Rgb8, 33, 21, ["--lossless"], ColorBound.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgba-8", SourceKind.Rgba8, 32, 24, ["--lossless"], ColorBound.Exact) { SourceIsPreserved = true };
        yield return new("lossless-gray-8", SourceKind.Gray8, 31, 17, ["--lossless"], ColorBound.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgb-10", SourceKind.Rgb16, 32, 24, ["--lossless", "--depth", "10"], ColorBound.Scaled16);
        yield return new("lossless-rgba-10", SourceKind.Rgba16, 33, 21, ["--lossless", "--depth", "10"], ColorBound.Scaled16);
        yield return new("lossless-gray-10", SourceKind.Gray16, 32, 24, ["--lossless", "--depth", "10"], ColorBound.Scaled16);
        yield return new("lossless-rgb-12", SourceKind.Rgb16, 31, 17, ["--lossless", "--depth", "12"], ColorBound.Scaled16);
        yield return new("lossless-rgba-12", SourceKind.Rgba16, 32, 24, ["--lossless", "--depth", "12"], ColorBound.Scaled16);
        yield return new("lossless-gray-12", SourceKind.Gray16, 33, 21, ["--lossless", "--depth", "12"], ColorBound.Scaled16);
        yield return new("lossless-gray-alpha-8", SourceKind.Rgba8, 32, 24, ["--lossless", "--yuv", "400"], ColorBound.Exact);
        yield return new("lossless-gray-alpha-12", SourceKind.Rgba16, 32, 24, ["--lossless", "--yuv", "400", "--depth", "12"], ColorBound.Scaled16);

        // Lossy: every depth and chroma format; the default matrix of avifenc is BT.601 in full range
        foreach (var depth in new[] { 8, 10, 12 })
        {
            var source = depth > 8 ? SourceKind.Rgb16 : SourceKind.Rgb8;
            string[] depthOptions = ["--depth", depth.ToString(CultureInfo.InvariantCulture)];
            yield return new($"lossy-444-{depth}", source, 48, 32, ["--qcolor", "70", "--yuv", "444", .. depthOptions], ColorBound.Matrix(depth));
            yield return new($"lossy-422-{depth}", source, 47, 33, ["--qcolor", "60", "--yuv", "422", .. depthOptions], ColorBound.Subsampled(depth));
            yield return new($"lossy-420-{depth}", source, 47, 33, ["--qcolor", "50", "--yuv", "420", .. depthOptions], ColorBound.Subsampled(depth));
            yield return new($"lossy-400-{depth}", source, 48, 32, ["--qcolor", "40", "--yuv", "400", .. depthOptions], ColorBound.Unmatrixed(depth));
        }

        // Alpha: lossless and lossy alpha planes, on lossy color; the alpha plane is always compared exactly
        yield return new("lossy-420-8-alpha", SourceKind.Rgba8, 48, 32, ["--qcolor", "60", "--qalpha", "100", "--yuv", "420"], ColorBound.Subsampled8);
        yield return new("lossy-420-8-lossy-alpha", SourceKind.Rgba8, 47, 33, ["--qcolor", "60", "--qalpha", "40", "--yuv", "420"], ColorBound.Subsampled8);
        yield return new("lossy-444-10-alpha", SourceKind.Rgba16, 48, 32, ["--qcolor", "70", "--qalpha", "60", "--yuv", "444", "--depth", "10"], ColorBound.Matrix16);
        yield return new("lossy-422-12-alpha", SourceKind.Rgba16, 48, 32, ["--qcolor", "70", "--qalpha", "100", "--yuv", "422", "--depth", "12"], ColorBound.Subsampled12);
        yield return new("lossy-400-8-alpha", SourceKind.Rgba8, 48, 32, ["--qcolor", "50", "--qalpha", "50", "--yuv", "400"], ColorBound.Exact);

        // Color descriptions: every supported matrix, in full and in limited range. libavif converts neither to nor from
        // YCgCo in limited range, so that combination has no reference
        foreach (var (matrix, label) in new[] { (1, "bt709"), (4, "fcc"), (5, "bt470bg"), (6, "bt601"), (7, "smpte240"), (8, "ycgco"), (9, "bt2020ncl") })
        {
            foreach (var fullRange in matrix == 8 ? [true] : new[] { true, false })
            {
                var range = fullRange ? "full" : "limited";
                var cicp = new CicpColorDescription(1, 13, (byte)matrix, fullRange);
                string[] options = ["--qcolor", "75", "--cicp", $"1/13/{matrix}", "--range", range];
                yield return new($"cicp-{label}-{range}-444-8", SourceKind.Rgb8, 32, 24, [.. options, "--yuv", "444"], ColorBound.Matrix8) { Cicp = cicp };
                yield return new($"cicp-{label}-{range}-420-8", SourceKind.Rgb8, 32, 24, [.. options, "--yuv", "420"], ColorBound.Subsampled8) { Cicp = cicp };
            }
        }

        yield return new("cicp-bt2020-pq-limited-420-10", SourceKind.Rgb16, 32, 24, ["--qcolor", "75", "--cicp", "9/16/9", "--range", "limited", "--yuv", "420", "--depth", "10"], ColorBound.Subsampled10) { Cicp = new(9, 16, 9, fullRange: false) };
        yield return new("cicp-bt2020-hlg-full-444-12", SourceKind.Rgb16, 32, 24, ["--qcolor", "75", "--cicp", "9/18/9", "--range", "full", "--yuv", "444", "--depth", "12"], ColorBound.Matrix16) { Cicp = new(9, 18, 9, fullRange: true) };
        yield return new("cicp-bt709-limited-422-10-alpha", SourceKind.Rgba16, 32, 24, ["--qcolor", "75", "--cicp", "1/1/1", "--range", "limited", "--yuv", "422", "--depth", "10"], ColorBound.Subsampled10) { Cicp = new(1, 1, 1, fullRange: false) };
        yield return new("cicp-identity-full-444-8", SourceKind.Rgb8, 32, 24, ["--qcolor", "75", "--cicp", "12/13/0", "--range", "full", "--yuv", "444"], ColorBound.Exact) { Cicp = new(12, 13, 0, fullRange: true) };
        yield return new("cicp-identity-limited-444-10", SourceKind.Rgb16, 32, 24, ["--qcolor", "75", "--cicp", "1/13/0", "--range", "limited", "--yuv", "444", "--depth", "10"], ColorBound.Matrix16) { Cicp = new(1, 13, 0, fullRange: false) };
        yield return new("cicp-unspecified-full-420-8", SourceKind.Rgb8, 32, 24, ["--qcolor", "75", "--cicp", "2/2/2", "--range", "full", "--yuv", "420"], ColorBound.Subsampled8) { Cicp = new(2, 2, 2, fullRange: true) };
        yield return new("cicp-gray-limited-400-8", SourceKind.Gray8, 32, 24, ["--qcolor", "75", "--cicp", "1/13/1", "--range", "limited"], ColorBound.Matrix8) { Cicp = new(1, 13, 1, fullRange: false) };
        yield return new("cicp-gray-limited-400-12", SourceKind.Gray16, 32, 24, ["--qcolor", "75", "--cicp", "1/13/1", "--range", "limited", "--depth", "12"], ColorBound.Matrix16) { Cicp = new(1, 13, 1, fullRange: false) };

        // Grids: avifenc splits the source in cells of at least 64 pixels
        yield return new("grid-2x2-lossless-rgb-8", SourceKind.Rgb8, 128, 128, ["--lossless", "--grid", "2x2"], ColorBound.Exact) { SourceIsPreserved = true };
        yield return new("grid-3x1-lossless-rgba-8", SourceKind.Rgba8, 192, 64, ["--lossless", "--grid", "3x1"], ColorBound.Exact) { SourceIsPreserved = true };
        yield return new("grid-2x1-lossy-420-8-alpha", SourceKind.Rgba8, 128, 64, ["--qcolor", "60", "--qalpha", "60", "--yuv", "420", "--grid", "2x1"], ColorBound.Subsampled8);
        yield return new("grid-1x2-lossy-444-10", SourceKind.Rgb16, 64, 128, ["--qcolor", "60", "--yuv", "444", "--depth", "10", "--grid", "1x2"], ColorBound.Matrix16);
        yield return new("grid-2x2-lossy-400-12-alpha", SourceKind.Rgba16, 128, 128, ["--qcolor", "60", "--qalpha", "100", "--yuv", "400", "--depth", "12", "--grid", "2x2"], ColorBound.Scaled16);

        // Encoder settings that change the coding tools, not the container
        yield return new("lossy-420-8-speed-0", SourceKind.Rgb8, 64, 48, ["--qcolor", "45", "--yuv", "420", "--speed", "0"], ColorBound.Subsampled8);
        yield return new("lossy-420-8-speed-10", SourceKind.Rgb8, 64, 48, ["--qcolor", "45", "--yuv", "420", "--speed", "10"], ColorBound.Subsampled8);
        yield return new("lossy-420-8-q5", SourceKind.Rgb8, 64, 48, ["--qcolor", "5", "--yuv", "420"], ColorBound.Subsampled8);
        yield return new("lossy-444-8-q99", SourceKind.Rgb8, 64, 48, ["--qcolor", "99", "--yuv", "444"], ColorBound.Matrix8);
        yield return new("lossy-420-8-tiles", SourceKind.Rgb8, 64, 48, ["--qcolor", "50", "--yuv", "420", "--tilerowslog2", "1", "--tilecolslog2", "1"], ColorBound.Subsampled8);
        yield return new("lossy-420-8-film-grain", SourceKind.Rgb8, 64, 48, ["--qcolor", "50", "--yuv", "420", "--advanced", "film-grain-test=4"], ColorBound.Subsampled8);
        yield return new("lossy-420-10-film-grain", SourceKind.Rgb16, 64, 48, ["--qcolor", "50", "--yuv", "420", "--depth", "10", "--advanced", "film-grain-test=11"], ColorBound.Subsampled10);
        yield return new("lossy-16x16-420-8", SourceKind.Rgb8, 16, 16, ["--qcolor", "50", "--yuv", "420"], ColorBound.Subsampled8);

        // Properties that describe how the samples are displayed: they are reported, and the pixels are those of the same
        // file without them
        yield return new("lossy-420-8-pasp-clli", SourceKind.Rgb8, 32, 24, ["--qcolor", "50", "--yuv", "420", "--pasp", "2,1", "--clli", "1000,400"], ColorBound.Subsampled8) { ContentLightLevel = new(1000, 400), PixelAspectRatio = new(2, 1) };
        yield return new("lossy-444-10-pasp-clli-alpha", SourceKind.Rgba16, 32, 24, ["--qcolor", "50", "--yuv", "444", "--depth", "10", "--cicp", "9/16/9", "--pasp", "64,45", "--clli", "65535,1"], ColorBound.Matrix16) { Cicp = new(9, 16, 9, fullRange: true), ContentLightLevel = new(65535, 1), PixelAspectRatio = new(64, 45) };

        // A hidden image that extends the depth of the primary item to 16 bits (sample transform): the primary item is presented,
        // which avifdec decodes unless it is asked for the transform
        yield return new("lossless-rgb-8-extension-8", SourceKind.Rgb16, 32, 24, ["--lossless", "--depth", "8,8"], ColorBound.Exact) { PrimaryItemDepth = 8 };
        yield return new("lossless-rgba-12-extension-4", SourceKind.Rgba16, 32, 24, ["--lossless", "--depth", "12,4"], ColorBound.Scaled16) { PrimaryItemDepth = 12 };
        yield return new("lossy-1x1-444-8", SourceKind.Rgb8, 1, 1, ["--qcolor", "50", "--yuv", "444"], ColorBound.Matrix8);

        // Progressive images: avifenc codes a coarse layer and the image as two spatial layers of one item (and of its alpha
        // item); the image is the last layer, which avifdec decodes unless it is asked for the progression
        yield return new("progressive-420-8", SourceKind.Rgb8, 64, 48, ["--progressive", "--qcolor", "60", "--yuv", "420"], ColorBound.Subsampled8);
        yield return new("progressive-444-8-alpha", SourceKind.Rgba8, 64, 48, ["--progressive", "--qcolor", "70", "--qalpha", "70", "--yuv", "444"], ColorBound.Matrix8);
        yield return new("progressive-422-10", SourceKind.Rgb16, 48, 64, ["--progressive", "--qcolor", "60", "--yuv", "422", "--depth", "10"], ColorBound.Subsampled10);
        yield return new("progressive-400-12", SourceKind.Gray16, 64, 64, ["--progressive", "--qcolor", "60", "--depth", "12"], ColorBound.Scaled16);
    }

    private static IEnumerable<SequenceCase> CreateSequenceCases()
    {
        yield return new("sequence-lossless-rgb-8", SourceKind.Rgb8, 32, 24, ["--lossless"], 30, [1, 1, 1], RepetitionCount: null, ColorBound.Exact) { PassRepetitionCount = false };
        yield return new("sequence-lossy-444-8-once", SourceKind.Rgb8, 48, 32, ["--qcolor", "60", "--yuv", "444"], 10, [3, 1, 1, 7], RepetitionCount: 0, ColorBound.Matrix8) { TimeScaleOption = "--fps" };
        yield return new("sequence-lossy-420-8-alpha-3-plays", SourceKind.Rgba8, 48, 32, ["--qcolor", "60", "--qalpha", "70", "--yuv", "420"], 1000, [40, 1, 100, 1000, 33], RepetitionCount: 2, ColorBound.Subsampled8);
        yield return new("sequence-lossy-420-10-infinite", SourceKind.Rgb16, 48, 32, ["--qcolor", "60", "--yuv", "420", "--depth", "10"], 90000, [3003, 3003, 3003, 6006], RepetitionCount: null, ColorBound.Subsampled10);
        yield return new("sequence-lossless-rgba-10-alpha", SourceKind.Rgba16, 32, 24, ["--lossless", "--depth", "10"], 24, [1, 2, 3], RepetitionCount: 4, ColorBound.Scaled16);
        yield return new("sequence-lossy-400-12-keyframes", SourceKind.Gray16, 48, 32, ["--qcolor", "60", "--depth", "12", "--keyframe", "2"], 25, [1, 1, 1, 1, 1], RepetitionCount: 9, ColorBound.Scaled16);
        yield return new("sequence-lossy-422-8-seven-plays", SourceKind.Rgb8, 64, 48, ["--qcolor", "40", "--yuv", "422", "--speed", "8"], 60000, [1001, 2002, 1001, 1001, 1001, 1001], RepetitionCount: 6, ColorBound.Subsampled8);
    }

    [Theory]
    [MemberData(nameof(StillCases))]
    public async Task StillImagesDecodeAsAvifDec(string name)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var item = Stills[name];
        var source = CreateSource(item.Source, item.Width, item.Height, seed: StableSeed(name));
        var sourcePath = await SaveSourceAsync(name, source);
        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync([.. item.Options, sourcePath], path, XunitCancellationToken);

        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        var reference = await avif.DecodeAsync(path, XunitCancellationToken);
        Assert.Equal((item.Width, item.Height), (info.Width, info.Height));
        Assert.Equal((item.Width, item.Height), (reference.Width, reference.Height));
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.HasRepetitionCount, $"{name}: avifdec reports a repetition count for a still image.");
        if (item.SourceIsPreserved)
        {
            // The pipeline is sound: libpng read the PNG file of the library, and avifdec returns its samples
            AssertPixels(ToRgba16(source), reference.Pixels, ColorBound.Exact, $"{name}: source as decoded by avifdec");
        }

        var depth = item.PrimaryItemDepth ?? info.BitDepth;
        using var image = Image.Load(path);
        Assert.Equal(ImageFormat.Avif, image.Metadata.SourceFormat);
        Assert.Equal(ExpectedPixelFormat(depth, info), image.PixelFormat);
        Assert.Equal(1, image.Frames.Count);
        Assert.Null(image.Animation);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        AssertColorDescription(info, item.Cicp, image.Metadata.Cicp, name);

        // avifdec prints the light levels and the terms of the pixel aspect ratio of a file that has them
        Assert.Equal(item.ContentLightLevel, image.Metadata.ContentLightLevel);
        Assert.Equal(item.PixelAspectRatio, image.Metadata.PixelAspectRatio);
        Assert.Equal<(int, int)?>(item.ContentLightLevel is { } level ? (level.MaxContentLightLevel, level.MaxPictureAverageLightLevel) : null, info.ContentLightLevel);
        Assert.Equal<(int, int)?>(item.PixelAspectRatio is { } ratio ? ((int)ratio.HorizontalSpacing, (int)ratio.VerticalSpacing) : null, info.PixelAspectRatio);
        Assert.Null(image.Metadata.MasteringDisplay);
        var measured = AssertPixels(reference.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), item.MaxColorError, $"{name}: library vs avifdec {avif.Version}");
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {info.Format} {info.BitDepth}-bit matrix {info.MatrixCoefficients} {(info.FullRange ? "full" : "limited")}{(info.HasAlpha ? " alpha" : "")}: max color difference {measured.Max} ({measured.Max / 257.0:F3}/255), mean {measured.Mean:F2}, bound {item.MaxColorError}"));

        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var identified = Image.Identify(path, new ImageIdentifyOptions { Mode = mode });
            Assert.Equal(ImageFormat.Avif, identified.Format);
            Assert.Equal(new Size(info.Width, info.Height), identified.Size);
            Assert.Equal(image.PixelFormat, identified.PixelFormat);
            Assert.Equal(depth, identified.BitsPerComponent);
            Assert.Equal<int?>(1, identified.FrameCount);
            Assert.Equal<bool?>(false, identified.IsAnimated);
            Assert.Equal<bool?>(info.HasAlpha, identified.MayHaveTransparency);
            Assert.Null(identified.Animation);
            Assert.Equal(image.Metadata.Cicp, identified.Metadata.Cicp);
            Assert.Equal(item.ContentLightLevel, identified.Metadata.ContentLightLevel);
            Assert.Equal(item.PixelAspectRatio, identified.Metadata.PixelAspectRatio);
            Assert.Equal(ExifOrientation.TopLeft, identified.Metadata.Orientation);
        }
    }

    /// <summary>
    /// Every sample value of a 10-bit and of a 12-bit image, stored without loss, for a monochrome plane and for the three
    /// planes of an identity-matrix image: the library scales each of them to 16 bits exactly, <c>round(v * 65535 / max)</c>,
    /// and avifdec, which computes in single precision, returns that value or a neighbor (<see cref="ColorBound.Scaled16"/>).
    /// </summary>
    [Theory]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(12, false)]
    [InlineData(12, true)]
    public async Task EverySampleValueIsScaledToSixteenBitsAsAvifDec(int depth, bool color)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var max = (1 << depth) - 1;
        var (width, height) = (64, (max + 1) / 64);
        var layout = color ? RawPixelLayout.Rgba16Le : RawPixelLayout.Gray16Le;
        var bytes = new byte[layout.GetByteLength(width, height)];
        for (var i = 0; i <= max; i++)
        {
            // The 16-bit value that avifenc maps back to the sample i; each plane of a color image sees the values in another order
            var pixel = bytes.AsSpan(i * layout.BytesPerPixel);
            for (var channel = 0; channel < (color ? 3 : 1); channel++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(pixel[(channel * 2)..], Scale(GetSample(i, channel)));
            }

            if (color)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(pixel[6..], ushort.MaxValue);
            }
        }

        var name = $"scale-{depth}-{(color ? "rgb" : "gray")}";
        var sourcePath = await SaveSourceAsync(name, RawPixelBuffer.Create(width, height, layout, bytes));
        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync(["--lossless", "--depth", depth.ToString(CultureInfo.InvariantCulture), sourcePath], path, XunitCancellationToken);

        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((depth, color ? "YUV444" : "YUV400"), (info.BitDepth, info.Format));
        var reference = await avif.DecodeAsync(path, XunitCancellationToken);
        var values = new HashSet<int>();
        for (var i = 0; i <= max; i++)
        {
            values.Add(reference.Pixels.GetSample(i % width, i / width, 0));
        }

        // Increasing 16-bit values that give max + 1 distinct samples: the sample of pixel i is i
        Assert.HasCount(max + 1, values, $"{name}: avifdec does not return {max + 1} distinct sample values; the image does not cover every value.");
        using var image = Image.Load(path);
        Assert.Equal(color ? PixelFormat.Rgba64 : PixelFormat.Gray16, image.PixelFormat);
        var decoded = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));
        var inexact = new SortedSet<int>();
        for (var i = 0; i <= max; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var sample = GetSample(i, color ? channel : 0);
                var exact = Scale(sample);
                Assert.Equal(exact, decoded.GetSample(i % width, i / width, channel), message: $"{name}: the sample {sample} is not scaled to round({sample} * 65535 / {max}).");
                var referenceValue = reference.Pixels.GetSample(i % width, i / width, channel);
                if (referenceValue != exact)
                {
                    Assert.InRange(referenceValue, exact - 1, exact + 1, message: $"{name}: avifdec scales the sample {sample} to a value that is more than one unit away from the exact one.");
                    inexact.Add(sample);
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: avifdec {avif.Version} is one unit away from the exact scaling for {inexact.Count} of the {max + 1} sample values: {string.Join(", ", inexact)}"));
        AssertPixels(reference.Pixels, decoded, ColorBound.Scaled16, $"{name}: library vs avifdec {avif.Version}");

        int GetSample(int index, int channel) => channel switch
        {
            0 => index,
            1 => max - index,
            _ => (index * 7) & max,
        };

        ushort Scale(int value) => (ushort)(((value * 65535L) + (max / 2)) / max);
    }

    /// <summary>
    /// <c>--irot A</c> (A quarter turns anti-clockwise) then <c>--imir M</c> (0 exchanges top and bottom, 1 left and right): the
    /// library reports the composition as an EXIF orientation and leaves the pixels as stored.
    /// </summary>
    [Theory]
    [InlineData(0, -1, ExifOrientation.TopLeft)]
    [InlineData(1, -1, ExifOrientation.LeftBottom)]
    [InlineData(2, -1, ExifOrientation.BottomRight)]
    [InlineData(3, -1, ExifOrientation.RightTop)]
    [InlineData(0, 1, ExifOrientation.TopRight)]
    [InlineData(1, 1, ExifOrientation.RightBottom)]
    [InlineData(2, 1, ExifOrientation.BottomLeft)]
    [InlineData(3, 1, ExifOrientation.LeftTop)]
    [InlineData(0, 0, ExifOrientation.BottomLeft)]
    [InlineData(1, 0, ExifOrientation.LeftTop)]
    [InlineData(2, 0, ExifOrientation.TopRight)]
    [InlineData(3, 0, ExifOrientation.RightBottom)]
    [InlineData(-1, 0, ExifOrientation.BottomLeft)]
    [InlineData(-1, 1, ExifOrientation.TopRight)]
    public async Task RotationAndMirrorAreReportedAsOrientationAndNotApplied(int rotation, int mirror, ExifOrientation expected)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var name = $"orientation-irot{(rotation < 0 ? "none" : rotation.ToString(CultureInfo.InvariantCulture))}-imir{(mirror < 0 ? "none" : mirror.ToString(CultureInfo.InvariantCulture))}";
        var source = CreateSource(SourceKind.Rgba8, 20, 12, seed: 17);
        var sourcePath = await SaveSourceAsync(name, source);
        var plainPath = ArtifactPath(name + "-plain.avif");
        var path = ArtifactPath(name + ".avif");
        List<string> options = ["--lossless"];
        await avif.EncodeAsync([.. options, sourcePath], plainPath, XunitCancellationToken);
        if (rotation >= 0)
        {
            options.AddRange(["--irot", rotation.ToString(CultureInfo.InvariantCulture)]);
        }

        if (mirror >= 0)
        {
            options.AddRange(["--imir", mirror.ToString(CultureInfo.InvariantCulture)]);
        }

        await avif.EncodeAsync([.. options, sourcePath], path, XunitCancellationToken);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((rotation < 0 ? (int?)null : rotation, mirror < 0 ? (int?)null : mirror), (info.Rotation, info.Mirror));

        // The pixels of the library are those of the image without transformation...
        var plain = await avif.DecodeAsync(plainPath, XunitCancellationToken);
        AssertPixels(ToRgba16(source), plain.Pixels, ColorBound.Exact, $"{name}: source as decoded by avifdec");
        using var image = Image.Load(path);
        Assert.Equal(expected, image.Metadata.Orientation);
        Assert.Equal(new Size(20, 12), image.Size);
        AssertPixels(plain.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Exact, $"{name}: library (as loaded) vs avifdec without transformation");
        Assert.Equal(expected, Image.Identify(path).Metadata.Orientation);
        Assert.Equal(new Size(20, 12), Image.Identify(path).Size);

        // ... and applying the orientation gives the image avifdec writes, which applies irot then imir
        var oriented = await avif.DecodeAsync(path, XunitCancellationToken);
        var swapped = rotation is 1 or 3;
        Assert.Equal(swapped ? (12, 20) : (20, 12), (oriented.Width, oriented.Height));
        image.AutoOrient(XunitCancellationToken);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        AssertPixels(oriented.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Exact, $"{name}: library after AutoOrient vs avifdec {avif.Version}");
    }

    /// <summary>
    /// A clean aperture (<c>clap</c>) that selects whole samples crops the image, before the rotation that follows it; the
    /// crop of a subsampled image has even offsets.
    /// </summary>
    [Theory]
    [InlineData("444", 3, 5, 17, 9, -1, false)]
    [InlineData("444", 0, 0, 31, 23, -1, false)]
    [InlineData("420", 4, 2, 16, 10, -1, false)]
    [InlineData("420", 6, 8, 21, 13, -1, false)]
    [InlineData("422", 2, 3, 20, 11, -1, false)]
    [InlineData("400", 5, 7, 9, 3, -1, false)]
    [InlineData("444", 3, 5, 17, 9, 1, false)]
    [InlineData("420", 4, 2, 16, 10, 3, false)]
    [InlineData("444", 3, 5, 17, 9, -1, true)]
    [InlineData("420", 4, 2, 16, 10, -1, true)]
    public async Task CleanApertureCropsTheImage(string yuv, int x, int y, int width, int height, int rotation, bool explicitClap)
    {
        const int SourceWidth = 32;
        const int SourceHeight = 24;
        var avif = await RequiredTools.LibAvifAsync();
        var name = string.Create(CultureInfo.InvariantCulture, $"{(explicitClap ? "clap" : "crop")}-{yuv}-{x}-{y}-{width}x{height}{(rotation < 0 ? "" : "-irot" + rotation.ToString(CultureInfo.InvariantCulture))}");
        var sourcePath = await SaveSourceAsync(name, CreateSource(SourceKind.Rgba8, SourceWidth, SourceHeight, seed: 23));
        var path = ArtifactPath(name + ".avif");

        // "--crop" computes the clean aperture; "--clap" gives it: the size, then the offset of the center of the aperture
        // from the center of the image, as fractions (width, height, horizontal offset, vertical offset)
        string[] aperture = explicitClap
            ? ["--clap", string.Create(CultureInfo.InvariantCulture, $"{width},1,{height},1,{(2 * x) + width - SourceWidth},2,{(2 * y) + height - SourceHeight},2")]
            : ["--crop", string.Create(CultureInfo.InvariantCulture, $"{x},{y},{width},{height}")];
        List<string> options = ["--qcolor", "70", "--qalpha", "100", "--yuv", yuv, .. aperture];
        if (rotation >= 0)
        {
            options.AddRange(["--irot", rotation.ToString(CultureInfo.InvariantCulture)]);
        }

        await avif.EncodeAsync([.. options, sourcePath], path, XunitCancellationToken);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal<(int, int, int, int)?>((x, y, width, height), info.CleanApertureCrop);

        var reference = await avif.DecodeAsync(path, XunitCancellationToken);
        var bound = yuv switch
        {
            "400" => ColorBound.Exact,
            "444" => ColorBound.Matrix8,
            _ => ColorBound.Subsampled8,
        };

        using var image = Image.Load(path);
        Assert.Equal(new Size(width, height), image.Size);
        Assert.Equal(new Size(width, height), Image.Identify(path).Size);
        Assert.Equal(rotation < 0 ? ExifOrientation.TopLeft : rotation == 1 ? ExifOrientation.LeftBottom : ExifOrientation.RightTop, image.Metadata.Orientation);
        image.AutoOrient(XunitCancellationToken);
        Assert.Equal((reference.Width, reference.Height), (image.Width, image.Height));
        AssertPixels(reference.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), bound, $"{name}: library vs avifdec {avif.Version}");
    }

    /// <summary>
    /// The position of the chroma samples that the bitstream declares (<c>chroma_sample_position</c>: unknown, vertical or
    /// co-located), taken by avifenc from a Y4M source. The planes are stored as they are; only the upsampling depends on the
    /// position. libavif ignores the position (avifdec always upsamples centered samples), so the reference is computed here,
    /// in floating point, from the planes that dav1d decodes: a bilinear interpolation of the chroma samples at the declared
    /// position, then the BT.601 matrix in limited range. The library, which rounds the upsampled chroma to the precision of
    /// the planes, is within <see cref="ColorBound.Subsampled8"/> of it.
    /// </summary>
    [Theory]
    [InlineData("center", 0)]
    [InlineData("left", 1)]
    [InlineData("topleft", 2)]
    public async Task ChromaIsUpsampledAtTheDeclaredSamplePosition(string location, int chromaSamplePosition)
    {
        // Even: the Y4M muxer of FFmpeg does not write the last odd row or column
        const int Width = 46;
        const int Height = 34;
        var avif = await RequiredTools.LibAvifAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var name = "chroma-position-" + location;
        var sourcePath = ArtifactPath(name + ".y4m");
        var source = await ffmpeg.RunAsync(
            ["-y", "-filter_threads", "1", "-f", "lavfi", "-i", string.Create(CultureInfo.InvariantCulture, $"testsrc2=size={Width}x{Height},noise=alls=10:allf=t:all_seed=3"), "-frames:v", "1", "-pix_fmt", "yuv420p", "-chroma_sample_location", location, "-strict", "-1", "-f", "yuv4mpegpipe", sourcePath],
            XunitCancellationToken);
        Assert.Equal(0, source.ExitCode, message: source.StandardError);
        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync(["--qcolor", "70", sourcePath], path, XunitCancellationToken);
        var trace = await ffmpeg.TraceHeadersAsync(path, format: null, XunitCancellationToken);
        Assert.Equal(1, CountOccurrences(trace, "chroma_sample_position", chromaSamplePosition.ToString(CultureInfo.InvariantCulture)));

        // A Y4M source is BT.601 in limited range
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((Width, Height, "YUV420", 8, 6, false), (info.Width, info.Height, info.Format, info.BitDepth, info.MatrixCoefficients, info.FullRange));

        // The planes as dav1d decodes them, without conversion: Y, then U and V at half the size, rounded up
        var planes = await ffmpeg.RunAsync(["-c:v", Av1ReferenceDecoder.Dav1d, "-i", path, "-map", "0:v:0", "-frames:v", "1", "-pix_fmt", "yuv420p", "-f", "rawvideo", "-"], XunitCancellationToken);
        Assert.Equal(0, planes.ExitCode, message: planes.StandardError);
        var (chromaWidth, chromaHeight) = ((Width + 1) / 2, (Height + 1) / 2);
        Assert.Equal((Width * Height) + (2 * chromaWidth * chromaHeight), planes.StandardOutput.Length);
        var luma = planes.StandardOutput.Span[..(Width * Height)];
        var u = planes.StandardOutput.Span.Slice(Width * Height, chromaWidth * chromaHeight);
        var v = planes.StandardOutput.Span[((Width * Height) + (chromaWidth * chromaHeight))..];

        // The offset of the first chroma sample from the first luma sample, in luma samples: half a sample when the chroma
        // sample is between two luma samples, none when it is at the position of the first one
        var offsetX = chromaSamplePosition == 0 ? 0.5 : 0;
        var offsetY = chromaSamplePosition == 2 ? 0 : 0.5;
        var expected = new byte[RawPixelLayout.Rgba16Le.GetByteLength(Width, Height)];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cb = Interpolate(u, chromaWidth, chromaHeight, (x - offsetX) / 2, (y - offsetY) / 2) - 128;
                var cr = Interpolate(v, chromaWidth, chromaHeight, (x - offsetX) / 2, (y - offsetY) / 2) - 128;
                var scaledLuma = (luma[(y * Width) + x] - 16) / 219.0;
                var pixel = expected.AsSpan(((y * Width) + x) * 8);
                BinaryPrimitives.WriteUInt16LittleEndian(pixel, ToSample(scaledLuma + (1.402 * cr / 224)));
                BinaryPrimitives.WriteUInt16LittleEndian(pixel[2..], ToSample(scaledLuma - ((0.114 * 1.772 / 0.587) * cb / 224) - ((0.299 * 1.402 / 0.587) * cr / 224)));
                BinaryPrimitives.WriteUInt16LittleEndian(pixel[4..], ToSample(scaledLuma + (1.772 * cb / 224)));
                BinaryPrimitives.WriteUInt16LittleEndian(pixel[6..], ushort.MaxValue);
            }
        }

        using var image = Image.Load(path);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        var decoded = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));

        // For information: avifdec, which ignores the position, is only close for centered samples
        var centered = MeasurePixels((await avif.DecodeAsync(path, XunitCancellationToken)).Pixels, decoded);
        var reference = MeasurePixels(RawPixelBuffer.Create(Width, Height, RawPixelLayout.Rgba16Le, expected), decoded);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: library vs bilinear interpolation at the declared position: max {reference.Max} ({reference.Max / 257.0:F3}/255), mean {reference.Mean:F2}; library vs avifdec {avif.Version} (centered): max {centered.Max} ({centered.Max / 257.0:F3}/255), mean {centered.Mean:F2}"));
        AssertPixels(RawPixelBuffer.Create(Width, Height, RawPixelLayout.Rgba16Le, expected), decoded, ColorBound.Subsampled8, $"{name}: library vs bilinear interpolation at the declared position");

        static double Interpolate(ReadOnlySpan<byte> plane, int width, int height, double x, double y)
        {
            // The samples of the edges are replicated
            var (x0, y0) = ((int)Math.Floor(x), (int)Math.Floor(y));
            var (fx, fy) = (x - x0, y - y0);
            var top = ((1 - fx) * Sample(plane, width, height, x0, y0)) + (fx * Sample(plane, width, height, x0 + 1, y0));
            var bottom = ((1 - fx) * Sample(plane, width, height, x0, y0 + 1)) + (fx * Sample(plane, width, height, x0 + 1, y0 + 1));
            return ((1 - fy) * top) + (fy * bottom);
        }

        static double Sample(ReadOnlySpan<byte> plane, int width, int height, int x, int y) => plane[(Math.Clamp(y, 0, height - 1) * width) + Math.Clamp(x, 0, width - 1)];

        static ushort ToSample(double value) => (ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The ICC profile, the EXIF block and the XMP packet given to avifenc are returned byte for byte. The orientation of the
    /// EXIF block is not the orientation of the image: avifenc turns it into <c>irot</c> and <c>imir</c> properties, which are
    /// the only source of <see cref="ImageMetadata.Orientation"/>, and an explicit <c>--irot</c> replaces them.
    /// </summary>
    [Theory]
    [InlineData(false, ExifOrientation.RightTop)]
    [InlineData(true, ExifOrientation.TopLeft)]
    public async Task MetadataPayloadsAreReadByteForByte(bool overrideRotation, ExifOrientation expectedOrientation)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var name = $"metadata-{(overrideRotation ? "irot0" : "exif-orientation")}";
        var source = CreateSource(SourceKind.Rgb8, 20, 12, seed: 29);
        var sourcePath = await SaveSourceAsync(name, source);

        // A minimal RGB display profile header (no tag), a little-endian TIFF structure whose only tag is Orientation = 6
        // (RightTop), and an XMP packet
        var icc = new byte[132];
        BinaryPrimitives.WriteUInt32BigEndian(icc, (uint)icc.Length);
        BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(8), 0x04300000);
        "mntr"u8.CopyTo(icc.AsSpan(12));
        "RGB "u8.CopyTo(icc.AsSpan(16));
        "XYZ "u8.CopyTo(icc.AsSpan(20));
        "acsp"u8.CopyTo(icc.AsSpan(36));
        byte[] exif = [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><lorem>ipsum</lorem></x:xmpmeta>"u8.ToArray();
        var iccPath = ArtifactPath(name + ".icc");
        var exifPath = ArtifactPath(name + ".exif");
        var xmpPath = ArtifactPath(name + ".xmp");
        await File.WriteAllBytesAsync(iccPath, icc, XunitCancellationToken);
        await File.WriteAllBytesAsync(exifPath, exif, XunitCancellationToken);
        await File.WriteAllBytesAsync(xmpPath, xmp, XunitCancellationToken);

        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync(["--lossless", "--icc", iccPath, "--exif", exifPath, "--xmp", xmpPath, .. (overrideRotation ? ["--irot", "0"] : Array.Empty<string>()), sourcePath], path, XunitCancellationToken);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal<(int?, int?, int?)>((icc.Length, exif.Length, xmp.Length), (info.IccProfileLength, info.ExifLength, info.XmpLength));
        Assert.Equal<int?>(overrideRotation ? 0 : 3, info.Rotation);

        using var image = Image.Load(path);
        Assert.Equal(icc, image.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(exif, image.Metadata.ExifProfile?.Data.ToArray());
        Assert.Equal(xmp, image.Metadata.XmpProfile?.Data.ToArray());
        Assert.Equal(expectedOrientation, image.Metadata.Orientation);
        Assert.Equal(new Size(20, 12), image.Size);
        AssertPixels(ToRgba16(source), ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Exact, $"{name}: library vs source");

        // The properties of the image (color profile, transformations) are in its header; the EXIF and XMP items are data of
        // the file, which a full scan reaches
        var header = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.Header });
        Assert.Equal(icc, header.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(expectedOrientation, header.Metadata.Orientation);
        var scanned = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(icc, scanned.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(exif, scanned.Metadata.ExifProfile?.Data.ToArray());
        Assert.Equal(xmp, scanned.Metadata.XmpProfile?.Data.ToArray());
        Assert.Equal(expectedOrientation, scanned.Metadata.Orientation);

        var reference = await avif.DecodeAsync(path, XunitCancellationToken);
        image.AutoOrient(XunitCancellationToken);
        AssertPixels(reference.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Exact, $"{name}: library after AutoOrient vs avifdec {avif.Version}");
    }

    [Theory]
    [MemberData(nameof(SequenceCases))]
    public async Task ImageSequencesDecodeAsAvifDec(string name)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var item = Sequences[name];
        List<string> arguments = [.. item.Options, item.TimeScaleOption, item.TimeScale.ToString(CultureInfo.InvariantCulture)];
        if (item.PassRepetitionCount)
        {
            arguments.AddRange(["--repetition-count", item.RepetitionCount?.ToString(CultureInfo.InvariantCulture) ?? "infinite"]);
        }

        for (var i = 0; i < item.Durations.Length; i++)
        {
            var framePath = await SaveSourceAsync($"{name}-{i}", CreateSource(item.Source, item.Width, item.Height, seed: StableSeed(name), frame: i));
            arguments.AddRange(["--duration", item.Durations[i].ToString(CultureInfo.InvariantCulture), framePath]);
        }

        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync(arguments, path, XunitCancellationToken);

        // avifdec reads back what avifenc was asked to write
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal(item.TimeScale, info.TimeScale);
        Assert.Equal(item.Durations.Select(duration => (long)duration), info.Frames.Select(frame => frame.Duration));
        Assert.True(info.HasRepetitionCount, $"{name}: avifdec reports no repetition count.");
        Assert.Equal(item.RepetitionCount, info.RepetitionCount);

        var totalPlays = item.RepetitionCount + 1;
        await AssertSequenceAsync(avif, path, info, totalPlays, item.MaxColorError, name);

        // --index N decodes the same frame as --index all
        var all = await avif.DecodeAllFramesAsync(path, XunitCancellationToken);
        var last = await avif.DecodeAsync(path, all.Count - 1, XunitCancellationToken);
        AssertPixels(all[^1].Pixels, last.Pixels, ColorBound.Exact, $"{name}: avifdec --index {all.Count - 1} vs --index all");
    }

    /// <summary>
    /// The XMP packet of an image sequence: avifenc stores it as an item of the <c>meta</c> box of the track (and of the file),
    /// and avifdec returns it. avifenc 1.4.2 crashes when a sequence is given EXIF data or a transformation, so the XMP
    /// packet is the only metadata item of a sequence that can be compared.
    /// </summary>
    [Fact]
    public async Task SequenceXmpIsReadAsAvifDec()
    {
        var avif = await RequiredTools.LibAvifAsync();
        const string Name = "sequence-xmp";
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><lorem>ipsum</lorem></x:xmpmeta>"u8.ToArray();
        var xmpPath = ArtifactPath(Name + ".xmp");
        await File.WriteAllBytesAsync(xmpPath, xmp, XunitCancellationToken);
        List<string> arguments = ["--lossless", "--xmp", xmpPath, "--fps", "10"];
        for (var i = 0; i < 3; i++)
        {
            arguments.Add(await SaveSourceAsync($"{Name}-{i}", CreateSource(SourceKind.Rgb8, 20, 12, seed: 31, frame: i)));
        }

        var path = ArtifactPath(Name + ".avif");
        await avif.EncodeAsync(arguments, path, XunitCancellationToken);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal<int?>(xmp.Length, info.XmpLength);
        Assert.Equal(3, info.Frames.Count);

        using var image = Image.Load(path);
        Assert.Equal(3, image.Frames.Count);
        Assert.Equal(xmp, image.Metadata.XmpProfile?.Data.ToArray());
        var scanned = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(xmp, scanned.Metadata.XmpProfile?.Data.ToArray());

        // The item is data of the file, which avifenc writes before the samples: every image of a reader has it
        await using var stream = File.OpenRead(path);
        await using var reader = await Image.OpenReaderAsync<Rgb24>(stream, cancellationToken: XunitCancellationToken);
        var count = 0;
        while (await reader.ReadFrameAsync(XunitCancellationToken) is { } frame)
        {
            using (frame)
            {
                Assert.Equal(xmp, frame.Metadata.XmpProfile?.Data.ToArray());
                count++;
            }
        }

        Assert.Equal(3, count);
    }

    /// <summary>
    /// Image sequences written by FFmpeg's <c>avif</c> muxer from the output of libaom and of SVT-AV1: inter frames, frames
    /// that are decoded without being shown and frames shown again (random access), which avifenc never writes.
    /// </summary>
    [Theory]
    [InlineData("libaom-av1", "yuv420p", 0)]
    [InlineData("libaom-av1", "yuv444p10le", 3)]
    [InlineData("libsvtav1", "yuv420p", 2)]
    [InlineData("libsvtav1", "yuv420p10le", 0)]
    public async Task SequencesMuxedByFFmpegDecodeAsAvifDec(string encoder, string pixelFormat, int loop)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var name = $"ffmpeg-{encoder}-{pixelFormat}-loop{loop.ToString(CultureInfo.InvariantCulture)}";
        var path = ArtifactPath(name + ".avif");
        const int FrameCount = 9;
        string[] encoderOptions = encoder == "libaom-av1"
            ? ["-c:v", "libaom-av1", "-cpu-used", "5", "-crf", "32", "-row-mt", "0"]
            : ["-c:v", "libsvtav1", "-preset", "7", "-crf", "34"];
        var result = await ffmpeg.RunAsync(
            ["-y", "-filter_threads", "1", "-f", "lavfi", "-i", "testsrc2=size=96x64:rate=12,noise=alls=12:allf=t:all_seed=5", "-frames:v", FrameCount.ToString(CultureInfo.InvariantCulture),
                "-sws_flags", "accurate_rnd+full_chroma_int+bitexact", "-pix_fmt", pixelFormat, .. encoderOptions, "-threads", "1", "-flags", "+bitexact", "-loop", loop.ToString(CultureInfo.InvariantCulture), "-f", "avif", path],
            XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ffmpeg {ffmpeg.Version} failed to write {name} (exit code {result.ExitCode}): {result.StandardError}");

        // The case exists for the frames that are not shown when they are decoded: the headers must have some
        var trace = await ffmpeg.TraceHeadersAsync(path, format: null, XunitCancellationToken);
        var hidden = CountOccurrences(trace, "show_frame", "0");
        var shownAgain = CountOccurrences(trace, "show_existing_frame", "1");
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {hidden} hidden frame(s), {shownAgain} frame(s) shown again"));
        Assert.True(hidden > 0, $"{name}: the stream has no hidden frame.");

        // FFmpeg's "-loop" is a number of plays (0: forever); avifdec reports the repetitions after the first play
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal(FrameCount, info.FrameCount);
        Assert.True(info.HasRepetitionCount, $"{name}: avifdec reports no repetition count.");
        Assert.Equal(loop == 0 ? (int?)null : loop - 1, info.RepetitionCount);
        var bound = pixelFormat switch
        {
            "yuv420p" => ColorBound.Subsampled8,
            "yuv420p10le" => ColorBound.Subsampled10,
            _ => ColorBound.Matrix16,
        };

        // FFmpeg describes the color of the primary item only (its "colr" box holds unspecified code points): the track has no
        // "colr" box and its sequence header no color description, so the sequence signals none. avifdec prints unspecified
        // code points (2) for it and converts with BT.601 in the range of the bitstream, as the library does
        Assert.Equal(0, CountOccurrences(trace, "color_description_present_flag", "1"));
        Assert.Equal((2, 2, 2, false), (info.ColorPrimaries, info.TransferCharacteristics, info.MatrixCoefficients, info.FullRange));
        await AssertSequenceAsync(avif, path, info, loop == 0 ? null : loop, bound, name, signalsColorDescription: false);
    }

    /// <summary>
    /// Layered images, one source per layer (<c>avifenc --layered</c>): the image is the last layer, as avifdec decodes it,
    /// and a file that selects a layer with the <c>lsel</c> property (which avifenc does not write: it is added to the
    /// item of the file) shows that layer, as <c>avifdec --progressive --index</c> decodes it. A layer that is smaller
    /// than the image cannot be selected without changing the size of the item, so it is only decoded on the way.
    /// </summary>
    [Theory]
    [InlineData("layered-2-quality", 2, 0)]
    [InlineData("layered-3-contents", 3, 0)]
    [InlineData("layered-4-contents", 4, 0)]
    [InlineData("layered-2-first-half-size", 2, 2)]
    [InlineData("layered-3-first-quarter-size", 3, 4)]
    public async Task LayeredImagesDecodeAsAvifDec(string name, int layers, int firstLayerDivisor)
    {
        var avif = await RequiredTools.LibAvifAsync();
        const int Width = 64;
        const int Height = 48;
        var sameContent = name.Contains("quality", StringComparison.Ordinal) || firstLayerDivisor is not 0;
        var arguments = new List<string> { "--layered", "--yuv", "444" };
        for (var layer = 0; layer < layers; layer++)
        {
            // An option with the ":u" suffix applies to the inputs that follow it
            var source = CreateSource(SourceKind.Rgb8, Width, Height, seed: StableSeed(name), frame: sameContent ? 0 : layer);
            var suffix = layer is 0 ? "" : ":u";
            arguments.AddRange(["--qcolor" + suffix, (30 + (60 * layer / Math.Max(1, layers - 1))).ToString(CultureInfo.InvariantCulture)]);
            if (firstLayerDivisor is not 0)
                arguments.AddRange(["--scaling-mode" + suffix, layer is 0 ? string.Create(CultureInfo.InvariantCulture, $"1/{firstLayerDivisor}") : "1/1"]);

            arguments.Add(await SaveSourceAsync(string.Create(CultureInfo.InvariantCulture, $"{name}-layer-{layer}"), source));
        }

        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync(arguments, path, XunitCancellationToken);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((Width, Height, 1), (info.Width, info.Height, info.FrameCount));

        // The image: the last layer
        var reference = await avif.DecodeAsync(path, XunitCancellationToken);
        using (var image = Image.Load(path))
        {
            Assert.Equal((Width, Height, 1), (image.Width, image.Height, image.Frames.Count));
            Assert.Null(image.Animation);
            AssertPixels(reference.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Matrix8, $"{name}: library vs avifdec {avif.Version}");
        }

        var last = await avif.DecodeLayerAsync(path, layers - 1, XunitCancellationToken);
        AssertPixels(reference.Pixels, last.Pixels, ColorBound.Exact, $"{name}: the last layer of avifdec is its image");
        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var identified = Image.Identify(path, new ImageIdentifyOptions { Mode = mode });
            Assert.Equal(new Size(Width, Height), identified.Size);
            Assert.Equal<int?>(1, identified.FrameCount);
        }

        // Each layer, selected by the file
        var bytes = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        for (var layer = 0; layer < layers; layer++)
        {
            var builder = AvifFileBuilder.From(bytes);
            var item = builder.Items.Single(candidate => candidate.Type == "av01");
            item.Associations.Add((builder.AddProperty(AvifFileBuilder.Box("lsel", [0, (byte)layer])), true));
            var selected = builder.Build();
            if (layer is 0 && firstLayerDivisor is not 0)
            {
                // The layer does not have the size that the item declares
                Assert.Throws<InvalidImageContentException>(() => Image.Load(selected.AsSpan()).Dispose());
                continue;
            }

            var layerReference = await avif.DecodeLayerAsync(path, layer, XunitCancellationToken);
            using var image = Image.Load(selected.AsSpan());
            Assert.Equal((Width, Height, 1), (image.Width, image.Height, image.Frames.Count));
            AssertPixels(layerReference.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Matrix8, string.Create(CultureInfo.InvariantCulture, $"{name}: layer {layer} vs avifdec {avif.Version}"));
        }

        // The layers are not copies of the image, or the comparisons above prove nothing
        if (layers > 1)
        {
            var first = await avif.DecodeLayerAsync(path, 0, XunitCancellationToken);
            Assert.False(first.Pixels.Span.SequenceEqual(reference.Pixels.Span), $"{name}: the first layer is the image.");
        }
    }

    /// <summary>
    /// Premultiplied alpha, as avifenc writes it (<c>--premultiply</c>): the library divides the colors by the alpha of their
    /// pixel once they are converted to the depth of the output, with an integer division rounded to nearest; avifdec
    /// divides the 16-bit samples in floating point. Alpha is exact. The division multiplies what separates the two
    /// decoders by <c>maximum / alpha</c>, so the pixels whose alpha is below a sixteenth of the range are only compared for
    /// their alpha, and the bounds are the ones of the conversion times at most 16:
    /// <list type="bullet">
    /// <item><description>lossless 8-bit planes: the colors are exact before the division, and an 8-bit result is at most half an 8-bit step (128.5 units) from the 16-bit result of avifdec. Measured: 129.</description></item>
    /// <item><description>lossless 10-bit planes: both sides produce 16-bit samples, and the single-precision scaling of avifdec is one unit off for some values (<see cref="ColorBound.Scaled16"/>). Measured: 7.</description></item>
    /// <item><description>lossy 4:4:4 8-bit planes: the library rounds the converted color to 8 bits before the division, at most half an 8-bit step from the color avifdec divides (<see cref="ColorBound.Matrix8"/>), then both results are rounded. Measured: 1307 (5.1/255).</description></item>
    /// </list>
    /// </summary>
    [Theory]
    [InlineData("premultiplied-lossless-8", false, new[] { "--lossless", "--premultiply" }, 130)]
    [InlineData("premultiplied-lossless-10", true, new[] { "--lossless", "--depth", "10", "--premultiply" }, 16)]
    [InlineData("premultiplied-lossy-444-8", false, new[] { "--qcolor", "60", "--qalpha", "60", "--yuv", "444", "--premultiply" }, (16 * 129) + 130)]
    public async Task PremultipliedAlphaIsDividedAsAvifDecDoes(string name, bool deep, string[] options, int maxColorError)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var sourcePath = await SaveSourceAsync(name, CreateSource(deep ? SourceKind.Rgba16 : SourceKind.Rgba8, 64, 64, seed: 31));
        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync([.. options, sourcePath], path, XunitCancellationToken);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal("Premultiplied", info.Alpha);
        Assert.True(ReferenceAvif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken)).IsPremultiplied);

        var reference = await avif.DecodeAsync(path, XunitCancellationToken);
        using var image = Image.Load(path);
        Assert.Equal(info.BitDepth > 8 ? PixelFormat.Rgba64 : PixelFormat.Rgba32, image.PixelFormat);
        var actual = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));
        Assert.Equal((reference.Width, reference.Height), (actual.Width, actual.Height));
        var max = 0;
        var compared = 0;
        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var alpha = actual.GetSample(x, y, 3);
                Assert.Equal(reference.Pixels.GetSample(x, y, 3), alpha, message: $"{name}: alpha of pixel ({x}, {y}).");
                if (alpha < 4096)
                    continue;

                compared++;
                for (var channel = 0; channel < 3; channel++)
                {
                    max = Math.Max(max, Math.Abs(reference.Pixels.GetSample(x, y, channel) - actual.GetSample(x, y, channel)));
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {compared} pixels compared, max color difference {max} ({max / 257.0:F3}/255), bound {maxColorError}"));
        Assert.True(compared > 1000, $"{name}: only {compared} pixels have enough alpha to be compared.");
        Assert.True(max <= maxColorError, $"{name}: the largest color difference is {max} ({max / 257.0:F3}/255), over the bound of {maxColorError}.");
    }

    /// <summary>
    /// The matrix coefficients that go through the transfer characteristics (BT.2020 constant luminance, chromaticity-derived
    /// constant luminance, ICtCp): avifenc labels the planes of a Y4M source with them without converting anything and
    /// avifdec reads the description of the file, but libavif has no conversion for them. The library reads the file and
    /// converts it; its pixels are compared with the exact conversion of the harness in the conformance tests.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(14)]
    public async Task TheMatricesThatGoThroughTheTransferFunctionAreReadFromAFileOfAvifEnc(int matrix)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var path = await EncodeLabeledPlanesAsync(avif, matrix);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((64, 64, 9, 16, matrix), (info.Width, info.Height, info.ColorPrimaries, info.TransferCharacteristics, info.MatrixCoefficients));

        using var image = Image.Load(path);
        Assert.Equal((64, 64), (image.Width, image.Height));
        Assert.Equal(new CicpColorDescription(9, 16, (byte)matrix, fullRange: info.FullRange), image.Metadata.Cicp);
        Assert.Equal(image.Metadata.Cicp, Image.Identify(path).Metadata.Cicp);

        // The planes of the test pattern are not the conversion of colors: what is asserted is that they are converted,
        // into colors that are not all the same
        var pixels = ImageSnapshots.CaptureFrame(image.Frames[0]);
        Assert.NotEqual((pixels.GetSample(0, 0, 0), pixels.GetSample(0, 0, 1), pixels.GetSample(0, 0, 2)), (pixels.GetSample(40, 40, 0), pixels.GetSample(40, 40, 1), pixels.GetSample(40, 40, 2)));
    }

    /// <summary>
    /// The chromaticity-derived matrix coefficients (12): the weights of the matrix are the luminance of the BT.2020
    /// primaries that the file declares. avifdec derives them from the primaries too, and converts the same planes
    /// within the bound of subsampled 8-bit content (<see cref="ColorBound.Subsampled8"/>).
    /// </summary>
    [Fact]
    public async Task TheChromaticityDerivedMatrixConvertsAsAvifDec()
    {
        var avif = await RequiredTools.LibAvifAsync();
        var path = await EncodeLabeledPlanesAsync(avif, 12);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((9, 16, 12), (info.ColorPrimaries, info.TransferCharacteristics, info.MatrixCoefficients));
        var reference = await avif.DecodeAsync(path, XunitCancellationToken);
        using var image = Image.Load(path);
        Assert.Equal(new CicpColorDescription(9, 16, 12, fullRange: info.FullRange), image.Metadata.Cicp);
        var measured = AssertPixels(reference.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Subsampled8, $"chromaticity-derived matrix: library vs avifdec {avif.Version}");
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"chromaticity-derived matrix: max color difference {measured.Max} ({measured.Max / 257.0:F3}/255)"));
    }

    /// <summary>
    /// The SMPTE ST 2085 matrix coefficients (11): avifenc labels the planes and avifdec reads the file, but libavif has no
    /// conversion for them, so the pixels of the library are compared with the exact conversion of the harness (within
    /// one unit), on the planes that the reference decoders agree on in the conformance tests.
    /// </summary>
    [Fact]
    public async Task TheSmpte2085MatrixIsReadFromAFileOfAvifEnc()
    {
        var avif = await RequiredTools.LibAvifAsync();
        var path = await EncodeLabeledPlanesAsync(avif, 11);
        var info = await avif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((64, 64, 11), (info.Width, info.Height, info.MatrixCoefficients));
        using var image = Image.Load(path);
        Assert.Equal((64, 64), (image.Width, image.Height));
        Assert.Equal(new CicpColorDescription(9, 16, 11, fullRange: info.FullRange), image.Metadata.Cicp);
        Assert.Equal(image.Metadata.Cicp, Image.Identify(path).Metadata.Cicp);
    }

    // The planes of a test pattern, which avifenc labels with matrix coefficients without converting them
    private static async Task<FullPath> EncodeLabeledPlanesAsync(LibAvifTool avif, int matrix)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var name = string.Create(CultureInfo.InvariantCulture, $"labeled-matrix-{matrix}");
        var sourcePath = ArtifactPath(name + ".y4m");
        var source = await ffmpeg.RunAsync(["-y", "-filter_threads", "1", "-f", "lavfi", "-i", "testsrc2=size=64x64", "-frames:v", "1", "-pix_fmt", "yuv420p", "-f", "yuv4mpegpipe", sourcePath], XunitCancellationToken);
        Assert.Equal(0, source.ExitCode, message: source.StandardError);
        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync(["--qcolor", "60", "--cicp", string.Create(CultureInfo.InvariantCulture, $"9/16/{matrix}"), sourcePath], path, XunitCancellationToken);
        return path;
    }

    /// <summary>
    /// A grid with transformations, as avifenc writes it: the <c>clap</c>, <c>irot</c> and <c>imir</c> properties are
    /// associated with the grid item and, as copies, with each of its cells. They transform the image once: avifdec applies
    /// them to the assembled grid.
    /// </summary>
    [Theory]
    [InlineData("irot", "1", ExifOrientation.LeftBottom)]
    [InlineData("imir", "0", ExifOrientation.BottomLeft)]
    [InlineData("crop", "10,20,100,90", ExifOrientation.TopLeft)]
    public async Task GridsWithTransformationsDecodeAsAvifDec(string property, string value, ExifOrientation expectedOrientation)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var name = $"grid-2x2-{property}";
        var sourcePath = await SaveSourceAsync(name, CreateSource(SourceKind.Rgb8, 128, 128, seed: 37));
        var path = ArtifactPath(name + ".avif");
        await avif.EncodeAsync(["--lossless", "--grid", "2x2", "--" + property, value, sourcePath], path, XunitCancellationToken);
        var reference = await avif.DecodeAsync(path, XunitCancellationToken);

        using var image = Image.Load(path);
        Assert.Equal(expectedOrientation, image.Metadata.Orientation);
        image.AutoOrient(XunitCancellationToken);
        AssertPixels(reference.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Exact, $"{name}: library after AutoOrient vs avifdec {avif.Version}");
    }

    /// <summary>
    /// Apple ImageIO (CGImageSource, through <c>imageio_frames.swift --binary-alpha</c>) decodes 8-bit AVIF files to 8-bit
    /// samples: the samples that are not matrixed are identical, and 4:4:4 samples are within <see cref="ImageIOMatrixBound"/>.
    /// ImageIO returns the pixels as stored (it does not apply <c>irot</c>, which is added to the still images for that
    /// reason) and every image of a sequence. Subsampled chroma is not compared: ImageIO replicates the chroma samples
    /// (it is within one 8-bit step of <c>avifdec --upsampling nearest</c>), where avifdec and the library interpolate them;
    /// on the 4:2:0 cases the two differ by up to 54 8-bit steps at saturated edges, 3.4 on average.
    /// </summary>
    [Theory]
    [InlineData("lossless-rgb-8")]
    [InlineData("lossless-gray-8")]
    [InlineData("lossy-400-8")]
    [InlineData("grid-2x2-lossless-rgb-8")]
    [InlineData("lossy-444-8")]
    [InlineData("cicp-bt709-limited-444-8")]
    [InlineData("sequence-lossless-rgb-8")]
    [InlineData("sequence-lossy-444-8-once")]
    public async Task EightBitImagesDecodeAsAppleImageIO(string name)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();
        var avif = await RequiredTools.LibAvifAsync();

        // The file is encoded again under another name: the theories of this class run concurrently
        var path = ArtifactPath("imageio-" + name + ".avif");
        int frameCount;
        int maxColorError;
        if (Stills.TryGetValue(name, out var still))
        {
            // avifenc copies the transformations to the cells of a grid, which the library does not accept
            // (GridsWithTransformationsDecodeAsAvifDec): the grid has no rotation
            string[] rotation = still.Options.Contains("--grid", StringComparer.Ordinal) ? [] : ["--irot", "1"];
            var sourcePath = await SaveSourceAsync("imageio-" + name, CreateSource(still.Source, still.Width, still.Height, seed: StableSeed(name)));
            await avif.EncodeAsync([.. still.Options, .. rotation, sourcePath], path, XunitCancellationToken);
            (frameCount, maxColorError) = (1, still.MaxColorError);
        }
        else
        {
            var sequence = Sequences[name];
            List<string> arguments = [.. sequence.Options];
            for (var i = 0; i < sequence.Durations.Length; i++)
            {
                arguments.Add(await SaveSourceAsync($"imageio-{name}-{i}", CreateSource(sequence.Source, sequence.Width, sequence.Height, seed: StableSeed(name), frame: i)));
            }

            await avif.EncodeAsync(arguments, path, XunitCancellationToken);
            (frameCount, maxColorError) = (sequence.Durations.Length, sequence.MaxColorError);
        }

        var result = await ProcessRunner.RunAsync(swift, [script, "--binary-alpha", path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed on {name} (exit code {result.ExitCode}): {result.StandardError}");

        using var image = Image.Load(path);

        // "frames <count> <width> <height> <bits>": straight RGBA, opaque. It is the last line of the error output: the
        // system frameworks can write diagnostics before it (a virtual machine without the hardware scaler does)
        var header = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1].Split(' ');
        Assert.Equal(["frames", frameCount.ToString(CultureInfo.InvariantCulture), image.Width.ToString(CultureInfo.InvariantCulture), image.Height.ToString(CultureInfo.InvariantCulture), "8"], header);
        Assert.Equal(frameCount, image.Frames.Count);
        var frameLength = RawPixelLayout.Rgba8.GetByteLength(image.Width, image.Height);
        Assert.Equal(frameCount * frameLength, result.StandardOutput.Length);
        var bound = maxColorError == ColorBound.Exact ? ColorBound.Exact : ImageIOMatrixBound;
        for (var i = 0; i < frameCount; i++)
        {
            var expected = ToRgba16(RawPixelBuffer.Create(image.Width, image.Height, RawPixelLayout.Rgba8, result.StandardOutput.Slice(i * frameLength, frameLength).Span));
            var measured = AssertPixels(expected, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[i])), bound, $"{name} frame {i}: library vs Apple ImageIO");
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name} frame {i}: max color difference {measured.Max} ({measured.Max / 257.0:F3}/255), mean {measured.Mean:F2}, bound {bound}"));
        }
    }

    /// <summary>
    /// Identity derivations (<c>iden</c> items), which no pinned tool writes or reads: the fixtures are assembled on
    /// committed lossless payloads, and Apple ImageIO, which follows a derivation with its own reader, returns the
    /// pixels as stored: the part of the coded image that the transformations of the derivation and of its input
    /// present, identical to the one the library returns, with the alpha plane that is derived the same way or that
    /// belongs to an untransformed derivation. ImageIO reads no file with nested derivations, shows no alpha plane when
    /// only the input of the derivation has one (the library does not either), and scales an alpha plane without
    /// transformation to the size of a derivation that has a clean aperture, a case that the library rejects
    /// (<c>invalid/avif/iden-alpha-untransformed</c>).
    /// </summary>
    [Theory]
    [InlineData("avif/iden", false)]
    [InlineData("avif/iden-clap-after-irot", false)]
    [InlineData("avif/iden-grid-clap", false)]
    [InlineData("avif/iden-alpha-derived", true)]
    [InlineData("avif/iden-alpha-coded", true)]
    public async Task IdentityDerivationsDecodeAsAppleImageIO(string id, bool hasAlpha)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();
        var path = ArtifactPath("imageio-" + id.Replace('/', '-') + ".avif");
        await File.WriteAllBytesAsync(path, GoldenCorpus.Default.Get(id).ReadInput(), XunitCancellationToken);
        string[] arguments = hasAlpha ? [script, path] : [script, "--binary-alpha", path];
        var result = await ProcessRunner.RunAsync(swift, arguments, XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed on {id} (exit code {result.ExitCode}): {result.StandardError}");

        using var image = Image.Load(path);
        Assert.Equal(hasAlpha, PixelFormats.HasAlpha(image.PixelFormat));
        var header = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1].Split(' ');
        Assert.Equal(["frames", "1", image.Width.ToString(CultureInfo.InvariantCulture), image.Height.ToString(CultureInfo.InvariantCulture), "8"], header);
        var expected = ToRgba16(RawPixelBuffer.Create(image.Width, image.Height, RawPixelLayout.Rgba8, result.StandardOutput.Span));
        var actual = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));
        if (hasAlpha)
        {
            // The alpha plane is the one of the same samples, and the colors that it shows are identical (ImageIO
            // returns the colors of the transparent pixels as black)
            for (var i = 0; i < image.Width * image.Height; i++)
            {
                var (x, y) = (i % image.Width, i / image.Width);
                Assert.Equal(expected.GetSample(x, y, 3), actual.GetSample(x, y, 3));
                if (actual.GetSample(x, y, 3) == 65535)
                    Assert.Equal((expected.GetSample(x, y, 0), expected.GetSample(x, y, 1), expected.GetSample(x, y, 2)), (actual.GetSample(x, y, 0), actual.GetSample(x, y, 1), actual.GetSample(x, y, 2)));
            }
        }
        else
        {
            AssertPixels(expected, actual, ColorBound.Exact, $"{id}: library vs Apple ImageIO");
        }
    }

    /// <summary>
    /// The sequence through <c>Load</c>, <c>Identify</c> and <c>OpenReader</c>: frame count, exact durations, play count, and
    /// every frame against avifdec.
    /// </summary>
    private static async Task AssertSequenceAsync(LibAvifTool avif, FullPath path, AvifDecInfo info, int? totalPlays, int maxColorError, string name, bool signalsColorDescription = true)
    {
        Assert.Equal(info.FrameCount, info.Frames.Count);
        var references = await avif.DecodeAllFramesAsync(path, XunitCancellationToken);
        Assert.Equal(info.FrameCount, references.Count);
        var durations = info.Frames.Select(frame => new FrameDuration(frame.Duration, info.TimeScale)).ToList();
        var pixelFormat = ExpectedPixelFormat(info.BitDepth, info);

        using var image = Image.Load(path);
        Assert.Equal(ImageFormat.Avif, image.Metadata.SourceFormat);
        Assert.Equal(pixelFormat, image.PixelFormat);
        Assert.Equal(new Size(info.Width, info.Height), image.Size);
        Assert.Null(image.PosterFrame);
        Assert.Equal(info.FrameCount, image.Frames.Count);
        Assert.Equal(durations, image.Frames.Select(frame => frame.Metadata.Duration));
        Assert.NotNull(image.Animation);
        Assert.Equal(totalPlays, image.Animation.TotalPlays);
        if (signalsColorDescription)
        {
            AssertColorDescription(info, expected: null, image.Metadata.Cicp, name);
        }
        else
        {
            Assert.Null(image.Metadata.Cicp);
        }

        var max = 0;
        for (var i = 0; i < references.Count; i++)
        {
            Assert.Equal((info.Width, info.Height), (references[i].Width, references[i].Height));
            max = Math.Max(max, AssertPixels(references[i].Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[i])), maxColorError, $"{name} frame {i}: library vs avifdec {avif.Version}").Max);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {info.FrameCount} frames, {info.Format} {info.BitDepth}-bit matrix {info.MatrixCoefficients} {(info.FullRange ? "full" : "limited")}{(info.HasAlpha ? " alpha" : "")}: max color difference {max} ({max / 257.0:F3}/255), bound {maxColorError}"));

        // A header identification does not count the frames; a full scan does
        var header = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.Header });
        var scanned = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        foreach (var identified in new[] { header, scanned })
        {
            Assert.Equal(ImageFormat.Avif, identified.Format);
            Assert.Equal(new Size(info.Width, info.Height), identified.Size);
            Assert.Equal(pixelFormat, identified.PixelFormat);
            Assert.Equal<bool?>(info.HasAlpha, identified.MayHaveTransparency);
            Assert.Equal(image.Metadata.Cicp, identified.Metadata.Cicp);
        }

        Assert.Equal<int?>(info.FrameCount, scanned.FrameCount);
        Assert.Equal<bool?>(true, scanned.IsAnimated);
        Assert.Equal(totalPlays, scanned.Animation?.TotalPlays);

        // The streaming reader, on a stream that cannot seek: one frame at a time
        await using var stream = new ForwardOnlyStream(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        if (info.BitDepth == 8)
        {
            await AssertReaderAsync<Rgba32>(stream, references, durations, totalPlays, maxColorError, name);
        }
        else
        {
            await AssertReaderAsync<Rgba64>(stream, references, durations, totalPlays, maxColorError, name);
        }
    }

    private static async Task AssertReaderAsync<TPixel>(Stream stream, IReadOnlyList<AvifDecImage> references, List<FrameDuration> durations, int? totalPlays, int maxColorError, string name)
        where TPixel : unmanaged
    {
        await using var reader = await Image.OpenReaderAsync<TPixel>(stream, new ImageReaderOptions { LeaveOpen = true }, XunitCancellationToken);
        Assert.Equal(ImageFormat.Avif, reader.Info.Format);
        Assert.Equal(totalPlays, reader.Info.Animation?.TotalPlays);
        Assert.Null(await reader.ReadPosterFrameAsync(XunitCancellationToken));
        for (var i = 0; i < references.Count; i++)
        {
            using var frame = await reader.ReadFrameAsync(XunitCancellationToken);
            Assert.NotNull(frame, $"{name}: the reader ends before frame {i}.");
            Assert.Equal(durations[i], frame.Frames[0].Metadata.Duration);
            AssertPixels(references[i].Pixels, ToRgba16(ImageSnapshots.CaptureFrame(frame.Frames[0])), maxColorError, $"{name} frame {i}: reader vs avifdec");
        }

        Assert.Null(await reader.ReadFrameAsync(XunitCancellationToken));
        Assert.Equal(references.Count, reader.FramesRead);
    }

    /// <summary>
    /// The color description of the library is the one avifdec reports, and the one that was passed to avifenc when the case
    /// passes one.
    /// </summary>
    private static void AssertColorDescription(AvifDecInfo info, CicpColorDescription? expected, CicpColorDescription? actual, string name)
    {
        var reported = new CicpColorDescription((byte)info.ColorPrimaries, (byte)info.TransferCharacteristics, (byte)info.MatrixCoefficients, info.FullRange);
        if (expected is not null)
        {
            Assert.Equal(expected, reported, message: $"{name}: avifdec does not report the color description passed to avifenc.");
        }

        Assert.Equal(reported, actual, message: $"{name}: color description (primaries/transfer/matrix/full range).");
    }

    /// <summary>The pixel format the documentation gives to a file: 8-bit files load at 8 bits, deeper files at 16; gray only without alpha.</summary>
    private static PixelFormat ExpectedPixelFormat(int bitDepth, AvifDecInfo info) => (bitDepth, info.Format == "YUV400", info.HasAlpha) switch
    {
        (8, true, false) => PixelFormat.Gray8,
        (8, false, false) => PixelFormat.Rgb24,
        (8, _, true) => PixelFormat.Rgba32,
        (_, true, false) => PixelFormat.Gray16,
        _ => PixelFormat.Rgba64,
    };

    /// <summary>
    /// Compares straight RGBA 16-bit buffers: alpha exactly, each color sample within <paramref name="maxColorError"/>.
    /// </summary>
    /// <returns>The largest and the mean absolute difference of the color samples.</returns>
    private static (int Max, double Mean) AssertPixels(RawPixelBuffer expected, RawPixelBuffer actual, int maxColorError, string context)
    {
        Assert.Equal(RawPixelLayout.Rgba16Le, expected.Layout);
        Assert.Equal(RawPixelLayout.Rgba16Le, actual.Layout);
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height), message: $"{context}: size.");
        var measured = MeasurePixels(expected, actual);
        if (measured.FirstAlphaMismatch is var (alphaX, alphaY))
            Assert.Fail($"{context}: alpha of pixel ({alphaX}, {alphaY}) is {actual.GetSample(alphaX, alphaY, 3)}, expected {expected.GetSample(alphaX, alphaY, 3)}.");

        if (measured.Max > maxColorError)
        {
            var (x, y, channel) = measured.Worst;
            Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{context}: the largest color difference is {measured.Max} ({measured.Max / 257.0:F3}/255), over the bound of {maxColorError} ({maxColorError / 257.0:F3}/255): pixel ({x}, {y}) channel {"RGB"[channel]} is {actual.GetSample(x, y, channel)}, expected {expected.GetSample(x, y, channel)} (16-bit scale)."));
        }

        return (measured.Max, measured.Mean);
    }

    /// <summary>Measures the differences of two straight RGBA 16-bit buffers of the same size.</summary>
    /// <returns>The largest and the mean absolute difference of the color samples, the first sample with the largest difference, and the first pixel whose alpha differs.</returns>
    private static (int Max, double Mean, (int X, int Y, int Channel) Worst, (int X, int Y)? FirstAlphaMismatch) MeasurePixels(RawPixelBuffer expected, RawPixelBuffer actual)
    {
        var max = 0;
        long sum = 0;
        var worst = (0, 0, 0);
        (int X, int Y)? alphaMismatch = null;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                if (alphaMismatch is null && expected.GetSample(x, y, 3) != actual.GetSample(x, y, 3))
                {
                    alphaMismatch = (x, y);
                }

                for (var channel = 0; channel < 3; channel++)
                {
                    var error = Math.Abs(expected.GetSample(x, y, channel) - actual.GetSample(x, y, channel));
                    sum += error;
                    if (error > max)
                    {
                        max = error;
                        worst = (x, y, channel);
                    }
                }
            }
        }

        return (max, sum / (double)(expected.Width * expected.Height * 3), worst, alphaMismatch);
    }

    /// <summary>Widens a captured frame to straight RGBA, 16 bits per sample: 8-bit samples are multiplied by 257, gray is replicated, a missing alpha is opaque.</summary>
    private static RawPixelBuffer ToRgba16(RawPixelBuffer source)
    {
        var layout = source.Layout;
        var result = new byte[RawPixelLayout.Rgba16Le.GetByteLength(source.Width, source.Height)];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var target = result.AsSpan(((y * source.Width) + x) * 8, 8);
                for (var channel = 0; channel < 4; channel++)
                {
                    int value;
                    if (channel == 3)
                    {
                        value = layout.HasAlpha ? Widen(source.GetSample(x, y, layout.AlphaChannel)) : ushort.MaxValue;
                    }
                    else
                    {
                        value = Widen(source.GetSample(x, y, layout.IsColor ? channel : 0));
                    }

                    BinaryPrimitives.WriteUInt16LittleEndian(target[(channel * 2)..], (ushort)value);
                }
            }
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba16Le, result);

        int Widen(int sample) => layout.BytesPerSample == 1 ? sample * 257 : sample;
    }

    /// <summary>
    /// A deterministic image: smooth gradients (chroma that subsampling changes), seeded noise, saturated rectangles with
    /// sharp edges, and, with alpha, opaque, translucent and fully transparent areas whose colors are kept. A frame number
    /// moves the content, so that the frames of a sequence predict each other.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static RawPixelBuffer CreateSource(SourceKind kind, int width, int height, int seed, int frame = 0)
    {
        var layout = kind switch
        {
            SourceKind.Rgb8 => RawPixelLayout.Rgb8,
            SourceKind.Rgba8 => RawPixelLayout.Rgba8,
            SourceKind.Gray8 => RawPixelLayout.Gray8,
            SourceKind.Gray16 => RawPixelLayout.Gray16Le,
            _ => RawPixelLayout.Rgba16Le,
        };

        var max = layout.MaxSampleValue;
        var random = new Random(seed);
        var bytes = new byte[layout.GetByteLength(width, height)];
        Span<int> pixel = stackalloc int[4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var u = (x + (3 * frame)) % Math.Max(width, 1);
                var v = (y + frame) % Math.Max(height, 1);
                pixel[0] = (int)((long)u * max / Math.Max(width - 1, 1));
                pixel[1] = (int)((long)v * max / Math.Max(height - 1, 1));
                pixel[2] = (int)((long)(u + v) * max / Math.Max(width + height - 2, 1));
                pixel[3] = max;
                if (u >= width / 4 && u < width / 2 && v >= height / 4 && v < (3 * height) / 4)
                {
                    // A saturated rectangle
                    (pixel[0], pixel[1], pixel[2]) = (max, v % 2 == 0 ? 0 : max, 0);
                }
                else if (u >= (5 * width) / 8 && v < height / 3)
                {
                    (pixel[0], pixel[1], pixel[2]) = (0, 0, max);
                }

                for (var channel = 0; channel < 3; channel++)
                {
                    pixel[channel] = Math.Clamp(pixel[channel] + random.Next(-max / 24, (max / 24) + 1), 0, max);
                }

                if (layout.HasAlpha && kind is SourceKind.Rgba8 or SourceKind.Rgba16)
                {
                    pixel[3] = ((u + (2 * v)) % 9) switch
                    {
                        0 => 0,
                        1 => max / 3,
                        2 => (int)((long)u * max / Math.Max(width - 1, 1)),
                        _ => max,
                    };
                }

                var target = bytes.AsSpan(((y * width) + x) * layout.BytesPerPixel);
                for (var channel = 0; channel < layout.ChannelCount; channel++)
                {
                    // Gray: the first component
                    var value = pixel[layout.IsColor || channel == 0 ? channel : 3];
                    if (layout.BytesPerSample == 1)
                    {
                        target[channel] = (byte)value;
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(target[(channel * 2)..], (ushort)value);
                    }
                }
            }
        }

        return RawPixelBuffer.Create(width, height, layout, bytes);
    }

    /// <summary>Writes a source as a PNG file with the library; avifenc reads it with libpng.</summary>
    private static async Task<FullPath> SaveSourceAsync(string name, RawPixelBuffer pixels)
    {
        var format = pixels.Layout.Name switch
        {
            "rgb8" => PixelFormat.Rgb24,
            "rgba8" => PixelFormat.Rgba32,
            "gray8" => PixelFormat.Gray8,
            "gray16le" => PixelFormat.Gray16,
            _ => PixelFormat.Rgba64,
        };

        using var image = EncoderSources.CreateImage(pixels, format);
        var path = ArtifactPath(name + ".png");
        await image.SaveAsync(path, new PngEncoder(), XunitCancellationToken);
        return path;
    }

    private static FullPath ArtifactPath(string fileName) => InteropSettings.GetArtifactsDirectory(nameof(AvifDecoderInteropTests)) / fileName;

    /// <summary>A seed derived from the name of a case, identical on every run and platform.</summary>
    private static int StableSeed(string name)
    {
        var hash = 17;
        foreach (var c in name)
        {
            hash = unchecked((hash * 31) + c);
        }

        return hash & 0x7FFFFFFF;
    }

    /// <summary>Counts the lines of a <c>trace_headers</c> log that give <paramref name="value"/> to a syntax element.</summary>
    private static int CountOccurrences(string trace, string element, string value)
    {
        var count = 0;
        foreach (var line in trace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            // "[trace_headers @ 0x...] 27          show_frame                                                  1 = 1"
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[^1] == value && parts[^2] == "=" && parts.Contains(element, StringComparer.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>A read-only stream that cannot seek and returns its content in small pieces.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, 997), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
