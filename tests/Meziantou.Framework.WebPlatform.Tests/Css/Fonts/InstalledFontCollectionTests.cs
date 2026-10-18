using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Fonts;

/// <summary>
/// The fonts that a host installs in addition to the bundled fonts (<see cref="InstalledFontCollection"/>,
/// <see cref="BrowsingContextOptions.InstalledFonts"/>), with the fonts of the web-platform-tests: the fonts of the CSS test
/// suite (the glyphs of <c>CSSTest Verify</c> for <c>PASS</c> and of <c>CSSTest FamilyName</c> for <c>5678</c> are the same),
/// M+ 1p (kana and ideographs of 1em) and Scheherazade (Arabic).
/// </summary>
public sealed class InstalledFontCollectionTests : IDisposable
{
    private const string LocalizedFamilyName = "\U0000FF23\U0000FF33\U0000FF33\U000030C6\U000030B9\U000030C8\U00003000\U000030D5\U000030A9\U000030F3\U000030C8\U0000540D";

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private static byte[] Read(params string[] path) => File.ReadAllBytes(TestPaths.External(["wpt", "fonts", .. path]));

    private static InstalledFontCollection CreateFonts()
    {
        var fonts = new InstalledFontCollection();
        Assert.True(fonts.Add(Read("CSSTest", "csstest-familyname.ttf")));
        Assert.True(fonts.Add(Read("CSSTest", "csstest-familyname-funkyA.ttf")));
        Assert.True(fonts.Add(Read("CSSTest", "csstest-verify.ttf")));
        Assert.True(fonts.Add(Read("CSSTest", "csstest-weights-full-w1.ttf")));
        Assert.True(fonts.Add(Read("CSSTest", "csstest-weights-full-w4.ttf")));
        Assert.True(fonts.Add(Read("CSSTest", "csstest-weights-full-w7.ttf")));
        Assert.True(fonts.AddFallback(Read("mplus-1p-regular.woff"), "U+3000-30FF, U+4E00-9FFF"));
        return fonts;
    }

    private Document Load(string html, InstalledFontCollection? fonts)
    {
        var context = _contexts.Create(new BrowsingContextOptions { InstalledFonts = fonts });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font-size: 20px }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static double GetWidth(Document document, string id) => document.GetElementById(id)!.GetBoundingClientRect().Width;

    [Fact]
    public void InvalidFont()
    {
        var fonts = new InstalledFontCollection();
        Assert.False(fonts.Add([1, 2, 3, 4]));
        Assert.False(fonts.AddFallback([1, 2, 3, 4]));
        Assert.Empty(fonts.Fonts);
        Assert.Empty(fonts.Fallbacks);
    }

    [Fact]
    public void FamilyNames()
    {
        var fonts = CreateFonts();

        // The first face installed with a style is the first one of its family; the localized names name the family too
        Assert.Equal("CSSTest FamilyName", fonts.GetFamilyFonts("csstest familyname")[0].Font.FullName);
        Assert.Equal(2, fonts.GetFamilyFonts(LocalizedFamilyName).Count);
        Assert.Equal("CSSTest FamilyName", fonts.GetFamilyFonts(LocalizedFamilyName)[0].Font.FullName);

        // The typographic family name groups the faces of the weights (the family names are "CSSTest Weights Full W1"...)
        Assert.Equal(3, fonts.GetFamilyFonts("CSSTest Weights Full").Count);
        Assert.Empty(fonts.GetFamilyFonts("CSSTest Weights Full W1"));
        Assert.Empty(fonts.GetFamilyFonts("Unknown"));
    }

