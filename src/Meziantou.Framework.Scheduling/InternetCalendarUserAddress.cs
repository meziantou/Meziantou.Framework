namespace Meziantou.Framework.Scheduling;

/// <summary>Represents a calendar user address as defined in RFC 2445.</summary>
public sealed class InternetCalendarUserAddress
{
    // RFC2445 - 4.3.3 Calendar User Address

    /// <summary>Gets the URI of the calendar user.</summary>
    public Uri Uri { get; }

    /// <summary>Initializes a new instance of the <see cref="InternetCalendarUserAddress"/> class with the specified email address.</summary>
    /// <param name="email">The email address of the calendar user.</param>
    public InternetCalendarUserAddress(string email)
    {
        Uri = new Uri("mailto:" + email);
    }

    public override string ToString()
    {
        return Uri.ToString();
    }
}
