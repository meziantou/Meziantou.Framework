using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The CSS Font Loading API (https://drafts.csswg.org/css-font-loading/): FontFace and document.fonts.</summary>
public sealed class FontLoadingTests : IDisposable
{
    private static readonly byte[] FontData = TestFonts.Ahem;

    private readonly BrowsingContextScope _contexts = new();
    private readonly List<DocumentRequest> _requests = [];

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = new FontLoader(this) });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Loads a document that is rendered (it has a rendering backend): the layout requests the font faces it needs.</summary>
    private Document LoadRendered(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = new FontLoader(this), RenderingBackend = new NullBackend() });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private sealed class FontLoader(FontLoadingTests tests) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            tests._requests.Add(request);

            // The fonts named slow*.woff are loaded when the test completes SlowFont
            if (request.Url.Contains("/slow", StringComparison.Ordinal))
                return new ValueTask<DocumentResource?>(tests.SlowFont.Task);

            return ValueTask.FromResult<DocumentResource?>(request.Url.EndsWith(".woff", StringComparison.Ordinal) ? new DocumentResource(FontData) : null);
        }
    }

    private TaskCompletionSource<DocumentResource?> SlowFont { get; } = new();

    /// <summary>A rendering backend without services: the documents of its browsing contexts are rendered.</summary>
    private sealed class NullBackend : RenderingBackend
    {
        internal override RenderingSurface? CreateSurface(int width, int height, RenderingSurfaceOptions options) => null;

        internal override RenderingImageDecoder? CreateImageDecoder(ReadOnlyMemory<byte> data, string? mimeType) => null;
    }

    [Fact]
    public void Fonts_ContainsTheCssConnectedFontFaces()
    {
        var document = Load("<style>@font-face { font-family: 'A'; src: url(a.woff); font-stretch: condensed } @media print { @font-face { font-family: B; src: url(b.woff) } }</style>");
        var faces = document.Fonts.ToList();
        Assert.Equal(["A", "B"], faces.Select(face => face.Family));
        Assert.Equal("condensed", faces[0].Stretch);

        // The CSS-connected font faces cannot be deleted (adding a font face of the set does nothing)
        Assert.Same(document.Fonts, document.Fonts.Add(faces[0]));
        Assert.False(document.Fonts.Delete(faces[0]));
        Assert.Equal(2, document.Fonts.Count);

        // The descriptors of the rule are reflected in both directions
        faces[0].Weight = "bold";
        Assert.Equal("bold", ((CssFontFaceRule)document.StyleSheets[0].CssRules[0]).Style.GetPropertyValue("font-weight"));

        // A removed rule is no longer CSS-connected
        document.QuerySelector("style")!.Remove();
        Assert.Empty(document.Fonts);
        Assert.Equal("A", faces[0].Family);
        Assert.Equal("bold", faces[0].Weight);
        Assert.True(document.Fonts.Add(faces[0]).Has(faces[0]));
    }

    [Fact]
    public async Task FontFace_Load_FetchesTheSources()
    {
        var document = Load("");
        var face = new FontFace("A", "local(Unknown), url(missing.ttf), url(font.woff) format(woff)", new FontFaceDescriptors { Weight = "700" });
        document.Fonts.Add(face);
        Assert.Equal(FontFaceLoadStatus.Unloaded, face.Status);

        var task = face.Load();
        Assert.Equal(FontFaceLoadStatus.Loading, face.Status);
        Assert.Equal(FontFaceSetLoadStatus.Loading, document.Fonts.Status);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(face, await task);
        Assert.Equal(FontFaceSetLoadStatus.Loaded, document.Fonts.Status);
        Assert.Equal(["https://example.com/missing.ttf", "https://example.com/font.woff"], _requests.Where(r => r.Destination == RequestDestination.Font).Select(r => r.Url));
    }

    [Fact]
    public void FontFace_Load_FailsWithNetworkError()
    {
        var document = Load("");
        var face = new FontFace("A", "url(missing.ttf)");
        document.Fonts.Add(face);
        var events = new List<string>();
        document.Fonts.AddEventListener("loadingerror", e => events.Add(e.Type + ":" + string.Join(',', ((FontFaceSetLoadEvent)e).FontFaces.Select(f => f.Family))));

        _ = face.Load();
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(FontFaceLoadStatus.Error, face.Status);
        Assert.Equal("NetworkError", Assert.IsType<DomException>(face.Loaded.Exception!.InnerException).Name);
        Assert.Equal(["loadingerror:A"], events);
    }

    [Fact]
    public void FontFace_BinaryData()
    {
        var document = Load("");
        var face = new FontFace("A", FontData);
        var invalid = new FontFace("B", new byte[12]);

        // The data is validated by the font reader: a font signature is not enough
        var truncated = new FontFace("C", FontData.AsSpan(0, 1000));
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
        Assert.Equal("Ahem", face.FontFile!.Faces[0].FamilyName);
        Assert.Equal(FontFaceLoadStatus.Error, invalid.Status);
        Assert.Equal("SyntaxError", Assert.IsType<DomException>(invalid.Loaded.Exception!.InnerException).Name);
        Assert.Equal(FontFaceLoadStatus.Error, truncated.Status);
        Assert.Null(truncated.FontFile);
        Assert.NotNull(document);
    }

    [Theory]
    [InlineData("Ahem", "Ahem")]
    [InlineData("My Font", "My Font")]
    [InlineData("sans-serif", "\"sans-serif\"")]
    [InlineData("a  b", "\"a  b\"")]
    [InlineData("A, B", "\"A, B\"")]
    [InlineData("inherit", "\"inherit\"")]
    public void FontFace_Family(string family, string expected) => Assert.Equal(expected, new FontFace(family, "url(a.woff)").Family);

    [Fact]
    public void FontFace_InvalidDescriptors()
    {
        var face = new FontFace("A", "url(a.woff)", new FontFaceDescriptors { AscentOverride = "-10%" });
        Assert.Equal(FontFaceLoadStatus.Error, face.Status);
        Assert.Equal("", face.AscentOverride);
        Assert.Equal("SyntaxError", Assert.IsType<DomException>(face.Loaded.Exception!.InnerException).Name);

        var valid = new FontFace("A", "url(a.woff)", new FontFaceDescriptors { FeatureSettings = "'liga' 1", Width = "condensed", Stretch = "expanded" });
        Assert.Equal("\"liga\"", valid.FeatureSettings);
        Assert.Equal("condensed", valid.Stretch);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => valid.Weight = "heavy").Name);

        Assert.Equal(FontFaceLoadStatus.Error, new FontFace("A", "font.woff").Status);
    }

    [Fact]
    public async Task FontFaceSet_LoadAndCheck()
    {
        var document = Load("<style>@font-face { font-family: A; src: url(a.woff) } @font-face { font-family: A; font-weight: bold; src: url(bold.woff) } @font-face { font-family: B; src: url(b.woff); unicode-range: U+0-7F }</style>");
        var faces = document.Fonts.ToList();
        Assert.False(document.Fonts.Check("10px A"));
        Assert.True(document.Fonts.Check("10px B", "一"));
        Assert.True(document.Fonts.Check("10px Unknown"));
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => document.Fonts.Check("inherit")).Name);

        var task = document.Fonts.Load("bold 10px A, B");
        _contexts.EventLoop.RunUntilIdle();

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal([faces[1], faces[2]], await task);
        Assert.Equal(FontFaceLoadStatus.Unloaded, faces[0].Status);
        Assert.True(document.Fonts.Check("bold 10px A"));
        Assert.Equal("SyntaxError", Assert.IsType<DomException>(document.Fonts.Load("var(--x) A").Exception!.InnerException).Name);
    }

    [Fact]
    public async Task FontFaceSet_Load_TheFontFaceOfTheLastLayerWins()
    {
        var document = Load("<style>@layer base, override; @layer override { @font-face { font-family: A; src: url(a.woff) } } @layer base { @font-face { font-family: A; src: url(missing.woff2) } } @font-face { font-family: B; src: url(b.woff) } @layer { @font-face { font-family: B; src: url(missing.woff2) } }</style>");
        var faces = document.Fonts.ToList();
        var task = document.Fonts.Load("10px A, B");
        _contexts.EventLoop.RunUntilIdle();

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal([faces[0], faces[2]], await task);
        Assert.Equal(FontFaceLoadStatus.Unloaded, faces[1].Status);
    }

    [Fact]
    public void FontFaceSet_Ready_LoadsTheFontFacesUsedByTheText()
    {
        var document = Load("<style>@font-face { font-family: A; src: url(a.woff) } @font-face { font-family: B; src: url(b.woff) } p { font-family: A, B }</style><p>Text</p>");
        var faces = document.Fonts.ToList();

        Assert.True(document.Fonts.Ready.IsCompletedSuccessfully);
        Assert.Equal(FontFaceLoadStatus.Loaded, faces[0].Status);
        Assert.Equal(FontFaceLoadStatus.Unloaded, faces[1].Status);
    }

    [Fact]
    public void FontFaceSet_Ready_WaitsForTheFontFacesRequestedByTheLayout()
    {
        // No text uses the font: only the layout knows that the ch unit needs the first available font. The document is
        // loaded, but the set is not: "there are no pending layout operations which might cause the user agent to request a font"
        var document = LoadRendered("<style>@font-face { font-family: A; src: url(slow.woff) } div { font: 20px A; width: 5ch; height: 10px }</style><div id=target></div>");
        var face = Assert.Single(document.Fonts);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
        Assert.Equal(FontFaceLoadStatus.Loading, face.Status);
        Assert.Equal(FontFaceSetLoadStatus.Loading, document.Fonts.Status);
        var ready = document.Fonts.Ready;
        Assert.False(ready.IsCompleted);

        // The fallback font while the font loads: "0" is 0.5em wide
        Assert.Equal(50, document.GetElementById("target")!.GetBoundingClientRect().Width);

        SlowFont.SetResult(new DocumentResource(FontData));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
        Assert.Equal(FontFaceSetLoadStatus.Loaded, document.Fonts.Status);
        Assert.True(ready.IsCompletedSuccessfully);

        // The glyphs of Ahem are 1em wide
        Assert.Equal(100, document.GetElementById("target")!.GetBoundingClientRect().Width);
    }

    [Fact]
    public void FontFaceSet_Ready_WaitsForTheNextFamilyWhenAFontFaceFails()
    {
        // The first family fails to load: the layout that depends on it requests the font face of the next family before
        // the set is loaded
        var document = LoadRendered("<style>@font-face { font-family: A; src: url(missing.ttf) } @font-face { font-family: B; src: url(slow.woff) } p { font-family: A, B }</style><p>Text</p>");
        var faces = document.Fonts.ToList();
        Assert.Equal(FontFaceLoadStatus.Error, faces[0].Status);
        Assert.Equal(FontFaceLoadStatus.Loading, faces[1].Status);
        Assert.Equal(FontFaceSetLoadStatus.Loading, document.Fonts.Status);
        Assert.False(document.Fonts.Ready.IsCompleted);

        SlowFont.SetResult(new DocumentResource(FontData));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(FontFaceLoadStatus.Loaded, faces[1].Status);
        Assert.Equal(FontFaceSetLoadStatus.Loaded, document.Fonts.Status);
        Assert.True(document.Fonts.Ready.IsCompletedSuccessfully);
    }

    [Fact]
    public void FontFaceSet_Ready_UpdatesTheLayoutWhenItIsRead()
    {
        var document = LoadRendered("<style>@font-face { font-family: A; src: url(slow.woff) }</style><div id=target style='width: 5ch'></div>");
        var face = Assert.Single(document.Fonts);
        Assert.Equal(FontFaceLoadStatus.Unloaded, face.Status);
        Assert.Equal(FontFaceSetLoadStatus.Loaded, document.Fonts.Status);
        Assert.True(document.Fonts.Ready.IsCompletedSuccessfully);

        // A style change makes the layout need the font: as in Blink, reading the promise updates the layout, and the font
        // face it requests replaces the promise
        ((HtmlElement)document.GetElementById("target")!).Style.SetProperty("font-family", "A");
        var ready = document.Fonts.Ready;
        Assert.Equal(FontFaceLoadStatus.Loading, face.Status);
        Assert.False(ready.IsCompleted);

        SlowFont.SetResult(new DocumentResource(FontData));
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(ready.IsCompletedSuccessfully);
    }

    [Fact]
    public void FontFaceSet_Ready_DoesNotUpdateTheLayoutOfADocumentThatIsNotRendered()
    {
        // Without rendering backend, the layout is computed on demand only: the font faces that no text uses are not loaded,
        // and the set is loaded with the document
        var document = Load("<style>@font-face { font-family: A; src: url(a.woff) } div { font: 20px A; width: 5ch }</style><div id=target></div>");
        var face = Assert.Single(document.Fonts);
        Assert.Null(document.LayoutEngineOrNull);
        Assert.Equal(FontFaceLoadStatus.Unloaded, face.Status);
        Assert.True(document.Fonts.Ready.IsCompletedSuccessfully);
        Assert.Null(document.LayoutEngineOrNull);

        // A rendered document without font face that is not loaded is not laid out either
        document = LoadRendered("<style>@font-face { font-family: A; src: url(a.woff) } p { font-family: A }</style><p>Text</p><div style='width: 5ch'></div>");
        Assert.Equal(FontFaceLoadStatus.Loaded, Assert.Single(document.Fonts).Status);
        Assert.True(document.Fonts.Ready.IsCompletedSuccessfully);
    }
}
