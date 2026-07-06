namespace Meziantou.Framework.Yaml;

/// <summary>
/// Resolves type metadata used by <see cref="YamlSerializer"/>.
/// </summary>
public interface IYamlTypeInfoResolver
{
    /// <summary>Gets type metadata for a CLR type.</summary>
    /// <param name="type">The CLR type to resolve.</param>
    /// <param name="options">The serializer options in use.</param>
    /// <returns>The metadata for <paramref name="type"/>, or <see langword="null"/> when not available.</returns>
    YamlTypeInfo? GetTypeInfo(Type type, YamlSerializerOptions options);
}

