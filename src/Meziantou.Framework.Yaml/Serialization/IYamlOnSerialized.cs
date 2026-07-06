namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Specifies that <see cref="OnSerialized"/> should be called after serialization occurs.
/// </summary>
public interface IYamlOnSerialized
{
    /// <summary>Called after the instance has been serialized.</summary>
    void OnSerialized();
}

