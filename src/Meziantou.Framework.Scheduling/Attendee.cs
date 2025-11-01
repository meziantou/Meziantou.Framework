namespace Meziantou.Framework.Scheduling;

/// <summary>Represents an event attendee as defined in RFC 2445.</summary>
public sealed class Attendee
{
    //RFC2445 - 4.8.4.1 Attendee

    /// <summary>Gets or sets the calendar user address of the attendee.</summary>
    public CalendarUserAddress? Address { get; set; }
}
