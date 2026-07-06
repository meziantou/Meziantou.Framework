namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Specifies that <see cref="OnSerializing"/> should be called before serialization occurs.
/// </summary>
public interface IYamlOnSerializing
{
    /// <summary>Called before the instance is serialized.</summary>
    void OnSerializing();
}

