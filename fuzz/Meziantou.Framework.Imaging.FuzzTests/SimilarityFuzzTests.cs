using System.Diagnostics;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Random pairs of frames compared by the similarity metrics (SSIM at every precision, the SSIM difference map,
/// SSIMULACRA 2 and Butteraugli), whose vectorized kernels must keep the invariants of the scores.
/// </summary>
/// <remarks>
/// The frames have any size from 1×1 (SSIMULACRA 2 from 8×8), a third of them translucent, and the second frame is a
/// copy of the first with a few changes, or unrelated pixels. The scores are numbers in their ranges, and identical
/// frames get the scores of identical frames: an SSIM of exactly 1, a map of black pixels, a SSIMULACRA 2 score of 100
/// and a Butteraugli distance of 0. Floating-point frames compared as fractions may hold samples that are not finite: the
/// comparison fails with <see cref="UnsupportedImageFeatureException"/> then, and only then.
/// </remarks>
public sealed class SimilarityFuzzTests
{
    // The alphas that premultiplication treats specially: transparent, opaque, and the roundings around the middle
    private static readonly byte[] Alphas = [0, 1, 127, 128, 254, 255];

    [Fact]
    public void RandomFramesGetScoresInTheirRanges()
    {
        var random = new FuzzRandom(FuzzSettings.Seed ^ 0x53696D696C617279UL);
        var stopwatch = Stopwatch.StartNew();
        var failures = new List<string>();
        var executed = 0;

        // A case computes every metric twice: a tenth of the budget of the decoding campaigns
        var iterations = Math.Max(FuzzSettings.Iterations / 10, 1);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            var caseSeed = random.NextUInt64();
            var failure = RunWithWatchdog(caseSeed);
            executed++;
            if (failure is null)
                continue;

            failures.Add($"case seed {caseSeed}: {failure}");
            if (failures.Count >= 4)
                break;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant($"similarity: {executed} cases (base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s"));
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} fuzz failure(s):{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures)}");
    }

    [Fact]
    public void TheOracleReportsScoresOutOfTheirRanges()
    {
        Assert.Null(CheckRange("score", 0.5, -1, 1));
        Assert.StartsWith("score is NaN", CheckRange("score", double.NaN, -1, 1), StringComparison.Ordinal);
        Assert.StartsWith("score is 2", CheckRange("score", 2, -1, 1), StringComparison.Ordinal);
    }

    private static string? RunWithWatchdog(ulong seed)
    {
        var task = Task.Run(() => Check(seed));
        return task.Wait(FuzzSettings.CaseTimeout) ? task.Result : $"timeout after {FuzzSettings.CaseTimeout.TotalSeconds:F0} s";
    }

    /// <summary>Runs one case, entirely determined by its seed.</summary>
    private static string? Check(ulong seed)
    {
        var random = new FuzzRandom(seed);
        var width = random.Next(1, 41);
        var height = random.Next(1, 41);
        try
        {
            return random.Chance(25) ? CheckFloatingPoint(random, width, height) : CheckBytes(random, width, height);
        }
        catch (Exception exception)
        {
            return $"exception: {exception}";
        }
    }

    private static string? CheckBytes(FuzzRandom random, int width, int height)
    {
        var translucent = random.Chance(33);
        var expectedPixels = new Rgba32[width * height];
        for (var i = 0; i < expectedPixels.Length; i++)
        {
            expectedPixels[i] = new Rgba32(random.NextByte(), random.NextByte(), random.NextByte(), translucent ? random.Pick(Alphas) : byte.MaxValue);
        }

        var identical = random.Chance(20);
        var actualPixels = (Rgba32[])expectedPixels.Clone();
        if (!identical)
        {
            var related = random.Chance(50);
            for (var i = 0; i < actualPixels.Length; i++)
            {
                if (!related || random.Chance(10))
                {
                    actualPixels[i] = new Rgba32(random.NextByte(), random.NextByte(), random.NextByte(), translucent ? random.Pick(Alphas) : byte.MaxValue);
                }
            }

            identical = actualPixels.AsSpan().SequenceEqual(expectedPixels);
        }

        using var expectedImage = Image.ImportPixelData<Rgba32>(expectedPixels, width, height);
        using var actualImage = Image.ImportPixelData<Rgba32>(actualPixels, width, height);
        using var expectedWide = expectedImage.CloneAs<Rgba64>();
        using var actualWide = actualImage.CloneAs<Rgba64>();
        var expected = expectedImage.Frames[0];
        var actual = actualImage.Frames[0];

        var ssim = ImageSimilarity.ComputeSsim(expected, actual);
        var wideSsim = ImageSimilarity.ComputeSsim(expectedWide.Frames[0], actualWide.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit });
        var failure = CheckRange("SSIM", ssim, -1, 1) ?? CheckRange("16-bit SSIM", wideSsim, -1, 1) ?? CheckMap(expected, actual, identical);
        if (failure is not null)
            return failure;

        if (identical && (ssim != 1 || wideSsim != 1))
            return FormattableString.Invariant($"identical frames have an SSIM of {ssim} and a 16-bit SSIM of {wideSsim}");

        var butteraugli = ImageSimilarity.ComputeButteraugliDistance(expected, actual);
        failure = CheckRange("Butteraugli distance", butteraugli.MaxDistance, 0, double.MaxValue) ?? CheckRange("Butteraugli 3-norm", butteraugli.PNorm3, 0, double.MaxValue);
        if (failure is not null)
            return failure;

        if (identical && (butteraugli.MaxDistance != 0 || butteraugli.PNorm3 != 0))
            return FormattableString.Invariant($"identical frames have a Butteraugli distance of {butteraugli.MaxDistance} (3-norm {butteraugli.PNorm3})");

        // PSNR and the hash distances are symmetric, and those of identical frames are infinite and 0
        var psnr = ImageSimilarity.ComputePsnr(expected, actual);
        failure = CheckRange("PSNR", psnr, 0, double.PositiveInfinity);
        if (failure is not null)
            return failure;

        if ((identical && !double.IsPositiveInfinity(psnr)) || ImageSimilarity.ComputePsnr(actual, expected) != psnr)
            return FormattableString.Invariant($"the frames have a PSNR of {psnr}, and of {ImageSimilarity.ComputePsnr(actual, expected)} in the other order (identical: {identical})");

        foreach (var perceptual in (bool[])[false, true])
        {
            var distance = perceptual ? ImageSimilarity.ComputePerceptualHashDistance(expected, actual) : ImageSimilarity.ComputeDifferenceHashDistance(expected, actual);
            var reverse = perceptual ? ImageSimilarity.ComputePerceptualHashDistance(actual, expected) : ImageSimilarity.ComputeDifferenceHashDistance(actual, expected);
            if (distance is < 0 or > 64 || distance != reverse || (identical && distance != 0))
                return FormattableString.Invariant($"the frames have a {(perceptual ? "pHash" : "dHash")} distance of {distance}, and of {reverse} in the other order (identical: {identical})");
        }

        if (width < 8 || height < 8)
            return null;

        var ssimulacra2 = ImageSimilarity.ComputeSsimulacra2(expected, actual);
        failure = CheckRange("SSIMULACRA 2", ssimulacra2, double.MinValue, 100);
        if (failure is not null)
            return failure;

        return identical && ssimulacra2 != 100 ? FormattableString.Invariant($"identical frames have a SSIMULACRA 2 score of {ssimulacra2}") : null;
    }

    private static string? CheckFloatingPoint(FuzzRandom random, int width, int height)
    {
        var finite = true;
        using var expectedImage = new Image<RgbaFloat>(width, height);
        using var actualImage = new Image<RgbaFloat>(width, height);
        foreach (var image in new[] { expectedImage, actualImage })
        {
            var frame = image.Frames[0];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = new RgbaFloat(NextSample(random), NextSample(random), NextSample(random), NextSample(random));
                    finite &= float.IsFinite(pixel.R) && float.IsFinite(pixel.G) && float.IsFinite(pixel.B) && float.IsFinite(pixel.A);
                    frame[x, y] = pixel;
                }
            }
        }

        var expected = expectedImage.Frames[0];
        var actual = actualImage.Frames[0];
        try
        {
            _ = ImageSimilarity.ComputeSsim(expected, actual, new ImageSimilarityOptions { Precision = SimilarityPrecision.FloatingPoint });
            using var map = ImageSimilarity.CreateDifferenceMap(expected, actual, new DifferenceMapOptions { Kind = DifferenceMapKind.Ssim });
            return finite ? null : "frames with samples that are not finite were compared";
        }
        catch (UnsupportedImageFeatureException) when (!finite)
        {
            return null;
        }
    }

    /// <summary>Checks the size and the opacity of the SSIM map, and that identical frames get a black map.</summary>
    private static string? CheckMap(ImageFrame expected, ImageFrame actual, bool identical)
    {
        using var map = ImageSimilarity.CreateDifferenceMap(expected, actual, new DifferenceMapOptions { Kind = DifferenceMapKind.Ssim });
        if (map.Width != expected.Width || map.Height != expected.Height)
            return $"the map is {map.Width}×{map.Height}";

        var frame = map.Frames[0];
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var pixel = frame[x, y];
                if (pixel.A != byte.MaxValue || pixel.R != pixel.G || pixel.G != pixel.B || (identical && pixel.R != 0))
                    return $"pixel ({x}, {y}) of the map is {pixel}";
            }
        }

        return null;
    }

    private static string? CheckRange(string name, double value, double minimum, double maximum)
    {
        if (double.IsNaN(value))
            return $"{name} is NaN";

        return value < minimum - 1e-9 || value > maximum + 1e-9 ? FormattableString.Invariant($"{name} is {value}, outside [{minimum}, {maximum}]") : null;
    }

    private static float NextSample(FuzzRandom random) => random.Next(40) switch
    {
        0 => float.NaN,
        1 => float.PositiveInfinity,
        2 => -0f,
        3 => (float)random.Next(-1000, 1000),
        _ => random.Next(1 << 16) / 65535f,
    };
}
