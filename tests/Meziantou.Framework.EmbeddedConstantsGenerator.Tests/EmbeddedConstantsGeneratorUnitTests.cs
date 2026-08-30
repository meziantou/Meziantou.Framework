using System.Text;
using Options = Meziantou.Framework.EmbeddedConstantsGenerator.EmbeddedConstantsGeneratorTask.GeneratorOptions;
using Generator = Meziantou.Framework.EmbeddedConstantsGenerator.EmbeddedConstantsGeneratorTask;
using InputFile = Meziantou.Framework.EmbeddedConstantsGenerator.EmbeddedConstantsGeneratorTask.InputFile;

namespace Meziantou.Framework.EmbeddedConstantsGenerator.Tests;

/// <summary>
/// Covers the generator core directly. The end-to-end tests in <see cref="EmbeddedConstantsGeneratorTests"/>
/// pack the NuGet package and run a real build, which is too expensive for edge cases.
/// </summary>
public sealed class EmbeddedConstantsGeneratorUnitTests
{
    [Fact]
    public void Create_InvalidNamespace_ReportsError()
    {
        var result = Generator.Create(CreateOptions("temp", ns: "My.class.Ns"), []);

        Assert.Equal(["MFECG0001"], ErrorCodes(result));
    }

    [Fact]
    public void Create_InvalidClassName_ReportsError()
    {
        var result = Generator.Create(CreateOptions("temp", className: "9Bad"), []);

        Assert.Equal(["MFECG0002"], ErrorCodes(result));
    }

    [Fact]
    public void Create_InvalidVisibility_ReportsError()
    {
        var result = Generator.Create(CreateOptions("temp", classVisibility: "private"), []);

        Assert.Equal(["MFECG0009"], ErrorCodes(result));
    }

    [Fact]
    public async Task Create_FileDoesNotExist_ReportsError()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();

        var result = Generator.Create(CreateOptions(temporaryDirectory.FullPath), [TextFile(temporaryDirectory, "missing.txt")]);

