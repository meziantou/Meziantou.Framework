using System.Buffers;
using System.Diagnostics;

namespace Meziantou.Framework.UrlPatternInternal;

/// <summary>A URL, as the URL Standard parses it.</summary>
/// <remarks>
/// <para>
/// <see cref="System.Uri"/> follows RFC 3986, and the values a URL pattern is matched against have to come from
/// the URL Standard instead. The two disagree on more than escaping: <see cref="System.Uri"/> rejects
/// "https:example.com", resolves a relative URL against a base URL with an opaque path such as "data:text",
/// does not read "0x7f.1" as an IPv4 address, and decodes "/%41" to "/A".
/// </para>
/// <para>
/// Every component is kept in its serialized form, which is the form a URL pattern matches.
/// </para>
/// <see href="https://url.spec.whatwg.org/#concept-url">URL Standard - URL</see>
/// </remarks>
internal sealed class UrlRecord
{
    private const int EndOfFile = -1;

    private UrlRecord(string scheme, string username, string password, string? host, string? port, List<string>? path, string? opaquePath, string? query, string? fragment)
    {
        Scheme = scheme;
        Username = username;
        Password = password;
        Host = host;
        Port = port;
        Path = path;
        OpaquePath = opaquePath;
        Query = query;
        Fragment = fragment;
    }

    public string Scheme { get; }

    public string Username { get; }

    public string Password { get; }

    /// <summary>Gets the serialized host, or <see langword="null"/> when the URL has no host.</summary>
    public string? Host { get; }

    /// <summary>Gets the serialized port, or <see langword="null"/> when the URL has none or it is the default port of the scheme.</summary>
    public string? Port { get; }

    /// <summary>Gets the path segments, or <see langword="null"/> when the URL has an opaque path.</summary>
    public List<string>? Path { get; }

    public string? OpaquePath { get; }

    public string? Query { get; }

    public string? Fragment { get; }

    [MemberNotNullWhen(true, nameof(OpaquePath))]
    [MemberNotNullWhen(false, nameof(Path))]
    public bool HasOpaquePath => OpaquePath is not null;

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#url-path-serializer">URL Standard - URL path serializer</see>
    /// </remarks>
    public string SerializePath()
    {
        if (HasOpaquePath)
            return OpaquePath;

        var builder = new StringBuilder();
        foreach (var segment in Path)
        {
            builder.Append('/').Append(segment);
        }

        return builder.ToString();
    }

