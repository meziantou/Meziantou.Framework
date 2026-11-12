using System.Security.Cryptography;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The parsers of the security features of the fetches: the integrity metadata of Subresource Integrity, the
// Unencoded-Digest header, the Integrity-Policy header, and the dangling markup flag of the URL parser
public static partial class WebPlatformFuzzTargets
{
    private static readonly byte[] IntegrityBody = "window.ran = true;"u8.ToArray();

    /// <summary>
    /// The tokens of the integrity target: the algorithms, the structured field syntax and the keys of the headers (a property:
    /// the static fields of the partial files are initialized in an unspecified order, and <see cref="All"/> reads it).
    /// </summary>
    private static string[] IntegrityTokens =>
    [
        "sha256-", "sha384-", "sha512-", "sha-256=", "sha-512=", "?", "=", ":", "(", ")", ",", ";", "\"", "*", "\n", "\t", "<",
        "blocked-destinations", "sources", "endpoints", "script", "style", "inline",
    ];

    /// <summary>
    /// Parses integrity metadata, an <c>Unencoded-Digest</c> header and an <c>Integrity-Policy</c> header, and checks the
    /// properties of their results: the metadata without hash matches any body, the metadata of the digest of a body
    /// matches it, the policy only has the defined sources and destinations, and the URL parser flags the dangling markup
    /// of its input.
    /// </summary>
    public static void Integrity(string input)
    {
        var parsed = SubresourceIntegrity.Parse(input);
        foreach (var item in parsed)
        {
            FuzzAssert.True(item.StartsWith("sha256-", StringComparison.Ordinal) || item.StartsWith("sha384-", StringComparison.Ordinal) || item.StartsWith("sha512-", StringComparison.Ordinal), $"The integrity metadata {FuzzAssert.Format(input)} has the hash expression {FuzzAssert.Format(item)}");
        }

        var matches = SubresourceIntegrity.Matches(IntegrityBody, opaque: false, input);
        if (parsed.Count == 0)
        {
            FuzzAssert.True(matches, $"The integrity metadata {FuzzAssert.Format(input)} has no hash, but does not match the body");
            FuzzAssert.True(SubresourceIntegrity.Matches(IntegrityBody, opaque: true, input), $"The integrity metadata {FuzzAssert.Format(input)} has no hash, but does not match an opaque response");
        }
        else
        {
            FuzzAssert.False(SubresourceIntegrity.Matches(IntegrityBody, opaque: true, input), $"The integrity metadata {FuzzAssert.Format(input)} matches an opaque response");
        }

        // The metadata of the digest of the body (the strongest algorithm) matches it, whatever the other expressions
        var digest = "sha512-" + Convert.ToBase64String(SHA512.HashData(IntegrityBody));
        FuzzAssert.True(SubresourceIntegrity.Matches(IntegrityBody, opaque: false, input + " " + digest), $"The integrity metadata {FuzzAssert.Format(input)} followed by the digest of the body does not match it");

        _ = SubresourceIntegrity.MatchesUnencodedDigest([new("Unencoded-Digest", input)], IntegrityBody);
        var unencodedDigest = "sha-256=:" + Convert.ToBase64String(SHA256.HashData(IntegrityBody)) + ":";
        FuzzAssert.True(SubresourceIntegrity.MatchesUnencodedDigest([new("Unencoded-Digest", unencodedDigest)], IntegrityBody), "The Unencoded-Digest header of the digest of the body does not match it");

        var policy = IntegrityPolicy.Parse(input);
        FuzzAssert.True(policy.Sources.All(source => source == "inline") && policy.Sources.Count <= 1, $"The integrity policy {FuzzAssert.Format(input)} has the sources {FuzzAssert.Format(string.Join(' ', policy.Sources))}");
        FuzzAssert.True(policy.BlockedDestinations.All(destination => destination is RequestDestination.Script or RequestDestination.Style) && policy.BlockedDestinations.Distinct().Count() == policy.BlockedDestinations.Count, $"The integrity policy {FuzzAssert.Format(input)} has unexpected blocked destinations");
        if (policy.Applies(RequestDestination.Script))
        {
            FuzzAssert.True(policy.Sources.Contains("inline") && policy.BlockedDestinations.Contains(RequestDestination.Script), $"The integrity policy {FuzzAssert.Format(input)} applies to the scripts without blocking them");
        }

        // The dangling markup flag of a URL is the one of its input
        foreach (var baseUrl in new[] { "https://example.com/a/b", "data:text/plain,a" })
        {
            if (UrlParser.Parse(input, UrlParser.Parse(baseUrl, baseUrl: null)) is { } url)
            {
                FuzzAssert.Equal(UrlParser.IsPotentiallyDanglingMarkup(input), url.PotentiallyDanglingMarkup, $"The dangling markup flag of the URL {FuzzAssert.Format(input)}");
                FuzzAssert.Equal(url.PotentiallyDanglingMarkup, url.Clone().PotentiallyDanglingMarkup, $"The dangling markup flag of the clone of the URL {FuzzAssert.Format(input)}");
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetIntegritySeeds()
    {
        string[] inputs =
        [
            "",
            "sha256-Bu681KMnQ15RYHFvsYdWumweeFAw0hJDTFt9seErghA=",
            "sha384-cINXh+nCzEHPWzXS7eoT+vYMBpyqczOybRLNU3XAButFWCRhHT5hLByIbPRqIm2f",
            "sha512-KZdenhzBd7X7Q/vmaOSyvFz1CGdoVt26xzCZjlkU9lfBEK+V/ougGys7iYDi0+tOHIQSQa87bIqx95R7GU7I9Q==",
            "sha256-... sha384-?foo",
            "?-=",
            "-hash",
            "sha256-",
            "foobar-AAAAAAAAAAAAAAAAAAAa",
            "sha256-Bu681KMnQ15RYHFvsYdWumweeFAw0hJDTFt9seErghA=?foo=bar sha512-deadbeef",
            "sha-256=:uU0nuZNNPgilLlLX2n2r+sSE7+N6U4DukIj3rOLvzek=:",
            "sha-256=:uU0nuZNNPgilLlLX2n2r+sSE7+N6U4DukIj3rOLvzek=:,sha-512=:AAAA:",
            "blocked-destinations=(script)",
            "blocked-destinations=(script), sources=(inline)",
            "blocked-destinations=(script style), endpoints=(integrity-endpoint-1 integrity-endpoint-2)",
            "blocked-destinations=script",
            "blocked-destinations=(\"script\")",
            "blocked-destinations=(script",
            "blocked-destinations=(script,style)",
            "blocked-destinations=(script), sources=(telepathy)",
            "/images/gre\nen-1x1.png?img=<",
            "/images/green-1x1.png?<\t=block",
            "\n /images/green-1x1.png?img=< \n",
            "data:text/html,\n<img src=x>",
        ];
        return inputs.Select((input, index) => FuzzSeed.FromText($"integrity-{index}", input));
    }
}
