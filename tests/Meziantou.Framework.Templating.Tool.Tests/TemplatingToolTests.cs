using Meziantou.Framework;
using Meziantou.Framework.Templating.Tool;

namespace Meziantou.Framework.Templating.Tool.Tests;

public sealed class TemplatingToolTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task RenderToStdout_DefaultDelimiters()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello <%= \"World\" %>!", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello World!", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task RenderToStdout_CustomDelimiters()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello {{= \"World\" }}!", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--start-code-block-delimiter", "{{",
                "--end-code-block-delimiter", "}}",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello World!", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello <%= \"World\" %>!", XunitCancellationToken);
        var outputPath = temp.GetFullPath("out/result.txt");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--output", outputPath.ToString(),
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello World!", await File.ReadAllTextAsync(outputPath, XunitCancellationToken));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile_FromOutputDirectiveExtension()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.tt", "<#@ output extension=\".cs\" #>Hello", XunitCancellationToken);
        var expectedOutputPath = inputPath.ChangeExtension(".cs");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--start-code-block-delimiter", "<#",
                "--end-code-block-delimiter", "#>",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello", await File.ReadAllTextAsync(expectedOutputPath, XunitCancellationToken));
        Assert.True(File.Exists(inputPath));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile_OutputOptionHasPriorityOverDirective()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.tt", "<#@ output extension=\".cs\" #>Hello", XunitCancellationToken);
        var directiveOutputPath = inputPath.ChangeExtension(".cs");
        var expectedOutputPath = temp.GetFullPath("out/result.generated");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--output", expectedOutputPath.ToString(),
                "--start-code-block-delimiter", "<#",
                "--end-code-block-delimiter", "#>",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello", await File.ReadAllTextAsync(expectedOutputPath, XunitCancellationToken));
        Assert.False(File.Exists(directiveOutputPath));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task EmptyStartDelimiter_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello <%= \"World\" %>!", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--start-code-block-delimiter", "",
            ],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("cannot be empty", console.Error, StringComparison.OrdinalIgnoreCase);
    }
}
