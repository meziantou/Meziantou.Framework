namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Defines a callback that is invoked after an instance is created but before it is populated during deserialization.
/// </summary>
/// <remarks>
/// The members set when the instance is created are set before the callback: the constructor arguments and, with the
/// source generator, the <see langword="required"/> members it sets in an object initializer. It does so for a struct, a
/// generic type, or a type created with a constructor that has parameters. Reflection-based metadata sets the
/// <see langword="required"/> members after the callback.
/// </remarks>
public interface ITomlOnDeserializing
{
    /// <summary>
    /// Called before the instance is populated from TOML.
    /// </summary>
    void OnTomlDeserializing();
}
