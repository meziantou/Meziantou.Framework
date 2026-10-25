using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.UrlPatternInternal;
using WebTypeErrorException = Meziantou.Framework.WebPlatform.Dom.TypeErrorException;
using WebUrlPattern = Meziantou.Framework.WebPlatform.Dom.UrlPattern;
using WebUrlPatternComponentResult = Meziantou.Framework.WebPlatform.Dom.UrlPatternComponentResult;
using WebUrlPatternInit = Meziantou.Framework.WebPlatform.Dom.UrlPatternInit;
using WebUrlPatternOptions = Meziantou.Framework.WebPlatform.Dom.UrlPatternOptions;
using WebUrlPatternResult = Meziantou.Framework.WebPlatform.Dom.UrlPatternResult;

namespace UriFuzz;

public static partial class UriFuzzTargets
{
    // UrlPattern matches with a backtracking regular expression and no timeout (UrlPatternComponent passes
    // Timeout.InfiniteTimeSpan, so REGEX_DEFAULT_MATCH_TIMEOUT does not apply). k adjacent groups try about C(n + k, k) ways
    // to split n characters, a repeated group without a prefix (":id*" in a search: "((?:[\s\S]+?)*)") tries about 2^n,
    // and the regexp groups are user-supplied regular expressions that can backtrack exponentially, so the inputs are kept
    // short enough for the worst case to stay fast.
    private const double MaxBacktrackingSteps = 1_000_000;
    private const int MaxInputLengthWhenExponential = 16;
    private const int MaxPatternInputs = 4;

    // Creating a pattern matches its protocol against the special schemes, and "**" is a repeated full wildcard
    // ("((?:.*)*)"), so a protocol made of many '*' backtracks exponentially before any input is matched
    private const int MaxRepetitionCodePoints = 16;

    private static readonly string[] ComponentNames = ["protocol", "username", "password", "hostname", "port", "pathname", "search", "hash"];

    private static readonly UrlPattern MatchAll = UrlPattern.Create(new UrlPatternInit());

