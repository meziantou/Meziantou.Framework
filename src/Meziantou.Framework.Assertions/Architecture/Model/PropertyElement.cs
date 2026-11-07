using System.Reflection.Metadata;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A property or an indexer.</summary>
public sealed class PropertyElement : MemberElement
{
    private readonly Lazy<TypeReference> _type;
    private readonly Lazy<IReadOnlyList<AttributeElement>> _customAttributes;

    internal PropertyElement(TypeElement declaringType, PropertyDefinitionHandle handle, MethodElement? getter, MethodElement? setter)
        : base(declaringType, declaringType.Image.Reader.GetString(declaringType.Image.Reader.GetPropertyDefinition(handle).Name))
    {
        Handle = handle;
        Getter = getter;
        Setter = setter;
        var reader = declaringType.Image.Reader;
        var definition = reader.GetPropertyDefinition(handle);
        IsRequired = MetadataNames.HasAttribute(reader, definition.GetCustomAttributes(), "System.Runtime.CompilerServices.RequiredMemberAttribute");
        IsInitOnly = setter is not null && HasInitOnlyModifier(reader, reader.GetMethodDefinition(setter.Handle));
        _type = new(() => DeclaringType.Image.Reader.GetPropertyDefinition(Handle).DecodeSignature(DeclaringType.Provider, DeclaringType.GenericContext).ReturnType);
        _customAttributes = new(() => AttributeElement.Create(DeclaringType.Provider, DeclaringType.Image.Reader, DeclaringType.Image.Reader.GetPropertyDefinition(Handle).GetCustomAttributes()));
    }

    internal PropertyDefinitionHandle Handle { get; }

    /// <summary>Gets the type of the property.</summary>
    public TypeReference Type => _type.Value;

    /// <summary>Gets the <c>get</c> accessor, or <see langword="null"/>.</summary>
    public MethodElement? Getter { get; }

    /// <summary>Gets the <c>set</c> or <c>init</c> accessor, or <see langword="null"/>.</summary>
    public MethodElement? Setter { get; }

    /// <summary>Gets a value indicating whether the setter is an <c>init</c> accessor.</summary>
    public bool IsInitOnly { get; }

    /// <summary>Gets a value indicating whether the property is <c>required</c>.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets the most permissive accessibility of the accessors.</summary>
    public override ElementAccessibility Accessibility => (ElementAccessibility)Math.Max((int)(Getter?.Accessibility ?? ElementAccessibility.Private), (int)(Setter?.Accessibility ?? ElementAccessibility.Private));

    /// <inheritdoc />
    public override bool IsStatic => (Getter ?? Setter)?.IsStatic ?? false;

    /// <inheritdoc />
    public override IReadOnlyList<AttributeElement> Attributes => _customAttributes.Value;

    /// <inheritdoc />
    public override SourceLocation? Location => Getter?.Location ?? Setter?.Location;

    // An init accessor returns "modreq(IsExternalInit) void"
    private static bool HasInitOnlyModifier(MetadataReader reader, MethodDefinition setter)
    {
        try
        {
            var blobReader = reader.GetBlobReader(setter.Signature);
            var header = blobReader.ReadSignatureHeader();
            if (header.IsGeneric)
            {
                blobReader.ReadCompressedInteger();
            }

            blobReader.ReadCompressedInteger(); // parameter count
            while (blobReader.RemainingBytes > 0)
            {
                var code = blobReader.ReadSignatureTypeCode();
                if (code is not (SignatureTypeCode.RequiredModifier or SignatureTypeCode.OptionalModifier))
                    return false;

                var modifier = blobReader.ReadTypeHandle();
                if (code == SignatureTypeCode.RequiredModifier && string.Equals(MetadataNames.GetFullName(reader, modifier), "System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal))
                    return true;
            }
        }
        catch (BadImageFormatException)
        {
        }

        return false;
    }
}
