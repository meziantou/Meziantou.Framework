using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The libavif reference command-line tools: <c>avifenc</c> writes AVIF files (still images, grids, image sequences, every
/// property of the container) for the decoder of the library, and <c>avifdec</c> is the independent decoder and container
/// reader they are compared with.
/// </summary>
public sealed partial class LibAvifTool
{
    private LibAvifTool(string avifEnc, string avifDec, string version, IReadOnlyList<string> encoders, IReadOnlyList<string> decoders)
    {
        AvifEncPath = avifEnc;
        AvifDecPath = avifDec;
        Version = version;
        Encoders = encoders;
        Decoders = decoders;
    }

    /// <summary>Gets the path of <c>avifenc</c>.</summary>
    public string AvifEncPath { get; }

    /// <summary>Gets the path of <c>avifdec</c>.</summary>
    public string AvifDecPath { get; }

    /// <summary>Gets the libavif version both tools report (<c>--version</c>).</summary>
    public string Version { get; }

    /// <summary>Gets the AV1 encoders <c>avifenc</c> is built with, as it reports them (for example <c>aom 3.14.1</c>).</summary>
    public IReadOnlyList<string> Encoders { get; }

    /// <summary>Gets the AV1 decoders <c>avifdec</c> is built with, as it reports them (for example <c>dav1d 1.5.3-0-gb546257</c>).</summary>
    public IReadOnlyList<string> Decoders { get; }

    /// <summary>
    /// Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that both report the libavif version the package declares.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tools.</returns>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run or does not report the pinned version.</exception>
    public static async Task<LibAvifTool> LocateAsync(CancellationToken cancellationToken)
    {
        var avifEnc = await PrebuiltTools.Avifenc.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var avifDec = await PrebuiltTools.Avifdec.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var encoder = await ReadVersionAsync(avifEnc, PrebuiltTools.Avifenc.Version, cancellationToken).ConfigureAwait(false);
        var decoder = await ReadVersionAsync(avifDec, PrebuiltTools.Avifdec.Version, cancellationToken).ConfigureAwait(false);
        if (encoder.Version != decoder.Version)
            throw new ExternalToolUnavailableException($"avifenc reports libavif {encoder.Version} and avifdec reports libavif {decoder.Version}.");

        return new LibAvifTool(avifEnc, avifDec, encoder.Version, encoder.Encoders, decoder.Decoders);
    }

    /// <summary>
    /// Runs <c>avifenc</c> with one worker thread (<c>--jobs 1</c>, so that the output does not depend on the machine).
    /// </summary>
    /// <param name="arguments">The options and the input files, in order; the output is given separately.</param>
    /// <param name="outputPath">The AVIF file to write.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The standard output of avifenc: the description of what it wrote.</returns>
    /// <exception cref="InvalidOperationException">avifenc failed.</exception>
    public async Task<string> EncodeAsync(IEnumerable<string> arguments, FullPath outputPath, CancellationToken cancellationToken)
    {
        var result = await RunCheckedAsync(AvifEncPath, ["--jobs", "1", .. arguments, "--output", outputPath], cancellationToken).ConfigureAwait(false);
        return result.StandardOutputText;
    }

    /// <summary>Reads the description printed by <c>avifdec --info</c>, which decodes every frame.</summary>
    /// <param name="inputPath">The AVIF file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed description.</returns>
    /// <exception cref="InvalidOperationException">avifdec rejected the file.</exception>
    public async Task<AvifDecInfo> GetInfoAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await RunCheckedAsync(AvifDecPath, ["--jobs", "1", "--info", inputPath], cancellationToken).ConfigureAwait(false);
        return AvifDecInfo.Parse(result.StandardOutputText);
    }

    /// <summary>
    /// Decodes a still image, or the first image of a sequence, with <c>avifdec --depth 16</c> (16-bit PNG, default chroma
    /// upsampling). avifdec applies the transformations of the file (<c>clap</c>, then <c>irot</c>, then <c>imir</c>).
    /// </summary>
    /// <param name="inputPath">The AVIF file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidOperationException">avifdec rejected the file.</exception>
    public Task<AvifDecImage> DecodeAsync(FullPath inputPath, CancellationToken cancellationToken) => DecodeAsync(inputPath, frameIndex: 0, cancellationToken);

    /// <summary>Decodes one image of an image sequence with <c>avifdec --depth 16 --index</c>.</summary>
    /// <param name="inputPath">The AVIF file.</param>
    /// <param name="frameIndex">The index of the image, from 0.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidOperationException">avifdec rejected the file.</exception>
    public async Task<AvifDecImage> DecodeAsync(FullPath inputPath, int frameIndex, CancellationToken cancellationToken)
    {
        var output = inputPath + string.Create(CultureInfo.InvariantCulture, $".avifdec-{frameIndex}.png");
        File.Delete(output);
        await RunCheckedAsync(AvifDecPath, ["--jobs", "1", "--depth", "16", "--index", frameIndex.ToString(CultureInfo.InvariantCulture), inputPath, output], cancellationToken).ConfigureAwait(false);
        return AvifDecImage.ReadPng(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Decodes one layer of a layered (progressive) image with <c>avifdec --depth 16 --progressive --index</c>. avifdec returns
    /// a layer that is smaller than the image scaled to the size of the image.
    /// </summary>
    /// <param name="inputPath">The AVIF file.</param>
    /// <param name="layerIndex">The index of the layer, from 0; the last layer is the image that <see cref="DecodeAsync(FullPath, CancellationToken)"/> returns.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded layer.</returns>
    /// <exception cref="InvalidOperationException">avifdec rejected the file.</exception>
    public async Task<AvifDecImage> DecodeLayerAsync(FullPath inputPath, int layerIndex, CancellationToken cancellationToken)
    {
        var output = inputPath + string.Create(CultureInfo.InvariantCulture, $".avifdec-layer-{layerIndex}.png");
        File.Delete(output);
        await RunCheckedAsync(AvifDecPath, ["--jobs", "1", "--depth", "16", "--progressive", "--index", layerIndex.ToString(CultureInfo.InvariantCulture), inputPath, output], cancellationToken).ConfigureAwait(false);
        return AvifDecImage.ReadPng(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Decodes every image of an image sequence in one pass with <c>avifdec --depth 16 --index all</c>.</summary>
    /// <param name="inputPath">The AVIF file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded images, in order.</returns>
    /// <exception cref="InvalidOperationException">avifdec rejected the file.</exception>
    public async Task<IReadOnlyList<AvifDecImage>> DecodeAllFramesAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        // avifdec appends "-<index on 10 digits>" to the name of the output, before its extension
        var folder = FullPath.FromPath(inputPath + ".avifdec-all");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        await RunCheckedAsync(AvifDecPath, ["--jobs", "1", "--depth", "16", "--index", "all", inputPath, folder / "frame.png"], cancellationToken).ConfigureAwait(false);
        var files = Directory.GetFiles(folder, "frame-*.png").Order(StringComparer.Ordinal).ToList();
        var frames = new List<AvifDecImage>();
        for (var i = 0; i < files.Count; i++)
        {
            var expectedName = string.Create(CultureInfo.InvariantCulture, $"frame-{i:D10}.png");
            if (Path.GetFileName(files[i]) != expectedName)
                throw new InvalidOperationException($"avifdec wrote the unexpected frame file '{Path.GetFileName(files[i])}' ('{expectedName}' expected).");

            frames.Add(AvifDecImage.ReadPng(await File.ReadAllBytesAsync(files[i], cancellationToken).ConfigureAwait(false)));
        }

        return frames;
    }

    private static async Task<(string Version, IReadOnlyList<string> Encoders, IReadOnlyList<string> Decoders)> ReadVersionAsync(string tool, string expectedVersion, CancellationToken cancellationToken)
    {
        // "Version: 1.4.2 (dav1d [dec]:1.5.3-0-gb546257, aom [enc]:3.14.1)"
        var result = await ProcessRunner.RunAsync(tool, ["--version"], cancellationToken).ConfigureAwait(false);
        var match = VersionRegex().Match(result.StandardOutputText);
        if (result.ExitCode != 0 || !match.Success)
            throw new ExternalToolUnavailableException($"Cannot determine the version of '{tool}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        var version = match.Groups["version"].Value;
        if (version != expectedVersion)
            throw new ExternalToolUnavailableException($"'{Path.GetFileName(tool)}' reports libavif '{version}', which does not match the version '{expectedVersion}' declared by meziantou/prebuilt {PrebuiltTools.ReleaseVersion}.");

        var encoders = new List<string>();
        var decoders = new List<string>();
        foreach (Match codec in CodecRegex().Matches(match.Groups["codecs"].Value))
        {
            var description = $"{codec.Groups["name"].Value} {codec.Groups["version"].Value}";
            var roles = codec.Groups["roles"].Value.Split('/');
            if (roles.Contains("enc", StringComparer.Ordinal))
            {
                encoders.Add(description);
            }

            if (roles.Contains("dec", StringComparer.Ordinal))
            {
                decoders.Add(description);
            }
        }

        return (version, encoders, decoders);
    }

    private static async Task<ProcessResult> RunCheckedAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var list = arguments.ToList();
        var result = await ProcessRunner.RunAsync(tool, list, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'{Path.GetFileName(tool)} {string.Join(' ', list)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return result;
    }

    [GeneratedRegex(@"^Version: (?<version>\S+) \((?<codecs>[^)]*)\)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"(?<name>[\w.-]+) \[(?<roles>[a-z/]+)\]:(?<version>[^,]+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodecRegex();
}
