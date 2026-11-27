using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The parser of the Connection-Allowlist header (https://w3c.github.io/connection-allowlists/), and its embedded enforcement
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the connection allowlist target: the structured field syntax, the parameters and URL pattern syntax (a property: see <see cref="IntegrityTokens"/>).</summary>
    private static string[] ConnectionAllowlistTokens =>
    [
        "(", ")", ",", ";", "=", "\"", "\\", " ", "\t", "\n", "?1", "*", ":", "{", "}", "/", "//",
        "response-origin", "report-to", "redirects", "webrtc", "allow", "block",
        "https://", "http://", "wss://", "*://", "https://*.", ":*", ":id", "(\\d+)", "example.com", "/*", "?*", "#*",
    ];

    /// <summary>
    /// Parses a <c>Connection-Allowlist</c> header, and checks the properties of the allowlist: the local URLs are allowed,
    /// the response origin is allowed by <c>(response-origin)</c>, the allowlist serialized again as a structured field has
    /// the same patterns and parameters, an allowlist satisfies itself (as a required allowlist and as the
    /// <c>connectionallowlist</c> attribute of an iframe), and the validity of the attribute matches the parse of the value.
    /// </summary>
    public static void ConnectionAllowlistHeader(string input)
    {
        var responseUrl = UrlParser.Parse("https://example.com/page", baseUrl: null)!;
        var headers = new List<KeyValuePair<string, string>> { new("Connection-Allowlist", input), new("Connection-Allowlist-Report-Only", input) };
        var allowlists = ConnectionAllowlist.FromHeaders(headers, responseUrl);
        var allowlist = ConnectionAllowlist.Parse(input, responseUrl, ConnectionAllowlistDisposition.Enforce);
        FuzzAssert.Equal(allowlist is null ? 0 : 2, allowlists.Count, $"The number of allowlists of the headers {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(StructuredFields.ParseList(input) is [{ InnerList: not null }, ..], allowlist is not null, $"Whether {FuzzAssert.Format(input)} is an allowlist");

        // The validity of the connectionallowlist attribute without a required allowlist is the parse of its value
        FuzzAssert.Equal(input.Length is > 0 and <= 4096 && allowlist is not null, ConnectionAllowlist.IsValidAttributeValue(input, parentRequired: null), $"The validity of the attribute {FuzzAssert.Format(input)}");
        if (allowlist is null)
        {
            FuzzAssert.False(ConnectionAllowlist.ShouldBlockByEmbeddedEnforcement(input, [], responseUrl, "https://parent.example"), $"The required allowlist {FuzzAssert.Format(input)} that is not valid blocks a response");
            return;
        }

        FuzzAssert.Equal(allowlist.Patterns.Count, allowlist.SerializedPatterns.Count, $"The patterns of {FuzzAssert.Format(input)}");
        foreach (var url in new[] { "about:blank", "data:text/plain,a", "blob:https://other.example/1" })
        {
            FuzzAssert.True(allowlist.Matches(UrlParser.Parse(url, baseUrl: null)!), $"The allowlist {FuzzAssert.Format(input)} does not allow the local URL {url}");
        }

        if (allowlist.SerializedPatterns.Contains("https://example.com"))
        {
            FuzzAssert.True(allowlist.Matches(UrlParser.Parse("https://example.com/other?query#hash", baseUrl: null)!), $"The allowlist {FuzzAssert.Format(input)} does not allow its response origin");
        }

        // The patterns and the parameters serialized as a structured field give the same allowlist
        List<KeyValuePair<string, object>> parameters = [new("redirects", new StructuredFieldToken(allowlist.AllowRedirects ? "allow" : "block")), new("webrtc", new StructuredFieldToken(allowlist.AllowWebRtc ? "allow" : "block"))];
        if (allowlist.ReportingEndpoint is { } endpoint)
        {
            parameters.Add(new("report-to", new StructuredFieldToken(endpoint)));
        }

        var serialized = StructuredFields.SerializeInnerList([.. allowlist.SerializedPatterns.Select(pattern => new StructuredFieldItem(pattern, []))], parameters);
        var reparsed = ConnectionAllowlist.Parse(serialized, responseUrl, ConnectionAllowlistDisposition.Report);
        FuzzAssert.True(reparsed is not null, $"The serialization {FuzzAssert.Format(serialized)} of the allowlist {FuzzAssert.Format(input)} is not an allowlist");
        FuzzAssert.Equal(string.Join('\n', allowlist.SerializedPatterns), string.Join('\n', reparsed!.SerializedPatterns), $"The patterns of the serialization {FuzzAssert.Format(serialized)} of the allowlist {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(allowlist.AllowRedirects, reparsed.AllowRedirects, $"The redirects of the serialization of the allowlist {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(allowlist.AllowWebRtc, reparsed.AllowWebRtc, $"The WebRTC of the serialization of the allowlist {FuzzAssert.Format(input)}");
        FuzzAssert.Equal(allowlist.ReportingEndpoint, reparsed.ReportingEndpoint, $"The reporting endpoint of the serialization of the allowlist {FuzzAssert.Format(input)}");

        // An allowlist satisfies itself: a response whose allowlist is the required one is not blocked, and does not enforce it again
        FuzzAssert.True(allowlist.Satisfies(allowlist), $"The allowlist {FuzzAssert.Format(input)} does not satisfy itself");
        FuzzAssert.False(ConnectionAllowlist.ShouldBlockByEmbeddedEnforcement(input, [new("Connection-Allowlist", input)], responseUrl, "https://parent.example"), $"The response whose allowlist is the required allowlist {FuzzAssert.Format(input)} is blocked");
        FuzzAssert.False(ConnectionAllowlist.ShouldBlockByEmbeddedEnforcement(input, [new("Allow-Connection-Allowlist-From", "*")], responseUrl, "https://parent.example"), $"The response that allows any required allowlist is blocked by {FuzzAssert.Format(input)}");
        FuzzAssert.True(ReferenceEquals(allowlists, ConnectionAllowlist.ApplyRequired(allowlists, input, responseUrl)), $"The required allowlist {FuzzAssert.Format(input)} is enforced again by the response that enforces it");
        if (ConnectionAllowlist.IsValidAttributeValue(input, parentRequired: null))
        {
            FuzzAssert.True(ConnectionAllowlist.IsValidAttributeValue(input, input), $"The attribute {FuzzAssert.Format(input)} is not at least as strict as itself");
        }
    }

    private static IEnumerable<FuzzSeed> GetConnectionAllowlistSeeds()
    {
        string[] inputs =
        [
            "",
            "()",
            "(response-origin)",
            "(response-origin);redirects=block",
            "(response-origin);redirects=allow",
            "(response-origin); redirects=block; report-to=endpoint",
            "(response-origin);webrtc=allow",
            "(response-origin \"https://*.cdn.example/*\");redirects=allow;webrtc=allow;report-to=endpoint",
            "(response-origin \"*://:subdomain.not-web-platform.test:*\")",
            "(response-origin \"https://web-platform.test:11000\")",
            "(response-origin \"*://*:*/connection-allowlist/tentative/resources/pay\" \"*://*:*/images/rgrg-256x256.png\")",
            "(\"relative/path\" 1 token \"https://a.example\" \"https://[\")",
            "(\"https://a.example\");redirects=\"allow\";report-to=\"endpoint\"",
            "(\"https://a.example\");redirects=unknown;webrtc=block",
            "(\"https://a.example\"), (\"https://b.example\")",
            "\"https://a.example\"",
            "(\"https://a.example\"",
            "(\"https://a.example\")\nX-Injected: evil",
            "(\"https://example.com/books/:id(\\\\d+)\")",
            "(\"https://api.example:*\" \"https://*.example.:tld\")",
            "(\"https://extra.test/\" response-origin)",
            "not a valid structured header",
        ];
        return inputs.Select((input, index) => FuzzSeed.FromText($"connection-allowlist-{index}", input));
    }
}
