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
    public void AddInlineAttributes(HtmlAttributes attributes) => AddSource(attributes);

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
