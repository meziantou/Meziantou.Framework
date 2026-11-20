using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>The generic attributes of an element: an id, classes, and other attributes, as <c>{#id .class name=value}</c> writes them.</summary>
/// <param name="Id">The id, or <see langword="null"/> when there is none.</param>
/// <param name="Classes">The classes.</param>
/// <param name="Properties">The other attributes, whose value is <see langword="null"/> when they are written without one, such as <c>{hidden}</c>.</param>
/// <remarks>
/// Two instances are equal when they have the same id, and the same classes and properties in the same order. A default
/// <see cref="ImmutableArray{T}"/> is read as an empty one.
/// </remarks>
public sealed record MarkdownGenericAttributes(string? Id, ImmutableArray<string> Classes, ImmutableArray<KeyValuePair<string, string?>> Properties)
{
    /// <summary>Gets the classes.</summary>
    public ImmutableArray<string> Classes { get => field.IsDefault ? [] : field; init; } = Classes;

    /// <summary>Gets the other attributes, whose value is <see langword="null"/> when they are written without one, such as <c>{hidden}</c>.</summary>
    public ImmutableArray<KeyValuePair<string, string?>> Properties { get => field.IsDefault ? [] : field; init; } = Properties;

    /// <summary>Gets attributes without id, classes or properties.</summary>
    public static MarkdownGenericAttributes Empty { get; } = new(Id: null, [], []);

    /// <summary>Gets a value indicating whether the attributes have no id, no class and no property.</summary>
    public bool IsEmpty => Id is null && Classes.Length == 0 && Properties.Length == 0;

    public bool Equals([NotNullWhen(true)] MarkdownGenericAttributes? other)
        => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal) && Classes.AsSpan().SequenceEqual(other.Classes.AsSpan(), StringComparer.Ordinal) && Properties.AsSpan().SequenceEqual(other.Properties.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        foreach (var cssClass in Classes)
        {
            hash.Add(cssClass, StringComparer.Ordinal);
        }

        foreach (var property in Properties)
        {
            hash.Add(property);
        }

        return hash.ToHashCode();
    }

    // The attributes as Markdown: {#id .class name=value name="a value" name}
    internal string ToMarkdown(string paramName)
    {
        var builder = new StringBuilder("{");
        if (Id is not null)
        {
            AppendSeparator(builder);
            builder.Append('#').Append(ValidateIdentifier(Id, "id", paramName));
        }

        foreach (var cssClass in Classes)
        {
            AppendSeparator(builder);
            builder.Append('.').Append(ValidateIdentifier(cssClass, "class", paramName));
        }

        foreach (var (name, value) in Properties)
        {
            if (string.IsNullOrEmpty(name) || !AttributesScanner.IsStartAttributeName(name[0]) || !name.Skip(1).All(AttributesScanner.IsAttributeNameChar))
                throw new ArgumentException($"'{name}' cannot be the name of an attribute: it starts with a letter, '_' or ':', followed by letters, digits, '_', ':', '.' or '-'.", paramName);

            AppendSeparator(builder);
            builder.Append(name);
            if (value is null)
            {
                continue;
            }

            if (value.AsSpan().ContainsAny('\r', '\n'))
                throw new ArgumentException($"The value of the attribute '{name}' cannot contain a line ending.", paramName);

            builder.Append('=');
            // A value without quotes ends at whitespace or at the }
            if (value.Length > 0 && !value.AsSpan().ContainsAny("\"'}") && !value.Any(AttributesScanner.IsWhitespace))
            {
                builder.Append(value);
            }
            else if (!value.Contains('"', StringComparison.Ordinal))
            {
                builder.Append('"').Append(value).Append('"');
            }
            else if (!value.Contains('\'', StringComparison.Ordinal))
            {
                builder.Append('\'').Append(value).Append('\'');
            }
            else
            {
                throw new ArgumentException($"The value of the attribute '{name}' cannot contain both a single and a double quote.", paramName);
            }
        }

        return builder.Append('}').ToString();
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 1)
        {
            builder.Append(' ');
        }
    }

    // An id or a class ends at whitespace or at the }
    private static string ValidateIdentifier(string value, string kind, string paramName)
    {
        if (value.Length == 0 || value.Contains('}', StringComparison.Ordinal) || value.Contains('\0', StringComparison.Ordinal) || value.Any(AttributesScanner.IsWhitespace))
            throw new ArgumentException($"'{value}' cannot be the {kind} of generic attributes: it is empty, or it contains whitespace or '}}'.", paramName);

        return value;
    }
}
