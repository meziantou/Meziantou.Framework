using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Meziantou.Framework.Tests;

/// <summary>
/// Runs the URL Pattern conformance corpus of the web-platform-tests project.
/// See <c>files/urlpatterntestdata.LICENSE.md</c> for its source and its license.
/// </summary>
public sealed class UrlPatternWebPlatformTests
{
    /// <summary>The cases that are known not to pass, by their index in the corpus.</summary>
    /// <remarks>
    /// A case listed here is asserted to still fail, so that fixing one of them fails this test rather than
    /// passing silently. None of these are canonicalization: they are the parts of the spec that either rely
    /// on a JavaScript regular expression feature that .NET does not have, or on a URL parser of our own.
    /// </remarks>
    private static readonly FrozenDictionary<int, string> ExpectedFailures = new Dictionary<int, string>
    {
        // Uri resolves a relative URL against a base URL that has an opaque path, where the URL Standard fails
        [254] = "\"foo\" against the base URL \"data:data-urls-cannot-be-base-urls\"",

        // The constructor string parser does not read an escaped ":" as part of the username
        [260] = @"https\:foo\:bar@example.com",

        // A group that did not participate reports the empty string rather than being absent
        [329] = "*{}**?",

        // The "v" flag set operations of a JavaScript regular expression, which .NET cannot express
        [352] = "/([[a-z]--a])",
        [353] = @"/([\d&&[0-1]])",
    }.ToFrozenDictionary();

    public static TheoryData<int> TestCaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < TestCases.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static readonly JsonElement[] TestCases = LoadTestCases();

    private static JsonElement[] LoadTestCases()
    {
        using var stream = typeof(UrlPatternWebPlatformTests).GetTypeInfo().Assembly.GetManifestResourceStream("urlpatterntestdata.json");
        Assert.NotNull(stream);

        using var document = JsonDocument.Parse(stream);

        return [.. document.RootElement.EnumerateArray().Select(element => element.Clone())];
    }

    [Theory]
    [MemberData(nameof(TestCaseIndexes))]
    public void OfficialTestSuite(int index)
    {
        var testCase = TestCases[index];
        var failure = Run(testCase);

        if (ExpectedFailures.TryGetValue(index, out var reason))
        {
            if (failure is null)
            {
                Assert.Fail($"Case {index} ({reason}) is listed as an expected failure but it passes now. Remove it from {nameof(ExpectedFailures)}.");
            }

            return;
        }

        // The failure carries the case with it, so that the assertion says which one it was
        Assert.Null(failure is null ? null : $"{testCase.GetProperty("pattern").GetRawText()}: {failure}");
    }

    /// <summary>Runs one case and returns what went wrong, or <see langword="null"/> when it passed.</summary>
    private static string? Run(JsonElement testCase)
    {
        var patternArguments = testCase.GetProperty("pattern");
        var expectedObject = testCase.TryGetProperty("expected_obj", out var obj) ? obj : (JsonElement?)null;
        var expectsConstructorError = expectedObject?.ValueKind is JsonValueKind.String && ReadString(expectedObject.Value) is "error";

        UrlPattern pattern;
        try
        {
            pattern = CreatePattern(patternArguments);
        }
        catch (UrlPatternException ex)
        {
            return expectsConstructorError ? null : $"the constructor threw '{ex.Message}'";
        }

        if (expectsConstructorError)
            return "the constructor was expected to throw";

        var failures = new List<string>();

        if (expectedObject?.ValueKind is JsonValueKind.Object)
        {
            foreach (var component in expectedObject.Value.EnumerateObject())
            {
                var actual = GetComponent(pattern, component.Name);
                var expected = ReadString(component.Value);
                if (actual is not null && actual != expected)
                {
                    failures.Add($"{component.Name} is '{actual}' instead of '{expected}'");
                }
            }
        }

        // A component listed in exactly_empty_components is the empty string rather than a wildcard
        if (testCase.TryGetProperty("exactly_empty_components", out var emptyComponents))
        {
            foreach (var component in emptyComponents.EnumerateArray())
            {
                var name = ReadString(component);
                var actual = GetComponent(pattern, name);
                if (actual is not "")
                {
                    failures.Add($"{name} is '{actual}' instead of being empty");
                }
            }
        }

        failures.AddRange(RunInputs(testCase, pattern));

        return failures.Count is 0 ? null : string.Join("; ", failures);
    }

