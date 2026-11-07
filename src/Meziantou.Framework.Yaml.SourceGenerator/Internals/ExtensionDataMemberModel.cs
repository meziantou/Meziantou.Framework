using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class ExtensionDataMemberModel
{
    public ExtensionDataMemberModel(
        ISymbol symbol,
        ITypeSymbol type,
        ExtensionDataKind kind,
        ITypeSymbol? dictionaryValueType,
        Func<string, string> accessExpression,
        Func<string, string>? assignExpression,
        bool canAssign,
        bool isInitOnly)
    {
        Symbol = symbol;
        Type = type;
        Kind = kind;
        DictionaryValueType = dictionaryValueType;
        AccessExpression = accessExpression;
        AssignExpression = assignExpression;
        CanAssign = canAssign;
        IsInitOnly = isInitOnly;
    }

    public ISymbol Symbol { get; }
    public ITypeSymbol Type { get; }
    public ExtensionDataKind Kind { get; }
    public ITypeSymbol? DictionaryValueType { get; }

    /// <summary>
    /// Builds the expression reading the member from the given receiver expression.
    /// </summary>
    public Func<string, string> AccessExpression { get; }
    public Func<string, string>? AssignExpression { get; }
    public bool CanAssign { get; }
    public bool IsInitOnly { get; }
}
