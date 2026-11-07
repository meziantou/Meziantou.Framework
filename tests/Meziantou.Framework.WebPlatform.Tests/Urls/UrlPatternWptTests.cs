using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Urls;

/// <summary>Data-driven tests from web-platform-tests/urlpattern/resources/urlpatterntestdata.json (the steps of urlpatterntests.js).</summary>
public sealed class UrlPatternWptTests
{
    private static readonly string[] Components = ["protocol", "username", "password", "hostname", "port", "pathname", "search", "hash"];

    private static readonly Dictionary<string, string[]> EarlierComponents = new(StringComparer.Ordinal)
    {
        ["protocol"] = [],
        ["hostname"] = ["protocol"],
        ["port"] = ["protocol", "hostname"],
        ["username"] = [],
        ["password"] = [],
        ["pathname"] = ["protocol", "hostname", "port"],
        ["search"] = ["protocol", "hostname", "port", "pathname"],
        ["hash"] = ["protocol", "hostname", "port", "pathname", "search"],
    };

    private static JsonElement Load() => TestJson.Load(TestPaths.External("wpt", "urlpattern", "resources", "urlpatterntestdata.json"));

    public static TheoryData<int> TestData()
    {
        var data = new TheoryData<int>();
        var document = Load();
        for (var i = 0; i < document.GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void UrlPatternTestData(int index)
    {
        var document = Load();
        var entry = document[index];
        TestContext.Current.TestOutputHelper?.WriteLine(entry.GetRawText());
        var pattern = entry.GetProperty("pattern");
        var expectedObj = entry.TryGetProperty("expected_obj", out var expectedObjElement) ? expectedObjElement : default;
        if (expectedObj.ValueKind == JsonValueKind.String && expectedObj.GetString() == "error")
        {
            Assert.Throws<TypeErrorException>(() => CreatePattern(pattern));
            return;
        }

        var urlPattern = CreatePattern(pattern);
        var exactlyEmptyComponents = entry.TryGetProperty("exactly_empty_components", out var empty) ? empty.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal) : [];
        var firstArgument = pattern.GetArrayLength() > 0 ? pattern[0] : default;
        foreach (var component in Components)
        {
            string? expected = null;
            if (expectedObj.ValueKind == JsonValueKind.Object && expectedObj.TryGetProperty(component, out var value))
            {
                expected = value.GetStringPreservingSurrogates();
            }

            if (expected is null)
            {
                Url? baseUrl = null;
                if (pattern.GetArrayLength() > 0 && pattern[0].ValueKind == JsonValueKind.Object && pattern[0].TryGetProperty("baseURL", out var patternBase) && patternBase.GetString() is { Length: > 0 } patternBaseValue)
                {
                    baseUrl = new Url(patternBaseValue);
                }
                else if (pattern.GetArrayLength() > 1 && pattern[1].ValueKind == JsonValueKind.String)
                {
                    baseUrl = new Url(pattern[1].GetString()!);
                }

                if (exactlyEmptyComponents.Contains(component))
                {
                    expected = "";
                }
                else if (firstArgument.ValueKind == JsonValueKind.Object && firstArgument.TryGetProperty(component, out var patternValue) && patternValue.GetString() is { Length: > 0 } patternComponent)
                {
                    expected = patternComponent;
                }
                else if (firstArgument.ValueKind == JsonValueKind.Object && EarlierComponents[component].Any(c => firstArgument.TryGetProperty(c, out _)))
                {
                    expected = "*";
                }
                else if (baseUrl is not null && component is not "username" and not "password")
                {
                    var baseValue = GetUrlComponent(baseUrl, component);
                    if (component == "protocol")
                    {
                        baseValue = baseValue[..^1];
                    }
                    else if (component is "search" or "hash" && baseValue.Length > 0)
                    {
                        baseValue = baseValue[1..];
                    }

                    expected = baseValue;
                }
                else
                {
                    expected = "*";
                }
            }

            Assert.Equal(expected, GetPatternComponent(urlPattern, component));
        }

        var inputs = entry.TryGetProperty("inputs", out var inputsElement) ? inputsElement : default;
        var expectedMatch = entry.TryGetProperty("expected_match", out var expectedMatchElement) ? expectedMatchElement : default;
        if (expectedMatch.ValueKind == JsonValueKind.String && expectedMatch.GetString() == "error")
        {
            Assert.Throws<TypeErrorException>(() => Test(urlPattern, inputs));
            Assert.Throws<TypeErrorException>(() => Exec(urlPattern, inputs));
            return;
        }

        var isMatch = expectedMatch.ValueKind == JsonValueKind.Object;
        Assert.Equal(isMatch, Test(urlPattern, inputs));
        var result = Exec(urlPattern, inputs);
        if (!isMatch)
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        var expectedInputs = expectedMatch.TryGetProperty("inputs", out var expectedInputsElement) ? expectedInputsElement : inputs;
        Assert.Equal(expectedInputs.GetArrayLength(), result.Inputs.Count);
        for (var i = 0; i < result.Inputs.Count; i++)
        {
            var input = result.Inputs[i];
            var expectedInput = expectedInputs[i];
            if (input.StringValue is { } inputString)
            {
                Assert.Equal(expectedInput.GetStringPreservingSurrogates(), inputString);
                continue;
            }

            foreach (var component in Components)
            {
                var expectedValue = expectedInput.TryGetProperty(component, out var v) ? v.GetStringPreservingSurrogates() : null;
                Assert.Equal(expectedValue, GetInitComponent(input.Init!, component));
            }
        }

        foreach (var component in Components)
        {
            var componentResult = GetResultComponent(result, component);
            if (expectedMatch.TryGetProperty(component, out var expectedComponent))
            {
                Assert.Equal(expectedComponent.GetProperty("input").GetStringPreservingSurrogates(), componentResult.Input);
                var expectedGroups = expectedComponent.GetProperty("groups").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetStringPreservingSurrogates(), StringComparer.Ordinal);
                Assert.Equal(expectedGroups.OrderBy(p => p.Key, StringComparer.Ordinal), componentResult.Groups.OrderBy(p => p.Key, StringComparer.Ordinal));
            }
            else
            {
                Assert.Equal("", componentResult.Input);
                var expectedGroups = exactlyEmptyComponents.Contains(component) ? new Dictionary<string, string?>(StringComparer.Ordinal) : new Dictionary<string, string?>(StringComparer.Ordinal) { ["0"] = "" };
                Assert.Equal(expectedGroups.OrderBy(p => p.Key, StringComparer.Ordinal), componentResult.Groups.OrderBy(p => p.Key, StringComparer.Ordinal));
            }
        }
    }

