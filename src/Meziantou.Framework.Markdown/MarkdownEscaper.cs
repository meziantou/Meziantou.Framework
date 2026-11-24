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
    /// <para>
    /// The spaces and the tabs that start or end a line are written as entities, such as <c>&amp;#32;</c>: the parser would
    /// strip them, and they would start an indented code block or end the line with a hard line break. So the text stays text
    /// whatever its whitespace, and the parser decodes the entities to the same text. An empty line still ends the paragraph,
    /// and the parser replaces U+0000 with U+FFFD.
    /// </para>
    /// <para>
    /// The extensions and the syntax extensions of the parse options are not taken into account: use
    /// <see cref="Escape(string, MarkdownParseOptions)"/> when the text is parsed with them.
    /// </para>
    /// </remarks>
    public static string Escape(string text) => EscapeText(text, options: null);

    /// <summary>
    /// Escapes the text, as <see cref="Escape(string)"/> does, and the characters where the extensions of the parse options
    /// would start a construct, so that the text is displayed as is when it is parsed with these options.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <param name="options">The options that parse the Markdown the text is written into.</param>
    /// <returns>The escaped text.</returns>
    /// <remarks>
    /// <para>
    /// In addition to what <see cref="Escape(string)"/> escapes, the trigger characters of the inline extensions of
    /// <see cref="MarkdownParseOptions.SyntaxExtensions"/>, the trigger characters of the fenced block extensions that start a
    /// line, and the first character of the shortcodes and the smileys of <see cref="MarkdownParseOptions.EmojiMapping"/> that
    /// the text contains when <see cref="MarkdownExtensions.Emoji"/> is enabled, are written as entities, such as
    /// <c>&amp;#167;</c>, when they are not ASCII punctuation, which is escaped with a backslash.
    /// </para>
    /// <para>
    /// The text is not protected from what the escaper cannot know or change: the abbreviations of
    /// <see cref="MarkdownExtensions.Abbreviations"/> are defined elsewhere in the document and match the words of the text,
    /// the paragraph extensions read the lines of the paragraph that contains the text, and an inline extension whose trigger
    /// character is <c>&amp;</c> is tried on the entities.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static string Escape(string text, MarkdownParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return EscapeText(text, options);
    }

    /// <summary>
    /// Creates an inline code span that contains the specified text.
    /// </summary>
    /// <param name="content">The content of the code span.</param>
    /// <returns>The code span, including its backtick delimiters.</returns>
    /// <remarks>
    /// The delimiters are one backtick longer than the longest run of backticks of the content. The line endings of the
    /// content are replaced with spaces, as the parser converts them: a line of the code span could start a block, such as
    /// <c># a</c>, and a blank line would end the paragraph.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="content"/> is empty: a code span cannot be empty.</exception>
    public static string CreateCodeSpan(string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);

        content = ReplaceLineEndingsWithSpaces(content);
        var delimiterCount = GetLongestRun(content, '`') + 1;

        // A backtick next to the delimiter would lengthen it, and the parser strips one space on each side when both are present
        var pad = content[0] == '`'
            || content[^1] == '`'
            || (content[0] == ' ' && content[^1] == ' ' && content.AsSpan().ContainsAnyExcept(' '));

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
    /// <remarks>
    /// The parser matches the labels case-insensitively, and collapses consecutive whitespace, so the line endings of the label
    /// are replaced with spaces: a line of the label could start a block, such as <c># a</c>, and a label cannot contain a
    /// blank line. A <c>^</c> that starts the label, after its spaces, is escaped, as <c>[^label]</c> is a footnote when
    /// <see cref="MarkdownExtensions.Footnotes"/> is enabled.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="label"/> is blank, or longer than 999 characters once escaped.</exception>
    public static string EscapeLinkLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        var escaped = Escape(ReplaceLineEndingsWithSpaces(label), "[]\\", decodedEntities: false);

        // The label of a footnote starts with ^ once its spaces and tabs are trimmed
        var start = escaped.AsSpan().IndexOfAnyExcept(' ', '\t');
        if (escaped[start] == '^')
        {
            escaped = escaped.Insert(start, "\\");
        }

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
    /// A row of a pipe table cannot span several lines, so the line endings are replaced by spaces. Then the text is escaped as
    /// with <see cref="Escape(string)"/>, which also escapes the pipes. The parser trims the spaces, the tabs, the vertical tabs
    /// and the form feeds around the text of a cell, so the ones that start or end the text are written as entities, such as
    /// <c>&amp;#11;</c>. To write a code span in a cell, replace the pipes of the result of <see cref="CreateCodeSpan(string)"/>
    /// with <c>\|</c>.
    /// </remarks>
    public static string EscapeTableCell(string text)
    {
        text = ReplaceLineEndingsWithSpaces(text);

        // Escape writes the spaces and the tabs that start or end the text as entities, but not the vertical tabs and the form
        // feeds, nor the spaces and the tabs that follow or precede them
        var start = 0;
        while (start < text.Length && TableScanner.IsSpace(text[start]))
        {
            start++;
        }

        var end = text.Length;
        while (end > start && TableScanner.IsSpace(text[end - 1]))
        {
            end--;
        }

        if (start == 0 && end == text.Length)
        {
            return Escape(text);
        }

        var builder = new StringBuilder(text.Length + (8 * (text.Length - end + start)));
        AppendCharacterReferences(builder, text.AsSpan(0, start));
        builder.Append(Escape(text[start..end]));
        AppendCharacterReferences(builder, text.AsSpan(end));
        return builder.ToString();

        static void AppendCharacterReferences(StringBuilder builder, ReadOnlySpan<char> value)
        {
            foreach (var c in value)
            {
                builder.Append("&#").Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append(';');
            }
        }
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
        // The info string cannot contain a backtick after a backtick fence, and a { would start generic attributes; it is
        // unescaped and its entities are decoded
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
            else if (c == '{')
            {
                builder.Append("&#123;");
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

    private static string EscapeText(string text, MarkdownParseOptions? options)
    {
        // The characters that an extension would read, which are written as entities
        var entities = options is null ? null : GetExtensionCharacters(text, options);

        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is ' ' or '\t')
            {
                // The parser strips the spaces and the tabs around the lines of a paragraph: at the start of a line, they could
                // start an indented code block, and two spaces at the end of a line are a hard line break
                if (i == 0 || text[i - 1] is '\r' or '\n' || i + 1 == text.Length || text[i + 1] is '\r' or '\n')
                {
                    builder ??= new StringBuilder(text, 0, i, text.Length + 16);
                    builder.Append(c == ' ' ? "&#32;" : "&#9;");
                    continue;
                }
            }
            else if (Scanners.IsEscapable(c))
            {
                builder ??= new StringBuilder(text, 0, i, text.Length + 16);
                builder.Append('\\');
            }
            else if (entities is not null && entities[i])
            {
                builder ??= new StringBuilder(text, 0, i, text.Length + 16);
                builder.Append("&#").Append(char.ConvertToUtf32(text, i).ToString(CultureInfo.InvariantCulture)).Append(';');
                i += char.IsHighSurrogate(c) ? 1 : 0;
                continue;
            }

            builder?.Append(c);
        }

        return builder?.ToString() ?? text;
    }

    // The characters, which a backslash cannot escape, where an extension would start a construct: the trigger characters of the
    // inline extensions, the ones of the fenced block extensions that start a line, and the first character of a shortcode or of
    // a smiley. It is null when there is none.
    private static bool[]? GetExtensionCharacters(string text, MarkdownParseOptions options)
    {
        string? inlineTriggers = null;
        string? fencedBlockTriggers = null;
        foreach (var extension in options.SyntaxExtensions)
        {
            switch (extension)
            {
                case MarkdownInlineExtension inline:
                    inlineTriggers += inline.TriggerCharacters;
                    break;

                case MarkdownFencedBlockExtension fencedBlock:
                    fencedBlockTriggers += fencedBlock.TriggerCharacters;
                    break;
            }
        }

        var emoji = (options.Extensions & MarkdownExtensions.Emoji) != MarkdownExtensions.None ? options.EmojiMapping : null;
        if (inlineTriggers is null && fencedBlockTriggers is null && emoji is null)
        {
            return null;
        }

        // From the end, so that a smiley is read with the characters after it as they are written: an escaped character, a line
        // ending or an entity is not part of its text, and is not a letter that it would end before
        bool[]? entities = null;
        var end = text.Length;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c is '\r' or '\n' || Scanners.IsEscapable(c))
            {
                end = i;
                continue;
            }

            if (c is ' ' or '\t')
            {
                continue;
            }

            // A pair of surrogates is one entity, written at its high surrogate. A lone surrogate is not escaped: the parser would
            // decode its entity to U+FFFD.
            var lowSurrogate = '\0';
            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 == text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    continue;
                }

                lowSurrogate = text[i + 1];
            }

            var lineStart = i == 0 || text[i - 1] is '\r' or '\n';
            if (IsTrigger(inlineTriggers, c, lowSurrogate) || (lineStart && IsTrigger(fencedBlockTriggers, c, lowSurrogate)) || emoji?.MatchLongest(text.AsSpan(i, end - i)) is not null)
            {
                (entities ??= new bool[text.Length])[i] = true;
                end = i;
            }
        }

        return entities;
    }

    private static bool IsTrigger(string? triggers, char c, char lowSurrogate)
    {
        return triggers is not null && (triggers.Contains(c, StringComparison.Ordinal) || (lowSurrogate != '\0' && triggers.Contains(lowSurrogate, StringComparison.Ordinal)));
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

    // Only the line endings of Markdown: U+0085, U+2028 and U+2029 are text
    private static string ReplaceLineEndingsWithSpaces(string text)
    {
        if (text.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            return text;
        }

        return text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    }

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
