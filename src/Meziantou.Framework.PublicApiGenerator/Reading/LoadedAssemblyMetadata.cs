using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.PublicApiGenerator;

// Reads from the metadata of a loaded assembly the data that reflection does not expose
internal static class LoadedAssemblyMetadata
{
    private const string NullableAttributeFullName = "System.Runtime.CompilerServices.NullableAttribute";
    private const string TupleElementNamesAttributeFullName = "System.Runtime.CompilerServices.TupleElementNamesAttribute";

    private static readonly ConditionalWeakTable<Assembly, StrongBox<MetadataReader?>> MetadataReaders = new();

    // Reads the attributes of a constraint of a generic parameter: the flags of its NullableAttribute, and the names of its TupleElementNamesAttribute.
    // The constraints are in the same order as the ones returned by Type.GetGenericParameterConstraints.
    // Returns false when the metadata of the assembly cannot be read.
    public static bool TryGetConstraintAnnotations(Type genericParameter, int constraintIndex, int constraintCount, out byte[]? nullableFlags, out string?[]? tupleElementNames)
    {
        nullableFlags = null;
        tupleElementNames = null;
        var reader = GetMetadataReader(genericParameter.Assembly);
        if (reader is null)
            return false;

        try
        {
            var handle = MetadataTokens.EntityHandle(genericParameter.MetadataToken);
            if (handle.Kind != HandleKind.GenericParameter)
                return false;

            var constraints = reader.GetGenericParameter((GenericParameterHandle)handle).GetConstraints();
            if (constraints.Count != constraintCount)
                return false;

            ReadAnnotations(reader, reader.GetGenericParameterConstraint(constraints[constraintIndex]).GetCustomAttributes(), out nullableFlags, out tupleElementNames);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    // Reads the interfaces a type declares, which reflection cannot tell from the ones its base type implements, with the attributes
    // of the interface implementations: the flags of their NullableAttribute, and the names of their TupleElementNamesAttribute.
    // Returns false when the metadata of the assembly cannot be read, or when an interface cannot be resolved.
    public static bool TryGetDeclaredInterfaces(Type type, out List<(Type Interface, byte[]? NullableFlags, string?[]? TupleElementNames)> interfaces)
    {
        interfaces = [];
        var reader = GetMetadataReader(type.Assembly);
        if (reader is null)
            return false;

        try
        {
            var handle = MetadataTokens.EntityHandle(type.MetadataToken);
            if (handle.Kind != HandleKind.TypeDefinition)
                return false;

            foreach (var interfaceImplementationHandle in reader.GetTypeDefinition((TypeDefinitionHandle)handle).GetInterfaceImplementations())
            {
                var interfaceImplementation = reader.GetInterfaceImplementation(interfaceImplementationHandle);
                if (ResolveType(type, interfaceImplementation.Interface) is not { } @interface)
                    return false;

                ReadAnnotations(reader, interfaceImplementation.GetCustomAttributes(), out var nullableFlags, out var tupleElementNames);
                interfaces.Add((@interface, nullableFlags, tupleElementNames));
            }

            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static void ReadAnnotations(MetadataReader reader, CustomAttributeHandleCollection attributes, out byte[]? nullableFlags, out string?[]? tupleElementNames)
    {
        nullableFlags = null;
        tupleElementNames = null;
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            switch (GetAttributeTypeFullName(reader, attribute))
            {
                case NullableAttributeFullName when PublicApiMetadataReader.TryDecodeNullableAttributeFlags(reader.GetBlobBytes(attribute.Value), out var flags):
                    nullableFlags = [.. flags];
                    break;

                case TupleElementNamesAttributeFullName when PublicApiMetadataReader.DecodeTupleElementNames(reader.GetBlobReader(attribute.Value)) is { IsDefault: false } names:
                    tupleElementNames = [.. names];
                    break;
            }
        }
    }

    // The interface is resolved with the generic parameters of the type, as Type.GetInterfaces does
    private static Type? ResolveType(Type declaringType, EntityHandle handle)
    {
        try
        {
            return declaringType.Module.ResolveType(MetadataTokens.GetToken(handle), declaringType.GetGenericArguments(), genericMethodArguments: null);
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or IOException or TypeLoadException)
        {
            return null;
        }
    }

    private static MetadataReader? GetMetadataReader(Assembly assembly)
    {
        return MetadataReaders.GetValue(assembly, static assembly => new StrongBox<MetadataReader?>(CreateMetadataReader(assembly))).Value;
    }

    private static MetadataReader? CreateMetadataReader(Assembly assembly)
    {
        // The metadata stays in memory as long as the assembly is loaded, and the reader is only reachable from the assembly
        unsafe
        {
            return assembly.TryGetRawMetadata(out var blob, out var length) ? new MetadataReader(blob, length) : null;
        }
    }

    private static string GetAttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MethodDefinition:
                {
                    var type = reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType());
                    return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
                }

            case HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent:
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)parent);
                    return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
                }

            default:
                return string.Empty;
        }
    }
}
