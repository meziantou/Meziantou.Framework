using Meziantou.Framework;
using Meziantou.Framework.Templating.Tool;
using System.Text;

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
    public async Task RenderToOutputFile_WithUtf8BomEncoding()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello", XunitCancellationToken);
        var outputPath = temp.GetFullPath("out/result.txt");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--output", outputPath.ToString(),
                "--output-encoding", "utf8-bom",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        var bytes = await File.ReadAllBytesAsync(outputPath, XunitCancellationToken);
        var preamble = Encoding.UTF8.GetPreamble();
        Assert.True(bytes.AsSpan().StartsWith(preamble));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile_WithLineEndingCrlf()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "A\nB\n", XunitCancellationToken);
        var outputPath = temp.GetFullPath("out/result.txt");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--output", outputPath.ToString(),
                "--line-ending", "CRLF",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("A\r\nB\r\n", await File.ReadAllTextAsync(outputPath, XunitCancellationToken));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile_InvalidLineEnding_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello", XunitCancellationToken);
        var outputPath = temp.GetFullPath("out/result.txt");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--output", outputPath.ToString(),
                "--line-ending", "INVALID",
            ],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("Invalid line ending", console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile_InvalidEncoding_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello", XunitCancellationToken);
        var outputPath = temp.GetFullPath("out/result.txt");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--output", outputPath.ToString(),
                "--output-encoding", "INVALID-ENCODING",
            ],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("Invalid output encoding", console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile_FromOutputDirectiveExtension()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.tt", "<#@ output extension=\".cs\" #>Hello <#= \"World\" #>!", XunitCancellationToken);
        var expectedOutputPath = inputPath.WithExtension(".cs");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello World!", await File.ReadAllTextAsync(expectedOutputPath, XunitCancellationToken));
        Assert.True(File.Exists(inputPath));
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task RenderToOutputFile_OutputOptionHasPriorityOverDirective()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.tt", "<#@ output extension=\".cs\" #>Hello", XunitCancellationToken);
        var directiveOutputPath = inputPath.WithExtension(".cs");
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
        Assert.Contains("cannot be empty", console.Error, ignoreCase: true);
    }

    [Fact]
    public async Task InvalidTemplate_ReturnsDiagnosticWithInputPathAndPosition()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "line1\n<% Missing(); %>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("template.txt(2,4)", console.Error);
    }
}
