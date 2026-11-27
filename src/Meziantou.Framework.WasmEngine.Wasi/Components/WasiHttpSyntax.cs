namespace Meziantou.Framework.WasmEngine.Wasi.Components;

/// <summary>The syntax of the parts of the HTTP requests and responses of <c>wasi:http</c> (RFC 9110, RFC 3986).</summary>
internal static class WasiHttpSyntax
{
    /// <summary>The headers that a component cannot set: they belong to the connection, which the host manages.</summary>
    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection",
        "keep-alive",
        "proxy-authenticate",
        "proxy-authorization",
        "proxy-connection",
        "te",
        "transfer-encoding",
        "upgrade",
        "host",
        "http2-settings",
    };

    /// <summary>Whether a character is a <c>tchar</c>: the characters of a token (a method, a field name).</summary>
    public static bool IsTokenCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    /// <summary>Whether a text is a token (<c>1*tchar</c>).</summary>
    public static bool IsToken(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var c in value)
        {
            if (!IsTokenCharacter(c))
                return false;
        }

        return true;
    }

    public static bool IsValidFieldName(string name) => IsToken(name);

    /// <summary>Whether a field value has no control characters other than the horizontal tabs (CR, LF and NUL would split the field).</summary>
    public static bool IsValidFieldValue(ReadOnlySpan<byte> value)
    {
        foreach (var b in value)
        {
            if ((b < 0x20 && b != '\t') || b == 0x7F)
                return false;
        }

        return true;
    }

    public static bool IsForbiddenHeader(string name) => ForbiddenHeaders.Contains(name);

    /// <summary>Whether a text is a scheme: <c>ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )</c>.</summary>
    public static bool IsValidScheme(string scheme)
    {
        if (scheme.Length == 0 || !char.IsAsciiLetter(scheme[0]))
            return false;

        foreach (var c in scheme)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return false;
        }

        return true;
    }

    /// <summary>Whether a text is an authority: <c>[ userinfo "@" ] host [ ":" port ]</c>, the host a name, an IPv4 address or a bracketed IPv6 address.</summary>
    public static bool IsValidAuthority(string authority)
    {
        if (authority.Length == 0)
            return false;

        var at = authority.LastIndexOf("@", StringComparison.Ordinal);
        var userInfo = at < 0 ? "" : authority[..at];
        var hostPort = authority[(at + 1)..];
        foreach (var c in userInfo)
        {
            if (!IsUnreservedOrSubDelimiter(c) && c is not (':' or '%'))
                return false;
        }

        string host;
        var port = "";
        if (hostPort.StartsWith("[", StringComparison.Ordinal))
        {
            var close = hostPort.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
                return false;

            host = hostPort[1..close];
            var rest = hostPort[(close + 1)..];
            if (rest.Length > 0)
            {
                if (rest[0] != ':')
                    return false;

                port = rest[1..];
            }

            if (!System.Net.IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                return false;
        }
        else
        {
            var colon = hostPort.LastIndexOf(":", StringComparison.Ordinal);
            host = colon < 0 ? hostPort : hostPort[..colon];
            port = colon < 0 ? "" : hostPort[(colon + 1)..];
            if (host.Length == 0)
                return false;

            foreach (var c in host)
            {
                if (!IsUnreservedOrSubDelimiter(c) && c != '%')
                    return false;
            }
        }

        foreach (var c in port)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return port.Length <= 5 && (port.Length == 0 || int.Parse(port, System.Globalization.CultureInfo.InvariantCulture) <= 65535);
    }

    /// <summary>
    /// Whether a text is a path with a query (<c>origin-form</c>, or <c>*</c>): it starts with <c>/</c> or <c>?</c>, and its
    /// characters are the characters that the crate http accepts unencoded (the path accepts <c>"</c>, <c>{</c> and
    /// <c>}</c>, which some clients send). A fragment (<c>#</c>) ends the text: see <see cref="RemoveFragment"/>.
    /// </summary>
    public static bool IsValidPathWithQuery(string pathWithQuery)
    {
        if (pathWithQuery is not ("" or "*") && pathWithQuery[0] is not ('/' or '?'))
            return false;

        var inQuery = false;
        foreach (var c in pathWithQuery)
        {
            if (c == '#')
                return true;

            if (c == '?')
            {
                inQuery = true;
                continue;
            }

            // The bytes of the non-ASCII characters are accepted, as by the crate http
            var valid = c >= 0x80 || (inQuery
                ? c is '!' or (>= '$' and <= ';') or '=' or (>= '?' and <= '~')
                : c is '!' or (>= '$' and <= ';') or '=' or (>= '@' and <= '_') or (>= 'a' and <= 'z') or '|' or '~' or '"' or '{' or '}');
            if (!valid)
                return false;
        }

        return true;
    }

    /// <summary>Removes the fragment of a path with a query: it is not sent.</summary>
    public static string RemoveFragment(string pathWithQuery)
    {
        var fragment = pathWithQuery.IndexOf('#', StringComparison.Ordinal);
        return fragment < 0 ? pathWithQuery : pathWithQuery[..fragment];
    }

    /// <summary>Whether a status code is a status of HTTP (100 to 599).</summary>
    public static bool IsValidStatusCode(int statusCode) => statusCode is >= 100 and <= 599;

    private static bool IsUnreservedOrSubDelimiter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '=';
}
