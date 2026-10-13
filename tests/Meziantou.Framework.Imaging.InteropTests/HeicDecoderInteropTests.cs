using System.Globalization;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// HEIC decoding against independent references: deterministic sources are written as PNG by the library, encoded by
/// <c>heif-enc</c> (libheif with x265) over a matrix of options, and decoded by the library. Every file is compared with two
/// references:
/// <list type="bullet">
/// <item><description>
/// <b>The planes.</b> <see cref="ReferenceHeif"/> extracts the HEVC stream of each coded item or track, two reference
/// decoders (FFmpeg and dec265, or HM when they disagree) must decode it to the same planes, and
/// <see cref="ReferenceYuvConverter"/> converts the planes to pixels by the rules the library documents: the color
/// description of the <c>nclx</c> box or else of the bitstream, chroma upsampled at the position the bitstream declares, the
/// clean aperture applied to the upsampled picture. The library is within one unit of it for matrixed samples
/// (<see cref="ColorBound.Oracle(int)"/>), and equal to it for the others and for alpha.
/// </description></item>
/// <item><description>
/// <b>libheif.</b> <c>heif-dec -C bilinear</c> decodes the file to a PNG file, read back by <see cref="HeifDecImage"/>
/// without the library. heif-dec takes the chroma samples as centered whatever the stream declares, applies the clean
/// aperture to the planes before it upsamples them, and replicates the chroma samples of the images of a sequence: the
/// library is compared with it directly when no chroma upsampling is involved, or when the stream of a still image
/// declares centered chroma samples and the image has no clean aperture (<see cref="HeifDecComparison"/>); otherwise
/// heif-dec is compared with the conversion of the same planes by its own rules, which shows that the two decoders only
/// differ by these rules.
/// </description></item>
/// </list>
/// What libheif does not write is assembled by the test harness: the layouts of the container
/// (<see cref="HeifFileBuilder"/> lays the files of heif-enc out differently), and the streams without color description
/// or with an alpha plane of another range, which the x265 command-line tool encodes.
/// <c>irot</c> and <c>imir</c> are reported as <see cref="ImageMetadata.Orientation"/> and never applied: heif-dec applies
/// them, so its output is compared with the pixels of the library after <c>AutoOrient()</c>. Image sequences are compared
/// frame by frame, with exact durations and play counts, through <c>Image.Load</c>, <c>Image.Identify</c> and
/// <c>Image.OpenReader</c>. On macOS, files written by <c>sips</c> are compared with the planes and with Apple ImageIO.
/// The theories of this class run concurrently: every case writes files of its own name to the artifacts directory.
/// </summary>
public sealed partial class HeicDecoderInteropTests
{
    /// <summary>
    /// Whether the HEVC decoder of the library decodes P and B slices. While it does not, loading an image sequence that
    /// uses inter prediction must throw <see cref="UnsupportedImageFeatureException"/>; set it to <see langword="true"/> when
    /// inter prediction is implemented, and the sequence cases compare every image of these sequences instead.
    /// </summary>
    private const bool InterPredictionIsDecoded = true;

    private static readonly Dictionary<string, StillCase> Stills = CreateStillCases().ToDictionary(item => item.Name, StringComparer.Ordinal);
    private static readonly Dictionary<string, SequenceCase> Sequences = CreateSequenceCases().ToDictionary(item => item.Name, StringComparer.Ordinal);

    public static TheoryData<string> StillCases => [.. Stills.Keys];

    public static TheoryData<string> SequenceCases => [.. Sequences.Keys];

    /// <summary>
    /// The largest difference accepted between two color samples, in units of the 16-bit scale (257 units are one 8-bit
    /// step). Alpha never has a tolerance. The HEVC planes are identical on every side (the differential and conformance
    /// tests cover them): a difference comes from the conversion to RGB only. The measure of every case is written to the
    /// test output; the measured values below are those of libheif 1.23.6.
    /// </summary>
    internal static class ColorBound
    {
        /// <summary>Samples that are not matrixed (identity matrix, monochrome) and alpha: both sides scale the same sample.</summary>
        public const int Exact = 0;

        /// <summary>
        /// The library against the oracle (<see cref="ReferenceYuvConverter"/>, exact rational arithmetic) on the same planes,
        /// for the samples that a matrix or the expansion of the limited range computes: one unit of the pixels of the
        /// library (an 8-bit step when every plane has 8-bit samples, one unit of the 16-bit scale otherwise), because the
        /// library computes with coefficients rounded to 16 fractional bits. Measured: 1 unit, for every sample format.
        /// </summary>
        public static int Oracle(int depth) => depth == 8 ? 257 : 1;

        /// <summary>
        /// The bound of the library against the oracle for an image: <see cref="Exact"/> for the samples that are only widened
        /// (identity matrix or monochrome, in full range), <see cref="Oracle(int)"/> for the samples that a matrix or the
        /// expansion of the limited range computes.
        /// </summary>
        public static int Oracle(ReferencePlanes color, ReferencePlanes? alpha, int matrix, bool fullRange) => (color.IsMonochrome || matrix == 0) && fullRange ? Exact : Oracle(Math.Max(color.BitDepth, alpha?.BitDepth ?? 8));

        /// <summary>One step of a sample of <paramref name="depth"/> bits on the 16-bit scale, rounded up: 257, 65 or 17.</summary>
        public static int Step(int depth) => (65535 + (1 << depth) - 2) / ((1 << depth) - 1);

        /// <summary>
        /// Matrixed samples without chroma upsampling (4:4:4), against heif-dec: both sides convert the same samples and round
        /// the result, the library once to its output (8 bits for 8-bit planes, 16 bits otherwise), heif-dec to the bit depth
        /// of the image, with a fixed-point matrix. Two roundings of the same value to the same grid differ by at most one
        /// step of that grid. Measured: one step at 8 bits (257), half a step at 10 bits (32) and at 12 bits (9).
        /// <para>
        /// Above 8 bits, libheif truncates the samples it converts from the limited range instead of rounding them (its
        /// pixels are half a step below those of the library on average): a second step. Measured: 98 at 10 bits (1.53
        /// steps), 26 at 12 bits (1.63 steps).
        /// </para>
        /// </summary>
        public static int Matrix(int depth, bool fullRange) => depth > 8 && !fullRange ? 2 * Step(depth) : Step(depth);

        /// <summary>
        /// Matrixed samples with subsampled chroma, the library against heif-dec when both upsample the chroma samples at the
        /// same position: on top of <see cref="Matrix"/>, the two bilinear filters may round the upsampled chroma differently
        /// (the library rounds it to the precision of the planes, halves up), by half a chroma step at most, multiplied by
        /// the largest chroma coefficient (2.142 for the blue of BT.2020 in limited range, 1.772 for BT.601 in full range):
        /// one more step. Measured: the values of <see cref="Matrix"/>, one step at 8 bits included; the second step is not
        /// reached by the pictures of the suite.
        /// </summary>
        public static int Subsampled(int depth, bool fullRange) => Step(depth) + Matrix(depth, fullRange);
    }

