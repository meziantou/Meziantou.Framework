using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The DPX and Cineon readers of OpenImageIO (<c>oiiotool</c>) and ImageMagick (<c>magick</c>), which give the code values
/// a file stores.
/// </summary>
/// <remarks>
/// <c>oiiotool -v --info --dumpdata</c> prints the pixels in the order of the file: 8 and 16-bit samples as they are, 10 and
/// 12-bit samples scaled to 16 bits by bit replication (the code value is the 16-bit sample shifted right), floating-point
/// samples with seven significant digits. <c>magick ... -set colorspace sRGB</c> keeps the code values of logarithmic data
/// and gives them as 16-bit samples, <c>round(v × 65535 / max)</c>.
/// </remarks>
public sealed class DpxTool
{
    private DpxTool(string oiioTool, string magick)
    {
        OiioToolPath = oiioTool;
        MagickPath = magick;
    }

    /// <summary>Gets the OpenImageIO version the package declares.</summary>
    public static string OiioVersion => PrebuiltTools.OiioTool.Version;

    /// <summary>Gets the ImageMagick version the package declares.</summary>
    public static string MagickVersion => PrebuiltTools.Magick.Version;

    public string OiioToolPath { get; }

    public string MagickPath { get; }

    /// <summary>Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package) and checks that they run.</summary>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run.</exception>
    public static async Task<DpxTool> LocateAsync(CancellationToken cancellationToken)
    {
        var oiioTool = await PrebuiltTools.OiioTool.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var magick = await PrebuiltTools.Magick.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(oiioTool, ["--version"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ExternalToolUnavailableException($"'{oiioTool}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        result = await ProcessRunner.RunAsync(magick, ["-version"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ExternalToolUnavailableException($"'{magick}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return new DpxTool(oiioTool, magick);
    }

    /// <summary>Reads the code values of a file with <c>oiiotool --dumpdata</c>, in the order of the channels OpenImageIO gives.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bits">The bit depth of its components (32 for floating point).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The code values, or the bits of the single-precision values.</returns>
    /// <exception cref="InvalidOperationException">oiiotool rejected the file.</exception>
    public async Task<uint[]> ReadWithOpenImageIOAsync(FullPath path, int bits, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(OiioToolPath, ["-v", "--info", "--dumpdata", path], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.StandardOutputText.Contains("Error", StringComparison.Ordinal))
            throw new InvalidOperationException($"oiiotool rejected '{path}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        var values = new List<uint>();
        foreach (var line in result.StandardOutputText.Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith("Pixel (", StringComparison.Ordinal))
                continue;

            text = text[(text.IndexOf(':', StringComparison.Ordinal) + 1)..];
            var parenthesis = text.IndexOf('(', StringComparison.Ordinal);
            if (parenthesis >= 0)
            {
                text = text[..parenthesis];
            }

            foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var value = double.Parse(token, CultureInfo.InvariantCulture);
                values.Add(bits >= 32 ? BitConverter.SingleToUInt32Bits((float)value) : bits is 10 or 12 ? (uint)value >> (16 - bits) : (uint)value);
            }
        }

        return [.. values];
    }

    /// <summary>Reads the code values of a file with ImageMagick, as 16-bit samples of the channels of <paramref name="map"/> (<c>gray</c>, <c>rgb</c>, <c>rgba</c>).</summary>
    /// <param name="path">The file.</param>
    /// <param name="map">The channels to read.</param>
    /// <param name="bits">The bit depth of the code values.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="colorspace">
    /// The colorspace set after reading, so that ImageMagick converts nothing: <c>sRGB</c>, or <c>Gray</c> for a gray Cineon
    /// file (with <c>sRGB</c>, ImageMagick still converts it from log).
    /// </param>
    /// <exception cref="InvalidOperationException">magick rejected the file.</exception>
    public async Task<uint[]> ReadWithImageMagickAsync(FullPath path, string map, int bits, CancellationToken cancellationToken, string colorspace = "sRGB")
    {
        var result = await ProcessRunner.RunAsync(MagickPath, [path, "-set", "colorspace", colorspace, "-depth", "16", "-endian", "LSB", map + ":-"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"magick rejected '{path}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        var data = result.StandardOutput.Span;
        var max = (1UL << bits) - 1;
        var values = new uint[data.Length / 2];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (uint)(((BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]) * max) + 32767) / 65535);
        }

        return values;
    }
}
