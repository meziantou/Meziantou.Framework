using System.Globalization;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The DDS readers and writers of the pinned <c>Meziantou.Prebuilt</c> release: <c>oiiotool</c> of OpenImageIO (its DDS
/// reader, through the raw samples <c>--dumpdata</c> prints), ImageMagick (<c>magick</c>, which writes DXT1, DXT5 and
/// uncompressed files with mipmaps), and on Windows <c>texconv</c> of DirectXTex.
/// </summary>
/// <remarks>
/// The image outputs of OpenImageIO treat DDS alpha as premultiplied; <c>--dumpdata</c> prints the stored samples. Its BC1
/// to BC5 decoders use integer approximations (within one unit); BC6H and BC7 are exact.
/// </remarks>
public sealed class DdsReferenceTools
{
    private DdsReferenceTools(string oiiotool, string magick)
    {
        OiiotoolPath = oiiotool;
        MagickPath = magick;
    }

    public string OiiotoolPath { get; }

    public string MagickPath { get; }

    /// <summary>Downloads oiiotool and magick (cached and checksum-verified) and checks that they run.</summary>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run.</exception>
    public static async Task<DdsReferenceTools> LocateAsync(CancellationToken cancellationToken)
    {
        var oiiotool = await PrebuiltTools.OiioTool.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var magick = await PrebuiltTools.Magick.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (path, argument) in new[] { (oiiotool, "--version"), (magick, "-version") })
        {
            var result = await ProcessRunner.RunAsync(path, [argument], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new ExternalToolUnavailableException($"'{path}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");
        }

        return new DdsReferenceTools(oiiotool, magick);
    }

    /// <summary>Downloads texconv, the converter of DirectXTex, which only runs on Windows.</summary>
    public static Task<string> LocateTexconvAsync(CancellationToken cancellationToken) => PrebuiltTools.Texconv.GetOrDownloadAsync(cancellationToken);

    /// <summary>Gets the stored samples of the first surface of a file, as OpenImageIO prints them: four values per pixel (missing channels are 0, a missing alpha 1).</summary>
    public async Task<double[]> DumpAsync(FullPath path, int width, int height, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(OiiotoolPath, ["-v", "--info", "--dumpdata", path], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"oiiotool failed on '{path}' ({result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        var values = new double[width * height * 4];
        for (var i = 3; i < values.Length; i += 4)
            values[i] = 1;

        var count = 0;
        foreach (var line in result.StandardOutputText.Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith("Pixel (", StringComparison.Ordinal))
                continue;

            var close = text.IndexOf(')', StringComparison.Ordinal);
            var coordinates = text[7..close].Split(',');
            var x = int.Parse(coordinates[0], CultureInfo.InvariantCulture);
            var y = int.Parse(coordinates[1], CultureInfo.InvariantCulture);
            var samples = text[(close + 2)..];
            var paren = samples.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0)
                samples = samples[..paren];

            var parsed = samples.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var c = 0; c < parsed.Length && c < 4; c++)
                values[(((y * width) + x) * 4) + c] = ParseSample(parsed[c]);

            count++;
        }

        if (count != width * height)
            throw new InvalidOperationException($"oiiotool printed {count.ToString(CultureInfo.InvariantCulture)} pixels of '{path}'; {(width * height).ToString(CultureInfo.InvariantCulture)} were expected.");

        return values;
    }

    /// <summary>Reads a sample OpenImageIO prints: a number, or <c>inf</c>, <c>-inf</c> and <c>nan</c>.</summary>
    private static double ParseSample(string text) => text switch
    {
        "inf" => double.PositiveInfinity,
        "-inf" => double.NegativeInfinity,
        "nan" or "-nan" => double.NaN,
        _ => double.Parse(text, CultureInfo.InvariantCulture),
    };

    /// <summary>Writes 8-bit RGBA samples as a DDS file with ImageMagick (<c>dds:compression</c> dxt1, dxt5 or none).</summary>
    public async Task WriteAsync(ReadOnlyMemory<byte> rgba, int width, int height, string compression, int mipmaps, FullPath output, CancellationToken cancellationToken)
    {
        var input = output + ".rgba";
        await File.WriteAllBytesAsync(input, rgba.ToArray(), cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(MagickPath, ["-size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"), "-depth", "8", "rgba:" + input, "-define", "dds:compression=" + compression, "-define", "dds:mipmaps=" + mipmaps.ToString(CultureInfo.InvariantCulture), output], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"magick failed ({result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");
    }

    /// <summary>Converts a DDS file with texconv (Windows only), keeping its levels: <c>texconv -tu -m levels -f format</c>.</summary>
    /// <returns>The converted file.</returns>
    public static async Task<byte[]> TexconvAsync(string texconv, FullPath input, IEnumerable<string> options, FullPath outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = await ProcessRunner.RunAsync(texconv, ["-nologo", "-y", "-tu", .. options, "-o", outputDirectory, input], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"texconv failed on '{input}' ({result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        return await File.ReadAllBytesAsync(outputDirectory / input.Name, cancellationToken).ConfigureAwait(false);
    }
}
