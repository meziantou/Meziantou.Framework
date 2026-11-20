using System.Text;

namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>
/// A content type of the Media Capabilities API: a valid MIME type string (https://mimesniff.spec.whatwg.org/#valid-mime-type),
/// the <c>media-type</c> production of HTTP (https://www.rfc-editor.org/rfc/rfc9110#name-media-type) where a parameter
/// cannot be empty, as Chromium parses it. Unlike the lenient parser of MIME Sniffing, a string with a trailing
/// semicolon (<c>audio/mpeg;</c>), a parameter without value, or a character that is not allowed is not a content type.
/// The optional whitespace around the string is allowed.
/// </summary>
internal sealed class MediaContentType
{
    private MediaContentType(string type, string subtype, IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        Type = type;
        Subtype = subtype;
        Parameters = parameters;
    }

    /// <summary>Gets the type, in ASCII lowercase (e.g. <c>video</c>).</summary>
    public string Type { get; }

    /// <summary>Gets the subtype, in ASCII lowercase (e.g. <c>webm</c>).</summary>
    public string Subtype { get; }

    /// <summary>Gets the essence of the type, in ASCII lowercase (e.g. <c>video/webm</c>).</summary>
    public string Essence => Type + "/" + Subtype;

    /// <summary>Gets the parameters in their order: the names in ASCII lowercase, the values without their quotes and escapes. A name can appear several times.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }

    /// <summary>Parses a content type, or returns null when the string is not a valid MIME type string.</summary>
    /// <param name="value">The string.</param>
    public static MediaContentType? Parse(string value)
    {
        var position = 0;
        SkipWhitespace(value, ref position);
        var type = ReadToken(value, ref position);
        if (type.Length == 0 || position >= value.Length || value[position] != '/')
            return null;

        position++;
        var subtype = ReadToken(value, ref position);
        if (subtype.Length == 0)
            return null;

        var parameters = new List<KeyValuePair<string, string>>();
        while (true)
        {
            SkipWhitespace(value, ref position);
            if (position == value.Length)
                break;

            if (value[position] != ';')
                return null;

            position++;
            SkipWhitespace(value, ref position);
            var name = ReadToken(value, ref position);
            if (name.Length == 0 || position >= value.Length || value[position] != '=')
                return null;

            position++;
            // A quoted value can be empty, a token cannot
            var isQuoted = position < value.Length && value[position] == '"';
            var parameterValue = isQuoted ? ReadQuotedString(value, ref position) : ReadToken(value, ref position);
            if (parameterValue is null || (!isQuoted && parameterValue.Length == 0))
                return null;

            parameters.Add(new(name.ToLowerInvariant(), parameterValue));
        }

        return new MediaContentType(type.ToLowerInvariant(), subtype.ToLowerInvariant(), parameters);
    }

    /// <summary>Returns the value of the only parameter with a name, or null when there is none or several.</summary>
    /// <param name="name">The name of the parameter, in ASCII lowercase.</param>
    public string? GetParameter(string name)
    {
        string? result = null;
        foreach (var parameter in Parameters)
        {
            if (parameter.Key == name)
            {
                if (result is not null)
                    return null;

                result = parameter.Value;
            }
        }

        return result;
    }

    /// <summary>Serializes the content type: the values that are not tokens are quoted.</summary>
    public override string ToString()
    {
        var result = new StringBuilder();
        result.Append(Type).Append('/').Append(Subtype);
        foreach (var (name, value) in Parameters)
        {
            result.Append(';').Append(name).Append('=');
            if (value.Length > 0 && value.All(IsTokenChar))
            {
                result.Append(value);
                continue;
            }

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

        return result.ToString();
    }

    /// <summary>tchar (https://www.rfc-editor.org/rfc/rfc9110#name-tokens)</summary>
    internal static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && value[position] is ' ' or '\t')
        {
            position++;
        }
    }

    private static string ReadToken(string value, ref int position)
    {
        var start = position;
        while (position < value.Length && IsTokenChar(value[position]))
        {
            position++;
        }

        return value[start..position];
    }

    /// <summary>quoted-string (https://www.rfc-editor.org/rfc/rfc9110#name-quoted-strings): the value, or null when the string is not terminated or has a character that is not allowed.</summary>
    private static string? ReadQuotedString(string value, ref int position)
    {
        var result = new StringBuilder();
        position++;
        while (position < value.Length)
        {
            var c = value[position++];
            switch (c)
            {
                case '"':
                    return result.ToString();

                case '\\':
                    if (position >= value.Length || !IsQuotedPairChar(value[position]))
                        return null;

                    result.Append(value[position++]);
                    break;

                default:
                    if (!IsQuotedTextChar(c))
                        return null;

                    result.Append(c);
                    break;
            }
        }

        return null;

        // qdtext = HTAB / SP / %x21 / %x23-5B / %x5D-7E / obs-text
        static bool IsQuotedTextChar(char c) => c is '\t' or ' ' or '!' or (>= '#' and <= '[') or (>= ']' and <= '~') or (>= '\u0080' and <= 'ÿ');

        // quoted-pair = "\" ( HTAB / SP / VCHAR / obs-text )
        static bool IsQuotedPairChar(char c) => c is '\t' or (>= ' ' and <= '~') or (>= '\u0080' and <= 'ÿ');
    }
}
