namespace Meziantou.Framework.Yaml;

/// <summary>The kind of a <see cref="YamlElement"/>.</summary>
public enum YamlElementKind
{
    /// <summary>A scalar.</summary>
    Scalar,

    /// <summary>A mapping.</summary>
    Mapping,

    /// <summary>A sequence.</summary>
    Sequence,

    /// <summary>An alias to an anchored node.</summary>
    Alias,
}
