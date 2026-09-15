namespace Meziantou.Framework.Scheduling;

/// <summary>An iCalendar property kept verbatim: its name, its parameters and its value exactly as they are written in a content line (RFC 5545 section 3.1).</summary>
/// <remarks>
/// <para>Unlike the TEXT values of <see cref="InternetCalendar.AdditionalProperties"/> and <see cref="Event.AdditionalProperties"/>,
/// the value is neither escaped when written nor unescaped when read, so structured values such as
/// <c>GEO:37.386013;-122.082932</c>, <c>CATEGORIES:WORK,MEETING</c> or <c>EXDATE;TZID=Europe/Paris:20240104T100000</c> round-trip unchanged.</para>
/// <para>A parameter value is kept in its param-value form, including the DQUOTE characters of a quoted value, as in
/// <c>new KeyValuePair&lt;string, string&gt;("ALTREP", "\"cid:part1@example.org\"")</c>, but with the RFC 6868 encoding
/// (<c>^^</c>, <c>^n</c> and <c>^'</c>) decoded. See <see cref="Parameters"/>.</para>
/// </remarks>
public sealed class InternetCalendarProperty
{
    /// <summary>The properties whose value is not a single TEXT value (RFC 5545 section 3.8 and RFC 7986 section 5), so TEXT escaping would alter it.</summary>
    private static readonly HashSet<string> NonTextPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // URI, BINARY, CAL-ADDRESS, FLOAT and INTEGER values
        "ATTACH", "ATTENDEE", "CONFERENCE", "GEO", "IMAGE", "ORGANIZER", "PERCENT-COMPLETE", "PRIORITY", "REPEAT", "SEQUENCE", "SOURCE", "TZURL", "URL",

        // DATE, DATE-TIME, DURATION, PERIOD, RECUR and UTC-OFFSET values
        "COMPLETED", "CREATED", "DTEND", "DTSTAMP", "DTSTART", "DUE", "DURATION", "EXDATE", "EXRULE", "FREEBUSY", "LAST-MODIFIED",
        "RDATE", "RECURRENCE-ID", "REFRESH-INTERVAL", "RRULE", "TRIGGER", "TZOFFSETFROM", "TZOFFSETTO",

