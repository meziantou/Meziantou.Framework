namespace Tomlyn.Serialization;

/// <summary>
/// Defines a callback that is invoked before an instance is serialized to TOML.
/// </summary>
public interface ITomlOnSerializing
{
    /// <summary>
    /// Called before the instance is serialized.
    /// </summary>
    void OnTomlSerializing();
}
