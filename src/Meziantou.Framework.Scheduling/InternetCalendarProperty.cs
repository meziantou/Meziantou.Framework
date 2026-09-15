namespace Meziantou.Framework.Scheduling;

/// <summary>An iCalendar property kept verbatim: its name, its parameters and its value exactly as they are written in a content line (RFC 5545 section 3.1).</summary>
/// <remarks>
/// <para>Unlike the TEXT values of <see cref="InternetCalendar.AdditionalProperties"/> and <see cref="Event.AdditionalProperties"/>,
/// the value is neither escaped when written nor unescaped when read, so structured values such as
/// <c>GEO:37.386013;-122.082932</c>, <c>CATEGORIES:WORK,MEETING</c> or <c>EXDATE;TZID=Europe/Paris:20240104T100000</c> round-trip unchanged.</para>
/// <para>A parameter value is also kept as written, including the DQUOTE characters of a quoted value, as in
/// <c>new KeyValuePair&lt;string, string&gt;("ALTREP", "\"cid:part1@example.org\"")</c>.</para>
/// </remarks>
public sealed class InternetCalendarProperty
{
    /// <summary>Initializes a new instance of the <see cref="InternetCalendarProperty"/> class without parameters.</summary>
    /// <param name="name">The property name, made of ASCII letters, digits and dashes.</param>
    /// <param name="value">The value as it is written in the content line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a property name, or <paramref name="value"/> contains a control character.</exception>
    public InternetCalendarProperty(string name, string value)
        : this(name, [], value)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InternetCalendarProperty"/> class.</summary>
    /// <param name="name">The property name, made of ASCII letters, digits and dashes.</param>
    /// <param name="parameters">The property parameters, each value as it is written in the content line.</param>
    /// <param name="value">The value as it is written in the content line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="parameters"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The name of the property or of a parameter is not a valid name, a parameter value is neither a
    /// paramtext nor a quoted string, or <paramref name="value"/> contains a control character.</exception>
    public InternetCalendarProperty(string name, IEnumerable<KeyValuePair<string, string>> parameters, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(value);

        // Validating here keeps every instance writable: a line break or an unquoted separator would
        // otherwise let the value start a property, or a component, of its own.
        if (!IsValidName(name))
            throw new ArgumentException($"'{name}' is not a valid iCalendar property name", nameof(name));

        var list = new List<KeyValuePair<string, string>>();
        foreach (var parameter in parameters)
        {
            if (!IsValidName(parameter.Key))
                throw new ArgumentException($"'{parameter.Key}' is not a valid iCalendar parameter name", nameof(parameters));

            if (parameter.Value is null || !IsValidParameterValue(parameter.Value))
                throw new ArgumentException($"The value of the parameter '{parameter.Key}' is neither a paramtext nor a quoted string", nameof(parameters));

            list.Add(parameter);
        }

        if (!IsValidValue(value))
            throw new ArgumentException("The value contains a control character", nameof(value));

        Name = name;
        Parameters = list.AsReadOnly();
        Value = value;
    }

    /// <summary>Creates a property from a parsed content line, whose parts are well formed by construction.</summary>
    private InternetCalendarProperty(ContentLine line)
    {
        Name = line.Name;
        Parameters = line.GetRawParameters().AsReadOnly();
        Value = line.Value;
    }

    /// <summary>Gets the property name.</summary>
    public string Name { get; }

    /// <summary>Gets the property parameters, each value as it is written in the content line.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    /// <summary>Gets the value as it is written in the content line.</summary>
    public string Value { get; }

    internal static InternetCalendarProperty FromContentLine(ContentLine line)
    {
        return new InternetCalendarProperty(line);
    }

    internal void Write(TextWriter writer)
    {
        var sb = new StringBuilder(Name);
        foreach (var parameter in Parameters)
        {
            sb.Append(';').Append(parameter.Key).Append('=').Append(parameter.Value);
        }

        sb.Append(':').Append(Value);
        Utilities.WriteLine(writer, sb.ToString());
    }

    /// <summary>An iCalendar property or parameter name is ALPHA / DIGIT / "-" (RFC 5545 section 3.1).</summary>
    internal static bool IsValidName([NotNullWhen(returnValue: true)] string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-')
                return false;
        }

        return true;
    }

    /// <summary>A param-value list (RFC 5545 section 3.1): paramtext or quoted-string values separated by commas.</summary>
    internal static bool IsValidParameterValue(string value)
    {
        var index = 0;
        while (true)
        {
            if (index < value.Length && value[index] is '"')
            {
                // quoted-string = DQUOTE *QSAFE-CHAR DQUOTE, where QSAFE-CHAR is any character but a CTL and DQUOTE.
                index++;
                while (index < value.Length && value[index] is not '"')
                {
                    if (IsControl(value[index]))
                        return false;

                    index++;
                }

                if (index >= value.Length)
                    return false;

                index++;
            }
            else
            {
                // paramtext = *SAFE-CHAR, where SAFE-CHAR also excludes DQUOTE, ";", ":" and ",".
                while (index < value.Length && value[index] is not ',')
                {
                    if (IsControl(value[index]) || value[index] is '"' or ';' or ':')
                        return false;

                    index++;
                }
            }

            if (index == value.Length)
                return true;

            if (value[index] is not ',')
                return false;

            index++;
        }
    }

    /// <summary>A value is *VALUE-CHAR (RFC 5545 section 3.1): any character but a CTL, horizontal tabs aside.</summary>
    internal static bool IsValidValue(string value)
    {
        foreach (var c in value)
        {
            if (IsControl(c) && c is not '\t')
                return false;
        }

        return true;
    }

    private static bool IsControl(char c)
    {
        return c < 0x20 || c == 0x7F;
    }
}
