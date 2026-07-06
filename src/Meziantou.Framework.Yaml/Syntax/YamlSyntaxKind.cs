namespace Meziantou.Framework.Yaml.Syntax;

/// <summary>Identifies a syntax token kind.</summary>
public enum YamlSyntaxKind
{
    /// <summary>Unknown token.</summary>
    Unknown = 0,

    /// <summary>Whitespace trivia.</summary>
    WhitespaceTrivia,

    /// <summary>New line trivia.</summary>
    NewLineTrivia,

    /// <summary>Comment trivia.</summary>
    CommentTrivia,

    /// <summary>Stream start token.</summary>
    StreamStart,

    /// <summary>Stream end token.</summary>
    StreamEnd,

    /// <summary>Document start token.</summary>
    DocumentStart,

    /// <summary>Document end token.</summary>
    DocumentEnd,

    /// <summary>Block sequence start token.</summary>
    BlockSequenceStart,

    /// <summary>Block mapping start token.</summary>
    BlockMappingStart,

    /// <summary>Block end token.</summary>
    BlockEnd,

    /// <summary>Flow sequence start token.</summary>
    FlowSequenceStart,

    /// <summary>Flow sequence end token.</summary>
    FlowSequenceEnd,

    /// <summary>Flow mapping start token.</summary>
    FlowMappingStart,

    /// <summary>Flow mapping end token.</summary>
    FlowMappingEnd,

    /// <summary>Flow entry token.</summary>
    FlowEntry,

    /// <summary>Block entry token.</summary>
    BlockEntry,

    /// <summary>Key token.</summary>
    Key,

    /// <summary>Value token.</summary>
    Value,

    /// <summary>Scalar token.</summary>
    Scalar,

    /// <summary>Tag token.</summary>
    Tag,

    /// <summary>Anchor token.</summary>
    Anchor,

    /// <summary>Alias token.</summary>
    AnchorAlias,

    /// <summary>Version directive token.</summary>
    VersionDirective,

    /// <summary>Tag directive token.</summary>
    TagDirective,
}
