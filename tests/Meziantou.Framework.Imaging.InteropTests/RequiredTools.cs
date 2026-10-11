using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The pinned tools, downloaded from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package: a tool that cannot be downloaded or
/// reports an unexpected version fails the test, it is never skipped.
/// </summary>
internal static class RequiredTools
{
    public static Task<FFmpegTool> FFmpegAsync() => FFmpegTool.LocateAsync(XunitCancellationToken);

    public static Task<LibWebPTool> LibWebPAsync() => LibWebPTool.LocateAsync(XunitCancellationToken);

    /// <summary>
    /// Gets the <c>swift</c> executable and the Apple ImageIO helper script (<c>tools/Meziantou.Framework.Imaging.CorpusGenerator/imageio_frames.swift</c>).
    /// Apple ImageIO is part of macOS and cannot be downloaded: the cases are skipped on other systems, unless
    /// <c>MEZIANTOU_FRAMEWORK_IMAGING_IMAGEIO_REQUIRED=true</c> (set by the CI interop job) makes them fail. A missing <c>swift</c> on
    /// macOS always fails.
    /// </summary>
    public static (string Swift, string Script) AppleImageIO()
    {
        if (!OperatingSystem.IsMacOS())
        {
            if (InteropSettings.IsImageIORequired)
                throw new ExternalToolUnavailableException($"Apple ImageIO is required ({InteropSettings.ImageIORequiredVariable}=true) but is only available on macOS.");

            Assert.XunitSkip($"Apple ImageIO is only available on macOS; set {InteropSettings.ImageIORequiredVariable}=true to fail instead of skipping.");
        }

        var swift = ExecutableFinder.GetFullExecutablePath("swift") ?? throw new ExternalToolUnavailableException("swift (Xcode command line tools) is required to run the Apple ImageIO helper.");
        return (swift, FullPath.FromPath(AppContext.BaseDirectory) / "imageio_frames.swift");
    }
}
