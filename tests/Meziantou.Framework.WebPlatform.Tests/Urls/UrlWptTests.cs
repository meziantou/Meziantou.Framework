using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Urls;

/// <summary>Data-driven tests from web-platform-tests/url/resources.</summary>
public sealed class UrlWptTests
{
    private static JsonElement Load(string name) => TestJson.Load(TestPaths.External("wpt", "url", "resources", name));

    public static TheoryData<int> UrlTestData() => Indexes("urltestdata.json");

    public static TheoryData<int> UrlTestDataJavaScriptOnly() => Indexes("urltestdata-javascript-only.json");

    public static TheoryData<int> ToAsciiData() => Indexes("toascii.json");

    public static TheoryData<int> IdnaTestV2Data() => Indexes("IdnaTestV2.json");

    public static TheoryData<string, int> SettersData()
    {
        var data = new TheoryData<string, int>();
        var document = Load("setters_tests.json");
        foreach (var property in document.EnumerateObject())
        {
            if (property.Name == "comment")
                continue;

            for (var i = 0; i < property.Value.GetArrayLength(); i++)
            {
                data.Add(property.Name, i);
            }
        }

        return data;
    }

    private static TheoryData<int> Indexes(string file)
    {
        var data = new TheoryData<int>();
        var document = Load(file);
        var index = 0;
        foreach (var item in document.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                data.Add(index);
            }

            index++;
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UrlTestData))]
    public void Constructor(int index) => RunConstructorTest("urltestdata.json", index);

    [Theory]
    [MemberData(nameof(UrlTestDataJavaScriptOnly))]
    public void Constructor_JavaScriptOnly(int index) => RunConstructorTest("urltestdata-javascript-only.json", index);

    private static void RunConstructorTest(string file, int index)
    {
        var document = Load(file);
        var test = document[index];
        var input = test.GetProperty("input").GetStringPreservingSurrogates()!;
        var @base = test.TryGetProperty("base", out var baseElement) ? baseElement.GetStringPreservingSurrogates() : null;
        TestContext.Current.TestOutputHelper?.WriteLine($"Input: <{input}> Base: <{@base}>");

        if (test.TryGetProperty("failure", out var failure) && failure.GetBoolean())
        {
            Assert.Throws<TypeErrorException>(() => new Url(input, @base));
            Assert.False(Url.CanParse(input, @base));
            Assert.Null(Url.Parse(input, @base));
            return;
        }

        var url = new Url(input, @base);
        Assert.Equal(test.GetProperty("href").GetStringPreservingSurrogates(), url.Href);
        Assert.Equal(test.GetProperty("protocol").GetStringPreservingSurrogates(), url.Protocol);
        Assert.Equal(test.GetProperty("username").GetStringPreservingSurrogates(), url.Username);
        Assert.Equal(test.GetProperty("password").GetStringPreservingSurrogates(), url.Password);
        Assert.Equal(test.GetProperty("host").GetStringPreservingSurrogates(), url.Host);
        Assert.Equal(test.GetProperty("hostname").GetStringPreservingSurrogates(), url.Hostname);
        Assert.Equal(test.GetProperty("port").GetStringPreservingSurrogates(), url.Port);
        Assert.Equal(test.GetProperty("pathname").GetStringPreservingSurrogates(), url.Pathname);
        Assert.Equal(test.GetProperty("search").GetStringPreservingSurrogates(), url.Search);
        if (test.TryGetProperty("searchParams", out var searchParams))
        {
            Assert.Equal(searchParams.GetStringPreservingSurrogates(), url.SearchParams.ToString());
        }

        Assert.Equal(test.GetProperty("hash").GetStringPreservingSurrogates(), url.Hash);
        if (test.TryGetProperty("origin", out var origin))
        {
            Assert.Equal(origin.GetStringPreservingSurrogates(), url.Origin);
        }

        Assert.True(Url.CanParse(input, @base));
    }

    [Theory]
    [MemberData(nameof(ToAsciiData))]
    public void ToAscii(int index) => RunHostTest("toascii.json", index, testSetters: true);

    [Theory]
    [MemberData(nameof(IdnaTestV2Data))]
    public void IdnaTestV2(int index) => RunHostTest("IdnaTestV2.json", index, testSetters: false);

    /// <summary>
    /// Code points assigned after the Unicode version used to generate the pinned IdnaTestV2.json.
    /// The implementation uses the latest Unicode data (UTS #46 requires it), so these tests are stale.
    /// </summary>
    private static readonly (int Start, int End, string Reason)[] NewlyAssignedCodePoints =
    [
        (0x3D000, 0x3FC3F, "Small Seal characters are assigned in Unicode 18.0"),
    ];

    private static void RunHostTest(string file, int index, bool testSetters)
    {
        var document = Load(file);
        var test = document[index];
        var input = test.GetProperty("input").GetStringPreservingSurrogates()!;
        var output = test.GetProperty("output").GetStringPreservingSurrogates();
        if (input.Length == 0)
            return;

        if (output is null)
        {
            // Only skip tests that fail because of the Unicode version skew
            if (Url.CanParse("https://" + input + "/x"))
            {
                foreach (var rune in input.EnumerateRunes())
                {
                    foreach (var (start, end, reason) in NewlyAssignedCodePoints)
                    {
                        Assert.SkipWhen(rune.Value >= start && rune.Value <= end, reason);
                    }
                }
            }

            Assert.Throws<TypeErrorException>(() => new Url("https://" + input + "/x"));
        }
        else
        {
            var url = new Url("https://" + input + "/x");
            Assert.Equal(output, url.Host);
            Assert.Equal(output, url.Hostname);
            Assert.Equal("/x", url.Pathname);
            Assert.Equal("https://" + output + "/x", url.Href);
        }

        if (testSetters)
        {
            var url = new Url("https://x/x");
            url.Host = input;
            Assert.Equal(output ?? "x", url.Host);

            url = new Url("https://x/x");
            url.Hostname = input;
            Assert.Equal(output ?? "x", url.Hostname);
        }
    }

    [Theory]
    [MemberData(nameof(SettersData))]
    public void Setters(string property, int index)
    {
        var document = Load("setters_tests.json");
        var test = document.GetProperty(property)[index];
        var url = new Url(test.GetProperty("href").GetStringPreservingSurrogates()!);
        var newValue = test.GetProperty("new_value").GetStringPreservingSurrogates()!;
        SetProperty(url, property, newValue);
        foreach (var expected in test.GetProperty("expected").EnumerateObject())
        {
            Assert.Equal(expected.Value.GetStringPreservingSurrogates(), GetProperty(url, expected.Name));
        }
    }

    private static void SetProperty(Url url, string property, string value)
    {
        switch (property)
        {
            case "href": url.Href = value; break;
            case "protocol": url.Protocol = value; break;
            case "username": url.Username = value; break;
            case "password": url.Password = value; break;
            case "host": url.Host = value; break;
            case "hostname": url.Hostname = value; break;
            case "port": url.Port = value; break;
            case "pathname": url.Pathname = value; break;
            case "search": url.Search = value; break;
            case "hash": url.Hash = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(property), property, null);
        }
    }

    private static string GetProperty(Url url, string property) => property switch
    {
        "href" => url.Href,
        "origin" => url.Origin,
        "protocol" => url.Protocol,
        "username" => url.Username,
        "password" => url.Password,
        "host" => url.Host,
        "hostname" => url.Hostname,
        "port" => url.Port,
        "pathname" => url.Pathname,
        "search" => url.Search,
        "hash" => url.Hash,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
    };

    public static TheoryData<int, string> PercentEncodingData()
    {
        var data = new TheoryData<int, string>();
        var document = Load("percent-encoding.json");
        var index = 0;
        foreach (var item in document.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                foreach (var encoding in item.GetProperty("output").EnumerateObject())
                {
                    data.Add(index, encoding.Name);
                }
            }

            index++;
        }

        return data;
    }

    /// <summary>
    /// percent-encoding.window.js parses &lt;a href="?value#value"&gt; in a document using the given encoding:
    /// the query uses the document's encoding and the fragment is always UTF-8.
    /// </summary>
    [Theory]
    [MemberData(nameof(PercentEncodingData))]
    public void PercentEncoding(int index, string encodingLabel)
    {
        var document = Load("percent-encoding.json");
        var test = document[index];
        var input = test.GetProperty("input").GetStringPreservingSurrogates();
        Assert.True(Meziantou.Framework.WebPlatform.Encodings.WebEncodingRegistry.TryGetEncoding(encodingLabel, out var encoding));

        var baseUrl = Meziantou.Framework.WebPlatform.Urls.UrlParser.Parse("https://example.test/", baseUrl: null);
        var url = Meziantou.Framework.WebPlatform.Urls.UrlParser.Parse("?" + input + "#" + input, baseUrl, encoding);
        Assert.NotNull(url);
        Assert.Equal(test.GetProperty("output").GetProperty(encodingLabel).GetStringPreservingSurrogates(), url.Query);
        Assert.Equal(test.GetProperty("output").GetProperty("utf-8").GetStringPreservingSurrogates(), url.Fragment);
    }
}
