using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.Dng;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The reference implementations of DNG: <c>dng_validate</c> of the Adobe DNG SDK (its stage images and its rendering, and
/// the DNG files it rewrites) and <c>unprocessed_raw</c> of LibRaw (the stored samples of a color filter array).
/// </summary>
/// <remarks>
/// <c>dng_validate -1/-2/-3 name</c> writes the stages of its pipeline as uncompressed little-endian TIFF files: the stored
/// samples, the linear values of the active area (16-bit integers, or floats for floating-point samples) and the demosaiced
/// image; <c>-32 -tif name</c> writes its rendering, sRGB-encoded floats. Its outputs embed the time: they are never compared
/// byte for byte. <c>unprocessed_raw file</c> writes <c>file.pgm</c>, the samples of a color filter array as stored, in a
/// 16-bit big-endian PGM file; LibRaw reads no image smaller than 22 pixels.
/// </remarks>
public sealed class DngSdkTool
{
    private DngSdkTool(string dngValidate, string unprocessedRaw)
    {
        DngValidatePath = dngValidate;
        UnprocessedRawPath = unprocessedRaw;
    }

    /// <summary>Gets the version of the DNG SDK the package declares.</summary>
    public static string Version => PrebuiltTools.DngValidate.Version;

    public string DngValidatePath { get; }

    public string UnprocessedRawPath { get; }

    /// <summary>Downloads the tools (pinned by the <c>Meziantou.Prebuilt</c> package, cached and checksum-verified) and checks that they run.</summary>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run.</exception>
    public static async Task<DngSdkTool> LocateAsync(CancellationToken cancellationToken)
    {
        var dngValidate = await PrebuiltTools.DngValidate.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var unprocessedRaw = await PrebuiltTools.UnprocessedRaw.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(dngValidate, [], cancellationToken).ConfigureAwait(false);
        if (!(result.StandardError + System.Text.Encoding.UTF8.GetString(result.StandardOutput.Span)).Contains("dng_validate", StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{dngValidate}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return new DngSdkTool(dngValidate, unprocessedRaw);
    }

    /// <summary>Gets a stage image of dng_validate (1, 2 or 3).</summary>
    /// <returns>The size, the samples per pixel and the samples of the stage.</returns>
    public async Task<(int Width, int Height, int Samples, double[] Values)> GetStageAsync(FullPath path, int stage, CancellationToken cancellationToken)
    {
        var output = path.Parent / (path.NameWithoutExtension + "-stage" + stage.ToString(CultureInfo.InvariantCulture));
        await RunAsync([("-" + stage.ToString(CultureInfo.InvariantCulture)), output, path], cancellationToken).ConfigureAwait(false);
        return ReadUncompressedTiff(await File.ReadAllBytesAsync(output.Value + ".tif", cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Gets the rendering of dng_validate: sRGB-encoded floats (<c>-32 -tif</c>).</summary>
    public async Task<(int Width, int Height, int Samples, double[] Values)> RenderAsync(FullPath path, CancellationToken cancellationToken)
    {
        var output = path.Parent / (path.NameWithoutExtension + "-render");
        await RunAsync(["-32", "-tif", output, path], cancellationToken).ConfigureAwait(false);
        return ReadUncompressedTiff(await File.ReadAllBytesAsync(output.Value + ".tif", cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Rewrites a DNG file with dng_validate (<c>-dng</c>, with more options such as <c>-losslessJXL</c> or <c>-proxy N</c>).</summary>
    /// <returns>The path of the file it writes.</returns>
    public async Task<FullPath> RewriteAsync(FullPath path, string name, IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        var output = path.Parent / name;
        await RunAsync([.. options, "-dng", output, path], cancellationToken).ConfigureAwait(false);
        return FullPath.FromPath(output.Value + ".dng");
    }

    /// <summary>Gets the stored samples of a color filter array with LibRaw's <c>unprocessed_raw</c>.</summary>
    public async Task<(int Width, int Height, ushort[] Samples)> GetLibRawSamplesAsync(FullPath path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(UnprocessedRawPath, [path], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"unprocessed_raw failed ({result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        var pgm = await File.ReadAllBytesAsync(path.Value + ".pgm", cancellationToken).ConfigureAwait(false);

        // P5, the width, the height, the maximum value, each followed by one white space character
        var fields = new List<string>();
        var position = 0;
        while (fields.Count < 4)
        {
            while (char.IsWhiteSpace((char)pgm[position]))
            {
                position++;
            }

            var start = position;
            while (!char.IsWhiteSpace((char)pgm[position]))
            {
                position++;
            }

            fields.Add(System.Text.Encoding.ASCII.GetString(pgm, start, position - start));
        }

        position++;
        var width = int.Parse(fields[1], CultureInfo.InvariantCulture);
        var height = int.Parse(fields[2], CultureInfo.InvariantCulture);
        var samples = new ushort[width * height];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadUInt16BigEndian(pgm.AsSpan(position + (i * 2)));
        }

        return (width, height, samples);
    }

    private async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(DngValidatePath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"dng_validate {string.Join(' ', arguments)} failed ({result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {System.Text.Encoding.UTF8.GetString(result.StandardOutput.Span)}{result.StandardError}");
    }

    /// <summary>Reads an uncompressed little-endian TIFF of one strip, as dng_validate writes its images.</summary>
    public static (int Width, int Height, int Samples, double[] Values) ReadUncompressedTiff(byte[] data)
    {
        var file = new TiffFile(data);
        var directory = file.ReadDirectory(file.FirstDirectory);
        var width = (int)directory.Number(256);
        var height = (int)directory.Number(257);
        var samples = (int)(directory.TryNumber(277) ?? 1);
        var bits = (int)directory.Numbers(258)[0];
        var isFloat = directory.TryNumber(339) == 3;
        var offset = (int)directory.Numbers(273)[0];
        var bytes = bits / 8;
        var values = new double[width * height * samples];
        for (var i = 0; i < values.Length; i++)
        {
            var sample = data.AsSpan(offset + (i * bytes));
            values[i] = (bits, isFloat) switch
            {
                (32, true) => BinaryPrimitives.ReadSingleLittleEndian(sample),
                (16, true) => (double)BinaryPrimitives.ReadHalfLittleEndian(sample),
                (16, _) => BinaryPrimitives.ReadUInt16LittleEndian(sample),
                _ => sample[0],
            };
        }

        return (width, height, samples, values);
    }
}