    [Fact]
    public void InstalledFamily()
    {
        // Without the fonts, the names are unknown: the texts use the default font
        var html = "<span id=a style='font-family: CSSTest Verify'>PASS</span><span id=b style='font-family: \"" + LocalizedFamilyName + "\"'>5678</span><span id=c>PASS</span>";
        var document = Load(html, fonts: null);
        Assert.Equal(GetWidth(document, "c"), GetWidth(document, "a"));

        document = Load(html, CreateFonts());
        Assert.NotEqual(GetWidth(document, "c"), GetWidth(document, "a"));
        Assert.Equal(GetWidth(document, "a"), GetWidth(document, "b"));

        // A family name with a character that is not an ident code point (U+3000) is serialized as a string
        Assert.Equal("\"" + LocalizedFamilyName + "\"", document.DefaultView!.GetComputedStyle(document.GetElementById("b")!).GetPropertyValue("font-family"));
    }

    [Fact]
    public void InstalledFamily_Weights()
    {
        // The glyph of A is the digit of the weight of the face: the same advance, the faces are the ones of the font matching
        var document = Load("<span id=a style='font-family: CSSTest Weights Full; font-weight: 100'>A</span><span id=b style='font-family: CSSTest Weights Full; font-weight: 900'>A</span>", CreateFonts());
        var style = document.GetStyleEngine().GetStyle(document.GetElementById("a")!);
        Assert.Equal(100, style.Fonts.PrimaryFont.Font.WeightClass);
        style = document.GetStyleEngine().GetStyle(document.GetElementById("b")!);
        Assert.Equal(700, style.Fonts.PrimaryFont.Font.WeightClass);
    }

    [Fact]
    public void BundledFontsFirst()
    {
        // The default font and the fonts of the characters that the bundled fonts support do not change
        const string Html = "<span id=a>Latin text</span><span id=b style='font-family: sans-serif'>Latin text</span>";
        var without = Load(Html, fonts: null);
        var with = Load(Html, CreateFonts());
        Assert.Equal(GetWidth(without, "a"), GetWidth(with, "a"));
        Assert.Equal(GetWidth(without, "b"), GetWidth(with, "b"));
        Assert.True(with.GetStyleEngine().GetStyle(with.GetElementById("a")!).Fonts.PrimaryFont.IsBundled);
    }

    [Fact]
    public void FallbackFont()
    {
        // The kana of the fallback font are 1em wide; a character outside of the ranges of the font (a fullwidth letter, that
        // the font has) is not rendered by it
        var document = Load("<span id=a>\U00003042\U00003044</span><span id=b style='font-family: Ahem'>\U00003042</span>", CreateFonts());
        Assert.Equal(40, GetWidth(document, "a"));
        Assert.Equal(20, GetWidth(document, "b"));

        var fonts = document.GetStyleEngine().GetStyle(document.GetElementById("a")!).Fonts;
        Assert.Equal("M+ 1p", fonts.FindFontForCharacter(0x3042)!.Font.FamilyName);
        Assert.Null(fonts.FindFontForCharacter(0xFF21));

        // The fallback font cannot be named
        document = Load("<span id=a style='font-family: \"M+ 1p\"'>A</span><span id=b>A</span>", CreateFonts());
        Assert.Equal(GetWidth(document, "b"), GetWidth(document, "a"));
    }

    [Fact]
    public void FallbackFont_GenericFamily()
    {
        // A generic family is a composite face: the fallback font renders the kana before the next family of the list (Ahem
        // has no kana: its missing glyph is used when it is the only family)
        var document = Load("<span id=a style='font-family: sans-serif, Ahem'>\U00003042</span>", CreateFonts());
        Assert.Equal("M+ 1p", document.GetStyleEngine().GetStyle(document.GetElementById("a")!).Fonts.FindFontForCharacter(0x3042)!.Font.FamilyName);
    }

    [Fact]
    public void LocalSource()
    {
        var document = Load("<style>@font-face { font-family: test; src: local('CSSTest Verify') }</style><span id=a style='font-family: test'>PASS</span><span id=b style='font-family: CSSTest Verify'>PASS</span><span id=c>PASS</span>", CreateFonts());
        Assert.Equal(GetWidth(document, "b"), GetWidth(document, "a"));
        Assert.NotEqual(GetWidth(document, "c"), GetWidth(document, "a"));
    }
}
