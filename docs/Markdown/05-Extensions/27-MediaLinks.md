---
title: Media links
description: Embed audio, videos and YouTube or Vimeo players with the image syntax.
---

Markdown has images but no audio or video. [MediaLinks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.MediaLinks)
uses the image syntax to embed them: an image of an audio or a video file, such as `![](song.mp3)`, is rendered as
`<audio>` or `<video>`, and one of YouTube, Vimeo, Yandex Music or Odnoklassniki as an `<iframe>` of their player.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_MediaLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = MarkdownSyntaxTree.ParseText("![Intro](https://youtu.be/mswPy5bt3TQ)\n\n![Song](song.mp3)\n", new MarkdownParseOptions { Extensions = MarkdownExtensions.MediaLinks });

Assert.Equal("<p><iframe src=\"https://www.youtube.com/embed/mswPy5bt3TQ\" class=\"youtube\" width=\"500\" height=\"281\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n<p><audio width=\"500\" controls=\"\"><source type=\"audio/mpeg\" src=\"song.mp3\"></source></audio></p>\n", MarkdownHtmlRenderer.ToHtml(tree));

// The options choose the size, and the sites that are an iframe
var options = new MarkdownHtmlRendererOptions
{
    MediaLinks = MarkdownMediaLinkOptions.Default with
    {
        Width = "100%",
        Height = "",
        Hosts = [MarkdownMediaHost.Create("videos.example.com", uri => "https://videos.example.com/embed" + uri.AbsolutePath, iframeClass: "example")],
    },
};
Assert.Equal("<p><iframe src=\"https://videos.example.com/embed/42\" class=\"example\" width=\"100%\" frameborder=\"0\" allowfullscreen=\"\"></iframe></p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("![](https://videos.example.com/42)\n", tree.Options), options));
```
<!-- endref -->

[MarkdownHtmlRendererOptions.MediaLinks](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.MediaLinks)
chooses the size, the class, the file extensions and the sites. By default, the file extensions are the formats that
browsers play: MP4, WebM, Ogg, QuickTime, Matroska, 3GP and MPEG videos, and MP3, AAC, Ogg, Opus, WAV, WebM, FLAC,
Matroska and AIFF audio. An image of another format, such as `.avi` or `.wmv`, stays an image, which links to the file,
and [ExtensionToMimeType](cref:Meziantou.Framework.Markdown.MarkdownMediaLinkOptions.ExtensionToMimeType) adds formats.
[MarkdownMediaHost.Create](cref:Meziantou.Framework.Markdown.MarkdownMediaHost.Create(System.String,System.Func{System.Uri,System.String},System.Boolean,System.String))
makes a site of its own.

The syntax tree does not change: the media are
[MarkdownLinkSyntax](cref:Meziantou.Framework.Markdown.MarkdownLinkSyntax) images.
