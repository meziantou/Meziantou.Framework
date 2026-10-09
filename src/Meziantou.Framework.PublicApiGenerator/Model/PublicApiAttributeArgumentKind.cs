namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Identifies the kind of value stored in a <see cref="PublicApiAttributeArgument"/>.</summary>
public enum PublicApiAttributeArgumentKind
{
    /// <summary>A primitive value, a string, or <see langword="null"/>.</summary>
    Constant,

    /// <summary>An enum value. <see cref="PublicApiAttributeArgument.Value"/> contains the underlying integral value.</summary>
    Enum,

    /// <summary>A <c>typeof</c> expression. <see cref="PublicApiAttributeArgument.Value"/> contains a <see cref="PublicApiTypeReference"/>, or <see langword="null"/>.</summary>
    Type,

    /// <summary>An array. <see cref="PublicApiAttributeArgument.Value"/> contains an <c>ImmutableArray&lt;PublicApiAttributeArgument&gt;</c>, or <see langword="null"/>.</summary>
    Array,
}
