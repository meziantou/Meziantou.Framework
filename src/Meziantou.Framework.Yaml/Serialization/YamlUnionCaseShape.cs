namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Describes the YAML shape a union case is represented by.</summary>
public enum YamlUnionCaseShape
{
    /// <summary>The case is represented by a boolean scalar.</summary>
    Boolean,

    /// <summary>The case is represented by a numeric scalar.</summary>
    Number,

    /// <summary>The case is represented by a text scalar.</summary>
    Text,

    /// <summary>The case is represented by a sequence.</summary>
    Sequence,

    /// <summary>The case is represented by a mapping.</summary>
    Mapping,

    /// <summary>The case can be represented by any YAML shape.</summary>
    Any,
}
