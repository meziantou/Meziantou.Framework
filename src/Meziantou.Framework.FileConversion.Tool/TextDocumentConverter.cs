using System.Net;
using Meziantou.Framework.Markdown;
using Meziantou.Framework.Markdown.Extensions.Yaml;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.WebPlatform;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>Converts Markdown documents to HTML, and HTML documents to Markdown.</summary>
internal static class TextDocumentConverter
{
    /// <summary>Creates the Markdown pipeline of the extensions of <c>--markdown-extensions</c>, such as <c>advanced+emojis</c>.</summary>
    /// <exception cref="ArgumentException">An extension is not valid.</exception>
    public static MarkdownPipeline CreatePipeline(string extensions, bool disableHtml)
    {
        var builder = new MarkdownPipelineBuilder().Configure(extensions);

        // The front matter holds metadata, such as the title: it is not content
        builder.UseYamlFrontMatter();
        if (disableHtml)
        {
            builder.DisableHtml();
        }

        return builder.Build();
    }

    /// <summary>Converts a Markdown document to HTML: a complete document, or the content of its body.</summary>
    /// <param name="markdown">The Markdown document.</param>
    /// <param name="defaultTitle">The title of the HTML document when the Markdown document has no title in its front matter and no heading.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="fragment">Whether only the content of the body is written, without <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c> and <c>&lt;body&gt;</c>.</param>
    /// <returns>The HTML, and the title of the document.</returns>
    /// <exception cref="ArgumentException">The document is nested too deeply.</exception>
    public static (string Html, string Title) MarkdownToHtml(string markdown, string defaultTitle, ConversionSettings settings, bool fragment)
    {
        var pipeline = CreatePipeline(settings.MarkdownExtensions, settings.MarkdownDisableHtml);
        var document = MarkdownConverter.Parse(markdown, pipeline);
        var body = document.ToHtml(pipeline);
        var title = settings.HtmlTitle ?? GetTitle(markdown, document, pipeline) ?? defaultTitle;
        if (fragment)
            return (body, title);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>\n");
        foreach (var stylesheet in settings.HtmlStylesheets)
        {
            sb.Append("<link rel=\"stylesheet\" href=\"").Append(WebUtility.HtmlEncode(stylesheet)).Append("\">\n");
        }

        sb.Append("</head>\n<body>\n").Append(body);
        if (body.Length > 0 && body[^1] is not '\n')
        {
            sb.Append('\n');
        }

        sb.Append("</body>\n</html>\n");
        return (sb.ToString(), title);
    }

    /// <summary>Converts an HTML document to Markdown: the content of its body, or the whole text when it has no body.</summary>
    public static string HtmlToMarkdown(string html, ConversionSettings settings)
    {
        // The converter handles HTML fragments: the head of a complete document is not content
        var body = HtmlDocumentParser.ParseDocument(html).Body?.InnerHtml ?? html;
        var markdown = Framework.HtmlToMarkdown.Convert(body, settings.MarkdownOutput);
        return markdown.Length > 0 ? markdown + "\n" : markdown;
    }

    /// <summary>Gets the title of a Markdown document: the <c>title</c> of its YAML front matter, or the text of its first heading.</summary>
    internal static string? GetTitle(string markdown, MarkdownDocument document, MarkdownPipeline pipeline)
    {
        if (document.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } frontMatter)
        {
            foreach (var line in (frontMatter.Lines.Lines ?? []).Take(frontMatter.Lines.Count))
            {
                var text = line.Slice.ToString();
                if (!text.StartsWith("title:", StringComparison.Ordinal))
                    continue;

                var value = text["title:".Length..].Trim();
                if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
                {
                    value = value[1..^1];
                }

                if (value.Length > 0)
                    return value;
            }
        }

        if (document.Descendants<HeadingBlock>().FirstOrDefault() is { } heading && heading.Span.Start >= 0 && heading.Span.End < markdown.Length)
        {
            var title = MarkdownConverter.ToPlainText(markdown[heading.Span.Start..(heading.Span.End + 1)], pipeline).Trim();
            if (title.Length > 0)
                return title.ReplaceLineEndings(" ");
        }

        return null;
    }
}
