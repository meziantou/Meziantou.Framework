using System.Text.Encodings.Web;
using Meziantou.AspNetCore.Mvc.TagHelpers;
using Meziantou.Framework;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Meziantou.AspNetCore.Mvc.Tests;

public sealed class TagHelpersTests
{
    [Fact]
    public void ShowIf_True_RendersTheElementWithoutTheAttribute()
    {
        var helper = new ShowIfTagHelper { Value = true };
        var output = CreateOutput("div", [new TagHelperAttribute("show-if", "true"), new TagHelperAttribute("class", "alert")]);

        helper.Process(CreateContext(), output);

        Assert.Equal("<div class=\"alert\"></div>", Render(output));
    }

    [Fact]
    public void ShowIf_False_SuppressesTheElement()
    {
        var helper = new ShowIfTagHelper { Value = false };
        var output = CreateOutput("div", [new TagHelperAttribute("show-if", "false")]);

        helper.Process(CreateContext(), output);

        Assert.Empty(Render(output));
    }

    [Fact]
    public void Datetime_IsNormalizedToUtcAndKeepsTheZDesignator()
    {
        var helper = new TimeTagHelper { Datetime = new DateTimeOffset(2024, 1, 15, 10, 30, 45, 123, TimeSpan.FromHours(5)) };
        var output = CreateOutput("time");

        helper.Process(CreateContext(), output);

        // Without the Z designator the value is read as local time and shifted by the reader's UTC offset
        Assert.Equal("<time datetime=\"2024-01-15T05:30:45.123Z\"></time>", Render(output));
    }

    [Fact]
    public void Datetime_Null_RemovesTheAttribute()
    {
        var helper = new TimeTagHelper { Datetime = null };
        var output = CreateOutput("time", [new TagHelperAttribute("datetime", "2024-01-01")]);

        helper.Process(CreateContext(), output);

        Assert.Equal("<time></time>", Render(output));
    }

    [Fact]
    public void RenderOnPageLoad_MaliciousId_IsNotInjectedIntoTheScript()
    {
        var helper = new RenderOnPageLoadTagHelper { Id = "');alert('xss');//" };
        var output = CreateOutput("render-on-page-load");

        helper.Process(CreateContext(), output);

        var html = Render(output);
        var script = html[html.IndexOf("<script>", StringComparison.Ordinal)..];
        Assert.DoesNotContain("alert", script);
        Assert.Contains("id=\"&#x27;);alert(&#x27;xss&#x27;);//\"", html);
    }

    [Fact]
    public void RenderOnPageLoad_WithoutId_DoesNotEmitAnIdAttribute()
    {
        var helper = new RenderOnPageLoadTagHelper();
        var output = CreateOutput("render-on-page-load");

        helper.Process(CreateContext(), output);

        Assert.StartsWith("<noscript></noscript>", Render(output));
    }

    [Fact]
    public void RenderOnPageLoad_SchedulesOnLoadInsteadOfRequestAnimationFrame()
    {
        var helper = new RenderOnPageLoadTagHelper();
        var output = CreateOutput("render-on-page-load");

        helper.Process(CreateContext(), output);

        var html = Render(output);

        // requestAnimationFrame never fires while the document is hidden, so the content was never rendered
        // when the page was opened in a background tab
        Assert.DoesNotContain("requestAnimationFrame", html);
        Assert.Contains("addEventListener('load'", html);
    }

    [Fact]
    public async Task InlineScript_EscapesTheClosingScriptTag()
    {
        using var host = new TestHost();
        host.Directory.CreateTextFile("app.js", """var s = "</script>";""");

        var helper = host.CreateScriptTagHelper();
        helper.Src = "app.js";
        var output = CreateOutput("inline-script");

        await helper.ProcessAsync(CreateContext(), output);

        var html = Render(output);

        // An unescaped sequence ends the <script> element early, whatever the JavaScript syntax around it
        Assert.Contains("""<\/script>""", html);
        Assert.Equal(1, CountOccurrences(html, "</script>"));
    }

    [Fact]
    public async Task InlineStyle_EscapesTheClosingStyleTag()
    {
        using var host = new TestHost();
        host.Directory.CreateTextFile("site.css", """a::after { content: "</style>"; }""");

        var helper = host.CreateStyleTagHelper();
        helper.Href = "site.css";
        var output = CreateOutput("inline-style");

        await helper.ProcessAsync(CreateContext(), output);

        var html = Render(output);
        Assert.Contains("""<\/style>""", html);
        Assert.Equal(1, CountOccurrences(html, "</style>"));
    }

