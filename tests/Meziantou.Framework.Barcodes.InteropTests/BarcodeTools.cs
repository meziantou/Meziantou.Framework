using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Barcodes.InteropTests;

/// <summary>
/// The pinned barcode tools, downloaded from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package: zint
/// (an encoder, the oracle of the module matrices), ZXingReader and zbarimg (two readers). A tool that cannot be downloaded
/// or reports an unexpected version fails the test, it is never skipped.
/// </summary>
internal sealed class BarcodeTools
{
    private static readonly Lazy<Task<BarcodeTools>> Instance = new(() => LocateAsync(CancellationToken.None));

    private BarcodeTools(string zint, string zxingReader, string zbarImg)
    {
        ZintPath = zint;
        ZXingReaderPath = zxingReader;
        ZBarImgPath = zbarImg;
    }

    public string ZintPath { get; }

    public string ZXingReaderPath { get; }

    public string ZBarImgPath { get; }

    public static Task<BarcodeTools> GetAsync() => Instance.Value;

    private static async Task<BarcodeTools> LocateAsync(CancellationToken cancellationToken)
    {
        var zint = await PrebuiltTools.Zint.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var zxing = await PrebuiltTools.ZXingReader.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var zbar = await PrebuiltTools.ZBarImg.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

        await CheckVersionAsync(zint, ["-v"], PrebuiltTools.Zint.Version, cancellationToken).ConfigureAwait(false);
        await CheckVersionAsync(zxing, ["-version"], PrebuiltTools.ZXingReader.Version, cancellationToken).ConfigureAwait(false);
        await CheckVersionAsync(zbar, ["--version"], PrebuiltTools.ZBarImg.Version, cancellationToken).ConfigureAwait(false);
        return new BarcodeTools(zint, zxing, zbar);
    }

    private static async Task CheckVersionAsync(string path, string[] arguments, string expectedVersion, CancellationToken cancellationToken)
    {
        var result = await RunAsync(path, arguments, cancellationToken).ConfigureAwait(false);
        var output = result.StandardOutput + result.StandardError;
        if (!output.Contains(expectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{path}' does not report version {expectedVersion}: {output}");
    }

    /// <summary>Encodes data with zint and returns its module matrix (<c>--dump</c>).</summary>
    /// <param name="arguments">The options (<c>-b</c>, <c>--vers</c>, <c>--mask</c>, <c>--secure</c>, ...), without <c>-d</c>.</param>
    /// <param name="data">The data to encode.</param>
    /// <param name="width">The width of the symbol, which the hexadecimal rows do not give exactly.</param>
    public async Task<bool[,]> GetZintMatrixAsync(IEnumerable<string> arguments, string data, int width)
    {
        var rows = await GetZintRowsAsync(arguments, data).ConfigureAwait(false);
        var matrix = new bool[rows.Count, width];
        for (var row = 0; row < rows.Count; row++)
        {
            var hex = rows[row];
            if (hex.Length != (width + 3) / 4)
                throw new InvalidOperationException($"zint row {row.ToString(CultureInfo.InvariantCulture)} has {hex.Length.ToString(CultureInfo.InvariantCulture)} digits, a symbol {width.ToString(CultureInfo.InvariantCulture)} modules wide needs {((width + 3) / 4).ToString(CultureInfo.InvariantCulture)}: {rows[row]}");

            for (var column = 0; column < width; column++)
            {
                var digit = int.Parse(hex.AsSpan(column / 4, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                matrix[row, column] = (digit & (8 >> (column % 4))) != 0;
            }
        }

        return matrix;
    }

    /// <summary>Encodes data with zint and returns the rows of its <c>--dump</c> output: one hexadecimal digit per 4 modules, the last one padded.</summary>
    /// <param name="arguments">The options (<c>-b</c>, <c>--vers</c>, <c>--mask</c>, <c>--secure</c>, ...), without <c>-d</c>.</param>
    /// <param name="data">The data to encode.</param>
    public async Task<IReadOnlyList<string>> GetZintRowsAsync(IEnumerable<string> arguments, string data)
    {
        var result = await RunAsync(ZintPath, [.. arguments, "--dump", "-d", data], XunitCancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"zint failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutput}");

        return [.. result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(row => row.Replace(" ", "", StringComparison.Ordinal))];
    }

    /// <summary>Reads the barcodes of image files with ZXingReader.</summary>
    /// <returns>The barcodes found, with the file they were found in.</returns>
    public async Task<IReadOnlyList<ZXingResult>> ReadWithZXingAsync(IReadOnlyCollection<string> paths)
    {
        // ZXingReader writes the file path into the JSON without escaping it, so a Windows path with backslashes is
        // invalid JSON. Windows accepts forward slashes, and the paths of the results map back to the given ones.
        var arguments = paths.ToDictionary(path => path.Replace('\\', '/'), path => path, StringComparer.Ordinal);
        var result = await RunAsync(ZXingReaderPath, ["-json", .. arguments.Keys], XunitCancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ZXingReader failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutput}");

        var results = new List<ZXingResult>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{', StringComparison.Ordinal))
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var filePath = root.GetProperty("FilePath").GetString()!;
            results.Add(new ZXingResult(
                FilePath: arguments.GetValueOrDefault(filePath, filePath),
                Text: root.TryGetProperty("Text", out var text) ? text.GetString() ?? "" : "",
                HumanReadableText: root.TryGetProperty("HRI", out var humanReadableText) ? humanReadableText.GetString() ?? "" : "",
                Bytes: root.TryGetProperty("Bytes", out var bytes) ? Convert.FromHexString((bytes.GetString() ?? "").Replace(" ", "", StringComparison.Ordinal)) : [],
                Format: root.GetProperty("Format").GetString()!,
                Version: root.TryGetProperty("Version", out var version) ? version.GetString() : null,
                ErrorCorrectionLevel: root.TryGetProperty("ECLevel", out var ecLevel) ? ecLevel.GetString() : null,
                DataMask: root.TryGetProperty("DataMask", out var mask) ? mask.GetInt32() : null,
                Error: root.TryGetProperty("Error", out var error) ? error.GetString() : null,
                SymbologyIdentifier: root.TryGetProperty("Identifier", out var identifier) ? identifier.GetString() : null,
                ContentType: root.TryGetProperty("ContentType", out var contentType) ? contentType.GetString() : null));
        }

        return results;
    }

    /// <summary>Reads the barcodes of an image file with zbarimg.</summary>
    /// <returns>The barcodes found, as <c>TYPE:data</c>.</returns>
    public async Task<IReadOnlyList<string>> ReadWithZBarAsync(string path)
    {
        var result = await RunAsync(ZBarImgPath, ["-q", path], XunitCancellationToken).ConfigureAwait(false);

        // zbarimg exits with 4 when it finds no barcode
        if (result.ExitCode is not (0 or 4))
            throw new InvalidOperationException($"zbarimg failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutput}");

        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Cannot start '{fileName}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
    }
}
