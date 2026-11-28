namespace Meziantou.Framework.Yaml;

/// <summary>The kind of a token read by <see cref="Utf8YamlReader"/>.</summary>
public enum Utf8YamlTokenKind : byte
{
    /// <summary>No token has been read yet.</summary>
    None,

    /// <summary>The UTF-8 byte order mark at the start of the stream.</summary>
    ByteOrderMark,

    /// <summary>A directive line, such as <c>%YAML 1.2</c> or <c>%TAG ! tag:example.com,2000:</c>.</summary>
    Directive,

    /// <summary>The start of a document: the <c>---</c> marker, or a zero-width token for an implicit start.</summary>
    DocumentStart,

    /// <summary>The end of a document: the <c>...</c> marker, or a zero-width token for an implicit end.</summary>
    DocumentEnd,

    /// <summary>A <c>...</c> marker that does not end a document, for example a second one.</summary>
    DocumentEndMarker,

    /// <summary>The start of a mapping: <c>{</c>, or a zero-width token for a block mapping or a single-pair flow mapping.</summary>
    MappingStart,

    /// <summary>The end of a mapping: <c>}</c>, or a zero-width token.</summary>
    MappingEnd,

    /// <summary>The start of a sequence: <c>[</c>, or a zero-width token for a block sequence.</summary>
    SequenceStart,

    /// <summary>The end of a sequence: <c>]</c>, or a zero-width token.</summary>
    SequenceEnd,

    /// <summary>A scalar. The token spans the quotes of a quoted scalar and the header of a block scalar.</summary>
    Scalar,

    /// <summary>An alias, such as <c>*name</c>.</summary>
    Alias,
}
