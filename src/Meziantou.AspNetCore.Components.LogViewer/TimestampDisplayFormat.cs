namespace Meziantou.AspNetCore.Components;

/// <summary>Defines how timestamps are displayed in the log viewer.</summary>
public enum TimestampDisplayFormat
{
    /// <summary>Timestamps are not displayed.</summary>
    Hidden,

    /// <summary>Displays the full date and time for each log entry.</summary>
    FullDateTime,

    /// <summary>Displays the full date and time for the first entry, then shows relative time (elapsed time since the first entry) for subsequent entries.</summary>
    DateTimeThenRelativeTime,

    /// <summary>Displays relative time starting at zero for the first entry, showing elapsed time since the first entry for all entries.</summary>
    RelativeTimeStartingAtZero,
}