    /// <summary>How the pixels of the library compare with those of <c>heif-dec -C bilinear</c> (after <c>AutoOrient()</c>).</summary>
    public enum HeifDecComparison
    {
        /// <summary>Samples that are not matrixed (identity matrix, monochrome in full range): the same pixels (<see cref="ColorBound.Exact"/>).</summary>
        Exact,

        /// <summary>Matrixed 4:4:4 samples: within <see cref="ColorBound.Matrix"/>.</summary>
        Matrix,

        /// <summary>
        /// 4:2:0 samples that the stream declares as centered (<c>chroma_sample_loc_type</c> 1), in a still image without
        /// clean aperture: both sides upsample the same samples at the same position, within <see cref="ColorBound.Subsampled"/>.
        /// </summary>
        Subsampled,

        /// <summary>
        /// Subsampled chroma that libheif does not convert as the library does: heif-dec ignores
        /// <c>chroma_sample_loc_type</c> (the default position of HEVC is on the left of the luma samples, where the library
        /// puts it; so are the chroma samples of 4:2:2 pictures), applies the clean aperture to the planes before it
        /// upsamples them, where the library upsamples the image then crops it, and replicates the chroma samples of the
        /// images of a sequence (<c>heif-dec -S</c> ignores <c>-C bilinear</c>). The pixels of heif-dec are compared with
        /// the planes of the reference decoders converted by the rules of libheif (<see cref="ChromaModel.LibHeif"/> and
        /// <see cref="ChromaModel.LibHeifSequence"/>), within <see cref="ColorBound.Matrix"/>: the oracle then upsamples
        /// and rounds the chroma samples as libheif does (measured: no difference at all at 8 bits, half a step above; a
        /// step for the images of a sequence). The difference with the library is only written to the test output: a quarter
        /// of a sample of displacement is, at a saturated edge, a quarter of the range of the chroma samples, up to
        /// 0.25 x 255 x 1.772 = 113 8-bit steps for the blue of BT.601 in full range. Measured on the pictures of the suite:
        /// up to 67 steps for still images (2 on average), and up to 147 for the replicated chroma samples of a sequence.
        /// </summary>
        SitingDiffers,

        /// <summary>
        /// A monochrome image whose <c>nclx</c> box declares the limited range: heif-enc stores the samples of the source as
        /// they are (the bitstream declares the full range) and writes the range it was asked for in the box, and heif-dec
        /// returns the samples as they are. The library applies the box, which wins over the bitstream. The pixels of
        /// heif-dec are compared with the planes scaled as full range samples, exactly.
        /// </summary>
        MonochromeRangeIgnored,
    }

    /// <param name="Name">The name of the case and of its files.</param>
    /// <param name="Source">The layout of the PNG source.</param>
    /// <param name="Width">The width of the source.</param>
    /// <param name="Height">The height of the source.</param>
    /// <param name="Options">The heif-enc options.</param>
    /// <param name="Comparison">How the library compares with heif-dec.</param>
    private sealed record StillCase(string Name, SourceKind Source, int Width, int Height, string[] Options, HeifDecComparison Comparison)
    {
        /// <summary>The color description passed to heif-enc, which the library must report.</summary>
        public CicpColorDescription? Cicp { get; init; }

        /// <summary>Whether the source is stored without loss and without conversion: the library must return its samples.</summary>
        public bool SourceIsPreserved { get; init; }
    }

    /// <summary>What heif-dec makes of the alpha plane of an image.</summary>
    internal enum HeifDecAlpha
    {
        /// <summary>heif-dec and the library read the same alpha plane, or none.</summary>
        Same,

        /// <summary>heif-dec does not find the alpha plane that the library reads: it only knows the auxiliary type of HEVC.</summary>
        Missing,

        /// <summary>
        /// heif-dec returns an alpha plane where the library reads none: the tiles of a grid have an auxiliary alpha image
        /// each, which is how heif-enc writes the alpha plane of a grid, and the grid has none. libheif composes the alpha
        /// planes of the tiles; the library reads the auxiliary images of the image it presents, as Apple ImageIO does.
        /// </summary>
        FromTiles,
    }

    /// <param name="Name">The name of the case and of its files.</param>
    /// <param name="Source">The layout of the PNG sources.</param>
    /// <param name="Width">The width of the frames.</param>
    /// <param name="Height">The height of the frames.</param>
    /// <param name="Options">The heif-enc options: quality, sample format, structure of the group of pictures, timing.</param>
    /// <param name="FrameCount">The number of frames.</param>
    /// <param name="Comparison">How the library compares with heif-dec.</param>
    private sealed record SequenceCase(string Name, SourceKind Source, int Width, int Height, string[] Options, int FrameCount, HeifDecComparison Comparison)
    {
        /// <summary>Whether the stream has P or B slices: <see cref="InterPredictionIsDecoded"/> says whether the library decodes it.</summary>
        public bool UsesInterPrediction { get; init; }

        /// <summary>
        /// The durations given to the samples after heif-enc wrote the file, in units of the time scale; <see langword="null"/>
        /// keeps the single duration heif-enc gives to every sample.
        /// </summary>
        public int[]? Durations { get; init; }

        /// <summary>Whether the source is stored without loss and without conversion: the library must return its samples.</summary>
        public bool SourceIsPreserved { get; init; }
    }