    private static UrlPatternInput ToInput(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetStringPreservingSurrogates()!;

        string? Get(string name) => element.TryGetProperty(name, out var value) ? value.GetStringPreservingSurrogates() : null;
        return new UrlPatternInit
        {
            Protocol = Get("protocol"),
            Username = Get("username"),
            Password = Get("password"),
            Hostname = Get("hostname"),
            Port = Get("port"),
            Pathname = Get("pathname"),
            Search = Get("search"),
            Hash = Get("hash"),
            BaseUrl = Get("baseURL"),
        };
    }

    private static UrlPattern CreatePattern(JsonElement pattern)
    {
        var arguments = pattern.EnumerateArray().ToList();
        if (arguments.Count == 0)
            return new UrlPattern();

        var input = ToInput(arguments[0]);
        if (arguments.Count == 1)
            return new UrlPattern(input);

        if (arguments[1].ValueKind == JsonValueKind.String)
            return new UrlPattern(input, arguments[1].GetStringPreservingSurrogates()!, arguments.Count > 2 ? ToOptions(arguments[2]) : null);

        return new UrlPattern(input, ToOptions(arguments[1]));
    }

    private static UrlPatternOptions ToOptions(JsonElement element) => new() { IgnoreCase = element.TryGetProperty("ignoreCase", out var value) && value.GetBoolean() };

    private static bool Test(UrlPattern pattern, JsonElement inputs)
    {
        var arguments = inputs.ValueKind == JsonValueKind.Array ? inputs.EnumerateArray().ToList() : [];
        return arguments.Count switch
        {
            0 => pattern.Test(),
            1 => pattern.Test(ToInput(arguments[0])),
            _ => pattern.Test(ToInput(arguments[0]), arguments[1].GetStringPreservingSurrogates()),
        };
    }

    private static UrlPatternResult? Exec(UrlPattern pattern, JsonElement inputs)
    {
        var arguments = inputs.ValueKind == JsonValueKind.Array ? inputs.EnumerateArray().ToList() : [];
        return arguments.Count switch
        {
            0 => pattern.Exec(),
            1 => pattern.Exec(ToInput(arguments[0])),
            _ => pattern.Exec(ToInput(arguments[0]), arguments[1].GetStringPreservingSurrogates()),
        };
    }

    private static string GetUrlComponent(Url url, string component) => component switch
    {
        "protocol" => url.Protocol,
        "hostname" => url.Hostname,
        "port" => url.Port,
        "pathname" => url.Pathname,
        "search" => url.Search,
        "hash" => url.Hash,
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };

    private static string GetPatternComponent(UrlPattern pattern, string component) => component switch
    {
        "protocol" => pattern.Protocol,
        "username" => pattern.Username,
        "password" => pattern.Password,
        "hostname" => pattern.Hostname,
        "port" => pattern.Port,
        "pathname" => pattern.Pathname,
        "search" => pattern.Search,
        "hash" => pattern.Hash,
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };

    private static string? GetInitComponent(UrlPatternInit init, string component) => component switch
    {
        "protocol" => init.Protocol,
        "username" => init.Username,
        "password" => init.Password,
        "hostname" => init.Hostname,
        "port" => init.Port,
        "pathname" => init.Pathname,
        "search" => init.Search,
        "hash" => init.Hash,
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };

    private static UrlPatternComponentResult GetResultComponent(UrlPatternResult result, string component) => component switch
    {
        "protocol" => result.Protocol,
        "username" => result.Username,
        "password" => result.Password,
        "hostname" => result.Hostname,
        "port" => result.Port,
        "pathname" => result.Pathname,
        "search" => result.Search,
        "hash" => result.Hash,
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
}
