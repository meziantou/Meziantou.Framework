namespace Meziantou.Framework.HumanReadable.ValueFormatters;

internal sealed class UrlEncodedFormFormatter : ValueFormatter
{
    private readonly UrlEncodedFormFormatterOptions _options;

    public UrlEncodedFormFormatter(UrlEncodedFormFormatterOptions options)
    {
        _options = options;
    }

    public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (!_options.PrettyFormat && !_options.OrderProperties)
        {
            writer.WriteValue(value);
            return;
        }

        var items = ParseValue(value);
        if (items.Count == 0)
        {
            writer.WriteValue(value);
            return;
        }

        if (_options.UnescapeValues)
        {
            items = items.Select(item => (Unescape(item.Key), Unescape(item.Value))).ToList();

        }

        if (_options.OrderProperties)
        {
            items = items.OrderBy(item => item.Key, StringComparer.Ordinal).ThenBy(item => item.Value, StringComparer.Ordinal).ToList();
        }

        if (!_options.PrettyFormat)
        {
            writer.WriteValue(string.Join("&", items.Select(item => $"{item.Key}={item.Value}")));
            return;
        }

        writer.StartObject();
        foreach (var (key, itemValue) in items)
        {
            writer.WritePropertyName(key);
            writer.WriteValue(itemValue);
        }

        writer.EndObject();
    }

    private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private static List<(string Key, string Value)> ParseValue(string urlEncodedValue)
    {
        var result = new List<(string Key, string Value)>();

        var scanIndex = 0;
        var textLength = urlEncodedValue.Length;
        var equalIndex = urlEncodedValue.IndexOf('=', StringComparison.Ordinal);
        if (equalIndex == -1)
        {
            equalIndex = textLength;
        }

        while (scanIndex < textLength)
        {
            var delimiterIndex = urlEncodedValue.IndexOf('&', scanIndex);
            if (delimiterIndex == -1)
            {
                delimiterIndex = textLength;
            }

            if (equalIndex < delimiterIndex)
            {
                while (scanIndex != equalIndex && char.IsWhiteSpace(urlEncodedValue[scanIndex]))
                {
                    ++scanIndex;
                }

                var name = urlEncodedValue[scanIndex..equalIndex];
                var value = urlEncodedValue.Substring(equalIndex + 1, delimiterIndex - equalIndex - 1);
                result.Add((name, value));
                equalIndex = urlEncodedValue.IndexOf('=', delimiterIndex);
                if (equalIndex == -1)
                {
                    equalIndex = textLength;
                }
            }
            else
            {
                if (delimiterIndex > scanIndex)
                {
                    result.Add((urlEncodedValue[scanIndex..delimiterIndex], ""));
                }
            }

            scanIndex = delimiterIndex + 1;
        }

        return result;
    }
}
