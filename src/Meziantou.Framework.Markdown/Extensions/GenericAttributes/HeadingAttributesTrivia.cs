using System.Text;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// With trivia, what the roundtrip renderer needs to write back the attributes of a heading that were changed after the
/// parsing: the attributes when the parsing ended, the attributes written in the text, and where this text is.
/// </summary>
internal sealed class HeadingAttributesTrivia
{
    private static readonly object Key = typeof(HeadingAttributesTrivia);

    private HtmlAttributes? _parsed;
    private bool _isParsedSet;

    /// <summary>
    /// Gets the attributes written in the text and attached to the heading, merged.
    /// </summary>
    private HtmlAttributes Source { get; } = new();

    /// <summary>
    /// Gets the text of the attributes that end the line of an ATX heading, in its trivia after, or an empty slice.
    /// </summary>
    public StringSlice TriviaAfterAttributes { get; private set; } = StringSlice.Empty;

    /// <summary>
    /// Gets the texts of the lines before the heading that only have attributes, from the paragraphs that were removed.
    /// </summary>
    public List<string>? LinesBefore { get; private set; }

    public static HeadingAttributesTrivia? Get(HeadingBlock heading) => heading.GetData(Key) as HeadingAttributesTrivia;

    public static HeadingAttributesTrivia GetOrCreate(HeadingBlock heading)
    {
        if (heading.GetData(Key) is not HeadingAttributesTrivia trivia)
        {
            trivia = new HeadingAttributesTrivia();
            heading.SetData(Key, trivia);
        }

        return trivia;
    }

    /// <summary>
    /// Records the attributes that end the line of an ATX heading.
    /// </summary>
    public void AddTriviaAfterAttributes(HtmlAttributes attributes, StringSlice text)
    {
        AddSource(attributes);
        TriviaAfterAttributes = text;
    }

    /// <summary>
    /// Records attributes written in the content of the heading, kept in a <see cref="GenericAttributesInline"/>.
    /// </summary>
    public void AddInlineAttributes(HtmlAttributes attributes)
    {
        AddSource(attributes);
        HasInlineAttributes = true;
    }

    /// <summary>
    /// Gets whether attributes written in the content of the heading are attached to it.
    /// </summary>
    public bool HasInlineAttributes { get; private set; }

    /// <summary>
    /// Records attributes written on a line before the heading.
    /// </summary>
    public void AddLineBefore(HtmlAttributes attributes, string line)
    {
        AddSource(attributes);
        (LinesBefore ??= []).Add(line);
    }

