using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Html.Tool.Tests;

[Collection("Tool")] // Ensure tests run sequentially
public class UnitTest1
{
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

        var result = await Program.Main(new[] { "append-version", "--single-file=" + htmlPath });
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<link href='sample.css?v=77a968' />");
    }

    [Fact]
    public async Task AppendVersion_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<link href='sample.css' />");
        await temp.CreateTextFileAsync("sample.css", "html { }");

        var result = await Program.Main(new[] { "append-version", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath });
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<link href='sample.css?v=77a968' />");
    }

    [Fact]
    public async Task ReplaceValue_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>");

        var result = await Program.Main(new[] { "replace-value", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath, "--xpath=//head/link/@href", "--new-value=test" });
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<head><link href='test' /></head>");
    }

    [Fact]
    public async Task ReplaceValue_SingleFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>");

        var result = await Program.Main(new[] { "replace-value", "--single-file=" + htmlPath, "--xpath=//head/link/@href", "--new-value=test" });
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<head><link href='test' /></head>");
    }

    [Fact]
    public async Task ReplaceValue_SingleFile_TextElement()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<span>test</span>");

        var result = await Program.Main(new[] { "replace-value", "--single-file=" + htmlPath, "--xpath=//span/text()", "--new-value=replaced" });
        result.Should().Be(0);
        await ContentEquals(htmlPath, "<span>replaced</span>");
    }
}
