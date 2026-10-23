using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>qpdf checks the files the library writes, and its dump of the objects of the fixtures is compared with what the parser reads.</summary>
public sealed class QpdfInteropTests
{
    public static TheoryData<string, bool> Written => InteropDocuments.Written;

    public static TheoryData<string> ValidFixtures => InteropDocuments.ValidFixtures;

    [Theory]
    [MemberData(nameof(Written))]
    public async Task WrittenDocumentsPassTheCheck(string name, bool objectStreams)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.Write(name, objectStreams);

        var result = await qpdf.CheckAsync(path, XunitCancellationToken);

        Assert.Equal(0, result.ExitCode, result.StandardOutputText + result.StandardError);
        Assert.Contains("No syntax or stream encoding errors found", result.StandardOutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncrementalUpdatesPassTheCheck()
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        foreach (var fixture in new[] { "structure/simple.pdf", "structure/simple.objstm.pdf", "structure/incremental.pdf" })
        {
            var document = PdfDocument.Open(File.ReadAllBytes(InteropDocuments.GetFixturePath(fixture)));
            document.Information.Title = "Updated";
            document.Pages[0].CreateContentBuilder().DrawText("Added", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10, 10, 10);
            var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "incremental-" + Path.GetFileName(fixture));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = File.Create(path))
            {
                document.SaveIncremental(stream);
            }

            var result = await qpdf.CheckAsync(path, XunitCancellationToken);

            Assert.Equal(0, result.ExitCode, fixture + ": " + result.StandardOutputText + result.StandardError);
        }
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public async Task FixturesPassTheCheck(string fixture)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);

        var result = await qpdf.CheckAsync(InteropDocuments.GetFixturePath(fixture), XunitCancellationToken);

        Assert.Equal(0, result.ExitCode, result.StandardOutputText + result.StandardError);
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public async Task ParserReadsTheObjectsQpdfReads(string fixture)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.GetFixturePath(fixture);
        var expected = await qpdf.GetObjectsAsync(path, XunitCancellationToken);
        Assert.False(expected.HasWarnings);

        var document = PdfDocument.Open(File.ReadAllBytes(path));
        var differences = new List<string>();
        foreach (var (key, value) in expected.Objects)
        {
            if (!key.StartsWith("obj:", StringComparison.Ordinal))
                continue;

            var reference = key[4..].Split(' ');
            var number = int.Parse(reference[0], CultureInfo.InvariantCulture);
            if (!document.Objects.TryGetReference(number, out var actualReference))
            {
                differences.Add($"{key}: missing");
                continue;
            }

            var actual = actualReference.Resolve();
            if (value.TryGetProperty("value", out var expectedValue))
            {
                Compare(expectedValue, actual, key, differences);
            }
            else if (value.TryGetProperty("stream", out var stream))
            {
                if (actual is not PdfStream actualStream)
                {
                    differences.Add($"{key}: a stream is expected, the parser read a {actual.Kind}");
                    continue;
                }

                Compare(stream.GetProperty("dict"), actualStream.Dictionary, key, differences);
            }
        }

        Assert.Empty(differences);
    }

    /// <summary>Compares a value of the JSON of qpdf (version 2) with an object.</summary>
    private static void Compare(JsonElement expected, PdfObject actual, string path, List<string> differences)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Null:
                Check(actual is PdfNull, path, "null", actual, differences);
                break;
            case JsonValueKind.True or JsonValueKind.False:
                Check(actual is PdfBoolean boolean && boolean.Value == expected.GetBoolean(), path, expected.ToString(), actual, differences);
                break;
            case JsonValueKind.Number:
                Check(actual is PdfNumber number && Math.Abs(number.DoubleValue - expected.GetDouble()) < 1e-6, path, expected.ToString(), actual, differences);
                break;
            case JsonValueKind.String:
                var text = expected.GetString()!;
                if (text.StartsWith('/', StringComparison.Ordinal))
                {
                    Check(actual is PdfName name && name.Value == text[1..], path, text, actual, differences);
                }
                else if (text.StartsWith("u:", StringComparison.Ordinal))
                {
                    Check(actual is PdfString value && value.ToText() == text[2..], path, text, actual, differences);
                }
                else if (text.StartsWith("b:", StringComparison.Ordinal))
                {
                    Check(actual is PdfString value && Convert.ToHexStringLower(value.Bytes.Span) == text[2..].ToLowerInvariant(), path, text, actual, differences);
                }
                else if (text.EndsWith(" R", StringComparison.Ordinal))
                {
                    var parts = text.Split(' ');
                    Check(actual is PdfReference reference && reference.ObjectNumber.ToString(CultureInfo.InvariantCulture) == parts[0] && reference.Generation.ToString(CultureInfo.InvariantCulture) == parts[1], path, text, actual, differences);
                }
                else
                {
                    differences.Add($"{path}: unknown qpdf value {text}");
                }

                break;
            case JsonValueKind.Array:
                if (actual is not PdfArray array || array.Count != expected.GetArrayLength())
                {
                    differences.Add($"{path}: expected an array of {expected.GetArrayLength()} items, got {actual}");
                    return;
                }

                var index = 0;
                foreach (var item in expected.EnumerateArray())
                {
                    Compare(item, array[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", differences);
                    index++;
                }

                break;
            case JsonValueKind.Object:
                if (actual is not PdfDictionary dictionary)
                {
                    differences.Add($"{path}: expected a dictionary, got {actual}");
                    return;
                }

                var count = 0;
                foreach (var property in expected.EnumerateObject())
                {
                    count++;
                    var key = PdfName.Create(property.Name[1..]);
                    if (dictionary[key] is not { } value)
                    {
                        differences.Add($"{path}{property.Name}: missing");
                        continue;
                    }

                    Compare(property.Value, value, path + property.Name, differences);
                }

                // qpdf omits the entries whose value is null
                var actualCount = dictionary.Count(e => e.Value is not PdfNull);
                if (count != actualCount)
                {
                    differences.Add($"{path}: {count} entries expected, {actualCount} read");
                }

                break;
        }
    }

    private static void Check(bool condition, string path, string expected, PdfObject actual, List<string> differences)
    {
        if (!condition)
        {
            differences.Add($"{path}: expected {expected}, got {actual}");
        }
    }
}
