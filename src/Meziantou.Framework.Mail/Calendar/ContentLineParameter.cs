namespace Meziantou.Framework.Mail;

/// <summary>A parameter of a content line of a vCard or of an iCalendar object, such as <c>TYPE=work,voice</c> or <c>TZID=Europe/Paris</c>.</summary>
public sealed class ContentLineParameter
{
    /// <summary>Creates a parameter.</summary>
    /// <param name="name">The name of the parameter, or <see langword="null"/> for a parameter of vCard 2.1 without a name (<c>TEL;WORK;VOICE:</c>).</param>
    /// <param name="values">The values of the parameter.</param>
    public ContentLineParameter(string? name, params IEnumerable<string> values)
    {
        Name = name;
        Values = [.. values];
    }

    /// <summary>Gets the name of the parameter, or <see langword="null"/> for a parameter of vCard 2.1 without a name.</summary>
    public string? Name { get; }

    /// <summary>Gets the values of the parameter, unquoted.</summary>
    public IList<string> Values { get; }

    /// <summary>Returns the parameter as it is written in a content line.</summary>
    /// <returns>The parameter.</returns>
    public override string ToString() => Name is null ? string.Join(",", Values) : Name + "=" + string.Join(",", Values);
}
