namespace Meziantou.Framework.AtlassianDataFormatTool.Tests;

public sealed class AtlassianDataFormatToolTests(ITestOutputHelper testOutputHelper)
{
    private const string SampleDocument = """
        {"version":1,"type":"doc","content":[{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Title"}]},{"type":"paragraph","content":[{"type":"text","text":"Hello","marks":[{"type":"strong"}]}]}]}
        """;

    [Fact]
    public async Task ConvertStdinToStdout()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl([], console.ConfigureConsole, new StringReader(SampleDocument));

        Assert.Equal(0, result);
        Assert.Equal("# Title\n\n**Hello**\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConvertInputFileToStdout()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("issue.json", SampleDocument, XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("# Title\n\n**Hello**\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConvertInputFileToOutputFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("issue.json", SampleDocument, XunitCancellationToken);
        var outputPath = temp.GetFullPath("out/issue.md");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--output", outputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("# Title\n\n**Hello**\n", await File.ReadAllTextAsync(outputPath, XunitCancellationToken));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConvertStdinToOutputFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var outputPath = temp.GetFullPath("issue.md");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--output", outputPath.ToString()], console.ConfigureConsole, new StringReader(SampleDocument));

        Assert.Equal(0, result);
        Assert.Equal("# Title\n\n**Hello**\n", await File.ReadAllTextAsync(outputPath, XunitCancellationToken));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task StandardInputIsIgnoredWhenInputFileIsSet()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("issue.json", SampleDocument, XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath.ToString()],
            console.ConfigureConsole,
            new StringReader("""{"version":1,"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"from stdin"}]}]}"""));

        Assert.Equal(0, result);
        Assert.Equal("# Title\n\n**Hello**\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task EmptyInputProducesEmptyOutput()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl([], console.ConfigureConsole, new StringReader(""));

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task EmptyDocumentProducesEmptyOutput()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl([], console.ConfigureConsole, new StringReader("""{"version":1,"type":"doc","content":[]}"""));

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConversionOptionsAreApplied()
    {
        const string Document = """
            {"version":1,"type":"doc","content":[{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Title"}]},{"type":"paragraph","content":[{"type":"text","text":"world","marks":[{"type":"em"}]}]},{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"item"}]}]}]},{"type":"rule"}]}
            """;

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--heading-style", "Setext",
                "--emphasis-marker", "Underscore",
                "--unordered-list-marker", "*",
                "--thematic-break", "***",
            ],
            console.ConfigureConsole,
            new StringReader(Document));

        Assert.Equal(0, result);
        Assert.Equal("Title\n=====\n\n_world_\n\n* item\n\n***\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task PanelStyleIsApplied()
    {
        const string Document = """
            {"version":1,"type":"doc","content":[{"type":"panel","attrs":{"panelType":"warning"},"content":[{"type":"paragraph","content":[{"type":"text","text":"Careful"}]}]}]}
            """;

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--panel-style", "GitHubAlert"], console.ConfigureConsole, new StringReader(Document));

        Assert.Equal(0, result);
        Assert.Equal("> [!WARNING]\n> Careful\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task UnknownNodeHandlingIsApplied()
    {
        const string Document = """
            {"version":1,"type":"doc","content":[{"type":"somethingNew","content":[{"type":"paragraph","content":[{"type":"text","text":"inner"}]}]}]}
            """;

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--unknown-node-handling", "KeepContent"], console.ConfigureConsole, new StringReader(Document));

        Assert.Equal(0, result);
        Assert.Equal("inner\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task MentionAndDateFormatsAreApplied()
    {
        const string Document = """
            {"version":1,"type":"doc","content":[{"type":"paragraph","content":[{"type":"mention","attrs":{"id":"123","text":"@Alex"}},{"type":"text","text":" on "},{"type":"date","attrs":{"timestamp":"1704067200000"}}]}]}
            """;

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--mention-format", "{text} ({id})", "--date-format", "dd/MM/yyyy"],
            console.ConfigureConsole,
            new StringReader(Document));

        Assert.Equal(0, result);
        Assert.Equal("Alex (123) on 01/01/2024\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task MissingInputFileReportsAnError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = temp.GetFullPath("missing.json");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("does not exist", console.Error);
    }

    [Fact]
    public async Task InvalidJsonReportsAnError()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl([], console.ConfigureConsole, new StringReader("not json"));

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("not a valid ADF document", console.Error);
    }

    [Fact]
    public async Task DocumentWithAnotherRootTypeReportsAnError()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl([], console.ConfigureConsole, new StringReader("""{"type":"paragraph"}"""));

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("not a valid ADF document", console.Error);
    }

    [Fact]
    public async Task InvalidCharacterOptionReportsAnError()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--unordered-list-marker", "ab"], console.ConfigureConsole, new StringReader(SampleDocument));

        Assert.NotEqual(0, result);
        Assert.Contains("single character", console.Error);
    }
}
