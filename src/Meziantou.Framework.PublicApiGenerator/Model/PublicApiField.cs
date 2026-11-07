using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A field, a constant, or an enum member.</summary>
public sealed class PublicApiField : PublicApiMember
{
    internal PublicApiField(
        string name,
        PublicApiAccessibility accessibility,
        ImmutableArray<PublicApiAttribute> attributes,
        PublicApiMetadataOrigin? origin,
        PublicApiMemberModifiers modifiers,
        PublicApiTypeReference type,
        PublicApiRefKind refKind,
        bool isReadOnly,
        bool isConst,
        bool hasConstantValue,
        object? constantValue,
        ImmutableArray<string> constantValueEnumMemberNames,
        bool isVolatile,
        bool isRequired)
        : base(name, accessibility, attributes, origin, modifiers, explicitInterfaceImplementations: [])
    {
        Type = type;
        RefKind = refKind;
        IsReadOnly = isReadOnly;
        IsConst = isConst;
        HasConstantValue = hasConstantValue;
        ConstantValue = constantValue;
        ConstantValueEnumMemberNames = constantValueEnumMemberNames;
        IsVolatile = isVolatile;
        IsRequired = isRequired;
    }

    public override PublicApiSymbolKind Kind => PublicApiSymbolKind.Field;

    public PublicApiTypeReference Type { get; }

    /// <summary>Gets <see cref="PublicApiRefKind.Ref"/> or <see cref="PublicApiRefKind.RefReadOnly"/> for a ref field of a ref struct.</summary>
    public PublicApiRefKind RefKind { get; }

    /// <summary>Gets a value indicating whether the field is <c>readonly</c>. For a ref field, it means the reference cannot be reassigned.</summary>
    public bool IsReadOnly { get; }

    public bool IsConst { get; }

    public bool HasConstantValue { get; }

    /// <summary>
    /// Gets the value of a constant, as stored in metadata. Enum members and constants of an enum type store the underlying integral value.
    /// A <see cref="decimal"/> constant is read from the <c>DecimalConstantAttribute</c> of the field.
    /// </summary>
    public object? ConstantValue { get; }

    // The names of the enum members that make the value, when the constant is of an enum type and the value can be named
    internal ImmutableArray<string> ConstantValueEnumMemberNames { get; }

    public bool IsVolatile { get; }

    public bool IsRequired { get; }
}
