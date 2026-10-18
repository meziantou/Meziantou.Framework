using System.Text;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Urls;

internal enum UrlParserState
{
    None,
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
    Hostname,
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

/// <summary>The basic URL parser: https://url.spec.whatwg.org/#concept-basic-url-parser</summary>
internal static class UrlParser
{
    private const int Eof = -1;

    /// <summary>Parses a URL. Returns null on failure.</summary>
    public static UrlRecord? Parse(string input, UrlRecord? baseUrl, WebEncodingId encoding = WebEncodingId.Utf8)
    {
        var url = new UrlRecord();
        return BasicParse(input, baseUrl, encoding, url, UrlParserState.None) == ParseResult.Failure ? null : url;
    }

    /// <summary>Runs the basic URL parser with a URL and a state override (used by the setters of the API).</summary>
    public static bool ParseWithStateOverride(string input, UrlRecord url, UrlParserState stateOverride)
    {
        return BasicParse(input, baseUrl: null, WebEncodingId.Utf8, url, stateOverride) != ParseResult.Failure;
    }

    private enum ParseResult
    {
        Success,
        Failure,
    }

    /// <summary>https://url.spec.whatwg.org/#windows-drive-letter</summary>
    public static bool IsWindowsDriveLetter(ReadOnlySpan<char> value) => value.Length == 2 && char.IsAsciiLetter(value[0]) && value[1] is ':' or '|';

    /// <summary>https://url.spec.whatwg.org/#normalized-windows-drive-letter</summary>
    public static bool IsNormalizedWindowsDriveLetter(ReadOnlySpan<char> value) => value.Length == 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';

    /// <summary>https://url.spec.whatwg.org/#start-with-a-windows-drive-letter</summary>
    private static bool StartsWithWindowsDriveLetter(in CodePoints input, int pointer)
    {
        if (input.Length - pointer < 2)
            return false;

        if (!(IsAsciiAlpha(input[pointer]) && input[pointer + 1] is ':' or '|'))
            return false;

        if (input.Length - pointer == 2)
            return true;

        return input[pointer + 2] is '/' or '\\' or '?' or '#';
    }