    /// <summary>
    /// Records the attributes of each heading when the parsing ends, to know later whether they were changed.
    /// </summary>
    public static void RecordParsedAttributes(MarkdownDocument document)
    {
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var trivia = GetOrCreate(heading);
            trivia._parsed = Copy(heading.TryGetAttributes());
            trivia._isParsedSet = true;
        }
    }

    /// <summary>
    /// Returns the trivia of a heading whose attributes were changed after the parsing, or <see langword="null"/> when they
    /// were not changed or are not tracked.
    /// </summary>
    public static HeadingAttributesTrivia? GetChanged(HeadingBlock heading)
    {
        if (Get(heading) is not { _isParsedSet: true } trivia || AreEqual(trivia._parsed, heading.TryGetAttributes()))
        {
            return null;
        }

        return trivia;
    }

    /// <summary>
    /// Formats the attributes of the heading as <c>{...}</c>, or returns <see langword="null"/> when there is nothing to write.
    /// The attributes that the parsing added without them being in the text (such as an identifier generated from the content)
    /// are not written while they are unchanged, so that the parsing adds them again.
    /// </summary>
    /// <exception cref="InvalidOperationException">An attribute cannot be written as Markdown.</exception>
    public string? Format(HeadingBlock heading)
    {
        if (heading.TryGetAttributes() is not { } attributes)
        {
            return null;
        }

        var sb = new StringBuilder();
        if (attributes.Id is { } id && !(id == _parsed?.Id && id != Source.Id))
        {
            sb.Append(" #").Append(GetIdentifier(id, nameof(HtmlAttributes.Id)));
        }

        if (attributes.Classes is { } classes)
        {
            foreach (var cssClass in classes)
            {
                if (!IsAddedByParsing(_parsed?.Classes, Source.Classes, cssClass))
                {
                    sb.Append(" .").Append(GetIdentifier(cssClass, "class"));
                }
            }
        }

        if (attributes.Properties is { } properties)
        {
            foreach (var property in properties)
            {
                if (IsAddedByParsing(_parsed?.Properties, Source.Properties, property))
                {
                    continue;
                }

                if (!IsAttributeName(property.Key))
                {
                    throw new InvalidOperationException($"The attribute name '{property.Key}' cannot be written as Markdown generic attributes.");
                }

                sb.Append(' ').Append(property.Key);
                if (property.Value is { } value)
                {
                    sb.Append('=');
                    AppendValue(sb, property.Key, value);
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
    /// Throws when the parser would not read the attributes written after the text of a heading:
    /// <list type="bullet">
    /// <item><description>a <c>{</c> of the text starts other attributes that read them, as a value for example;</description></item>
    /// <item><description>the parser of an ATX heading reads attributes from the first <c>{</c> of its line only, and cuts the
    /// heading there, so the other attributes must not start at this <c>{</c> at all;</description></item>
    /// <item><description>the last line of a setext heading could start another block once it has more text, such as a list
    /// item (<c>*</c>), a definition (<c>:</c>), a table row or a link reference definition;</description></item>
    /// <item><description>when the parser reads them as inline attributes (in a setext heading, or after a <c>{</c> of an ATX
    /// heading), a code span, raw HTML, an autolink or math of the text could end in them.</description></item>
    /// </list>
    /// </summary>
    /// <param name="text">The text before the attributes, with the separator. Its lines start with the indent of the containers.</param>
    /// <param name="attributes">The attributes, as returned by <see cref="Format"/>.</param>
    /// <param name="isSetext">Whether the heading is a setext heading.</param>
    /// <exception cref="InvalidOperationException">The attributes cannot be written after the text.</exception>
    public static void CheckAttributesAfterText(string text, string attributes, bool isSetext)
    {
        if (isSetext && CanStartBlock(text))
        {
            throw new InvalidOperationException("The attributes of the heading cannot be written after its text, as its last line would start another block.");
        }

        // The inline parsers of these constructs look for their end from their start, before the parser of the attributes
        if ((isSetext || text.Contains('{', StringComparison.Ordinal)) &&
            ((text.Contains('`', StringComparison.Ordinal) && attributes.Contains('`', StringComparison.Ordinal)) ||
             (text.Contains('<', StringComparison.Ordinal) && attributes.Contains('>', StringComparison.Ordinal)) ||
             (text.Contains('$', StringComparison.Ordinal) && attributes.Contains('$', StringComparison.Ordinal))))
        {
            throw new InvalidOperationException("The attributes of the heading cannot be written after its text, which has the start of a code span, raw HTML, an autolink or math that could end in them.");
        }

        var index = text.IndexOf('{', StringComparison.Ordinal);
        if (index < 0)
        {
            return;
        }

        // The scan does not skip the code spans, so it can report attributes that the inline parser does not read
        var line = text + attributes;
        var isFirst = !isSetext;
        for (; index >= 0; index = text.IndexOf('{', index + 1, StringComparison.Ordinal))
        {
            var slice = new StringSlice(line, index, line.Length - 1);
            if (GenericAttributesParser.TryParse(ref slice, out _) && (isFirst || slice.Start > text.Length))
            {
                throw new InvalidOperationException($"The attributes of the heading cannot be written after its text, which has a '{{' that starts other attributes at position {index}.");
            }

            isFirst = false;
        }
    }

    // Whether the text, with more text after it, could be parsed as another block: its first line could start a link reference
    // definition, a footnote or an abbreviation, whose label can span lines, and its last line could start a block that interrupts
    // a paragraph. The indent of the containers ('>' and whitespace) is skipped.
    private static bool CanStartBlock(string text)
    {
        var firstLine = text.AsSpan(0, text.AsSpan().IndexOfAny('\r', '\n') is var index and >= 0 ? index : text.Length).TrimStart(" \t>");
        if ((firstLine.StartsWith('[') || firstLine.StartsWith("*[", StringComparison.Ordinal)) && text.Contains("]:", StringComparison.Ordinal))
        {
            return true;
        }

        var line = text.AsSpan(text.AsSpan().LastIndexOfAny('\r', '\n') + 1).TrimStart(" \t>").TrimEnd();
        if (line.IsEmpty)
        {
            return false;
        }

        // A table row, a definition or a footer
        if (line[0] is '|' or '+' or ':' or '^')
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
    /// <param name="attributes">The attributes, as returned by <see cref="Format"/>, or <see langword="null"/> for none.</param>
    /// <param name="text">The attributes of the text.</param>
    public static string ReplaceAttributes(string? attributes, ReadOnlySpan<char> text)
    {
        attributes ??= "{}";
        if (!attributes.Contains(' ', StringComparison.Ordinal) && text.TrimEnd("\r\n").ContainsAny(" \t"))
        {
            return "{ " + attributes[1..];
        }

        return attributes;
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
    private static string GetIdentifier(string value, string kind)
    {
        if (value.Length == 0 || EndsUnquotedText(value))
        {
            throw new InvalidOperationException($"The {kind} '{value}' cannot be written as Markdown generic attributes.");
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

    private static void AppendValue(StringBuilder sb, string name, string value)
    {
        // An unquoted value ends at a whitespace or the closing brace, and a quoted value ends at its quote. The attributes are
        // on the line of the heading, so a value cannot have a line break.
        if (value.Length > 0 && value[0] is not ('"' or '\'') && !EndsUnquotedText(value))
        {
            sb.Append(value);
        }
        else if (value.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new InvalidOperationException($"The value of the attribute '{name}' cannot be written as Markdown generic attributes, as it contains a line break.");
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
            throw new InvalidOperationException($"The value of the attribute '{name}' cannot be written as Markdown generic attributes, as it contains both kinds of quotes.");
        }
    }
}