    /// <summary>
    /// Creates a URL pattern (from a string or an init, with or without a base URL, ignoring the case or not) and matches it
    /// against URLs (strings, with or without a base URL, <see cref="System.Uri"/>) or inits. An invalid pattern only throws
    /// <see cref="UrlPatternException"/>, and matching never throws. The pattern strings of the components are a fixed point:
    /// a pattern created from them has the same pattern strings and matches the same inputs. <c>IsMatch</c> agrees with
    /// <c>Match</c>, a match reports the components of the URL as the match-everything pattern does, a group is a substring
    /// of its component, and ignoring the case only adds matches.
    /// </summary>
    private static void UrlPatternTarget(ReadOnlySpan<byte> data)
    {
        var testCase = PatternCase.Read(data);
        if (testCase.CountRepetitionCodePoints() > MaxRepetitionCodePoints)
            return;

        var pattern = testCase.CreatePattern();
        if (pattern is null)
            return;

        // An escaped "\[" is an IPv6 hostname pattern, whose pattern string "[" is too short to be one, and is not a valid
        // hostname, as in the spec
        if (pattern.Hostname is "[")
            return;

        var options = new UrlPatternOptions { IgnoreCase = testCase.IgnoreCase };

        // A fixed text that canonicalizes to nothing outside of a group (a tab, "a/..") is an empty part, as in the spec,
        // and the part that follows it is written as if nothing preceded it: "(f)\t*?" is written "(f)*?", which does not
        // parse, and ":a\t(b)" is written ":a(b)", which is another pattern. Likewise, an empty group ("8200x{}?40" in a
        // port) writes nothing between two fixed parts, which are read back as one ("820040")
        var mayHaveSilentSeparator = testCase.MayHaveSilentSeparator();
        var fixedPoint = CreateFromPatternStrings(pattern, options);
        if (fixedPoint is null)
        {
            if (mayHaveSilentSeparator)
                return;

            FuzzAssert.Fail($"The pattern strings of the pattern do not compile: {FormatPattern(pattern)}");
        }

        var again = CreateFromPatternStrings(fixedPoint, options);
        FuzzAssert.True(again is not null, $"The pattern strings of the pattern created from the pattern strings do not compile: {FormatPattern(fixedPoint)}");
        FuzzAssert.Equal(FormatPattern(fixedPoint), FormatPattern(again), "The pattern strings are not a fixed point after one round");

        // A default port is only dropped when the pattern spells it as the default port ("080" is kept as "80"), as the
        // spec does, so the pattern created from the pattern strings drops it. A group whose fixed text canonicalizes to
        // nothing ("{\t}+") is written as "{}+", which parses into no part, as the spec does.
        var portDropped = pattern.Port.Length > 0 && fixedPoint.Port.Length == 0 && SpecialSchemes.TryGetDefaultPort(pattern.Protocol, out var defaultPort) && pattern.Port == defaultPort;
        if (!mayHaveSilentSeparator && !FormatPattern(pattern).Contains("{}", StringComparison.Ordinal))
        {
            FuzzAssert.Equal(FormatPattern(pattern, portDropped), FormatPattern(fixedPoint), "The pattern strings are not a fixed point");
        }

        FuzzAssert.Equal(pattern.HasRegExpGroups, fixedPoint.HasRegExpGroups, "The pattern created from the pattern strings does not have the same regexp groups");

        UrlPattern? ignoreCase = null;
        if (!testCase.IgnoreCase && !pattern.HasRegExpGroups)
        {
            ignoreCase = testCase.CreatePattern(ignoreCase: true);
            FuzzAssert.True(ignoreCase is not null, "The pattern cannot be created when ignoring the case");
        }

        var maxLength = GetMaxInputLength(pattern);
        foreach (var input in testCase.Inputs)
        {
            if (input.GetLength(testCase.InputBaseUrl) > maxLength)
                continue;

            var result = input.Match(pattern, testCase.InputBaseUrl);
            var isMatch = input.IsMatch(pattern, testCase.InputBaseUrl);
            FuzzAssert.Equal(result is not null, isMatch, $"IsMatch and Match disagree on {input}");
            if (!portDropped && !mayHaveSilentSeparator)
            {
                FuzzAssert.Equal(isMatch, input.IsMatch(fixedPoint, testCase.InputBaseUrl), $"The pattern created from the pattern strings does not give the same match for {input}");
            }

            if (result is not null)
            {
                CheckGroups(result);
                if (input.Url is not null)
                {
                    var everything = input.Match(MatchAll, testCase.InputBaseUrl);
                    FuzzAssert.True(everything is not null, $"The match-everything pattern does not match {input}, which matches {FormatPattern(pattern)}");
                    for (var i = 0; i < ComponentNames.Length; i++)
                    {
                        FuzzAssert.Equal(GetComponent(everything, i).Input, GetComponent(result, i).Input, $"The {ComponentNames[i]} of {input} depends on the pattern");
                    }
                }
            }

            if (ignoreCase is not null && isMatch)
            {
                FuzzAssert.True(input.IsMatch(ignoreCase, testCase.InputBaseUrl), $"{input} matches the case-sensitive pattern, but not the one that ignores the case");
            }

            if (input.Url is not null && testCase.InputBaseUrl is null && System.Uri.TryCreate(input.Url, UriKind.Absolute, out var uri))
            {
                FuzzAssert.Equal(pattern.Match(uri) is not null, pattern.IsMatch(uri), $"IsMatch(Uri) and Match(Uri) disagree on {input}");
            }
        }
    }

