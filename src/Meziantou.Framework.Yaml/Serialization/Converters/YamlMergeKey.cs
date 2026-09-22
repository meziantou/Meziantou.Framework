#nullable enable // The file is embedded in the source generator, so we need to enable nullable reference types to avoid warnings in the generated code.
using Meziantou.Framework.Yaml.Schemas;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>Recognizes the <c>&lt;&lt;</c> merge key of the YAML merge extension.</summary>
/// <remarks>The file is also embedded in the code generated for a <see cref="YamlSerializerContext"/>.</remarks>
[Microsoft.CodeAnalysis.Embedded]
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
        return IsEnabled(reader.Options) && IsMergeKeyScalar(reader);
    }

    /// <summary>Determines whether the scalar the reader is positioned on is spelled as a merge key, whatever the schema in use.</summary>
    /// <param name="reader">The reader positioned on a mapping key.</param>
    /// <remarks>
    /// Generated code resolves whether the schema enables the merge key separately, so it only needs this part of
    /// <see cref="IsMergeKey"/>. The same rules about the style and the tag apply.
    /// </remarks>
    public static bool IsMergeKeyScalar(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.Scalar || !string.Equals(reader.ScalarValue, Key, global::System.StringComparison.Ordinal))
        {
            return false;
        }

        var tag = reader.Tag;
        if (tag is not null)
        {
            return string.Equals(tag, ExtendedSchema.MergeShortTag, global::System.StringComparison.Ordinal) ||
                   string.Equals(tag, ExtendedSchema.MergeLongTag, global::System.StringComparison.Ordinal);
        }

        return reader.ScalarStyle is ScalarStyle.Any or ScalarStyle.Plain;
    }

    /// <summary>Positions the reader on a copy of the mapping node a merge alias refers to.</summary>
    /// <param name="reader">The reader positioned on a merge value, or on an entry of a merge sequence.</param>
    /// <param name="targetType">The type the merge applies to, which is reported when aliases are not supported.</param>
    /// <remarks>
    /// A merge copies the entries of the anchored mapping node, as they appear in the document, so an alias is read
    /// like an inline mapping instead of taking the value the node was deserialized into. The reader does not move when
    /// it is not positioned on an alias.
    /// </remarks>
    public static void ReplayAlias(YamlReader reader, global::System.Type targetType)
    {
        if (reader.TokenType == YamlTokenType.Alias && !reader.TryReplayMappingAlias())
        {
            throw YamlThrowHelper.ThrowAliasNotSupported(reader, targetType);
        }
    }
}
