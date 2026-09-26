namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance is created but before it is populated during deserialization.
/// </summary>
/// <remarks>
/// The members set when the instance is created are set before the callback: the constructor arguments and, with the
/// source generator, the <see langword="required"/> members, which it sets in an object initializer. Reflection-based
/// metadata sets the <see langword="required"/> members after the callback.
/// </remarks>
public interface ITomlOnDeserializing
{
    /// <summary>
    /// Called before the instance is populated from TOML.
    /// </summary>
    void OnTomlDeserializing();
}
