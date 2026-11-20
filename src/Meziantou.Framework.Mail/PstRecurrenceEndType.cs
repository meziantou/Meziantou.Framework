namespace Meziantou.Framework.Mail;

/// <summary>How a recurrence ends ([MS-OXOCAL] 2.2.1.44.1, EndType).</summary>
public enum PstRecurrenceEndType
{
    /// <summary>The recurrence ends on <see cref="PstRecurrencePattern.EndDate"/>.</summary>
    EndAfterDate = 0x2021,

    /// <summary>The recurrence ends after <see cref="PstRecurrencePattern.OccurrenceCount"/> occurrences.</summary>
    EndAfterOccurrences = 0x2022,

    /// <summary>The recurrence never ends.</summary>
    NeverEnd = 0x2023,
}
