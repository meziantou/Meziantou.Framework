namespace Meziantou.Framework.Toml;

/// <summary>Determines how unmapped TOML members are handled during object deserialization.</summary>
public enum TomlUnmappedMemberHandling
{
    /// <summary>Unmapped members are ignored.</summary>
    Skip = 0,

    /// <summary>
    /// Encountering an unmapped member causes a <see cref="TomlException"/> to be thrown.
    /// </summary>
    Disallow = 1,
}
