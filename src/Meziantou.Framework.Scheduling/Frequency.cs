namespace Meziantou.Framework.Scheduling;

/// <summary>Specifies the frequency of a recurrence rule.</summary>
public enum Frequency
{
    /// <summary>No frequency specified.</summary>
    None,

    /// <summary>Recurrence occurs every second.</summary>
    Secondly,

    /// <summary>Recurrence occurs every minute.</summary>
    Minutely,

    /// <summary>Recurrence occurs every hour.</summary>
    Hourly,

    /// <summary>Recurrence occurs daily.</summary>
    Daily,

    /// <summary>Recurrence occurs weekly.</summary>
    Weekly,

    /// <summary>Recurrence occurs monthly.</summary>
    Monthly,

    /// <summary>Recurrence occurs yearly.</summary>
    Yearly,
}
