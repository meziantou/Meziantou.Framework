// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace Tomlyn.Model
{
    /// <summary>
    /// Kind of an TOML object.
    /// </summary>
#pragma warning disable CA1720 // The members are named after the TOML value types
    public enum ObjectKind
    {
        /// <summary>Inline table.</summary>
        InlineTable,

        /// <summary>Table.</summary>
        Table,

        /// <summary>Table array.</summary>
        TableArray,

        /// <summary>Array.</summary>
        Array,

        /// <summary>Boolean.</summary>
        Boolean,

        /// <summary>String.</summary>
        String,

        /// <summary>Integer.</summary>
        Integer,

        /// <summary>Float.</summary>
        Float,

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
#pragma warning restore CA1720
}