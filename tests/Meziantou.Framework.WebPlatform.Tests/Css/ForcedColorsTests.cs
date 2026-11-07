using System.Drawing;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The forced colors mode (https://drafts.csswg.org/css-color-adjust-1/#forced): the media features, the palette, the computed and used values, and the painting.</summary>
public sealed class ForcedColorsTests : IDisposable
{
    private static readonly ViewportOptions Forced = new() { ForcedColors = true };

    // A palette whose colors differ from the ones of the color schemes: yellow text on a navy canvas
    private static readonly ForcedColorsTheme Theme = ForcedColorsTheme.Dark with
    {
        Canvas = Color.FromArgb(0, 0, 128),
        CanvasText = Color.FromArgb(255, 255, 0),
        LinkText = Color.FromArgb(0, 255, 255),
        ButtonFace = Color.FromArgb(0, 0, 64),
        ButtonText = Color.FromArgb(0, 255, 0),
        ButtonBorder = Color.FromArgb(255, 0, 255),
        Mark = Color.FromArgb(128, 128, 0),
        MarkText = Color.FromArgb(1, 2, 3),
        Highlight = Color.FromArgb(128, 0, 128),
        HighlightText = Color.FromArgb(250, 250, 250),
        GrayText = Color.FromArgb(160, 160, 160),
    };

    private static readonly ViewportOptions Themed = new() { ForcedColors = true, ForcedColorsTheme = Theme };

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html, ViewportOptions? viewport)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = viewport });
        return context.Navigate(_ => "<!DOCTYPE html>" + html);
    }

    private string Get(string html, string property, ViewportOptions? viewport, string selector = "#target", string? pseudoElement = null)
    {
        var document = Parse(html, viewport);
        return document.DefaultView!.GetComputedStyle(document.QuerySelector(selector)!, pseudoElement).GetPropertyValue(property);
    }

    private static bool Matches(Document document, string query) => document.DefaultView!.MatchMedia(query).Matches;

    // ----------------------------------------------------------------------------------------------------------------
    // Media features and palette
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void MediaFeatures_Default()
    {
        var document = Parse("", viewport: null);
        Assert.True(Matches(document, "(forced-colors: none)"));
        Assert.False(Matches(document, "(forced-colors)"));
        Assert.True(Matches(document, "(prefers-contrast: no-preference)"));
    }

    [Theory]
    [InlineData(PreferredContrast.More, "more")]
    [InlineData(PreferredContrast.Less, "less")]
    [InlineData(PreferredContrast.Custom, "custom")]
    public void PrefersContrast_Option(PreferredContrast contrast, string expected)
    {
        var document = Parse("", new ViewportOptions { PreferredContrast = contrast });
        Assert.True(Matches(document, "(prefers-contrast: " + expected + ")"));
        Assert.True(Matches(document, "(prefers-contrast)"));
        Assert.True(Matches(document, "(forced-colors: none)"));
    }

    [Fact]
    public void MediaFeatures_ForcedColors_DefaultPalette()
    {
        // Black on white: a high contrast, and a light color scheme
        var document = Parse("", Forced);
        Assert.True(Matches(document, "(forced-colors: active)"));
        Assert.True(Matches(document, "(prefers-contrast: more)"));
        Assert.True(Matches(document, "(prefers-color-scheme: light)"));

        document = Parse("", Forced with { PreferredColorScheme = PreferredColorScheme.Dark });
        Assert.True(Matches(document, "(prefers-color-scheme: dark)"));
        Assert.Equal("rgb(255, 255, 255)", Get("<p id=target>", "color", Forced with { PreferredColorScheme = PreferredColorScheme.Dark }));
    }

    [Fact]
    public void MediaFeatures_ForcedColors_Palette()
    {
        // The contrast of the palette replaces the preference of the user; a dark canvas is a dark color scheme
        var document = Parse("", Themed with { PreferredContrast = PreferredContrast.Less });
        Assert.True(Matches(document, "(forced-colors: active)"));
        Assert.True(Matches(document, "(prefers-contrast: more)"));
        Assert.True(Matches(document, "(prefers-color-scheme: dark)"));

        // Gray text on a gray canvas: neither a high nor a low contrast, neither light nor dark
        var custom = new ViewportOptions { ForcedColors = true, ForcedColorsTheme = ForcedColorsTheme.Light with { Canvas = Color.FromArgb(119, 119, 119), CanvasText = Color.FromArgb(230, 230, 230) } };
        document = Parse("", custom);
        Assert.True(Matches(document, "(prefers-contrast: custom)"));
        Assert.True(Matches(document, "(prefers-color-scheme: light)"));
        Assert.True(Matches(Parse("", custom with { PreferredColorScheme = PreferredColorScheme.Dark }), "(prefers-color-scheme: dark)"));

        var low = new ViewportOptions { ForcedColors = true, ForcedColorsTheme = ForcedColorsTheme.Light with { Canvas = Color.FromArgb(200, 200, 200), CanvasText = Color.FromArgb(150, 150, 150) } };
        Assert.True(Matches(Parse("", low), "(prefers-contrast: less)"));
    }

    [Theory]
    [InlineData("Canvas", "rgb(0, 0, 128)")]
    [InlineData("CanvasText", "rgb(255, 255, 0)")]
    [InlineData("LinkText", "rgb(0, 255, 255)")]
    [InlineData("ButtonFace", "rgb(0, 0, 64)")]
    [InlineData("WindowText", "rgb(255, 255, 0)")]
    [InlineData("color-mix(in srgb, Canvas, CanvasText)", "color(srgb 0.5 0.5 0.25098)")]
    public void SystemColors_AreTheColorsOfThePalette(string color, string expected)
    {
        // (whatever the color scheme of the element)
        Assert.Equal(expected, Get($"<p id=target style='forced-color-adjust: none; color-scheme: light; color: {color}'>", "color", Themed));
    }

    [Fact]
    public void DefaultPalettes_AreTheSystemColorsOfTheColorSchemes()
    {
        string[] keywords = ["AccentColor", "AccentColorText", "ActiveText", "ButtonBorder", "ButtonFace", "ButtonText", "Canvas", "CanvasText", "Field", "FieldText", "GrayText",
            "Highlight", "HighlightText", "LinkText", "Mark", "MarkText", "SelectedItem", "SelectedItemText", "VisitedText"];
        foreach (var (scheme, theme) in new[] { (PreferredColorScheme.Light, ForcedColorsTheme.Light), (PreferredColorScheme.Dark, ForcedColorsTheme.Dark) })
        {
            var document = Parse("<meta name=color-scheme content='light dark'><p id=target>", new ViewportOptions { PreferredColorScheme = scheme });
            var forced = Parse("<p id=target>", new ViewportOptions { ForcedColors = true, ForcedColorsTheme = theme });
            foreach (var keyword in keywords)
            {
                var color = (Color)typeof(ForcedColorsTheme).GetProperty(keyword)!.GetValue(theme)!;
                ((HtmlElement)document.GetElementById("target")!).Style.SetProperty("color", keyword);
                ((HtmlElement)forced.GetElementById("target")!).Style.SetProperty("color", keyword);
                var expected = $"rgb({color.R}, {color.G}, {color.B})";
                Assert.Equal(expected, document.DefaultView!.GetComputedStyle(document.GetElementById("target")!).GetPropertyValue("color"));
                Assert.Equal(expected, forced.DefaultView!.GetComputedStyle(forced.GetElementById("target")!).GetPropertyValue("color"));
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Used values
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    // The colors are forced to the colors of an empty author style sheet
    [InlineData("<p id=target style='color: red'>", "color", "rgb(255, 255, 0)")]
    [InlineData("<p id=target style='color: transparent'>", "color", "rgb(255, 255, 0)")]
    [InlineData("<div style='color: red'><p id=target>", "color", "rgb(255, 255, 0)")]
    [InlineData("<a href='https://example.com/' id=target style='color: red'>", "color", "rgb(0, 255, 255)")]
    [InlineData("<a href='https://example.com/' style='color: red'><b id=target style='color: green'>", "color", "rgb(0, 255, 255)")]
    [InlineData("<button style='color: red'><em id=target style='color: green'>", "color", "rgb(0, 255, 0)")]
    [InlineData("<mark id=target style='color: red'>", "color", "rgb(1, 2, 3)")]
    [InlineData("<input id=target placeholder=x>", "color", "rgb(160, 160, 160)", "::placeholder")]
    // The system colors are kept: specified, substituted, or inherited
    [InlineData("<p id=target style='color: LinkText'>", "color", "rgb(0, 255, 255)")]
    [InlineData("<p id=target style='--c: ButtonText; color: var(--c)'>", "color", "rgb(0, 255, 0)")]
    [InlineData("<div style='color: LinkText'><p id=target>", "color", "rgb(0, 255, 255)")]
    [InlineData("<div style='color: LinkText'><p style='color: red'><b id=target>", "color", "rgb(255, 255, 0)")]
    // A color computed from system colors is not a system color
    [InlineData("<p id=target style='color: color-mix(in srgb, LinkText, Canvas)'>", "color", "rgb(255, 255, 0)")]
    [InlineData("<p id=target style='color: rgb(from LinkText r g b)'>", "color", "rgb(255, 255, 0)")]
    [InlineData("<a href='https://example.com/' id=target style='color: red; border: solid currentcolor'>", "border-top-color", "rgb(0, 255, 255)")]
    // forced-color-adjust
    [InlineData("<p id=target style='color: red; forced-color-adjust: none'>", "color", "rgb(255, 0, 0)")]
    [InlineData("<div style='color: red; forced-color-adjust: none'><p id=target style='forced-color-adjust: auto'>", "color", "rgb(255, 255, 0)")]
    [InlineData("<div style='color: red'><p id=target style='forced-color-adjust: preserve-parent-color'>", "color", "rgb(255, 255, 0)")]
    [InlineData("<div style='color: red'><p id=target style='forced-color-adjust: preserve-parent-color; color: lime'>", "color", "rgb(0, 255, 0)")]
    [InlineData("<div style='color: red; forced-color-adjust: none'><p id=target style='forced-color-adjust: preserve-parent-color'>", "color", "rgb(255, 0, 0)")]
    // The background has the system color paired with the forced color, and the alpha of the original color
    [InlineData("<p id=target style='background-color: red'>", "background-color", "rgb(0, 0, 128)")]
    [InlineData("<p id=target style='background-color: rgb(255 0 0 / 0.25)'>", "background-color", "rgba(0, 0, 128, 0.25)")]
    [InlineData("<p id=target>", "background-color", "rgba(0, 0, 128, 0)")]
    [InlineData("<p id=target style='background-color: LinkText'>", "background-color", "rgb(0, 255, 255)")]
    [InlineData("<button><em id=target style='background-color: red'>", "background-color", "rgb(0, 0, 64)")]
    [InlineData("<button id=target style='background-color: red'>", "background-color", "rgb(0, 0, 64)")]
    [InlineData("<mark id=target style='background-color: red'>", "background-color", "rgb(128, 128, 0)")]
    [InlineData("<p id=target style='background-color: red; forced-color-adjust: none'>", "background-color", "rgb(255, 0, 0)")]
    // The borders and the outline have the system color of the user agent style sheet, or the forced color
    [InlineData("<p id=target style='border: solid red'>", "border-top-color", "rgb(255, 255, 0)")]
    [InlineData("<p id=target style='border: solid transparent'>", "border-left-color", "rgb(255, 255, 0)")]
    [InlineData("<p id=target style='border: solid GrayText'>", "border-left-color", "rgb(160, 160, 160)")]
    [InlineData("<button id=target style='border-color: red'>", "border-bottom-color", "rgb(255, 0, 255)")]
    [InlineData("<a href='https://example.com/' id=target style='border: solid red; color: red'>", "border-inline-start-color", "rgb(0, 255, 255)")]
    [InlineData("<p id=target style='outline: solid red'>", "outline-color", "rgb(255, 255, 0)")]
    [InlineData("<p id=target style='outline: auto'>", "outline-color", "auto")]
    [InlineData("<p id=target style='caret-color: red'>", "caret-color", "rgb(255, 255, 0)")]
    [InlineData("<p id=target style='caret-color: GrayText'>", "caret-color", "rgb(160, 160, 160)")]
    public void UsedColors_AreForced(string html, string property, string expected, string? pseudoElement = null)
        => Assert.Equal(expected, Get(html, property, Themed, pseudoElement: pseudoElement));

    [Theory]
    // The resolved value of these properties is the computed value: the colors are forced when they are painted
    [InlineData("text-decoration-color")]
    [InlineData("text-emphasis-color")]
    [InlineData("column-rule-color")]
    [InlineData("fill")]
    [InlineData("stroke")]
    [InlineData("stop-color")]
    [InlineData("flood-color")]
    [InlineData("lighting-color")]
    public void ResolvedValues_OfTheOtherColorsAreNotForced(string property)
        => Assert.Equal("rgb(255, 0, 0)", Get($"<p id=target style='{property}: red'>", property, Themed));

    [Fact]
    public void ComputedValues_AreNotForced()
    {
        var document = Parse("<p id=target style='color: red; background-color: green; border: solid blue'><b id=child>", Themed);
        var map = document.GetElementById("target")!.ComputedStyleMap();
        Assert.Equal("rgb(255, 0, 0)", map.Get("color")!.ToString());
        Assert.Equal("rgb(0, 128, 0)", map.Get("background-color")!.ToString());
        Assert.Equal("rgb(0, 0, 255)", map.Get("border-top-color")!.ToString());

        // The computed value is inherited
        Assert.Equal("rgb(255, 0, 0)", document.GetElementById("child")!.ComputedStyleMap().Get("color")!.ToString());
    }

    [Theory]
    [InlineData("box-shadow: 1px 1px red", "box-shadow", "none")]
    [InlineData("text-shadow: 1px 1px red", "text-shadow", "none")]
    [InlineData("color-scheme: dark", "color-scheme", "light dark")]
    [InlineData("scrollbar-color: red blue", "scrollbar-color", "auto")]
    [InlineData("accent-color: red", "accent-color", "auto")]
    [InlineData("accent-color: Highlight", "accent-color", "rgb(128, 0, 128)")]
    [InlineData("background-image: linear-gradient(red, blue)", "background-image", "none")]
    [InlineData("background-image: url(\"https://example.com/a.png\")", "background-image", "url(\"https://example.com/a.png\")")]
    [InlineData("background-image: url(\"https://example.com/a.png\"), linear-gradient(red, blue)", "background-image", "url(\"https://example.com/a.png\"), linear-gradient(rgb(255, 0, 0), rgb(0, 0, 255))")]
    [InlineData("font-variant-emoji: normal", "font-variant-emoji", "text")]
    [InlineData("font-variant-emoji: unicode", "font-variant-emoji", "text")]
    [InlineData("font-variant-emoji: emoji", "font-variant-emoji", "emoji")]
    public void ComputedValues_OfTheAdjustedProperties(string declaration, string property, string expected)
    {
        Assert.Equal(expected, Get($"<p id=target style='{declaration}'>", property, Themed));

        // forced-color-adjust: none opts out; the properties are not adjusted without forced colors (the system colors differ)
        if (!declaration.Contains("Highlight", StringComparison.Ordinal))
        {
            var unadjusted = Get($"<p id=target style='{declaration}'>", property, viewport: null);
            Assert.Equal(unadjusted, Get($"<p id=target style='{declaration}; forced-color-adjust: none'>", property, Themed));
        }
    }

    [Fact]
    public void Svg_KeepsItsColors()
    {
        var document = Parse("<div style='color: red'><svg id=svg><rect id=rect fill=red /><foreignObject id=foreign style='color: blue'><p id=p style='color: green'></p></foreignObject></svg>", Themed);
        var window = document.DefaultView!;
        Assert.Equal("preserve-parent-color", window.GetComputedStyle(document.GetElementById("svg")!).GetPropertyValue("forced-color-adjust"));
        Assert.Equal("auto", window.GetComputedStyle(document.GetElementById("foreign")!).GetPropertyValue("forced-color-adjust"));

        // The svg element has the used color of its parent; the foreign content is forced
        Assert.Equal("rgb(255, 255, 0)", window.GetComputedStyle(document.GetElementById("svg")!).GetPropertyValue("color"));
        Assert.Equal("rgb(255, 255, 0)", window.GetComputedStyle(document.GetElementById("p")!).GetPropertyValue("color"));
    }

    [Fact]
    public void Animations_DoNotChangeTheForcedColors()
    {
        const string Html = "<style>@keyframes a { from { color: red; background-color: red } to { color: red; background-color: red } } #target { animation: a 10s; color: CanvasText }</style><p id=target>";
        Assert.Equal("rgb(255, 255, 0)", Get(Html, "color", Themed));
        Assert.Equal("rgb(0, 0, 128)", Get(Html, "background-color", Themed));
        Assert.Equal("rgb(255, 0, 0)", Get(Html, "color", viewport: null));
    }

    [Fact]
    public void NestedBrowsingContext_HasTheForcedColors()
    {
        var document = Parse("<iframe srcdoc='<p id=target style=\"color: red\">'></iframe>", Themed);
        _contexts.EventLoop.RunUntilIdle();
        var frame = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentDocument!;
        Assert.True(Matches(frame, "(forced-colors: active)"));
        Assert.Equal("rgb(255, 255, 0)", frame.DefaultView!.GetComputedStyle(frame.GetElementById("target")!).GetPropertyValue("color"));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Painting
    // ----------------------------------------------------------------------------------------------------------------

    private static DisplayColor Rgb(byte r, byte g, byte b) => DisplayColor.FromRgb(r, g, b);

    private Document Paint(string html, ViewportOptions viewport)
    {
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = viewport });
        var document = context.Navigate(_ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static DrawRectItem[] GetRects(Document document, string id)
        => [.. document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Where(item => (item.Source as Element)?.GetAttributeValue("id") == id)];

    [Fact]
    public void Painting_BackgroundAndBackplate()
    {
        var document = Paint("<div id=a style='width: 100px; background-color: red; color: lime'>XX<br>XXXX</div>", Themed);
        var rects = GetRects(document, "a");

        // The background (the canvas color), then the backplate behind the text of the two lines
        Assert.Equal(2, rects.Length);
        Assert.Equal((new PhysicalRect(0, 0, 100, 20), Rgb(0, 0, 128)), (rects[0].Rect, rects[0].Paint.Color));
        Assert.Equal((new PhysicalRect(0, 0, 40, 20), Rgb(0, 0, 128)), (rects[1].Rect, rects[1].Paint.Color));

        // The text has the forced color
        Assert.Contains(document.GetDisplayList()!.Items.ToArray().OfType<DrawGlyphsItem>(), item => item.Paint.Color == Rgb(255, 255, 0));
        Assert.DoesNotContain(document.GetDisplayList()!.Items.ToArray().OfType<DrawGlyphsItem>(), item => item.Paint.Color == Rgb(0, 255, 0));
    }

    [Fact]
    public void Painting_Backplate_ParagraphsAndOptOut()
    {
        // Two consecutive line breaks end a paragraph; an inline box follows its block container
        var document = Paint("<div id=a style='width: 100px'>XX<br><span style='forced-color-adjust: none'>XXX</span><br><br>X</div><div id=b style='forced-color-adjust: none'>XX</div><div id=c style='visibility: hidden'>XX</div>", Themed);
        Assert.Equal([new PhysicalRect(0, 0, 30, 20), new PhysicalRect(0, 30, 10, 10)], GetRects(document, "a").Select(item => item.Rect));
        Assert.Empty(GetRects(document, "b"));
        Assert.Empty(GetRects(document, "c"));

        // No backplate without forced colors
        Assert.Empty(GetRects(Paint("<div id=a>XX</div>", new ViewportOptions()), "a"));
    }

    [Fact]
    public void Painting_BackplateOfAButton_HasTheColorOfItsFace()
    {
        var document = Paint("<button id=a style='appearance: none; border: 0; padding: 0; font: inherit; background: red'>XX</button>", Themed);
        Assert.All(GetRects(document, "a"), item => Assert.Equal(Rgb(0, 0, 64), item.Paint.Color));
    }

    [Fact]
    public void Painting_CanvasBackground_FollowsTheRootElement()
    {
        // forced-color-adjust is propagated to the viewport from the root element, not from the body element
        var document = Paint("<body style='background: red; forced-color-adjust: none'>", Themed);
        Assert.Equal(Rgb(0, 0, 128), document.GetDisplayList()!.Items.ToArray().OfType<DrawPaintItem>().Last().Paint.Color);

        document = Paint("<style>:root { background: red; forced-color-adjust: none }</style>", Themed);
        Assert.Equal(Rgb(255, 0, 0), document.GetDisplayList()!.Items.ToArray().OfType<DrawPaintItem>().Last().Paint.Color);
    }

    [Fact]
    public void Painting_OtherColors_AreForcedToTheColor()
    {
        var document = Paint("<a href='https://example.com/' style='text-decoration: underline red; color: blue'>XX</a>", Themed);
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("#00ffff", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("#ff0000", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("#0000ff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_Svg()
    {
        // The colors of the SVG content are kept, unless the svg element opts in
        var document = Paint("<svg width=100 height=100><rect width=10 height=10 fill=red /></svg>", Themed);
        Assert.Contains("#ff0000", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);

        document = Paint("<svg width=100 height=100 style='forced-color-adjust: auto'><rect width=10 height=10 fill=red stroke=none /><rect width=10 height=10 fill=LinkText /></svg>", Themed);
        var dump = document.GetDisplayList()!.Dump();
        Assert.DoesNotContain("#ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("#ffff00", dump, StringComparison.Ordinal);
        Assert.Contains("#00ffff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Painting_TextControls_ValueAndPlaceholderAreOnTheField()
    {
        // The value and the placeholder of a text control are the text of anonymous boxes of the control: they have the
        // forced colors of the control, and their backplates the color of its background
        var field = DisplayColor.Parse(Get("<input id=target>", "background-color", Themed))!.Value;
        var document = Paint("<style>::selection { color: red; background: blue } input, textarea { font: inherit; color: lime; background: red; border: 0; padding: 0 } ::placeholder { color: lime }</style><input id=a value=XX><textarea id=b>XX</textarea><input id=c placeholder=XX>", Themed);
        ((HtmlInputElement)document.GetElementById("a")!).Select();
        var dump = document.GetDisplayList()!.Dump();
        foreach (var color in new[] { "#00ff00", "#ff0000", "#0000ff" })
        {
            Assert.DoesNotContain(color, dump, StringComparison.Ordinal);
        }

        // The selection of the value has the highlight colors
        Assert.Contains("#800080", dump, StringComparison.Ordinal);
        Assert.Contains("#fafafa", dump, StringComparison.Ordinal);

        // The backgrounds and the backplates (of the inner editors, without source element, and of the placeholder)
        var rects = document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Where(item => item.Paint.Color != Rgb(128, 0, 128)).ToArray();
        Assert.Equal(6, rects.Length);
        Assert.All(rects, item => Assert.Equal(field, item.Paint.Color));
    }

    [Fact]
    public void Painting_Selection_HasTheHighlightColors()
    {
        var document = Paint("<style>::selection { color: red; background: blue }</style><div id=a>XX</div>", Themed);
        document.GetSelection()!.SelectAllChildren(document.GetElementById("a")!);
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("#800080", dump, StringComparison.Ordinal);
        Assert.Contains("#fafafa", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("#ff0000", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("#0000ff", dump, StringComparison.Ordinal);
    }
}
