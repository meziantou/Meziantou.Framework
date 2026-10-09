using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

// Types decoded from signatures, before the nullable annotations and the tuple element names are applied
internal abstract record RawType
{
    public bool HasInModifier { get; init; }
    public bool HasIsReadOnlyModifier { get; init; }
    public bool HasRequiresLocationModifier { get; init; }
    public bool HasIsVolatileModifier { get; init; }
    public bool HasOutModifier { get; init; }

    // A type definition, possibly nested. Generic types are wrapped in a GenericInstance.
    public sealed record Named(
        string Namespace,
        string MetadataName,
        Named? ContainingType,
        string? AssemblyName,
        bool IsValueType,
        bool IsPrimitive = false,
        bool IsFromSerializedName = false) : RawType
    {
        public string FullName => ContainingType is null
            ? (Namespace.Length == 0 ? MetadataName : Namespace + "." + MetadataName)
            : ContainingType.FullName + "+" + MetadataName;

        public bool IsSystemType(string metadataName) => ContainingType is null && Namespace is "System" && string.Equals(MetadataName, metadataName, StringComparison.Ordinal);
    }

    // The type arguments include the ones of the containing types, as metadata does
    public sealed record GenericInstance(Named Definition, ImmutableArray<RawType> TypeArguments) : RawType;

    public sealed record TypeParameter(string Name, int Index, bool IsMethodTypeParameter) : RawType;

    public sealed record Array(RawType ElementType, int Rank, bool IsSZArray) : RawType;

    public sealed record Pointer(RawType ElementType) : RawType;

    public sealed record ByReference(RawType ElementType) : RawType;

    public sealed record FunctionPointer(MethodSignature<RawType> Signature) : RawType;
}
