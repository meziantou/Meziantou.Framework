namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Specifies that <see cref="OnDeserializing"/> should be called before deserialization occurs.
/// </summary>
public interface IYamlOnDeserializing
{
    /// <summary>Called before the instance is populated during deserialization.</summary>
    void OnDeserializing();
}

