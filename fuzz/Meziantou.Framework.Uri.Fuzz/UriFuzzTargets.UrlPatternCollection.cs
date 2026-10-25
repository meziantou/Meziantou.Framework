using Meziantou.Framework;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.UrlPatternInternal;

namespace UriFuzz;

public static partial class UriFuzzTargets
{
    private const int MaxCollectionPatterns = 4;

    /// <summary>
    /// Adds URL patterns to a <see cref="UrlPatternCollection"/> (from a string, a string and a base URL, an init, or a
    /// pattern that ignores the case) and matches it against URLs (strings, with or without a base URL, <see cref="System.Uri"/>)
    /// or inits. An invalid pattern only throws <see cref="UrlPatternException"/> and is not added. <c>FindPattern</c> is the
    /// first added pattern that matches, <c>IsMatch</c> says whether there is one, and <c>Match</c> is the match of that
    /// pattern. A <see cref="System.Uri"/> created from the URL gives the same pattern when its text is parsed with the same
    /// scheme, and so does an init made of the components of a URL with a special scheme.
    /// </summary>
    private static void UrlPatternCollectionTarget(ReadOnlySpan<byte> data)
    {
        var testCase = CollectionCase.Read(data);
        if (testCase.Patterns.Sum(pattern => pattern.CountRepetitionCodePoints()) > MaxRepetitionCodePoints)
            return;

        var collection = new UrlPatternCollection();
        foreach (var patternCase in testCase.Patterns)
        {
            var count = collection.Count;
            try
            {
                var added = patternCase.AddTo(collection);
                FuzzAssert.Equal(count + 1, collection.Count, $"Adding {patternCase} does not add one pattern");
                FuzzAssert.True(ReferenceEquals(added, collection[count]), $"The pattern returned when adding {patternCase} is not the last pattern");
                FuzzAssert.True(collection.Contains(added), $"The collection does not contain the pattern {patternCase}");
            }
            catch (UrlPatternException)
            {
                FuzzAssert.Equal(count, collection.Count, $"The invalid pattern {patternCase} is added");
            }
        }

        if (collection.Count is 0)
            return;

        var copy = new UrlPatternCollection(collection);
        FuzzAssert.Equal(collection.Count, copy.Count, "The copy of the collection does not have the same number of patterns");
        var maxLength = collection.Min(GetMaxInputLength);
        foreach (var input in testCase.Inputs)
        {
            if (input.GetLength(testCase.InputBaseUrl) > maxLength)
                continue;

            if (input.Init is not null)
            {
                var init = ToInit(input.Init, testCase.InputBaseUrl);
                CheckCollection(collection, input.ToString(), pattern => pattern.IsMatch(init), pattern => pattern.Match(init), collection.IsMatch(init), findPattern: null, collection.Match(init));
                FuzzAssert.Equal(collection.IsMatch(init), copy.IsMatch(init), $"The copy of the collection does not give the same match for {input}");
                continue;
            }

            var url = input.Url!;
            var baseUrl = testCase.InputBaseUrl;
            var expected = CheckCollection(collection, input.ToString(), pattern => pattern.IsMatch(url, baseUrl), pattern => pattern.Match(url, baseUrl), collection.IsMatch(url, baseUrl), () => collection.FindPattern(url, baseUrl), collection.Match(url, baseUrl));
            FuzzAssert.True(ReferenceEquals(expected, copy.FindPattern(url, baseUrl)), $"The copy of the collection does not give the same pattern for {input}");
            if (baseUrl is null)
            {
                FuzzAssert.Equal(expected is not null, collection.IsMatch(url), $"IsMatch(string) and IsMatch(string, null) disagree on {input}");
                FuzzAssert.True(ReferenceEquals(expected, collection.FindPattern(url)), $"FindPattern(string) and FindPattern(string, null) disagree on {input}");
                FuzzAssert.Equal(FormatResult(expected?.Match(url)), FormatResult(collection.Match(url)), $"Match(string) and Match(string, null) disagree on {input}");

                if (System.Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var expectedUri = CheckCollection(collection, input + " (Uri)", pattern => pattern.IsMatch(uri), pattern => pattern.Match(uri), collection.IsMatch(uri), () => collection.FindPattern(uri), collection.Match(uri));

                    // A Uri is matched as the text it was created from when that text is a URL with the same scheme
                    if (UrlRecord.TryParse(url, baseUrl: null, out var record) && string.Equals(record.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase))
                    {
                        FuzzAssert.True(ReferenceEquals(expected, expectedUri), $"FindPattern(Uri) and FindPattern(string) disagree on {input}");
                    }
                }
            }

            // The components of a URL with a special scheme are canonical, so an init made of them is the same URL. The
            // hostname of an init is canonicalized as the host of a special URL, and the hostnames that depend on IDNA are
            // excluded, as in uri-urlpattern-webplatform.
            if (MatchAll.Match(url, baseUrl) is { } components && components.Protocol.Input is not "file" && SpecialSchemes.Contains(components.Protocol.Input) && !IsSensitiveUrl(url) && !IsSensitiveUrl(baseUrl))
            {
                var init = new UrlPatternInit
                {
                    Protocol = components.Protocol.Input,
                    Username = components.Username.Input,
                    Password = components.Password.Input,
                    Hostname = components.Hostname.Input,
                    Port = components.Port.Input,
                    Pathname = components.Pathname.Input,

                    // Processing an init strips a '?' that starts the search and a '#' that starts the hash
                    Search = "?" + components.Search.Input,
                    Hash = "#" + components.Hash.Input,
                };
                var expectedInit = CheckCollection(collection, input + " (init)", pattern => pattern.IsMatch(init), pattern => pattern.Match(init), collection.IsMatch(init), findPattern: null, collection.Match(init));
                FuzzAssert.True(ReferenceEquals(expected, expectedInit), $"The init made of the components of {input} does not match the same pattern: {FormatPatternOrNull(expected)} and {FormatPatternOrNull(expectedInit)}");
            }
        }
    }

