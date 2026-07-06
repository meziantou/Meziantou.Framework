namespace Meziantou.Framework.Yaml;

/// <summary>Specifies the YAML schema used for scalar resolution.</summary>
public enum YamlSchemaKind
{
    /// <summary>YAML 1.2 Core schema.</summary>
    Core = 0,

    /// <summary>YAML 1.2 JSON schema.</summary>
    Json = 1,

    /// <summary>YAML 1.2 Failsafe schema.</summary>
    Failsafe = 2,

    /// <summary>Extended schema with additional scalar rules.</summary>
    Extended = 3,
}

