namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies how scalar values are emitted in Yamlish.</summary>
public enum YamlishScalarStyle
{
    /// <summary>Automatically selects the scalar style.</summary>
    Auto,

    /// <summary>Emits the scalar as a plain value.</summary>
    Plain,

    /// <summary>Emits the scalar as a double-quoted value (<c>"</c>).</summary>
    DoubleQuoted,

    /// <summary>Emits the scalar as a single-quoted value (<c>'</c>).</summary>
    SingleQuoted,

    /// <summary>Emits the scalar using the literal block style (<c>|</c>).</summary>
    Literal,

    /// <summary>Emits the scalar using the folded block style (<c>&gt;</c>).</summary>
    Folded,
}
