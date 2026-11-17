using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// <c>opencv-oracle</c>, a program of meziantou/prebuilt built with OpenCV (one thread, no IPP, no OpenCL): it runs one
/// OpenCV function on an image or a set of correspondences and prints the result as JSON.
/// </summary>
/// <remarks>
/// The images are passed as raw 8-bit samples (<c>raw:&lt;width&gt;:&lt;height&gt;:&lt;channels&gt;:&lt;file&gt;</c>), gray for the
/// line and corner functions. OpenCV reads the correspondences of <c>homography</c> as 32-bit floats: give it values that
/// floats hold exactly.
/// </remarks>
public sealed class OpenCvOracleTool
{
    private static int s_fileIndex;

    private OpenCvOracleTool(string path, FullPath workDirectory)
    {
        ToolPath = path;
        WorkDirectory = workDirectory;
    }

    /// <summary>Gets the OpenCV version the package declares.</summary>
    public static string Version => PrebuiltTools.OpenCvOracle.Version;

    public string ToolPath { get; }

    /// <summary>Gets the directory of the input and output files of the commands.</summary>
    public FullPath WorkDirectory { get; }

    /// <summary>
    /// Downloads the tool (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that it runs and reports the pinned OpenCV version.
    /// </summary>
    /// <exception cref="ExternalToolUnavailableException">The tool does not run or reports another version.</exception>
    public static async Task<OpenCvOracleTool> LocateAsync(FullPath workDirectory, CancellationToken cancellationToken)
    {
        var path = await PrebuiltTools.OpenCvOracle.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(path, ["--version"], cancellationToken).ConfigureAwait(false);
        var expected = "opencv-oracle (OpenCV " + Version + ")";
        if (result.ExitCode != 0 || result.StandardOutputText.Trim() != expected)
            throw new ExternalToolUnavailableException($"'{path} --version' printed '{result.StandardOutputText.Trim()}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}), not '{expected}': {result.StandardError}");

        Directory.CreateDirectory(workDirectory);
        return new OpenCvOracleTool(path, workDirectory);
    }

    /// <summary><c>cv::HoughLines</c>: the lines <c>(rho, theta in radians, votes)</c>, sorted by votes, of the cells with more than <paramref name="threshold"/> votes.</summary>
    public async Task<IReadOnlyList<(double Rho, double Theta, int Votes)>> HoughLinesAsync(OpenCvGrayImage image, double rho, double theta, int threshold, CancellationToken cancellationToken)
    {
        using var json = await RunJsonAsync(["hough-lines", await WriteAsync(image, cancellationToken).ConfigureAwait(false), Format(rho), Format(theta), Format(threshold)], cancellationToken).ConfigureAwait(false);
        return [.. json.RootElement.GetProperty("lines").EnumerateArray().Select(line => (line[0].GetDouble(), line[1].GetDouble(), (int)line[2].GetDouble()))];
    }

    /// <summary><c>cv::HoughLinesP</c>: the segments <c>(x1, y1, x2, y2)</c>.</summary>
    public async Task<IReadOnlyList<(int X1, int Y1, int X2, int Y2)>> HoughLinesPAsync(OpenCvGrayImage image, double rho, double theta, int threshold, double minLineLength, double maxLineGap, CancellationToken cancellationToken)
    {
        using var json = await RunJsonAsync(["hough-lines-p", await WriteAsync(image, cancellationToken).ConfigureAwait(false), Format(rho), Format(theta), Format(threshold), Format(minLineLength), Format(maxLineGap)], cancellationToken).ConfigureAwait(false);
        return [.. json.RootElement.GetProperty("lines").EnumerateArray().Select(line => (line[0].GetInt32(), line[1].GetInt32(), line[2].GetInt32(), line[3].GetInt32()))];
    }

