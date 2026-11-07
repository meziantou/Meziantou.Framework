using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.UrlPatternInternal;
using WebUrl = Meziantou.Framework.WebPlatform.Dom.Url;

namespace UriFuzz;

public static partial class UriFuzzTargets
{
    private static readonly PercentEncodeSet[] PercentEncodeSets = Enum.GetValues<PercentEncodeSet>();

    // A set of the first column is a subset of the sets of the second column
    private static readonly (PercentEncodeSet Subset, PercentEncodeSet Superset)[] PercentEncodeSetInclusions =
    [
        (PercentEncodeSet.C0Control, PercentEncodeSet.Fragment),
        (PercentEncodeSet.C0Control, PercentEncodeSet.Query),
        (PercentEncodeSet.Query, PercentEncodeSet.SpecialQuery),
        (PercentEncodeSet.Query, PercentEncodeSet.Path),
        (PercentEncodeSet.Fragment, PercentEncodeSet.Path),
        (PercentEncodeSet.Path, PercentEncodeSet.UserInfo),
    ];

    private static readonly string?[] PortProtocols = [null, "", "http", "https", "ws", "wss", "ftp", "file", "foo"];

    /// <summary>
    /// Parses a URL, with or without a base URL, with the WHATWG URL parser of UrlPattern (<see cref="UrlRecord"/>). The
    /// parser never throws, and agrees with WebPlatform's URL parser on whether the URL is valid and on its components
    /// (except for the hosts that depend on IDNA). The serialization of the URL parses into the same URL; the
    /// match-everything pattern matches the URL and reports its components; and for a special scheme, the pattern made of the
    /// escaped components of the URL matches it (the pattern side and the URL side are canonicalized the same way).
    /// </summary>
    private static void UrlParserTarget(ReadOnlySpan<byte> data)
    {
        var hasBaseUrl = (FuzzInput.ReadByte(ref data) & 1) != 0;
        var baseUrl = hasBaseUrl ? FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator)) : null;
        var input = FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator));

        UrlRecord? baseRecord = null;
        var isValid = (baseUrl is null || UrlRecord.TryParse(baseUrl, baseUrl: null, out baseRecord)) & UrlRecord.TryParse(input, baseRecord, out var url);
        url = isValid ? url : null;

        // UrlPattern treats an empty base URL as no base URL
        if (baseUrl is not "")
        {
            var everything = MatchAll.Match(input, baseUrl);
            FuzzAssert.Equal(isValid, everything is not null, "The URL parser and the match-everything pattern disagree");
            if (url is not null)
            {
                var components = GetComponents(url);
                for (var i = 0; i < ComponentNames.Length; i++)
                {
                    FuzzAssert.Equal(components[i], GetComponent(everything!, i).Input, $"The match-everything pattern reports another {ComponentNames[i]}");
                }
            }
        }

        var webUrl = WebUrl.Parse(input, baseUrl);
        var isSensitive = IsSensitiveUrl(input) || IsSensitiveUrl(baseUrl);
        if (isValid != (webUrl is not null) && !isSensitive)
        {
            FuzzAssert.Fail($"Only {(isValid ? "UrlPattern" : "WebPlatform")} parses the URL");
        }

        if (url is null)
            return;

        var href = Serialize(url);
        FuzzAssert.True(UrlRecord.TryParse(href, baseUrl: null, out var reparsed), $"The serialization of the URL is not a valid URL: {FuzzAssert.Format(href)}");
        FuzzAssert.Equal(href, Serialize(reparsed), "The serialization of the URL is not a fixed point");
        FuzzAssert.Equal(string.Join("\n", GetComponents(url)), string.Join("\n", GetComponents(reparsed)), $"The serialization {FuzzAssert.Format(href)} parses into other components");

        if (webUrl is not null && (!isSensitive || webUrl.Hostname == (url.Host ?? "")))
        {
            FuzzAssert.Equal(webUrl.Href, href, "WebPlatform serializes the URL differently");
            FuzzAssert.Equal(webUrl.Protocol, url.Scheme + ":", "WebPlatform gives another protocol");
            FuzzAssert.Equal(webUrl.Username, url.Username, "WebPlatform gives another username");
            FuzzAssert.Equal(webUrl.Password, url.Password, "WebPlatform gives another password");
            FuzzAssert.Equal(webUrl.Hostname, url.Host ?? "", "WebPlatform gives another hostname");
            FuzzAssert.Equal(webUrl.Port, url.Port ?? "", "WebPlatform gives another port");
            FuzzAssert.Equal(webUrl.Pathname, url.SerializePath(), "WebPlatform gives another pathname");
            FuzzAssert.Equal(webUrl.Search, string.IsNullOrEmpty(url.Query) ? "" : "?" + url.Query, "WebPlatform gives another search");
            FuzzAssert.Equal(webUrl.Hash, string.IsNullOrEmpty(url.Fragment) ? "" : "#" + url.Fragment, "WebPlatform gives another hash");
        }

        if (SpecialSchemes.Contains(url.Scheme))
        {
            var components = GetComponents(url);
            var exact = UrlPattern.Create(new UrlPatternInit
            {
                Protocol = PatternParser.EscapePatternString(components[0]),
                Username = PatternParser.EscapePatternString(components[1]),
                Password = PatternParser.EscapePatternString(components[2]),
                Hostname = PatternParser.EscapePatternString(components[3]),
                Port = PatternParser.EscapePatternString(components[4]),
                Pathname = PatternParser.EscapePatternString(components[5]),
                Search = "?" + PatternParser.EscapePatternString(components[6]),
                Hash = "#" + PatternParser.EscapePatternString(components[7]),
            });
            FuzzAssert.True(exact.IsMatch(href), $"The pattern made of the components of the URL does not match it: {FormatPattern(exact)}");
        }
    }

    /// <summary>
    /// Runs one of the canonicalization functions of the URL patterns (<see cref="UrlCanonicalizer"/>), the percent-encoding
    /// (<see cref="PercentEncoding"/>), or the host parser (<see cref="HostParser"/>). An invalid value only throws
    /// <see cref="UrlPatternException"/>, and every canonicalization is idempotent. Percent-encoding is idempotent, leaves no
    /// code point of the set, encodes with a superset of a set the same way as with the superset alone, and percent-decoding
    /// the result gives the percent-decoding of the value. A parsed host parses into itself.
    /// </summary>
    private static void CanonicalizerTarget(ReadOnlySpan<byte> data)
    {
        var function = FuzzInput.ReadByte(ref data);
        var option = FuzzInput.ReadByte(ref data);
        var value = FuzzText.Decode(data);
        switch (function % 13)
        {
            case 0:
                CheckIdempotent(UrlCanonicalizer.CanonicalizeProtocol, value, "protocol");
                break;
            case 1:
                CheckIdempotent(UrlCanonicalizer.CanonicalizeUsername, value, "username");
                break;
            case 2:
                CheckIdempotent(UrlCanonicalizer.CanonicalizePassword, value, "password");
                break;
            case 3:
                CheckIdempotent(UrlCanonicalizer.CanonicalizeHostname, value, "hostname");
                break;
            case 4:
                CheckIdempotent(UrlCanonicalizer.CanonicalizeIPv6Hostname, value, "IPv6 hostname");
                break;
            case 5:
                var protocol = FuzzInput.Pick(PortProtocols, option);
                CheckIdempotent(port => UrlCanonicalizer.CanonicalizePort(port, protocol), value, "port");
                break;
            case 6:
                CheckIdempotent(UrlCanonicalizer.CanonicalizePathname, value, "pathname");
                break;
            case 7:
                CheckIdempotent(UrlCanonicalizer.CanonicalizeOpaquePathname, value, "opaque pathname");
                break;
            case 8:
                CheckIdempotent(UrlCanonicalizer.CanonicalizeSearch, value, "search");
                break;
            case 9:
                CheckIdempotent(UrlCanonicalizer.CanonicalizeHash, value, "hash");
                break;
            case 10:
                CheckPercentEncoding(value, FuzzInput.Pick(PercentEncodeSets, option));
                break;
            case 11:
                CheckPercentEncodingInclusion(value, FuzzInput.Pick(PercentEncodeSetInclusions, option));
                break;
            default:
                CheckHost(value, isOpaque: (option & 1) != 0);
                break;
        }
    }

    private static void CheckIdempotent(Func<string, string> canonicalize, string value, string component)
    {
        string canonical;
        try
        {
            canonical = canonicalize(value);
        }
        catch (UrlPatternException)
        {
            return;
        }

        string again;
        try
        {
            again = canonicalize(canonical);
        }
        catch (UrlPatternException ex)
        {
            throw new FuzzAssertionException($"The canonical {component} {FuzzAssert.Format(canonical)} is not valid", ex);
        }

        FuzzAssert.Equal(canonical, again, $"Canonicalizing the {component} is not idempotent");
    }

    private static void CheckPercentEncoding(string value, PercentEncodeSet set)
    {
        var encoded = PercentEncoding.Encode(value, set);
        FuzzAssert.Equal(encoded, PercentEncoding.Encode(encoded, set), $"Percent-encoding with the {set} set is not idempotent");
        foreach (var c in encoded)
        {
            var single = c.ToString();
            FuzzAssert.True(c is '%' || PercentEncoding.Encode(single, set) == single, $"Percent-encoding with the {set} set leaves {FuzzAssert.Format(single)}");
        }

        // An unpaired surrogate becomes U+FFFD when it is encoded, or decoded along with an escape sequence
        FuzzAssert.Equal(ToScalarValueString(PercentEncoding.Decode(value)), PercentEncoding.Decode(encoded), $"Percent-encoding with the {set} set changes the percent-decoded value");
        if (!value.Contains('%', StringComparison.Ordinal))
        {
            FuzzAssert.Equal(value, PercentEncoding.Decode(value), "Percent-decoding a value without '%' changes it");
        }
    }

    private static void CheckPercentEncodingInclusion(string value, (PercentEncodeSet Subset, PercentEncodeSet Superset) sets)
    {
        var expected = PercentEncoding.Encode(value, sets.Superset);
        FuzzAssert.Equal(expected, PercentEncoding.Encode(PercentEncoding.Encode(value, sets.Subset), sets.Superset), $"Percent-encoding with the {sets.Subset} set changes the percent-encoding with the {sets.Superset} set");
    }

    private static void CheckHost(string value, bool isOpaque)
    {
        if (!HostParser.TryParse(value, isOpaque, out var host))
            return;

        FuzzAssert.True(HostParser.TryParse(host, isOpaque, out var again), $"The parsed host {FuzzAssert.Format(host)} is not valid");
        FuzzAssert.Equal(host, again, "Parsing a host is not idempotent");
        if (!isOpaque)
        {
            FuzzAssert.True(System.Text.Ascii.IsValid(host), $"The parsed domain {FuzzAssert.Format(host)} is not ASCII");
            FuzzAssert.Equal(host.ToLowerInvariant(), host, "The parsed domain is not lowercase");
        }
    }

    private static string[] GetComponents(UrlRecord url) => [url.Scheme, url.Username, url.Password, url.Host ?? "", url.Port ?? "", url.SerializePath(), url.Query ?? "", url.Fragment ?? ""];

    /// <summary>Serializes a URL record.</summary>
    /// <remarks>
    /// <see href="https://url.spec.whatwg.org/#concept-url-serializer">URL Standard - URL serializer</see>
    /// </remarks>
    private static string Serialize(UrlRecord url)
    {
        var builder = new StringBuilder();
        builder.Append(url.Scheme).Append(':');
        if (url.Host is not null)
        {
            builder.Append("//");
            if (url.Username.Length > 0 || url.Password.Length > 0)
            {
                builder.Append(url.Username);
                if (url.Password.Length > 0)
                {
                    builder.Append(':').Append(url.Password);
                }

                builder.Append('@');
            }

            builder.Append(url.Host);
            if (url.Port is not null)
            {
                builder.Append(':').Append(url.Port);
            }
        }
        else if (!url.HasOpaquePath && url.Path.Count > 1 && url.Path[0].Length == 0)
        {
            builder.Append("/.");
        }

        builder.Append(url.SerializePath());
        if (url.Query is not null)
        {
            builder.Append('?').Append(url.Query);
        }

        if (url.Fragment is not null)
        {
            builder.Append('#').Append(url.Fragment);
        }

        return builder.ToString();
    }

    private static IEnumerable<FuzzSeed> GetUrlParserSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        yield return new FuzzSeed("absolute", Encode([0], "https://user:pass@EXAMPLE.com:8080/a/./b/../c?d=e#f"));
        yield return new FuzzSeed("relative", Encode([1], "https://example.com/a/b", "../c d?e#f"));
        yield return new FuzzSeed("ipv4", Encode([0], "http://0x7f.1/"));
        yield return new FuzzSeed("ipv6", Encode([0], "http://[::ffff:1.2.3.4]:80/"));
        yield return new FuzzSeed("idn", Encode([0], "https://caf\u00E9.example/\u00E9?\u00E9#\u00E9"));
        yield return new FuzzSeed("opaque", Encode([0], "mailto:user@example.com?subject=a b"));
        yield return new FuzzSeed("file", Encode([0], "file:///C:/a/b"));

        foreach (var literal in GetStringLiterals("UrlPatternTests.cs"))
        {
            yield return new FuzzSeed("literal", Encode([0], literal));
        }

        using var document = FuzzResources.ReadJson(typeof(UriFuzzTargets).Assembly, "urltestdata.json");
        var index = 0;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            index++;
            if (entry.ValueKind is not JsonValueKind.Object)
                continue;

            string? input;
            string? baseUrl;
            try
            {
                input = entry.GetProperty("input").GetString();
                baseUrl = entry.TryGetProperty("base", out var baseElement) ? baseElement.GetString() : null;
            }
            catch (InvalidOperationException)
            {
                // JsonElement.GetString refuses the unpaired surrogates of a few cases
                continue;
            }

            if (input is null)
                continue;

            var name = "wpt-" + index.ToString(CultureInfo.InvariantCulture);
            yield return baseUrl is null ? new FuzzSeed(name, Encode([0], input)) : new FuzzSeed(name, Encode([1], baseUrl, input));
        }
    }

    private static IEnumerable<FuzzSeed> GetCanonicalizerSeeds()
    {
        string[] values =
        [
            "", "https", "HTTP", "a+b-c.d", "1http", "us er", "p@ss:w/rd", "EXAMPLE.com", "caf\u00E9.example", "xn--caf-dma.example", "a%2Eb", "%41.com", "0x7f.1", "1.2.3.4", "foo.1", "[::1]", "[1:2::3]", "[::FFFF:1.2.3.4]",
            "80", "0443", "65535", "65536", "8080x", "/a/b/../c", "a/../..", "./a", "/%2e/a/%2E%2E/b", "a b", "a b?c", "?q=1&r='", "#a b`c", "\u00E9\t\n\r", "%", "%2", "%zz", "%C3%A9", "%ED%A0%80", "\uD800",
        ];

        for (var function = 0; function < 13; function++)
        {
            for (var option = 0; option < 2; option++)
            {
                foreach (var value in values)
                {
                    yield return FuzzSeed.FromText($"{function}-{option}", [(byte)function, (byte)option], value);
                }
            }
        }

        yield return FuzzSeed.FromText("port-https", [5, 3], "443");
    }
}
