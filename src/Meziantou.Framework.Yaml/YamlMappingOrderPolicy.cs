namespace Meziantou.Framework.Yaml;

/// <summary>Controls member ordering for emitted mappings.</summary>
public enum YamlMappingOrderPolicy
{
    /// <summary>Preserve declaration order when no explicit order attribute is provided.</summary>
    Declaration = 0,

    /// <summary>Sort mapping entries by final emitted member name.</summary>
    Sorted = 1,
}

