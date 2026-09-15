namespace Meziantou.Framework.Scheduling;

/// <summary>Represents an event organizer, the ORGANIZER property defined in RFC 5545 section 3.8.4.3.</summary>
public sealed class Organizer
{
    /// <summary>Gets or sets the calendar user address of the organizer.</summary>
    public InternetCalendarUserAddress? Address { get; set; }

    /// <summary>Gets the parameters of the ORGANIZER property, such as CN or SENT-BY, each value as it is written in the content line.</summary>
    /// <remarks>
    /// <para>As for <see cref="InternetCalendarProperty.Parameters"/>, a value is kept as written, including the DQUOTE characters
    /// of a quoted value, as in <c>new KeyValuePair&lt;string, string&gt;("SENT-BY", "\"mailto:assistant@example.com\"")</c>. The
    /// parser fills the collection in order, and the parameters are written back in that order.</para>
    /// <para>A parameter is validated when it is added: adding one whose name is not made of ASCII letters, digits and dashes, or
    /// whose value is neither a paramtext nor a quoted string, throws an <see cref="ArgumentException"/>.</para>
    /// </remarks>
    public IList<KeyValuePair<string, string>> Parameters { get; } = new InternetCalendarParameterCollection();
}
