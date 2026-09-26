namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance has been serialized to TOML.
/// </summary>
public interface ITomlOnSerialized
{
    /// <summary>
    /// Called after the instance has been serialized.
    /// </summary>
    void OnTomlSerialized();
}
