namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies how sequence values are emitted in Yamlish.</summary>
public enum YamlishSequenceStyle
{
    /// <summary>Automatically selects the sequence style.</summary>
    Auto,

    /// <summary>Emits the sequence using block style.</summary>
    Block,

    /// <summary>Emits the sequence using flow style.</summary>
    Flow,
}
