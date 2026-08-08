namespace Meziantou.Framework.Yaml;

/// <summary>Defines how object references are represented in YAML.</summary>
public enum YamlReferenceHandling
{
    /// <summary>Do not emit anchors/aliases for repeated references.</summary>
    None = 0,

    /// <summary>Preserve object references using YAML anchors/aliases.</summary>
    Preserve = 1,

    /// <summary>
    /// Preserve object references using YAML anchors/aliases, emitting anchors only for objects that are referenced more than once.
    /// </summary>
    /// <remarks>This mode performs a pre-serialization pass to identify shared and cyclic references.</remarks>
    PreserveMinimal = 2,
}

