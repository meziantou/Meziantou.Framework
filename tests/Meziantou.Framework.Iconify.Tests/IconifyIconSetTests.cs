using System.Text;
using System.Text.Json;
using Meziantou.Framework.Internals;

namespace Meziantou.Framework.IconifyTests;

public sealed class IconifyIconSetTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-icons.json");

    // The expected values are the output of iconToHTML(iconToSVG(getIconData(set, name), { width: 'auto', height: 'auto' })) of @iconify/utils 3.1.7
    [Theory]
    [InlineData("home", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z\"/></svg>")]
    [InlineData("wide", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"16\" viewBox=\"0 0 32 16\"><path d=\"M0 0h32v16H0z\"/></svg>")]
    [InlineData("offset", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"16\" viewBox=\"2 4 20 16\"><path d=\"M2 4h20v16H2z\"/></svg>")]
    [InlineData("rotated", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\" viewBox=\"0 0 20 10\"><g transform=\"rotate(90 10 10)\"><path d=\"M0 0h10v20H0z\"/></g></svg>")]
    [InlineData("flipped", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"3\" height=\"4\" viewBox=\"0 0 3 4\"><g transform=\"translate(4 -2) scale(-1 1)\"><path d=\"M1 2h3v4H1z\"/></g></svg>")]
    [InlineData("both-flips", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"3\" height=\"5\" viewBox=\"0 0 3 5\"><g transform=\"rotate(180 1.5 2.5)\"><path d=\"M1 2h3v4H1z\"/></g></svg>")]
    [InlineData("link", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><use xlink:href=\"#a\"/></svg>")]
    [InlineData("old", true, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><path d=\"M0 0h1v1H0z\"/></svg>")]
    [InlineData("fraction", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"25.9\" height=\"12.5\" viewBox=\"0 0 25.9 12.5\"><g transform=\"translate(0 12.5) scale(1 -1)\"><path d=\"M0 0h1v1H0z\"/></g></svg>")]
    [InlineData("house", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z\"/></svg>")]
    [InlineData("home-left", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><g transform=\"rotate(-90 12 12)\"><path fill=\"currentColor\" d=\"M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z\"/></g></svg>")]
    [InlineData("home-down", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><g transform=\"rotate(180 12 12) translate(24 0) scale(-1 1)\"><path fill=\"currentColor\" d=\"M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z\"/></g></svg>")]
    [InlineData("wide-180", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"16\" viewBox=\"0 0 32 16\"><g transform=\"rotate(180 16 8)\"><path d=\"M0 0h32v16H0z\"/></g></svg>")]
    [InlineData("wide-90", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"32\" viewBox=\"0 0 16 32\"><g transform=\"rotate(90 8 8)\"><path d=\"M0 0h32v16H0z\"/></g></svg>")]
    [InlineData("wide-270", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"32\" viewBox=\"0 0 16 32\"><g transform=\"rotate(-90 16 16)\"><path d=\"M0 0h32v16H0z\"/></g></svg>")]
    [InlineData("wide-vflip", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"16\" viewBox=\"0 0 32 16\"><g transform=\"translate(0 16) scale(1 -1)\"><path d=\"M0 0h32v16H0z\"/></g></svg>")]
    [InlineData("rotated-back", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"20\" viewBox=\"0 0 10 20\"><path d=\"M0 0h10v20H0z\"/></svg>")]
    [InlineData("flipped-back", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"3\" height=\"4\" viewBox=\"1 2 3 4\"><path d=\"M1 2h3v4H1z\"/></svg>")]
    [InlineData("offset-flip-rotate", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"20\" viewBox=\"0 0 16 20\"><g transform=\"rotate(90 8 8) translate(-2 20) scale(1 -1)\"><path d=\"M2 4h20v16H2z\"/></g></svg>")]
    [InlineData("home-big", false, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" height=\"48\" viewBox=\"0 0 48 48\"><path fill=\"currentColor\" d=\"M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z\"/></svg>")]
    [InlineData("old-alias", true, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><path d=\"M0 0h1v1H0z\"/></svg>")]
    public void Load_MatchesIconifyUtils(string name, bool hidden, string expectedSvg)
    {
        var iconSet = Iconify.Load(FixturePath);
        var icon = iconSet[name];
        Assert.Equal("test", icon.Prefix);
        Assert.Equal(name, icon.Name);
        Assert.Equal(hidden, icon.IsHidden);
        Assert.Equal(expectedSvg, icon.Svg);
        Assert.Equal(expectedSvg, icon.ToString());
    }

    [Fact]
    public void Load_SkipsUnresolvableAliases()
    {
        var iconSet = Iconify.Load(FixturePath);
        Assert.False(iconSet.ContainsIcon("missing"));
        Assert.False(iconSet.ContainsIcon("cycle-a"));
        Assert.False(iconSet.ContainsIcon("cycle-b"));
        Assert.False(iconSet.TryGetIcon("cycle-a", out _));
        Assert.HasCount(21, iconSet.Icons);
        Assert.Equal(21, iconSet.Count);
    }

    [Fact]
    public void Load_ReadsInfo()
    {
        var iconSet = Iconify.Load(FixturePath);
        Assert.Equal("test", iconSet.Prefix);
        var info = iconSet.Info;
        Assert.NotNull(info);
        Assert.Equal("Test Icons", info.Name);
        Assert.Equal("Meziantou", info.AuthorName);
        Assert.Equal("https://www.meziantou.net", info.AuthorUrl);
        Assert.Equal("MIT", info.LicenseTitle);
        Assert.Equal("MIT", info.LicenseSpdx);
        Assert.Equal("https://opensource.org/licenses/MIT", info.LicenseUrl);
        Assert.Equal(9, info.Total);
        Assert.Equal("1.2.3", info.Version);
        Assert.Equal("General", info.Category);
        Assert.Equal(false, info.Palette);
    }

    [Fact]
    public void Load_ResolvesViewBox()
    {
        var iconSet = Iconify.Load(FixturePath);
        var icon = iconSet["offset"];
        Assert.Equal(2d, icon.Left);
        Assert.Equal(4d, icon.Top);
        Assert.Equal(20d, icon.Width);
        Assert.Equal(16d, icon.Height);
        Assert.Equal("<path d=\"M2 4h20v16H2z\"/>", icon.Body);

        // A rotation by 90 degrees swaps the width and the height
        var rotated = iconSet["wide-90"];
        Assert.Equal(16d, rotated.Width);
        Assert.Equal(32d, rotated.Height);
    }

    [Fact]
    public void Load_UsesDefaultSize()
    {
        var iconSet = Iconify.Parse("""{ "prefix": "a", "icons": { "b": { "body": "<g/>" } } }""");
        var icon = iconSet["b"];
        Assert.Equal(16d, icon.Width);
        Assert.Equal(16d, icon.Height);
        Assert.Equal(0d, icon.Left);
        Assert.Equal(0d, icon.Top);
        Assert.Null(iconSet.Info);
    }

    [Fact]
    public void Load_IconWinsOverAliasWithTheSameName()
    {
        var iconSet = Iconify.Parse("""{ "prefix": "a", "icons": { "b": { "body": "<g/>" }, "c": { "body": "<path/>" } }, "aliases": { "b": { "parent": "c" } } }""");
        Assert.Equal("<g/>", iconSet["b"].Body);
        Assert.Equal(2, iconSet.Count);
    }

    [Fact]
    public void Load_IsCaseSensitive()
    {
        var iconSet = Iconify.Load(FixturePath);
        Assert.False(iconSet.ContainsIcon("Home"));
        var exception = Assert.Throws<KeyNotFoundException>(() => iconSet["Home"]);
        Assert.Equal("The icon set 'test' has no icon named 'Home'.", exception.Message);
    }

    [Fact]
    public void Load_Stream()
    {
        using var stream = File.OpenRead(FixturePath);
        var iconSet = Iconify.Load(stream);
        Assert.Equal("test", iconSet.Prefix);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task LoadAsync_Path()
    {
        var iconSet = await Iconify.LoadAsync(FixturePath, XunitCancellationToken);
        Assert.Equal("test", iconSet.Prefix);
    }

    [Fact]
    public async Task LoadAsync_Stream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(File.ReadAllBytes(FixturePath)).ToArray());
        var iconSet = await Iconify.LoadAsync(stream, XunitCancellationToken);
        Assert.Equal("test", iconSet.Prefix);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "prefix": "", "icons": {} }""")]
    [InlineData("""{ "prefix": 1, "icons": {} }""")]
    [InlineData("""{ "prefix": "a" }""")]
    [InlineData("""{ "prefix": "a", "icons": [] }""")]
    [InlineData("""{ "prefix": "a", "icons": { "b": {} } }""")]
    [InlineData("""{ "prefix": "a", "icons": { "b": 1 } }""")]
    [InlineData("""{ "prefix": "a", "icons": { "b": { "body": 1 } } }""")]
    [InlineData("""{ "prefix": "a", "icons": {} } x""")]
    [InlineData("""{ "prefix": "a", "icons": { } """)]
    public void Parse_InvalidIconSet(string json)
    {
        Assert.Throws<FormatException>(() => Iconify.Parse(json));
    }

    [Fact]
    public void Icon_Constructor()
    {
        var icon = new IconifyIcon("p", "n", "<path/>", -1.5, 0, 10, 20);
        Assert.False(icon.IsHidden);
        Assert.Equal("""<svg xmlns="http://www.w3.org/2000/svg" width="10" height="20" viewBox="-1.5 0 10 20"><path/></svg>""", icon.Svg);
        Assert.Same(icon.Svg, icon.Svg);
    }

    [Fact]
    public void Load_Sample()
    {
        #region Iconify_Load
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-icons.json");
        IconifyIconSet iconSet = Iconify.Load(path);
        IconifyIcon icon = iconSet["home"];
        Assert.Equal("home", icon.Name);
        Assert.Equal(24d, icon.Width);
        Assert.Equal("""<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path fill="currentColor" d="M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z"/></svg>""", icon.Svg);

        if (iconSet.TryGetIcon("house", out var alias))
        {
            Assert.Equal(icon.Body, alias.Body);
        }
        #endregion
    }

    [Fact]
    public void GeneratedIcons()
    {
        var home = Iconify.TestIcons.Icons.Home;
        Assert.Equal("test", home.Prefix);
        Assert.Equal("home", home.Name);
        Assert.Equal(24d, home.Width);
        Assert.Equal(24d, home.Height);
        Assert.Equal("""<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><path fill="currentColor" d="M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z"/></svg>""", home.Svg);
        Assert.Equal("test", Iconify.TestIcons.Prefix);
        Assert.Equal("Test Icons", Iconify.TestIcons.Info?.Name);
        Assert.Equal("MIT", Iconify.TestIcons.Info?.LicenseSpdx);
        Assert.Equal(9, Iconify.TestIcons.Info?.Total);
        Assert.True(Iconify.TestIcons.Icons.Old.IsHidden);
    }

    [Fact]
    public void GeneratedIcons_MatchLoadedIcons()
    {
        var iconSet = Iconify.Load(FixturePath);
        var icons = Iconify.TestIcons.Icons;
        IconifyIcon[] generated =
        [
            icons.Home, icons.Wide, icons.Offset, icons.Rotated, icons.Flipped, icons.BothFlips, icons.Link, icons.Old, icons.Fraction,
            icons.House, icons.HomeLeft, icons.HomeDown, icons.Wide180, icons.Wide90, icons.Wide270, icons.WideVflip, icons.RotatedBack,
            icons.FlippedBack, icons.OffsetFlipRotate, icons.HomeBig, icons.OldAlias,
        ];

        Assert.HasCount(iconSet.Count, generated);
        foreach (var icon in generated)
        {
            var expected = iconSet[icon.Name];
            Assert.Equal(expected.Svg, icon.Svg);
            Assert.Equal(expected.IsHidden, icon.IsHidden);
        }
    }

    [Theory]
    [InlineData("""{ "a": [1, -2, 3.5, 1e3, 1E-2, -0.25e+2, 0], "b": { "c": null, "d": true, "e": false } }""")]
    [InlineData(""" { "s": "a\"b\\c\/d\b\f\n\r\t\u00e9\uD83D\uDE00" } """)]
    [InlineData("""[]""")]
    [InlineData("""{}""")]
    [InlineData("""[[[[]]], {}, "", 12345678901234567890]""")]
    [InlineData("\uFEFF{ \"a\" :\t\r\n 1 }")]
    [InlineData("""{ "a": 1, "a": 2 }""")]
    public void Parser_MatchesSystemTextJson(string json)
    {
        var actual = IconifyJsonParser.Parse(json);
        using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
        AssertJsonEqual(document.RootElement, actual);

        static void AssertJsonEqual(JsonElement expected, object? actual)
        {
            switch (expected.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = Assert.IsType<Dictionary<string, object?>>(actual);
                    var properties = expected.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).ToList();
                    Assert.HasCount(properties.Count, obj);
                    foreach (var group in properties)
                    {
                        AssertJsonEqual(group.Last().Value, obj[group.Key]);
                    }

                    break;
                case JsonValueKind.Array:
                    var array = Assert.IsType<List<object?>>(actual);
                    var items = expected.EnumerateArray().ToList();
                    Assert.HasCount(items.Count, array);
                    for (var i = 0; i < items.Count; i++)
                    {
                        AssertJsonEqual(items[i], array[i]);
                    }

                    break;
                case JsonValueKind.String:
                    Assert.Equal(expected.GetString(), Assert.IsType<string>(actual));
                    break;
                case JsonValueKind.Number:
                    Assert.Equal(expected.GetDouble(), Assert.IsType<double>(actual));
                    break;
                case JsonValueKind.True:
                    Assert.Equal(true, actual);
                    break;
                case JsonValueKind.False:
                    Assert.Equal(false, actual);
                    break;
                default:
                    Assert.Null(actual);
                    break;
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData("-")]
    [InlineData("1e")]
    [InlineData(".5")]
    [InlineData("+1")]
    [InlineData("tru")]
    [InlineData("nul")]
    [InlineData("\"abc")]
    [InlineData("\"a\tb\"")]
    [InlineData("\"\\x\"")]
    [InlineData("\"\\u12\"")]
    [InlineData("\"\\u12G4\"")]
    [InlineData("[1,]")]
    [InlineData("[1 2]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{\"a\" 1}")]
    [InlineData("{a:1}")]
    [InlineData("[] []")]
    [InlineData("'a'")]
    public void Parser_RejectsInvalidJson(string json)
    {
        Assert.Throws<FormatException>(() => IconifyJsonParser.Parse(json));
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(json).Dispose());
    }

    [Fact]
    public void Parser_LimitsDepth()
    {
        Assert.NotNull(IconifyJsonParser.Parse(new string('[', 64) + new string(']', 64)));
        Assert.Throws<FormatException>(() => IconifyJsonParser.Parse(new string('[', 65) + new string(']', 65)));
    }
}