    /// <summary>Runs the basic URL parser, without a URL record or a state override.</summary>
    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#concept-basic-url-parser">URL Standard - Basic URL parser</see>
    /// </remarks>
    public static bool TryParse(string input, UrlRecord? baseUrl, [NotNullWhen(true)] out UrlRecord? result)
    {
        result = null;

        var codePoints = Preprocess(input);
        var state = State.SchemeStart;
        var buffer = new StringBuilder();
        var bufferCodePointCount = 0;
        var atSignSeen = false;
        var insideBrackets = false;
        var passwordTokenSeen = false;

        var scheme = "";
        var username = new StringBuilder();
        var password = new StringBuilder();
        string? host = null;
        string? port = null;
        var path = new List<string>();
        StringBuilder? opaquePath = null;
        StringBuilder? query = null;
        StringBuilder? fragment = null;

        var pointer = 0;
        while (true)
        {
            var c = pointer < codePoints.Length ? codePoints[pointer] : EndOfFile;
            var isSpecial = SpecialSchemes.Contains(scheme);

            switch (state)
            {
                case State.SchemeStart:
                    if (IsAsciiAlpha(c))
                    {
                        buffer.Append(char.ToLowerInvariant((char)c));
                        state = State.Scheme;
                    }
                    else
                    {
                        state = State.NoScheme;
                        pointer--;
                    }

                    break;

                case State.Scheme:
                    if (IsAsciiAlpha(c) || IsAsciiDigit(c) || c is '+' or '-' or '.')
                    {
                        buffer.Append(char.ToLowerInvariant((char)c));
                    }
                    else if (c is ':')
                    {
                        scheme = buffer.ToString();
                        buffer.Clear();
                        if (scheme is "file")
                        {
                            state = State.File;
                        }
                        else if (SpecialSchemes.Contains(scheme) && baseUrl is not null && baseUrl.Scheme == scheme)
                        {
                            state = State.SpecialRelativeOrAuthority;
                        }
                        else if (SpecialSchemes.Contains(scheme))
                        {
                            state = State.SpecialAuthoritySlashes;
                        }
                        else if (RemainingStartsWith(codePoints, pointer, '/'))
                        {
                            state = State.PathOrAuthority;
                            pointer++;
                        }
                        else
                        {
                            opaquePath = new StringBuilder();
                            state = State.OpaquePath;
                        }
                    }
                    else
                    {
                        // Not a scheme after all, so the input is read again from its first code point
                        buffer.Clear();
                        state = State.NoScheme;
                        pointer = -1;
                    }

                    break;

                case State.NoScheme:
                    if (baseUrl is null || (baseUrl.HasOpaquePath && c is not '#'))
                        return false;

                    if (baseUrl.HasOpaquePath)
                    {
                        scheme = baseUrl.Scheme;
                        opaquePath = new StringBuilder(baseUrl.OpaquePath);
                        query = CopyOf(baseUrl.Query);
                        fragment = new StringBuilder();
                        state = State.Fragment;
                    }
                    else
                    {
                        state = baseUrl.Scheme is "file" ? State.File : State.Relative;
                        pointer--;
                    }

                    break;

                case State.SpecialRelativeOrAuthority:
                    if (c is '/' && RemainingStartsWith(codePoints, pointer, '/'))
                    {
                        state = State.SpecialAuthorityIgnoreSlashes;
                        pointer++;
                    }
                    else
                    {
                        state = State.Relative;
                        pointer--;
                    }

                    break;

                case State.PathOrAuthority:
                    if (c is '/')
                    {
                        state = State.Authority;
                    }
                    else
                    {
                        state = State.Path;
                        pointer--;
                    }

                    break;

                case State.Relative:
                    Debug.Assert(baseUrl is not null && !baseUrl.HasOpaquePath);
                    scheme = baseUrl.Scheme;
                    if (c is '/' || (SpecialSchemes.Contains(scheme) && c is '\\'))
                    {
                        state = State.RelativeSlash;
                    }
                    else
                    {
                        username.Clear().Append(baseUrl.Username);
                        password.Clear().Append(baseUrl.Password);
                        host = baseUrl.Host;
                        port = baseUrl.Port;
                        path = [.. baseUrl.Path];
                        query = CopyOf(baseUrl.Query);
                        if (c is '?')
                        {
                            query = new StringBuilder();
                            state = State.Query;
                        }
                        else if (c is '#')
                        {
                            fragment = new StringBuilder();
                            state = State.Fragment;
                        }
                        else if (c is not EndOfFile)
                        {
                            query = null;
                            ShortenPath(scheme, path);
                            state = State.Path;
                            pointer--;
                        }
                    }

                    break;

                case State.RelativeSlash:
                    Debug.Assert(baseUrl is not null);
                    if (isSpecial && c is '/' or '\\')
                    {
                        state = State.SpecialAuthorityIgnoreSlashes;
                    }
                    else if (c is '/')
                    {
                        state = State.Authority;
                    }
                    else
                    {
                        username.Clear().Append(baseUrl.Username);
                        password.Clear().Append(baseUrl.Password);
                        host = baseUrl.Host;
                        port = baseUrl.Port;
                        state = State.Path;
                        pointer--;
                    }

                    break;

                case State.SpecialAuthoritySlashes:
                    state = State.SpecialAuthorityIgnoreSlashes;
                    if (c is '/' && RemainingStartsWith(codePoints, pointer, '/'))
                    {
                        pointer++;
                    }
                    else
                    {
                        pointer--;
                    }

                    break;

                case State.SpecialAuthorityIgnoreSlashes:
                    if (c is not ('/' or '\\'))
                    {
                        state = State.Authority;
                        pointer--;
                    }

                    break;

                case State.Authority:
                    if (c is '@')
                    {
                        if (atSignSeen)
                        {
                            buffer.Insert(0, "%40");
                        }

                        atSignSeen = true;
                        foreach (var codePoint in buffer.ToString().EnumerateRunes())
                        {
                            if (codePoint.Value is ':' && !passwordTokenSeen)
                            {
                                passwordTokenSeen = true;
                                continue;
                            }

                            PercentEncoding.EncodeCodePoint(passwordTokenSeen ? password : username, codePoint, PercentEncodeSet.UserInfo);
                        }

                        buffer.Clear();
                        bufferCodePointCount = 0;
                    }
                    else if (c is EndOfFile or '/' or '?' or '#' || (isSpecial && c is '\\'))
                    {
                        // A URL with credentials must have a host
                        if (atSignSeen && buffer.Length == 0)
                            return false;

                        // The host is read again, from the code point that follows the last '@'
                        pointer -= bufferCodePointCount + 1;
                        buffer.Clear();
                        bufferCodePointCount = 0;
                        state = State.Host;
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                        bufferCodePointCount++;
                    }

                    break;

                case State.Host:
                    if (c is ':' && !insideBrackets)
                    {
                        if (buffer.Length == 0)
                            return false;

                        if (!HostParser.TryParse(buffer.ToString(), isOpaque: !isSpecial, out host))
                            return false;

                        buffer.Clear();
                        state = State.Port;
                    }
                    else if (c is EndOfFile or '/' or '?' or '#' || (isSpecial && c is '\\'))
                    {
                        pointer--;
                        if (isSpecial && buffer.Length == 0)
                            return false;

                        if (!HostParser.TryParse(buffer.ToString(), isOpaque: !isSpecial, out host))
                            return false;

                        buffer.Clear();
                        state = State.PathStart;
                    }
                    else
                    {
                        if (c is '[')
                        {
                            insideBrackets = true;
                        }
                        else if (c is ']')
                        {
                            insideBrackets = false;
                        }

                        AppendCodePoint(buffer, c);
                    }

                    break;

                case State.Port:
                    if (IsAsciiDigit(c))
                    {
                        buffer.Append((char)c);
                    }
                    else if (c is EndOfFile or '/' or '?' or '#' || (isSpecial && c is '\\'))
                    {
                        if (buffer.Length != 0)
                        {
                            if (!TryParsePort(buffer, out var portNumber))
                                return false;

                            var serialized = portNumber.ToString(CultureInfo.InvariantCulture);
                            port = SpecialSchemes.TryGetDefaultPort(scheme, out var defaultPort) && serialized == defaultPort ? null : serialized;
                            buffer.Clear();
                        }

                        state = State.PathStart;
                        pointer--;
                    }
                    else
                    {
                        return false;
                    }

                    break;

                case State.File:
                    scheme = "file";
                    host = "";
                    if (c is '/' or '\\')
                    {
                        state = State.FileSlash;
                    }
                    else if (baseUrl is not null && baseUrl.Scheme is "file")
                    {
                        Debug.Assert(!baseUrl.HasOpaquePath);
                        host = baseUrl.Host;
                        path = [.. baseUrl.Path];
                        query = CopyOf(baseUrl.Query);
                        if (c is '?')
                        {
                            query = new StringBuilder();
                            state = State.Query;
                        }
                        else if (c is '#')
                        {
                            fragment = new StringBuilder();
                            state = State.Fragment;
                        }
                        else if (c is not EndOfFile)
                        {
                            query = null;
                            if (StartsWithWindowsDriveLetter(codePoints, pointer))
                            {
                                path.Clear();
                            }
                            else
                            {
                                ShortenPath(scheme, path);
                            }

                            state = State.Path;
                            pointer--;
                        }
                    }
                    else
                    {
                        state = State.Path;
                        pointer--;
                    }

                    break;

                case State.FileSlash:
                    if (c is '/' or '\\')
                    {
                        state = State.FileHost;
                    }
                    else
                    {
                        if (baseUrl is not null && baseUrl.Scheme is "file")
                        {
                            Debug.Assert(!baseUrl.HasOpaquePath);
                            host = baseUrl.Host;
                            if (!StartsWithWindowsDriveLetter(codePoints, pointer) && baseUrl.Path.Count > 0 && IsNormalizedWindowsDriveLetter(baseUrl.Path[0]))
                            {
                                path.Add(baseUrl.Path[0]);
                            }
                        }

                        state = State.Path;
                        pointer--;
                    }

                    break;

                case State.FileHost:
                    if (c is EndOfFile or '/' or '\\' or '?' or '#')
                    {
                        pointer--;
                        if (IsWindowsDriveLetter(buffer.ToString()))
                        {
                            // The buffer is kept, and becomes the first segment of the path
                            state = State.Path;
                        }
                        else if (buffer.Length == 0)
                        {
                            host = "";
                            state = State.PathStart;
                        }
                        else
                        {
                            if (!HostParser.TryParse(buffer.ToString(), isOpaque: !isSpecial, out host))
                                return false;

                            if (host is "localhost")
                            {
                                host = "";
                            }

                            buffer.Clear();
                            state = State.PathStart;
                        }
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                    }

                    break;

                case State.PathStart:
                    if (isSpecial)
                    {
                        state = State.Path;
                        if (c is not ('/' or '\\'))
                        {
                            pointer--;
                        }
                    }
                    else if (c is '?')
                    {
                        query = new StringBuilder();
                        state = State.Query;
                    }
                    else if (c is '#')
                    {
                        fragment = new StringBuilder();
                        state = State.Fragment;
                    }
                    else if (c is not EndOfFile)
                    {
                        state = State.Path;
                        if (c is not '/')
                        {
                            pointer--;
                        }
                    }

                    break;

                case State.Path:
                    var isSeparator = c is '/' || (isSpecial && c is '\\');
                    if (c is EndOfFile or '?' or '#' || isSeparator)
                    {
                        var segment = buffer.ToString();
                        if (IsDoubleDotPathSegment(segment))
                        {
                            ShortenPath(scheme, path);
                            if (!isSeparator)
                            {
                                path.Add("");
                            }
                        }
                        else if (IsSingleDotPathSegment(segment))
                        {
                            if (!isSeparator)
                            {
                                path.Add("");
                            }
                        }
                        else
                        {
                            if (scheme is "file" && path.Count == 0 && IsWindowsDriveLetter(segment))
                            {
                                segment = string.Concat(segment.AsSpan(0, 1), ":");
                            }

                            path.Add(segment);
                        }

                        buffer.Clear();
                        if (c is '?')
                        {
                            query = new StringBuilder();
                            state = State.Query;
                        }
                        else if (c is '#')
                        {
                            fragment = new StringBuilder();
                            state = State.Fragment;
                        }
                    }
                    else
                    {
                        PercentEncoding.EncodeCodePoint(buffer, new Rune(c), PercentEncodeSet.Path);
                    }

                    break;

                case State.OpaquePath:
                    Debug.Assert(opaquePath is not null);
                    if (c is '?')
                    {
                        query = new StringBuilder();
                        state = State.Query;
                    }
                    else if (c is '#')
                    {
                        fragment = new StringBuilder();
                        state = State.Fragment;
                    }
                    else if (c is ' ')
                    {
                        // A space is only encoded when it would otherwise end up trailing the path
                        opaquePath.Append(RemainingStartsWith(codePoints, pointer, '?') || RemainingStartsWith(codePoints, pointer, '#') ? "%20" : " ");
                    }
                    else if (c is not EndOfFile)
                    {
                        PercentEncoding.EncodeCodePoint(opaquePath, new Rune(c), PercentEncodeSet.C0Control);
                    }

                    break;

                case State.Query:
                    Debug.Assert(query is not null);
                    if (c is '#' or EndOfFile)
                    {
                        query.Append(PercentEncoding.Encode(buffer.ToString(), isSpecial ? PercentEncodeSet.SpecialQuery : PercentEncodeSet.Query));
                        buffer.Clear();
                        if (c is '#')
                        {
                            fragment = new StringBuilder();
                            state = State.Fragment;
                        }
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                    }

                    break;

                case State.Fragment:
                    Debug.Assert(fragment is not null);
                    if (c is not EndOfFile)
                    {
                        PercentEncoding.EncodeCodePoint(fragment, new Rune(c), PercentEncodeSet.Fragment);
                    }

                    break;
            }

            if (pointer >= codePoints.Length)
                break;

            pointer++;
        }

        result = new UrlRecord(
            scheme,
            username.ToString(),
            password.ToString(),
            host,
            port,
            opaquePath is null ? path : null,
            opaquePath?.ToString(),
            query?.ToString(),
            fragment?.ToString());
        return true;
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#single-dot-path-segment">URL Standard - Single-dot URL path segment</see>
    /// </remarks>
    public static bool IsSingleDotPathSegment(string segment)
    {
        return segment is "." || segment.Equals("%2e", StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#double-dot-path-segment">URL Standard - Double-dot URL path segment</see>
    /// </remarks>
    public static bool IsDoubleDotPathSegment(string segment)
    {
        return segment is ".." ||
            segment.Equals(".%2e", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("%2e.", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Removes the leading and trailing C0 controls and spaces, and every tab and newline, then splits the input into code points.</summary>
    /// <remarks>
    /// An unpaired surrogate becomes U+FFFD, which is how a DOMString becomes a USVString.
    /// </remarks>
    private static int[] Preprocess(string input)
    {
        var span = input.AsSpan();
        var start = 0;
        while (start < span.Length && span[start] <= ' ')
        {
            start++;
        }

        var end = span.Length;
        while (end > start && span[end - 1] <= ' ')
        {
            end--;
        }

        span = span[start..end];

        var codePoints = new List<int>(span.Length);
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out var rune, out var consumed) is not OperationStatus.Done)
            {
                rune = Rune.ReplacementChar;
            }

            if (rune.Value is not ('\t' or '\n' or '\r'))
            {
                codePoints.Add(rune.Value);
            }

            span = span[consumed..];
        }

        return [.. codePoints];
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#shorten-a-urls-path">URL Standard - Shorten a URL's path</see>
    /// </remarks>
    private static void ShortenPath(string scheme, List<string> path)
    {
        if (scheme is "file" && path.Count == 1 && IsNormalizedWindowsDriveLetter(path[0]))
            return;

        if (path.Count > 0)
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool TryParsePort(StringBuilder digits, out int port)
    {
        port = 0;
        foreach (var chunk in digits.GetChunks())
        {
            foreach (var c in chunk.Span)
            {
                port = (port * 10) + (c - '0');
                if (port > ushort.MaxValue)
                    return false;
            }
        }

        return true;
    }

    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#windows-drive-letter">URL Standard - Windows drive letter</see>
    /// </remarks>
    private static bool IsWindowsDriveLetter(string value)
    {
        return value.Length == 2 && char.IsAsciiLetter(value[0]) && value[1] is ':' or '|';
    }

    private static bool IsNormalizedWindowsDriveLetter(string value)
    {
        return value.Length == 2 && char.IsAsciiLetter(value[0]) && value[1] is ':';
    }

    /// <summary>Determines whether the code points from <paramref name="index"/> start with a Windows drive letter.</summary>
    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#start-with-a-windows-drive-letter">URL Standard - Starts with a Windows drive letter</see>
    /// </remarks>
    private static bool StartsWithWindowsDriveLetter(int[] codePoints, int index)
    {
        var length = codePoints.Length - index;
        return length >= 2 &&
            IsAsciiAlpha(codePoints[index]) &&
            codePoints[index + 1] is ':' or '|' &&
            (length == 2 || codePoints[index + 2] is '/' or '\\' or '?' or '#');
    }

    /// <summary>Determines whether the code points that follow <paramref name="pointer"/> start with <paramref name="value"/>.</summary>
    private static bool RemainingStartsWith(int[] codePoints, int pointer, char value)
    {
        return pointer + 1 < codePoints.Length && codePoints[pointer + 1] == value;
    }

    private static bool IsAsciiAlpha(int c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsAsciiDigit(int c) => c is >= '0' and <= '9';

    private static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        Span<char> chars = stackalloc char[2];
        var length = new Rune(codePoint).EncodeToUtf16(chars);
        builder.Append(chars[..length]);
    }

    private static StringBuilder? CopyOf(string? value) => value is null ? null : new StringBuilder(value);

    private enum State
    {
        SchemeStart,
        Scheme,
        NoScheme,
        SpecialRelativeOrAuthority,
        PathOrAuthority,
        Relative,
        RelativeSlash,
        SpecialAuthoritySlashes,
        SpecialAuthorityIgnoreSlashes,
        Authority,
        Host,
        Port,
        File,
        FileSlash,
        FileHost,
        PathStart,
        Path,
        OpaquePath,
        Query,
        Fragment,
    }
}
