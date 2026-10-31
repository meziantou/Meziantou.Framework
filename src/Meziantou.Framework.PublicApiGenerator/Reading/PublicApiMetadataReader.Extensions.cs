using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

// C# 14 extension blocks. For each block, the compiler emits in the static class:
// - a nested grouping type "<G>$HASH" per receiver type and constraints, whose type parameters are renamed "$T0", "$T1"...
//   It declares the members as written in source, each marked with [ExtensionMarker("<M>$HASH2")].
// - in the grouping type, a marker type "<M>$HASH2" per block. Its static method "<Extension>$" declares the receiver parameter,
//   and its type parameters have the names and the constraints written in source.
// - an implementation method per member (or accessor) on the static class. Nothing in metadata links it to the member: the
//   compiler matches them by signature, as done here. The implementation takes the type parameters of the block followed by
//   the ones of the member, and the receiver as its first parameter for instance members.
internal sealed partial class PublicApiMetadataReader
{
    private const string ExtensionMarkerAttributeFullName = "System.Runtime.CompilerServices.ExtensionMarkerAttribute";
    private const string ExtensionMarkerMethodName = "<Extension>$";

    // Set while reading the members of a grouping type, so that they use the type parameter names of the block
    private (TypeDefinitionHandle TypeHandle, ImmutableArray<string> Names) _typeParameterNamesOverride;

    // The members of a grouping type are not declared in a class hierarchy: they cannot hide inherited members
    private bool _isReadingExtensionBlock;

    private ImmutableArray<PublicApiExtensionBlock> ReadExtensionBlocks(TypeDefinition typeDefinition, ImmutableArray<PublicApiMember> members)
    {
        var result = ImmutableArray.CreateBuilder<PublicApiExtensionBlock>();
        foreach (var groupingTypeHandle in typeDefinition.GetNestedTypes())
        {
            var groupingType = _metadataReader.GetTypeDefinition(groupingTypeHandle);
            if (!IsExtensionType(groupingType, "<G>$") || !HasAttribute(groupingType.GetCustomAttributes(), ExtensionAttributeFullName))
                continue;

            foreach (var markerTypeHandle in groupingType.GetNestedTypes())
            {
                var markerType = _metadataReader.GetTypeDefinition(markerTypeHandle);
                if (!IsExtensionType(markerType, "<M>$"))
                    continue;

                var block = ReadExtensionBlock(groupingTypeHandle, groupingType, markerTypeHandle, markerType);
                if (block is not null)
                {
                    LinkImplementations(block, members);
                    result.Add(block);
                }
            }
        }

        return [.. result
            .OrderBy(static block => PublicApiOrdering.GetTypeSortText(block.Receiver.Type), StringComparer.Ordinal)
            .ThenBy(static block => block.Receiver.Name, StringComparer.Ordinal)
            .ThenBy(static block => block.MarkerTypeMetadataName, StringComparer.Ordinal)];
    }

    private bool IsExtensionType(TypeDefinition typeDefinition, string namePrefix)
    {
        return typeDefinition.Attributes.HasFlag(TypeAttributes.SpecialName) &&
               IsVisibleNestedType(typeDefinition) &&
               _metadataReader.GetString(typeDefinition.Name).StartsWith(namePrefix, StringComparison.Ordinal);
    }

    private PublicApiExtensionBlock? ReadExtensionBlock(TypeDefinitionHandle groupingTypeHandle, TypeDefinition groupingType, TypeDefinitionHandle markerTypeHandle, TypeDefinition markerType)
    {
        var markerName = _metadataReader.GetString(markerType.Name);
        var receiverMethodHandle = markerType.GetMethods().FirstOrDefault(handle => string.Equals(_metadataReader.GetString(_metadataReader.GetMethodDefinition(handle).Name), ExtensionMarkerMethodName, StringComparison.Ordinal));
        if (receiverMethodHandle.IsNil)
            return null;

        var receiverMethod = _metadataReader.GetMethodDefinition(receiverMethodHandle);
        var receiverSignature = DecodeMethodSignature(markerTypeHandle, receiverMethodHandle, receiverMethod).Signature;
        if (receiverSignature.ParameterTypes.Length != 1)
            return null;

        var receiver = ReadParameters(markerTypeHandle, receiverMethodHandle, receiverMethod, receiverSignature.ParameterTypes, isExtensionMethod: false)[0];
        var genericParameters = ReadGenericParameters(markerType.GetGenericParameters(), BuildGenericContext(markerTypeHandle), markerTypeHandle, methodHandle: default);

        ImmutableArray<PublicApiMember> members;
        var previousOverride = _typeParameterNamesOverride;
        var previousIsReadingExtensionBlock = _isReadingExtensionBlock;
        try
        {
            _typeParameterNamesOverride = (groupingTypeHandle, [.. genericParameters.Select(static parameter => parameter.Name)]);
            _isReadingExtensionBlock = true;
            members = [.. ReadMembers(groupingTypeHandle, groupingType).Where(member => string.Equals(GetExtensionMarkerName(member), markerName, StringComparison.Ordinal))];
        }
        finally
        {
            _typeParameterNamesOverride = previousOverride;
            _isReadingExtensionBlock = previousIsReadingExtensionBlock;
        }

        if (members.IsEmpty)
            return null;

        var block = new PublicApiExtensionBlock(_metadataReader.GetString(groupingType.Name), markerName, receiver, genericParameters, members);
        foreach (var member in members)
        {
            member.ExtensionBlock = block;
            foreach (var accessor in GetAccessors(member))
            {
                accessor.ExtensionBlock = block;
            }
        }

        return block;
    }

