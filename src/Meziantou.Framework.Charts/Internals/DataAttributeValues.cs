using System.Globalization;

namespace Meziantou.Framework.Charts;

/// <summary>Validates the names and formats the values of the <c>data-*</c> attributes of the SVG output.</summary>
internal static class DataAttributeValues
{
    /// <summary>Throws when a name is not lowercase ASCII letters, digits, hyphens, underscores and dots.</summary>
    public static void ThrowIfInvalidName(string name, string paramName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, paramName);
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_' or '.'))
                throw new ArgumentException($"The data attribute name '{name}' is not valid: use lowercase ASCII letters, digits, hyphens, underscores and dots, without the 'data-' prefix.", paramName);
        }
    }

    /// <summary>Formats a value with the invariant culture: numbers round-trip, times use ISO 8601, and missing values return <see langword="null"/>.</summary>
    public static string? Format(object? value)
    {
        return value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
            _ => ChannelValues.ToCategory(value),
        };
    }

    /// <summary>Validates and formats the attributes of a row. Entries whose value is missing are skipped; the last entry of a name wins.</summary>
    /// <exception cref="ArgumentException">A name is not valid. The parameter is the <c>DataAttributes</c> channel of the mark.</exception>
    public static KeyValuePair<string, string>[]? Evaluate(IEnumerable<KeyValuePair<string, object?>>? attributes)
    {
        if (attributes is null)
            return null;

        var result = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in attributes)
        {
            ThrowIfInvalidName(name, nameof(Mark<object>.DataAttributes));
            result.RemoveAll(item => string.Equals(item.Key, name, StringComparison.Ordinal));
            if (Format(value) is { } text)
            {
                result.Add(new(name, text));
            }
        }

        return result.Count is 0 ? null : [.. result];
    }

    /// <summary>Copies the attributes of a row to an element of the scene.</summary>
    public static void Apply(SceneElement element, KeyValuePair<string, string>[]?[]? attributes, int index)
    {
        if (attributes is null || index >= attributes.Length || attributes[index] is not { } row)
            return;

        foreach (var (name, value) in row)
        {
            element.SetDataAttribute(name, value);
        }
    }
}