        Assert.Equal(["MFECG0006"], ErrorCodes(result));
    }

    [Fact]
    public async Task Create_TextFileIsNotValidUtf8_ReportsError()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        File.WriteAllBytes(temporaryDirectory.FullPath / "invalid.txt", [0xFF, 0xFE, 0x41]);

        var result = Generator.Create(CreateOptions(temporaryDirectory.FullPath), [TextFile(temporaryDirectory, "invalid.txt")]);

        Assert.Equal(["MFECG0007"], ErrorCodes(result));
    }

    [Fact]
    public async Task Create_TextFileExceedsMaximumSize_ReportsError()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("big.txt", new string('a', Generator.MaxTextFileBytes + 1));

        var result = Generator.Create(CreateOptions(temporaryDirectory.FullPath), [TextFile(temporaryDirectory, "big.txt")]);

        Assert.Equal(["MFECG0008"], ErrorCodes(result));
    }

    [Fact]
    public async Task Create_BinaryFileExceedsMaximumSize_ReportsError()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        File.WriteAllBytes(temporaryDirectory.FullPath / "big.bin", new byte[Generator.DefaultMaxBinaryFileBytes + 1]);

        var result = Generator.Create(CreateOptions(temporaryDirectory.FullPath), [TextFile(temporaryDirectory, "big.bin", kind: "Binary")]);

        Assert.Equal(["MFECG0012"], ErrorCodes(result));
    }

    [Fact]
    public async Task Create_BinaryFileExceedsMaximumSizeButTheLimitIsRaised_Succeeds()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        File.WriteAllBytes(temporaryDirectory.FullPath / "big.bin", new byte[Generator.DefaultMaxBinaryFileBytes + 1]);

        var options = CreateOptions(temporaryDirectory.FullPath, maxBinaryFileBytes: Generator.DefaultMaxBinaryFileBytes * 2);
        var result = Generator.Create(options, [TextFile(temporaryDirectory, "big.bin", kind: "Binary")]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Create_BinaryFileAtTheMaximumSize_Succeeds()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        File.WriteAllBytes(temporaryDirectory.FullPath / "exact.bin", new byte[Generator.DefaultMaxBinaryFileBytes]);

        var result = Generator.Create(CreateOptions(temporaryDirectory.FullPath), [TextFile(temporaryDirectory, "exact.bin", kind: "Binary")]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task GenerateSource_TextWithCharactersThatNeedEscaping_ProducesAValidLiteral()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var content = "q:\" bs:\\ nl:\n crlf:\r\n tab:\t nul:\0 u2028:\u2028 u2029:\u2029 nel:\u0085 emoji:\U0001F600 zwj:\u200D";
        File.WriteAllText(temporaryDirectory.FullPath / "nasty.txt", content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var result = Generator.Create(CreateOptions(temporaryDirectory.FullPath), [TextFile(temporaryDirectory, "nasty.txt")]);
        Assert.Empty(result.Errors);

        var source = Generator.GenerateSource(result.Options, result.Entries);

        // Nothing may terminate the literal or the line it is on
        Assert.Contains(@"\"" bs:\\ nl:\n crlf:\r\n tab:\t nul:\0 u2028:\u2028 u2029:\u2029 nel:\u0085", source);
        Assert.Contains("emoji:\U0001F600 zwj:\u200D\";", source);
        Assert.Equal(1, source.Split('\n').Count(line => line.Contains("NastyText", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Create_TextFileStartsWithByteOrderMark_RemovesIt()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        File.WriteAllBytes(temporaryDirectory.FullPath / "bom.txt", [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']);

        var result = Generator.Create(CreateOptions(temporaryDirectory.FullPath), [TextFile(temporaryDirectory, "bom.txt")]);
        var source = Generator.GenerateSource(result.Options, result.Entries);

        Assert.Contains("""public const string BomText = "{}";""", source);
    }

    [Fact]
    public async Task Create_ImplicitNamesCollide_UsesPathBasedNames()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("a/settings.txt", "A");
        temporaryDirectory.CreateTextFile("b/settings.txt", "B");

        var result = Generator.Create(
            CreateOptions(temporaryDirectory.FullPath),
            [TextFile(temporaryDirectory, "a/settings.txt"), TextFile(temporaryDirectory, "b/settings.txt")]);
        Assert.Empty(result.Errors);

        var source = Generator.GenerateSource(result.Options, result.Entries);

        Assert.Contains("""public const string ASettingsText = "A";""", source);
        Assert.Contains("""public const string BSettingsText = "B";""", source);
    }

    [Fact]
    public async Task Create_ExplicitNamesNormaliseToTheSameIdentifier_ReportsError()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("p.txt", "p");
        temporaryDirectory.CreateTextFile("q.txt", "q");

        var result = Generator.Create(
            CreateOptions(temporaryDirectory.FullPath),
            [TextFile(temporaryDirectory, "p.txt", name: "my-name"), TextFile(temporaryDirectory, "q.txt", name: "My Name")]);

        Assert.Equal(["MFECG0005"], ErrorCodes(result));
    }

    [Fact]
    public async Task GenerateSource_OrdersMembersByName()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("zebra.txt", "z");
        temporaryDirectory.CreateTextFile("apple.txt", "a");

        var result = Generator.Create(
            CreateOptions(temporaryDirectory.FullPath),
            [TextFile(temporaryDirectory, "zebra.txt"), TextFile(temporaryDirectory, "apple.txt")]);

        var source = Generator.GenerateSource(result.Options, result.Entries);

        Assert.True(source.IndexOf("AppleText", StringComparison.Ordinal) < source.IndexOf("ZebraText", StringComparison.Ordinal));
    }

    private static Options CreateOptions(string projectDirectory, string ns = "Demo", string className = "EmbeddedFiles", string classVisibility = "internal", string memberVisibility = "public", int maxBinaryFileBytes = Generator.DefaultMaxBinaryFileBytes)
    {
        return new Options(ns, className, classVisibility, memberVisibility, projectDirectory, maxBinaryFileBytes);
    }

    private static InputFile TextFile(TemporaryDirectory temporaryDirectory, string relativePath, string kind = "Text", string? name = null)
    {
        return new InputFile(relativePath, kind, name, temporaryDirectory.FullPath);
    }

    private static string[] ErrorCodes(Generator.Result result)
    {
        return [.. result.Errors.Select(error => error.Code)];
    }
}
