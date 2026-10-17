using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A custom attribute applied to an assembly, a symbol, a parameter, a return value or a generic parameter.</summary>
/// <remarks>
/// All the custom attributes found in metadata are exposed, including the ones that the C# formatter does not display
/// (e.g. <c>NullableAttribute</c>, <c>EditorBrowsableAttribute</c> or <c>CompilerGeneratedAttribute</c>).
/// Pseudo-attributes, such as <c>SerializableAttribute</c> or <c>InAttribute</c>, are stored as metadata flags and are not exposed.
/// </remarks>
public sealed class PublicApiAttribute
{
    internal PublicApiAttribute(
        PublicApiNamedTypeReference attributeType,
        ImmutableArray<PublicApiAttributeArgument> constructorArguments,
        ImmutableArray<PublicApiAttributeNamedArgument> namedArguments,
        bool areArgumentsDecoded)
    {
        AttributeType = attributeType;
        ConstructorArguments = constructorArguments;
        NamedArguments = namedArguments;
        AreArgumentsDecoded = areArgumentsDecoded;
    }

    public PublicApiNamedTypeReference AttributeType { get; }

    public ImmutableArray<PublicApiAttributeArgument> ConstructorArguments { get; }

    public ImmutableArray<PublicApiAttributeNamedArgument> NamedArguments { get; }

    /// <summary>
    /// Gets a value indicating whether the arguments could be decoded. Decoding requires the underlying type of the enums used as argument,
    /// which is unknown for enums declared in other assemblies; such enums are assumed to be <see cref="int"/>.
    /// When decoding fails, <see cref="ConstructorArguments"/> and <see cref="NamedArguments"/> are empty.
    /// </summary>
    public bool AreArgumentsDecoded { get; }

    public override string ToString() => AttributeType.FullName;
}
