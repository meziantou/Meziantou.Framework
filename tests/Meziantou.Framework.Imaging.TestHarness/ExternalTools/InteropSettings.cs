namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// Environment-driven settings of the external-tool interoperability tests.
/// </summary>
/// <remarks>
/// The tools themselves are not configurable: they are downloaded from the release pinned by the <c>Meziantou.Prebuilt</c>
/// package (<see cref="FFmpegTool.LocateAsync"/>, <see cref="LibWebPTool.LocateAsync"/>).
/// <list type="bullet">
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_IMAGEIO_REQUIRED=true</c>: the Apple ImageIO cases must run; on systems other than macOS they fail instead of skipping. Set by the CI interop job (macOS).</description></item>
/// <item><description><c>MEZIANTOU_FRAMEWORK_IMAGING_INTEROP_ARTIFACTS</c>: directory where files produced by interop tests are kept for diagnosis (uploaded by CI).</description></item>
/// </list>
/// </remarks>
public static class InteropSettings
{
    public const string ArtifactsVariable = "MEZIANTOU_FRAMEWORK_IMAGING_INTEROP_ARTIFACTS";
    public const string ImageIORequiredVariable = "MEZIANTOU_FRAMEWORK_IMAGING_IMAGEIO_REQUIRED";

    /// <summary>Gets a value indicating whether the Apple ImageIO cases must run (CI interop job on macOS): they then fail instead of skipping on other systems.</summary>
    public static bool IsImageIORequired => IsTrue(Environment.GetEnvironmentVariable(ImageIORequiredVariable));

    /// <summary>Gets the directory where interop outputs are kept. Created on demand.</summary>
    /// <param name="testName">A file-system-safe name for the test.</param>
    /// <returns>A directory dedicated to the test.</returns>
    public static FullPath GetArtifactsDirectory(string testName)
    {
        ArgumentException.ThrowIfNullOrEmpty(testName);
        var root = NullIfEmpty(Environment.GetEnvironmentVariable(ArtifactsVariable)) is { } value ? FullPath.FromPath(value) : FullPath.FromPath(AppContext.BaseDirectory) / "InteropOutput";
        var path = root / testName;
        Directory.CreateDirectory(path);
        return path;
    }

    internal static bool IsTrue(string? value) => value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
