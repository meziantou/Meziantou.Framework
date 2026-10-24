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
    [InlineData(PdfEncryptionAlgorithm.Rc4With40BitKey, "R = 2")]
    [InlineData(PdfEncryptionAlgorithm.Rc4With128BitKey, "R = 3")]
    [InlineData(PdfEncryptionAlgorithm.AesWith128BitKey, "R = 4")]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, "R = 6")]
    public async Task EncryptedDocumentsPassTheCheck(PdfEncryptionAlgorithm algorithm, string revision)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        foreach (var objectStreams in new[] { true, false })
        {
            var document = new PdfDocument();
            document.Information.Title = "Encrypted";
            document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Encrypted text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
            document.Encryption = new PdfEncryption("user", "owner") { Algorithm = algorithm, Permissions = PdfPermissions.Print };
            var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "encrypted-" + algorithm + "-" + objectStreams + ".pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, document.ToByteArray(new PdfSaveOptions { UseObjectStreams = objectStreams }), XunitCancellationToken);

            foreach (var password in new[] { "user", "owner" })
            {
                var result = await qpdf.CheckAsync(path, password, XunitCancellationToken);

                Assert.Equal(0, result.ExitCode, password + ": " + result.StandardOutputText + result.StandardError);
                Assert.Contains(revision, result.StandardOutputText, StringComparison.Ordinal);
                Assert.Contains("No syntax or stream encoding errors found", result.StandardOutputText, StringComparison.Ordinal);
            }

            Assert.Contains("print low resolution: allowed", (await qpdf.CheckAsync(path, "user", XunitCancellationToken)).StandardOutputText, StringComparison.Ordinal);
            Assert.Contains("Incorrect password supplied", (await qpdf.CheckAsync(path, "wrong", XunitCancellationToken)).StandardOutputText, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("rc4-40", new[] { "--allow-weak-crypto", "--encrypt", "user", "owner", "40", "--" })]
    [InlineData("rc4-128", new[] { "--allow-weak-crypto", "--encrypt", "user", "owner", "128", "--use-aes=n", "--" })]
    [InlineData("aes-128", new[] { "--encrypt", "user", "owner", "128", "--use-aes=y", "--" })]
    [InlineData("aes-128-cleartext-metadata", new[] { "--encrypt", "user", "owner", "128", "--use-aes=y", "--cleartext-metadata", "--" })]
    [InlineData("aes-256-r5", new[] { "--encrypt", "user", "owner", "256", "--force-R5", "--" })]
    [InlineData("aes-256", new[] { "--encrypt", "user", "owner", "256", "--" })]
    [InlineData("aes-256-objstm", new[] { "--encrypt", "user", "owner", "256", "--", "--object-streams=generate" })]
    public async Task DocumentsEncryptedByQpdfAreRead(string name, string[] arguments)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "qpdf-encrypted-" + name + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await qpdf.RewriteAsync(InteropDocuments.GetFixturePath("structure/navigation.pdf"), path, arguments, XunitCancellationToken);
        var original = PdfDocument.Open(File.ReadAllBytes(InteropDocuments.GetFixturePath("structure/navigation.pdf")));

        foreach (var password in new[] { "user", "owner" })
        {
            var document = PdfDocument.Open(File.ReadAllBytes(path), new PdfReadOptions { Password = password });

            Assert.True(document.IsEncrypted);
            Assert.Equal(original.Pages.Select(page => page.ExtractText()), document.Pages.Select(page => page.ExtractText()));
            Assert.Equal(original.Outlines.Select(item => item.Title), document.Outlines.Select(item => item.Title));
            Assert.Equal("A note", document.Pages[0].Annotations[2].Contents);

            // Saved again with the keys of the file
            var saved = Path.ChangeExtension(path, "." + password + ".resaved.pdf");
            await File.WriteAllBytesAsync(saved, document.ToByteArray(), XunitCancellationToken);
            var result = await qpdf.CheckAsync(saved, "user", XunitCancellationToken);
            Assert.Equal(0, result.ExitCode, result.StandardOutputText + result.StandardError);
        }

        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(File.ReadAllBytes(path)));
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
