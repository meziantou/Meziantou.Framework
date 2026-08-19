namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>Exposes the mapping keys of a type so a union case can be classified structurally.</summary>
internal interface IYamlUnionCaseShapeProvider
{
    /// <summary>Gets the mapping keys declared by the converted type, or <see langword="null"/> when they are unknown.</summary>
    IReadOnlyList<YamlUnionCaseProperty>? GetUnionCaseProperties(YamlReaderWriterBase readerWriter, out bool disallowUnmappedProperties);
}
