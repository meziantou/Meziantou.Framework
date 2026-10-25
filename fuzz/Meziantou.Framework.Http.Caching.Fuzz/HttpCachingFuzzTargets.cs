using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Http.Caching.Fuzz;

/// <summary>
/// The cache reads the headers of arbitrary servers and the entries of an external store. The Structured Fields parser and
/// the <c>No-Vary-Search</c> parser never throw and agree with a serializer, the persisted payload of a response round
/// trips and a corrupted one is rejected by the validation that turns it into a cache miss, and the caching handler,
/// driven end to end in memory, never throws and only serves responses it stored for the same method and URL.
/// </summary>
public static partial class HttpCachingFuzzTargets
{
    private static readonly string[] StructuredFieldTokens =
    [
        "=", ",", ", ", ";", "(", ")", " ", "\t", "\"", "\\\"", "\\\\", "?0", "?1", ":", "*", "-", ".", "/",
        "params", "except", "key-order", "=(\"a\" \"b\")", "=?1", "=?0", ":YWJj:", "1.5", "-12", "123456789012345", "tok/en", ";p=1",
    ];

    private static readonly string[] QueryTokens = ["?", "&", "=", "+", "%", "%20", "%2B", "%26", "%3D", "%C3%A9", "%F0%9F%98%80", "%FF", "a", "b", "utm_source", "id"];

    // A property rather than a field: the dictionaries are declared in the other files of this partial class, whose static
    // fields may be initialized after the fields of this file.
    public static IReadOnlyList<FuzzTarget> All =>
    [
        new("http-caching-structured-fields", StructuredFields) { Seeds = GetStructuredFieldsSeeds, Dictionary = StructuredFieldTokens },
        new("http-caching-structured-fields-valid", ValidStructuredFields) { Seeds = GetValidStructuredFieldsSeeds },
        new("http-caching-no-vary-search", NoVarySearch) { Seeds = GetNoVarySearchSeeds, Dictionary = [.. StructuredFieldTokens, .. QueryTokens] },
        new("http-caching-response-serializer", ResponseSerializerRoundTrip) { Seeds = GetResponseSerializerSeeds, Dictionary = SerializerTokens },
        new("http-caching-handler", Handler) { Seeds = GetHandlerSeeds, Dictionary = HandlerTokens },
        new("http-caching-persistence-entry", PersistenceEntry) { Seeds = GetPersistenceEntrySeeds, Dictionary = [.. StructuredFieldTokens, .. QueryTokens] },
    ];

    /// <summary>
    /// The input is a dictionary field value. Parsing never throws; a parsed dictionary has valid keys and printable
    /// strings, its serialization parses back to the same dictionary, and leading and trailing spaces do not change the
    /// result.
    /// </summary>
    private static void StructuredFields(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var parsed = StructuredFieldsParser.TryParseDictionary(text, out var dictionary);

        // RFC 8941 Section 4.2: leading and trailing spaces are discarded
        var padded = StructuredFieldsParser.TryParseDictionary(" " + text + " ", out var paddedDictionary);
        FuzzAssert.Equal(parsed, padded, "Leading and trailing spaces change whether the field parses");
        if (!parsed)
            return;

        foreach (var (key, value) in dictionary!)
        {
            FuzzAssert.True(IsValidKey(key), $"The key {FuzzAssert.Format(key)} is not a valid key");
            if (value.IsInnerList)
            {
                foreach (var item in value.InnerList)
                {
                    CheckItem(item);
                }
            }
            else
            {
                CheckItem(value.Item);
            }
        }

        AssertSameDictionary(dictionary, paddedDictionary!, "Leading and trailing spaces change the parsed dictionary");

        var serialized = SerializeDictionary(dictionary);
        FuzzAssert.True(StructuredFieldsParser.TryParseDictionary(serialized, out var reparsed), $"The serialized dictionary {FuzzAssert.Format(serialized)} does not parse");
        AssertSameDictionary(dictionary, reparsed, $"The serialized dictionary {FuzzAssert.Format(serialized)} does not parse back to the same dictionary");
        FuzzAssert.Equal(serialized, SerializeDictionary(reparsed), "Serializing the dictionary is not a fixed point");

        static void CheckItem(StructuredFieldItem item)
        {
            if (item.Type is StructuredFieldItemType.String)
            {
                FuzzAssert.True(item.StringValue is not null && item.StringValue.All(c => c is >= ' ' and <= '~'), $"The string {FuzzAssert.Format(item.StringValue)} has a character outside the printable ASCII range");
            }
        }
    }

