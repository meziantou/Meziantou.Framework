namespace Meziantou.Framework.Yaml;

/// <summary>Defines behavior when an unknown derived type discriminator is encountered.</summary>
public enum YamlUnknownDerivedTypeHandling
{
    /// <summary>Use the serializer options default.</summary>
    Unspecified = -1,

    /// <summary>Throw when an unknown discriminator is encountered.</summary>
    Fail = 0,

    /// <summary>Fall back to the configured base type.</summary>
    FallBackToBase = 1,
}

