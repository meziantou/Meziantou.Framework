namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Describes the kind of type a <see cref="YamlTypeClassifierFactory"/> is asked to classify.</summary>
public enum YamlTypeClassifierKind
{
    /// <summary>No kind is specified.</summary>
    None = 0,

    /// <summary>The type is a C# union type.</summary>
    Union = 1,
}
