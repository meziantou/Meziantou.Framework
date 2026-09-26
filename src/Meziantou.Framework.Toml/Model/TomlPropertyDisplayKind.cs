using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Model;

/// <summary>
/// Display hints for TOML property formatting.
/// </summary>
public enum TomlPropertyDisplayKind
{
    /// <summary>Default display.</summary>
    Default = 0,

    /// <summary>Offset date-time with Z suffix.</summary>
    OffsetDateTimeByZ,
    /// <summary>Offset date-time with numeric offset.</summary>
    OffsetDateTimeByNumber,
    /// <summary>Local date-time.</summary>
    LocalDateTime,
    /// <summary>Local date.</summary>
    LocalDate,
    /// <summary>Local time.</summary>
    LocalTime,

    /// <summary>Hexadecimal integer.</summary>
    IntegerHexadecimal,

    /// <summary>Octal integer.</summary>
    IntegerOctal,

    /// <summary>Binary integer.</summary>
    IntegerBinary,

    /// <summary>Multi-line string.</summary>
    StringMulti,

    /// <summary>Literal string.</summary>
    StringLiteral,

    /// <summary>Multi-line literal string.</summary>
    StringLiteralMulti,

    /// <summary>Inline table.</summary>
    InlineTable,
    /// <summary>Force non-inline table formatting.</summary>
    NoInline
}