    private static bool IsAsciiAlpha(int c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsAsciiAlphanumeric(int c) => IsAsciiAlpha(c) || c is >= '0' and <= '9';

    private static bool IsSingleDotPathSegment(string value) => value == "." || value.Equals("%2e", StringComparison.OrdinalIgnoreCase);

    private static bool IsDoubleDotPathSegment(string value) => value is ".." || value.Equals(".%2e", StringComparison.OrdinalIgnoreCase) || value.Equals("%2e.", StringComparison.OrdinalIgnoreCase) || value.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The code points of the input of the parser, without the ASCII tabs and newlines and, when <paramref name="trimC0ControlOrSpace"/>
    /// is true, without the leading and trailing C0 controls and spaces.
    /// </summary>
    private static CodePoints ToCodePoints(string input, bool trimC0ControlOrSpace)
    {
        // Tab and newlines are C0 controls: trimming before removing them gives the same result
        var start = 0;
        var end = input.Length;
        if (trimC0ControlOrSpace)
        {
            while (start < end && input[start] <= 0x20)
            {
                start++;
            }

            while (end > start && input[end - 1] <= 0x20)
            {
                end--;
            }
        }

        // Most inputs have no surrogate, tab or newline: their code points are their characters
        var span = input.AsSpan(start, end - start);
        if (span.IndexOfAny('\t', '\n', '\r') < 0 && span.IndexOfAnyInRange('\uD800', '\uDFFF') < 0)
            return new CodePoints(input, start, end - start);

        var count = 0;
        foreach (var rune in span.EnumerateRunes())
        {
            if (rune.Value is not ('\t' or '\n' or '\r'))
            {
                count++;
            }
        }

        var result = new int[count];
        var index = 0;
        foreach (var rune in span.EnumerateRunes())
        {
            // Remove all ASCII tab or newline
            if (rune.Value is not ('\t' or '\n' or '\r'))
            {
                result[index++] = rune.Value;
            }
        }

        return new CodePoints(result);
    }

    /// <summary>The code points of the input: the characters of a substring when they are not surrogates, else an array.</summary>
    private readonly struct CodePoints
    {
        private readonly string? _string;
        private readonly int _start;
        private readonly int[]? _codePoints;

        public CodePoints(string value, int start, int length)
        {
            _string = value;
            _start = start;
            Length = length;
        }

        public CodePoints(int[] codePoints)
        {
            _codePoints = codePoints;
            Length = codePoints.Length;
        }

        public int Length { get; }

        public int this[int index] => _codePoints is not null ? _codePoints[index] : _string![_start + index];
    }

    private static ParseResult BasicParse(string inputString, UrlRecord? baseUrl, WebEncodingId encoding, UrlRecord url, UrlParserState stateOverride)
    {
        var buffer = StringBuilderCache.Acquire();
        try
        {
            return BasicParse(inputString, baseUrl, encoding, url, stateOverride, buffer);
        }
        finally
        {
            StringBuilderCache.Release(buffer);
        }
    }

    private static ParseResult BasicParse(string inputString, UrlRecord? baseUrl, WebEncodingId encoding, UrlRecord url, UrlParserState stateOverride, StringBuilder buffer)
    {
        // Leading/trailing C0 control or space are removed only when url is not given (i.e. no state override).
        // Tab and newlines are removed before trimming; since they are C0 controls this gives the same result.
        var input = ToCodePoints(inputString, trimC0ControlOrSpace: stateOverride == UrlParserState.None);
        var hasStateOverride = stateOverride != UrlParserState.None;
        var state = hasStateOverride ? stateOverride : UrlParserState.SchemeStart;
        encoding = WebEncodingRegistry.GetOutputEncoding(encoding);

        var atSignSeen = false;
        var insideBrackets = false;
        var passwordTokenSeen = false;
        var pointer = 0;

        // The opaque path and the fragment are built incrementally; they are flushed to url when leaving the state.
        StringBuilder? opaquePath = null;
        StringBuilder? fragment = null;

        int CodePointAt(int index) => index >= 0 && index < input.Length ? input[index] : Eof;
        bool RemainingStartsWith(char value) => CodePointAt(pointer + 1) == value;

        while (true)
        {
            var c = CodePointAt(pointer);
            switch (state)
            {
                case UrlParserState.SchemeStart:
                    if (IsAsciiAlpha(c))
                    {
                        buffer.Append(char.ToLowerInvariant((char)c));
                        state = UrlParserState.Scheme;
                    }
                    else if (!hasStateOverride)
                    {
                        state = UrlParserState.NoScheme;
                        pointer--;
                    }
                    else
                    {
                        return ParseResult.Failure;
                    }

                    break;

                case UrlParserState.Scheme:
                    if (IsAsciiAlphanumeric(c) || c is '+' or '-' or '.')
                    {
                        buffer.Append(char.ToLowerInvariant((char)c));
                    }
                    else if (c == ':')
                    {
                        var newScheme = buffer.ToString();
                        if (hasStateOverride)
                        {
                            if (url.IsSpecial && !UrlSchemes.IsSpecial(newScheme))
                                return ParseResult.Success;

                            if (!url.IsSpecial && UrlSchemes.IsSpecial(newScheme))
                                return ParseResult.Success;

                            if ((url.IncludesCredentials || url.Port >= 0) && newScheme == "file")
                                return ParseResult.Success;

                            if (url.Scheme == "file" && url.Host is { Kind: UrlHostKind.Empty })
                                return ParseResult.Success;
                        }

                        url.Scheme = newScheme;
                        if (hasStateOverride)
                        {
                            if (url.Port == UrlSchemes.GetDefaultPort(url.Scheme))
                            {
                                url.Port = -1;
                            }

                            return ParseResult.Success;
                        }

                        buffer.Clear();
                        if (url.Scheme == "file")
                        {
                            state = UrlParserState.File;
                        }
                        else if (url.IsSpecial && baseUrl is not null && baseUrl.Scheme == url.Scheme)
                        {
                            state = UrlParserState.SpecialRelativeOrAuthority;
                        }
                        else if (url.IsSpecial)
                        {
                            state = UrlParserState.SpecialAuthoritySlashes;
                        }
                        else if (RemainingStartsWith('/'))
                        {
                            state = UrlParserState.PathOrAuthority;
                            pointer++;
                        }
                        else
                        {
                            url.OpaquePath = "";
                            state = UrlParserState.OpaquePath;
                        }
                    }
                    else if (!hasStateOverride)
                    {
                        buffer.Clear();
                        state = UrlParserState.NoScheme;
                        pointer = -1;
                    }
                    else
                    {
                        return ParseResult.Failure;
                    }

                    break;

                case UrlParserState.NoScheme:
                    if (baseUrl is null || (baseUrl.HasOpaquePath && c != '#'))
                        return ParseResult.Failure;

                    if (baseUrl.HasOpaquePath && c == '#')
                    {
                        url.Scheme = baseUrl.Scheme;
                        url.OpaquePath = baseUrl.OpaquePath;
                        url.Path = [.. baseUrl.Path];
                        url.Query = baseUrl.Query;
                        url.Fragment = "";
                        state = UrlParserState.Fragment;
                    }
                    else if (baseUrl.Scheme != "file")
                    {
                        state = UrlParserState.Relative;
                        pointer--;
                    }
                    else
                    {
                        state = UrlParserState.File;
                        pointer--;
                    }

                    break;

                case UrlParserState.SpecialRelativeOrAuthority:
                    if (c == '/' && RemainingStartsWith('/'))
                    {
                        state = UrlParserState.SpecialAuthorityIgnoreSlashes;
                        pointer++;
                    }
                    else
                    {
                        state = UrlParserState.Relative;
                        pointer--;
                    }

                    break;

                case UrlParserState.PathOrAuthority:
                    if (c == '/')
                    {
                        state = UrlParserState.Authority;
                    }
                    else
                    {
                        state = UrlParserState.Path;
                        pointer--;
                    }

                    break;

                case UrlParserState.Relative:
                    url.Scheme = baseUrl!.Scheme;
                    if (c == '/')
                    {
                        state = UrlParserState.RelativeSlash;
                    }
                    else if (url.IsSpecial && c == '\\')
                    {
                        state = UrlParserState.RelativeSlash;
                    }
                    else
                    {
                        url.Username = baseUrl.Username;
                        url.Password = baseUrl.Password;
                        url.Host = baseUrl.Host;
                        url.Port = baseUrl.Port;
                        url.OpaquePath = baseUrl.OpaquePath;
                        url.Path = [.. baseUrl.Path];
                        url.Query = baseUrl.Query;
                        if (c == '?')
                        {
                            url.Query = "";
                            state = UrlParserState.Query;
                        }
                        else if (c == '#')
                        {
                            url.Fragment = "";
                            state = UrlParserState.Fragment;
                        }
                        else if (c != Eof)
                        {
                            url.Query = null;
                            url.ShortenPath();
                            state = UrlParserState.Path;
                            pointer--;
                        }
                    }

                    break;

                case UrlParserState.RelativeSlash:
                    if (url.IsSpecial && c is '/' or '\\')
                    {
                        state = UrlParserState.SpecialAuthorityIgnoreSlashes;
                    }
                    else if (c == '/')
                    {
                        state = UrlParserState.Authority;
                    }
                    else
                    {
                        url.Username = baseUrl!.Username;
                        url.Password = baseUrl.Password;
                        url.Host = baseUrl.Host;
                        url.Port = baseUrl.Port;
                        state = UrlParserState.Path;
                        pointer--;
                    }

                    break;

                case UrlParserState.SpecialAuthoritySlashes:
                    if (c == '/' && RemainingStartsWith('/'))
                    {
                        state = UrlParserState.SpecialAuthorityIgnoreSlashes;
                        pointer++;
                    }
                    else
                    {
                        state = UrlParserState.SpecialAuthorityIgnoreSlashes;
                        pointer--;
                    }

                    break;

                case UrlParserState.SpecialAuthorityIgnoreSlashes:
                    if (c is not '/' and not '\\')
                    {
                        state = UrlParserState.Authority;
                        pointer--;
                    }

                    break;

                case UrlParserState.Authority:
                    if (c == '@')
                    {
                        if (atSignSeen)
                        {
                            buffer.Insert(0, "%40");
                        }

                        atSignSeen = true;
                        var usernameBuilder = new StringBuilder(url.Username);
                        var passwordBuilder = new StringBuilder(url.Password);
                        foreach (var rune in buffer.ToString().EnumerateRunes())
                        {
                            if (rune.Value == ':' && !passwordTokenSeen)
                            {
                                passwordTokenSeen = true;
                                continue;
                            }

                            PercentEncoding.AppendUtf8PercentEncoded(passwordTokenSeen ? passwordBuilder : usernameBuilder, rune.Value, PercentEncodeSet.Userinfo);
                        }

                        url.Username = usernameBuilder.ToString();
                        url.Password = passwordBuilder.ToString();
                        buffer.Clear();
                    }
                    else if (c is Eof or '/' or '?' or '#' || (url.IsSpecial && c == '\\'))
                    {
                        if (atSignSeen && buffer.Length == 0)
                            return ParseResult.Failure;

                        pointer -= CodePointLength(buffer) + 1;
                        buffer.Clear();
                        state = UrlParserState.Host;
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                    }

                    break;

                case UrlParserState.Host:
                case UrlParserState.Hostname:
                    if (hasStateOverride && url.Scheme == "file")
                    {
                        pointer--;
                        state = UrlParserState.FileHost;
                    }
                    else if (c == ':' && !insideBrackets)
                    {
                        if (buffer.Length == 0)
                            return ParseResult.Failure;

                        if (stateOverride == UrlParserState.Hostname)
                            return ParseResult.Failure;

                        var host = HostParser.Parse(buffer.ToString(), isOpaque: !url.IsSpecial);
                        if (host is null)
                            return ParseResult.Failure;

                        url.Host = host;
                        buffer.Clear();
                        state = UrlParserState.Port;
                    }
                    else if (c is Eof or '/' or '?' or '#' || (url.IsSpecial && c == '\\'))
                    {
                        pointer--;
                        if (url.IsSpecial && buffer.Length == 0)
                            return ParseResult.Failure;

                        if (hasStateOverride && buffer.Length == 0 && (url.IncludesCredentials || url.Port >= 0))
                            return ParseResult.Failure;

                        var host = HostParser.Parse(buffer.ToString(), isOpaque: !url.IsSpecial);
                        if (host is null)
                            return ParseResult.Failure;

                        url.Host = host;
                        buffer.Clear();
                        state = UrlParserState.PathStart;
                        if (hasStateOverride)
                            return ParseResult.Success;
                    }
                    else
                    {
                        if (c == '[')
                        {
                            insideBrackets = true;
                        }

                        if (c == ']')
                        {
                            insideBrackets = false;
                        }

                        AppendCodePoint(buffer, c);
                    }

                    break;

                case UrlParserState.Port:
                    if (c is >= '0' and <= '9')
                    {
                        buffer.Append((char)c);
                    }
                    else if (c is Eof or '/' or '?' or '#' || (url.IsSpecial && c == '\\') || hasStateOverride)
                    {
                        if (buffer.Length > 0)
                        {
                            // Parse without overflow: anything larger than 65535 is a failure
                            var port = 0;
                            foreach (var chunk in buffer.GetChunks())
                            {
                                foreach (var digit in chunk.Span)
                                {
                                    port = (port * 10) + (digit - '0');
                                    if (port > 65535)
                                        return ParseResult.Failure;
                                }
                            }

                            url.Port = port == UrlSchemes.GetDefaultPort(url.Scheme) ? -1 : port;
                            buffer.Clear();
                            if (hasStateOverride)
                                return ParseResult.Success;
                        }

                        if (hasStateOverride)
                            return ParseResult.Failure;

                        state = UrlParserState.PathStart;
                        pointer--;
                    }
                    else
                    {
                        return ParseResult.Failure;
                    }

                    break;

                case UrlParserState.File:
                    url.Scheme = "file";
                    url.Host = UrlHost.EmptyHost;
                    if (c is '/' or '\\')
                    {
                        state = UrlParserState.FileSlash;
                    }
                    else if (baseUrl is not null && baseUrl.Scheme == "file")
                    {
                        url.Host = baseUrl.Host;
                        url.Path = [.. baseUrl.Path];
                        url.Query = baseUrl.Query;
                        if (c == '?')
                        {
                            url.Query = "";
                            state = UrlParserState.Query;
                        }
                        else if (c == '#')
                        {
                            url.Fragment = "";
                            state = UrlParserState.Fragment;
                        }
                        else if (c != Eof)
                        {
                            url.Query = null;
                            if (!StartsWithWindowsDriveLetter(input, pointer))
                            {
                                url.ShortenPath();
                            }
                            else
                            {
                                url.Path = [];
                            }

                            state = UrlParserState.Path;
                            pointer--;
                        }
                    }
                    else
                    {
                        state = UrlParserState.Path;
                        pointer--;
                    }

                    break;

                case UrlParserState.FileSlash:
                    if (c is '/' or '\\')
                    {
                        state = UrlParserState.FileHost;
                    }
                    else
                    {
                        if (baseUrl is not null && baseUrl.Scheme == "file")
                        {
                            url.Host = baseUrl.Host;
                            if (!StartsWithWindowsDriveLetter(input, pointer) && baseUrl.Path.Count > 0 && IsNormalizedWindowsDriveLetter(baseUrl.Path[0]))
                            {
                                url.Path.Add(baseUrl.Path[0]);
                            }
                        }

                        state = UrlParserState.Path;
                        pointer--;
                    }

                    break;

                case UrlParserState.FileHost:
                    if (c is Eof or '/' or '\\' or '?' or '#')
                    {
                        pointer--;
                        if (!hasStateOverride && IsWindowsDriveLetter(buffer.ToString()))
                        {
                            // The buffer is not reset here and instead used in the path state
                            state = UrlParserState.Path;
                        }
                        else if (buffer.Length == 0)
                        {
                            url.Host = UrlHost.EmptyHost;
                            if (hasStateOverride)
                                return ParseResult.Success;

                            state = UrlParserState.PathStart;
                        }
                        else
                        {
                            var host = HostParser.Parse(buffer.ToString(), isOpaque: !url.IsSpecial);
                            if (host is null)
                                return ParseResult.Failure;

                            if (host.Value.Kind == UrlHostKind.Domain && host.Value.Serialized == "localhost")
                            {
                                host = UrlHost.EmptyHost;
                            }

                            url.Host = host;
                            if (hasStateOverride)
                                return ParseResult.Success;

                            buffer.Clear();
                            state = UrlParserState.PathStart;
                        }
                    }
                    else
                    {
                        AppendCodePoint(buffer, c);
                    }

                    break;

                case UrlParserState.PathStart:
                    if (url.IsSpecial)
                    {
                        state = UrlParserState.Path;
                        if (c is not '/' and not '\\')
                        {
                            pointer--;
                        }
                    }
                    else if (!hasStateOverride && c == '?')
                    {
                        url.Query = "";
                        state = UrlParserState.Query;
                    }
                    else if (!hasStateOverride && c == '#')
                    {
                        url.Fragment = "";
                        state = UrlParserState.Fragment;
                    }
                    else if (c != Eof)
                    {
                        state = UrlParserState.Path;
                        if (c != '/')
                        {
                            pointer--;
                        }
                    }
                    else if (hasStateOverride && url.Host is null)
                    {
                        url.Path.Add("");
                    }

                    break;

                case UrlParserState.Path:
                    if (c is Eof or '/' || (url.IsSpecial && c == '\\') || (!hasStateOverride && c is '?' or '#'))
                    {
                        var isSlash = c == '/' || (url.IsSpecial && c == '\\');
                        var segment = buffer.ToString();
                        if (IsDoubleDotPathSegment(segment))
                        {
                            url.ShortenPath();
                            if (!isSlash)
                            {
                                url.Path.Add("");
                            }
                        }
                        else if (IsSingleDotPathSegment(segment) && !isSlash)
                        {
                            url.Path.Add("");
                        }
                        else if (!IsSingleDotPathSegment(segment))
                        {
                            if (url.Scheme == "file" && url.Path.Count == 0 && IsWindowsDriveLetter(segment))
                            {
                                segment = segment[0] + ":";
                            }

                            url.Path.Add(segment);
                        }

                        buffer.Clear();
                        if (c == '?')
                        {
                            url.Query = "";
                            state = UrlParserState.Query;
                        }

                        if (c == '#')
                        {
                            url.Fragment = "";
                            state = UrlParserState.Fragment;
                        }
                    }
                    else
                    {
                        PercentEncoding.AppendUtf8PercentEncoded(buffer, c, PercentEncodeSet.Path);
                    }

                    break;

                case UrlParserState.OpaquePath:
                    opaquePath ??= new StringBuilder(url.OpaquePath, (url.OpaquePath?.Length ?? 0) + input.Length - pointer);
                    if (c is '?' or '#')
                    {
                        url.OpaquePath = opaquePath.ToString();
                        opaquePath = null;
                        if (c == '?')
                        {
                            url.Query = "";
                            state = UrlParserState.Query;
                        }
                        else
                        {
                            url.Fragment = "";
                            state = UrlParserState.Fragment;
                        }
                    }
                    else if (c == ' ')
                    {
                        var next = CodePointAt(pointer + 1);
                        opaquePath.Append(next is '?' or '#' ? "%20" : " ");
                    }
                    else if (c != Eof)
                    {
                        PercentEncoding.AppendUtf8PercentEncoded(opaquePath, c, PercentEncodeSet.C0Control);
                    }

                    break;

                case UrlParserState.Query:
                    if (encoding != WebEncodingId.Utf8 && (!url.IsSpecial || url.Scheme is "ws" or "wss"))
                    {
                        encoding = WebEncodingId.Utf8;
                    }

                    if ((!hasStateOverride && c == '#') || c == Eof)
                    {
                        var set = url.IsSpecial ? PercentEncodeSet.SpecialQuery : PercentEncodeSet.Query;
                        var query = new StringBuilder(url.Query, (url.Query?.Length ?? 0) + buffer.Length);
                        PercentEncoding.AppendPercentEncodeAfterEncoding(query, encoding, buffer.ToString(), set);
                        url.Query = query.ToString();
                        buffer.Clear();
                        if (c == '#')
                        {
                            url.Fragment = "";
                            state = UrlParserState.Fragment;
                        }
                    }
                    else if (c != Eof)
                    {
                        AppendCodePoint(buffer, c);
                    }

                    break;

                case UrlParserState.Fragment:
                    fragment ??= new StringBuilder(url.Fragment, (url.Fragment?.Length ?? 0) + input.Length - pointer);
                    if (c != Eof)
                    {
                        PercentEncoding.AppendUtf8PercentEncoded(fragment, c, PercentEncodeSet.Fragment);
                    }

                    break;

                default:
                    throw new InvalidOperationException("Invalid state");
            }

            if (pointer >= input.Length)
                break;

            pointer++;
        }

        if (opaquePath is not null)
        {
            url.OpaquePath = opaquePath.ToString();
        }

        if (fragment is not null)
        {
            url.Fragment = fragment.ToString();
        }

        return ParseResult.Success;
    }

    private static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        if (codePoint < 0x10000)
        {
            builder.Append((char)codePoint);
        }
        else
        {
            builder.Append(new Rune(codePoint));
        }
    }

    private static int CodePointLength(StringBuilder builder)
    {
        var count = 0;
        foreach (var chunk in builder.GetChunks())
        {
            foreach (var c in chunk.Span)
            {
                if (!char.IsLowSurrogate(c))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
