namespace Meziantou.Framework.Templating.Tool.Tests;

public sealed class TemplatingToolTests(ITestOutputHelper testOutputHelper)
{
    // The templates of the documentation are rendered next to themselves, from their output directive
    [Theory]
    [InlineData("Weekdays.template", "Weekdays.g.cs")]
    [InlineData("Greeting.tt", "Greeting.txt")]
    public async Task DocumentationExample_RendersTheExpectedFile(string templateFileName, string expectedFileName)
    {
        var examplesPath = FullPath.FromPath(AppContext.BaseDirectory) / "Examples";
        await using var temp = TemporaryDirectory.Create();
        var inputPath = temp.GetFullPath(templateFileName);
        File.Copy(examplesPath / templateFileName, inputPath);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
        var expected = await File.ReadAllTextAsync(examplesPath / expectedFileName, XunitCancellationToken);
        var actual = await File.ReadAllTextAsync(temp.GetFullPath(expectedFileName), XunitCancellationToken);
        Assert.Equal(expected, actual);
    }

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

    [Fact]
    public async Task OnlyStartDelimiterSpecified_WithUnknownValue_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello {{= \"World\" }}!", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--start-code-block-delimiter", "{{",
            ],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("must be specified together", console.Error, ignoreCase: false);
        Assert.Equal(string.Empty, console.Output);
    }

    [Fact]
    public async Task OnlyStartDelimiterSpecified_WithWellKnownValue_InfersTheEndDelimiter()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello <#= \"World\" #>!", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--start-code-block-delimiter", "<#",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello World!", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task OnlyEndDelimiterSpecified_WithWellKnownValue_InfersTheStartDelimiter()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello <#= \"World\" #>!", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--end-code-block-delimiter", "#>",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello World!", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task T4Delimiters_AreAutoDetected()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello <#= \"World\" #>!", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello World!", console.Output);
    }

    // A '<#' that is not part of a complete block, in a template that uses the default delimiters,
    // must not switch the tool to T4 delimiters.
    [Fact]
    public async Task T4Delimiters_AreNotAutoDetectedFromAnIncompleteMarkerInText()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "C# generics: List<#> and <%= 1 + 1 %>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("C# generics: List<#> and 2", console.Output);
    }

