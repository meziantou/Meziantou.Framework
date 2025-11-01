namespace Meziantou.Framework.Scheduling;

/// <summary>Specifies the status of a calendar event.</summary>
public enum EventStatus
{
    /// <summary>The event is tentative.</summary>
    Tentative,

    /// <summary>The event is confirmed.</summary>
    Confirmed,

    /// <summary>The event is cancelled.</summary>
    Cancelled,
}