    private static string? GetExtensionMarkerName(PublicApiMember member)
    {
        foreach (var attribute in member.Attributes)
        {
            if (string.Equals(attribute.AttributeType.FullName, ExtensionMarkerAttributeFullName, StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string name)
            {
                return name;
            }
        }

        return null;
    }

    private static IEnumerable<PublicApiMethod> GetAccessors(PublicApiMember member)
    {
        return member switch
        {
            PublicApiMethod method => [method],
            PublicApiProperty property => new[] { property.GetMethod, property.SetMethod }.OfType<PublicApiMethod>(),
            PublicApiEvent @event => new[] { @event.AddMethod, @event.RemoveMethod, @event.RaiseMethod }.OfType<PublicApiMethod>(),
            _ => [],
        };
    }

    private static void LinkImplementations(PublicApiExtensionBlock block, ImmutableArray<PublicApiMember> members)
    {
        foreach (var member in block.Members)
        {
            foreach (var method in GetAccessors(member))
            {
                foreach (var candidate in members)
                {
                    if (candidate is PublicApiMethod { ImplementedExtensionMember: null } implementation && IsImplementation(block, method, implementation))
                    {
                        method.ExtensionImplementation = implementation;
                        implementation.ImplementedExtensionMember = member;
                        break;
                    }
                }
            }
        }
    }

    private static bool IsImplementation(PublicApiExtensionBlock block, PublicApiMethod method, PublicApiMethod implementation)
    {
        if (!implementation.IsStatic || !string.Equals(implementation.Name, method.Name, StringComparison.Ordinal))
            return false;

        var genericParameterNames = block.GenericParameters.Concat(method.GenericParameters).Select(static parameter => parameter.Name);
        if (!implementation.GenericParameters.Select(static parameter => parameter.Name).SequenceEqual(genericParameterNames, StringComparer.Ordinal))
            return false;

        var offset = method.IsStatic ? 0 : 1;
        if (implementation.Parameters.Length != method.Parameters.Length + offset)
            return false;

        if (offset == 1 && !IsSameParameterType(block.Receiver, implementation.Parameters[0]))
            return false;

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (!IsSameParameterType(method.Parameters[i], implementation.Parameters[i + offset]))
                return false;
        }

        return implementation.ReturnRefKind == method.ReturnRefKind && IsSameType(implementation.ReturnType, method.ReturnType);
    }

    private static bool IsSameParameterType(PublicApiParameter x, PublicApiParameter y) => IsSameRefKind(x.RefKind, y.RefKind) && IsSameType(x.Type, y.Type);

    // in, ref readonly and ref are the same in signatures
    private static bool IsSameRefKind(PublicApiRefKind x, PublicApiRefKind y) => (x == PublicApiRefKind.None) == (y == PublicApiRefKind.None) && (x == PublicApiRefKind.Out) == (y == PublicApiRefKind.Out);

    // Type parameters have the same names in the block and in the implementation, so types are compared by name, ignoring nullable reference types
    private static bool IsSameType(PublicApiTypeReference x, PublicApiTypeReference y) => string.Equals(GetTypeText(x), GetTypeText(y), StringComparison.Ordinal);

    private static string GetTypeText(PublicApiTypeReference type)
    {
        var writer = new DeclarationWriter("\n");
        CSharpCompilableFormatter.WriteTypeReference(writer, type, includeNullableAnnotations: false);
        return writer.GetText();
    }
}