    /// <summary>
    /// Creates the same URL pattern with Meziantou.Framework.Uri and with WebPlatform, which both implement the URL Pattern
    /// spec, and matches the same inputs. Both accept or reject the pattern, give the same pattern strings, and give the same
    /// matches with the same groups. The patterns that have a regexp group are excluded (they are ECMAScript regular
    /// expressions, which WebPlatform translates and UrlPattern does not), as are the hostnames whose canonicalization
    /// depends on IDNA (the two use different IDNA implementations) or on IPv4 (UrlPattern deliberately does not rewrite the
    /// IPv4 addresses of a pattern, see <see cref="UrlCanonicalizer.CanonicalizeHostname"/>).
    /// </summary>
    private static void UrlPatternWebPlatformTarget(ReadOnlySpan<byte> data)
    {
        var testCase = PatternCase.Read(data).ToScalarValueStrings();
        if (testCase.ContainsRegExpGroup() || testCase.InputBaseUrl is "" || testCase.CountRepetitionCodePoints() > MaxRepetitionCodePoints)
            return;

        var pattern = testCase.CreatePattern();
        var webPattern = testCase.CreateWebPattern();
        if (pattern is null || webPattern is null)
        {
            if ((pattern is null) != (webPattern is null) && !testCase.HasSensitiveHostname() && !testCase.HasUnassignedCodePoint() && !(pattern is null && testCase.HasProtocolWithColon()))
            {
                FuzzAssert.Fail($"Only {(pattern is null ? "WebPlatform" : "UrlPattern")} accepts the pattern {testCase}");
            }

            return;
        }

        if (pattern.Hostname != webPattern.Hostname && IsSensitiveHostname(pattern.Hostname))
            return;

        if (FormatPattern(webPattern) != FormatPattern(pattern) && testCase.HasUnassignedCodePoint())
            return;

        // The spec drops the default port of a special scheme by comparing the protocol as written, so "hTtp" keeps the
        // port "80" for WebPlatform, while UrlPattern compares the protocol ignoring the case
        if (webPattern.Port != pattern.Port && pattern.Port.Length == 0 && testCase.HasUpperCaseProtocol())
            return;

        FuzzAssert.Equal(FormatPattern(webPattern), FormatPattern(pattern), $"The pattern strings of {testCase} differ");
        FuzzAssert.Equal(webPattern.HasRegExpGroups, pattern.HasRegExpGroups, $"HasRegExpGroups of {testCase} differs");

        var maxLength = GetMaxInputLength(pattern);
        foreach (var input in testCase.Inputs)
        {
            if (input.GetLength(testCase.InputBaseUrl) > maxLength)
                continue;

            var result = input.Match(pattern, testCase.InputBaseUrl);
            WebUrlPatternResult? webResult;
            try
            {
                webResult = input.Match(webPattern, testCase.InputBaseUrl);
            }
            catch (RegexMatchTimeoutException)
            {
                // WebPlatform matches with a timeout (see Program.cs), and its translated regular expressions backtrack differently
                continue;
            }

            if ((result is null) != (webResult is null))
            {
                if (input.HasSensitiveHostname(testCase.InputBaseUrl) || (result is null && input.HasProtocolWithColon()))
                    continue;

                FuzzAssert.Fail($"Only {(result is null ? "WebPlatform" : "UrlPattern")} matches {input} with the pattern {testCase}");
            }

            if (result is null || webResult is null)
                continue;

            if (result.Hostname.Input != webResult.Hostname.Input && input.HasSensitiveHostname(testCase.InputBaseUrl))
                continue;

            for (var i = 0; i < ComponentNames.Length; i++)
            {
                var component = GetComponent(result, i);
                var webComponent = GetComponent(webResult, i);
                FuzzAssert.Equal(webComponent.Input, component.Input, $"The {ComponentNames[i]} of {input} differs (pattern {testCase})");
                FuzzAssert.Equal(FormatGroups(webComponent.Groups), FormatGroups(component.Groups), $"The {ComponentNames[i]} groups of {input} differ (pattern {testCase})");
            }
        }
    }

    private static UrlPattern? CreateFromPatternStrings(UrlPattern pattern, UrlPatternOptions options)
    {
        try
        {
            return UrlPattern.Create(new UrlPatternInit
            {
                // Processing an init strips a ':' that ends the protocol, a '?' that starts the search and a '#' that starts the hash
                Protocol = pattern.Protocol + ":",
                Username = pattern.Username,
                Password = pattern.Password,
                Hostname = pattern.Hostname,
                Port = pattern.Port,
                Pathname = pattern.Pathname,
                Search = "?" + pattern.Search,
                Hash = "#" + pattern.Hash,
            }, options);
        }
        catch (UrlPatternException)
        {
            return null;
        }
    }

    private static void CheckGroups(UrlPatternResult result)
    {
        for (var i = 0; i < ComponentNames.Length; i++)
        {
            var component = GetComponent(result, i);
            foreach (var (name, value) in component.Groups)
            {
                if (value is not null)
                {
                    FuzzAssert.True(component.Input.Contains(value, StringComparison.Ordinal), $"The group {FuzzAssert.Format(name)} {FuzzAssert.Format(value)} is not a substring of the {ComponentNames[i]} {FuzzAssert.Format(component.Input)}");
                }
            }
        }
    }

    private static int GetMaxInputLength(UrlPattern pattern)
    {
        var groups = 0;
        var isExponential = pattern.HasRegExpGroups;
        for (var i = 0; i < ComponentNames.Length; i++)
        {
            var patternString = GetPatternString(pattern, i);
            groups += patternString.Count(c => c is '*' or ':' or '(');

            // A '*' or '+' modifier repeats a group (a full wildcard '*' only counts as a group)
            isExponential |= patternString.AsSpan().IndexOfAny(":({") >= 0 && patternString.AsSpan().IndexOfAny("*+") >= 0;
        }

        if (!isExponential && groups <= 1)
            return int.MaxValue;

        var maxLength = isExponential ? MaxInputLengthWhenExponential : int.MaxValue;

        // The largest n for which 2^n C(n + k, k) (or C(n + k, k)) stays under the budget
        var length = 0;
        var steps = 1.0;
        while (length < maxLength)
        {
            steps = steps * (length + 1 + groups) / (length + 1) * (isExponential ? 2 : 1);
            if (steps > MaxBacktrackingSteps)
                break;

            length++;
        }

        return length;
    }

