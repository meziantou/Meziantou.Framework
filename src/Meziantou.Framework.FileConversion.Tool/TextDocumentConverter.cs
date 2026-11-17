using System.Net;
using Meziantou.Framework.Markdown;
using Meziantou.Framework.WebPlatform;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>Converts Markdown documents to HTML, and HTML documents to Markdown.</summary>
internal static class TextDocumentConverter
{
    /// <summary>Creates the Markdown options of the extensions of <c>--markdown-extensions</c>, such as <c>advanced+emojis</c>.</summary>
    /// <exception cref="ArgumentException">An extension is not valid.</exception>
    public static (MarkdownParseOptions ParseOptions, MarkdownHtmlRendererOptions RendererOptions) CreateOptions(string extensions, bool disableHtml)
    {
        // The front matter holds metadata, such as the title: it is not content
        var flags = MarkdownExtensions.YamlFrontMatter;
        var pipeTables = false;
        var escapeNonAscii = true;
        List<string>? linkRel = null;
        foreach (var extension in extensions.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (extension.ToLowerInvariant())
            {
                case "common" or "yaml":
                    break;
                case "nohtml":
                    disableHtml = true;
                    break;
                case "nonascii-noescape":
                    escapeNonAscii = false;
                    break;
                case "nofollowlinks" or "noopenerlinks" or "noreferrerlinks":
                    (linkRel ??= []).Add(extension[..^"links".Length].ToLowerInvariant());
                    break;
                case "pipetables":
                    pipeTables = true;
                    break;
                default:
                    flags |= extension.ToLowerInvariant() switch
                    {
                        "advanced" => MarkdownExtensions.Advanced,
                        "alerts" => MarkdownExtensions.Alerts,
                        "gfm-pipetables" => MarkdownExtensions.Tables,
                        "emphasisextras" => MarkdownExtensions.EmphasisExtras,
                        "listextras" => MarkdownExtensions.ListExtras,
                        "hardlinebreak" => MarkdownExtensions.HardLineBreaks,
                        "footnotes" => MarkdownExtensions.Footnotes,
                        "footers" => MarkdownExtensions.Footers,
                        "citations" => MarkdownExtensions.Citations,
                        "attributes" => MarkdownExtensions.GenericAttributes,
                        "gridtables" => MarkdownExtensions.GridTables,
                        "abbreviations" => MarkdownExtensions.Abbreviations,
                        "emojis" => MarkdownExtensions.Emoji,
                        "definitionlists" => MarkdownExtensions.DefinitionLists,
                        "customcontainers" => MarkdownExtensions.CustomContainers,
                        "figures" => MarkdownExtensions.Figures,
                        "mathematics" => MarkdownExtensions.Mathematics,
                        "bootstrap" => MarkdownExtensions.Bootstrap,
                        "medialinks" => MarkdownExtensions.MediaLinks,
                        "smartypants" => MarkdownExtensions.SmartyPants,
                        "autoidentifiers" => MarkdownExtensions.AutoIdentifiers,
                        "tasklists" => MarkdownExtensions.TaskLists,
                        "diagrams" => MarkdownExtensions.Diagrams,
                        "autolinks" => MarkdownExtensions.MarkdigAutolinks,
                        "globalization" => MarkdownExtensions.Globalization,
                        "cjk-friendly-emphasis" => MarkdownExtensions.CjkFriendlyEmphasis,
                        _ => throw new ArgumentException($"Invalid extension `{extension}` from `{extensions}`", nameof(extensions)),
                    };
                    break;
            }
        }

        if (pipeTables)
        {
            flags |= MarkdownExtensions.PipeTables;
        }

        var parseOptions = new MarkdownParseOptions { Extensions = flags, DisableHtml = disableHtml };
        var rendererOptions = new MarkdownHtmlRendererOptions
        {
            EscapeNonAsciiUrlCharacters = escapeNonAscii,
            LinkRel = linkRel is null ? null : string.Join(' ', linkRel.Distinct(StringComparer.Ordinal)),
        };
        return (parseOptions, rendererOptions);
    }

    /// <summary>Converts a Markdown document to HTML: a complete document, or the content of its body.</summary>
    /// <param name="markdown">The Markdown document.</param>
    /// <param name="defaultTitle">The title of the HTML document when the Markdown document has no title in its front matter and no heading.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="fragment">Whether only the content of the body is written, without <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c> and <c>&lt;body&gt;</c>.</param>
    /// <returns>The HTML, and the title of the document.</returns>
    public static (string Html, string Title) MarkdownToHtml(string markdown, string defaultTitle, ConversionSettings settings, bool fragment)
    {
        var (parseOptions, rendererOptions) = CreateOptions(settings.MarkdownExtensions, settings.MarkdownDisableHtml);
        var tree = MarkdownSyntaxTree.ParseText(markdown, parseOptions);
        var body = MarkdownHtmlRenderer.ToHtml(tree, rendererOptions);
        var title = settings.HtmlTitle ?? GetTitle(tree) ?? defaultTitle;
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
    internal static string? GetTitle(MarkdownSyntaxTree tree)
    {
        var root = tree.GetRoot();
        if (root.DescendantNodes().OfType<MarkdownFrontMatterSyntax>().FirstOrDefault() is { } frontMatter)
        {
            foreach (var text in frontMatter.Yaml.Split('\n'))
            {
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

        if (root.DescendantNodes().OfType<MarkdownHeadingSyntax>().FirstOrDefault() is { } heading)
        {
            var title = heading.GetPlainText().Trim();
            if (title.Length > 0)
                return title.ReplaceLineEndings(" ");
        }

        return null;
    }
}
