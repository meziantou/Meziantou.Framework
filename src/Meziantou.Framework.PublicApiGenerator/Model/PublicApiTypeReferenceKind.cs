namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Identifies the kind of a <see cref="PublicApiTypeReference"/>.</summary>
public enum PublicApiTypeReferenceKind
{
    NamedType,
    TypeParameter,
    ArrayType,
    PointerType,
    FunctionPointerType,
}
