using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Compression;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Compression Dictionary Transport (https://www.rfc-editor.org/rfc/rfc9842): the dictionaries registered by the
/// <c>Use-As-Dictionary</c> header of the responses, shared by the browsing contexts and the workers that use the same
/// <see cref="DocumentLoader"/>. As the HTTP cache, they are partitioned by the top-level site of the request and whether it
/// comes from a cross-site frame (<see cref="HttpCache.GetPartition"/>), and they are used while their response would be
/// fresh in the HTTP cache (or within its <c>stale-while-revalidate</c> window). The best matching dictionary of a request
/// is announced by its <c>Available-Dictionary</c> and <c>Dictionary-ID</c> headers, and decodes a response whose content
/// coding is <c>dcb</c> or <c>dcz</c>.
/// </summary>
/// <remarks>The dictionaries are used by the threads of the workers.</remarks>
internal sealed class CompressionDictionaries
{
    /// <summary>The maximum length of the <c>id</c> of a dictionary (https://www.rfc-editor.org/rfc/rfc9842#section-2.1.3).</summary>
    internal const int MaxIdLength = 1024;

    /// <summary>The content codings of the dictionaries, sent in the <c>Accept-Encoding</c> header with a dictionary.</summary>
    internal const string AcceptEncoding = "dcb, dcz";

    private static readonly ConditionalWeakTable<DocumentLoader, CompressionDictionaries> Stores = [];

    private readonly Lock _lock = new();
    private readonly List<CompressionDictionary> _dictionaries = [];
    private long _sequence;

    /// <summary>The dictionaries of the requests sent by a loader.</summary>
    public static CompressionDictionaries Get(DocumentLoader loader) => Stores.GetValue(loader, static _ => new CompressionDictionaries());

    /// <summary>
    /// Parses the value of a <c>Use-As-Dictionary</c> header (https://www.rfc-editor.org/rfc/rfc9842#section-2.1): a
    /// structured dictionary whose <c>match</c> is a URL pattern (relative to the URL of the dictionary, without regular
    /// expression groups, and same-origin with the URL of the dictionary), whose <c>match-dest</c> is an inner list of
    /// destinations, whose <c>id</c> is a string of at most 1024 characters, and whose <c>type</c> is <c>raw</c>. Returns
    /// null when the value is not valid. The unknown members are ignored.
    /// </summary>
    internal static CompressionDictionaryOptions? ParseUseAsDictionary(string value, UrlRecord dictionaryUrl)
    {
        if (StructuredFields.ParseDictionary(value) is not { } dictionary)
            return null;

        string? match = null;
        var destinations = new List<string>();
        var id = "";
        foreach (var (key, member) in dictionary)
        {
            switch (key)
            {
                case "match":
                    if (member.Item?.Value is not string matchValue)
                        return null;

                    match = matchValue;
                    break;

                case "match-dest":
                    if (member.InnerList is not { } list)
                        return null;

                    destinations.Clear();
                    foreach (var item in list)
                    {
                        if (item.Value is not string destination)
                            return null;

                        destinations.Add(destination);
                    }

                    break;

                case "id":
                    if (member.Item?.Value is not string idValue || idValue.Length > MaxIdLength)
                        return null;

                    id = idValue;
                    break;

                case "type":
                    // https://www.rfc-editor.org/rfc/rfc9842#section-2.1.4: only the raw dictionaries are supported
                    if (member.Item?.Value is not StructuredFieldToken { Value: "raw" })
                        return null;

                    break;
            }
        }

        if (match is null || CreatePattern(match, dictionaryUrl) is not { } pattern)
            return null;

        return new CompressionDictionaryOptions(pattern, match, destinations, id);
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc9842#section-2.1.1: the URL pattern of the <c>match</c> member, created with the URL of
    /// the dictionary as the base URL. It is not valid when it has regular expression groups, or when it is not same-origin
    /// with the dictionary (its protocol, hostname and port are not the ones of the URL of the dictionary).
    /// </summary>
    private static UrlPattern? CreatePattern(string match, UrlRecord dictionaryUrl)
    {
        UrlPattern pattern;
        try
        {
            pattern = new UrlPattern(match, dictionaryUrl.Serialize());
        }
        catch (TypeErrorException)
        {
            return null;
        }

        if (pattern.HasRegExpGroups)
            return null;

        var port = dictionaryUrl.Port >= 0 ? dictionaryUrl.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        if (Unescape(pattern.Protocol) != dictionaryUrl.Scheme || Unescape(pattern.Hostname) != (dictionaryUrl.Host?.Serialized ?? "") || Unescape(pattern.Port) != port)
            return null;

        return pattern;

        // The fixed text of a component is escaped with backslashes (https://urlpattern.spec.whatwg.org/#escape-a-pattern-string)
        static string Unescape(string component)
        {
            var result = new StringBuilder(component.Length);
            for (var i = 0; i < component.Length; i++)
            {
                if (component[i] == '\\' && i + 1 < component.Length)
                {
                    i++;
                }
                else if (component[i] is '*' or ':' or '(' or '{')
                {
                    // A wildcard, a named group or a group: the component is not fixed
                    return "\0";
                }

                result.Append(component[i]);
            }

            return result.ToString();
        }
    }

    /// <summary>
    /// Registers the dictionary of a response whose <c>Use-As-Dictionary</c> header is valid
    /// (https://www.rfc-editor.org/rfc/rfc9842#section-2.1): it replaces the dictionary of the same origin, the same
    /// <c>match</c> and the same <c>match-dest</c> in the partition. A response that cannot be stored (<c>no-store</c>) or
    /// that is not fresh is not registered.
    /// </summary>
    public void Register(string partition, UrlRecord url, CompressionDictionaryOptions options, byte[] content, int status, IReadOnlyList<KeyValuePair<string, string>> headers, DateTimeOffset requestTime, DateTimeOffset responseTime)
    {
        if (HttpCache.ParseCacheControl(headers).ContainsKey("no-store"))
            return;

        var origin = url.SerializeOrigin();
        var hash = System.Security.Cryptography.SHA256.HashData(content);
        lock (_lock)
        {
            var dictionary = new CompressionDictionary(partition, origin, url.Serialize(), options, content, hash, status, [.. headers], requestTime, responseTime, ++_sequence);
            if (!dictionary.IsUsable(responseTime))
                return;

            _dictionaries.RemoveAll(existing => existing.Partition == partition && existing.Origin == origin && existing.Options.Match == options.Match && existing.Options.Destinations.SequenceEqual(options.Destinations, StringComparer.Ordinal));
            _dictionaries.Add(dictionary);
        }
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc9842#section-2.2.2: the best dictionary of a request in a partition: a usable
    /// dictionary of the origin of the URL whose pattern matches the URL and whose <c>match-dest</c> is empty or has the
    /// destination of the request. The dictionaries with a <c>match-dest</c> have precedence, then the longest
    /// <c>match</c>, then the most recently registered dictionary.
    /// </summary>
    public CompressionDictionary? FindBestMatch(string partition, UrlRecord url, string destination, DateTimeOffset now)
    {
        var origin = url.SerializeOrigin();
        CompressionDictionary? best = null;
        lock (_lock)
        {
            _dictionaries.RemoveAll(dictionary => !dictionary.IsUsable(now));
            foreach (var dictionary in _dictionaries)
            {
                if (dictionary.Partition != partition || dictionary.Origin != origin)
                    continue;

                var destinations = dictionary.Options.Destinations;
                if (destinations.Count > 0 && !destinations.Contains(destination, StringComparer.Ordinal))
                    continue;

                if (!dictionary.Options.Pattern.Test(url))
                    continue;

                if (best is null || Compare(dictionary, best) > 0)
                {
                    best = dictionary;
                }
            }
        }

        return best;

        static int Compare(CompressionDictionary left, CompressionDictionary right)
        {
            var hasDestinations = (left.Options.Destinations.Count > 0).CompareTo(right.Options.Destinations.Count > 0);
            if (hasDestinations != 0)
                return hasDestinations;

            var length = left.Options.Match.Length.CompareTo(right.Options.Match.Length);
            return length != 0 ? length : left.Sequence.CompareTo(right.Sequence);
        }
    }

    /// <summary>
    /// Removes the dictionaries of an origin (https://www.rfc-editor.org/rfc/rfc9842#section-9.1: the <c>cache</c> and
    /// <c>cookies</c> types of the <c>Clear-Site-Data</c> header), in a partition or, when <paramref name="partition"/> is
    /// null, in every partition.
    /// </summary>
    public void ClearOrigin(string origin, string? partition)
    {
        lock (_lock)
        {
            _dictionaries.RemoveAll(dictionary => dictionary.Origin == origin && (partition is null || dictionary.Partition == partition));
        }
    }

    /// <summary>
    /// The value of the <c>Available-Dictionary</c> header of a dictionary (https://www.rfc-editor.org/rfc/rfc9842#section-2.2):
    /// the structured field byte sequence of its SHA-256 hash.
    /// </summary>
    internal static string GetAvailableDictionaryValue(CompressionDictionary dictionary) => ":" + Convert.ToBase64String(dictionary.Hash) + ":";

    /// <summary>The value of the <c>Dictionary-ID</c> header of an <c>id</c> (https://www.rfc-editor.org/rfc/rfc9842#section-2.3): a structured field string.</summary>
    internal static string GetDictionaryIdValue(string id)
    {
        var result = new StringBuilder(id.Length + 2).Append('"');
        foreach (var c in id)
        {
            if (c is '"' or '\\')
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.Append('"').ToString();
    }

    /// <summary>
    /// Decodes the body of a response whose content coding is <c>dcb</c> or <c>dcz</c> with the dictionary announced by its
    /// request (https://www.rfc-editor.org/rfc/rfc9842#section-4).
    /// </summary>
    /// <exception cref="InvalidDataException">The body is not valid, or it was not compressed with the dictionary.</exception>
    internal static byte[] Decode(string coding, ReadOnlySpan<byte> body, CompressionDictionary? dictionary)
    {
        if (dictionary is null)
            throw new InvalidDataException("The response is compressed with a dictionary, but the request has no dictionary");

        return DictionaryDecompressor.Decompress(coding, body, dictionary.Content, dictionary.Hash, HttpCache.MaxBodyLength * 4);
    }
}

/// <summary>The members of a valid <c>Use-As-Dictionary</c> header (https://www.rfc-editor.org/rfc/rfc9842#section-2.1).</summary>
/// <param name="Pattern">The URL pattern of the <c>match</c> member.</param>
/// <param name="Match">The <c>match</c> member.</param>
/// <param name="Destinations">The destinations of the <c>match-dest</c> member (empty for every destination).</param>
/// <param name="Id">The <c>id</c> member (empty when it has none).</param>
internal sealed record CompressionDictionaryOptions(UrlPattern Pattern, string Match, IReadOnlyList<string> Destinations, string Id);

/// <summary>A registered dictionary (see <see cref="CompressionDictionaries"/>).</summary>
internal sealed record CompressionDictionary(
    string Partition,
    string Origin,
    string Url,
    CompressionDictionaryOptions Options,
    byte[] Content,
    byte[] Hash,
    int Status,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    DateTimeOffset RequestTime,
    DateTimeOffset ResponseTime,
    long Sequence)
{
    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc9842#section-2.2.1: "the dictionary resource MUST be either fresh or allowed to be
    /// served stale": its response would be fresh in the HTTP cache, or within its <c>stale-while-revalidate</c> window.
    /// </summary>
    public bool IsUsable(DateTimeOffset now)
    {
        var entry = new HttpCacheEntry(IncludeCredentials: false, [], Status, "", Headers, [], RequestTime, ResponseTime);
        return HttpCache.GetFreshness(entry, [], now) is HttpCacheFreshness.Fresh or HttpCacheFreshness.StaleWhileRevalidate;
    }
}
