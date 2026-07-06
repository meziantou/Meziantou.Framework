using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class CSharpUnionCaseModel
{
    public CSharpUnionCaseModel(ITypeSymbol type, ITypeSymbol runtimeType, CSharpUnionCaseKind kind, bool acceptsNull)
    {
        Type = type;
        RuntimeType = runtimeType;
        Kind = kind;
        AcceptsNull = acceptsNull;
    }

    public ITypeSymbol Type { get; }
    public ITypeSymbol RuntimeType { get; }
    public CSharpUnionCaseKind Kind { get; }
    public bool AcceptsNull { get; }
}
