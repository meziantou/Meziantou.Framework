using Meziantou.Framework.Yaml.Schemas;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>Recognizes the <c>&lt;&lt;</c> merge key of the YAML merge extension.</summary>
internal static class YamlMergeKey
{
    /// <summary>The merge key text.</summary>
    public const string Key = "<<";

    /// <summary>Determines whether the schema in use resolves the merge key.</summary>
    public static bool IsEnabled(YamlSerializerOptions options)
        => options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;

    /// <summary>Determines whether the scalar the reader is positioned on is a merge key.</summary>
    /// <param name="reader">The reader positioned on a mapping key.</param>
    /// <remarks>
    /// The merge key is resolved from a plain <c>&lt;&lt;</c> scalar or from an explicit <c>!!merge</c> tag. A quoted or
    /// block scalar, and any other tag such as <c>!!str</c>, denote an ordinary key whose name happens to be
    /// <c>&lt;&lt;</c>. This must be checked before the reader advances past the key, because the style and the tag
    /// belong to the key token.
    /// </remarks>
    public static bool IsMergeKey(YamlReader reader)
    {
        if (!IsEnabled(reader.Options))
        {
            return false;
        }

        if (reader.TokenType != YamlTokenType.Scalar || !string.Equals(reader.ScalarValue, Key, StringComparison.Ordinal))
        {
            return false;
        }

        var tag = reader.Tag;
        if (tag is not null)
        {
            return string.Equals(tag, ExtendedSchema.MergeShortTag, StringComparison.Ordinal) ||
                   string.Equals(tag, ExtendedSchema.MergeLongTag, StringComparison.Ordinal);
        }

        return reader.ScalarStyle is ScalarStyle.Any or ScalarStyle.Plain;
    }
}
