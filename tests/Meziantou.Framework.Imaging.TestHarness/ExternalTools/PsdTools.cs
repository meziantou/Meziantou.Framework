using System.Globalization;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The independent readers and writers of PSD files of the pinned <c>Meziantou.Prebuilt</c> release: ImageMagick
/// (<c>magick</c>, its PSD coder reads and writes RGB, CMYK, grayscale, indexed and bitmap documents of 1, 8 and 16 bits,
/// raw and PackBits, PSB, and reads 32-bit floats), psd-tools (<c>psd-tools write</c> and <c>psd-tools dump</c>, the stored
/// samples of every color mode, depth and compression) and OpenImageIO (<c>oiiotool</c>, which converts every color mode to
/// RGB or gray, raw and PackBits composites only).
/// </summary>
/// <remarks>
/// ImageMagick needs <c>-endian MSB</c> after its raw inputs (they are little-endian for it, and its PSD coder then writes the
/// blend mode of the layer backwards); it un-mattes the colors of a composite with a transparency channel from white unless
/// <c>-define psd:alpha-unblend=off</c>, but its own writer does not matte them.
/// </remarks>
public sealed class PsdTools
{
    private PsdTools(string magick, string psdTools, string oiiotool)
    {
        MagickPath = magick;
        PsdToolsPath = psdTools;
        OiioToolPath = oiiotool;
    }

    /// <summary>Gets the ImageMagick version the package declares.</summary>
    public static string MagickVersion => PrebuiltTools.Magick.Version;

    public string MagickPath { get; }

    public string PsdToolsPath { get; }

    public string OiioToolPath { get; }

    /// <summary>Downloads the tools (cached and checksum-verified) and checks that they run.</summary>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run.</exception>
    public static async Task<PsdTools> LocateAsync(CancellationToken cancellationToken)
    {
        var magick = await PrebuiltTools.Magick.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var psdTools = await PrebuiltTools.PsdTools.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var oiiotool = await PrebuiltTools.OiioTool.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (tool, arguments, expected) in new[] { (magick, "-version", "ImageMagick"), (psdTools, "--version", PrebuiltTools.PsdTools.Version), (oiiotool, "--version", "") })
        {
            var result = await ProcessRunner.RunAsync(tool, [arguments], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || !result.StandardOutputText.Contains(expected, StringComparison.Ordinal))
                throw new ExternalToolUnavailableException($"'{tool}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");
        }

        return new PsdTools(magick, psdTools, oiiotool);
    }

    /// <summary>Runs <c>magick</c> with arguments.</summary>
    public Task MagickAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => RunAsync(MagickPath, arguments, cancellationToken);

    /// <summary>Decodes the composite of a file (<c>[0]</c>) with ImageMagick to straight RGBA samples of 8 or 16 bits.</summary>
    /// <param name="path">The file.</param>
    /// <param name="depth">8 or 16.</param>
    /// <param name="unblend">Whether ImageMagick un-mattes the colors of a transparency channel (its default).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async Task<byte[]> MagickDecodeAsync(FullPath path, int depth, bool unblend, CancellationToken cancellationToken)
    {
        List<string> arguments = [.. unblend ? Array.Empty<string>() : ["-define", "psd:alpha-unblend=off"], path + "[0]", "-depth", depth.ToString(CultureInfo.InvariantCulture), "-endian", "LSB", "rgba:-"];
        var result = await RunAsync(MagickPath, arguments, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.ToArray();
    }

    /// <summary>Writes a file with <c>psd-tools write</c> from a description (header.json, see the bundle).</summary>
    public Task PsdToolsWriteAsync(FullPath description, FullPath output, CancellationToken cancellationToken) => RunAsync(PsdToolsPath, ["write", description, output], cancellationToken);

    /// <summary>Gives the stored samples of the composite (<c>psd-tools dump --no-layers</c>): composite.raw, interleaved, little-endian.</summary>
    public async Task<byte[]> PsdToolsDumpAsync(FullPath path, FullPath folder, CancellationToken cancellationToken)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        await RunAsync(PsdToolsPath, ["dump", "--no-layers", path, folder], cancellationToken).ConfigureAwait(false);
        return await File.ReadAllBytesAsync(folder / "composite.raw", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Dumps the composite and the layers of a file (<c>psd-tools dump</c>): header.json, composite.raw and layer-<i>n</i>.raw.</summary>
    public Task PsdToolsDumpAllAsync(FullPath path, FullPath folder, CancellationToken cancellationToken) => RunAsync(PsdToolsPath, ["dump", path, folder], cancellationToken);

    /// <summary>
    /// Converts the composite image of a file (the next subimages are the layers) with OpenImageIO to a TIFF file of a data type
    /// (<c>uint8</c>, <c>uint16</c>, <c>float</c>), with straight alpha: OpenImageIO removes the matte of a PSD composite into
    /// premultiplied colors, which <c>--unpremult</c> divides, and the library does not read associated alpha.
    /// </summary>
    public async Task OiioToTiffAsync(FullPath path, string dataType, FullPath output, CancellationToken cancellationToken)
    {
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        await RunAsync(OiioToolPath, [path, "--subimage", "0", "--unpremult", "-d", dataType, "--attrib", "oiio:UnassociatedAlpha", "1", "-o", output], cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'{Path.GetFileName(tool)} {string.Join(' ', arguments)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return result;
    }
}
