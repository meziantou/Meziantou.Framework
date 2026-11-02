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

    public static TheoryData<string> WrittenNames => new(InteropDocuments.WrittenNames);

    [Theory]
    [MemberData(nameof(WrittenNames))]
    public async Task LinearizedDocumentsPassTheLinearizationCheck(string name)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.Write(name, objectStreams: false, linearize: true);

        var linearization = await qpdf.CheckLinearizationAsync(path, XunitCancellationToken);
        var check = await qpdf.CheckAsync(path, XunitCancellationToken);

        Assert.Equal(0, linearization.ExitCode, linearization.StandardOutputText + linearization.StandardError);
        Assert.Contains("no linearization errors", linearization.StandardOutputText, StringComparison.Ordinal);
        Assert.Equal("", linearization.StandardError.Trim());
        Assert.Equal(0, check.ExitCode, check.StandardOutputText + check.StandardError);
        Assert.Contains("File is linearized", check.StandardOutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinearizedDocumentWithSharedObjectsPassesTheLinearizationCheck()
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var document = new PdfDocument();
        var helvetica = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var courier = PdfFont.GetStandardFont(PdfStandardFont.Courier);
        for (var i = 0; i < 6; i++)
        {
            document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Page " + i, helvetica, 12, 72, 700).DrawText("Shared", i == 0 ? helvetica : courier, 12, 72, 600);
        }

        for (var i = 0; i < 6; i++)
        {
            document.Pages[i].Annotations.AddLink(new PdfRectangle(10, 10, 50, 50), PdfDestination.Fit(document.Pages[(i + 1) % 6]));
        }

        document.Outlines.Add("Chapter", PdfDestination.Fit(document.Pages[3])).Children.Add("Section", PdfDestination.Fit(document.Pages[4]));
        document.Catalog[PdfName.Create("OpenAction")] = new PdfArray(document.Pages[0].Reference, PdfName.Create("Fit"));
        var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "linearized-shared.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path, new PdfSaveOptions { Linearize = true });

        var result = await qpdf.CheckLinearizationAsync(path, XunitCancellationToken);

        Assert.Equal(0, result.ExitCode, result.StandardOutputText + result.StandardError);
        Assert.Contains("no linearization errors", result.StandardOutputText, StringComparison.Ordinal);
        Assert.Equal("", result.StandardError.Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmbeddedFilesAreReadByQpdf(bool objectStreams)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.Write("attachments", objectStreams);

        var keys = await qpdf.ListAttachmentsAsync(path, XunitCancellationToken);
        var data = await qpdf.ShowAttachmentAsync(path, "data.csv", XunitCancellationToken);

        Assert.Equal(["data.csv", "r\u00E9sum\u00E9.txt"], keys);
        Assert.Equal(InteropDocuments.AttachedCsv, data);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncrementalUpdatesWithRemovedObjectsPassTheCheck(bool objectStreams)
    {
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var source = new PdfDocument();
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Original", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 700);
        source.Catalog[PdfName.Create("PrivateData")] = source.Objects.Add(new PdfDictionary { [PdfName.Create("Value")] = PdfInteger.Get(1) });
        source.Trailer[PdfName.Create("PrivateKey")] = PdfString.FromText("kept");
        var document = PdfDocument.Open(source.ToByteArray(new PdfSaveOptions { UseObjectStreams = objectStreams }));

        var removed = (PdfReference)document.Catalog[PdfName.Create("PrivateData")]!;
        document.Catalog.Remove(PdfName.Create("PrivateData"));
        document.Objects.Remove(removed);
        document.Information.Title = "Updated";
        document.Pages[0].CreateContentBuilder().DrawText("Added", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10, 10, 10);
        var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "incremental-removed-" + objectStreams + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var stream = File.Create(path))
        {
            await document.SaveIncrementalAsync(stream, cancellationToken: XunitCancellationToken);
        }

        var result = await qpdf.CheckAsync(path, XunitCancellationToken);

        Assert.Equal(0, result.ExitCode, result.StandardOutputText + result.StandardError);
        Assert.Contains("No syntax or stream encoding errors found", result.StandardOutputText, StringComparison.Ordinal);
        var read = PdfDocument.Open(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.False(read.Objects.TryGetReference(removed.ObjectNumber, out _));
        Assert.Equal("kept", read.Trailer.Get<PdfString>(PdfName.Create("PrivateKey"))!.ToText());
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
    [InlineData("ascii", 130, "")]
    [InlineData("split-character", 126, "é")]
    [InlineData("split-emoji", 125, "\U0001F600")]
    public async Task LongAes256PasswordsAreTruncatedLikeQpdf(string name, int asciiLength, string suffix)
    {
        // 7.6.4.3.3: the UTF-8 password is truncated to 127 bytes, even inside a character, as qpdf reads it. The files
        // qpdf 12 writes with a password longer than 127 bytes cannot be opened by qpdf itself: only the files of the
        // library are checked
        // On Windows, a character outside the BMP does not reach qpdf intact through its command line
        global::Xunit.Assert.SkipWhen(OperatingSystem.IsWindows() && suffix.Any(char.IsSurrogate), "qpdf does not receive the characters outside the BMP of its arguments on Windows");
        var password = new string('a', asciiLength) + suffix;
        var qpdf = await QpdfTool.LocateAsync(XunitCancellationToken);
        var directory = Path.Combine(AppContext.BaseDirectory, "InteropOutput");
        Directory.CreateDirectory(directory);

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption(password, password);
        var byLibrary = Path.Combine(directory, "long-password-" + name + ".pdf");
        await File.WriteAllBytesAsync(byLibrary, document.ToByteArray(), XunitCancellationToken);
        var result = await qpdf.CheckAsync(byLibrary, password, XunitCancellationToken);
        Assert.Equal(0, result.ExitCode, result.StandardOutputText + result.StandardError);
        Assert.DoesNotContain("Incorrect password supplied", result.StandardOutputText, StringComparison.Ordinal);
        Assert.Contains("R = 6", result.StandardOutputText, StringComparison.Ordinal);

        // The password truncated on a character boundary is not the password
        var truncated = await qpdf.CheckAsync(byLibrary, new string('a', asciiLength), XunitCancellationToken);
        Assert.Equal(suffix.Length != 0, truncated.StandardOutputText.Contains("Incorrect password supplied", StringComparison.Ordinal));
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
