using System.Collections.ObjectModel;

namespace Meziantou.Framework.Scheduling;

/// <summary>The property parameters of a content line, in the form <see cref="InternetCalendarProperty.Parameters"/> describes, validated as they are added.</summary>
/// <remarks>
/// Validating on insertion, as <see cref="InternetCalendarProperty"/> does on creation, reports an invalid parameter where it
/// is added rather than when the calendar is written. A value is encoded when it is written, so a separator or a line feed in it
/// cannot start a property, or a component, of its own; only a control character that the encoding cannot represent is rejected.
/// </remarks>
internal sealed class InternetCalendarParameterCollection : Collection<KeyValuePair<string, string>>
{
    /// <summary>Adds the parameters of a parsed content line, which the parser decoded into values this collection accepts.</summary>
    internal void AddParsed(IEnumerable<KeyValuePair<string, string>> parameters, Func<string, bool>? skip = null)
    {
        foreach (var parameter in parameters)
        {
            if (skip is not null && skip(parameter.Key))
                continue;

            Add(parameter);
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
            throw new ArgumentException($"The value of the parameter '{item.Key}' contains a control character", nameof(item));
    }
}
