using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The Strict-Transport-Security header of HSTS (https://www.rfc-editor.org/rfc/rfc6797#section-6.1)
public static partial class WebPlatformFuzzTargets
{
    /// <summary>The tokens of the Strict-Transport-Security header target: the directives and the syntax of their values.</summary>
    private static string[] StrictTransportSecurityTokens =>
    [
        "max-age", "MAX-AGE", "includeSubDomains", "includesubdomains", "preload", "=", ";", " ", "\t", "\"", "\\", "\\\"", "0", "60",
        "31536000", "31536001", "99999999999999999999", "-1", "1.5", ",",
    ];

    /// <summary>
    /// Parses the value of a Strict-Transport-Security header, and checks the properties of a valid header: its
    /// <c>max-age</c> is between 0 and one year, serializing it and parsing the serialization gives the same header, and
    /// the store of the learned hosts holds the host until the policy expires (a <c>max-age</c> of 0 removes it).
    /// An invalid header is ignored (null), never an exception.
    /// </summary>
    public static void StrictTransportSecurityHeaderTarget(string input)
    {
        if (StrictTransportSecurityHeader.Parse(input) is not { } header)
            return;

        FuzzAssert.True(header.MaxAgeSeconds is >= 0 and <= StrictTransportSecurityHeader.MaxMaxAgeSeconds, $"The Strict-Transport-Security header {FuzzAssert.Format(input)} has the max-age {header.MaxAgeSeconds}");

        var serialized = header.Serialize();
        FuzzAssert.Equal(header, StrictTransportSecurityHeader.Parse(serialized), $"The serialization {FuzzAssert.Format(serialized)} of the Strict-Transport-Security header {FuzzAssert.Format(input)}");

        var store = new HstsHostStore();
        store.Add("example.com", header, FrozenTimeProvider.Instance);
        FuzzAssert.Equal(header.MaxAgeSeconds > 0, store.Contains("example.com", FrozenTimeProvider.Instance), $"The host of the Strict-Transport-Security header {FuzzAssert.Format(input)} is known");
        FuzzAssert.Equal(header.MaxAgeSeconds > 0 && header.IncludeSubdomains, store.Contains("www.example.com", FrozenTimeProvider.Instance), $"The subdomain of the Strict-Transport-Security header {FuzzAssert.Format(input)} is known");
        FuzzAssert.False(store.Contains("example.org", FrozenTimeProvider.Instance), $"Another host is known after the Strict-Transport-Security header {FuzzAssert.Format(input)}");
    }

    /// <summary>The tokens of the HSTS preload target: labels of preloaded domains, and the dots between labels.</summary>
    private static string[] HstsPreloadTokens =>
    [
        ".", "..", "com", "github", "gmail", "www", "mail", "google", "dev", "app", "api", "example", "xn--", "-",
    ];

    /// <summary>The entries of the preload list, read once: the oracle of <see cref="HstsPreloadTarget"/>.</summary>
    private static readonly Lazy<Dictionary<string, bool>> PreloadEntries = new(() => global::Meziantou.Framework.Http.HstsPreloadList.Shared.GetEntries().ToDictionary(entry => entry.Host, entry => entry.IncludeSubdomains, StringComparer.Ordinal));

    /// <summary>
    /// Looks a host up in the HSTS preload list, and checks that the result agrees with a dictionary of the entries of
    /// the list: the host is preloaded when it is an entry, or when one of its superdomains is an entry that includes
    /// the subdomains. The input is lowercased, as the hosts of the URLs are.
    /// </summary>
    public static void HstsPreloadTarget(string input)
    {
        var host = input.ToLowerInvariant();
        var actual = StrictTransportSecurity.IsPreloaded(host);

        var entries = PreloadEntries.Value;
        var expected = entries.ContainsKey(host);
        for (var dot = host.IndexOf('.', StringComparison.Ordinal); !expected && dot >= 0; dot = host.IndexOf('.', dot + 1, StringComparison.Ordinal))
        {
            expected = entries.TryGetValue(host[(dot + 1)..], out var includeSubdomains) && includeSubdomains;
        }

        FuzzAssert.Equal(expected, actual, $"The host {FuzzAssert.Format(host)} is in the preload list");
    }

    private static IEnumerable<FuzzSeed> GetHstsPreloadSeeds()
    {
        string[] inputs =
        [
            "github.com",
            "api.github.com",
            "gmail.com",
            "www.gmail.com",
            "mail.gmail.com",
            "a.b.mail.google.com",
            "google.com",
            "dev",
            "my-project.dev",
            "example.com",
            "com",
            "a..github.com",
            ".github.com",
            "",
            ".",
            "xn--bcher-kva.example",
        ];
        return inputs.Select((input, index) => new FuzzSeed($"hsts-preload-{index}", Encoding.UTF8.GetBytes(input)));
    }

    private static IEnumerable<FuzzSeed> GetStrictTransportSecuritySeeds()
    {
        string[] inputs =
        [
            "max-age=60",
            "MAX-AGE=60;INCLUDESUBDOMAINS",
            " max-age = \"60\" ; includeSubDomains ; preload ",
            ";;max-age=0;;",
            "includeSubDomains; max-age=1; ext=\"a;includeSubDomains\"",
            "max-age=60; ext=\"a;includeSubDomains\"",
            "max-age=99999999999999999999999",
            "max-age=31536001",
            "max-age=\"\\60\"",
            "",
            "includeSubDomains",
            "max-age",
            "max-age=",
            "max-age=-1",
            "max-age=1.5",
            "max-age=\"60",
            "max-age=60 60",
            "max-age=60, max-age=60",
            "max-age=60; max-age=60",
            "max-age=60; includeSubDomains; includeSubDomains",
            "max-age=60; includeSubDomains=1",
            "max-age=60; =1",
            "max-age=60; ext=",
            "max-age=0000000000000000000000060",
            "animal=\"squirrel; distinguished\"; incLudesUbdOmains; max-age=123",
            ";;    ; ; max-age=394082038;;; includeSubdomains     ;;  ;",
            "max-age=1; \"includeSubdomains\"",
            "max-age=3488923 includesubdomains",
            "max-age=999999999999999999999999$.&#!",
        ];
        return inputs.Select((input, index) => new FuzzSeed($"hsts-{index}", Encoding.UTF8.GetBytes(input)));
    }
}
