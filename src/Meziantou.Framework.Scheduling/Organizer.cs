namespace Meziantou.Framework.Scheduling;

/// <summary>Represents an event organizer as defined in RFC 2445.</summary>
public sealed class Organizer
{
    //RFC2445 - 4.8.4.3 Organizer

    /// <summary>Gets or sets the calendar user address of the organizer.</summary>
    public CalendarUserAddress? Address { get; set; }
}
