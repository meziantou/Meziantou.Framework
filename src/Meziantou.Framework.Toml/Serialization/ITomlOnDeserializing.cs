namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance is created but before it is populated during deserialization.
/// </summary>
public interface ITomlOnDeserializing
{
    /// <summary>
    /// Called before the instance is populated from TOML.
    /// </summary>
    void OnTomlDeserializing();
}
