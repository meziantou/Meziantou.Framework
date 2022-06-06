using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

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
    public async Task AppendVersion_SingleFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<link href='sample.css' />");
        await temp.CreateTextFileAsync("sample.css", "html { }");

        var result = await Program.MainImpl(new[] { "append-version", "--single-file=" + htmlPath }, new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<link href='sample.css?v=77a968' />");
    }

    [Fact]
    public async Task AppendVersion_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<link href='sample.css' />");
        await temp.CreateTextFileAsync("sample.css", "html { }");

        var result = await Program.MainImpl(new[] { "append-version", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath }, new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<link href='sample.css?v=77a968' />");
    }

    [Fact]
    public async Task ReplaceValue_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>");

        var result = await Program.MainImpl(new[] { "replace-value", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath, "--xpath=//head/link/@href", "--new-value=test" }, new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<head><link href='test' /></head>");
    }

    [Fact]
    public async Task ReplaceValue_SingleFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>");

        var result = await Program.MainImpl(new[] { "replace-value", "--single-file=" + htmlPath, "--xpath=//head/link/@href", "--new-value=test" }, new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<head><link href='test' /></head>");
    }

    [Fact]
    public async Task ReplaceValue_SingleFile_TextElement()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<span>test</span>");

        var result = await Program.MainImpl(new[] { "replace-value", "--single-file=" + htmlPath, "--xpath=//span/text()", "--new-value=replaced" }, new XunitConsole(_testOutputHelper));
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<span>replaced</span>");
    }

}
