using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// The types whose type argument describes the value a tag applies to, resolved once per compilation.
/// </summary>
internal sealed class KnownTypes
{
    private readonly INamedTypeSymbol? _guidType;
    private readonly INamedTypeSymbol? _keyValuePairType;
    private readonly INamedTypeSymbol? _expressionType;
    private readonly INamedTypeSymbol? _asyncEnumerableType;
    private readonly INamedTypeSymbol[] _valueWrapperTypes;
    private readonly INamedTypeSymbol[] _elementWrapperTypes;

    public KnownTypes(Compilation compilation)
    {
        _guidType = compilation.GetBestTypeByMetadataName("System.Guid");
        _keyValuePairType = compilation.GetBestTypeByMetadataName("System.Collections.Generic.KeyValuePair`2");
        _expressionType = compilation.GetBestTypeByMetadataName("System.Linq.Expressions.Expression`1");
        _asyncEnumerableType = compilation.GetBestTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");

        // Types whose tag describes their value
        _valueWrapperTypes = GetTypes(
            compilation,
            "System.Threading.Tasks.Task`1",
            "System.Threading.Tasks.ValueTask`1",
            "System.Lazy`1");

        // Types whose tag describes their elements, and that do not implement IEnumerable<T>
        _elementWrapperTypes = GetTypes(
            compilation,
            "System.Span`1",
            "System.ReadOnlySpan`1",
            "System.Memory`1",
            "System.ReadOnlyMemory`1",
            "System.Collections.Generic.IAsyncEnumerable`1",
            "System.Collections.Generic.IAsyncEnumerator`1");
    }

    public bool IsGuid(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type, _guidType);
    }

    public bool IsKeyValuePair(INamedTypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _keyValuePairType);
    }

    /// <summary>
    /// Returns the delegate type of a parameter that accepts a lambda: the delegate itself, or <c>TDelegate</c> for <c>Expression&lt;TDelegate&gt;</c>.
    /// </summary>
    public INamedTypeSymbol? GetDelegateType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType)
            return delegateType;

        // Queryable methods take Expression<Func<...>>
        if (type is INamedTypeSymbol expressionType && SymbolEqualityComparer.Default.Equals(expressionType.OriginalDefinition, _expressionType))
            return expressionType.TypeArguments[0] as INamedTypeSymbol;

        return null;
    }

    /// <summary>
    /// Returns the type of the value a tag describes for a wrapper type: the element of a collection, or the value of a
    /// <c>Nullable&lt;T&gt;</c>, a <c>Task&lt;T&gt;</c>, a <c>ValueTask&lt;T&gt;</c>, a <c>Lazy&lt;T&gt;</c>, a span, or a memory.
    /// </summary>
    public bool TryGetWrappedType(INamedTypeSymbol type, [NotNullWhen(true)] out ITypeSymbol? wrappedType)
    {
        wrappedType = null;
        if (type.SpecialType is SpecialType.System_String)
            return false;

        if (type.TypeArguments.Length is 1)
        {
            var definition = type.OriginalDefinition;
            if (definition.SpecialType is SpecialType.System_Nullable_T or SpecialType.System_Collections_Generic_IEnumerable_T or SpecialType.System_Collections_Generic_IEnumerator_T ||
                Contains(_valueWrapperTypes, definition) ||
                Contains(_elementWrapperTypes, definition))
            {
                wrappedType = type.TypeArguments[0];
                return true;
            }
        }

        ITypeSymbol? elementType = null;
        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T ||
                SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, _asyncEnumerableType))
            {
                if (elementType is not null && !SymbolEqualityComparer.Default.Equals(elementType, @interface.TypeArguments[0]))
                    return false;

                elementType = @interface.TypeArguments[0];
            }
        }

        wrappedType = elementType;
        return wrappedType is not null;
    }

    /// <summary>
    /// Returns whether the tags of a value of this type describe its elements, as opposed to the value itself or the value of a wrapper.
    /// </summary>
    public bool IsCollectionType(ITypeSymbol type)
    {
        return type is IArrayTypeSymbol ||
            (type is INamedTypeSymbol namedType &&
             namedType.OriginalDefinition.SpecialType is not SpecialType.System_Nullable_T &&
             !Contains(_valueWrapperTypes, namedType.OriginalDefinition) &&
             TryGetWrappedType(namedType, out _));
    }

    /// <summary>
    /// Returns whether the tags of a value of this type describe a dictionary, whose keys and values are tagged separately.
    /// </summary>
    public bool IsKeyValueShaped(ITypeSymbol type)
    {
        for (var depth = 0; depth < 8; depth++)
        {
            switch (type)
            {
                case IArrayTypeSymbol arrayType:
                    type = arrayType.ElementType;
                    break;

                case INamedTypeSymbol namedType when IsKeyValuePair(namedType):
                    return true;

                case INamedTypeSymbol namedType when TryGetWrappedType(namedType, out var wrappedType):
                    type = wrappedType;
                    break;

                default:
                    return false;
            }
        }

        return false;
    }

    private static INamedTypeSymbol[] GetTypes(Compilation compilation, params string[] metadataNames)
    {
        var result = new List<INamedTypeSymbol>(metadataNames.Length);
        foreach (var metadataName in metadataNames)
        {
            if (compilation.GetBestTypeByMetadataName(metadataName) is { } type)
            {
                result.Add(type);
            }
        }

        return [.. result];
    }

    private static bool Contains(INamedTypeSymbol[] types, INamedTypeSymbol type)
    {
        foreach (var candidate in types)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, type))
                return true;
        }

        return false;
    }
}
