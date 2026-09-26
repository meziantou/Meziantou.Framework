namespace Tomlyn.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance has been populated during deserialization.
/// </summary>
public interface ITomlOnDeserialized
{
    /// <summary>
    /// Called after the instance has been populated from TOML.
    /// </summary>
    void OnTomlDeserialized();
}
