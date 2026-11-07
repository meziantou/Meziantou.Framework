namespace Meziantou.Framework.Scheduling;

/// <summary>Represents an event attendee, the ATTENDEE property defined in RFC 5545 section 3.8.4.1.</summary>
public sealed class Attendee
{
    /// <summary>Gets or sets the calendar user address of the attendee.</summary>
    public InternetCalendarUserAddress? Address { get; set; }

    /// <summary>Gets the parameters of the ATTENDEE property, such as CN, ROLE, PARTSTAT or RSVP.</summary>
    /// <remarks>
    /// <para>As for <see cref="InternetCalendarProperty.Parameters"/>, a param-value list is kept in its written form, including the
    /// DQUOTE characters of a quoted value, as in <c>new KeyValuePair&lt;string, string&gt;("CN", "\"Doe, Jane\"")</c>, and any other
    /// value, such as <c>Jane "JD" Doe</c>, is written as a quoted-string. The RFC 6868 encoding is decoded when read and applied
    /// when written. The parser fills the collection in order, and the parameters are written back in that order.</para>
    /// <para>A parameter is validated when it is added: adding one whose name is not made of ASCII letters, digits and dashes, or
    /// whose value is <see langword="null"/> or contains a control character other than a horizontal tab or a line feed, throws an
    /// <see cref="ArgumentException"/>.</para>
    /// </remarks>
    public IList<KeyValuePair<string, string>> Parameters { get; } = new InternetCalendarParameterCollection();
}
