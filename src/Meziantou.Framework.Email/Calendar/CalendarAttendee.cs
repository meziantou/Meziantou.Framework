namespace Meziantou.Framework.Email.Calendar;

/// <summary>An attendee or the organizer of an event (<c>ATTENDEE</c>, <c>ORGANIZER</c>).</summary>
/// <param name="Address">The address, without its <c>mailto:</c> prefix.</param>
/// <param name="Name">The name (<c>CN</c>), or <see langword="null"/>.</param>
/// <param name="Role">The role (<c>ROLE</c>), such as <c>REQ-PARTICIPANT</c> or <c>OPT-PARTICIPANT</c>, or <see langword="null"/>.</param>
/// <param name="ParticipationStatus">The participation status (<c>PARTSTAT</c>), such as <c>ACCEPTED</c>, or <see langword="null"/>.</param>
public sealed record CalendarAttendee(string Address, string? Name = null, string? Role = null, string? ParticipationStatus = null)
{
    internal static CalendarAttendee FromLine(ContentLine line)
    {
        var value = line.Value.Trim();
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..];
        }

        return new CalendarAttendee(value, line.GetParameter("CN"), line.GetParameter("ROLE"), line.GetParameter("PARTSTAT"));
    }

    internal ContentLine ToLine(string name)
    {
        var line = new ContentLine(name, Address.Contains(':', StringComparison.Ordinal) ? Address : "mailto:" + Address);
        line.SetParameter("CN", Name);
        line.SetParameter("ROLE", Role);
        line.SetParameter("PARTSTAT", ParticipationStatus);
        return line;
    }
}
