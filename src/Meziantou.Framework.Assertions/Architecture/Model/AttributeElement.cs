using System.Collections.Immutable;
using System.Reflection.Metadata;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>An attribute applied to an assembly, a type, a member or a parameter.</summary>
public sealed class AttributeElement
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyNamedArguments = new Dictionary<string, object?>(StringComparer.Ordinal);

    private readonly Lazy<(IReadOnlyList<object?> ConstructorArguments, IReadOnlyDictionary<string, object?> NamedArguments)> _arguments;

    private AttributeElement(TypeReference type, TypeReferenceProvider provider, CustomAttribute attribute)
    {
        Type = type;
        _arguments = new(() => DecodeArguments(provider, attribute));
    }

    /// <summary>Gets the type of the attribute.</summary>
    public TypeReference Type { get; }

    /// <summary>
    /// Gets the arguments of the constructor. A value is a primitive value, a <see cref="string"/>, a <see cref="TypeReference"/> for <c>typeof</c>,
    /// an <see cref="IReadOnlyList{T}"/> of values for an array, or <see langword="null"/>. The list is empty when the values cannot be decoded, for instance when an enum type cannot be found.
    /// </summary>
    public IReadOnlyList<object?> ConstructorArguments => _arguments.Value.ConstructorArguments;

    /// <summary>Gets the values of the properties and fields set by the attribute, by name. The values have the same shape as <see cref="ConstructorArguments"/>.</summary>
    public IReadOnlyDictionary<string, object?> NamedArguments => _arguments.Value.NamedArguments;

    /// <summary>Returns the type of the attribute.</summary>
    /// <returns>The type of the attribute.</returns>
    public override string ToString() => Type.ToString();

    internal static IReadOnlyList<AttributeElement> Create(TypeReferenceProvider provider, MetadataReader reader, CustomAttributeHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];

        var result = new List<AttributeElement>(handles.Count);
        foreach (var handle in handles)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (GetAttributeType(provider, reader, attribute) is { } type)
            {
                result.Add(new AttributeElement(type, provider, attribute));
            }
        }

        return result;
    }

    // The values of the arguments that are types (typeof), which are dependencies of the code that applies the attribute
    internal IEnumerable<TypeReference> GetTypeArguments()
    {
        foreach (var value in ConstructorArguments.Concat(NamedArguments.Values))
        {
            foreach (var type in GetTypes(value))
            {
                yield return type;
            }
        }

        static IEnumerable<TypeReference> GetTypes(object? value)
        {
            if (value is TypeReference type)
            {
                yield return type;
            }
            else if (value is IReadOnlyList<object?> values)
            {
                foreach (var item in values)
                {
                    foreach (var itemType in GetTypes(item))
                    {
                        yield return itemType;
                    }
                }
            }
        }
    }

    private static TypeReference? GetAttributeType(TypeReferenceProvider provider, MetadataReader reader, CustomAttribute attribute)
    {
        try
        {
            var parent = attribute.Constructor.Kind switch
            {
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
                _ => default,
            };

            return parent.Kind switch
            {
                HandleKind.TypeDefinition or HandleKind.TypeReference => provider.GetNamedType(parent),
                HandleKind.TypeSpecification => provider.GetTypeFromSpecification(reader, GenericContext.Empty, (TypeSpecificationHandle)parent, rawTypeKind: 0),
                _ => null,
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static (IReadOnlyList<object?>, IReadOnlyDictionary<string, object?>) DecodeArguments(TypeReferenceProvider provider, CustomAttribute attribute)
    {
        try
        {
            var value = attribute.DecodeValue(provider);
            var constructorArguments = value.FixedArguments.Select(argument => ConvertValue(argument.Value)).ToList();
            var namedArguments = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var argument in value.NamedArguments)
            {
                if (argument.Name is not null)
                {
                    namedArguments[argument.Name] = ConvertValue(argument.Value);
                }
            }

            return (constructorArguments, namedArguments);
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
        {
            return ([], EmptyNamedArguments);
        }
    }

    private static object? ConvertValue(object? value)
    {
        return value is ImmutableArray<CustomAttributeTypedArgument<TypeReference>> array
            ? array.Select(item => ConvertValue(item.Value)).ToList()
            : value;
    }
}
