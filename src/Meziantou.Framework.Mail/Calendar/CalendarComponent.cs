using System.Globalization;
using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A component of an iCalendar object (<c>BEGIN:NAME</c> ... <c>END:NAME</c>): its properties, in their order, and its components. The typed components (<see cref="VEvent"/>, <see cref="VTodo"/>, <see cref="VTimeZone"/>) read and write their properties through it, so that a component written back keeps the properties they do not know.</summary>
public class CalendarComponent
{
    /// <summary>Creates a component.</summary>
    /// <param name="name">The name of the component, such as <c>VEVENT</c>, <c>VALARM</c> or <c>X-CUSTOM</c>.</param>
    public CalendarComponent(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name.ToUpperInvariant();
    }

    /// <summary>Gets the name of the component.</summary>
    public string Name { get; }

    /// <summary>Gets the properties.</summary>
    public IList<ContentLine> Properties { get; } = [];

    /// <summary>Gets the components, such as the events of a calendar or the alarms of an event.</summary>
    public IList<CalendarComponent> Components { get; } = [];

    /// <summary>Gets the calendar the component is in, when it was read from one or added to one.</summary>
    public VCalendar? Calendar { get; internal set; }

    /// <summary>Returns the first property of a name.</summary>
    /// <param name="name">The name, without case.</param>
    /// <returns>The property, or <see langword="null"/>.</returns>
    public ContentLine? GetProperty(string name)
    {
        foreach (var property in Properties)
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>Returns the properties of a name.</summary>
    /// <param name="name">The name, without case.</param>
    /// <returns>The properties.</returns>
    public IEnumerable<ContentLine> GetProperties(string name) => Properties.Where(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the text of a property, decoded.</summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public string? GetText(string name) => GetProperty(name)?.GetText();

    /// <summary>Sets the text of a property, or removes the properties of this name when the value is <see langword="null"/>.</summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The text.</param>
    public void SetText(string name, string? value) => SetValue(name, value is null ? null : ContentLine.EncodeText(value));

    /// <summary>Sets the value of a property, as it is written, or removes the properties of this name when the value is <see langword="null"/>.</summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The value.</param>
    public void SetValue(string name, string? value)
    {
        var property = GetProperty(name);
        if (value is null)
        {
            RemoveProperties(name);
        }
        else if (property is null)
        {
            Properties.Add(new ContentLine(name, value));
        }
        else
        {
            property.Value = value;
        }
    }

    /// <summary>Removes the properties of a name.</summary>
    /// <param name="name">The name, without case.</param>
    public void RemoveProperties(string name)
    {
        for (var i = Properties.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Properties[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Properties.RemoveAt(i);
            }
        }
    }

    /// <summary>Returns the date or the date and time of a property.</summary>
    /// <param name="name">The name of the property, such as <c>DTSTART</c>.</param>
    /// <returns>The value, or <see langword="null"/> when the component does not have the property or its value is not a date.</returns>
    public CalendarDateTime? GetDateTime(string name) => GetProperty(name) is { } property ? CalendarDateTime.Parse(property) : null;

    /// <summary>Sets the date or the date and time of a property, or removes it.</summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="value">The value.</param>
    public void SetDateTime(string name, CalendarDateTime? value)
    {
        if (value is not { } dateTime)
        {
            RemoveProperties(name);
            return;
        }

        var property = GetProperty(name);
        if (property is null)
        {
            property = new ContentLine(name, "");
            Properties.Add(property);
        }

        dateTime.Apply(property);
    }

    internal int? GetInteger(string name) => int.TryParse(GetProperty(name)?.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    internal void SetInteger(string name, int? value) => SetValue(name, value?.ToString(CultureInfo.InvariantCulture));

    internal IEnumerable<T> GetComponents<T>()
        where T : CalendarComponent => Components.OfType<T>();

    internal void Write(StringBuilder output)
    {
        output.Append("BEGIN:").Append(Name).Append("\r\n");
        foreach (var property in Properties)
        {
            ContentLineWriter.Write(output, property, vcard21: false);
        }

        foreach (var component in Components)
        {
            component.Write(output);
        }

        output.Append("END:").Append(Name).Append("\r\n");
    }

    internal static CalendarComponent Create(string name) => name.ToUpperInvariant() switch
    {
        "VCALENDAR" => new VCalendar(addDefaults: false),
        "VEVENT" => new VEvent(),
        "VTODO" => new VTodo(),
        "VTIMEZONE" => new VTimeZone(),
        _ => new CalendarComponent(name),
    };

    /// <summary>Returns the component as it is written, with CRLF line breaks.</summary>
    /// <returns>The component.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        Write(builder);
        return builder.ToString();
    }
}
