using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class CSharpUnionCaseModel
{
    public CSharpUnionCaseModel(
        ITypeSymbol type,
        ITypeSymbol runtimeType,
        CSharpUnionCaseKind exactKinds,
        CSharpUnionCaseKind fallbackKinds,
        ImmutableArray<ITypeSymbol> converterTypes,
        bool acceptsNull,
        int? numberHandling)
    {
        Type = type;
        RuntimeType = runtimeType;
        ExactKinds = exactKinds;
        FallbackKinds = fallbackKinds;
        ConverterTypes = converterTypes;
        AcceptsNull = acceptsNull;
        NumberHandling = numberHandling;
    }

    public ITypeSymbol Type { get; }
    public ITypeSymbol RuntimeType { get; }

    /// <summary>Gets the YAML kinds the case type is represented by.</summary>
    public CSharpUnionCaseKind ExactKinds { get; }

    /// <summary>Gets the other YAML kinds the case can read when no case matches a value exactly.</summary>
    public CSharpUnionCaseKind FallbackKinds { get; }

    /// <summary>
    /// Gets the types whose runtime custom converter lets the case read any YAML kind as a fallback: the case type and the
    /// case types of a nested union. It is empty for a case that references an enclosing union, which never matches.
    /// </summary>
    public ImmutableArray<ITypeSymbol> ConverterTypes { get; }

    public bool AcceptsNull { get; }

    /// <summary>Gets the number handling declared on the union, when it applies to this numeric case.</summary>
    public int? NumberHandling { get; }
}
