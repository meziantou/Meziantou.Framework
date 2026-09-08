using System.Text.Json;

namespace Meziantou.Framework.Language.Tool.Tests;

public sealed class LanguageToolTests(ITestOutputHelper testOutputHelper)
{
    private const string JsonSample = "{\"a\":[1,/*c*/2]}";
    private const string XmlSample = "<?xml version=\"1.0\"?><root attr=\"value\"><child>sample</child><!--c--></root>";
    private const string ShellSample = "FOO=1 echo hi >out\n";
    private const string RegexSample = @"(?<year>\d{4})-\d\d";

    public static TheoryData<string, string, string> DetectedSamples => new()
    {
        { "sample.json", JsonSample, "json" },
        { "sample.xml", XmlSample, "xml" },
        { "sample.csproj", XmlSample, "xml" },
        { "sample.props", XmlSample, "xml" },
        { "sample.svg", XmlSample, "xml" },
        { "sample.sh", ShellSample, "shell" },
        { "sample.bash", ShellSample, "shell" },
        { "sample.zsh", ShellSample, "shell" },
        { "sample.ps1", "Write-Output 'hi'\n", "shell" },
        { "sample.psm1", "Write-Output 'hi'\n", "shell" },
        { "sample.cmd", "echo hi\r\n", "shell" },
        { "sample.bat", "echo hi\r\n", "shell" },
        { "SAMPLE.JSON", JsonSample, "json" },
    };

    public static TheoryData<string, string> RegexDialects => new()
    {
        { "regex", "net" },
        { "regex-dotnet", "net" },
        { "regex-net", "net" },
        { "regex-javascript", "javascript" },
        { "regex-js", "javascript" },
        { "regex-pcre", "pcre" },
        { "regex-ere", "ere" },
        { "regex-bre", "bre" },
    };

    public static TheoryData<string, string> ShellDialects => new()
    {
        { "sh", "sh" },
        { "bash", "bash" },
        { "zsh", "zsh" },
        { "powershell", "powershell" },
        { "pwsh", "pwsh" },
        { "powershell-core", "pwsh" },
        { "cmd", "cmd" },
    };

    [Theory]
    [MemberData(nameof(DetectedSamples))]
    public async Task Dump_DetectsTheLanguageFromTheFileExtension(string fileName, string content, string expectedLanguage)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var path = await temporaryDirectory.CreateTextFileAsync(fileName, content, TestContext.Current.CancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--input", path], console.ConfigureConsole);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        Assert.Equal(expectedLanguage, document.RootElement.GetProperty("language").GetString());
    }