    /// <summary>
    /// The input drives a generator of RFC 8941 dictionaries: every member type, parameters, inner lists, and the
    /// optional whitespace the syntax allows. The parser accepts every generated dictionary and reads the values it
    /// materializes (booleans and strings), keeping the last value of a duplicated key.
    /// </summary>
    private static void ValidStructuredFields(ReadOnlySpan<byte> data)
    {
        var generator = new StructuredFieldGenerator(data);
        var (text, expected) = generator.GenerateDictionary();

        FuzzAssert.True(StructuredFieldsParser.TryParseDictionary(text, out var dictionary), $"The valid dictionary {FuzzAssert.Format(text)} does not parse");
        AssertSameDictionary(expected, dictionary, $"The dictionary {FuzzAssert.Format(text)} is not parsed as generated");
    }

    /// <summary>
    /// The input is a <c>No-Vary-Search</c> value, then queries on the following lines. Parsing never throws, the canonical
    /// value is a fixed point that describes the same config, and the canonicalized queries match a reference
    /// implementation of the draft (URL Standard form-urlencoded parsing, filtering, then sorting by code units).
    /// </summary>
    private static void NoVarySearch(ReadOnlySpan<byte> data)
    {
        var header = FuzzText.Decode(FuzzInput.ReadUntil(ref data, (byte)'\n'));
        var config = UrlVariationConfig.Parse(header);
        var canonical = config.ToHeaderValue();
        var reparsed = UrlVariationConfig.Parse(canonical);
        FuzzAssert.Equal(canonical, reparsed.ToHeaderValue(), $"The canonical value of {FuzzAssert.Format(header)} is not a fixed point");
        FuzzAssert.Equal(config.IsDefault, reparsed.IsDefault, "The canonical value changes IsDefault");
        FuzzAssert.Equal(config.VaryOnKeyOrder, reparsed.VaryOnKeyOrder, "The canonical value changes VaryOnKeyOrder");
        FuzzAssert.Equal(config.IsDefault, canonical.Length is 0, "Only the default config has an empty canonical value");

        var reference = ReferenceVariationConfig.FromCanonicalValue(canonical);
        for (var i = 0; i < 4 && !data.IsEmpty; i++)
        {
            var text = FuzzText.Decode(FuzzInput.ReadUntil(ref data, (byte)'\n'));
            if (!Uri.TryCreate("https://example.com/path?" + text, UriKind.Absolute, out var uri))
                continue;

            // The cache passes Uri.Query, which escapes every non-ASCII character
            var query = uri.Query;
            var normalized = config.NormalizeQuery(query);
            FuzzAssert.Equal(normalized, reparsed.NormalizeQuery(query), "The config and its canonical value canonicalize a query differently");
            FuzzAssert.Equal(reference.NormalizeQuery(query), normalized, $"Canonicalizing {FuzzAssert.Format(query)} with {FuzzAssert.Format(canonical)}");
            if (!config.IsDefault)
            {
                FuzzAssert.Equal(normalized, config.NormalizeQuery("?" + normalized), "Canonicalizing a query is not idempotent");
            }
        }
    }

    private static bool IsValidKey(string key)
    {
        if (key.Length is 0 || !(key[0] is '*' or (>= 'a' and <= 'z')))
            return false;

        return key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.' or '*');
    }

    private static void AssertSameDictionary(Dictionary<string, StructuredFieldValue> expected, Dictionary<string, StructuredFieldValue> actual, string message)
    {
        FuzzAssert.Equal(expected.Count, actual.Count, message + ": the number of members differs");
        foreach (var (key, expectedValue) in expected)
        {
            FuzzAssert.True(actual.TryGetValue(key, out var actualValue), $"{message}: the member {FuzzAssert.Format(key)} is missing");
            FuzzAssert.Equal(expectedValue.IsInnerList, actualValue.IsInnerList, $"{message}: the member {FuzzAssert.Format(key)} is not of the same kind");
            if (expectedValue.IsInnerList)
            {
                FuzzAssert.Equal(expectedValue.InnerList.Count, actualValue.InnerList.Count, $"{message}: the inner list {FuzzAssert.Format(key)} has a different length");
                for (var i = 0; i < expectedValue.InnerList.Count; i++)
                {
                    AssertSameItem(expectedValue.InnerList[i], actualValue.InnerList[i], $"{message}: item {i} of {FuzzAssert.Format(key)}");
                }
            }
            else
            {
                AssertSameItem(expectedValue.Item, actualValue.Item, $"{message}: the member {FuzzAssert.Format(key)}");
            }
        }

        static void AssertSameItem(StructuredFieldItem expected, StructuredFieldItem actual, string message)
        {
            FuzzAssert.Equal(expected.Type, actual.Type, message + " has a different type");
            FuzzAssert.Equal(expected.BooleanValue, actual.BooleanValue, message + " has a different boolean value");
            FuzzAssert.Equal(expected.StringValue, actual.StringValue, message + " has a different string value");
        }
    }

