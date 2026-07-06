using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class SerializableTypeModel
{
    public SerializableTypeModel(ITypeSymbol typeSymbol, string? typeInfoPropertyName)
    {
        TypeSymbol = typeSymbol;
        TypeInfoPropertyName = typeInfoPropertyName;
    }

    public ITypeSymbol TypeSymbol { get; }
    public string? TypeInfoPropertyName { get; }
}
