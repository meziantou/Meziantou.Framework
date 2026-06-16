namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Specifies the kind of a Yamlish node.</summary>
public enum YamlishNodeKind
{
    /// <summary>A scalar node.</summary>
    Scalar,

    /// <summary>A mapping node.</summary>
    Mapping,

    /// <summary>A sequence node.</summary>
    Sequence,
}
