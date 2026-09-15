using System.Collections.ObjectModel;

namespace Meziantou.Framework.Scheduling;

/// <summary>The property parameters of a content line, each value as it is written, validated as they are added.</summary>
/// <remarks>
/// Validating on insertion, as <see cref="InternetCalendarProperty"/> does on creation, reports an invalid parameter where it
/// is added rather than when the calendar is written, and keeps every parameter writable: a line break or an unquoted
/// separator in a value would otherwise let it start a property, or a component, of its own.
/// </remarks>
internal sealed class InternetCalendarParameterCollection : Collection<KeyValuePair<string, string>>
{
    /// <summary>Adds the parameters of a parsed content line, which are well formed by construction and kept as written.</summary>
    internal void AddParsed(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        foreach (var parameter in parameters)
        {
            Items.Add(parameter);
        }
    }

    protected override void InsertItem(int index, KeyValuePair<string, string> item)
    {
        Validate(item);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, KeyValuePair<string, string> item)
    {
        Validate(item);
        base.SetItem(index, item);
    }

    private static void Validate(KeyValuePair<string, string> item)
    {
        if (!InternetCalendarProperty.IsValidName(item.Key))
            throw new ArgumentException($"'{item.Key}' is not a valid iCalendar parameter name", nameof(item));

        if (item.Value is null || !InternetCalendarProperty.IsValidParameterValue(item.Value))
            throw new ArgumentException($"The value of the parameter '{item.Key}' is neither a paramtext nor a quoted string", nameof(item));
    }
}
