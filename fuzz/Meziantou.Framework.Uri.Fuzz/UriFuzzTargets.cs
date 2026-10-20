using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace UriFuzz;

/// <summary>
/// The targets of Meziantou.Framework.Uri: the URL Pattern API, the URL parser and the canonicalization it is built on,
/// the Public Suffix List, and the query string helpers. The differential targets compare the URL parser and the URL
/// patterns with WebPlatform, which implements the same two WHATWG specifications.
/// </summary>
/// <remarks>
/// The namespace is not under <c>Meziantou.Framework</c>, so that no <c>Uri</c> namespace shadows <see cref="System.Uri"/>.
/// The texts of an input are separated by <see cref="Separator"/>, a byte that UTF-8 never uses, so that a text can hold any
/// character (<see cref="FuzzText"/>).
/// </remarks>
public static partial class UriFuzzTargets
{
    private const byte Separator = 0xFF;

    private static readonly string[] UrlTokens =
    [
        "http:", "https:", "ws:", "wss:", "ftp:", "file:", "data:", "blob:", "mailto:", "javascript:", "foo:", "//", "///", "\\\\", "/", "\\", ":", "@", "?", "#", "[", "]", "%", "%2e", "%2E", "%2e%2e", ".", "..", "./", "../", "/.", "/..",
        "%00", "%20", "%25", "%41", "%7F", "%C3%A9", "%EF%BF%BD", "%ff", "localhost", "example.com", "EXAMPLE.COM", "xn--", "xn--nxasmq6b", "0x", "0x7f", "127.1", "1.2.3.4", "0.0.0.0", "4294967295", "[::]", "[::1]", "[1:2:3:4:5:6:7:8]",
        "::ffff:", "::1.2.3.4", ":80", ":443", ":8080", ":65535", ":65536", "user:pass@", "\t", "\n", "\r", " ", "\u00E9", "\u00DF", "\u0130", "\u3002", "\uFF0E", "\uFFFD", "\u200D", "\U0001F600",
    ];

    private static readonly string[] UrlPatternTokens =
    [
        .. UrlTokens,
        "*", ":id", ":name", ":_", ":$", ":\u00E9", "(", ")", "(.*)", "([^/]+?)", "(\\d+)", "(?:", "(?<", "{", "}", "{/", "}?", "}*", "}+", "?", "+", "\\", "\\:", "\\*", "\\{", "\\(", "\\/", "/*", "/:id", "/:id?", "/:id*", "/:id+",
        "/(.*)", "/*?", "{:id}", "{.:ext}", "[", "]", "|", "^", "$", ".", "-", "a", "A",
    ];

    private static readonly string[] DomainTokens =
    [
        ".", "..", "-", "_", "*", "!", "com", "co.uk", "uk", "example", "www", "github.io", "blogspot.com", "jp", "kawasaki.jp", "city.kawasaki.jp", "ck", "www.ck", "us", "ak.us", "k12.ak.us", "xn--", "xn--85x722f", "xn--55qx5d.cn",
        "\u98DF\u72EE", "\u4E2D\u56FD", "\u516C\u53F8", "\u00E9", "\u00DF", "\u0130", "\u3002", "\uFF0E", "\u200D", "%", "localhost", "1.2.3.4", "[::1]", "COM", "WWW",
    ];

    private static readonly string[] QueryStringTokens =
    [
        "?", "&", "=", "#", "+", "%", "%20", "%2B", "%26", "%3D", "%23", "%3F", "%25", "%C3%A9", "%ED%A0%80", "%ff", "%", " ", "\t", "\n", "\u00A0", "\u2028", "\u00E9", "\uD800", "\U0001F600", "a=1", "b=2", "a", "http://www.example.com/", "?a=1&b=2#hash",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("uri-urlpattern", UrlPatternTarget) { Seeds = GetUrlPatternSeeds, Dictionary = UrlPatternTokens },
        new("uri-urlpattern-webplatform", UrlPatternWebPlatformTarget) { Seeds = GetUrlPatternSeeds, Dictionary = UrlPatternTokens },
        new("uri-url-parser", UrlParserTarget) { Seeds = GetUrlParserSeeds, Dictionary = UrlTokens },
        new("uri-canonicalizer", CanonicalizerTarget) { Seeds = GetCanonicalizerSeeds, Dictionary = UrlTokens },
        new("uri-public-suffix", PublicSuffixTarget) { Seeds = GetPublicSuffixSeeds, Dictionary = DomainTokens },
        new("uri-query-string", QueryStringTarget) { Seeds = GetQueryStringSeeds, Dictionary = QueryStringTokens },
    ];

    private static List<string> ReadTexts(ReadOnlySpan<byte> data, int max)
    {
        var result = new List<string>();
        while (!data.IsEmpty && result.Count < max)
        {
            result.Add(FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator)));
        }

        return result;
    }

    private static byte[] Encode(ReadOnlySpan<byte> prefix, params ReadOnlySpan<string> texts)
    {
        var result = new List<byte>();
        result.AddRange(prefix);
        foreach (var text in texts)
        {
            result.AddRange(FuzzText.Encode(text));
            result.Add(Separator);
        }

        return [.. result];
    }

    /// <summary>Replaces the unpaired surrogates with U+FFFD, as the conversion of a JavaScript string to a USVString does.</summary>
    private static string ToScalarValueString(string value)
    {
        if (!value.AsSpan().ContainsAnyInRange('\uD800', '\uDFFF'))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
            }
            else
            {
                builder.Append(char.IsSurrogate(c) ? '\uFFFD' : c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Gets the regular string literals of a C# source file embedded as a seed.</summary>
    private static IEnumerable<string> GetStringLiterals(string resourceName)
    {
        var source = FuzzResources.ReadText(typeof(UriFuzzTargets).Assembly, resourceName);
        foreach (Match match in StringLiteralRegex().Matches(source))
        {
            if (TryUnescape(match.Groups["value"].Value) is { } value)
                yield return value;
        }
    }

    private static string? TryUnescape(string literal)
    {
        var builder = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] is not '\\')
            {
                builder.Append(literal[i]);
                continue;
            }

            i++;
            switch (literal[i])
            {
                case '\\' or '"' or '\'':
                    builder.Append(literal[i]);
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 'u' when i + 4 < literal.Length && int.TryParse(literal.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    i += 4;
                    break;
                default:
                    return null;
            }
        }

        return builder.ToString();
    }

    // A regular (not verbatim, not interpolated, not raw) string literal on one line
    [GeneratedRegex("""(?<![@$"\\])"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();
}
