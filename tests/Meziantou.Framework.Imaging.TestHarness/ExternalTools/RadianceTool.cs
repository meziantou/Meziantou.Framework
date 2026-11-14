using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The tools of the Radiance distribution (<c>pvalue</c>, <c>getinfo</c>, <c>pflip</c>, <c>protate</c>, <c>ra_xyze</c>,
/// <c>ra_rgbe</c>), the reference implementation of the Radiance picture format.
/// </summary>
/// <remarks>
/// <c>pvalue -h -H -df</c> gives the colors of the pixels in the order of the scanlines of the file (not in the standard
/// orientation), read as <c>(m + 0.5) × 2^(e - 136)</c>. <c>pvalue -r</c> writes the new run-length encoding, or flat
/// scanlines with <c>-u</c> and for scanlines shorter than 17 or longer than 32,767 pixels; the order and the signs of its
/// <c>-y</c> and <c>+x</c> options are the resolution string it writes. <c>pflip</c> and <c>protate</c> without
/// <c>-c</c> rewrite the order of the scanlines and the resolution string so that the picture stays the same.
/// </remarks>
public sealed class RadianceTool
{
    private RadianceTool(string pvalue, string getInfo, string pflip, string protate, string raXyze, string raRgbe)
    {
        PvaluePath = pvalue;
        GetInfoPath = getInfo;
        PflipPath = pflip;
        ProtatePath = protate;
        RaXyzePath = raXyze;
        RaRgbePath = raRgbe;
    }

    /// <summary>Gets the Radiance version the package declares.</summary>
    public static string Version => PrebuiltTools.Pvalue.Version;

    public string PvaluePath { get; }

    public string GetInfoPath { get; }

    public string PflipPath { get; }

    public string ProtatePath { get; }

    public string RaXyzePath { get; }

    public string RaRgbePath { get; }

    /// <summary>
    /// Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that they run.
    /// </summary>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run.</exception>
    public static async Task<RadianceTool> LocateAsync(CancellationToken cancellationToken)
    {
        var pvalue = await PrebuiltTools.Pvalue.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var getInfo = await PrebuiltTools.RadianceGetInfo.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var pflip = await PrebuiltTools.Pflip.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var protate = await PrebuiltTools.Protate.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var raXyze = await PrebuiltTools.RaXyze.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var raRgbe = await PrebuiltTools.RaRgbe.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

        // Without arguments, getinfo reads its standard input, which is empty: it prints nothing and succeeds; pvalue -h
        // of a missing file fails with its usage or an error that names it
        var result = await ProcessRunner.RunAsync(pvalue, ["-h", "-H", "-df", "missing.hdr"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 || !result.StandardError.Contains("missing.hdr", StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{pvalue}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return new RadianceTool(pvalue, getInfo, pflip, protate, raXyze, raRgbe);
    }

    /// <summary>Decodes a picture with <c>pvalue -h -H -df</c>: the RGB colors of its pixels, in the order of its scanlines.</summary>
    /// <exception cref="InvalidOperationException">pvalue rejected the file.</exception>
    public async Task<float[]> DecodeAsync(FullPath path, CancellationToken cancellationToken)
    {
        var result = await RunAsync(PvaluePath, ["-h", "-H", "-df", path], cancellationToken).ConfigureAwait(false);
        var output = result.StandardOutput.Span;
        var colors = new float[output.Length / 4];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = BinaryPrimitives.ReadSingleLittleEndian(output[(i * 4)..]);
        }

        return colors;
    }

    /// <summary>Gets the resolution string of a picture (<c>getinfo -d</c>), its tokens separated by one space, such as <c>-Y 2 +X 3</c>.</summary>
    public async Task<string> GetResolutionAsync(FullPath path, CancellationToken cancellationToken)
    {
        var result = await RunAsync(GetInfoPath, ["-d", path], cancellationToken).ConfigureAwait(false);
        var text = result.StandardOutputText.Trim();

        // getinfo -d prints "<path>: -Y      2 +X      3", the sizes padded
        var colon = text.LastIndexOf(": ", StringComparison.Ordinal);
        return string.Join(' ', (colon < 0 ? text : text[(colon + 2)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Writes a picture with <c>pvalue -r</c> from RGB colors in the order of its scanlines.
    /// </summary>
    /// <param name="colors">Three floats per pixel, in the order of the scanlines.</param>
    /// <param name="scanOrder">The orientation of the resolution string, such as <c>-Y +X</c>.</param>
    /// <param name="width">The size of the X axis.</param>
    /// <param name="height">The size of the Y axis.</param>
    /// <param name="flat">Whether every scanline is flat (<c>-u</c>).</param>
    /// <param name="xyz">Whether the picture is XYZE (<c>-pXYZ</c>).</param>
    /// <param name="output">The file to write.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async Task EncodeAsync(ReadOnlyMemory<float> colors, string scanOrder, int width, int height, bool flat, bool xyz, FullPath output, CancellationToken cancellationToken)
    {
        var input = new byte[colors.Length * 4];
        for (var i = 0; i < colors.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(input.AsSpan(i * 4), colors.Span[i]);
        }

        var source = output + ".f32";
        await File.WriteAllBytesAsync(source, input, cancellationToken).ConfigureAwait(false);
        List<string> arguments = ["-r", "-h", "-df"];
        if (flat)
        {
            arguments.Add("-u");
        }

        if (xyz)
        {
            arguments.Add("-pXYZ");
        }

        foreach (var axis in scanOrder.Split(' '))
        {
            // "-Y" is "-y", "+X" is "+x"
            arguments.Add(axis.ToLowerInvariant());
            arguments.Add((axis[1] == 'X' ? width : height).ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add(source);
        var result = await RunAsync(PvaluePath, arguments, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(output, result.StandardOutput.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rewrites a picture with <c>pflip</c> (without <c>-c</c>: the picture stays the same, its storage changes).</summary>
    public Task FlipAsync(FullPath input, FullPath output, bool horizontal, bool vertical, CancellationToken cancellationToken)
        => RunToFileAsync(PflipPath, [.. horizontal ? ["-h"] : Array.Empty<string>(), .. vertical ? ["-v"] : Array.Empty<string>(), input, output], output, cancellationToken);

    /// <summary>Rewrites a picture with <c>protate</c> (without <c>-c</c>: the picture stays the same, its storage changes).</summary>
    public Task RotateAsync(FullPath input, FullPath output, bool counterClockwise, CancellationToken cancellationToken)
        => RunToFileAsync(ProtatePath, [.. counterClockwise ? ["-r"] : Array.Empty<string>(), input, output], output, cancellationToken);

    /// <summary>Converts a picture to flat scanlines with <c>ra_rgbe</c>.</summary>
    public async Task ToFlatAsync(FullPath input, FullPath output, CancellationToken cancellationToken)
    {
        var result = await RunAsync(RaRgbePath, [input], cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(output, result.StandardOutput.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Converts an RGBE picture to a run-length encoded XYZE one with <c>ra_xyze -c</c>.</summary>
    public async Task ToXyzeAsync(FullPath input, FullPath output, CancellationToken cancellationToken)
    {
        var result = await RunAsync(RaXyzePath, ["-c", input], cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(output, result.StandardOutput.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunToFileAsync(string tool, IReadOnlyList<string> arguments, FullPath output, CancellationToken cancellationToken)
    {
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        await RunAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(output))
            throw new InvalidOperationException($"'{tool}' did not write '{output}'.");
    }

    private static async Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'{Path.GetFileName(tool)} {string.Join(' ', arguments)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return result;
    }
}
