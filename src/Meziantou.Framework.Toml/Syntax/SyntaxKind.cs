namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// Defines the kind for a <see cref="SyntaxNode"/>
/// </summary>
#pragma warning disable CA1720 // The members are named after the TOML value types
public enum SyntaxKind
{
    /// <summary>Array value.</summary>
    Array,

    /// <summary>Array item.</summary>
    ArrayItem,

    /// <summary>Basic key.</summary>
    BasicKey,

    /// <summary>Boolean value.</summary>
    Boolean,

    /// <summary>Offset date-time with Z suffix.</summary>
    OffsetDateTimeByZ,

    /// <summary>Offset date-time with numeric offset.</summary>
    OffsetDateTimeByNumber,

    /// <summary>Local date-time value.</summary>
    LocalDateTime,

    /// <summary>Local date value.</summary>
    LocalDate,

    /// <summary>Local time value.</summary>
    LocalTime,

    /// <summary>Document root.</summary>
    Document,

    /// <summary>Dotted key item.</summary>
    DottedKeyItem,

    /// <summary>Float value.</summary>
    Float,

    /// <summary>Inline table.</summary>
    InlineTable,

    /// <summary>Integer value.</summary>
    Integer,

    /// <summary>Key node.</summary>
    Key,

    /// <summary>Key/value pair.</summary>
    KeyValue,

    /// <summary>Syntax list node.</summary>
    List,

    /// <summary>String value.</summary>
    String,

    /// <summary>Table.</summary>
    Table,

    /// <summary>Table array.</summary>
    TableArray,

    /// <summary>Token node.</summary>
    Token,
}
#pragma warning restore CA1720
