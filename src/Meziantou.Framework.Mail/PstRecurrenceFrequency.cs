namespace Meziantou.Framework.Mail;

/// <summary>How often a recurrence repeats ([MS-OXOCAL] 2.2.1.44.1, RecurFrequency).</summary>
public enum PstRecurrenceFrequency
{
    /// <summary>Every day or every N days.</summary>
    Daily = 0x200A,

    /// <summary>Every week or every N weeks.</summary>
    Weekly = 0x200B,

    /// <summary>Every month or every N months.</summary>
    Monthly = 0x200C,

    /// <summary>Every year or every N years.</summary>
    Yearly = 0x200D,
}
