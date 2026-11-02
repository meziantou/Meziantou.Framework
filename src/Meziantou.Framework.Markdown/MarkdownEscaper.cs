using System.Text;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Normalize.Inlines;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// Provides methods to write text into Markdown, so that it is parsed back to the same text instead of being interpreted as
/// Markdown syntax.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs" ref:region="MarkdownEscaper_Usage" ref:dedent="true">
/// // Escape the text so that Markdown displays it as is: every ASCII punctuation character is escaped
/// Assert.Equal(@"\*Note\* \[docs\]\!", MarkdownEscaper.Escape("*Note* [docs]!"));
///
/// // The delimiters of code are longer than the runs of backticks of the content
/// Assert.Equal("``a`b``", MarkdownEscaper.CreateCodeSpan("a`b"));
/// Assert.Equal("`` `code` ``", MarkdownEscaper.CreateCodeSpan("`code`"));
/// Assert.Equal("```csharp\nvar a = 1;\n```\n", MarkdownEscaper.CreateFencedCodeBlock("var a = 1;", "csharp"));
///
/// // Build a link from untrusted parts
/// var text = MarkdownEscaper.Escape("Docs [v2]");
/// var destination = MarkdownEscaper.EscapeLinkDestination("https://example.com/a b");
/// var title = MarkdownEscaper.EscapeLinkTitle("Say \"hi\"");
/// Assert.Equal("&lt;https://example.com/a b&gt;", destination);
/// Assert.Equal("\"Say \\\"hi\\\"\"", title);
///
/// var html = MarkdownConverter.ToHtml($"[{text}]({destination} {title})");
/// Assert.Equal("&lt;p&gt;&lt;a href=\"https://example.com/a%20b\" title=\"Say &amp;quot;hi&amp;quot;\"&gt;Docs [v2]&lt;/a&gt;&lt;/p&gt;\n", html);
///
/// // The label of a reference link
/// Assert.Equal(@"see \[1\]", MarkdownEscaper.EscapeLinkLabel("see [1]"));
///
/// // A cell of a pipe table cannot contain a pipe or a line ending
/// Assert.Equal(@"a \| b c", MarkdownEscaper.EscapeTableCell("a | b\nc"));
/// </code>
/// </example>
public static class MarkdownEscaper
{
    // https://spec.commonmark.org/0.31.2/#link-label
    private const int MaximumLinkLabelLength = 999;