    private static string GetPatternString(UrlPattern pattern, int index) => index switch
    {
        0 => pattern.Protocol,
        1 => pattern.Username,
        2 => pattern.Password,
        3 => pattern.Hostname,
        4 => pattern.Port,
        5 => pattern.Pathname,
        6 => pattern.Search,
        _ => pattern.Hash,
    };

    private static UrlPatternComponentResult GetComponent(UrlPatternResult result, int index) => index switch
    {
        0 => result.Protocol,
        1 => result.Username,
        2 => result.Password,
        3 => result.Hostname,
        4 => result.Port,
        5 => result.Pathname,
        6 => result.Search,
        _ => result.Hash,
    };

    private static WebUrlPatternComponentResult GetComponent(WebUrlPatternResult result, int index) => index switch
    {
        0 => result.Protocol,
        1 => result.Username,
        2 => result.Password,
        3 => result.Hostname,
        4 => result.Port,
        5 => result.Pathname,
        6 => result.Search,
        _ => result.Hash,
    };

    private static string FormatPattern(UrlPattern pattern, bool withoutPort = false)
        => string.Join(" ", Enumerable.Range(0, ComponentNames.Length).Select(i => ComponentNames[i] + "=" + FuzzAssert.Format(i is 4 && withoutPort ? "" : GetPatternString(pattern, i))));

    private static string FormatPattern(WebUrlPattern pattern)
        => string.Join(" ", new[] { pattern.Protocol, pattern.Username, pattern.Password, pattern.Hostname, pattern.Port, pattern.Pathname, pattern.Search, pattern.Hash }.Select((value, i) => ComponentNames[i] + "=" + FuzzAssert.Format(value)));

