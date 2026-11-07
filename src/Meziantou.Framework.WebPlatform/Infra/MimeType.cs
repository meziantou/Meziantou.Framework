using System.Text;

namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>A MIME type (https://mimesniff.spec.whatwg.org/#mime-type-representation).</summary>
internal sealed class MimeType
{
    private MimeType(string type, string subtype)
    {
        Type = type;
        Subtype = subtype;
    }

    /// <summary>Gets the type, in ASCII lowercase (e.g. <c>text</c>).</summary>
    public string Type { get; }

    /// <summary>Gets the subtype, in ASCII lowercase (e.g. <c>html</c>).</summary>
    public string Subtype { get; }

    /// <summary>Gets the parameters, whose names are in ASCII lowercase, in their order of appearance.</summary>
    public List<KeyValuePair<string, string>> Parameters { get; } = [];

    /// <summary>Gets the essence (https://mimesniff.spec.whatwg.org/#mime-type-essence), e.g. <c>text/html</c>.</summary>
    public string Essence => Type + "/" + Subtype;

    /// <summary>Returns the value of a parameter, or null.</summary>
    public string? GetParameter(string name)
    {
        foreach (var parameter in Parameters)
        {
            if (parameter.Key == name)
                return parameter.Value;
        }

        return null;
    }

    /// <summary>Sets the value of a parameter, keeping its position when it exists.</summary>
    public void SetParameter(string name, string value)
    {
        for (var i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i].Key == name)
            {
                Parameters[i] = new(name, value);
                return;
            }
        }

        Parameters.Add(new(name, value));
    }

    /// <summary>https://mimesniff.spec.whatwg.org/#javascript-mime-type</summary>
    public bool IsJavaScript => Dom.HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(Essence);

    /// <summary>https://mimesniff.spec.whatwg.org/#xml-mime-type</summary>
    public bool IsXml => Subtype.EndsWith("+xml", StringComparison.Ordinal) || Essence is "text/xml" or "application/xml";

    /// <summary>https://mimesniff.spec.whatwg.org/#parse-a-mime-type. Returns null on failure.</summary>
    public static MimeType? Parse(string input)
    {
        var value = TrimHttpWhitespace(input);
        var slash = value.IndexOf('/');
        if (slash < 0)
            return null;

        var type = value[..slash];
        if (type.Length == 0 || !IsHttpToken(type))
            return null;

        var position = slash + 1;
        var subtypeEnd = value[position..].IndexOf(';');
        subtypeEnd = subtypeEnd < 0 ? value.Length : position + subtypeEnd;
        var subtype = value[position..subtypeEnd].TrimEnd(HttpWhitespace);
        if (subtype.Length == 0 || !IsHttpToken(subtype))
            return null;

        var mimeType = new MimeType(InfraAscii.ToAsciiLowercase(type.ToString()), InfraAscii.ToAsciiLowercase(subtype.ToString()));
        position = subtypeEnd;
        while (position < value.Length)
        {
            // Skip the ';' and the HTTP whitespace
            position++;
            while (position < value.Length && IsHttpWhitespace(value[position]))
            {
                position++;
            }

            var nameStart = position;
            while (position < value.Length && value[position] is not ';' and not '=')
            {
                position++;
            }

            var name = InfraAscii.ToAsciiLowercase(value[nameStart..position].ToString());
            if (position < value.Length)
            {
                if (value[position] == ';')
                    continue;

                position++;
            }

            if (position >= value.Length)
                break;

            string parameterValue;
            if (value[position] == '"')
            {
                parameterValue = CollectHttpQuotedString(value, ref position, extractValue: true);
                while (position < value.Length && value[position] != ';')
                {
                    position++;
                }
            }
            else
            {
                var valueStart = position;
                while (position < value.Length && value[position] != ';')
                {
                    position++;
                }

                parameterValue = value[valueStart..position].TrimEnd(HttpWhitespace).ToString();
                if (parameterValue.Length == 0)
                    continue;
            }

            if (name.Length > 0 && IsHttpToken(name) && IsHttpQuotedStringTokens(parameterValue) && mimeType.GetParameter(name) is null)
            {
                mimeType.Parameters.Add(new(name, parameterValue));
            }
        }

        return mimeType;
    }

    /// <summary>https://mimesniff.spec.whatwg.org/#serialize-a-mime-type</summary>
    public override string ToString()
    {
        var result = new StringBuilder(Essence);
        foreach (var (name, value) in Parameters)
        {
            result.Append(';').Append(name).Append('=');
            if (value.Length == 0 || !IsHttpToken(value))
            {
                result.Append('"');
                foreach (var c in value)
                {
                    if (c is '"' or '\\')
                    {
                        result.Append('\\');
                    }

                    result.Append(c);
                }

                result.Append('"');
            }
            else
            {
                result.Append(value);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#collect-an-http-quoted-string: <paramref name="position"/> is at a '"', and is moved
    /// after the closing '"' (or at the end of the input).
    /// </summary>
    internal static string CollectHttpQuotedString(ReadOnlySpan<char> input, ref int position, bool extractValue)
    {
        var start = position;
        var value = new StringBuilder();
        position++;
        while (true)
        {
            while (position < input.Length && input[position] is not '"' and not '\\')
            {
                value.Append(input[position]);
                position++;
            }

            if (position >= input.Length)
                break;

            var quoteOrBackslash = input[position];
            position++;
            if (quoteOrBackslash == '\\')
            {
                if (position >= input.Length)
                {
                    value.Append('\\');
                    break;
                }

                value.Append(input[position]);
                position++;
            }
            else
            {
                break;
            }
        }

        return extractValue ? value.ToString() : input[start..position].ToString();
    }

    private static ReadOnlySpan<char> HttpWhitespace => "\t\n\r ";

    /// <summary>https://fetch.spec.whatwg.org/#http-whitespace</summary>
    internal static bool IsHttpWhitespace(char c) => c is '\t' or '\n' or '\r' or ' ';

    internal static ReadOnlySpan<char> TrimHttpWhitespace(ReadOnlySpan<char> value) => value.Trim(HttpWhitespace);

    /// <summary>https://mimesniff.spec.whatwg.org/#http-token-code-point</summary>
    private static bool IsHttpToken(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~'))
                return false;
        }

        return true;
    }

    /// <summary>https://mimesniff.spec.whatwg.org/#http-quoted-string-token-code-point</summary>
    private static bool IsHttpQuotedStringTokens(string value)
    {
        foreach (var c in value)
        {
            if (c is not ('\t' or (>= ' ' and <= '~') or (>= (char)0x80 and <= (char)0xFF)))
                return false;
        }

        return true;
    }
}