    /// <summary>Checks that the results of the collection are those of the first pattern that matches, and returns it.</summary>
    private static UrlPattern? CheckCollection(UrlPatternCollection collection, string input, Func<UrlPattern, bool> isMatch, Func<UrlPattern, UrlPatternResult?> match, bool collectionIsMatch, Func<UrlPattern?>? findPattern, UrlPatternResult? collectionMatch)
    {
        var expected = collection.FirstOrDefault(isMatch);
        FuzzAssert.Equal(expected is not null, collectionIsMatch, $"IsMatch disagrees with the patterns of the collection on {input}");
        if (findPattern is not null)
        {
            var found = findPattern();
            FuzzAssert.True(ReferenceEquals(expected, found), $"FindPattern is not the first pattern that matches {input}: {FormatPatternOrNull(found)} instead of {FormatPatternOrNull(expected)}");
        }

        FuzzAssert.Equal(FormatResult(expected is null ? null : match(expected)), FormatResult(collectionMatch), $"Match is not the match of the first pattern that matches {input}");
        return expected;
    }

    private static string FormatPatternOrNull(UrlPattern? pattern) => pattern is null ? "null" : FormatPattern(pattern);

    private static string FormatResult(UrlPatternResult? result)
    {
        if (result is null)
            return "null";

        return string.Join(" ", Enumerable.Range(0, ComponentNames.Length).Select(i => ComponentNames[i] + "=" + FuzzAssert.Format(GetComponent(result, i).Input) + " [" + FormatGroups(GetComponent(result, i).Groups) + "]"));
    }

    private static IEnumerable<FuzzSeed> GetUrlPatternCollectionSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        yield return new FuzzSeed("doc", new CollectionCase
        {
            Patterns = [new() { PatternString = "https://example.com/api/users/:id" }, new() { PatternString = "https://example.com/api/products/:id" }, new() { PatternString = "https://example.com/static/*" }],
            Inputs = [new("https://example.com/api/users/123", null), new("https://example.com/api/products/456", null), new("https://example.com/static/a/b", null), new("https://example.com/other", null)],
        }.Encode());
        yield return new FuzzSeed("overlapping", new CollectionCase
        {
            Patterns = [new() { PatternString = "/books/:id", PatternBaseUrl = "https://example.com" }, new() { PatternInit = Init(pathname: "/books/*") }, new() { PatternString = "https://EXAMPLE.com/BOOKS/*", IgnoreCase = true }],
            InputBaseUrl = "https://example.com/",
            Inputs = [new("books/1", null), new("/BOOKS/2", null), new("https://example.org/books/3", null)],
        }.Encode());

        // Each test case of the URL pattern target, between the patterns of the previous and the next test cases, so that
        // another pattern may match its inputs first
        var cases = GetUrlPatternSeeds().Select(seed => seed.Data.Span.IsEmpty ? null : PatternCase.Read(seed.Data.Span)).OfType<PatternCase>().ToList();
        for (var i = 0; i < cases.Count; i++)
        {
            var patterns = new List<PatternCase>();
            if (i > 0)
            {
                patterns.Add(cases[i - 1]);
            }

            patterns.Add(cases[i]);
            if (i + 1 < cases.Count)
            {
                patterns.Add(cases[i + 1]);
            }

            yield return new FuzzSeed("case-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), new CollectionCase { Patterns = patterns, InputBaseUrl = cases[i].InputBaseUrl, Inputs = cases[i].Inputs }.Encode());
        }
    }