    private static string FormatGroups(IReadOnlyDictionary<string, string?> groups)
        => string.Join(", ", groups.OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => FuzzAssert.Format(group.Key) + ": " + FuzzAssert.Format(group.Value)));

    // Hostnames that UrlPattern and WebPlatform may canonicalize differently on purpose: IDNA (IdnMapping and Meziantou.Framework.Unicode
    // implement different versions of UTS #46, and IdnMapping depends on ICU), and IPv4 (UrlPattern does not rewrite the IPv4 addresses of a pattern)
    private static bool IsSensitiveHostname(string? hostname)
        => hostname is not null && (!System.Text.Ascii.IsValid(hostname) || hostname.Contains('%', StringComparison.Ordinal) || hostname.Contains("xn--", StringComparison.OrdinalIgnoreCase) || (!hostname.StartsWith('[', StringComparison.Ordinal) && hostname.Any(char.IsAsciiDigit)));

    private static bool ContainsUnassignedCodePoint(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.OtherNotAssigned)
                return true;
        }

        return false;
    }

    // Processing an init strips one ':' that ends the protocol
    private static bool IsProtocolWithColon(string? protocol)
        => protocol is not null && (protocol.EndsWith(':', StringComparison.Ordinal) ? protocol[..^1] : protocol).Contains(':', StringComparison.Ordinal);

    private static bool IsSensitiveUrl(string? url)
        => url is not null && (!System.Text.Ascii.IsValid(url) || url.Contains('%', StringComparison.Ordinal) || url.Contains("xn--", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<FuzzSeed> GetUrlPatternSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        yield return new FuzzSeed("string", Encode([0], "https://example.com/books/:id", "https://example.com/books/123", "https://example.com/books/", "https://EXAMPLE.com/books/a%20b?c#d"));
        yield return new FuzzSeed("string-ignore-case", Encode([(byte)PatternFlags.IgnoreCase], "https://example.com/Books/:id(\\d+)", "https://example.com/BOOKS/42", "https://example.com/books/x"));
        yield return new FuzzSeed("string-base", Encode([(byte)(PatternFlags.PatternBaseUrl | PatternFlags.InputBaseUrl)], "/:id/*?", "https://example.com/a/b", "https://example.com/c/", "x/y/z", "/x?y#z"));
        yield return new FuzzSeed("init", new PatternCase { PatternInit = Init(hostname: "{*.}?example.com", pathname: "/items{/:category}?"), Inputs = [new("https://www.example.com/items/books", null), new("http://example.com/items", null)] }.Encode());
        yield return new FuzzSeed("init-inputs", new PatternCase { PatternInit = Init(pathname: "/foo/:bar+"), Inputs = [new(null, Init(pathname: "/foo/a/b/c")), new(null, Init(protocol: "foo", pathname: "/foo/a"))] }.Encode());
        yield return new FuzzSeed("init-base", new PatternCase { PatternInit = Init(pathname: "c/*"), PatternBaseUrl = "https://example.com/a/b", InputBaseUrl = "https://example.com/a/", Inputs = [new(null, Init(pathname: "c/d"))] }.Encode());
        yield return new FuzzSeed("init-ipv6", new PatternCase { PatternInit = Init(hostname: @"[\:\:ab]"), Inputs = [new(null, Init(hostname: "[0:0:0:0:0:0:0:AB]")), new(null, Init(hostname: "[::ab"))] }.Encode());
        yield return new FuzzSeed("init-dot-segments", new PatternCase { PatternInit = Init(pathname: "a/../.."), Inputs = [new(null, Init(pathname: "b\\.."))] }.Encode());

        foreach (var literal in GetStringLiterals("UrlPatternTests.cs"))
        {
            yield return new FuzzSeed("literal", Encode([0], literal, "https://example.com/books/123", literal));
        }

        using var document = FuzzResources.ReadJson(typeof(UriFuzzTargets).Assembly, "urlpatterntestdata.json");
        var index = 0;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            index++;
            if (entry.ValueKind is not JsonValueKind.Object || PatternCase.FromJson(entry) is not { } testCase)
                continue;

            yield return new FuzzSeed("wpt-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), testCase.Encode());
        }
    }

    [Flags]
    private enum PatternFlags : byte
    {
        None = 0,
        IgnoreCase = 1,
        PatternInit = 2,
        PatternBaseUrl = 4,
        InputInit = 8,
        InputBaseUrl = 16,
    }

    /// <summary>
    /// A pattern and the inputs it is matched against (all strings, or all inits). The input starts with a <see cref="PatternFlags"/> byte. An init is a
    /// byte whose bits say which components it has, followed by these components. The texts are separated by <see cref="Separator"/>:
    /// the pattern (a string or an init), the base URL of the pattern, the base URL of the inputs, and the inputs (strings or inits).
    /// </summary>
    private sealed class PatternCase
    {
        public bool IgnoreCase { get; init; }

        public string? PatternString { get; init; }

        public string?[]? PatternInit { get; init; }

        public string? PatternBaseUrl { get; init; }

        public string? InputBaseUrl { get; init; }

        public List<PatternCaseInput> Inputs { get; init; } = [];

        public static PatternCase Read(ReadOnlySpan<byte> data)
        {
            var flags = (PatternFlags)FuzzInput.ReadByte(ref data);
            string? patternString = null;
            string?[]? patternInit = null;
            if (flags.HasFlag(PatternFlags.PatternInit))
            {
                patternInit = ReadPatternInit(ref data);
            }
            else
            {
                patternString = ReadPatternText(ref data);
            }

            var patternBaseUrl = flags.HasFlag(PatternFlags.PatternBaseUrl) ? ReadPatternText(ref data) : null;
            var inputBaseUrl = flags.HasFlag(PatternFlags.InputBaseUrl) ? ReadPatternText(ref data) : null;
            var inputs = new List<PatternCaseInput>();
            while (!data.IsEmpty && inputs.Count < MaxPatternInputs)
            {
                inputs.Add(flags.HasFlag(PatternFlags.InputInit) ? new PatternCaseInput(null, ReadPatternInit(ref data)) : new PatternCaseInput(ReadPatternText(ref data), null));
            }

            return new PatternCase
            {
                IgnoreCase = flags.HasFlag(PatternFlags.IgnoreCase),
                PatternString = patternString,
                PatternInit = patternInit,
                PatternBaseUrl = patternBaseUrl,
                InputBaseUrl = inputBaseUrl,
                Inputs = inputs,
            };
        }

        /// <summary>Converts a test case of the WPT corpus (urlpatterntestdata.json), or returns null when it cannot be represented.</summary>
        public static PatternCase? FromJson(JsonElement entry)
        {
            try
            {
                var pattern = entry.GetProperty("pattern");
                var ignoreCase = false;
                string? patternString = null;
                string?[]? patternInit = null;
                string? patternBaseUrl = null;
                foreach (var (item, position) in pattern.EnumerateArray().Select((item, position) => (item, position)))
                {
                    switch (item.ValueKind)
                    {
                        case JsonValueKind.String when position is 0:
                            patternString = item.GetString();
                            break;
                        case JsonValueKind.String:
                            patternBaseUrl = item.GetString();
                            break;
                        case JsonValueKind.Object when position is 0:
                            patternInit = ReadInit(item, out patternBaseUrl);
                            break;
                        case JsonValueKind.Object:
                            ignoreCase = item.TryGetProperty("ignoreCase", out var value) && value.ValueKind is JsonValueKind.True;
                            break;
                        default:
                            return null;
                    }
                }

                if (patternString is null && patternInit is null)
                    return null;

                string? inputBaseUrl = null;
                var inputs = new List<PatternCaseInput>();
                if (entry.TryGetProperty("inputs", out var inputsElement))
                {
                    foreach (var (item, position) in inputsElement.EnumerateArray().Select((item, position) => (item, position)))
                    {
                        switch (item.ValueKind)
                        {
                            case JsonValueKind.String when position is 0:
                                inputs.Add(new PatternCaseInput(item.GetString(), null));
                                break;
                            case JsonValueKind.String when inputs is [{ Url: not null }]:
                                inputBaseUrl = item.GetString();
                                break;
                            case JsonValueKind.Object when position is 0:
                                inputs.Add(new PatternCaseInput(null, ReadInit(item, out var baseUrl)));
                                inputBaseUrl = baseUrl;
                                break;
                            default:
                                // An init with a base URL argument is a TypeError, which the C# API cannot express
                                break;
                        }
                    }
                }

                return new PatternCase
                {
                    IgnoreCase = ignoreCase,
                    PatternString = patternString,
                    PatternInit = patternInit,
                    PatternBaseUrl = patternBaseUrl,
                    InputBaseUrl = inputBaseUrl,
                    Inputs = inputs,
                };
            }
            catch (InvalidOperationException)
            {
                // JsonElement.GetString refuses the unpaired surrogates of a few cases
                return null;
            }

            static string?[] ReadInit(JsonElement element, out string? baseUrl)
            {
                var init = new string?[ComponentNames.Length];
                for (var i = 0; i < init.Length; i++)
                {
                    if (element.TryGetProperty(ComponentNames[i], out var value))
                    {
                        init[i] = value.GetString();
                    }
                }

                baseUrl = element.TryGetProperty("baseURL", out var baseUrlElement) ? baseUrlElement.GetString() : null;
                return init;
            }
        }

        public byte[] Encode()
        {
            var flags = PatternFlags.None;
            var bytes = new List<byte> { 0 };
            if (IgnoreCase)
            {
                flags |= PatternFlags.IgnoreCase;
            }

            if (PatternInit is not null)
            {
                flags |= PatternFlags.PatternInit;
                AppendPatternInit(bytes, PatternInit);
            }
            else
            {
                AppendPatternText(bytes, PatternString!);
            }

            if (PatternBaseUrl is not null)
            {
                flags |= PatternFlags.PatternBaseUrl;
                AppendPatternText(bytes, PatternBaseUrl);
            }

            if (InputBaseUrl is not null)
            {
                flags |= PatternFlags.InputBaseUrl;
                AppendPatternText(bytes, InputBaseUrl);
            }

            foreach (var input in Inputs)
            {
                if (input.Init is not null)
                {
                    flags |= PatternFlags.InputInit;
                    AppendPatternInit(bytes, input.Init);
                }
                else
                {
                    AppendPatternText(bytes, input.Url!);
                }
            }

            bytes[0] = (byte)flags;
            return [.. bytes];
        }

        public PatternCase ToScalarValueStrings() => new()
        {
            IgnoreCase = IgnoreCase,
            PatternString = PatternString is null ? null : ToScalarValueString(PatternString),
            PatternInit = PatternInit?.Select(value => value is null ? null : ToScalarValueString(value)).ToArray(),
            PatternBaseUrl = PatternBaseUrl is null ? null : ToScalarValueString(PatternBaseUrl),
            InputBaseUrl = InputBaseUrl is null ? null : ToScalarValueString(InputBaseUrl),
            Inputs = [.. Inputs.Select(input => input.ToScalarValueStrings())],
        };

        // The valid name code points (ID_Start, ID_Continue) come from the Unicode version of .NET for UrlPattern, and from
        // Meziantou.Framework.Unicode for WebPlatform, which is more recent: a code point that .NET does not know may be a
        // name code point for WebPlatform only
        public bool HasUnassignedCodePoint()
            => new[] { PatternString, PatternBaseUrl }.Concat(PatternInit ?? []).Any(value => value is not null && ContainsUnassignedCodePoint(value));

        // Two fixed parts are written next to each other, and read back as one, when what separates them writes nothing: a
        // fixed text that canonicalizes to nothing (a tab, "a/..") or an empty group ("{}?")
        public bool MayHaveSilentSeparator()
            => new[] { PatternString, PatternBaseUrl }.Concat(PatternInit ?? []).Any(value => value is not null && (value.AsSpan().IndexOfAny("\t\n\r") >= 0 || value.Contains("..", StringComparison.Ordinal) || value.Contains("%2e", StringComparison.OrdinalIgnoreCase) || value.Contains("{}", StringComparison.Ordinal)));

        public bool HasUpperCaseProtocol()
        {
            if (PatternInit is not null)
                return PatternInit[0]?.Any(char.IsAsciiLetterUpper) is true;

            try
            {
                return new ConstructorStringParser(PatternString!).Parse().Protocol?.Any(char.IsAsciiLetterUpper) is true;
            }
            catch (UrlPatternException)
            {
                return false;
            }
        }

        public int CountRepetitionCodePoints()
            => new[] { PatternString, PatternBaseUrl }.Concat(PatternInit ?? []).Sum(value => value?.Count(c => c is '*' or '+') ?? 0);

        public bool ContainsRegExpGroup()
            => (PatternString ?? "").Contains('(', StringComparison.Ordinal) || (PatternInit ?? []).Any(value => value is not null && value.Contains('(', StringComparison.Ordinal));

        /// <summary>Determines whether the hostname of the pattern may be canonicalized differently by UrlPattern and WebPlatform.</summary>
        public bool HasSensitiveHostname()
        {
            if (PatternBaseUrl is not null && (IsSensitiveUrl(PatternBaseUrl) || (UrlRecord.TryParse(PatternBaseUrl, baseUrl: null, out var baseUrl) && IsSensitiveHostname(baseUrl.Host))))
                return true;

            if (PatternInit is not null)
                return IsSensitiveHostname(PatternInit[3]);

            try
            {
                return IsSensitiveHostname(new ConstructorStringParser(PatternString!).Parse().Hostname);
            }
            catch (UrlPatternException)
            {
                // The pattern string is invalid: its hostname may be what WebPlatform accepts
                return IsSensitiveUrl(PatternString) || PatternString!.Any(char.IsAsciiDigit);
            }
        }

        /// <summary>
        /// Determines whether the protocol of the pattern holds a ':'. The spec canonicalizes a protocol by parsing it followed
        /// by "://dummy.invalid/", so WebPlatform reads "a:b" as the scheme "a", which UrlPattern rejects.
        /// </summary>
        public bool HasProtocolWithColon()
        {
            if (PatternInit is not null)
                return IsProtocolWithColon(PatternInit[0]);

            try
            {
                return new ConstructorStringParser(PatternString!).Parse().Protocol?.Contains(':', StringComparison.Ordinal) is true;
            }
            catch (UrlPatternException)
            {
                return false;
            }
        }

        public UrlPattern? CreatePattern(bool? ignoreCase = null)
        {
            var options = new UrlPatternOptions { IgnoreCase = ignoreCase ?? IgnoreCase };
            try
            {
                if (PatternInit is not null)
                    return UrlPattern.Create(ToInit(PatternInit, PatternBaseUrl), options);

                return UrlPattern.Create(PatternString!, PatternBaseUrl, options);
            }
            catch (UrlPatternException)
            {
                return null;
            }
        }

        /// <summary>Adds the pattern to the collection with the overload that matches how it is written, and returns it.</summary>
        public UrlPattern AddTo(UrlPatternCollection collection)
        {
            if (IgnoreCase)
            {
                var options = new UrlPatternOptions { IgnoreCase = true };
                var pattern = PatternInit is not null ? UrlPattern.Create(ToInit(PatternInit, PatternBaseUrl), options) : UrlPattern.Create(PatternString!, PatternBaseUrl, options);
                collection.Add(pattern);
                return pattern;
            }

            if (PatternInit is not null)
                return collection.Add(ToInit(PatternInit, PatternBaseUrl));

            return PatternBaseUrl is null ? collection.Add(PatternString!) : collection.Add(PatternString!, PatternBaseUrl);
        }

        public WebUrlPattern? CreateWebPattern()
        {
            var options = new WebUrlPatternOptions { IgnoreCase = IgnoreCase };
            try
            {
                if (PatternInit is not null)
                    return new WebUrlPattern(ToWebInit(PatternInit, PatternBaseUrl), options);

                return PatternBaseUrl is null ? new WebUrlPattern(PatternString!, options) : new WebUrlPattern(PatternString!, PatternBaseUrl, options);
            }
            catch (WebTypeErrorException)
            {
                return null;
            }
        }

        public override string ToString()
        {
            var pattern = PatternInit is not null ? FormatInit(PatternInit) : FuzzAssert.Format(PatternString);
            return PatternBaseUrl is null ? pattern : $"{pattern} (base {FuzzAssert.Format(PatternBaseUrl)})";
        }
    }

    private sealed record PatternCaseInput(string? Url, string?[]? Init)
    {
        /// <summary>Gets an upper bound of the length of the matched components: a non-ASCII character is percent-encoded into up to 9 characters.</summary>
        public int GetLength(string? baseUrl) => GetEncodedLength(baseUrl) + (Url is not null ? GetEncodedLength(Url) : Init!.Sum(GetEncodedLength));

        private static int GetEncodedLength(string? value) => value?.Sum(c => char.IsAscii(c) ? 1 : 9) ?? 0;

        public PatternCaseInput ToScalarValueStrings()
            => new(Url is null ? null : ToScalarValueString(Url), Init?.Select(value => value is null ? null : ToScalarValueString(value)).ToArray());

        public bool HasSensitiveHostname(string? baseUrl) => IsSensitiveUrl(baseUrl) || (Url is not null ? IsSensitiveUrl(Url) : IsSensitiveHostname(Init![3]));

        // The spec reads the protocol "a:b" as the scheme "a", which UrlPattern rejects (see PatternCase.HasProtocolWithColon)
        public bool HasProtocolWithColon() => Init is not null && IsProtocolWithColon(Init[0]);

        // An init takes the base URL of the inputs as its baseURL member
        public UrlPatternResult? Match(UrlPattern pattern, string? baseUrl)
            => Url is not null ? pattern.Match(Url, baseUrl) : pattern.Match(ToInit(Init!, baseUrl));

        public bool IsMatch(UrlPattern pattern, string? baseUrl)
            => Url is not null ? pattern.IsMatch(Url, baseUrl) : pattern.IsMatch(ToInit(Init!, baseUrl));

        public WebUrlPatternResult? Match(WebUrlPattern pattern, string? baseUrl)
            => Url is not null ? pattern.Exec(Url, baseUrl) : pattern.Exec(ToWebInit(Init!, baseUrl));

        public override string ToString() => Url is not null ? FuzzAssert.Format(Url) : FormatInit(Init!);
    }

    private static string ReadPatternText(ref ReadOnlySpan<byte> data) => FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator));

    /// <summary>Reads an init: a byte whose bits say which components it has, followed by these components.</summary>
    private static string?[] ReadPatternInit(ref ReadOnlySpan<byte> data)
    {
        var mask = FuzzInput.ReadByte(ref data);
        var init = new string?[ComponentNames.Length];
        for (var i = 0; i < init.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                init[i] = ReadPatternText(ref data);
            }
        }

        return init;
    }

    private static void AppendPatternText(List<byte> bytes, string text)
    {
        bytes.AddRange(FuzzText.Encode(text));
        bytes.Add(Separator);
    }

    private static void AppendPatternInit(List<byte> bytes, string?[] init)
    {
        var mask = 0;
        for (var i = 0; i < init.Length; i++)
        {
            if (init[i] is not null)
            {
                mask |= 1 << i;
            }
        }

        bytes.Add((byte)mask);
        foreach (var component in init)
        {
            if (component is not null)
            {
                AppendPatternText(bytes, component);
            }
        }
    }

    private static string?[] Init(string? protocol = null, string? hostname = null, string? pathname = null) => [protocol, null, null, hostname, null, pathname, null, null];

    private static UrlPatternInit ToInit(string?[] init, string? baseUrl) => new()
    {
        Protocol = init[0],
        Username = init[1],
        Password = init[2],
        Hostname = init[3],
        Port = init[4],
        Pathname = init[5],
        Search = init[6],
        Hash = init[7],
        BaseUrl = baseUrl,
    };

    private static WebUrlPatternInit ToWebInit(string?[] init, string? baseUrl) => new()
    {
        Protocol = init[0],
        Username = init[1],
        Password = init[2],
        Hostname = init[3],
        Port = init[4],
        Pathname = init[5],
        Search = init[6],
        Hash = init[7],
        BaseUrl = baseUrl,
    };

    private static string FormatInit(string?[] init)
        => "{ " + string.Join(", ", init.Select((value, i) => (value, i)).Where(item => item.value is not null).Select(item => ComponentNames[item.i] + ": " + FuzzAssert.Format(item.value))) + " }";
}
