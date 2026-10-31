using Meziantou.Framework.Imaging.TestHarness;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The pinned tools, downloaded from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package: a tool that cannot be downloaded or
/// reports an unexpected version fails the test, it is never skipped.
/// </summary>
internal static class RequiredTools
{
    public static Task<FFmpegTool> FFmpegAsync() => FFmpegTool.LocateAsync(XunitCancellationToken);

    public static Task<LibWebPTool> LibWebPAsync() => LibWebPTool.LocateAsync(XunitCancellationToken);

    public static Task<LibAvifTool> LibAvifAsync() => LibAvifTool.LocateAsync(XunitCancellationToken);

    /// <summary>Gets the libheif tools (<c>heif-enc</c> with x265, <c>heif-dec</c> with libde265, <c>heif-info</c>).</summary>
    public static Task<LibHeifTool> LibHeifAsync() => LibHeifTool.LocateAsync(XunitCancellationToken);

    public static Task<LibJxlTool> LibJxlAsync() => LibJxlTool.LocateAsync(XunitCancellationToken);

    public static Task<JxlOxideTool> JxlOxideAsync() => JxlOxideTool.LocateAsync(XunitCancellationToken);

    /// <summary>Gets the three HEVC reference decoders: the native decoder of FFmpeg, dec265 and TAppDecoder of HM.</summary>
    public static Task<IReadOnlyList<HevcReferenceDecoder>> HevcReferenceDecodersAsync() => HevcReferenceDecoder.LocateAllAsync(XunitCancellationToken);

    /// <summary>Gets an HEVC encoder: <see cref="HevcReferenceEncoder.X265"/>, <see cref="HevcReferenceEncoder.Kvazaar"/> or <see cref="HevcReferenceEncoder.Hm"/>.</summary>
    public static Task<HevcReferenceEncoder> HevcReferenceEncoderAsync(string name) => HevcReferenceEncoder.LocateAsync(name, XunitCancellationToken);

    public static Task<OpenJpegTool> OpenJpegAsync() => OpenJpegTool.LocateAsync(XunitCancellationToken);

    public static Task<OpenJphTool> OpenJphAsync() => OpenJphTool.LocateAsync(XunitCancellationToken);

    public static Task<OpenExrTool> OpenExrAsync() => OpenExrTool.LocateAsync(XunitCancellationToken);

    /// <summary>Gets the <c>jpeg</c> tool of the ISO JPEG reference codec, which shares no code with libjpeg-turbo.</summary>
    public static Task<IsoJpegTool> IsoJpegAsync() => IsoJpegTool.LocateAsync(XunitCancellationToken);

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
        // The Apple ImageIO frame dumper is shared with the corpus generator
        return (swift, RepositoryPaths.Root / "tools" / "Meziantou.Framework.Imaging.CorpusGenerator" / "imageio_frames.swift");
    }

    /// <summary>Decodes the displayed frames of an 8-bit animation with Apple ImageIO (<see cref="AppleImageIO"/>).</summary>
    /// <param name="path">The file.</param>
    /// <param name="width">The canvas width.</param>
    /// <param name="height">The canvas height.</param>
    /// <param name="binaryAlpha">Whether every pixel is opaque or fully transparent (GIF), which accepts more decoded layouts.</param>
    /// <returns>The displayed frames, straight 8-bit RGBA, with the colors of fully transparent pixels cleared.</returns>
    public static async Task<IReadOnlyList<RawPixelBuffer>> DecodeWithAppleImageIOAsync(FullPath path, int width, int height, bool binaryAlpha)
    {
        var (swift, script) = AppleImageIO();
        var result = await ProcessRunner.RunAsync(swift, binaryAlpha ? [script, "--binary-alpha", path] : [script, path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed (exit code {result.ExitCode}): {result.StandardError}");

        var header = result.StandardError.Trim().Split(' ');
        Assert.Equal(["frames", width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture), "8"], [header[0], header[2], header[3], header[4]]);
        var count = int.Parse(header[1], CultureInfo.InvariantCulture);
        var frameLength = width * height * 4;
        Assert.Equal(count * frameLength, result.StandardOutput.Length);
        return [.. Enumerable.Range(0, count).Select(i => RawPixelBuffer.Create(width, height, RawPixelLayout.Rgba8, result.StandardOutput.Slice(i * frameLength, frameLength).Span).WithTransparentColorsCleared())];
    }
}