    [Flags]
    private enum CollectionFlags : byte
    {
        None = 0,
        InputInit = 1,
        InputBaseUrl = 2,
    }

    [Flags]
    private enum CollectionPatternKind : byte
    {
        String = 0,
        Init = 1,
        BaseUrl = 2,
        IgnoreCase = 4,
    }

    /// <summary>
    /// Patterns and the inputs they are matched against (all strings, or all inits). The input starts with a <see cref="CollectionFlags"/>
    /// byte whose bits 4 and 5 are the number of patterns minus one. Each pattern is a <see cref="CollectionPatternKind"/> byte, the pattern
    /// (a string, or an init as in <see cref="PatternCase"/>), and its base URL. Then come the base URL of the inputs and the inputs.
    /// </summary>
    private sealed class CollectionCase
    {
        public List<PatternCase> Patterns { get; init; } = [];

        public string? InputBaseUrl { get; init; }

        public List<PatternCaseInput> Inputs { get; init; } = [];

        public static CollectionCase Read(ReadOnlySpan<byte> data)
        {
            var header = FuzzInput.ReadByte(ref data);
            var flags = (CollectionFlags)(header & 0x0F);
            var patternCount = ((header >> 4) & 0x03) + 1;
            var patterns = new List<PatternCase>();
            for (var i = 0; i < patternCount && !data.IsEmpty; i++)
            {
                var kind = (CollectionPatternKind)FuzzInput.ReadByte(ref data);
                string? patternString = null;
                string?[]? patternInit = null;
                if (kind.HasFlag(CollectionPatternKind.Init))
                {
                    patternInit = ReadPatternInit(ref data);
                }
                else
                {
                    patternString = ReadPatternText(ref data);
                }

                patterns.Add(new PatternCase
                {
                    IgnoreCase = kind.HasFlag(CollectionPatternKind.IgnoreCase),
                    PatternString = patternString,
                    PatternInit = patternInit,
                    PatternBaseUrl = kind.HasFlag(CollectionPatternKind.BaseUrl) ? ReadPatternText(ref data) : null,
                });
            }

            var inputBaseUrl = flags.HasFlag(CollectionFlags.InputBaseUrl) ? ReadPatternText(ref data) : null;
            var inputs = new List<PatternCaseInput>();
            while (!data.IsEmpty && inputs.Count < MaxPatternInputs)
            {
                inputs.Add(flags.HasFlag(CollectionFlags.InputInit) ? new PatternCaseInput(null, ReadPatternInit(ref data)) : new PatternCaseInput(ReadPatternText(ref data), null));
            }

            return new CollectionCase { Patterns = patterns, InputBaseUrl = inputBaseUrl, Inputs = inputs };
        }

        public byte[] Encode()
        {
            var flags = CollectionFlags.None;
            if (InputBaseUrl is not null)
            {
                flags |= CollectionFlags.InputBaseUrl;
            }

            if (Inputs.Any(input => input.Init is not null))
            {
                flags |= CollectionFlags.InputInit;
            }

            var bytes = new List<byte> { (byte)((byte)flags | ((Math.Min(Patterns.Count, MaxCollectionPatterns) - 1) << 4)) };
            foreach (var pattern in Patterns.Take(MaxCollectionPatterns))
            {
                var kind = CollectionPatternKind.String;
                if (pattern.PatternInit is not null)
                {
                    kind |= CollectionPatternKind.Init;
                }

                if (pattern.PatternBaseUrl is not null)
                {
                    kind |= CollectionPatternKind.BaseUrl;
                }

                if (pattern.IgnoreCase)
                {
                    kind |= CollectionPatternKind.IgnoreCase;
                }

                bytes.Add((byte)kind);
                if (pattern.PatternInit is not null)
                {
                    AppendPatternInit(bytes, pattern.PatternInit);
                }
                else
                {
                    AppendPatternText(bytes, pattern.PatternString!);
                }

                if (pattern.PatternBaseUrl is not null)
                {
                    AppendPatternText(bytes, pattern.PatternBaseUrl);
                }
            }

            if (InputBaseUrl is not null)
            {
                AppendPatternText(bytes, InputBaseUrl);
            }

            foreach (var input in Inputs)
            {
                if (flags.HasFlag(CollectionFlags.InputInit))
                {
                    AppendPatternInit(bytes, input.Init ?? new string?[ComponentNames.Length]);
                }
                else
                {
                    AppendPatternText(bytes, input.Url!);
                }
            }

            return [.. bytes];
        }
    }
}
