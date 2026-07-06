namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Describes the current YAML token being read.</summary>
public enum YamlTokenType
{
    /// <summary>No token is available.</summary>
    None = 0,

    /// <summary>Start of a YAML stream.</summary>
    StreamStart = 1,

    /// <summary>End of a YAML stream.</summary>
    StreamEnd = 2,

    /// <summary>Start of a YAML document.</summary>
    DocumentStart = 3,

    /// <summary>End of a YAML document.</summary>
    DocumentEnd = 4,

    /// <summary>Start of a mapping (object).</summary>
    StartMapping = 5,

    /// <summary>End of a mapping (object).</summary>
    EndMapping = 6,

    /// <summary>Start of a sequence (array).</summary>
    StartSequence = 7,

    /// <summary>End of a sequence (array).</summary>
    EndSequence = 8,

    /// <summary>A scalar value.</summary>
    Scalar = 9,

    /// <summary>An alias (reference to an anchor).</summary>
    Alias = 10,
}

