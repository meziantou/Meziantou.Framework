using System.Reflection;
using System.Reflection.Metadata;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A field.</summary>
public sealed class FieldElement : MemberElement
{
    private readonly FieldAttributes _attributes;
    private readonly Lazy<TypeReference> _type;
    private readonly Lazy<IReadOnlyList<AttributeElement>> _customAttributes;

    internal FieldElement(TypeElement declaringType, FieldDefinitionHandle handle)
        : base(declaringType, declaringType.Image.Reader.GetString(declaringType.Image.Reader.GetFieldDefinition(handle).Name))
    {
        Handle = handle;
        _attributes = declaringType.Image.Reader.GetFieldDefinition(handle).Attributes;
        _type = new(() => DeclaringType.Image.Reader.GetFieldDefinition(Handle).DecodeSignature(DeclaringType.Provider, DeclaringType.GenericContext));
        _customAttributes = new(() => AttributeElement.Create(DeclaringType.Provider, DeclaringType.Image.Reader, DeclaringType.Image.Reader.GetFieldDefinition(Handle).GetCustomAttributes()));
    }

    internal FieldDefinitionHandle Handle { get; }

    /// <summary>Gets the type of the field.</summary>
    public TypeReference Type => _type.Value;

    /// <summary>Gets a value indicating whether the field is <c>readonly</c>.</summary>
    public bool IsReadOnly => _attributes.HasFlag(FieldAttributes.InitOnly);

    /// <summary>Gets a value indicating whether the field is a constant (<c>const</c>, or a value of an enum).</summary>
    public bool IsConstant => _attributes.HasFlag(FieldAttributes.Literal);

    /// <inheritdoc />
    public override ElementAccessibility Accessibility => GetAccessibility(_attributes);

    /// <inheritdoc />
    public override bool IsStatic => _attributes.HasFlag(FieldAttributes.Static);

    /// <inheritdoc />
    public override IReadOnlyList<AttributeElement> Attributes => _customAttributes.Value;

    /// <summary>Gets <see langword="null"/>: a field has no code, so the PDB records no location for it.</summary>
    public override SourceLocation? Location => null;
}
