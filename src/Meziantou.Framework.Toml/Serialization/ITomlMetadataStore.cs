using Tomlyn.Model;

namespace Tomlyn.Serialization;

/// <summary>
/// Associates TOML trivia/comment metadata with object instances.
/// </summary>
public interface ITomlMetadataStore
{
    /// <summary>
    /// Tries to get metadata for an instance.
    /// </summary>
    bool TryGetProperties(object instance, out TomlPropertiesMetadata? metadata);

    /// <summary>
    /// Sets metadata for an instance.
    /// </summary>
    void SetProperties(object instance, TomlPropertiesMetadata? metadata);
}