    [Theory]
    [MemberData(nameof(RegexDialects))]
    public async Task Dump_ParsesEveryRegexDialect(string language, string expectedDialect)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", language], console.ConfigureConsole, new StringReader(RegexSample));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        Assert.Equal("regex", document.RootElement.GetProperty("language").GetString());
        Assert.Equal(expectedDialect, document.RootElement.GetProperty("dialect").GetString());
    }

    [Theory]
    [MemberData(nameof(ShellDialects))]
    public async Task Dump_ParsesEveryShellDialect(string language, string expectedDialect)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", language], console.ConfigureConsole, new StringReader(ShellSample));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        Assert.Equal("shell", document.RootElement.GetProperty("language").GetString());
        Assert.Equal(expectedDialect, document.RootElement.GetProperty("dialect").GetString());
    }

    [Fact]
    public async Task Dump_ExplicitLanguageOverridesTheExtension()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var path = await temporaryDirectory.CreateTextFileAsync("sample.json", ShellSample, TestContext.Current.CancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--input", path, "--language", "bash"], console.ConfigureConsole);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        Assert.Equal("shell", document.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Dump_ReadsTheStandardInput()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", "json"], console.ConfigureConsole, new StringReader(JsonSample));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        Assert.Equal("json", document.RootElement.GetProperty("language").GetString());
        Assert.Equal(JsonSample, document.RootElement.GetProperty("root").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Dump_StandardInputWithoutLanguage_Fails()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax"], console.ConfigureConsole, new StringReader(JsonSample));

        Assert.Equal(1, exitCode);
        Assert.Contains("--language is required", console.Error);
        Assert.Empty(console.Output);
    }

    [Fact]
    public async Task Dump_UnknownExtensionWithoutLanguage_Fails()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var path = await temporaryDirectory.CreateTextFileAsync("sample.unknown", JsonSample, TestContext.Current.CancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--input", path], console.ConfigureConsole);

        Assert.Equal(1, exitCode);
        Assert.Contains("Cannot detect the language", console.Error);
    }

    [Fact]
    public async Task Dump_MissingInputFile_Fails()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var path = temporaryDirectory.FullPath / "missing.json";

        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--input", path], console.ConfigureConsole);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", console.Error);
    }

    [Fact]
    public async Task Dump_UnknownLanguage_Fails()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", "cobol"], console.ConfigureConsole, new StringReader(JsonSample));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("The language must be one of", console.Error);
    }

    [Fact]
    public async Task Dump_WritesTheOutputFileWithoutBom()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var inputPath = await temporaryDirectory.CreateTextFileAsync("sample.json", JsonSample, TestContext.Current.CancellationToken);
        var outputPath = temporaryDirectory.FullPath / "nested" / "tree.json";

        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--input", inputPath, "--output", outputPath], console.ConfigureConsole);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Output);

        var bytes = await File.ReadAllBytesAsync(outputPath, TestContext.Current.CancellationToken);
        Assert.NotEqual<byte>(0xEF, bytes[0]);
        Assert.Equal((byte)'\n', bytes[^1]);

        using var document = JsonDocument.Parse(bytes);
        Assert.Equal("json", document.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Dump_WritesASingleLine()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", "json"], console.ConfigureConsole, new StringReader("{\n  \"a\": 1\n}"));

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("\n", console.Output.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task Dump_InvalidInput_ReportsDiagnosticsAndSucceeds()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", "json"], console.ConfigureConsole, new StringReader("{\"a\":}"));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        var diagnostics = document.RootElement.GetProperty("diagnostics");
        Assert.NotEmpty(diagnostics.EnumerateArray());
        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            Assert.NotEmpty(diagnostic.GetProperty("id").GetString()!);
            Assert.NotEmpty(diagnostic.GetProperty("severity").GetString()!);
            Assert.NotEmpty(diagnostic.GetProperty("message").GetString()!);
        }
    }

    [Theory]
    [InlineData("json", JsonSample)]
    [InlineData("xml", XmlSample)]
    [InlineData("bash", ShellSample)]
    [InlineData("regex-pcre", RegexSample)]
    public async Task Dump_EveryNodeFullSpanSlicesBackToItsOwnText(string language, string text)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", language], console.ConfigureConsole, new StringReader(text));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        AssertSpansMatchText(document.RootElement.GetProperty("root"), text);
    }

    [Theory]
    [InlineData("--no-tokens", "tokens")]
    [InlineData("--no-tokens", "leadingTrivia")]
    [InlineData("--no-trivia", "leadingTrivia")]
    [InlineData("--no-text", "text")]
    [InlineData("--no-spans", "span")]
    [InlineData("--no-spans", "fullSpan")]
    public async Task Dump_OptionRemovesItsFields(string option, string removedProperty)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", "json", option], console.ConfigureConsole, new StringReader(JsonSample));

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain($"\"{removedProperty}\":", console.Output);

        var withoutOption = new ConsoleHelper(testOutputHelper);
        Assert.Equal(0, await Program.MainImpl(["syntax", "--language", "json"], withoutOption.ConfigureConsole, new StringReader(JsonSample)));
        Assert.Contains($"\"{removedProperty}\":", withoutOption.Output);
    }

    [Fact]
    public async Task Dump_RegexReportsCaptures()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["syntax", "--language", "regex-dotnet"], console.ConfigureConsole, new StringReader(RegexSample));

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(console.Output);
        var capture = Assert.Single(document.RootElement.GetProperty("captures").EnumerateArray());
        Assert.Equal(1, capture.GetProperty("number").GetInt32());
        Assert.Equal("year", capture.GetProperty("name").GetString());
    }

    [Fact]
    public async Task NoCommand_PrintsTheHelpAndFails()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl([], console.ConfigureConsole);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("syntax", console.Output + console.Error);
    }

    [Fact]
    public async Task Dump_JsonAndXmlHaveNoDialect()
    {
        foreach (var (language, text) in new[] { ("json", JsonSample), ("xml", XmlSample) })
        {
            var console = new ConsoleHelper(testOutputHelper);
            Assert.Equal(0, await Program.MainImpl(["syntax", "--language", language], console.ConfigureConsole, new StringReader(text)));

            using var document = JsonDocument.Parse(console.Output);
            Assert.False(document.RootElement.TryGetProperty("dialect", out _));
        }
    }

    /// <summary>
    /// <c>text</c> is the node's full text, so it is <c>fullSpan</c> that slices back to it: <c>span</c> leaves out the
    /// leading and trailing trivia, and sits inside <c>fullSpan</c>.
    /// </summary>
    private static void AssertSpansMatchText(JsonElement node, string text)
    {
        var fullSpan = node.GetProperty("fullSpan");
        var fullStart = fullSpan.GetProperty("start").GetInt32();
        var fullLength = fullSpan.GetProperty("length").GetInt32();
        Assert.Equal(node.GetProperty("text").GetString(), text.Substring(fullStart, fullLength));

        var span = node.GetProperty("span");
        var start = span.GetProperty("start").GetInt32();
        var length = span.GetProperty("length").GetInt32();
        Assert.True(start >= fullStart && start + length <= fullStart + fullLength, "span is not contained in fullSpan");

        if (node.TryGetProperty("childNodes", out var childNodes))
        {
            foreach (var child in childNodes.EnumerateArray())
            {
                AssertSpansMatchText(child, text);
            }
        }
    }
}