    private static List<string> RunInputs(JsonElement testCase, UrlPattern pattern)
    {
        var failures = new List<string>();
        if (!testCase.TryGetProperty("inputs", out var inputs) || inputs.GetArrayLength() is 0)
            return failures;

        var expectedMatch = testCase.TryGetProperty("expected_match", out var match) ? match : (JsonElement?)null;
        var expectsMatchError = expectedMatch?.ValueKind is JsonValueKind.String && ReadString(expectedMatch.Value) is "error";
        var expectsNoMatch = expectedMatch is null || expectedMatch.Value.ValueKind is JsonValueKind.Null;

        UrlPatternResult? result;
        try
        {
            result = Match(pattern, inputs);
        }
        catch (UrlPatternException ex)
        {
            if (!expectsMatchError)
            {
                failures.Add($"matching threw '{ex.Message}'");
            }

            return failures;
        }

        if (expectsMatchError)
        {
            failures.Add("matching was expected to throw");
            return failures;
        }

        if (expectsNoMatch)
        {
            if (result is not null)
            {
                failures.Add("the input was expected not to match");
            }

            return failures;
        }

        if (result is null)
        {
            failures.Add("the input was expected to match");
            return failures;
        }

        foreach (var component in expectedMatch!.Value.EnumerateObject())
        {
            if (component.Name is "inputs")
                continue;

            var actual = GetComponentResult(result, component.Name);
            if (actual is null)
                continue;

            if (component.Value.TryGetProperty("input", out var expectedInput) && actual.Input != ReadString(expectedInput))
            {
                failures.Add($"{component.Name}.input is '{actual.Input}' instead of '{ReadString(expectedInput)}'");
            }

            if (!component.Value.TryGetProperty("groups", out var expectedGroups))
                continue;

            foreach (var group in expectedGroups.EnumerateObject())
            {
                var expected = group.Value.ValueKind is JsonValueKind.Null ? null : ReadString(group.Value);
                actual.Groups.TryGetValue(group.Name, out var value);
                if (value != expected)
                {
                    failures.Add($"{component.Name}.groups['{group.Name}'] is '{value ?? "<null>"}' instead of '{expected ?? "<null>"}'");
                }
            }
        }

        return failures;
    }

    private static UrlPattern CreatePattern(JsonElement arguments)
    {
        // The arguments are those of the URLPattern constructor: a pattern string or an init, optionally
        // followed by a base URL, optionally followed by the options
        if (arguments.GetArrayLength() is 0)
            return UrlPattern.Create(new UrlPatternInit());

        var first = arguments[0];
        var options = ReadOptions(arguments);

        if (first.ValueKind is JsonValueKind.String)
        {
            var baseUrl = arguments.GetArrayLength() > 1 && arguments[1].ValueKind is JsonValueKind.String
                ? ReadString(arguments[1])
                : null;

            return UrlPattern.Create(ReadString(first), baseUrl, options);
        }

        ThrowIfBaseUrlAccompaniesAnInit(arguments);

        return UrlPattern.Create(ReadInit(first), options);
    }

    private static UrlPatternOptions? ReadOptions(JsonElement arguments)
    {
        for (var i = 1; i < arguments.GetArrayLength(); i++)
        {
            if (arguments[i].ValueKind is JsonValueKind.Object && arguments[i].TryGetProperty("ignoreCase", out var ignoreCase))
                return new UrlPatternOptions { IgnoreCase = ignoreCase.GetBoolean() };
        }

        return null;
    }

