namespace Meziantou.Framework.Yaml;

/// <summary>Specifies when a member should be ignored during YAML serialization.</summary>
public enum YamlIgnoreCondition
{
    /// <summary>Never ignore a member because of default/null value.</summary>
    Never = 0,

    /// <summary>
    /// Ignore a member when writing and its value is <see langword="null"/>.
    /// </summary>
    WhenWritingNull = 1,

    /// <summary>Ignore a member when writing and its value is the type default.</summary>
    WhenWritingDefault = 2,
}