    /// <summary><c>cv::cornerHarris</c>: the response of every pixel, row by row.</summary>
    public async Task<float[]> HarrisAsync(OpenCvGrayImage image, int blockSize, int apertureSize, double k, CancellationToken cancellationToken)
    {
        var output = NextPath("response.raw");
        using var json = await RunJsonAsync(["harris", await WriteAsync(image, cancellationToken).ConfigureAwait(false), Format(blockSize), Format(apertureSize), Format(k), output], cancellationToken).ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
        if (bytes.Length != image.Width * image.Height * 4)
            throw new InvalidOperationException($"opencv-oracle harris wrote {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes, not {(image.Width * image.Height * 4).ToString(CultureInfo.InvariantCulture)}.");

        var response = new float[image.Width * image.Height];
        for (var i = 0; i < response.Length; i++)
        {
            response[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }

        return response;
    }

    /// <summary><c>cv::goodFeaturesToTrack</c>: the corners, the strongest first.</summary>
    public async Task<IReadOnlyList<(double X, double Y)>> GoodFeaturesAsync(OpenCvGrayImage image, int maxCorners, double qualityLevel, double minDistance, int blockSize, bool useHarris, double k, CancellationToken cancellationToken)
    {
        using var json = await RunJsonAsync(["good-features", await WriteAsync(image, cancellationToken).ConfigureAwait(false), Format(maxCorners), Format(qualityLevel), Format(minDistance), Format(blockSize), useHarris ? "1" : "0", Format(k)], cancellationToken).ConfigureAwait(false);
        return [.. json.RootElement.GetProperty("corners").EnumerateArray().Select(corner => (corner[0].GetDouble(), corner[1].GetDouble()))];
    }

    /// <summary><c>cv::FAST</c>: the keypoints <c>(x, y, response)</c> of the segment test <c>9_16</c>, <c>7_12</c> or <c>5_8</c>.</summary>
    public async Task<IReadOnlyList<(int X, int Y, double Response)>> FastAsync(OpenCvGrayImage image, int threshold, bool nonmaxSuppression, string type, CancellationToken cancellationToken)
    {
        using var json = await RunJsonAsync(["fast", await WriteAsync(image, cancellationToken).ConfigureAwait(false), Format(threshold), nonmaxSuppression ? "1" : "0", type], cancellationToken).ConfigureAwait(false);
        return [.. json.RootElement.GetProperty("keypoints").EnumerateArray().Select(point => ((int)point[0].GetDouble(), (int)point[1].GetDouble(), point[2].GetDouble()))];
    }

    /// <summary>
    /// <c>cv::findHomography</c> with the method <c>dlt</c> (0), <c>ransac</c>, <c>lmeds</c> or <c>rho</c>, after
    /// <c>cv::setRNGSeed(seed)</c>: the matrix row by row (or <see langword="null"/>) and the inlier mask.
    /// </summary>
    public async Task<(double[]? Homography, bool[] Inliers)> FindHomographyAsync(ReadOnlyMemory<(double X, double Y, double U, double V)> correspondences, string method, double reprojectionThreshold, int seed, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var (x, y, u, v) in correspondences.Span)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{Format(x)} {Format(y)} {Format(u)} {Format(v)}\n");
        }

        var points = NextPath("points.txt");
        await File.WriteAllTextAsync(points, builder.ToString(), cancellationToken).ConfigureAwait(false);
        using var json = await RunJsonAsync(["homography", points, method, Format(reprojectionThreshold), Format(seed)], cancellationToken).ConfigureAwait(false);
        var homography = json.RootElement.GetProperty("homography");
        double[]? matrix = homography.ValueKind == JsonValueKind.Null ? null : [.. homography.EnumerateArray().Select(value => value.GetDouble())];
        return (matrix, [.. json.RootElement.GetProperty("inliers").EnumerateArray().Select(value => value.GetInt32() != 0)]);
    }

    /// <summary><c>cv::inpaint</c> with <c>telea</c> or <c>ns</c> of an image where the mask is not 0.</summary>
    public async Task<OpenCvGrayImage> InpaintAsync(OpenCvGrayImage image, OpenCvGrayImage mask, double radius, string method, CancellationToken cancellationToken)
    {
        var output = NextPath("inpainted.raw");
        using var json = await RunJsonAsync(["inpaint", await WriteAsync(image, cancellationToken).ConfigureAwait(false), await WriteAsync(mask, cancellationToken).ConfigureAwait(false), Format(radius), method, "raw:" + output], cancellationToken).ConfigureAwait(false);
        var samples = await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
        if (samples.Length != image.Width * image.Height)
            throw new InvalidOperationException($"opencv-oracle inpaint wrote {samples.Length.ToString(CultureInfo.InvariantCulture)} bytes, not {(image.Width * image.Height).ToString(CultureInfo.InvariantCulture)}.");

        return new OpenCvGrayImage(image.Width, image.Height, samples);
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private FullPath NextPath(string name) => WorkDirectory / (Interlocked.Increment(ref s_fileIndex).ToString(CultureInfo.InvariantCulture) + "-" + name);

    private async Task<string> WriteAsync(OpenCvGrayImage image, CancellationToken cancellationToken)
    {
        var path = NextPath("image.raw");
        await File.WriteAllBytesAsync(path, image.Samples, cancellationToken).ConfigureAwait(false);
        return string.Create(CultureInfo.InvariantCulture, $"raw:{image.Width}:{image.Height}:1:{path}");
    }

    private async Task<JsonDocument> RunJsonAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ToolPath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'opencv-oracle {string.Join(' ', arguments)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return JsonDocument.Parse(result.StandardOutputText);
    }
}
