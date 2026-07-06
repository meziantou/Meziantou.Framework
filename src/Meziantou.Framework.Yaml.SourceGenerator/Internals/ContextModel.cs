using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class ContextModel
{
    public ContextModel(
        INamedTypeSymbol contextSymbol,
        string namespaceName,
        string typeName,
        ImmutableArray<SerializableTypeModel> serializableTypes,
        ImmutableArray<YamlSerializerContextGenerator.DerivedTypeMappingModel> derivedTypeMappings,
        SourceGenerationOptionsModel sourceGenerationOptions,
        bool isValid)
    {
        ContextSymbol = contextSymbol;
        NamespaceName = namespaceName;
        TypeName = typeName;
        SerializableTypes = serializableTypes;
        DerivedTypeMappings = derivedTypeMappings;
        SourceGenerationOptions = sourceGenerationOptions;
        IsValid = isValid;
    }

    public INamedTypeSymbol ContextSymbol { get; }
    public string NamespaceName { get; }
    public string TypeName { get; }
    public ImmutableArray<SerializableTypeModel> SerializableTypes { get; }
    public ImmutableArray<YamlSerializerContextGenerator.DerivedTypeMappingModel> DerivedTypeMappings { get; }
    public SourceGenerationOptionsModel SourceGenerationOptions { get; }
    public bool IsValid { get; }
}
