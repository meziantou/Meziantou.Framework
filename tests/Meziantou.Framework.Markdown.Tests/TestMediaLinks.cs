using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using Meziantou.Framework.Markdown.Extensions.MediaLinks;

namespace Meziantou.Framework.Markdown.Tests;

public class TestMediaLinks
{
    private static MarkdownPipeline GetPipeline(MediaOptions? options = null)
    {
        return new MarkdownPipelineBuilder()
             .UseMediaLinks(options)
             .Build();
    }

    private static MarkdownPipeline GetPipelineWithBootstrap(MediaOptions? options = null)
    {
        return new MarkdownPipelineBuilder()
            .UseBootstrap()
            .UseMediaLinks(options)
            .Build();
    }

    [Theory]
    [InlineData("![static mp4](https://sample.com/video.mp4)", "<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"https://sample.com/video.mp4\"></source></video></p>\n")]
    [InlineData("![static mp4](//sample.com/video.mp4)", "<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"//sample.com/video.mp4\"></source></video></p>\n")]
    [InlineData(@"![youtube short](https://www.youtube.com/shorts/6BUptHVuvyI?feature=share)", "<p><iframe src=\"https://www.youtube.com/embed/6BUptHVuvyI\" class=\"youtubeshort\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    [InlineData(@"![youtube.com](https://www.youtube.com/watch?v=mswPy5bt3TQ)", "<p><iframe src=\"https://www.youtube.com/embed/mswPy5bt3TQ\" class=\"youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    [InlineData("![yandex.ru](https://music.yandex.ru/album/411845/track/4402274)", "<p><iframe src=\"https://music.yandex.ru/iframe/#track/4402274/411845/\" class=\"yandex\" width=\"500\" height=\"281\" frameborder=\"0\"></iframe></p>\n")]
    [InlineData("![vimeo](https://vimeo.com/8607834)", "<p><iframe src=\"https://player.vimeo.com/video/8607834\" class=\"vimeo\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    [InlineData("![ok.ru](https://ok.ru/video/26870090463)", "<p><iframe src=\"https://ok.ru/videoembed/26870090463\" class=\"odnoklassniki\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    [InlineData("![ok.ru](//ok.ru/video/26870090463)", "<p><iframe src=\"https://ok.ru/videoembed/26870090463\" class=\"odnoklassniki\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    public void TestBuiltInHosts(string markdown, string expected)
    {
        string html = MarkdownConverter.ToHtml(markdown, GetPipeline());
        Assert.Equal(expected, html);
    }

    [Theory]
    [InlineData("![static video relative path](./video.mp4)",
        "<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"./video.mp4\"></source></video></p>\n")]
    [InlineData("![static audio relative path](./audio.mp3)",
        "<p><audio width=\"500\" controls=\"\"><source type=\"audio/mpeg\" src=\"./audio.mp3\"></source></audio></p>\n")]
    public void TestBuiltInHostsWithRelativePaths(string markdown, string expected)
    {
        string html = MarkdownConverter.ToHtml(markdown, GetPipeline());
        Assert.Equal(expected, html);
    }

    [Theory]
    [InlineData("![v](http://x/a.mp4?\"></video><img/src/onerror=alert(1)>)",
        "<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"http://x/a.mp4?%22%3E%3C/video%3E%3Cimg/src/onerror=alert(1)%3E\"></source></video></p>\n")]
    [InlineData("![v](x\"><svg/onload=alert(1)>.mp4)",
        "<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"x%22%3E%3Csvg/onload=alert(1)%3E.mp4\"></source></video></p>\n")]
    public void TestAudioVideoUrlIsEscaped(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseMediaLinks()
            .DisableHtml()
            .Build();

        string html = MarkdownConverter.ToHtml(markdown, pipeline);
        Assert.Equal(expected, html);
    }

    [Theory]
    [InlineData("![v](javascript://www.youtube.com/embed/%0aalert(document.domain))",
        "<p><img src=\"javascript://www.youtube.com/embed/%0aalert(document.domain)\" alt=\"v\" /></p>\n")]
    [InlineData("![v](https://www.youtube.com.example.org/embed/abc)",
        "<p><img src=\"https://www.youtube.com.example.org/embed/abc\" alt=\"v\" /></p>\n")]
    [InlineData("![v](https://evilvimeo.com/8607834)",
        "<p><img src=\"https://evilvimeo.com/8607834\" alt=\"v\" /></p>\n")]
    [InlineData("![v](https://evil.example\uFF0F@www.youtube.com/embed/x)",
        "<p><iframe src=\"https://evil.example%EF%BC%8F@www.youtube.com/embed/x\" class=\"youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    [InlineData("![v](https://player.vimeo.com/8607834)",
        "<p><iframe src=\"https://player.vimeo.com/video/8607834\" class=\"vimeo\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    public void TestBuiltInHostsRequireHttpAndExactHost(string markdown, string expected)
    {
        string html = MarkdownConverter.ToHtml(markdown, GetPipeline());
        Assert.Equal(expected, html);
    }

    [Fact]
    public void TestCustomHostProviderCannotReturnScriptUrl()
    {
        string html = MarkdownConverter.ToHtml("![p1](https://sample.com/video)", GetPipeline(new MediaOptions
        {
            Hosts =
            {
                HostProviderBuilder.Create("sample.com", _ => "javascript:alert(1)"),
            },
        }));
        Assert.Equal("<p><img src=\"https://sample.com/video\" alt=\"p1\" /></p>\n", html);
    }

    private sealed class TestHostProvider : IHostProvider
    {
        public string Class { get; } = "regex";
        public bool AllowFullScreen { get; }

        public bool TryHandle(Uri mediaUri, bool isSchemaRelative, [NotNullWhen(true)] out string? iframeUrl)
        {
            iframeUrl = null;
            var uri = isSchemaRelative ? "//" + mediaUri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Scheme, UriFormat.UriEscaped) : mediaUri.ToString();
            if (!_matcher.IsMatch(uri))
                return false;
            iframeUrl = _matcher.Replace(uri, _replacement);
            return true;
        }

        private readonly Regex _matcher;
        private readonly string _replacement;

        public TestHostProvider(string provider, string replace)
        {
            _matcher = new Regex(provider);
            _replacement = replace;
        }
    }

    [Theory]
    [InlineData("![p1](https://sample.com/video.mp4)", "<p><iframe src=\"https://example.com/video.mp4\" class=\"regex\" width=\"500\" height=\"281\" frameborder=\"0\"></iframe></p>\n", @"^https?://sample.com/(.+)$", @"https://example.com/$1")]
    [InlineData("![p1](//sample.com/video.mp4)", "<p><iframe src=\"https://example.com/video.mp4\" class=\"regex\" width=\"500\" height=\"281\" frameborder=\"0\"></iframe></p>\n", @"^//sample.com/(.+)$", @"https://example.com/$1")]
    [InlineData("![p1](https://sample.com/video.mp4)", "<p><iframe src=\"https://example.com/video.mp4?token=aaabbb\" class=\"regex\" width=\"500\" height=\"281\" frameborder=\"0\"></iframe></p>\n", @"^https?://sample.com/(.+)$", @"https://example.com/$1?token=aaabbb")]
    public void TestCustomHostProvider(string markdown, string expected, string provider, string replace)
    {
        string html = MarkdownConverter.ToHtml(markdown, GetPipeline(new MediaOptions
        {
            Hosts =
            {
                new TestHostProvider(provider, replace),
            }
        }));
        Assert.Equal(html, expected);
    }

    [Theory]
    [InlineData("![static mp4](//sample.com/video.mp4)", "<p><video width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"//sample.com/video.mp4\"></source></video></p>\n", "")]
    [InlineData(@"![youtube.com](https://www.youtube.com/watch?v=mswPy5bt3TQ)", "<p><iframe src=\"https://www.youtube.com/embed/mswPy5bt3TQ\" class=\"youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n", "")]
    [InlineData("![static mp4](//sample.com/video.mp4)", "<p><video class=\"k\" width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"//sample.com/video.mp4\"></source></video></p>\n", "k")]
    [InlineData(@"![youtube.com](https://www.youtube.com/watch?v=mswPy5bt3TQ)", "<p><iframe src=\"https://www.youtube.com/embed/mswPy5bt3TQ\" class=\"k youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n", "k")]
    public void TestCustomClass(string markdown, string expected, string klass)
    {
        string html = MarkdownConverter.ToHtml(markdown, GetPipeline(new MediaOptions
        {
            Class = klass,
        }));
        Assert.Equal(html, expected);
    }

    [Theory]
    [InlineData("![static mp4](//sample.com/video.mp4)", "<p><video class=\"img-fluid\" width=\"500\" height=\"281\" controls=\"\"><source type=\"video/mp4\" src=\"//sample.com/video.mp4\"></source></video></p>\n")]
    [InlineData(@"![youtube.com](https://www.youtube.com/watch?v=mswPy5bt3TQ)", "<p><iframe src=\"https://www.youtube.com/embed/mswPy5bt3TQ\" class=\"img-fluid youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n")]
    public void TestWithBootstrap(string markdown, string expected)
    {
        string html = MarkdownConverter.ToHtml(markdown, GetPipelineWithBootstrap());
        Assert.Equal(html, expected);
    }
}