    private static IEnumerable<StillCase> CreateStillCases()
    {
        // Lossless: heif-enc codes R, G and B as the planes of an identity matrix image, and gray as a monochrome image
        yield return new("lossless-rgb-8", SourceKind.Rgb8, 80, 64, ["-L"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgba-8", SourceKind.Rgba8, 96, 64, ["-L"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-gray-8", SourceKind.Gray8, 80, 72, ["-L"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        foreach (var depth in new[] { 10, 12 })
        {
            string[] options = ["-L", "-b", depth.ToString(CultureInfo.InvariantCulture)];
            yield return new($"lossless-rgb-{depth}", SourceKind.Rgb16, 80, 64, [.. options, "--no-alpha"], HeifDecComparison.Exact) { SourceIsPreserved = true };
            yield return new($"lossless-rgba-{depth}", SourceKind.Rgba16, 96, 64, options, HeifDecComparison.Exact) { SourceIsPreserved = true };
            yield return new($"lossless-gray-{depth}", SourceKind.Gray16, 80, 72, options, HeifDecComparison.Exact) { SourceIsPreserved = true };
        }

        // Sizes that x265 does not code as they are: heif-enc pads the picture (to an even size, to 64 samples at least) and
        // writes a clean aperture
        yield return new("lossless-rgb-8-65x47", SourceKind.Rgb8, 65, 47, ["-L"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgba-8-31x17", SourceKind.Rgba8, 31, 17, ["-L"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-gray-8-1x1", SourceKind.Gray8, 1, 1, ["-L"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgba-10-33x21", SourceKind.Rgba16, 33, 21, ["-L", "-b", "10"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgb-8-131x77", SourceKind.Rgb8, 131, 77, ["-L"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-gray-12-67x64", SourceKind.Gray16, 67, 64, ["-L", "-b", "12"], HeifDecComparison.Exact) { SourceIsPreserved = true };

        // Transformations of images with a clean aperture, and of a grid: they apply to the image, after the aperture
        yield return new("lossless-rgb-8-65x47-rotate-270", SourceKind.Rgb8, 65, 47, ["-L", "--rotate-cw", "270"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgba-8-31x17-flip", SourceKind.Rgba8, 31, 17, ["-L", "--flip-h"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgb-8-grid-64-rotate-90", SourceKind.Rgb8, 200, 136, ["-L", "--cut-tiles", "64", "--rotate-cw", "90", "--flip-v"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossy-444-8-47x33-rotate-180", SourceKind.Rgb8, 47, 33, ["-q", "70", "-p", "chroma=444", "--rotate-cw", "180"], HeifDecComparison.Matrix);

        // Grids of square tiles; the tiles of the last row and column exceed the image, except in the first case
        yield return new("lossless-rgb-8-grid-64-exact", SourceKind.Rgb8, 128, 192, ["-L", "--cut-tiles", "64"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgb-8-grid-64", SourceKind.Rgb8, 200, 136, ["-L", "--cut-tiles", "64"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-gray-8-grid-128", SourceKind.Gray8, 300, 200, ["-L", "--cut-tiles", "128"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgb-10-grid-64", SourceKind.Rgb16, 130, 70, ["-L", "-b", "10", "--no-alpha", "--cut-tiles", "64"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossless-rgb-8-grid-96-odd", SourceKind.Rgb8, 201, 99, ["-L", "--cut-tiles", "96"], HeifDecComparison.Exact) { SourceIsPreserved = true };

        // A thumbnail, with its own alpha plane: the image is the primary item
        yield return new("lossless-rgba-8-thumbnail", SourceKind.Rgba8, 160, 96, ["-L", "-t", "32"], HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("lossy-420-8-thumbnail", SourceKind.Rgb8, 160, 96, ["-q", "60", "-t", "64"], HeifDecComparison.SitingDiffers);

        // Lossy: every depth and chroma format; the default matrix of heif-enc is BT.601 in full range, and x265 does not
        // signal the position of the chroma samples (the default of HEVC: on the left of the luma samples)
        foreach (var depth in new[] { 8, 10, 12 })
        {
            var (color, gray) = depth > 8 ? (SourceKind.Rgb16, SourceKind.Gray16) : (SourceKind.Rgb8, SourceKind.Gray8);
            string[] depthOptions = depth > 8 ? ["-b", depth.ToString(CultureInfo.InvariantCulture), "--no-alpha"] : [];
            yield return new($"lossy-444-{depth}", color, 96, 64, ["-q", "70", "-p", "chroma=444", .. depthOptions], HeifDecComparison.Matrix);
            yield return new($"lossy-422-{depth}", color, 80, 72, ["-q", "60", "-p", "chroma=422", .. depthOptions], HeifDecComparison.SitingDiffers);
            yield return new($"lossy-420-{depth}", color, 80, 72, ["-q", "50", .. depthOptions], HeifDecComparison.SitingDiffers);
            yield return new($"lossy-400-{depth}", gray, 96, 64, ["-q", "40", .. depthOptions], HeifDecComparison.Exact);
        }

        // The position of the chroma samples of a 4:2:0 stream (x265 "chromaloc"): on the left (0), centered (1), co-sited
        // with the top-left luma sample (2). A 4:2:2 stream has no such choice
        yield return new("lossy-420-8-chromaloc-0", SourceKind.Rgb8, 80, 72, ["-q", "60", "-p", "x265:chromaloc=0"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-420-8-chromaloc-1", SourceKind.Rgb8, 80, 72, ["-q", "60", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);
        yield return new("lossy-420-8-chromaloc-2", SourceKind.Rgb8, 80, 72, ["-q", "60", "-p", "x265:chromaloc=2"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-420-10-chromaloc-1", SourceKind.Rgb16, 80, 72, ["-q", "60", "-b", "10", "--no-alpha", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);
        yield return new("lossy-420-12-chromaloc-1", SourceKind.Rgb16, 80, 72, ["-q", "60", "-b", "12", "--no-alpha", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);
        yield return new("lossy-420-10-chromaloc-2", SourceKind.Rgb16, 80, 72, ["-q", "60", "-b", "10", "--no-alpha", "-p", "x265:chromaloc=2"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-422-8-chromaloc-1", SourceKind.Rgb8, 80, 72, ["-q", "60", "-p", "chroma=422", "-p", "x265:chromaloc=1"], HeifDecComparison.SitingDiffers);

        // Alpha: a monochrome image of its own, coded with the quality of the color image; always compared exactly
        yield return new("lossy-420-8-alpha", SourceKind.Rgba8, 96, 64, ["-q", "60"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-420-8-alpha-chromaloc-1", SourceKind.Rgba8, 96, 64, ["-q", "30", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);
        yield return new("lossy-444-10-alpha", SourceKind.Rgba16, 96, 64, ["-q", "70", "-p", "chroma=444", "-b", "10"], HeifDecComparison.Matrix);
        yield return new("lossy-422-12-alpha", SourceKind.Rgba16, 96, 64, ["-q", "70", "-p", "chroma=422", "-b", "12"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-444-8-alpha-47x33", SourceKind.Rgba8, 47, 33, ["-q", "80", "-p", "chroma=444"], HeifDecComparison.Matrix);
        yield return new("lossy-444-8-no-alpha", SourceKind.Rgba8, 96, 64, ["-q", "70", "-p", "chroma=444", "--no-alpha"], HeifDecComparison.Matrix);

        // Color descriptions: the matrices heif-enc converts to, in full and in limited range. The description is in the
        // "nclx" box and in the bitstream
        foreach (var (matrix, label) in new[] { (1, "bt709"), (5, "bt470bg"), (6, "bt601"), (9, "bt2020ncl") })
        {
            foreach (var fullRange in new[] { true, false })
            {
                var range = fullRange ? "full" : "limited";
                var cicp = new CicpColorDescription(1, 13, (byte)matrix, fullRange);
                string[] options = ["-q", "75", "--matrix_coefficients", matrix.ToString(CultureInfo.InvariantCulture), "--colour_primaries", "1", "--transfer_characteristic", "13", "--full_range_flag", fullRange ? "1" : "0"];
                yield return new($"cicp-{label}-{range}-444-8", SourceKind.Rgb8, 80, 64, [.. options, "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = cicp };
                yield return new($"cicp-{label}-{range}-420-8", SourceKind.Rgb8, 80, 64, [.. options, "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled) { Cicp = cicp };
            }
        }

        yield return new("cicp-bt2020-pq-limited-420-10", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "10", "--no-alpha", "--matrix_coefficients", "9", "--colour_primaries", "9", "--transfer_characteristic", "16", "--full_range_flag", "0", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled) { Cicp = new(9, 16, 9, fullRange: false) };
        yield return new("cicp-bt2020-hlg-full-444-12", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "12", "--no-alpha", "--matrix_coefficients", "9", "--colour_primaries", "9", "--transfer_characteristic", "18", "--full_range_flag", "1", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(9, 18, 9, fullRange: true) };
        yield return new("cicp-bt709-limited-422-10-alpha", SourceKind.Rgba16, 80, 64, ["-q", "75", "-b", "10", "--matrix_coefficients", "1", "--colour_primaries", "1", "--transfer_characteristic", "1", "--full_range_flag", "0", "-p", "chroma=422"], HeifDecComparison.SitingDiffers) { Cicp = new(1, 1, 1, fullRange: false) };
        yield return new("cicp-bt709-limited-444-10", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "10", "--no-alpha", "--matrix_coefficients", "1", "--full_range_flag", "0", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(1, 13, 1, fullRange: false) };
        yield return new("cicp-bt2020-limited-444-10", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "10", "--no-alpha", "--matrix_coefficients", "9", "--full_range_flag", "0", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(1, 13, 9, fullRange: false) };
        yield return new("cicp-bt601-limited-444-12", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "12", "--no-alpha", "--matrix_coefficients", "6", "--full_range_flag", "0", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(1, 13, 6, fullRange: false) };
        yield return new("cicp-bt2020-limited-444-12", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "12", "--no-alpha", "--matrix_coefficients", "9", "--full_range_flag", "0", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(1, 13, 9, fullRange: false) };
        yield return new("cicp-bt2020-limited-420-12", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "12", "--no-alpha", "--matrix_coefficients", "9", "--full_range_flag", "0", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled) { Cicp = new(1, 13, 9, fullRange: false) };
        yield return new("cicp-bt2020-full-420-12", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "12", "--no-alpha", "--matrix_coefficients", "9", "--full_range_flag", "1", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled) { Cicp = new(1, 13, 9, fullRange: true) };
        yield return new("cicp-identity-full-444-8", SourceKind.Rgb8, 80, 64, ["-q", "75", "--matrix_coefficients", "0", "-p", "chroma=444"], HeifDecComparison.Exact) { Cicp = new(1, 13, 0, fullRange: true) };
        yield return new("cicp-identity-full-444-10", SourceKind.Rgb16, 80, 64, ["-q", "75", "-b", "10", "--no-alpha", "--matrix_coefficients", "0", "-p", "chroma=444"], HeifDecComparison.Exact) { Cicp = new(1, 13, 0, fullRange: true) };
        yield return new("cicp-ycgco-full-444-8", SourceKind.Rgb8, 80, 64, ["-q", "75", "--matrix_coefficients", "8", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(1, 13, 8, fullRange: true) };

        // Unspecified matrix coefficients in the "nclx" box: heif-enc converts with BT.601 and says so in the bitstream only
        yield return new("cicp-unspecified-full-444-8", SourceKind.Rgb8, 80, 64, ["-q", "75", "--matrix_coefficients", "2", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(1, 13, 2, fullRange: true) };
        yield return new("cicp-gray-full-400-8", SourceKind.Gray8, 80, 64, ["-q", "75", "--matrix_coefficients", "1"], HeifDecComparison.Exact) { Cicp = new(1, 13, 1, fullRange: true) };
        yield return new("cicp-gray-limited-400-8", SourceKind.Gray8, 80, 64, ["-q", "75", "--full_range_flag", "0"], HeifDecComparison.MonochromeRangeIgnored) { Cicp = new(1, 13, 6, fullRange: false) };
        yield return new("cicp-gray-limited-400-12", SourceKind.Gray16, 80, 64, ["-q", "75", "-b", "12", "--full_range_flag", "0"], HeifDecComparison.MonochromeRangeIgnored) { Cicp = new(1, 13, 6, fullRange: false) };

        // The color profile presets of heif-enc
        yield return new("profile-auto-420-8", SourceKind.Rgb8, 80, 64, ["-q", "70", "--color-profile", "auto", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled) { Cicp = new(1, 13, 1, fullRange: true) };
        yield return new("profile-compatible-420-8", SourceKind.Rgb8, 80, 64, ["-q", "70", "--color-profile", "compatible", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled) { Cicp = new(1, 1, 1, fullRange: true) };
        yield return new("profile-601-420-8", SourceKind.Rgb8, 80, 64, ["-q", "70", "--color-profile", "601", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled) { Cicp = new(6, 6, 6, fullRange: true) };
        yield return new("profile-709-444-8", SourceKind.Rgb8, 80, 64, ["-q", "70", "--color-profile", "709", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(1, 1, 1, fullRange: true) };
        yield return new("profile-2020-420-8", SourceKind.Rgb8, 80, 64, ["-q", "70", "--color-profile", "2020"], HeifDecComparison.SitingDiffers) { Cicp = new(9, 15, 9, fullRange: true) };
        yield return new("profile-2020-444-10", SourceKind.Rgb16, 80, 64, ["-q", "70", "-b", "10", "--no-alpha", "--color-profile", "2020", "-p", "chroma=444"], HeifDecComparison.Matrix) { Cicp = new(9, 15, 9, fullRange: true) };

        // The chroma downsampling of heif-enc changes the chroma samples that are coded, not their decoding ("sharp-yuv"
        // is not in the build of the tools)
        yield return new("lossy-420-8-downsampling-nn", SourceKind.Rgb8, 80, 64, ["-q", "60", "-C", "nn", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);
        yield return new("lossy-420-8-downsampling-average", SourceKind.Rgb8, 80, 64, ["-q", "60", "-C", "average", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);

        // Encoder settings that change the coding tools of the bitstream, not the container
        foreach (var (label, parameter) in new[]
        {
            ("ctu-16", "x265:ctu=16"),
            ("ctu-32", "x265:ctu=32"),
            ("no-sao", "x265:sao=0"),
            ("no-deblock", "x265:no-deblock=1"),
            ("sao-non-deblock", "x265:sao-non-deblock=1"),
            ("tskip", "x265:tskip=1"),
            ("scaling-list", "x265:scaling-list=default"),
            ("wpp", "x265:wpp=1"),
            ("no-wpp", "x265:pools=none"),
            ("amp-rect", "x265:amp=1"),
            ("cu-lossless", "x265:cu-lossless=1"),
            ("constrained-intra", "x265:constrained-intra=1"),
            ("no-strong-intra-smoothing", "x265:strong-intra-smoothing=0"),
            ("chroma-qp-offsets", "x265:cbqpoffs=6"),
            ("picture-hash", "x265:hash=1"),
            ("access-unit-delimiters", "x265:aud=1"),
            ("repeated-headers", "x265:repeat-headers=1"),
            ("max-tu-16", "x265:max-tu-size=16"),
            ("min-cu-16", "x265:min-cu-size=16"),
            ("tu-depth-4", "x265:tu-intra-depth=4"),
            ("hdr-sei", "x265:max-cll=1000,400"),
            ("qp-20", "x265:qp=20"),
            ("preset-ultrafast", "preset=ultrafast"),
            ("preset-placebo", "preset=placebo"),
            ("tune-grain", "tune=grain"),
        })
        {
            // 200x136, and not a size such as 144x112 or 160x96: x265 4.2 never returns from the encoding of a picture of
            // these sizes when the deblocking filter is disabled ("no-deblock", "sao-non-deblock")
            yield return new($"lossy-420-8-{label}", SourceKind.Rgb8, 200, 136, ["-q", "45", "-p", "x265:chromaloc=1", "-p", parameter], HeifDecComparison.Subsampled);
        }

        // Slices that disable the in-loop filters across their boundaries, with the sample adaptive offset: libde265 filters
        // across them, so FFmpeg and HM give the planes, and heif-dec is not compared
        yield return new("lossy-420-8-slices-4", SourceKind.Rgb8, 200, 136, ["-q", "45", "-p", "x265:chromaloc=1", "-p", "x265:slices=4"], HeifDecComparison.Subsampled);
        yield return new("lossy-444-10-tskip-scaling-list", SourceKind.Rgb16, 144, 112, ["-q", "55", "-b", "10", "--no-alpha", "-p", "chroma=444", "-p", "x265:tskip=1", "-p", "x265:scaling-list=default"], HeifDecComparison.Matrix);
        yield return new("lossy-422-12-ctu-16-slices", SourceKind.Rgb16, 144, 112, ["-q", "55", "-b", "12", "--no-alpha", "-p", "chroma=422", "-p", "x265:ctu=16", "-p", "x265:slices=3"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-420-8-q5", SourceKind.Rgb8, 144, 112, ["-q", "5", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);
        yield return new("lossy-444-8-q99", SourceKind.Rgb8, 144, 112, ["-q", "99", "-p", "chroma=444"], HeifDecComparison.Matrix);
        yield return new("lossy-420-8-q100", SourceKind.Rgb8, 80, 64, ["-q", "100", "-p", "x265:chromaloc=1"], HeifDecComparison.Subsampled);

        // Sizes that are padded and cropped, with subsampled chroma: the clean aperture cuts through chroma samples
        yield return new("lossy-420-8-65x47", SourceKind.Rgb8, 65, 47, ["-q", "60"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-420-8-65x47-chromaloc-1", SourceKind.Rgb8, 65, 47, ["-q", "60", "-p", "x265:chromaloc=1"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-422-8-alpha-31x17", SourceKind.Rgba8, 31, 17, ["-q", "60", "-p", "chroma=422"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-420-10-131x77", SourceKind.Rgb16, 131, 77, ["-q", "60", "-b", "10", "--no-alpha"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-420-8-1x1", SourceKind.Rgb8, 1, 1, ["-q", "60"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-444-8-16x16", SourceKind.Rgb8, 16, 16, ["-q", "60", "-p", "chroma=444"], HeifDecComparison.Matrix);

        // Grids of lossy tiles: the chroma samples at the edges of the tiles are upsampled from the neighboring tile
        yield return new("lossy-444-8-grid-64", SourceKind.Rgb8, 200, 136, ["-q", "60", "-p", "chroma=444", "--cut-tiles", "64"], HeifDecComparison.Matrix);
        yield return new("lossy-420-8-grid-64-exact-chromaloc-1", SourceKind.Rgb8, 192, 128, ["-q", "60", "-p", "x265:chromaloc=1", "--cut-tiles", "64"], HeifDecComparison.Subsampled);
        yield return new("lossy-420-8-grid-128", SourceKind.Rgb8, 300, 200, ["-q", "60", "--cut-tiles", "128"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-422-10-grid-64", SourceKind.Rgb16, 150, 100, ["-q", "60", "-b", "10", "--no-alpha", "-p", "chroma=422", "--cut-tiles", "64"], HeifDecComparison.SitingDiffers);
        yield return new("lossy-400-12-grid-64", SourceKind.Gray16, 150, 100, ["-q", "60", "-b", "12", "--cut-tiles", "64"], HeifDecComparison.Exact);

        // Properties that describe the display and are not read, and another compatible brand
        yield return new("lossy-444-8-pasp-clli", SourceKind.Rgb8, 80, 64, ["-q", "50", "-p", "chroma=444", "--pasp", "2,1", "--clli", "1000,400"], HeifDecComparison.Matrix);
        yield return new("lossy-444-8-compatible-brand", SourceKind.Rgb8, 80, 64, ["-q", "50", "-p", "chroma=444", "--add-compatible-brand", "MiHE"], HeifDecComparison.Matrix);
    }

    private static IEnumerable<SequenceCase> CreateSequenceCases()
    {
        // Intra pictures only: every sample is a sync sample, and the presentation order is the decoding order
        yield return new("sequence-intra-lossless-rgb-8", SourceKind.Rgb8, 80, 64, ["-L", "--gop-structure", "intra-only"], 3, HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("sequence-intra-420-8-three-plays", SourceKind.Rgb8, 96, 64, ["-q", "60", "-p", "x265:chromaloc=1", "--gop-structure", "intra-only", "--timebase", "1000", "--duration", "40", "--repetitions", "3"], 5, HeifDecComparison.SitingDiffers) { Durations = [40, 1, 100, 1000, 33] };
        yield return new("sequence-intra-444-8-infinite", SourceKind.Rgb8, 96, 64, ["-q", "60", "-p", "chroma=444", "--gop-structure", "intra-only", "--fps", "25", "--repetitions", "infinite"], 4, HeifDecComparison.Matrix);
        yield return new("sequence-intra-420-8-alpha", SourceKind.Rgba8, 96, 64, ["-q", "60", "--gop-structure", "intra-only", "--timebase", "90000", "--duration", "3003", "--repetitions", "2"], 5, HeifDecComparison.SitingDiffers) { Durations = [3003, 3003, 6006, 3003, 9009] };
        yield return new("sequence-intra-420-10-once", SourceKind.Rgb16, 96, 64, ["-q", "60", "-b", "10", "--no-alpha", "-p", "x265:chromaloc=1", "--gop-structure", "intra-only", "--timebase", "24", "--repetitions", "1"], 3, HeifDecComparison.SitingDiffers);
        yield return new("sequence-intra-400-12", SourceKind.Gray16, 96, 64, ["-q", "60", "-b", "12", "--gop-structure", "intra-only", "--timebase", "25", "--repetitions", "10"], 4, HeifDecComparison.Exact) { Durations = [1, 2, 3, 4] };
        yield return new("sequence-intra-lossless-rgba-10", SourceKind.Rgba16, 80, 64, ["-L", "-b", "10", "--gop-structure", "intra-only", "--timebase", "60000", "--duration", "1001"], 3, HeifDecComparison.Exact) { SourceIsPreserved = true };
        yield return new("sequence-intra-444-12", SourceKind.Rgb16, 96, 64, ["-q", "60", "-b", "12", "--no-alpha", "-p", "chroma=444", "--gop-structure", "intra-only"], 3, HeifDecComparison.Matrix);
        yield return new("sequence-intra-422-8-eight-frames", SourceKind.Rgb8, 80, 64, ["-q", "40", "-p", "chroma=422", "--gop-structure", "intra-only", "--timebase", "600", "--duration", "50", "--repetitions", "7"], 8, HeifDecComparison.SitingDiffers);

        // P slices: every picture is predicted from the previous ones, in presentation order. heif-enc only uses inter
        // prediction for 8-bit pictures: it codes the pictures of a deeper sequence as intra pictures, whatever the structure
        yield return new("sequence-low-delay-420-8", SourceKind.Rgb8, 96, 64, ["-q", "60", "-p", "x265:chromaloc=1", "--gop-structure", "low-delay", "--timebase", "1000", "--duration", "40", "--repetitions", "3"], 5, HeifDecComparison.SitingDiffers) { UsesInterPrediction = true };
        yield return new("sequence-low-delay-444-8-alpha", SourceKind.Rgba8, 96, 64, ["-q", "60", "-p", "chroma=444", "--gop-structure", "low-delay", "--fps", "30", "--repetitions", "infinite"], 4, HeifDecComparison.Matrix) { UsesInterPrediction = true };
        yield return new("sequence-low-delay-420-8-keyframes", SourceKind.Rgb8, 96, 64, ["-q", "60", "--gop-structure", "low-delay", "--min-keyframe-distance", "2", "--max-keyframe-distance", "3"], 8, HeifDecComparison.SitingDiffers) { UsesInterPrediction = true };
        yield return new("sequence-low-delay-lossless-rgb-8", SourceKind.Rgb8, 80, 64, ["-L", "--gop-structure", "low-delay"], 4, HeifDecComparison.Exact) { UsesInterPrediction = true, SourceIsPreserved = true };
        yield return new("sequence-low-delay-420-10-intra", SourceKind.Rgb16, 96, 64, ["-q", "60", "-b", "10", "--no-alpha", "--gop-structure", "low-delay"], 4, HeifDecComparison.SitingDiffers);

        // B slices: the samples are in decoding order, and their composition offsets give the presentation order
        yield return new("sequence-unrestricted-420-8", SourceKind.Rgb8, 96, 64, ["-q", "60", "-p", "x265:chromaloc=1", "--gop-structure", "unrestricted", "--timebase", "1000", "--duration", "40", "--repetitions", "2"], 8, HeifDecComparison.SitingDiffers) { UsesInterPrediction = true };
        yield return new("sequence-unrestricted-422-8", SourceKind.Rgb8, 96, 64, ["-q", "60", "-p", "chroma=422", "--gop-structure", "unrestricted"], 6, HeifDecComparison.SitingDiffers) { UsesInterPrediction = true };
        yield return new("sequence-unrestricted-444-8-alpha", SourceKind.Rgba8, 96, 64, ["-q", "60", "-p", "chroma=444", "--gop-structure", "unrestricted", "--fps", "12"], 7, HeifDecComparison.Matrix) { UsesInterPrediction = true };
        yield return new("sequence-unrestricted-400-8", SourceKind.Gray8, 96, 64, ["-q", "60", "--gop-structure", "unrestricted"], 6, HeifDecComparison.Exact) { UsesInterPrediction = true };

        // The default structure of heif-enc
        yield return new("sequence-default-420-8", SourceKind.Rgb8, 96, 64, ["-q", "60"], 5, HeifDecComparison.SitingDiffers) { UsesInterPrediction = true };
    }

    [Theory]
    [MemberData(nameof(StillCases))]
    public async Task StillImagesDecodeAsTheReferences(string name)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var item = Stills[name];
        var source = CreateSource(item.Source, item.Width, item.Height, seed: StableSeed(name));
        var sourcePath = await SaveSourceAsync(name, source);
        var path = ArtifactPath(name + ".heic");
        await heif.EncodeAsync([.. item.Options, sourcePath], path, XunitCancellationToken);

        var still = await AssertStillAsync(heif, path, name, item.Comparison, item.SourceIsPreserved ? source : null);
        Assert.Equal((item.Width, item.Height), (still.Width, still.Height));
        if (item.Cicp is not null)
        {
            Assert.Equal(item.Cicp, still.Cicp, message: $"{name}: the library does not report the color description passed to heif-enc.");
        }
    }

    /// <summary>What <see cref="AssertStillAsync"/> verified: the size of the image as loaded, its color description and its pixels.</summary>
    internal sealed record StillResult(int Width, int Height, ExifOrientation Orientation, CicpColorDescription? Cicp, RawPixelBuffer Pixels, ReferenceHeif Reader, ReferencePlanes Color, ReferencePlanes? Alpha, (int X, int Y, int Width, int Height) Crop);

    /// <summary>
    /// A still image through <c>Load</c> and <c>Identify</c>, against the container reader of the test harness and heif-info
    /// (structure), the planes of the reference decoders converted by the oracle, and heif-dec.
    /// </summary>
    /// <param name="heif">The libheif tools.</param>
    /// <param name="path">The HEIC file.</param>
    /// <param name="name">The name of the case, and of the files it writes.</param>
    /// <param name="comparison">How the library compares with heif-dec.</param>
    /// <param name="preservedSource">The source of a file that stores it without loss, or <see langword="null"/>.</param>
    /// <param name="heifDecAlpha">What heif-dec makes of the alpha plane of the image.</param>
    /// <param name="compareWithHeifDec">Whether the pixels of heif-dec are compared; <see langword="false"/> for the files whose conversion by libheif has no model here.</param>
    internal static async Task<StillResult> AssertStillAsync(LibHeifTool heif, FullPath path, string name, HeifDecComparison comparison, RawPixelBuffer? preservedSource = null, HeifDecAlpha heifDecAlpha = HeifDecAlpha.Same, bool compareWithHeifDec = true)
    {
        var bytes = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var reader = ReferenceHeif.Parse(bytes);
        var info = await heif.GetInfoAsync(path, XunitCancellationToken);
        var primary = reader.PrimaryItem ?? throw new InvalidDataException($"{name}: the file has no primary item.");
        var color = await DecodeItemAsync(name + ".color", reader, primary);
        var alphaItem = reader.AlphaItem;
        var alpha = alphaItem is null ? null : await DecodeItemAsync(name + ".alpha", reader, alphaItem);

        // The geometry: the size of the image (for a grid, the size its descriptor gives), the clean aperture, the orientation
        var (imageWidth, imageHeight) = (color.Width, color.Height);
        Assert.Equal<(long, long)?>((imageWidth, imageHeight), primary.Size, message: $"{name}: the 'ispe' property of the primary item is not the size of its image.");
        var crop = primary.CleanAperture is { } aperture
            ? aperture.GetRectangle(imageWidth, imageHeight) ?? throw new InvalidDataException($"{name}: the clean aperture does not select whole samples.")
            : (X: 0, Y: 0, Width: imageWidth, Height: imageHeight);
        var orientation = (ExifOrientation)primary.Orientation;
        var isTransposed = orientation is ExifOrientation.LeftTop or ExifOrientation.RightTop or ExifOrientation.RightBottom or ExifOrientation.LeftBottom;

        // heif-info and the reader of the harness describe the same file
        Assert.True(info.MimeType is "image/heic" or "image/heif", $"{name}: heif-info gives the MIME type '{info.MimeType}' to the file.");
        Assert.Equal(isTransposed ? (crop.Height, crop.Width) : (crop.Width, crop.Height), (info.Width, info.Height), message: $"{name}: heif-info does not print the size of the image after its transformations.");
        Assert.Equal(color.BitDepth, info.BitDepth);
        Assert.Equal(color.IsMonochrome, info.IsMonochrome);
        Assert.Equal(heifDecAlpha switch { HeifDecAlpha.Missing => false, HeifDecAlpha.FromTiles => true, _ => alpha is not null }, info.HasAlpha, message: $"{name}: the alpha plane that heif-info reports.");
        Assert.Equal(heifDecAlpha switch { HeifDecAlpha.Missing => true, HeifDecAlpha.FromTiles => false, _ => alpha is not null }, alpha is not null, message: $"{name}: the alpha plane of the primary item.");

        Assert.Equal(primary.Type == "grid", info.Tiles is not null);
        var nclx = primary.Nclx;
        if (nclx is { ColorPrimaries: 2, TransferCharacteristics: 2, MatrixCoefficients: 2 })
        {
            // heif-info prints no color profile for an 'nclx' box whose three code points are "unspecified", whatever its
            // range: the box of the monochrome images that the library writes
            Assert.Equal<(int?, int?, int?, bool?)>((null, null, null, null), (info.ColorPrimaries, info.TransferCharacteristics, info.MatrixCoefficients, info.FullRange));
        }
        else if (nclx is not null)
        {
            Assert.Equal<(int?, int?, int?, bool?)>((nclx.ColorPrimaries, nclx.TransferCharacteristics, nclx.MatrixCoefficients, nclx.FullRange), (info.ColorPrimaries, info.TransferCharacteristics, info.MatrixCoefficients, info.FullRange));
        }

        // The pixels of the library are those of the planes, converted by the oracle
        var (matrix, fullRange) = ResolveColor(nclx, color.Signal);
        var expected = ConvertToPixels(color, alpha, matrix, fullRange, crop, ChromaModel.Declared);
        using var image = Image.Load(path);
        Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
        Assert.Equal(ExpectedPixelFormat(Math.Max(color.BitDepth, alpha?.BitDepth ?? 8), color.IsMonochrome, alpha is not null), image.PixelFormat);
        Assert.Equal(new Size(crop.Width, crop.Height), image.Size);
        Assert.Equal(1, image.Frames.Count);
        Assert.Null(image.Animation);
        Assert.Equal(orientation, image.Metadata.Orientation);
        var cicp = ExpectedCicp(nclx, color.Signal);
        Assert.Equal(cicp, image.Metadata.Cicp, message: $"{name}: color description (primaries/transfer/matrix/full range).");
        var actual = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));
        var oracleBound = ColorBound.Oracle(color, alpha, matrix, fullRange);
        var oracle = AssertPixels(expected, actual, oracleBound, $"{name}: library vs the planes of {color.Decoders} converted by the oracle");
        if (preservedSource is not null)
        {
            AssertPixels(ToStoredPixels(preservedSource, color.BitDepth), actual, ColorBound.Exact, $"{name}: library vs source");
        }

        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var identified = Image.Identify(path, new ImageIdentifyOptions { Mode = mode });
            Assert.Equal(ImageFormat.Heic, identified.Format);
            Assert.Equal(image.Size, identified.Size);
            Assert.Equal(image.PixelFormat, identified.PixelFormat);
            Assert.Equal(color.BitDepth, identified.BitsPerComponent);
            Assert.Equal<int?>(1, identified.FrameCount);
            Assert.Equal<bool?>(false, identified.IsAnimated);
            Assert.Equal<bool?>(alpha is not null, identified.MayHaveTransparency);
            Assert.Null(identified.Animation);
            Assert.Equal(cicp, identified.Metadata.Cicp);
            Assert.Equal(orientation, identified.Metadata.Orientation);
        }

        // heif-dec applies the orientation; the library reports it
        var loaded = actual;
        var result = new StillResult(crop.Width, crop.Height, orientation, cicp, loaded, reader, color, alpha, crop);
        var (decoded, decodingError) = await heif.TryDecodeAsync(path, bilinearChroma: true, XunitCancellationToken);
        if (decoded is null)
        {
            Assert.False(compareWithHeifDec, $"{name}: heif-dec does not decode the file: {decodingError}");
            WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: library vs oracle ({color.Decoders}) max {oracle.Max} (bound {oracleBound}); heif-dec does not decode the file: {decodingError}"));
            return result;
        }

        Assert.Equal((info.Width, info.Height), (decoded.Width, decoded.Height));
        image.AutoOrient(XunitCancellationToken);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        var oriented = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));
        Assert.Equal(info.HasAlpha, decoded.HasAlpha);
        if (heifDecAlpha is HeifDecAlpha.Missing)
        {
            oriented = WithoutAlpha(oriented);
        }

        if (compareWithHeifDec)
        {
            Assert.Equal(comparison, Classify(color, matrix, fullRange, crop, isSequence: false), message: $"{name}: the kind of comparison with heif-dec that the case declares is not the one its file calls for.");
            if (comparison is HeifDecComparison.SitingDiffers or HeifDecComparison.MonochromeRangeIgnored)
            {
                Assert.Equal(ExifOrientation.TopLeft, orientation, message: $"{name}: the conversion of libheif is only modeled for images without rotation.");
            }
        }

        string comparisonReport;
        if (!compareWithHeifDec)
        {
            // For information; the alpha plane of an image whose planes do not have the same bit depth is not read back
            var difference = alpha is null || alpha.BitDepth == color.BitDepth ? MeasurePixels(WithoutAlpha(decoded.GetPixels(color.BitDepth)), WithoutAlpha(oriented)) : default;
            comparisonReport = string.Create(CultureInfo.InvariantCulture, $"heif-dec is not compared (library vs heif-dec max {difference.Max}, {difference.Max / 257.0:F3}/255, mean {difference.Mean:F2})");
        }
        else if (!color.Decoders.Contains(HevcReferenceDecoder.Dec265, StringComparison.Ordinal) || alpha?.Decoders.Contains(HevcReferenceDecoder.Dec265, StringComparison.Ordinal) is false)
        {
            // The planes are those of FFmpeg and HM: dec265 gives other planes, and so does heif-dec, which has the same
            // decoder (libde265 applies the sample adaptive offset across the boundaries of the slices of a picture that
            // disable the in-loop filters across them). The pixels of heif-dec are then not a reference
            var difference = MeasurePixels(WithoutAlpha(decoded.GetPixels(color.BitDepth)), WithoutAlpha(oriented));
            comparisonReport = string.Create(CultureInfo.InvariantCulture, $"heif-dec is not compared, libde265 does not decode the planes of the other decoders (library vs heif-dec max {difference.Max}, {difference.Max / 257.0:F3}/255)");
        }
        else
        {
            comparisonReport = CompareWithHeifDec(name, comparison, heif, color, heifDecAlpha is HeifDecAlpha.Same ? alpha : null, matrix, fullRange, crop, decoded, oriented, isSequence: false, ignoreHeifDecAlpha: heifDecAlpha is HeifDecAlpha.FromTiles);
        }

        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: {info.ColorSpace} {color.BitDepth}-bit matrix {matrix} {(fullRange ? "full" : "limited")}{(alpha is not null ? " alpha" : "")}{(primary.Type == "grid" ? " grid" : "")}{(primary.CleanAperture is not null ? " clap" : "")}, chroma location {color.Signal.ChromaSampleLocType}: library vs oracle ({color.Decoders}) max {oracle.Max} (bound {oracleBound}); {comparisonReport}"));
        return result;
    }

    /// <summary>
    /// Compares the pixels of the library with those of heif-dec, by the kind of comparison the file calls for: directly, or
    /// through the planes of the reference decoders converted by the rules of libheif. <c>heif-dec -S</c> writes the images
    /// of a sequence on 8 bits, whatever their bit depth: both sides are then reduced to the 8 most significant bits of the
    /// samples of the image. For matrixed samples, libheif reduces the planes before it converts them, or the pixels after
    /// (it does the former for 4:2:0 planes and the latter for 4:4:4 planes): its pixels must be within one 8-bit step of
    /// one of the two.
    /// </summary>
    /// <returns>The measures, for the test output.</returns>
    internal static string CompareWithHeifDec(string name, HeifDecComparison comparison, LibHeifTool heif, ReferencePlanes color, ReferencePlanes? alpha, int matrix, bool fullRange, (int X, int Y, int Width, int Height) crop, HeifDecImage decoded, RawPixelBuffer library, bool isSequence, bool ignoreHeifDecAlpha = false)
    {
        var isReduced = decoded.FileBitDepth == 8 && color.BitDepth > 8;
        Assert.Equal(isSequence && color.BitDepth > 8, isReduced, message: $"{name}: heif-dec wrote a PNG file of {decoded.FileBitDepth} bits per sample.");
        var heifDec = decoded.GetPixels(isReduced ? 8 : color.BitDepth);
        if (ignoreHeifDecAlpha)
        {
            heifDec = WithoutAlpha(heifDec);
        }

        if (isReduced)
        {
            library = ReduceToEightBits(library, color.BitDepth);
        }

        if (comparison is HeifDecComparison.Exact or HeifDecComparison.Subsampled || (comparison is HeifDecComparison.Matrix && !isReduced))
        {
            var bound = comparison switch
            {
                HeifDecComparison.Exact => ColorBound.Exact,
                HeifDecComparison.Matrix => ColorBound.Matrix(color.BitDepth, fullRange),
                _ => ColorBound.Subsampled(color.BitDepth, fullRange),
            };
            var measured = AssertPixels(heifDec, library, bound, $"{name}: library vs heif-dec {heif.Version}");
            return string.Create(CultureInfo.InvariantCulture, $"library vs heif-dec max {measured.Max} ({measured.Max / 257.0:F3}/255), mean {measured.Mean:F2} (bound {bound})");
        }

        // heif-dec against the same planes converted by its own rules; its difference with the library is informative
        var chromaModel = isSequence ? ChromaModel.LibHeifSequence : ChromaModel.LibHeif;
        var modelFullRange = comparison is HeifDecComparison.MonochromeRangeIgnored || fullRange;
        var model = ConvertToPixels(color, alpha, matrix, modelFullRange, crop, chromaModel, isReduced);
        if (isReduced)
        {
            // The other order: the pixels are reduced after the conversion
            var reducedLast = ReduceToEightBits(ConvertToPixels(color, alpha, matrix, modelFullRange, crop, chromaModel), color.BitDepth);
            if (MeasurePixels(reducedLast, heifDec).Max < MeasurePixels(model, heifDec).Max)
            {
                model = reducedLast;
            }
        }

        var modelBound = comparison switch
        {
            HeifDecComparison.MonochromeRangeIgnored => ColorBound.Exact,
            _ when isReduced => ColorBound.Step(8),
            _ => ColorBound.Matrix(color.BitDepth, fullRange),
        };
        var modeled = AssertPixels(model, heifDec, modelBound, $"{name}: heif-dec {heif.Version} vs the planes of {color.Decoders} converted by the rules of libheif");
        var difference = MeasurePixels(heifDec, library);
        if (difference.FirstAlphaMismatch is var (x, y))
            Assert.Fail($"{name}: alpha of pixel ({x}, {y}) is {library.GetSample(x, y, 3)}; heif-dec {heif.Version} gives {heifDec.GetSample(x, y, 3)}.");

        return string.Create(CultureInfo.InvariantCulture, $"heif-dec vs the planes converted by the rules of libheif max {modeled.Max} (bound {modelBound}); library vs heif-dec, which converts differently: max {difference.Max} ({difference.Max / 257.0:F3}/255), mean {difference.Mean:F2}");
    }

    /// <summary>The kind of comparison with heif-dec that a file calls for (<see cref="HeifDecComparison"/>).</summary>
    internal static HeifDecComparison Classify(ReferencePlanes color, int matrix, bool fullRange, (int X, int Y, int Width, int Height) crop, bool isSequence)
    {
        if (color.IsMonochrome)
            return fullRange ? HeifDecComparison.Exact : HeifDecComparison.MonochromeRangeIgnored;

        if (matrix == 0)
            return HeifDecComparison.Exact;

        if (color.ChromaFormatIdc == 3)
            return HeifDecComparison.Matrix;

        var isCropped = crop != (0, 0, color.Width, color.Height);
        return color.ChromaFormatIdc == 1 && color.Signal.ChromaSampleLocType == 1 && !isCropped && !isSequence ? HeifDecComparison.Subsampled : HeifDecComparison.SitingDiffers;
    }

    internal static RawPixelBuffer WithoutAlpha(RawPixelBuffer pixels)
    {
        var result = pixels.Span.ToArray();
        for (var i = 6; i < result.Length; i += 8)
        {
            (result[i], result[i + 1]) = (0xFF, 0xFF);
        }

        return RawPixelBuffer.Create(pixels.Width, pixels.Height, RawPixelLayout.Rgba16Le, result);
    }
}
