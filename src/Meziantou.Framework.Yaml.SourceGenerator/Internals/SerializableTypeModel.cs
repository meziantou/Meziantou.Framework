using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class SerializableTypeModel
{
    public SerializableTypeModel(ITypeSymbol typeSymbol, string? typeInfoPropertyName, Location? location)
    {
        TypeSymbol = typeSymbol;
        TypeInfoPropertyName = typeInfoPropertyName;
        Location = location;
    }

    public ITypeSymbol TypeSymbol { get; }
    public string? TypeInfoPropertyName { get; }
    public Location? Location { get; }
}
