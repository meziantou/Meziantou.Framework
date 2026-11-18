using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// Provides methods to write text into Markdown, so that it is parsed back to the same text instead of being interpreted as
/// Markdown syntax.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownEscaper_Usage" ref:dedent="true">
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
/// var html = MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText($"[{text}]({destination} {title})"));
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
            if (Scanners.IsEscapable(c))
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
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "A destination is the text of a link, which can be relative or not a valid URI")]
    public static string EscapeLinkDestination(string url) => EscapeDestination(url);

    /// <summary>
    /// Escapes a link title and encloses it in double quotes, so that it can be used as the title of a link, an image, or a
    /// link reference definition: <c>[text](destination "title")</c>.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The escaped title, enclosed in double quotes.</returns>
    public static string EscapeLinkTitle(string title) => EscapeTitle(title);

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

        var escaped = Escape(label, "[]\\", decodedEntities: false);
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

        // Only the line endings of Markdown: U+0085, U+2028 and U+2029 are text
        return escaped.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    }

    // The info string of a fenced code block, whose fence is not written
    internal static string EscapeInfoString(string infoString)
    {
        var builder = new StringBuilder(infoString.Length);
        AppendInfoString(builder, infoString);
        return builder.ToString();
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
            else if (c == '&' && IsEntityStart(infoString.AsSpan(i + 1)))
            {
                builder.Append("&amp;");
            }
            else if (c == '\\' && (i + 1 == infoString.Length || Scanners.IsEscapable(infoString[i + 1]) || char.IsWhiteSpace(infoString[i + 1])))
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
            if (Scanners.IsEscapable(text[i]))
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

    private static string EscapeDestination(string url)
    {
        // A destination with spaces or ASCII control characters, or an empty one, needs angle brackets. The unbalanced
        // parentheses of another one are escaped: in angle brackets, they would be balanced for a link around this one, such as
        // the text before [](<)>).
        var depth = 0;
        var needsBrackets = url.Length == 0;
        var unbalanced = url.Length > 0 && url[0] == '<';
        foreach (var c in url)
        {
            // The parser takes the other Unicode spaces, such as a no-break space, for the end of a destination
            if (c <= ' ' || char.IsControl(c) || (c > (char)0x7F && PipeTableScanner.IsWhitespace(c)))
            {
                needsBrackets = true;
                break;
            }

            if (c == ')' && --depth < 0)
            {
                unbalanced = true;
            }
            else if (c == '(')
            {
                depth++;
            }
        }

        unbalanced |= depth != 0;
        var escaped = needsBrackets ? "<" + Escape(url, "<>", decodedEntities: true) + ">" : Escape(url, unbalanced ? "()<" : "", decodedEntities: true);

        // A destination cannot contain a line ending, or another control character, so they are written as entities, which the
        // parser decodes
        if ((url.AsSpan().ContainsAnyInRange('\0', (char)0x1F) || url.AsSpan().ContainsAnyInRange((char)0x7F, (char)0x9F)))
        {
            var builder = new StringBuilder(escaped.Length + 8);
            foreach (var c in escaped)
            {
                if (char.IsControl(c) && c is not '\t')
                {
                    // A backslash before the line ending is text, which would escape the ampersand of the entity
                    if (builder.Length > 0 && builder[^1] == '\\')
                    {
                        builder.Append('\\');
                    }

                    builder.Append("&#x").Append(((int)c).ToString("X", CultureInfo.InvariantCulture)).Append(';');
                }
                else
                {
                    builder.Append(c);
                }
            }

            escaped = builder.ToString();
        }

        return escaped;
    }

    private static string EscapeTitle(string title)
    {
        var inner = Escape(title, "\"", decodedEntities: true);

        // A line of the title could end the paragraph, as a blank line or a line that starts a block such as "# a", so the line
        // endings are written as entities, which the parser decodes
        if (inner.AsSpan().ContainsAny('\n', '\r'))
        {
            var builder = new StringBuilder(inner.Length + 16);
            foreach (var c in inner)
            {
                if (c is '\n' or '\r')
                {
                    // A backslash before the line ending is text, which would escape the ampersand of the entity
                    if (builder.Length > 0 && builder[^1] == '\\')
                    {
                        builder.Append('\\');
                    }

                    builder.Append(c == '\n' ? "&#10;" : "&#13;");
                }
                else
                {
                    builder.Append(c);
                }
            }

            inner = builder.ToString();
        }

        return "\"" + inner + "\"";
    }

    private static string Escape(string text, string characters, bool decodedEntities)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (decodedEntities && c == '&' && IsEntityStart(text.AsSpan(i + 1)))
            {
                // The entity is decoded once, even where a backslash escape is not honored
                builder ??= new StringBuilder(text, 0, i, text.Length + 8);
                builder.Append("&amp;");
                continue;
            }

            if (characters.Contains(c, StringComparison.Ordinal) || (c == '\\' && (i + 1 == text.Length || Scanners.IsEscapable(text[i + 1]))))
            {
                builder ??= new StringBuilder(text, 0, i, text.Length + 8);
                builder.Append('\\');
            }

            builder?.Append(c);
        }

        return builder?.ToString() ?? text;
    }

    private static bool IsEntityStart(ReadOnlySpan<char> text)
    {
        var length = text.Length > 0 && text[0] == '#' ? 1 : 0;
        while (length < text.Length && char.IsAsciiLetterOrDigit(text[length]))
        {
            length++;
        }

        return length > 0 && length < text.Length && text[length] == ';';
    }
}