    /// <summary>
    /// Escapes every ASCII punctuation character with a backslash, so that the text is displayed as is.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>The escaped text.</returns>
    /// <remarks>
    /// The whitespace at the start and at the end of a line is not preserved, and a blank line starts a new paragraph.
    /// </remarks>
    public static string Escape(string text)
    {
        var index = IndexOfAsciiPunctuation(text);
        if (index < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 8);
        builder.Append(text, 0, index);
        for (var i = index; i < text.Length; i++)
        {
            var c = text[i];
            if (c.IsAsciiPunctuation())
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates an inline code span that contains the specified text.
    /// </summary>
    /// <param name="content">The content of the code span.</param>
    /// <returns>The code span, including its backtick delimiters.</returns>
    /// <remarks>
    /// The delimiters are one backtick longer than the longest run of backticks of the content. The parser converts the line
    /// endings of a code span to spaces, and a blank line ends the paragraph.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="content"/> is empty: a code span cannot be empty.</exception>
    public static string CreateCodeSpan(string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);

        var delimiterCount = GetLongestRun(content, '`') + 1;

        // A backtick next to the delimiter would lengthen it, and the parser strips one space on each side when both are present
        var pad = content[0] == '`'
            || content[^1] == '`'
            || (IsSpaceOrLineEnding(content[0]) && IsSpaceOrLineEnding(content[^1]) && content.AsSpan().ContainsAnyExcept(" \r\n"));

        var builder = new StringBuilder(content.Length + (delimiterCount * 2) + 2);
        builder.Append('`', delimiterCount);
        if (pad)
        {
            builder.Append(' ');
        }

        builder.Append(content);
        if (pad)
        {
            builder.Append(' ');
        }

        builder.Append('`', delimiterCount);
        return builder.ToString();
    }

    /// <summary>
    /// Creates a fenced code block that contains the specified code.
    /// </summary>
    /// <param name="code">The code. The lines are written as is.</param>
    /// <param name="infoString">The info string, such as the language of the code (<c>csharp</c>), or <see langword="null"/> for none.</param>
    /// <returns>The fenced code block, ending with a line ending.</returns>
    /// <remarks>
    /// The fence is at least three backticks, and longer than any line of the code that would close it. A line ending at the
    /// end of <paramref name="code"/> is optional: it does not add an empty line to the block.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="infoString"/> contains a line ending.</exception>
    public static string CreateFencedCodeBlock(string code, string? infoString = null)
    {
        var fenceLength = 3;
        foreach (var line in code.AsSpan().EnumerateLines())
        {
            // A line made only of backticks, as long as the fence, would close the block
            var trimmed = line.TrimStart(" \t");
            var run = trimmed.Length - trimmed.TrimStart('`').Length;
            if (run >= fenceLength && trimmed[run..].IsWhiteSpace())
            {
                fenceLength = run + 1;
            }
        }

        var builder = new StringBuilder(code.Length + (fenceLength * 2) + 16);
        builder.Append('`', fenceLength);
        if (!string.IsNullOrWhiteSpace(infoString))
        {
            AppendInfoString(builder, infoString.Trim());
        }

        builder.Append('\n');
        if (code.Length > 0)
        {
            builder.Append(code);
            if (code[^1] is not ('\n' or '\r'))
            {
                builder.Append('\n');
            }
        }

        builder.Append('`', fenceLength);
        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>
    /// Escapes a URL, so that it can be used as the destination of a link or an image: <c>[text](destination)</c>.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns>The escaped destination. It is enclosed in angle brackets when the URL is empty or contains spaces.</returns>
    public static string EscapeLinkDestination(string url) => LinkInlineRenderer.EscapeDestination(url);

    /// <summary>
    /// Escapes a link title and encloses it in double quotes, so that it can be used as the title of a link, an image, or a
    /// link reference definition: <c>[text](destination "title")</c>.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The escaped title, enclosed in double quotes.</returns>
    public static string EscapeLinkTitle(string title) => LinkInlineRenderer.EscapeTitle(title);

    /// <summary>
    /// Escapes a link label, so that it can be used in a reference link and its link reference definition:
    /// <c>[text][label]</c> and <c>[label]: destination</c>.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns>The escaped label, without the brackets.</returns>
    /// <remarks>The parser matches the labels case-insensitively, and collapses consecutive whitespace.</remarks>
    /// <exception cref="ArgumentException"><paramref name="label"/> is blank, or longer than 999 characters once escaped.</exception>
    public static string EscapeLinkLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var escaped = LinkInlineRenderer.EscapeLabel(label);
        if (escaped.Length > MaximumLinkLabelLength)
        {
            throw new ArgumentException($"A link label cannot be longer than {MaximumLinkLabelLength} characters.", nameof(label));
        }

        return escaped;
    }

    /// <summary>
    /// Escapes the text of a cell of a pipe table, so that it is displayed as is.
    /// </summary>
    /// <param name="text">The text of the cell.</param>
    /// <returns>The escaped text.</returns>
    /// <remarks>
    /// The text is escaped as with <see cref="Escape(string)"/>, which also escapes the pipes. A row of a pipe table cannot span
    /// several lines, so the line endings are replaced by spaces. To write a code span in a cell, replace the pipes of the
    /// result of <see cref="CreateCodeSpan(string)"/> with <c>\|</c>.
    /// </remarks>
    public static string EscapeTableCell(string text)
    {
        var escaped = Escape(text);
        if (escaped.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            return escaped;
        }

        return escaped.ReplaceLineEndings(" ");
    }

    private static void AppendInfoString(StringBuilder builder, string infoString)
    {
        // The info string cannot contain a backtick after a backtick fence; it is unescaped and its entities are decoded
        for (var i = 0; i < infoString.Length; i++)
        {
            var c = infoString[i];
            if (c is '\r' or '\n')
            {
                throw new ArgumentException("The info string cannot contain a line ending.", nameof(infoString));
            }

            if (c == '`')
            {
                builder.Append("&#96;");
            }
            else if (c == '&' && LinkInlineRenderer.IsEntityStart(infoString.AsSpan(i + 1)))
            {
                builder.Append("&amp;");
            }
            else if (c == '\\' && (i + 1 == infoString.Length || infoString[i + 1].IsAsciiPunctuation() || char.IsWhiteSpace(infoString[i + 1])))
            {
                builder.Append(@"\\");
            }
            else
            {
                builder.Append(c);
            }
        }
    }

    private static int IndexOfAsciiPunctuation(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i].IsAsciiPunctuation())
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetLongestRun(string text, char c)
    {
        var longest = 0;
        var current = 0;
        foreach (var ch in text)
        {
            current = ch == c ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private static bool IsSpaceOrLineEnding(char c) => c is ' ' or '\r' or '\n';
}
