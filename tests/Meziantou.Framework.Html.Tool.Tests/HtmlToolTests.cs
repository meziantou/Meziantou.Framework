using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Meziantou.Framework.Html.Tool.Tests;

[Collection("Tool")] // Ensure tests run sequentially
public class HtmlToolTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public HtmlToolTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private static async Task ContentEquals(FullPath path, string expectedContent)
    {
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(expectedContent);
    }

    [Fact]
    public async Task Help()
    {
        var console = new StringBuilderConsole();
        var result = await Program.MainImpl(["--help"], console);
        result.Should().Be(0);
        console.Output.Should().Contain("meziantou.html");
    }

    [Fact]
    public async Task AppendVersion_SingleFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<link href='sample.css' />", XunitCancellationToken);
        await temp.CreateTextFileAsync("sample.css", "html { }", XunitCancellationToken);

        var result = await Program.MainImpl(["append-version", "--single-file=" + htmlPath], new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<link href='sample.css?v=77a968' />");
    }

    [Fact]
    public async Task AppendVersion_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<link href='sample.css' />", XunitCancellationToken);
        await temp.CreateTextFileAsync("sample.css", "html { }", XunitCancellationToken);

        var result = await Program.MainImpl(["append-version", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath], new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<link href='sample.css?v=77a968' />");
    }

    [Fact]
    public async Task ReplaceValue_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>", XunitCancellationToken);

        var result = await Program.MainImpl(["replace-value", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath, "--xpath=//head/link/@href", "--new-value=test"], new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<head><link href='test' /></head>");
    }

    [Fact]
    public async Task ReplaceValue_SingleFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>", XunitCancellationToken);

        var result = await Program.MainImpl(["replace-value", "--single-file=" + htmlPath, "--xpath=//head/link/@href", "--new-value=test"], new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<head><link href='test' /></head>");
    }

    [Fact]
    public async Task ReplaceValue_SingleFile_TextElement()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<span>test</span>", XunitCancellationToken);

        var result = await Program.MainImpl(["replace-value", "--single-file=" + htmlPath, "--xpath=//span/text()", "--new-value=replaced"], new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<span>replaced</span>");
    }

    [Fact]
    public async Task InlineResources()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html",
            """
<link href="style.css" />
<script src="script.js" />
<img src="img.png" />
<img src="img.jpg" />
""", XunitCancellationToken);

        await temp.CreateTextFileAsync("style.css", "a", XunitCancellationToken);
        await temp.CreateTextFileAsync("script.js", "b", XunitCancellationToken);
        await temp.CreateTextFileAsync("img.png", "c", XunitCancellationToken);

        var result = await Program.MainImpl(["inline-resources", "--single-file=" + htmlPath, "--resource-patterns=.(png|js|css)$"], new XunitConsole(_testOutputHelper));
        using (new AssertionScope())
        {
            result.Should().Be(0);
            await ContentEquals(htmlPath, """
<style>a</style>
<script>b</script>
<img src="data:image/png;base64,Yw==" />
<img src="img.jpg" />
""");
        }
    }
}