    private static UrlPatternResult? Match(UrlPattern pattern, JsonElement inputs)
    {
        var first = inputs[0];
        if (first.ValueKind is not JsonValueKind.String)
        {
            ThrowIfBaseUrlAccompaniesAnInit(inputs);

            return pattern.Match(ReadInit(first));
        }

        var baseUrl = inputs.GetArrayLength() > 1 ? ReadString(inputs[1]) : null;

        return pattern.Match(ReadString(first), baseUrl);
    }

    /// <summary>Reports the error the spec requires when an init is given together with a base URL.</summary>
    /// <remarks>
    /// An init carries its own base URL, so supplying a second one is a TypeError. There is no C# overload
    /// that takes both, which makes it a compile-time error rather than a run-time one, so the corpus cases
    /// that check for it are answered here.
    /// </remarks>
    private static void ThrowIfBaseUrlAccompaniesAnInit(JsonElement arguments)
    {
        if (arguments.GetArrayLength() > 1 && arguments[1].ValueKind is JsonValueKind.String)
            throw new UrlPatternException("A base URL cannot be given alongside a UrlPatternInit");
    }

    private static UrlPatternInit ReadInit(JsonElement element)
    {
        var init = new UrlPatternInit();
        foreach (var property in element.EnumerateObject())
        {
            // An init is a dictionary, so a member that is not a component is ignored rather than rejected
            var value = property.Value.ValueKind is JsonValueKind.Null ? null : ReadString(property.Value);
            switch (property.Name)
            {
                case "protocol": init.Protocol = value; break;
                case "username": init.Username = value; break;
                case "password": init.Password = value; break;
                case "hostname": init.Hostname = value; break;
                case "port": init.Port = value; break;
                case "pathname": init.Pathname = value; break;
                case "search": init.Search = value; break;
                case "hash": init.Hash = value; break;
                case "baseURL": init.BaseUrl = value; break;
            }
        }

        return init;
    }

    private static string? GetComponent(UrlPattern pattern, string name) => name switch
    {
        "protocol" => pattern.Protocol,
        "username" => pattern.Username,
        "password" => pattern.Password,
        "hostname" => pattern.Hostname,
        "port" => pattern.Port,
        "pathname" => pattern.Pathname,
        "search" => pattern.Search,
        "hash" => pattern.Hash,
        _ => null,
    };

    private static UrlPatternComponentResult? GetComponentResult(UrlPatternResult result, string name) => name switch
    {
        "protocol" => result.Protocol,
        "username" => result.Username,
        "password" => result.Password,
        "hostname" => result.Hostname,
        "port" => result.Port,
        "pathname" => result.Pathname,
        "search" => result.Search,
        "hash" => result.Hash,
        _ => null,
    };

    /// <summary>Reads a JSON string, including one that holds an unpaired surrogate.</summary>
    /// <remarks>
    /// A few cases check what happens to a lone surrogate, which <see cref="JsonElement.GetString"/> refuses
    /// to return. The raw text of such a token is unescaped here instead, which is what a JavaScript engine
    /// would hand to the URLPattern constructor.
    /// </remarks>
    private static string ReadString(JsonElement element)
    {
        try
        {
            return element.GetString()!;
        }
        catch (InvalidOperationException)
        {
            return UnescapeJsonString(element.GetRawText());
        }
    }

    private static string UnescapeJsonString(string rawText)
    {
        // The raw text of a string token, quotes included
        var value = rawText.AsSpan(1, rawText.Length - 2);
        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is not '\\')
            {
                builder.Append(value[i]);
                continue;
            }

            i++;
            switch (value[i])
            {
                case 'u':
                    builder.Append((char)int.Parse(value.Slice(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                default: builder.Append(value[i]); break;
            }
        }

        return builder.ToString();
    }
}
