using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Dom;
using SystemFontCollection = Meziantou.Framework.Fonts.SystemFontCollection;

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

    private Document Load(string html, InstalledFontCollection? fonts, bool useSystemFonts = false)
    {
        var context = _contexts.Create(new BrowsingContextOptions { InstalledFonts = fonts, UseSystemFonts = useSystemFonts });
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
        Assert.Equal("CSSTest FamilyName", fonts.GetFamilyFonts("csstest familyname")[0].FullName);
        Assert.Equal(2, fonts.GetFamilyFonts(LocalizedFamilyName).Count);
        Assert.Equal("CSSTest FamilyName", fonts.GetFamilyFonts(LocalizedFamilyName)[0].FullName);

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

    [Fact]
    public void SystemFonts()
    {
        var regular = ReadResource("ShapingTest.ttf");
        var face = FontFile.Load(regular)!.Faces[0];
        using var directory = new TemporaryDirectory();
        directory.Write("regular.ttf", regular);
        directory.Write(Path.Combine("styles", "bold.ttf"), SetStyle(regular, weight: 700, selection: 0x0020));
        directory.Write(Path.Combine("styles", "italic.ttf"), SetStyle(regular, weight: 400, selection: 0x0001));
        var fonts = InstalledFontCollection.FromSystemFonts(SystemFontCollection.Create([directory.Path]));

        Assert.Equal(3, fonts.GetFamilyFonts(face.FamilyName).Count);
        var html = $"<span id=a style='font-family: {face.FamilyName}'>aaaa</span><span id=b style='font-family: {face.FamilyName}; font-weight: bold'>aaaa</span><span id=c style='font-family: {face.FamilyName}; font-style: italic'>aaaa</span><span id=d>aaaa</span>";
        var document = Load(html, fonts);
        Assert.Equal(40, GetWidth(document, "a"));
        Assert.NotEqual(40, GetWidth(document, "d"));
        Assert.Equal(400, document.GetStyleEngine().GetStyle(document.GetElementById("a")!).Fonts.PrimaryFont.Font.WeightClass);
        Assert.Equal(700, document.GetStyleEngine().GetStyle(document.GetElementById("b")!).Fonts.PrimaryFont.Font.WeightClass);
        Assert.True(document.GetStyleEngine().GetStyle(document.GetElementById("c")!).Fonts.PrimaryFont.Font.IsItalic);

        // The capabilities of the matching come from the index; they are the ones of the loaded fonts
        foreach (var font in fonts.Fonts)
        {
            Assert.Equal(FontSelectionCapabilities.FromFont(font.Font!), font.Capabilities);
        }

        // local() finds a face of the machine by its PostScript name
        document = Load($"<style>@font-face {{ font-family: test; src: local('{face.PostScriptName}') }}</style><span id=a style='font-family: test'>aaaa</span>", fonts);
        Assert.Equal(40, GetWidth(document, "a"));
    }

    [Fact]
    public void SystemFonts_LocalFaceOfACollection()
    {
        // local() names the second face of a collection: the font face uses that face, not the first face of the file
        var first = ReadResource("ShapingTest.ttf");
        var second = ReadResource("ComplexShapingTest.ttf");
        var secondFace = FontFile.Load(second)!.Faces[0];
        using var directory = new TemporaryDirectory();
        directory.Write("fonts.ttc", CreateCollection(first, second));
        var fonts = InstalledFontCollection.FromSystemFonts(SystemFontCollection.Create([directory.Path]));

        var document = Load($"<style>@font-face {{ font-family: test; src: local('{secondFace.PostScriptName}') }}</style><span id=a style='font-family: test'>a</span>", fonts);
        var font = document.GetStyleEngine().GetStyle(document.GetElementById("a")!).Fonts.PrimaryFont.Font;
        Assert.Equal(secondFace.FullName, font.FullName);
        Assert.Equal(1, font.FaceIndex);
        Assert.Equal(2, font.File.Faces.Count);
    }

    [Fact]
    public void SystemFonts_FileRemovedAfterTheScan()
    {
        var regular = ReadResource("ShapingTest.ttf");
        var face = FontFile.Load(regular)!.Faces[0];
        using var directory = new TemporaryDirectory();
        var path = directory.Write("regular.ttf", regular);
        var fonts = InstalledFontCollection.FromSystemFonts(SystemFontCollection.Create([directory.Path]));

        // The file is read when the face is used: a face whose file is gone is missing, and the next family is used
        File.Delete(path);
        var document = Load($"<span id=a style='font-family: {face.FamilyName}, Ahem'>aaaa</span>", fonts);
        Assert.Equal(80, GetWidth(document, "a"));
        Assert.Null(Assert.Single(fonts.GetFamilyFonts(face.FamilyName)).Font);
    }

    [Fact]
    public void SystemFonts_CapabilitiesFromTheIndex()
    {
        // Fonts of all kinds (variable fonts, obliques, collections): the index gives the capabilities of the loaded fonts
        var fonts = InstalledFontCollection.FromSystemFonts(SystemFontCollection.Create([Path.Combine(TestPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Tests", "Css", "Fonts", "Resources")]));
        Assert.True(fonts.Fonts.Count > 10);
        foreach (var font in fonts.Fonts)
        {
            if (font.Font is { } loaded)
            {
                Assert.Equal(FontSelectionCapabilities.FromFont(loaded), font.Capabilities);
                Assert.Equal(loaded.FullName, font.FullName);
                Assert.Equal(loaded.PostScriptName, font.PostScriptName);
            }
        }
    }

    [Fact]
    public void SystemFonts_Option()
    {
        Assert.Null(Load("", fonts: null).InstalledFonts);
        Assert.Same(InstalledFontCollection.System, Load("", fonts: null, useSystemFonts: true).InstalledFonts);

        // The fonts installed by the host replace the fonts of the machine
        var fonts = CreateFonts();
        Assert.Same(fonts, Load("", fonts, useSystemFonts: true).InstalledFonts);

        // The fonts of the machine can be named, after the bundled fonts
        var document = Load("<span id=a style='font-family: Arimo'>a</span>", fonts: null, useSystemFonts: true);
        Assert.True(document.GetStyleEngine().GetStyle(document.GetElementById("a")!).Fonts.PrimaryFont.IsBundled);
        foreach (var font in InstalledFontCollection.System.Fonts.Take(10))
        {
            if (font.Font is { } loaded)
            {
                Assert.Equal(FontSelectionCapabilities.FromFont(loaded), font.Capabilities);
            }
        }
    }

    private static byte[] ReadResource(string name) => File.ReadAllBytes(Path.Combine(TestPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Tests", "Css", "Fonts", "Resources", name));

    /// <summary>Creates a font collection (<c>ttcf</c>) of fonts: each font is copied after the header, its table offsets moved by its position.</summary>
    private static byte[] CreateCollection(params byte[][] fonts)
    {
        var result = new List<byte>();
        var header = new byte[12 + (4 * fonts.Length)];
        "ttcf"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)fonts.Length);
        result.AddRange(header);
        var offsets = new List<int>();
        foreach (var font in fonts)
        {
            while (result.Count % 4 != 0)
            {
                result.Add(0);
            }

            var start = result.Count;
            offsets.Add(start);
            var copy = (byte[])font.Clone();
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(4));
            for (var i = 0; i < tableCount; i++)
            {
                var offset = copy.AsSpan(12 + (i * 16) + 8, 4);
                BinaryPrimitives.WriteUInt32BigEndian(offset, BinaryPrimitives.ReadUInt32BigEndian(offset) + (uint)start);
            }

            result.AddRange(copy);
        }

        var bytes = result.ToArray();
        for (var i = 0; i < offsets.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + (4 * i)), (uint)offsets[i]);
        }

        return bytes;
    }

    /// <summary>Sets the weight class and the selection flags of the OS/2 table of a font.</summary>
    private static byte[] SetStyle(byte[] font, int weight, int selection)
    {
        var result = (byte[])font.Clone();
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(4));
        for (var i = 0; i < tableCount; i++)
        {
            var record = result.AsSpan(12 + (i * 16), 16);
            if (record[..4].SequenceEqual("OS/2"u8))
            {
                var os2 = result.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));
                BinaryPrimitives.WriteUInt16BigEndian(os2[4..], (ushort)weight);
                BinaryPrimitives.WriteUInt16BigEndian(os2[62..], (ushort)selection);
            }
        }

        return result;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "installed-fonts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string relativePath, byte[] data)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
