namespace Meziantou.Framework.Scheduling;

/// <summary>An iCalendar content line (RFC 5545 section 3.1): a property name, its parameters and its value.</summary>
internal sealed class ContentLine
{
    private readonly List<KeyValuePair<string, string>>? _parameters;

    private ContentLine(string name, List<KeyValuePair<string, string>>? parameters, string value)
    {
        Name = name;
        _parameters = parameters;
        Value = value;
    }

    /// <summary>The property name, as written. iCalendar names are case-insensitive.</summary>
    public string Name { get; }

    /// <summary>The value as written, before the decoding its value type calls for.</summary>
    public string Value { get; }

    /// <summary>Gets the value of a property parameter, or <see langword="null"/> when the line does not carry it.</summary>
    public string? GetParameter(string name)
    {
        if (_parameters is null)
            return null;

        foreach (var parameter in _parameters)
        {
            if (string.Equals(parameter.Key, name, StringComparison.OrdinalIgnoreCase))
                return parameter.Value;
        }

        return null;
    }

    /// <summary>Gets the value decoded as an iCalendar TEXT value (RFC 5545 section 3.3.11).</summary>
    public string GetTextValue()
    {
        var value = Value;
        if (value.IndexOf('\\', StringComparison.Ordinal) < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is not '\\' || i + 1 >= value.Length)
            {
                sb.Append(value[i]);
                continue;
            }

            i++;
            sb.Append(value[i] switch
            {
                'n' or 'N' => '\n',

                // Covers the "\\", "\;" and "\," escapes, and leniently passes through anything else
                // a producer escaped: dropping the backslash is closer to the intent than keeping it.
                var escaped => escaped,
            });
        }

        return sb.ToString();
    }

    /// <summary>Parses an unfolded content line.</summary>
    public static bool TryParse(string line, [NotNullWhen(returnValue: true)] out ContentLine? contentLine, out string? error)
    {
        contentLine = null;

        var index = 0;
        if (!TryReadName(line, ref index, out var name))
        {
            error = $"'{line}' does not start with a property name";
            return false;
        }

        List<KeyValuePair<string, string>>? parameters = null;
        while (index < line.Length && line[index] is ';')
        {
            index++;
            if (!TryReadName(line, ref index, out var parameterName))
            {
                error = $"'{line}' contains a property parameter without a name";
                return false;
            }

            if (index >= line.Length || line[index] is not '=')
            {
                error = $"The property parameter '{parameterName}' of '{line}' has no value";
                return false;
            }

            index++;
            if (!TryReadParameterValue(line, ref index, out var parameterValue))
            {
                error = $"The property parameter '{parameterName}' of '{line}' has an unterminated quoted value";
                return false;
            }

            parameters ??= [];
            parameters.Add(new KeyValuePair<string, string>(parameterName, parameterValue));
        }

        if (index >= line.Length || line[index] is not ':')
        {
            error = $"'{line}' has no value";
            return false;
        }

        contentLine = new ContentLine(name, parameters, line[(index + 1)..]);
        error = null;
        return true;
    }

    /// <summary>Reads a name (RFC 5545 section 3.1: ALPHA / DIGIT / "-").</summary>
    private static bool TryReadName(string line, ref int index, [NotNullWhen(returnValue: true)] out string? name)
    {
        var start = index;
        while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] is '-'))
        {
            index++;
        }

        if (index == start)
        {
            name = null;
            return false;
        }

        name = line[start..index];
        return true;
    }

    /// <summary>Reads a param-value list (RFC 5545 section 3.2), which may contain quoted values holding a colon.</summary>
    private static bool TryReadParameterValue(string line, ref int index, [NotNullWhen(returnValue: true)] out string? value)
    {
        var sb = new StringBuilder();
        while (index < line.Length)
        {
            var c = line[index];
            if (c is '"')
            {
                index++;
                var start = index;
                while (index < line.Length && line[index] is not '"')
                {
                    index++;
                }

                if (index >= line.Length)
                {
                    value = null;
                    return false;
                }

                sb.Append(line, start, index - start);
                index++;
            }
            else if (c is ';' or ':')
            {
                // Only an unquoted separator ends the parameter.
                break;
            }
            else
            {
                sb.Append(c);
                index++;
            }
        }

        value = sb.ToString();
        return true;
    }
}
