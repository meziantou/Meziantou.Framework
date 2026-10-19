using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A metric of the <c>Server-Timing</c> headers of the response of a resource (https://w3c.github.io/server-timing/#the-performanceservertiming-interface),
/// exposed by <see cref="PerformanceResourceTiming.ServerTiming"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/server-timing/#dom-performanceservertiming"/>
[IdlInterface("PerformanceServerTiming", "https://w3c.github.io/server-timing/#dom-performanceservertiming")]
public sealed class PerformanceServerTiming
{
    internal PerformanceServerTiming(string name, double duration, string description)
    {
        Name = name;
        Duration = duration;
        Description = description;
    }

    /// <summary>Gets the name of the metric.</summary>
    /// <seealso href="https://w3c.github.io/server-timing/#dom-performanceservertiming-name"/>
    [IdlMember("name", "https://w3c.github.io/server-timing/#dom-performanceservertiming-name")]
    public string Name { get; }

    /// <summary>Gets the duration of the metric (its <c>dur</c> parameter), or 0.</summary>
    /// <seealso href="https://w3c.github.io/server-timing/#dom-performanceservertiming-duration"/>
    [IdlMember("duration", "https://w3c.github.io/server-timing/#dom-performanceservertiming-duration")]
    public double Duration { get; }

    /// <summary>Gets the description of the metric (its <c>desc</c> parameter), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/server-timing/#dom-performanceservertiming-description"/>
    [IdlMember("description", "https://w3c.github.io/server-timing/#dom-performanceservertiming-description")]
    public string Description { get; }

    /// <summary>
    /// Parses the <c>Server-Timing</c> headers of a response (https://w3c.github.io/server-timing/#the-server-timing-header-field):
    /// a list of metrics, each one a name followed by parameters (<c>name;dur=12.3;desc="description"</c>). The first
    /// <c>dur</c> and <c>desc</c> parameters of a metric are used, the other parameters are ignored, and a metric without
    /// name is skipped.
    /// </summary>
    internal static IReadOnlyList<PerformanceServerTiming> Parse(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        List<PerformanceServerTiming>? result = null;
        foreach (var (headerName, value) in headers)
        {
            if (!headerName.Equals("Server-Timing", StringComparison.OrdinalIgnoreCase))
                continue;

            var position = 0;
            while (position < value.Length)
            {
                SkipWhitespace(value, ref position);
                var name = ReadToken(value, ref position);
                double? duration = null;
                string? description = null;
                while (true)
                {
                    // The parameters, then the next metric
                    SkipUntil(value, ref position, ';', ',');
                    if (position >= value.Length || value[position] == ',')
                        break;

                    position++;
                    SkipWhitespace(value, ref position);
                    var parameterName = ReadToken(value, ref position);
                    SkipWhitespace(value, ref position);
                    var parameterValue = "";
                    if (position < value.Length && value[position] == '=')
                    {
                        position++;
                        SkipWhitespace(value, ref position);
                        parameterValue = position < value.Length && value[position] == '"' ? ReadQuotedString(value, ref position) : ReadToken(value, ref position);
                    }

                    if (parameterName.Equals("dur", StringComparison.OrdinalIgnoreCase))
                    {
                        duration ??= double.TryParse(parameterValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? parsed : 0;
                    }
                    else if (parameterName.Equals("desc", StringComparison.OrdinalIgnoreCase))
                    {
                        description ??= parameterValue;
                    }
                }

                position++;
                if (name.Length > 0)
                {
                    (result ??= []).Add(new PerformanceServerTiming(name, duration ?? 0, description ?? ""));
                }
            }
        }

        return result is null ? [] : result;
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && value[position] is ' ' or '\t')
        {
            position++;
        }
    }

    /// <summary>Skips the characters until one of two delimiters, outside of the quoted strings.</summary>
    private static void SkipUntil(string value, ref int position, char first, char second)
    {
        while (position < value.Length && value[position] != first && value[position] != second)
        {
            if (value[position] == '"')
            {
                ReadQuotedString(value, ref position);
            }
            else
            {
                position++;
            }
        }
    }

    /// <summary>Reads a token (https://httpwg.org/specs/rfc9110.html#tokens).</summary>
    private static string ReadToken(string value, ref int position)
    {
        var start = position;
        while (position < value.Length && (char.IsAsciiLetterOrDigit(value[position]) || "!#$%&'*+-.^_`|~".Contains(value[position], StringComparison.Ordinal)))
        {
            position++;
        }

        return value[start..position];
    }

    /// <summary>Reads a quoted string (https://httpwg.org/specs/rfc9110.html#quoted.strings): a string that is not closed ends at the end of the value.</summary>
    private static string ReadQuotedString(string value, ref int position)
    {
        var result = new StringBuilder();
        position++;
        while (position < value.Length && value[position] != '"')
        {
            if (value[position] == '\\' && position + 1 < value.Length)
            {
                position++;
            }

            result.Append(value[position++]);
        }

        position++;
        return result.ToString();
    }
}