    private static string SerializeDictionary(IReadOnlyDictionary<string, StructuredFieldValue> dictionary)
    {
        var builder = new StringBuilder();
        foreach (var (key, value) in dictionary.OrderBy(member => member.Key, StringComparer.Ordinal))
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(key);
            if (value.IsInnerList)
            {
                builder.Append("=(");
                for (var i = 0; i < value.InnerList.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(' ');
                    }

                    AppendItem(builder, value.InnerList[i]);
                }

                builder.Append(')');
            }
            else if (value.Item is not { Type: StructuredFieldItemType.Boolean, BooleanValue: true })
            {
                // RFC 8941 Section 4.1.2: a member whose value is true is serialized as its key alone
                builder.Append('=');
                AppendItem(builder, value.Item);
            }
        }

        return builder.ToString();

        static void AppendItem(StringBuilder builder, StructuredFieldItem item)
        {
            switch (item.Type)
            {
                case StructuredFieldItemType.Boolean:
                    builder.Append(item.BooleanValue ? "?1" : "?0");
                    break;

                case StructuredFieldItemType.String:
                    builder.Append('"').Append(item.StringValue!.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                    break;

                default:
                    // The parser does not keep the value of the other item types
                    builder.Append('1');
                    break;
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetStructuredFieldsSeeds()
    {
        string[] values =
        [
            "", "a", "a, b", "a=1", "a=-1.5, b=\"x\\\"y\\\\z\"", "a=?0, b=?1;p", "a=(1 2 \"s\" ?1 tok :YWJj:);q=1, b=()", "*a=*tok/en:x",
            "a=:YWJj:;x=?0;y=\"s\";z=1.25", "a=1, a=2", "a=1,\tb=2", "params, except=(\"id\")", "key-order, params=(\"a\" \"b\")",
            "a=(  1   2  )", "a=", "a,", "A=1", "a=@123", "a=%\"x\"", "a=(1", "a=\"\\x\"",
        ];

        var index = 0;
        foreach (var value in values.Concat(GetTestHeaderValues("No-Vary-Search")).Concat(GetTestHeaderValues("Cache-Control")).Distinct(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"value-{index++}"), value);
        }
    }

    private static IEnumerable<FuzzSeed> GetValidStructuredFieldsSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        for (var i = 0; i < 16; i++)
        {
            // Deterministic pseudo-random bytes
            var seed = SHA512.HashData(BitConverter.GetBytes(i));
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"random-{i}"), seed);
        }
    }

    private static IEnumerable<FuzzSeed> GetNoVarySearchSeeds()
    {
        string[] queries = ["", "a=1&b=2", "b=2&a=1", "utm_source=x&id=3&id=1", "a+b=%20c&%C3%A9=%FF", "&&a&=b&c=", "q=%F0%9F%98%80&q=%ED%A0%80"];
        string[] headers =
        [
            "", "key-order", "params", "params=?0", "params=(\"a\")", "except=(\"a\" \"id\")", "params, except=(\"id\")", "key-order, params=(\"utm_source\" \"a%20b\")",
            "params=(\"a\"), except=(\"b\")", "params=(a)", "key-order=?0", "key-order=(?1)",
        ];

        var index = 0;
        foreach (var header in headers.Concat(GetTestHeaderValues("No-Vary-Search")).Distinct(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"header-{index++}"), header + "\n" + string.Join('\n', queries));
        }
    }

    /// <summary>Gets the values the unit tests give to a header, in tuples such as <c>("Cache-Control", "max-age=60")</c>.</summary>
    private static IEnumerable<string> GetTestHeaderValues(string name)
    {
        var assembly = typeof(HttpCachingFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith("HttpCachingTests.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var source = FuzzResources.ReadText(assembly, resourceName);
            foreach (Match match in HeaderTupleRegex().Matches(source))
            {
                if (string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
                    yield return Regex.Unescape(match.Groups["value"].Value);
            }
        }
    }

    [GeneratedRegex("""\("(?<name>[A-Za-z-]+)",\s*"(?<value>(?:[^"\\\r\n]|\\.)*)"\)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex HeaderTupleRegex();
}
