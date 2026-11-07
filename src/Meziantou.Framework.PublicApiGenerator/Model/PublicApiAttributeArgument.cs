using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A value passed to a custom attribute.</summary>
public sealed class PublicApiAttributeArgument
{
    internal PublicApiAttributeArgument(PublicApiTypeReference type, PublicApiAttributeArgumentKind kind, object? value, ImmutableArray<string> enumMemberNames = default)
    {
        Type = type;
        Kind = kind;
        Value = value;
        EnumMemberNames = enumMemberNames.IsDefault ? [] : enumMemberNames;
    }

    /// <summary>Gets the type of the value. For a value passed to a parameter of type <see cref="object"/>, it is the type of the boxed value.</summary>
    public PublicApiTypeReference Type { get; }

    public PublicApiAttributeArgumentKind Kind { get; }

    /// <summary>
    /// Gets the value: a primitive value or a <see cref="string"/> for <see cref="PublicApiAttributeArgumentKind.Constant"/>,
    /// the underlying integral value for <see cref="PublicApiAttributeArgumentKind.Enum"/>,
    /// a <see cref="PublicApiTypeReference"/> for <see cref="PublicApiAttributeArgumentKind.Type"/>,
    /// or an <see cref="ImmutableArray{T}"/> of <see cref="PublicApiAttributeArgument"/> for <see cref="PublicApiAttributeArgumentKind.Array"/>.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the names of the enum members that make up the value of an <see cref="PublicApiAttributeArgumentKind.Enum"/> argument,
    /// or a single name when a member has the exact value. It is empty when the enum definition is not available,
    /// or when the value does not match its members.
    /// </summary>
    public ImmutableArray<string> EnumMemberNames { get; }

    public override string ToString() => Value?.ToString() ?? "null";
}
