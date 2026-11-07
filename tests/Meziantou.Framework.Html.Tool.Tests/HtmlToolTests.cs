namespace Meziantou.Framework.Html.Tool.Tests;

public class HtmlToolTests(ITestOutputHelper testOutputHelper)
{
    private static async Task ContentEquals(FullPath path, string expectedContent)
    {
        var content = await File.ReadAllTextAsync(path);
        Assert.Equal(expectedContent, content);
    }

    // The examples of the documentation: the tool updates Examples/<name>/index.html, which must match index.expected.html
    [Theory]
    [InlineData("ReplaceValue", new[] { "replace-value", "--xpath", "//meta[@name='version']/@content | //span[@id='version']", "--new-value", "1.2.0" })]
    [InlineData("AppendVersion", new[] { "append-version" })]
    [InlineData("InlineResources", new[] { "inline-resources", "--resource-patterns", "*.css" })]
    public async Task DocumentationExample(string name, string[] args)
    {
        var examplePath = FullPath.FromPath(AppContext.BaseDirectory) / "Examples" / name;
        await using var temp = TemporaryDirectory.Create();
        foreach (var file in Directory.EnumerateFiles(examplePath, "*", SearchOption.AllDirectories))
        {
            var destination = temp.FullPath / FullPath.FromPath(file).MakePathRelativeTo(examplePath);
            destination.CreateParentDirectory();
            File.Copy(file, destination);
        }

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl([.. args, "--single-file", temp.GetFullPath("index.html")], console.ConfigureConsole);

        Assert.Equal(0, result);
        await ContentEquals(temp.GetFullPath("index.html"), await File.ReadAllTextAsync(examplePath / "index.expected.html", XunitCancellationToken));
    }

    [Fact]
    public async Task AppendVersion_SingleFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<link href='sample.css' />", XunitCancellationToken);
        await temp.CreateTextFileAsync("sample.css", "html { }", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["append-version", "--single-file=" + htmlPath], console.ConfigureConsole);
        Assert.Equal(0, result);
        await ContentEquals(htmlPath, "<link href=\"sample.css?v=77a968\">");
    }

    [Fact]
    public async Task AppendVersion_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<link href='sample.css' />", XunitCancellationToken);
        await temp.CreateTextFileAsync("sample.css", "html { }", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["append-version", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath], console.ConfigureConsole);
        Assert.Equal(0, result);
        await ContentEquals(htmlPath, "<link href=\"sample.css?v=77a968\">");
    }

    [Fact]
    public async Task ReplaceValue_Globbing()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["replace-value", "--file-pattern=**/*.html", "--root-directory=" + temp.FullPath, "--xpath=//head/link/@href", "--new-value=test"], console.ConfigureConsole);
        Assert.Equal(0, result);
        await ContentEquals(htmlPath, "<html><head><link href=\"test\"></head><body></body></html>");
    }

    [Fact]
    public async Task ReplaceValue_SingleFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<head><link href='sample.css' /></head>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["replace-value", "--single-file=" + htmlPath, "--xpath=//head/link/@href", "--new-value=test"], console.ConfigureConsole);
        Assert.Equal(0, result);
        await ContentEquals(htmlPath, "<html><head><link href=\"test\"></head><body></body></html>");
    }

    [Fact]
    public async Task AppendVersion_FullDocument()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<!DOCTYPE html>\n<html lang='en'><head><link href='sample.css'></head><body><p>test</p></body></html>\n", XunitCancellationToken);
        await temp.CreateTextFileAsync("sample.css", "html { }", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["append-version", "--single-file=" + htmlPath], console.ConfigureConsole);
        Assert.Equal(0, result);
        await ContentEquals(htmlPath, "<!DOCTYPE html><html lang=\"en\"><head><link href=\"sample.css?v=77a968\"></head><body><p>test</p>\n</body></html>");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplaceValue_PreserveUtf8ByteOrderMark(bool byteOrderMark)
    {
        await using var temp = TemporaryDirectory.Create();
        var encoding = new UTF8Encoding(byteOrderMark);
        var htmlPath = temp.GetFullPath("test.html");
        await File.WriteAllTextAsync(htmlPath, "<span>café</span>", encoding, XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["replace-value", "--single-file=" + htmlPath, "--xpath=//span/text()", "--new-value=thé"], console.ConfigureConsole);
        Assert.Equal(0, result);
        Assert.Equal([.. encoding.GetPreamble(), .. encoding.GetBytes("<span>thé</span>")], await File.ReadAllBytesAsync(htmlPath, XunitCancellationToken));
    }

    [Fact]
    public async Task ReplaceValue_SingleFile_TextElement()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", "<span>test</span>", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["replace-value", "--single-file=" + htmlPath, "--xpath=//span/text()", "--new-value=replaced"], console.ConfigureConsole);
        Assert.Equal(0, result);
        await ContentEquals(htmlPath, "<span>replaced</span>");
    }

    [Fact]
    public async Task InlineResources()
    {
        await using var temp = TemporaryDirectory.Create();
        var htmlPath = await temp.CreateTextFileAsync("test.html", """
            <link href="style.css" />
            <script src="script.js"></script>
            <img src="img.png" />
            <img src="img.jpg" />
            """, XunitCancellationToken);

        await temp.CreateTextFileAsync("style.css", "a", XunitCancellationToken);
        await temp.CreateTextFileAsync("script.js", "b", XunitCancellationToken);
        await temp.CreateTextFileAsync("img.png", "c", XunitCancellationToken);

        var console = new ConsoleHelper(testOutputHelper);
        var result = await Program.MainImpl(["inline-resources", "--single-file=" + htmlPath, "--resource-patterns=.(png|js|css)$"], console.ConfigureConsole);
        Assert.Equal(0, result);
        await ContentEquals(htmlPath, """
            <style>a</style>
            <script>b</script>
            <img src="data:image/png;base64,Yw==">
            <img src="img.jpg">
            """);
    }
}
