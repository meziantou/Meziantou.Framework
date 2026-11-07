using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// <c>exr-to-raw</c>, the OpenEXR reference library behind a command line: it writes the header of a file as JSON and the
/// decoded samples of every channel of every part, in the type of the channel.
/// </summary>
public sealed class OpenExrTool
{
    private OpenExrTool(string exrToRaw) => ExrToRawPath = exrToRaw;

    /// <summary>Gets the path of <c>exr-to-raw</c>.</summary>
    public string ExrToRawPath { get; }

    /// <summary>Gets the OpenEXR version the package declares.</summary>
    public static string Version => PrebuiltTools.ExrToRaw.Version;

    /// <summary>
    /// Downloads the tool (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that it runs.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool does not run.</exception>
    public static async Task<OpenExrTool> LocateAsync(CancellationToken cancellationToken)
    {
        var path = await PrebuiltTools.ExrToRaw.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

        // Without arguments, the tool prints its usage
        var result = await ProcessRunner.RunAsync(path, [], cancellationToken).ConfigureAwait(false);
        if (!(result.StandardOutputText + result.StandardError).Contains("--header-only", StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{path}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        return new OpenExrTool(path);
    }

    /// <summary>Decodes the first part of a file with <c>exr-to-raw</c>.</summary>
    /// <param name="inputPath">The OpenEXR file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The compression and the decoded channels of the full-resolution level of the first part.</returns>
    /// <exception cref="InvalidOperationException">OpenEXR rejected the file.</exception>
    public async Task<OpenExrDecodedPart> DecodeAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var directory = inputPath + ".exr-to-raw";
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        var result = await ProcessRunner.RunAsync(ExrToRawPath, [inputPath, directory], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'exr-to-raw {inputPath}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        using var header = JsonDocument.Parse(await File.ReadAllBytesAsync(directory / "header.json", cancellationToken).ConfigureAwait(false));
        var part = header.RootElement.GetProperty("parts")[0];
        var dataWindow = part.GetProperty("dataWindow").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var width = dataWindow[2] - dataWindow[0] + 1;
        var height = dataWindow[3] - dataWindow[1] + 1;
        var channels = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var channel in part.GetProperty("channels").EnumerateArray())
        {
            var type = channel.GetProperty("pixelType").GetString();
            var bytes = await File.ReadAllBytesAsync(directory / channel.GetProperty("file").GetString()!, cancellationToken).ConfigureAwait(false);
            var samples = new float[width * height];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = type switch
                {
                    "HALF" => (float)BinaryPrimitives.ReadHalfLittleEndian(bytes.AsSpan(2 * i)),
                    "FLOAT" => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(4 * i)),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 * i)),
                };
            }

            channels.Add(channel.GetProperty("name").GetString()!, samples);
        }

        return new OpenExrDecodedPart(part.GetProperty("compression").GetString()!, width, height, channels);
    }
}
