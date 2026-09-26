namespace Tomlyn;

/// <summary>
/// Offsets used for a <see cref="TomlDateTime"/>
/// </summary>
public enum TomlDateTimeKind
{
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
}