namespace Meziantou.Framework.Scheduling;

/// <summary>Represents an event organizer, the ORGANIZER property defined in RFC 5545 section 3.8.4.3.</summary>
public sealed class Organizer
{
    /// <summary>Gets or sets the calendar user address of the organizer.</summary>
    public InternetCalendarUserAddress? Address { get; set; }

    /// <summary>Gets the parameters of the ORGANIZER property, such as CN or SENT-BY.</summary>
    /// <remarks>
    /// <para>As for <see cref="InternetCalendarProperty.Parameters"/>, a param-value list is kept in its written form, including the
    /// DQUOTE characters of a quoted value, as in <c>new KeyValuePair&lt;string, string&gt;("SENT-BY", "\"mailto:assistant@example.com\"")</c>,
    /// and any other value is written as a quoted-string. The RFC 6868 encoding is decoded when read and applied when written. The
    /// parser fills the collection in order, and the parameters are written back in that order.</para>
    /// <para>A parameter is validated when it is added: adding one whose name is not made of ASCII letters, digits and dashes, or
    /// whose value is <see langword="null"/> or contains a control character other than a horizontal tab or a line feed, throws an
    /// <see cref="ArgumentException"/>.</para>
    /// </remarks>
    public IList<KeyValuePair<string, string>> Parameters { get; } = new InternetCalendarParameterCollection();
}
