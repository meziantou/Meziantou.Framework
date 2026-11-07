using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Barcodes.InteropTests;

/// <summary>Opens the OCR-A and OCR-B fonts of <see cref="BarcodeFonts"/> with HarfBuzz, an independent font reader and shaper.</summary>
public sealed class FontInteropTests
{
    public static TheoryData<string> Fonts() => ["OcrA.ttf", "OcrB.ttf"];

    [Theory]
    [MemberData(nameof(Fonts))]
    public async Task HarfBuzz_MapsThePrintableAsciiCharacters(string fileName)
    {
        await using var directory = TemporaryDirectory.Create();
        var path = await WriteFontAsync(directory, fileName);
        var hbInfo = await PrebuiltTools.HbInfo.GetOrDownloadAsync(XunitCancellationToken);

        var output = await RunAsync(hbInfo, ["--list-unicodes", path]);
        var mapped = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("U+", StringComparison.Ordinal))
            .Select(line => int.Parse(line.AsSpan(2, line.IndexOf('\t', StringComparison.Ordinal) - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToHashSet();
        for (var c = 0x20; c <= 0x7E; c++)
        {
            Assert.Contains(c, mapped);
        }

        var info = await RunAsync(hbInfo, [path]);
        Assert.Contains("Units-Per-EM: 1000", info, StringComparison.Ordinal);
        Assert.Contains("Has TrueType outlines", info, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public async Task HarfBuzz_ShapesEveryCharacterWithTheAdvanceOfThePitch(string fileName)
    {
        await using var directory = TemporaryDirectory.Create();
        var path = await WriteFontAsync(directory, fileName);
        var hbShape = await PrebuiltTools.HbShape.GetOrDownloadAsync(XunitCancellationToken);
        var text = string.Concat(Enumerable.Range(0x21, 0x7E - 0x21 + 1).Select(c => (char)c));

        var output = await RunAsync(hbShape, ["--output-format=json", "--font-funcs=ot", path, text]);
        using var document = JsonDocument.Parse(output);
        var glyphs = document.RootElement.EnumerateArray().ToList();
        Assert.HasCount(text.Length, glyphs);
        Assert.All(glyphs, glyph =>
        {
            Assert.NotEqual("gid0", glyph.GetProperty("g").GetString()); // .notdef
            Assert.Equal(600, glyph.GetProperty("ax").GetInt32());
        });
    }

    private static async Task<string> WriteFontAsync(TemporaryDirectory directory, string fileName)
    {
        await using var resource = typeof(BarcodeFonts).Assembly.GetManifestResourceStream("Meziantou.Framework.Imaging.Barcodes.Fonts." + fileName)!;
        var path = directory.FullPath / fileName;
        await using var file = File.Create(path);
        await resource.CopyToAsync(file, XunitCancellationToken);
        return path;
    }

    private static async Task<string> RunAsync(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync(XunitCancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(XunitCancellationToken);
        await process.WaitForExitAsync(XunitCancellationToken);
        Assert.Equal(0, process.ExitCode, message: await standardError);
        return await standardOutput;
    }
}
