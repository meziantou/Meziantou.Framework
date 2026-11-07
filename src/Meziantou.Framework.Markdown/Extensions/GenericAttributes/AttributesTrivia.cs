using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// With trivia, what the roundtrip renderer needs to write back the attributes of a block or an inline that were changed after
/// the parsing: the attributes when the parsing ended, the attributes written in the text, and where this text is.
/// </summary>
internal sealed class AttributesTrivia
{
    private static readonly object Key = typeof(AttributesTrivia);
    private static readonly object DocumentKey = new();

    /// <summary>
    /// Gets the attributes when the parsing ended, or <see langword="null"/> when there were none.
    /// </summary>
    public HtmlAttributes? Parsed { get; private set; }

    /// <summary>
    /// Gets the attributes written in the text and attached to the object, merged.
    /// </summary>
    public HtmlAttributes Source { get; } = new();

    /// <summary>
    /// Gets the text of the attributes that end the line of an ATX heading or the opening line of a fenced block, in its trivia,
    /// or an empty slice.
    /// </summary>
    public StringSlice TriviaAfterAttributes { get; private set; } = StringSlice.Empty;

    /// <summary>
    /// Gets the texts of the lines before the block that only have attributes, from the paragraphs that were removed.
    /// </summary>
    public List<string>? LinesBefore { get; private set; }

    /// <summary>
    /// Gets the attributes written in the inlines, attached to the object.
    /// </summary>
    public List<GenericAttributesInline>? Inlines { get; private set; }

    /// <summary>
    /// Gets whether attributes written in the text are attached to the object.
    /// </summary>
    public bool HasSourceText => !TriviaAfterAttributes.IsEmpty || LinesBefore is not null || Inlines is not null;

    public static AttributesTrivia? Get(MarkdownObject obj) => obj.GetData(Key) as AttributesTrivia;

    public static AttributesTrivia GetOrCreate(MarkdownObject obj)
    {
        if (obj.GetData(Key) is not AttributesTrivia trivia)
        {
            trivia = new AttributesTrivia();
            obj.SetData(Key, trivia);
        }

        return trivia;
    }

    /// <summary>
    /// Records the attributes that end the line of an ATX heading, or the opening line of a fenced block.
    /// </summary>
    public void AddTriviaAfterAttributes(HtmlAttributes attributes, StringSlice text)
    {
        AddSource(attributes);
        TriviaAfterAttributes = text;
    }

    /// <summary>
    /// Records attributes written in the inlines.
    /// </summary>
    public void AddInline(HtmlAttributes attributes, GenericAttributesInline inline)
    {
        AddSource(attributes);
        (Inlines ??= []).Add(inline);
    }

    /// <summary>
    /// Records attributes written on a line before the block.
    /// </summary>
    public void AddLineBefore(HtmlAttributes attributes, string line)
    {
        AddSource(attributes);
        (LinesBefore ??= []).Add(line);
    }

    /// <summary>
    /// Moves the trivia of the attributes of an object to the object that its attributes are moved to.
    /// </summary>
    public static void Move(MarkdownObject from, MarkdownObject to)
    {
        if (Get(from) is not { } trivia)
        {
            return;
        }

        from.RemoveData(Key);
        to.SetData(Key, trivia);
        if (trivia.Inlines is { } inlines)
        {
            foreach (var inline in inlines)
            {
                inline.Target = to;
            }
        }
    }

    /// <summary>
    /// Records the attributes of the blocks and the inlines when the parsing ends, to know later whether they were changed.
    /// </summary>
    public static void RecordParsedAttributes(MarkdownDocument document)
    {
        document.SetData(DocumentKey, DocumentKey);
        foreach (var obj in document.Descendants())
        {
            var attributes = obj.TryGetAttributes();
            if ((attributes is not null && !IsEmpty(attributes)) || Get(obj) is not null)
            {
                GetOrCreate(obj).Parsed = Copy(attributes);
            }
        }
    }

    /// <summary>
    /// Gets whether the attributes of the document were recorded when the parsing ended.
    /// </summary>
    public static bool IsTracked(MarkdownDocument document) => document.GetData(DocumentKey) is not null;

    /// <summary>
    /// Gets whether the attributes of an object of a tracked document were changed after the parsing.
    /// </summary>
    public static bool IsChanged(MarkdownObject obj) => !AreEqual(Get(obj)?.Parsed, obj.TryGetAttributes());

