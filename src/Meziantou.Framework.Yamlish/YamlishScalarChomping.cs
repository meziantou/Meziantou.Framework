namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies how trailing line breaks are handled for block scalar values.</summary>
public enum YamlishScalarChomping
{
    /// <summary>Keeps one trailing line break.</summary>
    Clip,

    /// <summary>Removes trailing line breaks.</summary>
    Strip,

    /// <summary>Keeps all trailing line breaks.</summary>
    Keep,
}
