namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Specifies that <see cref="OnDeserialized"/> should be called after deserialization occurs.
/// </summary>
public interface IYamlOnDeserialized
{
    /// <summary>Called after the instance has been deserialized.</summary>
    void OnDeserialized();
}

