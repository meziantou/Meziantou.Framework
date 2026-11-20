namespace Meziantou.Framework.Mail;

/// <summary>Days of the week, as a set of flags.</summary>
[Flags]
public enum PstDaysOfWeek
{
    /// <summary>No day.</summary>
    None = 0,

    /// <summary>Sunday.</summary>
    Sunday = 0x01,

    /// <summary>Monday.</summary>
    Monday = 0x02,

    /// <summary>Tuesday.</summary>
    Tuesday = 0x04,

    /// <summary>Wednesday.</summary>
    Wednesday = 0x08,

    /// <summary>Thursday.</summary>
    Thursday = 0x10,

    /// <summary>Friday.</summary>
    Friday = 0x20,

    /// <summary>Saturday.</summary>
    Saturday = 0x40,
}