        // A TEXT value whose semicolons separate its components
        "REQUEST-STATUS",
    };

    /// <summary>The properties whose value is a list of TEXT values, whose commas separate the values.</summary>
    private static readonly HashSet<string> TextListPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CATEGORIES", "RESOURCES",
    };

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
    /// <param name="parameters">The property parameters. See <see cref="Parameters"/> for the form of a value.</param>
    /// <param name="value">The value as it is written in the content line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="parameters"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The name of the property or of a parameter is not a valid name, a parameter value is <see langword="null"/> or
    /// contains a control character other than a horizontal tab or a line feed, or <paramref name="value"/> contains a control character.</exception>
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
                throw new ArgumentException($"The value of the parameter '{parameter.Key}' contains a control character", nameof(parameters));

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
        Parameters = line.GetParameters().AsReadOnly();
        Value = line.Value;
    }

    /// <summary>Gets the property name.</summary>
    public string Name { get; }

    /// <summary>Gets the property parameters.</summary>
    /// <remarks>
    /// <para>A value that is a param-value list (RFC 5545 section 3.1), made of paramtext and quoted-string values separated by
    /// commas, such as <c>ACCEPTED</c>, <c>"Doe, Jane"</c> or <c>"mailto:a@example.com","mailto:b@example.com"</c>, is written as
    /// such. Any other value, such as <c>Jane "JD" Doe</c> or <c>a:b</c>, is a single value, written as a quoted-string.</para>
    /// <para>The characters a param-value cannot hold are written with the RFC 6868 encoding, which the parser decodes: a
    /// circumflex accent as <c>^^</c>, a line feed as <c>^n</c> and, in a single value, a DQUOTE as <c>^'</c>. As a consequence,
    /// a value that is itself a param-value list, such as <c>"a"</c>, cannot denote a value holding DQUOTE characters.</para>
    /// </remarks>
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
        AppendParameters(sb, Parameters);
        sb.Append(':').Append(Value);
        Utilities.WriteLine(writer, sb.ToString());
    }

    /// <summary>Appends the parameters of a content line, each value encoded as <see cref="EncodeParameterValue"/> does.</summary>
    internal static void AppendParameters(StringBuilder sb, IEnumerable<KeyValuePair<string, string>> parameters, Func<string, bool>? skip = null)
    {
        foreach (var parameter in parameters)
        {
            if (skip is not null && skip(parameter.Key))
                continue;

            sb.Append(';').Append(parameter.Key).Append('=').Append(EncodeParameterValue(parameter.Value));
        }
    }

    /// <summary>Gets a value indicating whether the value of a property with this name is a single TEXT value (RFC 5545 section 3.3.11).</summary>
    /// <remarks>A property RFC 5545 or RFC 7986 does not define, such as an <c>X-</c> property, has a TEXT value (RFC 5545 section 3.8.8.2).</remarks>
    internal static bool IsSingleTextProperty(string name)
    {
        return !NonTextPropertyNames.Contains(name) && !TextListPropertyNames.Contains(name);
    }

    /// <summary>Gets a value indicating whether the value of a property with this name is TEXT, or a list of TEXT values, and so is escaped.</summary>
    internal static bool IsTextProperty(string name)
    {
        return !NonTextPropertyNames.Contains(name);
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

    /// <summary>Gets a value indicating whether a parameter value can be written: it contains no control character but a horizontal tab and a line feed, which RFC 6868 encodes.</summary>
    internal static bool IsValidParameterValue(string value)
    {
        foreach (var c in value)
        {
            if (IsForbiddenParameterCharacter(c))
                return false;
        }

        return true;
    }

    /// <summary>Encodes a parameter value (see <see cref="Parameters"/>): a param-value list keeps its syntax, and any other value is written as a quoted-string.</summary>
    internal static string EncodeParameterValue(string value)
    {
        if (TrySplitParameterValues(value, values: null))
            return Encode(value, encodeQuote: false);

        return EncodeSingleParameterValue(value);
    }

    /// <summary>Encodes a value that denotes a single parameter value, such as a TZID, quoting it when it is not a paramtext.</summary>
    internal static string EncodeSingleParameterValue(string value)
    {
        if (value.IndexOfAny(['"', ';', ':', ',']) < 0)
            return Encode(value, encodeQuote: false);

        return '"' + Encode(value, encodeQuote: true) + '"';
    }

    /// <summary>Decodes a parameter value as written in a content line into the form <see cref="Parameters"/> holds.</summary>
    internal static string DecodeParameterValue(string rawValue)
    {
        var values = new List<(string Value, bool IsQuoted)>();
        if (!TrySplitParameterValues(rawValue, values))
        {
            // Not a param-value list, as in CN=Jane "JD" Doe: the text is the value.
            return Decode(rawValue);
        }

        if (rawValue.IndexOf('^', StringComparison.Ordinal) < 0 && !rawValue.Any(IsForbiddenParameterCharacter))
            return rawValue;

        var containsQuote = false;
        var sb = new StringBuilder(rawValue.Length);
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var decoded = Decode(values[i].Value);
            containsQuote |= decoded.Contains('"', StringComparison.Ordinal);
            if (values[i].IsQuoted)
            {
                sb.Append('"').Append(decoded).Append('"');
            }
            else
            {
                sb.Append(decoded);
            }
        }

        // A decoded DQUOTE cannot be held by a quoted-string, so a single value is kept without its delimiters and written
        // back as a quoted-string.
        if (containsQuote && values.Count is 1)
            return Decode(values[0].Value);

        return sb.ToString();
    }

    /// <summary>Gets the value a parameter denotes when it holds a single value, such as TZID or VALUE: the content of a quoted-string, or the value itself.</summary>
    internal static string GetSingleParameterValue(string value)
    {
        var values = new List<(string Value, bool IsQuoted)>(1);
        if (TrySplitParameterValues(value, values) && values.Count is 1)
            return values[0].Value;

        return value;
    }

    /// <summary>Splits a param-value list (RFC 5545 section 3.1) into its paramtext and quoted-string values.</summary>
    private static bool TrySplitParameterValues(string value, List<(string Value, bool IsQuoted)>? values)
    {
        var index = 0;
        while (true)
        {
            if (index < value.Length && value[index] is '"')
            {
                // quoted-string = DQUOTE *QSAFE-CHAR DQUOTE, where QSAFE-CHAR is any character but a CTL and DQUOTE.
                var start = index + 1;
                index = start;
                while (index < value.Length && value[index] is not '"')
                {
                    index++;
                }

                if (index >= value.Length)
                    return false;

                values?.Add((value[start..index], true));
                index++;
            }
            else
            {
                // paramtext = *SAFE-CHAR, where SAFE-CHAR also excludes DQUOTE, ";", ":" and ",".
                var start = index;
                while (index < value.Length && value[index] is not ',')
                {
                    if (value[index] is '"' or ';' or ':')
                        return false;

                    index++;
                }

                values?.Add((value[start..index], false));
            }

            if (index == value.Length)
                return true;

            if (value[index] is not ',')
                return false;

            index++;
        }
    }

    private static string Encode(string value, bool encodeQuote)
    {
        if (value.IndexOfAny(['^', '\n', '"']) < 0)
            return value;

        var sb = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            switch (c)
            {
                case '^':
                    sb.Append("^^");
                    break;
                case '\n':
                    sb.Append("^n");
                    break;
                case '"' when encodeQuote:
                    sb.Append("^'");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Decodes the RFC 6868 encoding, dropping the control characters a parameter value cannot hold.</summary>
    private static string Decode(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '^' && i + 1 < value.Length && value[i + 1] is '^' or 'n' or '\'')
            {
                i++;
                sb.Append(value[i] switch
                {
                    '^' => '^',
                    '\'' => '"',
                    _ => '\n',
                });
            }
            else if (!IsForbiddenParameterCharacter(c))
            {
                // RFC 6868 section 3: a circumflex accent followed by any other character is kept as is.
                sb.Append(c);
            }
        }

        return sb.ToString();
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

    private static bool IsForbiddenParameterCharacter(char c)
    {
        return IsControl(c) && c is not '\t' and not '\n';
    }

    private static bool IsControl(char c)
    {
        return c < 0x20 || c == 0x7F;
    }
}
