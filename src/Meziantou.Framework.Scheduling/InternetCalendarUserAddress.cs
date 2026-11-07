namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a calendar user address as defined in RFC 5545 section 3.3.3.</summary>
public sealed class InternetCalendarUserAddress
{
    /// <summary>Gets the URI of the calendar user.</summary>
    public Uri Uri { get; }

    /// <summary>Initializes a new instance of the <see cref="InternetCalendarUserAddress"/> class with the specified email address.</summary>
    /// <param name="email">The email address of the calendar user.</param>
    public InternetCalendarUserAddress(string email)
    {
        Uri = new Uri("mailto:" + email);
    }

    /// <summary>Initializes a new instance of the <see cref="InternetCalendarUserAddress"/> class with the specified URI.</summary>
    /// <param name="uri">The absolute URI of the calendar user, such as a mailto URI.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is a relative URI.</exception>
    public InternetCalendarUserAddress(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // RFC 5545 section 3.3.3: a CAL-ADDRESS is a URI, which has a scheme, so a relative reference cannot be written as one.
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("A calendar user address must be an absolute URI", nameof(uri));

        Uri = uri;
    }

    public override string ToString()
    {
        return Uri.ToString();
    }

    /// <summary>Gets the value written in an ORGANIZER or an ATTENDEE content line.</summary>
    /// <remarks>
    /// <see cref="Uri.ToString()"/> is the unescaped form, which keeps a line break or another control character of the
    /// address as is, so the written value would start a property, or a component, of its own. The escaped form round-trips
    /// through the parser instead. A <see cref="Uri"/> created without escaping can still hold a control character in that
    /// form, so any one left is percent-encoded as well.
    /// </remarks>
    internal string GetContentLineValue()
    {
        var value = Uri.AbsoluteUri;
        if (!value.Any(IsControl))
            return value;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if (IsControl(c))
            {
                sb.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();

        static bool IsControl(char c) => c < 0x20 || c == 0x7F;
    }
}
