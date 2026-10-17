using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

// Parses the type names serialized in custom attribute blobs (e.g. "System.Collections.Generic.List`1[[System.Int32, mscorlib]], mscorlib")
internal static class SerializedTypeNameParser
{
    private static readonly TypeNameParseOptions ParseOptions = new() { MaxNodes = 1024 };

    public static RawType? Parse(string name)
    {
        if (!TypeName.TryParse(name.AsSpan().Trim(), out var typeName, ParseOptions))
            return null;

        return Convert(typeName);
    }

    private static RawType Convert(TypeName typeName)
    {
        if (typeName.IsArray)
        {
            var elementType = Convert(typeName.GetElementType());
            return typeName.IsSZArray
                ? new RawType.Array(elementType, Rank: 1, IsSZArray: true)
                : new RawType.Array(elementType, typeName.GetArrayRank(), IsSZArray: false);
        }

        if (typeName.IsPointer)
            return new RawType.Pointer(Convert(typeName.GetElementType()));

        if (typeName.IsByRef)
            return new RawType.ByReference(Convert(typeName.GetElementType()));

        if (typeName.IsConstructedGenericType)
        {
            var definition = ConvertNamed(typeName.GetGenericTypeDefinition(), typeName.AssemblyName?.Name);
            return new RawType.GenericInstance(definition, [.. typeName.GetGenericArguments().Select(Convert)]);
        }

        return ConvertNamed(typeName, typeName.AssemblyName?.Name);
    }

    private static RawType.Named ConvertNamed(TypeName typeName, string? assemblyName)
    {
        assemblyName ??= typeName.AssemblyName?.Name;
        if (typeName.IsNested)
        {
            var containingType = ConvertNamed(typeName.DeclaringType, assemblyName);
            return new RawType.Named(string.Empty, typeName.Name, containingType, assemblyName, IsValueType: false, IsFromSerializedName: true);
        }

        var fullName = typeName.FullName;
        var separatorIndex = fullName.LastIndexOf(".", StringComparison.Ordinal);
        var namespaceName = separatorIndex < 0 ? string.Empty : fullName[..separatorIndex];
        var metadataName = separatorIndex < 0 ? fullName : fullName[(separatorIndex + 1)..];
        return new RawType.Named(namespaceName, metadataName, ContainingType: null, assemblyName, IsValueType: false, IsFromSerializedName: true);
    }
}
