namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies when a member is ignored during Yamlish serialization.</summary>
public enum YamlishIgnoreCondition
{
    /// <summary>The member is never ignored.</summary>
    Never,

    /// <summary>The member is always ignored.</summary>
    Always,

    /// <summary>The member is ignored when its value is the default value for its type.</summary>
    WhenWritingDefault,

    /// <summary>The member is ignored when its value is <see langword="null" />.</summary>
    WhenWritingNull,
}
