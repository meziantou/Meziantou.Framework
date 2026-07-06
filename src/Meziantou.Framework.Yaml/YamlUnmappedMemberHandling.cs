namespace Meziantou.Framework.Yaml;

/// <summary>Determines how unmapped YAML members are handled during object deserialization.</summary>
public enum YamlUnmappedMemberHandling
{
    /// <summary>Unmapped members are ignored.</summary>
    Skip = 0,

    /// <summary>
    /// Encountering an unmapped member causes a <see cref="YamlException"/> to be thrown.
    /// </summary>
    Disallow = 1,
}
