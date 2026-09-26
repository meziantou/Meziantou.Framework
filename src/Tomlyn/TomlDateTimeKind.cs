// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

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