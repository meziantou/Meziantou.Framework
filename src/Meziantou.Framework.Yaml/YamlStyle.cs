namespace Meziantou.Framework.Yaml;

/// <summary>Specifies the style of a sequence or mapping.</summary>
public enum YamlStyle
{
    /// <summary>Let the emitter choose the style.</summary>
    Any,

    /// <summary>The block style.</summary>
    Block,

    /// <summary>The flow style.</summary>
    Flow
}