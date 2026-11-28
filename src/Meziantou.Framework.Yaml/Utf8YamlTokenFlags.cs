namespace Meziantou.Framework.Yaml;

/// <summary>Describes the token read by <see cref="Utf8YamlReader"/>.</summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "The style values share the low bits of the flags")]
public enum Utf8YamlTokenFlags : ushort
{
    None = 0,

    /// <summary>A plain scalar. Same value as <see cref="ScalarStyle.Plain"/>.</summary>
    Plain = 1,

    /// <summary>A single-quoted scalar. Same value as <see cref="ScalarStyle.SingleQuoted"/>.</summary>
    SingleQuoted = 2,

    /// <summary>A double-quoted scalar. Same value as <see cref="ScalarStyle.DoubleQuoted"/>.</summary>
    DoubleQuoted = 3,

    /// <summary>A literal block scalar. Same value as <see cref="ScalarStyle.Literal"/>.</summary>
    Literal = 4,

    /// <summary>A folded block scalar. Same value as <see cref="ScalarStyle.Folded"/>.</summary>
    Folded = 5,

    /// <summary>The bits that hold the scalar style.</summary>
    ScalarStyleMask = 7,

    /// <summary>The collection is a flow collection.</summary>
    Flow = 1 << 3,

    /// <summary>The document start or end has no marker.</summary>
    Implicit = 1 << 4,

    /// <summary>The value of the scalar is exactly the bytes of its content: there is nothing to unescape or fold.</summary>
    RawValue = 1 << 5,

    /// <summary>The block scalar keeps its final line breaks (<c>+</c>).</summary>
    KeepChomping = 1 << 6,

    /// <summary>The block scalar strips its final line breaks (<c>-</c>).</summary>
    StripChomping = 1 << 7,
}