    /// <summary>
    /// Gets whether an attribute of an object was added by the parsing without being in the text, such as an identifier generated
    /// from the content of a heading, or the class of the language of a fenced code block.
    /// </summary>
    public static bool IsAddedByParsing(MarkdownObject obj, string? id, string? cssClass, KeyValuePair<string, string?>? property)
    {
        var trivia = Get(obj);
        if (trivia?.Parsed is not { } parsed)
        {
            return false;
        }

        if (id is not null)
        {
            return id == parsed.Id && id != trivia.Source.Id;
        }

        if (cssClass is not null)
        {
            return IsAddedByParsing(parsed.Classes, trivia.Source.Classes, cssClass);
        }

        return property is { } value && IsAddedByParsing(parsed.Properties, trivia.Source.Properties, value);
    }

    /// <summary>
    /// Formats the attributes of an object as <c>{...}</c>, or returns <see langword="null"/> when there is nothing to write.
    /// The attributes that the parsing added without them being in the text (such as an identifier generated from the content)
    /// are not written while they are unchanged, so that the parsing adds them again.
    /// </summary>
    /// <exception cref="InvalidOperationException">An attribute cannot be written as Markdown.</exception>
    public static string? Format(MarkdownObject obj)
    {
        if (obj.TryGetAttributes() is not { } attributes)
        {
            return null;
        }

        var sb = new StringBuilder();
        if (attributes.Id is { } id && !IsAddedByParsing(obj, id, cssClass: null, property: null))
        {
            sb.Append(" #").Append(GetIdentifier(obj, id, "identifier"));
        }

        if (attributes.Classes is { } classes)
        {
            foreach (var cssClass in classes)
            {
                if (!IsAddedByParsing(obj, id: null, cssClass, property: null))
                {
                    sb.Append(" .").Append(GetIdentifier(obj, cssClass, "class"));
                }
            }
        }

        if (attributes.Properties is { } properties)
        {
            foreach (var property in properties)
            {
                if (IsAddedByParsing(obj, id: null, cssClass: null, property))
                {
                    continue;
                }

                if (!IsAttributeName(property.Key))
                {
                    ThrowCannotWrite(obj, $"the attribute name '{property.Key}' cannot be written as Markdown");
                }

                sb.Append(' ').Append(property.Key);
                if (property.Value is { } value)
                {
                    sb.Append('=');
                    AppendValue(obj, sb, property.Key, value);
                }
            }
        }

        if (sb.Length == 0)
        {
            return null;
        }

        // The first separator is not needed after the opening brace
        sb[0] = '{';
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> that tells why the attributes of an object cannot be written.
    /// </summary>
    [DoesNotReturn]
    public static void ThrowCannotWrite(MarkdownObject obj, string reason)
    {
        throw new InvalidOperationException($"The attributes of the {obj.GetType().Name} cannot be written: {reason}.");
    }

    /// <summary>
    /// Throws when the parser would not read the attributes written after a text:
    /// <list type="bullet">
    /// <item><description>a <c>{</c> of the text starts other attributes that read them, as a value for example;</description></item>
    /// <item><description>the parser of an ATX heading or of the opening line of a fenced block reads attributes from the first
    /// <c>{</c> of its line only, and cuts the line there, so other attributes must not start at this <c>{</c> at all;</description></item>
    /// <item><description>the text of a paragraph or a setext heading could be parsed as another block once it has more text: its
    /// last line could start a list item (<c>*</c>), a definition (<c>:</c>) or a table row, and its first line could start a link
    /// reference definition;</description></item>
    /// <item><description>when the parser reads them as inline attributes, a code span, raw HTML, an autolink or math of the text
    /// could end in them.</description></item>
    /// </list>
    /// </summary>
    /// <param name="obj">The object whose attributes are written.</param>
    /// <param name="text">The text before the attributes, with the separator, without the indent of the containers.</param>
    /// <param name="attributes">The attributes, as returned by <see cref="Format"/>.</param>
    /// <param name="readsFirstBrace">Whether the parser reads the attributes from the first <c>{</c> of the line.</param>
    /// <param name="checkBlocks">Whether the text is the one of a paragraph or of a setext heading.</param>
    /// <exception cref="InvalidOperationException">The attributes cannot be written after the text.</exception>
    public static void CheckAttributesAfterText(MarkdownObject obj, string text, string attributes, bool readsFirstBrace, bool checkBlocks)
    {
        if (checkBlocks && CanStartBlock(text))
        {
            ThrowCannotWrite(obj, "the text before them would start another block");
        }

        // The parser of a table reads the lines that have a '|' as rows, whose cells and delimiter row they would change
        if (checkBlocks && text.AsSpan(text.AsSpan().LastIndexOfAny('\r', '\n') + 1).Contains('|'))
        {
            ThrowCannotWrite(obj, "the last line of the text before them has a '|', which the parser of a table reads");
        }

        // The inline parsers of these constructs look for their end from their start, before the parser of the attributes
        if ((!readsFirstBrace || text.Contains('{', StringComparison.Ordinal)) &&
            ((text.Contains('`', StringComparison.Ordinal) && attributes.Contains('`', StringComparison.Ordinal)) ||
             (text.Contains('<', StringComparison.Ordinal) && attributes.Contains('>', StringComparison.Ordinal)) ||
             (text.Contains('$', StringComparison.Ordinal) && attributes.Contains('$', StringComparison.Ordinal))))
        {
            ThrowCannotWrite(obj, "the text before them has the start of a code span, raw HTML, an autolink or math that could end in them");
        }

        var index = text.IndexOf('{', StringComparison.Ordinal);
        if (index < 0)
        {
            return;
        }

        // The scan does not skip the code spans, so it can report attributes that the inline parser does not read
        var line = text + attributes;
        var isFirst = readsFirstBrace;
        for (; index >= 0; index = text.IndexOf('{', index + 1, StringComparison.Ordinal))
        {
            var slice = new StringSlice(line, index, line.Length - 1);
            if (GenericAttributesParser.TryParse(ref slice, out _) && (isFirst || slice.Start > text.Length))
            {
                ThrowCannotWrite(obj, $"the text before them has a '{{' that starts other attributes at position {index}");
            }

            isFirst = false;
        }
    }

    /// <summary>
    /// Throws when attributes of the text that replace other ones change a construct that starts before them: an autolink or raw
    /// HTML ends at a '>' and cannot have a whitespace or a '&lt;', a code span ends at backticks, and math at a '$'.
    /// </summary>
    public static void CheckReplacedAttributes(MarkdownObject obj, string text, ReadOnlySpan<char> oldAttributes, string newAttributes)
    {
        var changes = (text.Contains('<', StringComparison.Ordinal) && (Count(oldAttributes, '<') != Count(newAttributes, '<') || Count(oldAttributes, '>') != Count(newAttributes, '>') || oldAttributes.ContainsAny(" \t\r\n") != newAttributes.AsSpan().ContainsAny(" \t\r\n"))) ||
            (text.Contains('`', StringComparison.Ordinal) && Count(oldAttributes, '`') != Count(newAttributes, '`')) ||
            (text.Contains('$', StringComparison.Ordinal) && Count(oldAttributes, '$') != Count(newAttributes, '$'));
        if (changes)
        {
            ThrowCannotWrite(obj, "the text before them has the start of a code span, raw HTML, an autolink or math that they could change");
        }

        // A paragraph that starts like a link reference definition, and is not one: the attributes can be what makes it invalid
        var firstLine = text.AsSpan(0, text.AsSpan().IndexOfAny('\r', '\n') is var index and >= 0 ? index : text.Length).TrimStart(" \t");
        if ((firstLine.StartsWith('[') || firstLine.StartsWith("*[", StringComparison.Ordinal)) && text.Contains("]:", StringComparison.Ordinal))
        {
            ThrowCannotWrite(obj, "the text before them starts like a link reference definition");
        }

        static int Count(ReadOnlySpan<char> value, char c) => value.Count(c);
    }

    // Whether the text, with more text after it, could be parsed as another block: its first line could start a link reference
    // definition, a footnote or an abbreviation, whose label can span lines, and its last line could start a block that interrupts
    // a paragraph.
    private static bool CanStartBlock(string text)
    {
        var firstLine = text.AsSpan(0, text.AsSpan().IndexOfAny('\r', '\n') is var index and >= 0 ? index : text.Length).TrimStart(" \t");
        if ((firstLine.StartsWith('[') || firstLine.StartsWith("*[", StringComparison.Ordinal)) && text.Contains("]:", StringComparison.Ordinal))
        {
            return true;
        }

        // The marker of an alert is alone on the first line of its quote
        if (firstLine.StartsWith("[!", StringComparison.Ordinal))
        {
            return true;
        }

        var line = text.AsSpan(text.AsSpan().LastIndexOfAny('\r', '\n') + 1).TrimStart(" \t").TrimEnd();
        if (line.IsEmpty)
        {
            return false;
        }

        // A table row, a definition or a footer
        if (line[0] is '|' or '+' or ':' or '~' or '^')
        {
            return true;
        }

        // A list item: an empty one cannot interrupt a paragraph, but one with text can. The ordered lists of the extensions
        // number their items with letters and roman numerals too.
        var markerLength = line[0] is '*' or '-' or '+' ? 1 : 0;
        if (markerLength == 0)
        {
            while (markerLength < line.Length && markerLength < 9 && char.IsAsciiLetterOrDigit(line[markerLength]))
            {
                markerLength++;
            }

            if (markerLength == 0 || markerLength == line.Length || line[markerLength] is not ('.' or ')'))
            {
                return false;
            }

            markerLength++;
        }

        return markerLength == line.Length || line[markerLength].IsSpaceOrTab();
    }

    /// <summary>
    /// Returns the attributes to write in place of attributes of the text. When the text has whitespace, so do they: without it,
    /// the text around them could change, such as a link reference definition whose destination they end.
    /// </summary>
    /// <param name="obj">The object the attributes are attached to.</param>
    /// <param name="attributes">The attributes, as returned by <see cref="Format"/>, or <see langword="null"/> for none.</param>
    /// <param name="text">The attributes of the text.</param>
    public static string ReplaceAttributes(MarkdownObject obj, string? attributes, ReadOnlySpan<char> text)
    {
        attributes ??= "{}";
        var source = text.TrimEnd("\r\n");
        if (!attributes.Contains(' ', StringComparison.Ordinal) && source.ContainsAny(" \t"))
        {
            attributes = "{ " + attributes[1..];
        }

        // The attributes of the text can span lines: the line breaks stay, so that the containers keep the same lines. A line cannot
        // be blank, which would end the paragraph, so each line between the first and the last one gets an attribute.
        var newLines = new List<string>();
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] is '\r' or '\n')
            {
                var length = source[i] is '\r' && i + 1 < source.Length && source[i + 1] is '\n' ? 2 : 1;
                newLines.Add(source.Slice(i, length).ToString());
                i += length - 1;
            }
        }

        if (newLines.Count > 0)
        {
            var tokens = SplitAttributes(attributes);
            if (tokens.Count < newLines.Count - 1)
            {
                ThrowCannotWrite(obj, "the attributes of the text span more lines than the attributes have");
            }

            var firstLineCount = tokens.Count - (newLines.Count - 1);
            var sb = new StringBuilder("{");
            sb.AppendJoin(' ', tokens.Take(firstLineCount));
            for (var i = 0; i < newLines.Count; i++)
            {
                sb.Append(newLines[i]);
                if (i < newLines.Count - 1)
                {
                    sb.Append(tokens[firstLineCount + i]);
                }
            }

            attributes = sb.Append('}').ToString();
        }

        return attributes;
    }

    /// <summary>
    /// Replaces the attributes of a text that only has attributes, on one line or more. The parser attaches the last attributes to
    /// the block, and the ones before them to the paragraph that the text is, which it removes: the last attributes are replaced
    /// with the specified ones, and the others with empty attributes, which keep the text one that only has attributes.
    /// </summary>
    /// <param name="obj">The block the attributes are attached to.</param>
    /// <param name="text">The text.</param>
    /// <param name="attributes">The attributes, as returned by <see cref="Format"/>, or <see langword="null"/> for none.</param>
    public static string ReplaceLineOfAttributes(MarkdownObject obj, ReadOnlySpan<char> text, string? attributes)
    {
        var line = text.ToString();
        var groups = new List<(int Start, int End)>();
        for (var index = line.IndexOf('{', StringComparison.Ordinal); index >= 0; index = line.IndexOf('{', index + 1, StringComparison.Ordinal))
        {
            var slice = new StringSlice(line, index, line.Length - 1);
            if (GenericAttributesParser.TryParse(ref slice, out _))
            {
                // The parser reads the line break that follows the attributes as part of them
                var end = slice.Start - 1;
                while (line[end] is '\r' or '\n')
                {
                    end--;
                }

                groups.Add((index, end));
                index = end;
            }
        }

        var sb = new StringBuilder(line.Length);
        var position = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            var (groupStart, groupEnd) = groups[i];
            sb.Append(line, position, groupStart - position);
            sb.Append(ReplaceAttributes(obj, i == groups.Count - 1 ? attributes : null, line.AsSpan(groupStart, groupEnd - groupStart + 1)));
            position = groupEnd + 1;
        }

        return sb.Append(line, position, line.Length - position).ToString();
    }

    // Splits the attributes written by Format into their identifier, classes and properties, which a quoted value can have spaces in
    private static List<string> SplitAttributes(string attributes)
    {
        var tokens = new List<string>();
        var start = 1;
        var quote = '\0';
        for (var i = 1; i < attributes.Length - 1; i++)
        {
            var c = attributes[i];
            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == ' ')
            {
                if (i > start)
                {
                    tokens.Add(attributes[start..i]);
                }

                start = i + 1;
            }
        }

        if (attributes.Length - 1 > start)
        {
            tokens.Add(attributes[start..^1]);
        }

        return tokens;
    }

    private void AddSource(HtmlAttributes attributes)
    {
        if (attributes.Id is not null)
        {
            Source.Id = attributes.Id;
        }

        if (attributes.Classes is { } classes)
        {
            (Source.Classes ??= []).AddRange(classes);
        }

        if (attributes.Properties is { } properties)
        {
            (Source.Properties ??= []).AddRange(properties);
        }
    }

    private static bool IsAddedByParsing<T>(List<T>? parsed, List<T>? source, T item)
    {
        return parsed is not null && parsed.Contains(item) && (source is null || !source.Contains(item));
    }

    private static HtmlAttributes? Copy(HtmlAttributes? attributes)
    {
        if (attributes is null || IsEmpty(attributes))
        {
            return null;
        }

        return new HtmlAttributes
        {
            Id = attributes.Id,
            Classes = attributes.Classes is { Count: > 0 } classes ? [.. classes] : null,
            Properties = attributes.Properties is { Count: > 0 } properties ? [.. properties] : null,
        };
    }

    private static bool IsEmpty(HtmlAttributes attributes)
    {
        return attributes.Id is null && attributes.Classes is null or { Count: 0 } && attributes.Properties is null or { Count: 0 };
    }

    private static bool AreEqual(HtmlAttributes? left, HtmlAttributes? right)
    {
        if (left is null || right is null)
        {
            return (left is null || IsEmpty(left)) && (right is null || IsEmpty(right));
        }

        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
            AreEqual(left.Classes, right.Classes) &&
            AreEqual(left.Properties, right.Properties);
    }

    private static bool AreEqual<T>(List<T>? left, List<T>? right)
    {
        if (left is null or { Count: 0 } || right is null or { Count: 0 })
        {
            return left is null or { Count: 0 } && right is null or { Count: 0 };
        }

        return left.SequenceEqual(right);
    }

    // The parser reads an identifier or a class up to a whitespace or the closing brace
    private static string GetIdentifier(MarkdownObject obj, string value, string kind)
    {
        if (value.Length == 0 || EndsUnquotedText(value))
        {
            ThrowCannotWrite(obj, $"the {kind} '{value}' cannot be written as Markdown");
        }

        return value;
    }

    private static bool EndsUnquotedText(string value)
    {
        foreach (var c in value)
        {
            if (c == '}' || c.IsWhiteSpaceOrZero() || c.IsWhitespace())
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAttributeName(string name)
    {
        if (name.Length == 0 || !(name[0].IsAlpha() || name[0] is '_' or ':'))
        {
            return false;
        }

        foreach (var c in name.AsSpan(1))
        {
            if (!(c.IsAlphaNumeric() || c is '_' or ':' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendValue(MarkdownObject obj, StringBuilder sb, string name, string value)
    {
        // An unquoted value ends at a whitespace or the closing brace, and a quoted value ends at its quote. The attributes are
        // on one line, so a value cannot have a line break.
        if (value.Length > 0 && value[0] is not ('"' or '\'') && !EndsUnquotedText(value))
        {
            sb.Append(value);
        }
        else if (value.AsSpan().ContainsAny('\r', '\n'))
        {
            ThrowCannotWrite(obj, $"the value of the attribute '{name}' has a line break");
        }
        else if (!value.Contains('"', StringComparison.Ordinal))
        {
            sb.Append('"').Append(value).Append('"');
        }
        else if (!value.Contains('\'', StringComparison.Ordinal))
        {
            sb.Append('\'').Append(value).Append('\'');
        }
        else
        {
            ThrowCannotWrite(obj, $"the value of the attribute '{name}' has both kinds of quotes");
        }
    }
}
