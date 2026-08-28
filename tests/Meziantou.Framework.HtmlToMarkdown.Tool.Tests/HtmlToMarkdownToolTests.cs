namespace Meziantou.Framework.HtmlToMarkdownTool.Tests;

public sealed class HtmlToMarkdownToolTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task ConvertStdinToStdout()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl([], console.ConfigureConsole, new StringReader("<h1>Title</h1><p>Hello <strong>world</strong></p>"));

        Assert.Equal(0, result);
        Assert.Equal("# Title\n\nHello **world**\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConvertInputFileToStdout()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("page.html", "<h1>Title</h1>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("# Title\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConvertInputFileToOutputFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("page.html", "<h1>Title</h1>", XunitCancellationToken);
        var outputPath = temp.GetFullPath("out/page.md");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--output", outputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("# Title\n", await File.ReadAllTextAsync(outputPath, XunitCancellationToken));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConvertStdinToOutputFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var outputPath = temp.GetFullPath("page.md");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--output", outputPath.ToString()], console.ConfigureConsole, new StringReader("<h1>Title</h1>"));

        Assert.Equal(0, result);
        Assert.Equal("# Title\n", await File.ReadAllTextAsync(outputPath, XunitCancellationToken));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task StandardInputIsIgnoredWhenInputFileIsSet()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("page.html", "<p>from file</p>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole, new StringReader("<p>from stdin</p>"));

        Assert.Equal(0, result);
        Assert.Equal("from file\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ConvertCompleteDocumentIgnoresHead()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [],
            console.ConfigureConsole,
            new StringReader("<!DOCTYPE html><html><head><title>Page title</title></head><body><h1>Title</h1></body></html>"));

        Assert.Equal(0, result);
        Assert.Equal("# Title\n", console.Output);
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
    public async Task ConversionOptionsAreApplied()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--heading-style", "Setext",
                "--emphasis-marker", "Underscore",
                "--unordered-list-marker", "*",
                "--thematic-break", "***",
                "--emoji-shortcode-mode", "GitHub",
            ],
            console.ConfigureConsole,
            new StringReader("<h1>Title</h1><p>Hello <em>world</em> ❤️</p><ul><li>item</li></ul><hr>"));

        Assert.Equal(0, result);
        Assert.Equal("Title\n=====\n\nHello _world_ :heart:\n\n* item\n\n***\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task UnknownElementHandlingIsApplied()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--unknown-element-handling", "StripKeepContent"],
            console.ConfigureConsole,
            new StringReader("<p>Hello <span>world</span></p>"));

        Assert.Equal(0, result);
        Assert.Equal("Hello world\n", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task MissingInputFileReportsAnError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = temp.GetFullPath("missing.html");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("does not exist", console.Error);
    }

    [Fact]
    public async Task InvalidCharacterOptionReportsAnError()
    {
        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--unordered-list-marker", "ab"], console.ConfigureConsole, new StringReader("<p>Hello</p>"));

        Assert.NotEqual(0, result);
        Assert.Contains("single character", console.Error);
    }
}
