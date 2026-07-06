namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Specifies the kind of a Yamlish scalar node.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Names match Yamlish scalar kinds.")]
public enum YamlishScalarKind
{
    /// <summary>The scalar kind is unknown.</summary>
    Unknown,

    /// <summary>A null scalar.</summary>
    Null,

    /// <summary>A string scalar.</summary>
    String,
}