    [Fact]
    public async Task T4Delimiters_AreNotAutoDetectedWhenTheDefaultDelimiterIsAlsoPresent()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "<# is a T4 block #> and <%= 1 + 1 %>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("<# is a T4 block #> and 2", console.Output);
    }

    [Fact]
    public async Task TemplateThrowsAtRuntime_ReturnsDiagnosticWithInputPathAndPosition()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "line1\n<% throw new System.InvalidOperationException(\"boom\"); %>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("template.txt(2", console.Error, ignoreCase: false);
        Assert.Contains("error: boom", console.Error, ignoreCase: false);
        Assert.DoesNotContain("System.Reflection", console.Error, ignoreCase: false);
    }

    [Fact]
    public async Task DocumentationExample_Arguments()
    {
        var examplesPath = FullPath.FromPath(AppContext.BaseDirectory) / "Examples";
        await using var temp = TemporaryDirectory.Create();
        var inputPath = temp.GetFullPath("Release.template");
        File.Copy(examplesPath / "Release.template", inputPath);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", inputPath.ToString(),
                "--arguments-file", (examplesPath / "Release.json").ToString(),
                "--arg", "Channel=preview",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Error);
        var expected = await File.ReadAllTextAsync(examplesPath / "Release.txt", XunitCancellationToken);
        var actual = await File.ReadAllTextAsync(temp.GetFullPath("Release.txt"), XunitCancellationToken);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Arg_IsPassedAsString()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "<%= Name.GetType().Name %>: Hello <%= Name %>! <%= Empty.Length %>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--arg", "Name=a=b", "--arg", "Empty="], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("String: Hello a=b! 0", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task Arg_ReplacesTheValueOfTheArgumentsFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "<%= Value.GetType().Name %> <%= Value %>", XunitCancellationToken);
        var argumentsPath = await temp.CreateTextFileAsync("arguments.json", """{ "Value": 1 }""", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath.ToString(), "--arguments-file", argumentsPath.ToString(), "--arg", "Value=2", "--arg", "Value=3"],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("String 3", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Fact]
    public async Task ArgumentsFile_ConvertsTheJsonValues()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync(
            "template.txt",
            "<%= Text.GetType().Name %>|<%= Flag.GetType().Name %>|<%= Integer.GetType().Name %>|<%= Number.GetType().Name %> <%= Number.ToString(System.Globalization.CultureInfo.InvariantCulture) %>|<%= Missing is null %>|<%= Item.GetProperty(\"Name\").GetString() %>",
            XunitCancellationToken);
        var argumentsPath = await temp.CreateTextFileAsync(
            "arguments.json",
            """
            {
              // Comments and trailing commas are allowed
              "Text": "a",
              "Flag": true,
              "Integer": 42,
              "Number": 1.50,
              "Missing": null,
              "Item": { "Name": "b" },
            }
            """,
            XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--arguments-file", argumentsPath.ToString()], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("String|Boolean|Int64|Decimal 1.50|True|b", console.Output);
        Assert.Equal(string.Empty, console.Error);
    }

    [Theory]
    [InlineData("Name", "must be written as name=value")]
    [InlineData("=value", "is not a valid C# identifier")]
    [InlineData("class=value", "is not a valid C# identifier")]
    [InlineData("my-name=value", "is not a valid C# identifier")]
    [InlineData("__output__=value", "is reserved")]
    public async Task Arg_Invalid_ReturnsError(string argument, string expectedError)
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--arg", argument], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains(expectedError, console.Error, ignoreCase: false);
    }

    [Theory]
    [InlineData("[1, 2]", "The root of the arguments file must be an object")]
    [InlineData("{ \"Name\": ", "is not valid JSON")]
    [InlineData("{ \"not valid\": 1 }", "is not a valid C# identifier")]
    public async Task ArgumentsFile_Invalid_ReturnsError(string json, string expectedError)
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello", XunitCancellationToken);
        var argumentsPath = await temp.CreateTextFileAsync("arguments.json", json, XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--arguments-file", argumentsPath.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains(expectedError, console.Error, ignoreCase: false);
    }

    [Fact]
    public async Task ArgumentsFile_Missing_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--arguments-file", temp.GetFullPath("missing.json").ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("The arguments file", console.Error, ignoreCase: false);
        Assert.Contains("does not exist", console.Error, ignoreCase: false);
    }

    [Fact]
    public async Task OutputExtension_SingleTemplate_WritesNextToTheTemplate()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.tt", "Hello", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--output-extension", "txt"], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
        Assert.Equal("Hello", await File.ReadAllTextAsync(temp.GetFullPath("template.txt"), XunitCancellationToken));
    }

    [Fact]
    public async Task OutputExtension_DirectiveHasPriority()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.tt", "<%@ outputextension .cs %>Hello", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--output-extension", ".txt"], console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal("Hello", await File.ReadAllTextAsync(temp.GetFullPath("template.cs"), XunitCancellationToken));
        Assert.False(File.Exists(temp.GetFullPath("template.txt")));
    }

    [Fact]
    public async Task OutputExtension_SameAsTheTemplate_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("template.txt", "Hello", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath.ToString(), "--output-extension", ".txt"], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("would overwrite the template", console.Error, ignoreCase: false);
        Assert.Equal("Hello", await File.ReadAllTextAsync(inputPath, XunitCancellationToken));
    }

    [Fact]
    public async Task MultipleInputs_WriteNextToEachTemplate_WithTheSameArguments()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath1 = await temp.CreateTextFileAsync("a.tt", "A <%= Name %>", XunitCancellationToken);
        var inputPath2 = await temp.CreateTextFileAsync("sub/b.tt", "<%@ outputextension .cs %>B <%= Name %>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath1.ToString(), inputPath2.ToString(), "--output-extension", ".txt", "--arg", "Name=x"],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
        Assert.Equal("A x", await File.ReadAllTextAsync(temp.GetFullPath("a.txt"), XunitCancellationToken));
        Assert.Equal("B x", await File.ReadAllTextAsync(temp.GetFullPath("sub/b.cs"), XunitCancellationToken));
    }

    [Fact]
    public async Task MultipleInputs_WithoutOutputName_ReturnsErrorAndWritesNothing()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath1 = await temp.CreateTextFileAsync("a.tt", "<%@ outputextension .txt %>A", XunitCancellationToken);
        var inputPath2 = await temp.CreateTextFileAsync("b.tt", "B", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", inputPath1.ToString(), "--input", inputPath2.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("b.tt: error: The template has no output directive", console.Error, ignoreCase: false);
        Assert.False(File.Exists(temp.GetFullPath("a.txt")));
    }

    [Fact]
    public async Task MultipleInputs_WithOutputOption_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath1 = await temp.CreateTextFileAsync("a.tt", "A", XunitCancellationToken);
        var inputPath2 = await temp.CreateTextFileAsync("b.tt", "B", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath1.ToString(), "--input", inputPath2.ToString(), "--output", temp.GetFullPath("out.txt").ToString()],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("Use --output-directory", console.Error, ignoreCase: false);
        Assert.False(File.Exists(temp.GetFullPath("out.txt")));
    }

    [Fact]
    public async Task OutputAndOutputDirectory_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("a.tt", "A", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath.ToString(), "--output", temp.GetFullPath("out.txt").ToString(), "--output-directory", temp.GetFullPath("out").ToString()],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("cannot be used together", console.Error, ignoreCase: false);
    }

    [Fact]
    public async Task GlobPattern_WritesToOutputDirectory_RelativeToTheStartOfThePattern()
    {
        await using var temp = TemporaryDirectory.Create();
        await temp.CreateTextFileAsync("templates/a.tt", "<%@ outputextension .cs %>A <%= Name %>", XunitCancellationToken);
        await temp.CreateTextFileAsync("templates/sub/b.tt", "B <%= Name %>", XunitCancellationToken);
        await temp.CreateTextFileAsync("templates/sub/c.txt", "not a template", XunitCancellationToken);
        var outputDirectory = temp.GetFullPath("out");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            [
                "--input", "templates/**/*.tt",
                "--root-directory", temp.FullPath.ToString(),
                "--output-directory", outputDirectory.ToString(),
                "--output-extension", ".txt",
                "--arg", "Name=x",
            ],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Equal(string.Empty, console.Error);
        Assert.Equal("A x", await File.ReadAllTextAsync(outputDirectory / "a.cs", XunitCancellationToken));
        Assert.Equal("B x", await File.ReadAllTextAsync(outputDirectory / "sub" / "b.txt", XunitCancellationToken));
        Assert.Equal(["a.cs", Path.Combine("sub", "b.txt")], Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(outputDirectory, path)).Order(StringComparer.Ordinal));
    }

    // A glob pattern always names the output by the output directive, even when it matches a single template
    [Fact]
    public async Task GlobPattern_MatchingASingleTemplate_DoesNotWriteToStdout()
    {
        await using var temp = TemporaryDirectory.Create();
        await temp.CreateTextFileAsync("a.tt", "A", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", "*.tt", "--root-directory", temp.FullPath.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Equal(string.Empty, console.Output);
        Assert.Contains("The template has no output directive", console.Error, ignoreCase: false);
    }

    [Fact]
    public async Task GlobPattern_NoMatch_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["--input", "**/*.tt", "--root-directory", temp.FullPath.ToString()], console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("The glob pattern '**/*.tt' does not match any file", console.Error, ignoreCase: false);
    }

    [Fact]
    public async Task MultipleInputs_SameOutputFile_ReturnsError()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath1 = await temp.CreateTextFileAsync("dir1/a.tt", "A", XunitCancellationToken);
        var inputPath2 = await temp.CreateTextFileAsync("dir2/a.tt", "B", XunitCancellationToken);
        var outputDirectory = temp.GetFullPath("out");

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath1.ToString(), "--input", inputPath2.ToString(), "--output-directory", outputDirectory.ToString(), "--output-extension", ".txt"],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("both generate", console.Error, ignoreCase: false);
        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public async Task MultipleInputs_SameTemplateTwice_RendersItOnce()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath = await temp.CreateTextFileAsync("a.tt", "A", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath.ToString(), "--input", "*.tt", "--root-directory", temp.FullPath.ToString(), "--output-extension", ".txt"],
            console.ConfigureConsole);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, console.Error);
        Assert.Equal("A", await File.ReadAllTextAsync(temp.GetFullPath("a.txt"), XunitCancellationToken));
    }

    [Fact]
    public async Task MultipleInputs_TemplateFails_OtherTemplatesAreRendered()
    {
        await using var temp = TemporaryDirectory.Create();
        var inputPath1 = await temp.CreateTextFileAsync("a.tt", "<% Missing(); %>", XunitCancellationToken);
        var inputPath2 = await temp.CreateTextFileAsync("b.tt", "B", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(
            ["--input", inputPath1.ToString(), "--input", inputPath2.ToString(), "--output-extension", ".txt"],
            console.ConfigureConsole);

        Assert.Equal(1, result);
        Assert.Contains("a.tt(1,4)", console.Error, ignoreCase: false);
        Assert.False(File.Exists(temp.GetFullPath("a.txt")));
        Assert.Equal("B", await File.ReadAllTextAsync(temp.GetFullPath("b.txt"), XunitCancellationToken));
    }
}
