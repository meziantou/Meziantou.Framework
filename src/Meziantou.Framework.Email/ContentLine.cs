using System.Text;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email;

/// <summary>A property of a vCard or of an iCalendar component: <c>[group.]NAME;PARAM=value:value</c> ([RFC 6350] 3.3, [RFC 5545] 3.1).</summary>
public sealed class ContentLine
{
    /// <summary>Creates a content line.</summary>
    /// <param name="name">The name of the property, such as <c>TEL</c> or <c>DTSTART</c>.</param>
    /// <param name="value">The value, as it is written (escaped text, a date, a recurrence rule...).</param>
    public ContentLine(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    /// <summary>Gets or sets the group of the property (the prefix before a dot, <c>item1.TEL</c>), or <see langword="null"/>.</summary>
    public string? Group { get; set; }

    /// <summary>Gets the name of the property, in upper case.</summary>
    public string Name
    {
        get;
        private set => field = value.ToUpperInvariant();
    }

    /// <summary>Gets the parameters of the property.</summary>
    public IList<ContentLineParameter> Parameters { get; } = [];

    /// <summary>Gets or sets the value, as it is written: the text values are escaped (<c>\,</c>, <c>\;</c>, <c>\n</c>). The reader decodes the values of vCard 2.1 in quoted-printable.</summary>
    public string Value { get; set; }

    /// <summary>Returns the values of a parameter.</summary>
    /// <param name="name">The name of the parameter, without case.</param>
    /// <returns>The values; empty when the property does not have the parameter.</returns>
    public IReadOnlyList<string> GetParameterValues(string name)
    {
        var result = new List<string>();
        foreach (var parameter in Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(parameter.Values);
            }
        }

        return result;
    }

    /// <summary>Returns the first value of a parameter.</summary>
    /// <param name="name">The name of the parameter, without case.</param>
    /// <returns>The value, or <see langword="null"/> when the property does not have the parameter.</returns>
    public string? GetParameter(string name) => GetParameterValues(name) is [var first, ..] ? first : null;

    /// <summary>Sets a parameter, or removes it when the value is <see langword="null"/>.</summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="value">The value.</param>
    public void SetParameter(string name, string? value)
    {
        for (var i = Parameters.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Parameters.RemoveAt(i);
            }
        }

        if (value is not null)
        {
            Parameters.Add(new ContentLineParameter(name.ToUpperInvariant(), value));
        }
    }

    /// <summary>Returns the types of the property: the values of the <c>TYPE</c> parameters and the parameters of vCard 2.1 without a name, in lower case.</summary>
    /// <returns>The types.</returns>
    public IReadOnlyList<string> GetTypes()
    {
        var result = new List<string>();
        foreach (var parameter in Parameters)
        {
            if (parameter.Name is null || string.Equals(parameter.Name, "TYPE", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var value in parameter.Values)
                {
                    foreach (var type in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        result.Add(type.ToLowerInvariant());
                    }
                }
            }
            else if (string.Equals(parameter.Name, "PREF", StringComparison.OrdinalIgnoreCase))
            {
                result.Add("pref");
            }
        }

        return result;
    }

    /// <summary>Returns the value decoded as a text: <c>\n</c> is a line break, <c>\,</c>, <c>\;</c> and <c>\\</c> are the characters.</summary>
    /// <returns>The text.</returns>
    public string GetText() => DecodeText(Value);

    /// <summary>Returns the components of a structured value (separated by <c>;</c>), decoded as texts.</summary>
    /// <returns>The components.</returns>
    public IReadOnlyList<string> GetComponents() => [.. Split(Value, ';').Select(DecodeText)];

    /// <summary>Returns the values of a value list (separated by <c>,</c>), decoded as texts.</summary>
    /// <returns>The values.</returns>
    public IReadOnlyList<string> GetValues() => [.. Split(Value, ',').Select(DecodeText).Where(value => value.Length > 0)];

    /// <summary>Decodes an escaped text.</summary>
    /// <param name="value">The escaped text.</param>
    /// <returns>The text.</returns>
    public static string DecodeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                builder.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Escapes a text: <c>\</c>, <c>,</c>, <c>;</c> and the line breaks.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string EncodeText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\' or ',' or ';':
                    builder.Append('\\').Append(c);
                    break;
                case '\r' when i + 1 < value.Length && value[i + 1] == '\n':
                    break;
                case '\r' or '\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Splits a value on a separator that is not escaped.</summary>
    internal static List<string> Split(string value, char separator)
    {
        var result = new List<string>();
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\')
            {
                i++;
            }
            else if (value[i] == separator)
            {
                result.Add(value[start..i]);
                start = i + 1;
            }
        }

        result.Add(value[start..]);
        return result;
    }

    /// <summary>Returns the content line as it is written, without folding.</summary>
    /// <returns>The content line.</returns>
    public override string ToString() => ContentLineWriter.Format(this, vcard21: false);
}
