namespace Meziantou.Framework.Yaml;

/// <summary>Defines how object references are represented in YAML.</summary>
public enum YamlReferenceHandling
{
    /// <summary>Do not emit anchors/aliases for repeated references.</summary>
    None = 0,

    /// <summary>Preserve object references using YAML anchors/aliases.</summary>
    Preserve = 1,
}