    [Fact]
    public async Task InlineImg_RendersTheContentOnceAsADataUri()
    {
        using var host = new TestHost();
        host.Directory.CreateTextFile("logo.png", "fake-png-bytes");
        var expected = Convert.ToBase64String(File.ReadAllBytes(host.Directory / "logo.png"));

        var helper = host.CreateImgTagHelper();
        helper.Src = "logo.png";
        var output = CreateOutput("inline-img");

        await helper.ProcessAsync(CreateContext(), output);

        Assert.Equal($"<img src=\"data:image/png;base64,{expected}\" />", Render(output));
    }

    [Fact]
    public async Task SamePathInlinedAsTextAndAsBase64_DoNotShareACacheEntry()
    {
        using var host = new TestHost();
        host.Directory.CreateTextFile("shared.css", "body{color:red}");

        var img = host.CreateImgTagHelper();
        img.Src = "shared.css";
        var imgOutput = CreateOutput("inline-img");
        await img.ProcessAsync(CreateContext(), imgOutput);

        var style = host.CreateStyleTagHelper();
        style.Href = "shared.css";
        var styleOutput = CreateOutput("inline-style");
        await style.ProcessAsync(CreateContext(), styleOutput);

        // A shared key made the second helper serve whichever encoding was computed first
        Assert.Contains("data:text/css;base64,", Render(imgOutput));
        Assert.Contains("body{color:red}", Render(styleOutput));
    }

    [Fact]
    public async Task InlineFile_IsCachedWhenTheApplicationConfiguresASizeLimit()
    {
        using var host = new TestHost(new MemoryCacheOptions { SizeLimit = 1024 });
        host.Directory.CreateTextFile("app.js", "console.log(1)");

        var helper = host.CreateScriptTagHelper();
        helper.Src = "app.js";
        var output = CreateOutput("inline-script");

        await helper.ProcessAsync(CreateContext(), output);

        Assert.Contains("console.log(1)", Render(output));
    }

    [Fact]
    public async Task CachedContent_CanBeEvictedUnderMemoryPressure()
    {
        using var host = new TestHost();
        host.Directory.CreateTextFile("app.js", "console.log(1)");

        var helper = host.CreateScriptTagHelper();
        helper.Src = "app.js";
        await helper.ProcessAsync(CreateContext(), CreateOutput("inline-script"));

        Assert.NotEqual(0, host.Cache.Count);

        // Compact never removes CacheItemPriority.NeverRemove entries
        host.Cache.Compact(1.0);
        Assert.Equal(0, host.Cache.Count);
    }

    [Fact]
    public async Task MissingFile_SuppressesTheOutputAndLogsAWarning()
    {
        using var host = new TestHost();

        var helper = host.CreateScriptTagHelper();
        helper.Src = "does-not-exist.js";
        var output = CreateOutput("inline-script");

        await helper.ProcessAsync(CreateContext(), output);

        Assert.Empty(Render(output));
        var message = Assert.Single(host.ScriptLogger.Messages);
        Assert.Contains("does-not-exist.js", message);
    }

    private static int CountOccurrences(string value, string substring)
    {
        return value.Split(substring).Length - 1;
    }

    private static string Render(TagHelperOutput output)
    {
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private static TagHelperContext CreateContext()
    {
        return new TagHelperContext([], new Dictionary<object, object>(), "test");
    }

    private static TagHelperOutput CreateOutput(string tagName, TagHelperAttributeList? attributes = null)
    {
        return new TagHelperOutput(tagName, attributes ?? [], (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
    }

    private sealed class TestHost : IDisposable
    {
        private readonly PhysicalFileProvider _fileProvider;
        private readonly IWebHostEnvironment _environment;

        public TestHost(MemoryCacheOptions? options = null)
        {
            Directory = TemporaryDirectory.Create();
            Cache = new MemoryCache(options ?? new MemoryCacheOptions());
            _fileProvider = new PhysicalFileProvider(Directory.FullPath);
            _environment = new TestWebHostEnvironment(Directory.FullPath, _fileProvider);
        }

        public TemporaryDirectory Directory { get; }

        public MemoryCache Cache { get; }

        public CollectingLogger<InlineScriptTagHelper> ScriptLogger { get; } = new();

        public InlineScriptTagHelper CreateScriptTagHelper() => new(_environment, Cache, ScriptLogger);

        public InlineStyleTagHelper CreateStyleTagHelper() => new(_environment, Cache, new CollectingLogger<InlineStyleTagHelper>());

        public InlineImgTagHelper CreateImgTagHelper() => new(_environment, Cache, new CollectingLogger<InlineImgTagHelper>());

        public void Dispose()
        {
            _fileProvider.Dispose();
            Cache.Dispose();
            Directory.Dispose();
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(FullPath root, IFileProvider fileProvider)
        {
            WebRootPath = root;
            ContentRootPath = root;
            WebRootFileProvider = fileProvider;
            ContentRootFileProvider = fileProvider;
        }

        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
