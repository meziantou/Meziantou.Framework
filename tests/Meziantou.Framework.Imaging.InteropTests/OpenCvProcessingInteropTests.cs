using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The feature detection, homography, inpainting and deskew of the library compared with OpenCV (<see cref="OpenCvOracleTool"/>)
/// on synthetic and seeded inputs. The conventions are normalized here, never in the library:
/// <list type="bullet">
/// <item><description>
/// Hough lines: OpenCV gets the Canny edges of the library, so both transforms see the same pixels. Both vote at the pixel
/// index; the library then gives the distance for the pixel centers, <c>(cos θ + sin θ) / 2</c> further, and its angles
/// in degrees. A library pixel only votes for the angles within 30 degrees of its gradient sector, OpenCV's for all of
/// them, so the votes of OpenCV are a superset: equal on straight edges, a few more where Canny rounds a corner. OpenCV
/// has no gradient sectors, separations or tie-breaks by rounding residuals: its peaks (4 neighbors) can be one cell from
/// the library's (8 neighbors). The library has no probabilistic variant (<c>HoughLinesP</c>).
/// </description></item>
/// <item><description>
/// Harris and Shi-Tomasi: both use the 3 x 3 Sobel operator. OpenCV scales it by <c>1 / (4 · blockSize · 255)</c> and sums
/// the products over a box of <c>blockSize</c>; the library divides it by 8 (samples as fractions of 1) and weights the
/// products with a Gaussian of <see cref="CornerDetectionOptions.Sigma"/>. With equal windows the response of OpenCV is
/// 16 times the library's, which a ramp (constant products) checks exactly; the Gaussian of σ = 0.8 has about the variance
/// of a box of 3 (2/3), which makes the two maps nearly proportional elsewhere. The borders differ (OpenCV reflects, the
/// library repeats the edge pixels): the comparisons leave out 3 pixels.
/// </description></item>
/// <item><description>
/// FAST: the segment test is the same (a circle of 16 at radius 3, an arc of 9 beyond the threshold, the pixels nearer
/// than 3 to an edge not tested), with the threshold <c>t</c> of OpenCV as <c>(t + 0.5) / 255</c>: both then need a
/// difference of at least <c>t + 1</c>. The scores differ (OpenCV: the largest threshold the pixel passes; the library:
/// the sum of the differences beyond the threshold, Rosten and Drummond), so the non-maximum suppressions keep different
/// pixels of a cluster; OpenCV also drops the corners of equal scores, as on binary shapes.
/// </description></item>
/// <item><description>
/// Homography: OpenCV's matrix maps column vectors, the library's row vectors (one is the transpose of the other), and
/// OpenCV reads the points as floats (the inputs are multiples of 1/1024). Its method 0 is the normalized DLT followed by
/// a Levenberg-Marquardt refinement of the reprojection error; the library stops at the DLT, so its error is at most a
/// fraction of a percent larger on noisy points. RANSAC draws different samples: the inlier sets and the refits are
/// compared, not the matrices.
/// </description></item>
/// <item><description>
/// Inpainting: both implement Telea (2004), but OpenCV's <c>INPAINT_TELEA</c> adds the gradient term normalized to a unit
/// vector, which shifts its fill by about a level even on a linear ramp that the first-order estimates of the paper (the
/// library) reproduce exactly. OpenCV is no exact oracle: the library must keep the same pixels and fill no worse than it,
/// measured against the image before the mask.
/// </description></item>
/// <item><description>
/// Deskew: the ground truth is the angle the page is drawn at. The second opinion is <c>HoughLines</c> on the lowest ink
/// pixels of the words (their baselines), at 0.05 degree.
/// </description></item>
/// </list>
/// Seam carving is not compared: its forward energy (Rubinstein, Shamir and Avidan, 2008) has no OpenCV equivalent.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class OpenCvProcessingInteropTests
{
    private static readonly ProjectiveTransform Perspective = new(1.1, 0.12, 0.0004, -0.08, 0.95, -0.0003, 12, -7, 1);

    // ---------------------------------------------------------------- Hough lines

    public static TheoryData<double> HalfPlaneAngles() => [0, 7, 15, 22, 30, 45, 60, 67.5, 89, 90, 113, 135, 150, 172];

    [Theory]
    [MemberData(nameof(HalfPlaneAngles))]
    public async Task DetectLines_StraightEdgeMatchesOpenCvHoughLines(double angle)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var (sin, cos) = Math.SinCos(angle * Math.PI / 180);
        var image = Fill(128, 128, 50, (x, y) => ((x + 0.5 - 61.3) * cos) + ((y + 0.5 - 66.8) * sin) > 0 ? 200 : null);
        var (lines, cv) = await DetectLinesAsync(opencv, image);

        // The strongest line of both is the edge, in the same cell; the votes of OpenCV are a superset of the library's
        // (the 2 extra votes at 135 degrees are pixels at the corners of the frame, outside the sector of the edge)
        var top = lines[0];
        Assert.Equal(Math.Round(angle, MidpointRounding.AwayFromZero), top.Angle);
        var cvTop = cv.MaxBy(line => line.Votes);
        Assert.Equal(top.Angle, cvTop.Theta * 180 / Math.PI, 1e-4);
        Assert.Equal(ToOpenCvDistance(top), cvTop.Rho, 1e-4);
        Assert.InRange(cvTop.Votes - top.Votes, 0, 2);
    }

    public static TheoryData<string> HoughScenes() => ["page", "rotated-10", "rotated-25", "rotated-37", "rotated-52", "rotated-71", "triangle", "grid"];

    [Theory]
    [MemberData(nameof(HoughScenes))]
    public async Task DetectLines_ShapesMatchOpenCvHoughLines(string scene)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var (lines, cv) = await DetectLinesAsync(opencv, CreateHoughScene(scene));
        Assert.NotEmpty(lines);

        // Every strong line of the library is a peak of OpenCV within a cell, with at least as many votes and at most 10%
        // more (the pixels where Canny rounds a corner vote outside their gradient sector in OpenCV only). The distances
        // are rounded the same way: most lines fall in the same cell
        var strongest = lines[0].Votes;
        var sameCell = 0;
        var strong = lines.Where(line => line.Votes * 2 >= strongest).ToArray();
        foreach (var line in strong)
        {
            var match = cv.Where(c => Math.Abs(line.Angle - (c.Theta * 180 / Math.PI)) <= 1 + 1e-4 && Math.Abs(ToOpenCvDistance(line) - c.Rho) <= 1 + 1e-4).MaxBy(c => c.Votes);
            Assert.True(match.Votes > 0, $"{scene}: no OpenCV peak near the line {line}.");
            Assert.InRange(match.Votes, line.Votes, (line.Votes * 1.1) + 2);
            if (Math.Abs(line.Angle - (match.Theta * 180 / Math.PI)) < 1e-4 && Math.Abs(ToOpenCvDistance(line) - match.Rho) < 1e-4)
            {
                sameCell++;
            }
        }

        Assert.True(sameCell * 4 >= strong.Length * 3, $"{scene}: {sameCell} of {strong.Length} lines in the cell of OpenCV.");

        // Every strong peak of OpenCV is within 2 degrees and 2 pixels of a line of the library
        var cvStrongest = cv.Max(c => c.Votes);
        foreach (var peak in cv.Where(c => c.Votes * 4 >= cvStrongest * 3))
        {
            Assert.Contains(lines, line => Math.Abs(line.Angle - (peak.Theta * 180 / Math.PI)) <= 2 + 1e-4 && Math.Abs(ToOpenCvDistance(line) - peak.Rho) <= 2 + 1e-4, $"{scene}: the OpenCV peak θ={peak.Theta * 180 / Math.PI} ρ={peak.Rho} ({peak.Votes} votes) has no line of the library.");
        }
    }

    // ---------------------------------------------------------------- Harris and Shi-Tomasi

    [Fact]
    public async Task Harris_RampResponseIsSixteenTimesOpenCv()
    {
        // The products of the gradients are constant: the box of OpenCV and the Gaussian of the library give the same
        // structure tensor, and the Sobel scales and k must agree exactly
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var ramp = Fill(48, 48, 0, (x, y) => 20 + (2 * x) + (3 * y));
        var cv = await opencv.HarrisAsync(ramp, 3, 3, 0.04, XunitCancellationToken);
        var library = ComputeLibraryResponses(ramp, sigma: 1);
        for (var y = 4; y < 44; y++)
        {
            for (var x = 4; x < 44; x++)
            {
                var expected = (double)cv[(y * 48) + x];
                Assert.True(expected < 0);
                Assert.Equal(expected, 16 * library[(y * 48) + x], Math.Abs(expected) * 1e-5);
            }
        }
    }

    public static TheoryData<string> CornerScenes() => ["square", "checkerboard", "rotated-square", "rectangles"];

    [Theory]
    [MemberData(nameof(CornerScenes))]
    public async Task Harris_ResponseMapMatchesOpenCv(string scene)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var image = CreateCornerScene(scene);
        var cv = await opencv.HarrisAsync(image, 3, 3, 0.04, XunitCancellationToken);
        var library = ComputeLibraryResponses(image, sigma: 0.8);

        // The windows differ (a box of 3, a Gaussian of 0.8): the maps are compared by their cosine similarity, measured
        // 0.974 to 0.982 on these scenes (0.61 to 0.71 with σ = 0.5), and by their largest values (ratios 0.80 to 1.004)
        double dot = 0, libraryNorm = 0, cvNorm = 0, libraryMax = double.MinValue, cvMax = double.MinValue;
        for (var y = 3; y < image.Height - 3; y++)
        {
            for (var x = 3; x < image.Width - 3; x++)
            {
                var a = 16 * library[(y * image.Width) + x];
                var b = (double)cv[(y * image.Width) + x];
                dot += a * b;
                libraryNorm += a * a;
                cvNorm += b * b;
                libraryMax = Math.Max(libraryMax, a);
                cvMax = Math.Max(cvMax, b);
            }
        }

        var cosine = dot / Math.Sqrt(libraryNorm * cvNorm);
        Assert.True(cosine >= 0.965, $"{scene}: cosine similarity {cosine}.");
        Assert.InRange(libraryMax / cvMax, 0.75, 1.25);
    }

    public static TheoryData<string, CornerDetectionMethod> CornerCases()
    {
        var data = new TheoryData<string, CornerDetectionMethod>();
        foreach (var scene in new[] { "square", "checkerboard", "rotated-square", "rectangles" })
        {
            data.Add(scene, CornerDetectionMethod.Harris);
            data.Add(scene, CornerDetectionMethod.ShiTomasi);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CornerCases))]
    public async Task DetectCorners_MatchOpenCvGoodFeaturesToTrack(string scene, CornerDetectionMethod method)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var image = CreateCornerScene(scene);
        using var library = ToImage(image);
        var useHarris = method == CornerDetectionMethod.Harris;

        // The windows differ: a corner of one is within a pixel of a corner of the other, when it is clearly above the
        // threshold (10% of the largest response) and the other keeps everything above 1%
        IReadOnlyList<ImageCorner> Detect(double threshold) => library.Frames[0].DetectCorners(new CornerDetectionOptions { Method = method, Sigma = 0.8, MinDistance = 3, ResponseThreshold = threshold, MaxCorners = 1000 });
        var libraryStrong = Detect(0.1);
        var libraryAll = Detect(0.01);
        var cvStrong = await opencv.GoodFeaturesAsync(image, 1000, 0.1, 3, 3, useHarris, 0.04, XunitCancellationToken);
        var cvAll = await opencv.GoodFeaturesAsync(image, 1000, 0.01, 3, 3, useHarris, 0.04, XunitCancellationToken);
        Assert.NotEmpty(libraryStrong);
        Assert.NotEmpty(cvStrong);
        foreach (var corner in libraryStrong)
        {
            Assert.Contains(cvAll, other => Math.Abs(other.X - corner.X) <= 1 && Math.Abs(other.Y - corner.Y) <= 1, $"{scene}: the corner {corner} of the library is not a corner of OpenCV.");
        }

        foreach (var (x, y) in cvStrong)
        {
            Assert.Contains(libraryAll, corner => Math.Abs(x - corner.X) <= 1 && Math.Abs(y - corner.Y) <= 1, $"{scene}: the corner ({x}, {y}) of OpenCV is not a corner of the library.");
        }
    }

    // ---------------------------------------------------------------- FAST

    public static TheoryData<string, int> FastCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var scene in new[] { "dots", "square", "checkerboard", "rectangles", "noise-rectangles" })
        {
            foreach (var threshold in new[] { 10, 20, 40 })
            {
                data.Add(scene, threshold);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FastCases))]
    public async Task DetectCorners_FastMatchesOpenCvSegmentTest(string scene, int threshold)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var image = scene == "dots" ? Fill(64, 64, 0, (x, y) => (x == 20 && y == 20) || (x == 40 && y == 30) || (x == 30 && y == 45) ? 200 : null) : CreateCornerScene(scene);
        using var library = ToImage(image);
        var corners = library.Frames[0].DetectCorners(new CornerDetectionOptions { Method = CornerDetectionMethod.Fast, FastThreshold = (threshold + 0.5) / 255, MinDistance = 1, MaxCorners = 1_000_000 });
        var segment = (await opencv.FastAsync(image, threshold, nonmaxSuppression: false, "9_16", XunitCancellationToken)).Select(point => (point.X, point.Y)).ToHashSet();
        Assert.NotEmpty(segment);

        // The corners of the library pass the segment test of OpenCV, and each cluster of pixels that pass it (8-connected)
        // keeps at least one, its strongest: an isolated pixel is always a corner
        var found = corners.Select(corner => (corner.X, corner.Y)).ToHashSet();
        Assert.True(found.IsSubsetOf(segment), $"{scene}: {found.Except(segment).Count()} corners of the library fail the segment test of OpenCV.");
        var visited = new HashSet<(int X, int Y)>();
        foreach (var start in segment)
        {
            if (!visited.Add(start))
                continue;

            var cluster = new List<(int X, int Y)> { start };
            for (var i = 0; i < cluster.Count; i++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var neighbor = (cluster[i].X + dx, cluster[i].Y + dy);
                        if (segment.Contains(neighbor) && visited.Add(neighbor))
                        {
                            cluster.Add(neighbor);
                        }
                    }
                }
            }

            Assert.True(cluster.Exists(found.Contains), $"{scene}: no corner in the cluster of {cluster.Count} pixels at {start}.");
        }

        if (scene == "dots")
        {
            // Isolated dots: both suppressions keep every one
            var suppressed = await opencv.FastAsync(image, threshold, nonmaxSuppression: true, "9_16", XunitCancellationToken);
            Assert.Equal(suppressed.Select(point => (point.X, point.Y)).Order().ToArray(), found.Order().ToArray());
        }
    }

    // ---------------------------------------------------------------- Homography

    public static TheoryData<double, int> HomographyCases()
    {
        var data = new TheoryData<double, int>();
        foreach (var noise in new[] { 0.0, 0.5, 2.0 })
        {
            for (var seed = 0; seed < 3; seed++)
            {
                data.Add(noise, seed);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HomographyCases))]
    public async Task EstimateHomography_MatchesOpenCvDlt(double noise, int seed)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var (source, destination) = CreateCorrespondences(Perspective, 30, noise, outliers: 0, seed);
        var library = ProjectiveTransform.EstimateHomography(source, destination);
        var (matrix, _) = await opencv.FindHomographyAsync(ToCorrespondences(source, destination), "dlt", 3, 0, XunitCancellationToken);
        Assert.NotNull(matrix);
        var cv = FromOpenCv(matrix);

        if (noise == 0)
        {
            // The destinations are exact up to their rounding to 1/1024: both recover the homography
            Assert.InRange(MaxDistance(library, Perspective, source), 0, 1e-3);
            Assert.InRange(MaxDistance(library, cv, source), 0, 1e-4);
        }
        else
        {
            // OpenCV refines the reprojection error, which the DLT does not minimize: measured, the error of the library is
            // 0.004% to 0.08% larger, and the two maps differ by 2.6% to 13% of the noise
            var cvError = RootMeanSquareError(cv, source, destination);
            Assert.InRange(RootMeanSquareError(library, source, destination), cvError * 0.9999, cvError * 1.002);
            Assert.InRange(MaxDistance(library, cv, source), 0, 0.15 * noise);
        }
    }

    public static TheoryData<int> RansacSeeds() => [0, 1, 2, 3, 4, 5, 6, 7];

    [Theory]
    [MemberData(nameof(RansacSeeds))]
    public async Task EstimateHomographyRobust_MatchesOpenCvRansac(int seed)
    {
        const int InlierCount = 40;
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var (source, destination) = CreateCorrespondences(Perspective, InlierCount, 0.3, outliers: 15, seed);
        var library = ProjectiveTransform.EstimateHomographyRobust(source, destination, new HomographyRansacOptions { Threshold = 3, Seed = seed });
        var (matrix, inliers) = await opencv.FindHomographyAsync(ToCorrespondences(source, destination), "ransac", 3, seed, XunitCancellationToken);
        Assert.NotNull(matrix);
        var cv = FromOpenCv(matrix);

        // The outliers are 30 pixels or more away, the inliers within 0.3 pixel of noise: both find the same inliers, the
        // true ones, and refit them to the same error (measured: within 0.01%)
        bool[] expected = [.. Enumerable.Range(0, source.Length).Select(i => i < InlierCount)];
        Assert.Equal(expected, inliers);
        Assert.Equal(expected, library.Inliers.ToArray());
        var cvError = RootMeanSquareError(cv, source.AsSpan(0, InlierCount), destination.AsSpan(0, InlierCount));
        Assert.InRange(library.RootMeanSquareError, cvError * 0.9999, cvError * 1.001);
        Assert.InRange(MaxDistance(library.Transform, cv, source.AsSpan(0, InlierCount)), 0, 0.05);
    }

    // ---------------------------------------------------------------- Inpainting

    [Fact]
    public async Task Inpaint_ConstantImageMatchesOpenCvTelea()
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var image = Fill(64, 64, 100, (x, y) => null);
        var mask = Fill(64, 64, 0, (x, y) => x is >= 28 and < 36 && y is >= 28 and < 36 ? 255 : null);
        var library = Inpaint(image, mask, 3);
        var telea = await opencv.InpaintAsync(image, mask, 3, "telea", XunitCancellationToken);

        // The library fills the constant, OpenCV within the 95 to 105 its own tests allow
        Assert.Equal(image.Samples, library.Samples);
        Assert.All(telea.Samples, sample => Assert.InRange(sample, 95, 105));
    }

    public static TheoryData<string, int> InpaintCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var scene in new[] { "gradient-blob", "gradient-scratches", "edge-blob", "texture-text", "rings-square" })
        {
            data.Add(scene, 3);
            data.Add(scene, 5);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InpaintCases))]
    public async Task Inpaint_FillsNoWorseThanOpenCvTelea(string scene, int radius)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var (image, mask) = CreateInpaintScene(scene);
        var library = Inpaint(image, mask, radius);
        var telea = await opencv.InpaintAsync(image, mask, radius, "telea", XunitCancellationToken);

        // Both keep the pixels outside the mask. Inside, the mean error against the image before the mask: measured, the
        // library has 0.03 to 0.3 on the ramps where OpenCV has 0.9 to 2.9, 20 where OpenCV has 24 to 25 across a step
        // edge, 7 to 10 where OpenCV has 11 to 12 on a texture, 38 to 41 where OpenCV has 68 to 77 on rings
        var libraryError = 0.0;
        var teleaError = 0.0;
        var count = 0;
        for (var i = 0; i < image.Samples.Length; i++)
        {
            if (mask.Samples[i] == 0)
            {
                Assert.Equal(image.Samples[i], library.Samples[i]);
                Assert.Equal(image.Samples[i], telea.Samples[i]);
                continue;
            }

            libraryError += Math.Abs(library.Samples[i] - image.Samples[i]);
            teleaError += Math.Abs(telea.Samples[i] - image.Samples[i]);
            count++;
        }

        libraryError /= count;
        teleaError /= count;
        Assert.True(libraryError <= teleaError, $"{scene}, radius {radius}: the mean error of the library is {libraryError}, OpenCV's {teleaError}.");
        if (scene.StartsWith("gradient-", StringComparison.Ordinal))
        {
            // A linear ramp: the first-order estimates are exact, up to the rounding to 8 bits
            Assert.InRange(libraryError, 0, 0.5);
        }
    }

    // ---------------------------------------------------------------- Deskew

    public static TheoryData<double> SkewAngles() => [-13.87, -9, -4.3, -1.5, 0, 0.7, 2.25, 3.33, 5, 11.4];

    [Theory]
    [MemberData(nameof(SkewAngles))]
    public async Task EstimateSkew_MatchesOpenCvHoughLines(double angle)
    {
        var opencv = await RequiredTools.OpenCvOracleAsync();
        var document = CreateDocument(320, 240, angle, out var ink);
        using var library = ToImage(document);
        var estimate = library.Frames[0].EstimateSkew();

        // OpenCV: the median angle of the 9 strongest near-horizontal lines through the baselines of the words (measured:
        // within 0.15 degree of the truth; the library within its precision, 0.05)
        var baselines = Fill(ink.Width, ink.Height, 0, (x, y) => ink[x, y] != 0 && (y + 1 == ink.Height || ink[x, y + 1] == 0) ? 255 : null);
        var lines = await opencv.HoughLinesAsync(baselines, 1, Math.PI / 3600, 60, XunitCancellationToken);
        var slopes = lines.Select(line => (line.Theta * 180 / Math.PI) - 90).Where(slope => Math.Abs(slope) <= 15).Take(9).Order().ToArray();
        Assert.HasCount(9, slopes);
        var cv = slopes[4];

        Assert.InRange(estimate, angle - 0.05, angle + 0.05);
        Assert.InRange(cv, angle - 0.2, angle + 0.2);
        Assert.InRange(estimate, cv - 0.2, cv + 0.2);
    }

    // ---------------------------------------------------------------- Helpers

    private static async Task<(IReadOnlyList<HoughLine> Lines, IReadOnlyList<(double Rho, double Theta, int Votes)> OpenCv)> DetectLinesAsync(OpenCvOracleTool opencv, OpenCvGrayImage image)
    {
        // Every peak of the library (no separation), and every peak of OpenCV over the Canny edges of the library
        using var library = ToImage(image);
        var options = new HoughLineOptions { MinVotes = 15, MaxLines = 100_000, MinAngleSeparation = 0, MinDistanceSeparation = 0 };
        var lines = library.Frames[0].DetectLines(options);
        library.DetectCannyEdges(options.EdgeOptions);
        var cv = await opencv.HoughLinesAsync(FromImage(library), 1, Math.PI / 180, threshold: 14, XunitCancellationToken);
        return (lines, cv);
    }

    /// <summary>The distance of a line of the library for the pixel indices, as OpenCV gives it.</summary>
    private static double ToOpenCvDistance(HoughLine line)
    {
        var (sin, cos) = Math.SinCos(line.Angle * Math.PI / 180);
        return line.Distance - ((cos + sin) / 2);
    }

    /// <summary>The Harris response of every pixel, as the library computes it.</summary>
    private static double[] ComputeLibraryResponses(OpenCvGrayImage image, double sigma)
    {
        // The luma of a gray pixel is its sample
        double[] luma = [.. image.Samples.Select(sample => sample / 255.0)];
        var responses = new double[luma.Length];
        CornerDetector.ComputeStructureTensorResponses(AllocationScope.Create(configuration: null), luma, responses, image.Width, image.Height, new CornerDetectionOptions { Sigma = sigma }, XunitCancellationToken);
        return responses;
    }

    private static OpenCvGrayImage Inpaint(OpenCvGrayImage image, OpenCvGrayImage mask, int radius)
    {
        using var library = ToImage(image);
        var pixels = new PixelMask(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                pixels[x, y] = mask[x, y] != 0;
            }
        }

        library.Inpaint(pixels, new InpaintingOptions { Radius = radius });
        return FromImage(library);
    }

    private static OpenCvGrayImage CreateHoughScene(string scene)
    {
        switch (scene)
        {
            case "page":
                return Fill(64, 48, 40, (x, y) => x is >= 12 and < 52 && y is >= 10 and < 40 ? 240 : null);
            case "triangle":
                return Fill(96, 96, 30, (x, y) => IsInsideTriangle(x + 0.5, y + 0.5, (10, 80), (85, 70), (40, 12)) ? 220 : null);
            case "grid":
                return Fill(96, 96, 230, (x, y) => (x % 24) is >= 10 and < 13 || (y % 32) is >= 14 and < 17 ? 20 : null);
            default:
                // A rectangle of 60 x 40 rotated around the center
                var (sin, cos) = Math.SinCos(double.Parse(scene["rotated-".Length..], CultureInfo.InvariantCulture) * Math.PI / 180);
                return Fill(96, 96, 35, (x, y) =>
                {
                    var dx = x + 0.5 - 48;
                    var dy = y + 0.5 - 48;
                    return Math.Abs((dx * cos) + (dy * sin)) <= 30 && Math.Abs((-dx * sin) + (dy * cos)) <= 20 ? 225 : null;
                });
        }
    }

    private static OpenCvGrayImage CreateCornerScene(string scene)
    {
        switch (scene)
        {
            case "square":
                return Fill(40, 40, 0, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 30 ? 255 : null);
            case "checkerboard":
                return Fill(80, 80, 128, (x, y) => x is >= 8 and < 72 && y is >= 8 and < 72 ? (((x - 8) / 8) + ((y - 8) / 8)) % 2 == 0 ? 230 : 25 : null);
            case "rotated-square":
                var (sin, cos) = Math.SinCos(30 * Math.PI / 180);
                return Fill(80, 80, 40, (x, y) =>
                {
                    var dx = x + 0.5 - 40;
                    var dy = y + 0.5 - 40;
                    return Math.Abs((dx * cos) + (dy * sin)) <= 18 && Math.Abs((-dx * sin) + (dy * cos)) <= 18 ? 200 : null;
                });
            case "rectangles":
            case "noise-rectangles":
                // Seeded rectangles of random sizes and levels, one per cell of 24 x 24 pixels, at least 5 pixels apart (closer
                // corners make the box and the Gaussian windows keep different ones of a cluster), and seeded noise of ±6 levels
                var random = new Random(scene.Length);
                var image = Fill(96, 96, 90, (x, y) => null);
                for (var cell = 0; cell < 16; cell++)
                {
                    var width = random.Next(6, 19);
                    var height = random.Next(6, 19);
                    var left = ((cell % 4) * 24) + random.Next(3, 24 - width - 1);
                    var top = ((cell / 4) * 24) + random.Next(3, 24 - height - 1);
                    var level = (byte)random.Next(0, 256);
                    for (var y = top; y < top + height; y++)
                    {
                        for (var x = left; x < left + width; x++)
                        {
                            image[x, y] = level;
                        }
                    }
                }

                if (scene == "noise-rectangles")
                {
                    for (var i = 0; i < image.Samples.Length; i++)
                    {
                        image.Samples[i] = (byte)Math.Clamp(image.Samples[i] + random.Next(-6, 7), 0, 255);
                    }
                }

                return image;
            default:
                throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown scene.");
        }
    }

    private static (OpenCvGrayImage Image, OpenCvGrayImage Mask) CreateInpaintScene(string scene)
    {
        const int Size = 96;
        var image = scene switch
        {
            "gradient-blob" or "gradient-scratches" => Fill(Size, Size, 0, (x, y) => 40 + x + (y / 2)),
            "edge-blob" => Fill(Size, Size, 0, (x, y) => x + (0.3 * y) < 55 ? 60 : 190),
            "texture-text" => Fill(Size, Size, 0, (x, y) => (int)(128 + (50 * Math.Sin(x * 0.3)) + (40 * Math.Cos(y * 0.23)))),
            "rings-square" => Fill(Size, Size, 0, (x, y) => (int)(128 + (90 * Math.Sin(Math.Sqrt(((x - 48) * (x - 48)) + ((y - 48) * (y - 48))) * 0.35)))),
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown scene."),
        };

        var mask = scene switch
        {
            "gradient-blob" or "edge-blob" => Fill(Size, Size, 0, (x, y) => ((x - 50) * (x - 50)) + ((y - 44) * (y - 44)) <= 100 ? 255 : null),
            "gradient-scratches" => Fill(Size, Size, 0, (x, y) => Math.Abs(y - (0.4 * x) - 20) < 1.5 || (Math.Abs(x - 70) < 1 && y is > 10 and < 85) ? 255 : null),
            "texture-text" => Fill(Size, Size, 0, (x, y) => y is >= 40 and < 47 && x is > 12 and < 84 && (x % 9) < 6 ? 255 : null),
            "rings-square" => Fill(Size, Size, 0, (x, y) => x is >= 40 and < 56 && y is >= 40 and < 56 ? 255 : null),
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown scene."),
        };

        return (image, mask);
    }

    /// <summary>A page of lines of words, dark on light, drawn rotated clockwise by an angle (y down), and its ink as 255 on 0.</summary>
    private static OpenCvGrayImage CreateDocument(int width, int height, double angle, out OpenCvGrayImage ink)
    {
        var (sin, cos) = Math.SinCos(angle * Math.PI / 180);
        var page = Fill(width, height, 0, (x, y) =>
        {
            // The center of the pixel rotated back around the center of the page
            var dx = x + 0.5 - (width / 2.0);
            var dy = y + 0.5 - (height / 2.0);
            var u = (dx * cos) + (dy * sin) + (width / 2.0);
            var v = (-dx * sin) + (dy * cos) + (height / 2.0);
            if (u < 40 || u >= width - 40 || v < 40 || v >= height - 40)
                return null;

            return (v - 40) % 16 < 7 && (u - 40) % 29 < 23 ? 255 : null;
        });

        ink = page;
        return Fill(width, height, 245, (x, y) => page[x, y] != 0 ? 25 : null);
    }

    private static OpenCvGrayImage Fill(int width, int height, int background, Func<int, int, double?> value)
    {
        var image = new OpenCvGrayImage(width, height, new byte[width * height]);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = (byte)Math.Clamp(value(x, y) ?? background, 0, 255);
            }
        }

        return image;
    }

    private static bool IsInsideTriangle(double x, double y, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        var ab = Cross(a, b, x, y);
        var bc = Cross(b, c, x, y);
        var ca = Cross(c, a, x, y);
        return (ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0);

        static double Cross((double X, double Y) p, (double X, double Y) q, double x, double y) => ((q.X - p.X) * (y - p.Y)) - ((q.Y - p.Y) * (x - p.X));
    }

    private static Image<Gray8> ToImage(OpenCvGrayImage source)
    {
        var image = new Image<Gray8>(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                image.Frames[0][x, y] = new Gray8(source[x, y]);
            }
        }

        return image;
    }

    private static OpenCvGrayImage FromImage(Image<Gray8> image)
    {
        var result = new OpenCvGrayImage(image.Width, image.Height, new byte[image.Width * image.Height]);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                result[x, y] = image.Frames[0][x, y].Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Seeded correspondences of a homography: the inliers moved by Gaussian noise, then the outliers moved 30 to 90 pixels
    /// away. Every coordinate is a multiple of 1/1024, which a float holds exactly: OpenCV reads the points as floats.
    /// </summary>
    private static (PointD[] Source, PointD[] Destination) CreateCorrespondences(ProjectiveTransform truth, int inliers, double noise, int outliers, int seed)
    {
        var random = new Random(seed);
        var source = new PointD[inliers + outliers];
        var destination = new PointD[inliers + outliers];
        for (var i = 0; i < source.Length; i++)
        {
            var point = new PointD(Quantize(random.NextDouble() * 200), Quantize(random.NextDouble() * 150));
            Assert.True(truth.TryTransform(point, out var image));
            if (i < inliers)
            {
                image = new PointD(image.X + (noise * Gaussian(random)), image.Y + (noise * Gaussian(random)));
            }
            else
            {
                var (sin, cos) = Math.SinCos(random.NextDouble() * 2 * Math.PI);
                var distance = 30 + (random.NextDouble() * 60);
                image = new PointD(image.X + (distance * cos), image.Y + (distance * sin));
            }

            source[i] = point;
            destination[i] = new PointD(Quantize(image.X), Quantize(image.Y));
        }

        return (source, destination);
    }

    private static double Quantize(double value) => Math.Round(value * 1024, MidpointRounding.AwayFromZero) / 1024;

    private static double Gaussian(Random random)
    {
        var u = 1 - random.NextDouble();
        var v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    private static (double X, double Y, double U, double V)[] ToCorrespondences(PointD[] source, PointD[] destination)
        => [.. source.Select((point, i) => (point.X, point.Y, destination[i].X, destination[i].Y))];

    /// <summary>The OpenCV matrix maps column vectors, the library's row vectors: one is the transpose of the other.</summary>
    private static ProjectiveTransform FromOpenCv(double[] h) => new(h[0], h[3], h[6], h[1], h[4], h[7], h[2], h[5], h[8]);

    private static double RootMeanSquareError(ProjectiveTransform transform, ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination)
    {
        var sum = 0.0;
        for (var i = 0; i < source.Length; i++)
        {
            Assert.True(transform.TryTransform(source[i], out var image));
            sum += ((image.X - destination[i].X) * (image.X - destination[i].X)) + ((image.Y - destination[i].Y) * (image.Y - destination[i].Y));
        }

        return Math.Sqrt(sum / source.Length);
    }

    private static double MaxDistance(ProjectiveTransform first, ProjectiveTransform second, ReadOnlySpan<PointD> points)
    {
        var max = 0.0;
        foreach (var point in points)
        {
            Assert.True(first.TryTransform(point, out var a));
            Assert.True(second.TryTransform(point, out var b));
            max = Math.Max(max, Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y))));
        }

        return max;
    }
}
