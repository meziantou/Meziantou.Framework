using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class CSharpUnionCaseModel
{
    public CSharpUnionCaseModel(ITypeSymbol type, ITypeSymbol runtimeType, CSharpUnionCaseKind kind, bool acceptsNull, int? numberHandling)
    {
        Type = type;
        RuntimeType = runtimeType;
        Kind = kind;
        AcceptsNull = acceptsNull;
        NumberHandling = numberHandling;
    }

    public ITypeSymbol Type { get; }
    public ITypeSymbol RuntimeType { get; }
    public CSharpUnionCaseKind Kind { get; }
    public bool AcceptsNull { get; }

    /// <summary>Gets the number handling declared on the union, when it applies to this numeric case.</summary>
    public int? NumberHandling { get; }
}
