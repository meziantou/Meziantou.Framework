using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class MediaQueryEvaluatorTests
{
    // The default environment: an 800x600 screen, 1dppx, 16px font, light color scheme
    [Theory]
    // Empty lists
    [InlineData("", true)]
    [InlineData("   ", true)]
    // Media types
    [InlineData("all", true)]
    [InlineData("screen", true)]
    [InlineData("SCREEN", true)]
    [InlineData("print", false)]
    [InlineData("tv", false)]
    [InlineData("handheld", false)]
    [InlineData("speech", false)]
    [InlineData("unknown-type", false)]
    [InlineData("print, screen", true)]
    [InlineData("print, tv", false)]
    [InlineData("only screen", true)]
    [InlineData("ONLY screen", true)]
    [InlineData("only print", false)]
    [InlineData("not print", true)]
    [InlineData("not screen", false)]
    [InlineData("not all", false)]
    [InlineData("NOT tv", true)]
    [InlineData("screen and (color)", true)]
    [InlineData("screen AND (color)", true)]
    [InlineData("print and (color)", false)]
    [InlineData("not screen and (color)", false)]
    [InlineData("not print and (color)", true)]
    [InlineData("not screen and (monochrome)", true)]
    [InlineData("all and (min-width: 500px)", true)]
    // Invalid media queries
    [InlineData("only", false)]
    [InlineData("not", false)]
    [InlineData("and", false)]
    [InlineData("or", false)]
    [InlineData("layer", false)]
    [InlineData("not only screen", false)]
    [InlineData("screen and", false)]
    [InlineData("screen color", false)]
    [InlineData("screen and color", false)]
    [InlineData("screen or (color)", false)]
    [InlineData("screen and (color) or (monochrome)", false)]
    [InlineData("screen and not (color) and (color)", false)]
    [InlineData("(color) and (color) or (color)", false)]
    [InlineData("not (color) and (color)", false)]
    [InlineData("only (color)", false)]
    [InlineData("screen and(color)", false)]
    [InlineData("screen,", true)]
    [InlineData("print,", false)]
    [InlineData(",", false)]
    [InlineData("bad$, screen", true)]
    [InlineData("screen, bad$", true)]
    [InlineData("[screen]", false)]
    [InlineData("(color) (color)", false)]
    [InlineData("(color) {}", false)]
    [InlineData("(color]", false)]
    // Conditions: not, and, or, nesting
    [InlineData("(color)", true)]
    [InlineData("(monochrome)", false)]
    [InlineData("not (color)", false)]
    [InlineData("not (monochrome)", true)]
    [InlineData("(color) and (min-width: 100px)", true)]
    [InlineData("(color) and (monochrome)", false)]
    [InlineData("(color) or (monochrome)", true)]
    [InlineData("(monochrome) or (grid)", false)]
    [InlineData("(monochrome) OR (grid) or (color)", true)]
    [InlineData("((color))", true)]
    [InlineData("((color) and (monochrome)) or (hover)", true)]
    [InlineData("not ((color) and (monochrome))", true)]
    [InlineData("not ((color) or (monochrome))", false)]
    [InlineData("screen and ((color) or (monochrome))", true)]
    [InlineData("screen and (not (monochrome))", true)]
    [InlineData("screen and not (monochrome)", true)]
    [InlineData("(not (color)) or (not (monochrome))", true)]
    // Range features: plain syntax and min-/max- prefixes
    [InlineData("(width: 800px)", true)]
    [InlineData("(width: 799px)", false)]
    [InlineData("(min-width: 800px)", true)]
    [InlineData("(min-width: 801px)", false)]
    [InlineData("(max-width: 800px)", true)]
    [InlineData("(max-width: 799.5px)", false)]
    [InlineData("(MIN-WIDTH: 400PX)", true)]
    [InlineData("(min-height: 600px) and (max-height: 600px)", true)]
    [InlineData("(min-device-width: 800px)", true)]
    [InlineData("(max-device-height: 599px)", false)]
    // min-/max- prefixes are only allowed on range features, not in boolean or range syntax
    [InlineData("(min-width)", false)]
    [InlineData("not (min-width)", false)]
    [InlineData("(min-orientation: portrait)", false)]
    [InlineData("not (min-orientation: portrait)", false)]
    [InlineData("(min-width > 100px)", false)]
    [InlineData("not (min-width > 100px)", false)]
    // Range syntax
    [InlineData("(width >= 800px)", true)]
    [InlineData("(width > 800px)", false)]
    [InlineData("(width < 801px)", true)]
    [InlineData("(width <= 799px)", false)]
    [InlineData("(width = 800px)", true)]
    [InlineData("(width=800px)", true)]
    [InlineData("(width>=800px)", true)]
    [InlineData("(800px = width)", true)]
    [InlineData("(600px < width)", true)]
    [InlineData("(900px < width)", false)]
    [InlineData("(800px <= width)", true)]
    [InlineData("(801px <= width)", false)]
    [InlineData("(900px > width)", true)]
    [InlineData("(800px >= width)", true)]
    [InlineData("(400px <= width < 900px)", true)]
    [InlineData("(400px <= width < 800px)", false)]
    [InlineData("(400px < width <= 800px)", true)]
    [InlineData("(900px > width > 400px)", true)]
    [InlineData("(900px >= width >= 801px)", false)]
    [InlineData("(0 < width)", true)]
    [InlineData("(height < width)", false)]
    // Invalid range syntax
    [InlineData("(width < = 800px)", false)]
    [InlineData("(width => 800px)", false)]
    [InlineData("(400px < width > 300px)", false)]
    [InlineData("(400px = width = 400px)", false)]
    [InlineData("(width < 900px < 1000px)", false)]
    [InlineData("(orientation > portrait)", false)]
    [InlineData("not (orientation > portrait)", false)]
    // Boolean context
    [InlineData("(width)", true)]
    [InlineData("(height)", true)]
    [InlineData("(aspect-ratio)", true)]
    [InlineData("(resolution)", true)]
    [InlineData("(grid)", false)]
    [InlineData("(color-index)", false)]
    [InlineData("(orientation)", true)]
    [InlineData("(hover)", true)]
    [InlineData("(pointer)", true)]
    [InlineData("(prefers-reduced-motion)", false)]
    [InlineData("(prefers-contrast)", false)]
    [InlineData("(prefers-color-scheme)", true)]
    [InlineData("(forced-colors)", false)]
    [InlineData("(inverted-colors)", false)]
    [InlineData("(scripting)", false)]
    [InlineData("(update)", true)]
    [InlineData("(color-gamut)", true)]
    [InlineData("(-webkit-transform-3d)", true)]
    // Units
    [InlineData("(width: 50em)", true)]
    [InlineData("(width: 50rem)", true)]
    [InlineData("(min-width: 51em)", false)]
    [InlineData("(min-width: 108ex)", true)] // The x-height of the default font (Tinos) is 0.459em
    [InlineData("(min-width: 109ex)", false)]
    [InlineData("(width: 100ch)", true)]
    [InlineData("(width: 50ic)", true)]
    [InlineData("(max-width: 41lh)", false)]
    [InlineData("(width: 100vw)", true)]
    [InlineData("(width: 100vi)", true)]
    [InlineData("(width: 100svw)", true)]
    [InlineData("(width: 100dvw)", true)]
    [InlineData("(height: 100vh)", true)]
    [InlineData("(height: 100vb)", true)]
    [InlineData("(height: 100vmin)", true)]
    [InlineData("(width: 100vmax)", true)]
    [InlineData("(width: 8.3333333333in)", true)]
    [InlineData("(width: 600pt)", true)]
    [InlineData("(width: 50pc)", true)]
    [InlineData("(width: 21.16666666667cm)", true)]
    [InlineData("(width: 211.6666666667mm)", true)]
    [InlineData("(width: 846.6666666667q)", true)]
    [InlineData("(min-width: 0)", true)]
    [InlineData("(min-width: 100)", false)]
    [InlineData("not (min-width: 100)", false)]
    [InlineData("(width: 800deg)", false)]
    [InlineData("(width: 100%)", false)]
    [InlineData("(width: auto)", false)]
    [InlineData("(min-width: -100px)", true)]
    // Resolution
    [InlineData("(resolution: 1dppx)", true)]
    [InlineData("(resolution: 1x)", true)]
    [InlineData("(resolution: 96dpi)", true)]
    [InlineData("(resolution: 37.795275590551dpcm)", true)]
    [InlineData("(min-resolution: 2dppx)", false)]
    [InlineData("(max-resolution: 2x)", true)]
    [InlineData("(resolution < 2x)", true)]
    [InlineData("(resolution < infinite)", true)]
    [InlineData("(resolution: infinite)", false)]
    [InlineData("(resolution: 1px)", false)]
    [InlineData("(resolution: 1)", false)]
    [InlineData("(-webkit-device-pixel-ratio: 1)", true)]
    [InlineData("(-webkit-min-device-pixel-ratio: 1)", true)]
    [InlineData("(-webkit-min-device-pixel-ratio: 1.5)", false)]
    [InlineData("(-webkit-max-device-pixel-ratio: 1.5)", true)]
    [InlineData("(-webkit-min-device-pixel-ratio)", false)]
    // Ratios
    [InlineData("(aspect-ratio: 4/3)", true)]
    [InlineData("(aspect-ratio: 4 / 3)", true)]
    [InlineData("(aspect-ratio: 8/6)", true)]
    [InlineData("(aspect-ratio: 16/9)", false)]
    [InlineData("(min-aspect-ratio: 1/1)", true)]
    [InlineData("(min-aspect-ratio: 1)", true)]
    [InlineData("(max-aspect-ratio: 1)", false)]
    // The degenerate ratio 0/0 behaves as 1/0
    [InlineData("(max-aspect-ratio: 0/0)", true)]
    [InlineData("(min-aspect-ratio: 0/0)", false)]
    [InlineData("(aspect-ratio > 1)", true)]
    [InlineData("(aspect-ratio < 16/9)", true)]
    [InlineData("(1/1 < aspect-ratio < 16/9)", true)]
    [InlineData("(max-aspect-ratio: 1/0)", true)]
    [InlineData("(aspect-ratio: 0/0)", false)]
    [InlineData("not (aspect-ratio: 0/0)", true)]
    [InlineData("(aspect-ratio: -4/3)", false)]
    [InlineData("(aspect-ratio: 4/-3)", false)]
    [InlineData("(aspect-ratio: 4px/3px)", false)]
    [InlineData("(device-aspect-ratio: 4/3)", true)]
    [InlineData("(aspect-ratio: 1.3333333333333333)", true)]
    // Integers
    [InlineData("(color: 8)", true)]
    [InlineData("(min-color: 4)", true)]
    [InlineData("(min-color: 9)", false)]
    [InlineData("(color: 8.0)", false)]
    [InlineData("(color > 1)", true)]
    [InlineData("(monochrome: 0)", true)]
    [InlineData("(color-index: 0)", true)]
    [InlineData("(grid: 0)", true)]
    [InlineData("(grid: 1)", false)]
    [InlineData("(grid: 2)", false)]
    [InlineData("not (grid: 2)", false)]
    [InlineData("(min-grid: 0)", false)]
    [InlineData("(-webkit-transform-3d: 1)", true)]
    [InlineData("(horizontal-viewport-segments: 1)", true)]
    [InlineData("(vertical-viewport-segments > 1)", false)]
    // Discrete features
    [InlineData("(orientation: landscape)", true)]
    [InlineData("(orientation: portrait)", false)]
    [InlineData("(ORIENTATION: LANDSCAPE)", true)]
    [InlineData("(orientation: 1px)", false)]
    [InlineData("(orientation: foo)", false)]
    [InlineData("not (orientation: foo)", false)]
    [InlineData("(scan: progressive)", false)]
    [InlineData("(scan: interlace)", false)]
    [InlineData("(scan)", false)]
    [InlineData("not all and (scan)", true)]
    [InlineData("(update: fast)", true)]
    [InlineData("(overflow-block: scroll)", true)]
    [InlineData("(overflow-inline: scroll)", true)]
    [InlineData("(color-gamut: srgb)", true)]
    [InlineData("(color-gamut: p3)", false)]
    [InlineData("(dynamic-range: standard)", true)]
    [InlineData("(dynamic-range: high)", false)]
    [InlineData("(video-dynamic-range: standard)", true)]
    [InlineData("(inverted-colors: none)", true)]
    [InlineData("(pointer: fine)", true)]
    [InlineData("(any-pointer: coarse)", false)]
    [InlineData("(hover: hover)", true)]
    [InlineData("(any-hover: none)", false)]
    [InlineData("(prefers-reduced-motion: no-preference)", true)]
    [InlineData("(prefers-reduced-motion: reduce)", false)]
    [InlineData("(prefers-reduced-transparency: no-preference)", true)]
    [InlineData("(prefers-reduced-data: reduce)", false)]
    [InlineData("(prefers-contrast: no-preference)", true)]
    [InlineData("(prefers-contrast: more)", false)]
    [InlineData("(forced-colors: none)", true)]
    [InlineData("(prefers-color-scheme: light)", true)]
    [InlineData("(prefers-color-scheme: dark)", false)]
    [InlineData("(scripting: none)", true)]
    [InlineData("(scripting: enabled)", false)]
    [InlineData("(scripting: initial-only)", false)]
    [InlineData("(display-mode: browser)", true)]
    [InlineData("(display-mode: standalone)", false)]
    [InlineData("(environment-blending: opaque)", true)]
    [InlineData("(nav-controls: back)", true)]
    [InlineData("(device-posture: continuous)", true)]
    // Unknown features and <general-enclosed> evaluate to unknown (false, even when negated)
    [InlineData("(unknown-feature)", false)]
    [InlineData("not (unknown-feature)", false)]
    [InlineData("(unknown-feature: 1px)", false)]
    [InlineData("(unknown-feature > 1px)", false)]
    [InlineData("()", false)]
    [InlineData("(width: )", false)]
    [InlineData("(width 800px)", false)]
    [InlineData("foo(bar)", false)]
    [InlineData("not foo(bar)", false)]
    [InlineData("(color) and (unknown-feature)", false)]
    [InlineData("(monochrome) and (unknown-feature)", false)]
    [InlineData("not ((monochrome) and (unknown-feature))", true)]
    [InlineData("(color) or (unknown-feature)", true)]
    [InlineData("(unknown-feature) or foo(bar) or (color)", true)]
    [InlineData("(monochrome) or (unknown-feature)", false)]
    [InlineData("screen and (unknown-feature)", false)]
    [InlineData("not print and (unknown-feature)", true)]
    [InlineData("not screen and (unknown-feature)", false)]
    [InlineData("(unknown-feature), screen", true)]
    [InlineData("((color) and (color) or (color))", false)]
    [InlineData("(color) and ((color) and (color) or (color))", false)]
    [InlineData("(color) or ((color) and (color) or (color))", true)]
    // calc()
    [InlineData("(width: calc(800px))", true)]
    [InlineData("(width: calc(700px + 100px))", true)]
    [InlineData("(width: calc(900px - 100px))", true)]
    [InlineData("(width: calc(400px * 2))", true)]
    [InlineData("(width: calc(2 * 400px))", true)]
    [InlineData("(width: calc(1600px / 2))", true)]
    [InlineData("(width: calc(50em))", true)]
    [InlineData("(width: calc(100vw - 0px))", true)]
    [InlineData("(width: calc((300px + 100px) * 2))", true)]
    [InlineData("(width: calc(calc(400px) * (1 + 1)))", true)]
    [InlineData("(width: calc(40em + 10rem))", true)]
    [InlineData("(width: CALC(800PX))", true)]
    [InlineData("(width: min(800px, 900px))", true)]
    [InlineData("(width: max(700px, 800px, 1vw))", true)]
    [InlineData("(width: clamp(100px, 800px, 900px))", true)]
    [InlineData("(width: clamp(100px, 1000px, 800px))", true)]
    [InlineData("(width: abs(-800px))", true)]
    [InlineData("(min-width: calc(800px + 1px))", false)]
    [InlineData("(width > calc(700px + 1em))", true)]
    [InlineData("(calc(700px) < width < calc(900px))", true)]
    [InlineData("(width: calc(800px +100px))", false)]
    [InlineData("(width: calc(800px+ 100px))", false)]
    [InlineData("(width: calc(800px + 1))", false)]
    [InlineData("(width: calc(800px * 1px))", false)]
    [InlineData("(width: calc(0))", false)]
    [InlineData("(width: calc(800px, 1px))", false)]
    [InlineData("(width: calc(100%))", false)]
    [InlineData("(width: clamp(1px, 2px))", false)]
    [InlineData("(width: foo(800px))", false)]
    [InlineData("(color: calc(4 * 2))", true)]
    [InlineData("(color: calc(7.6))", true)]
    [InlineData("(resolution: calc(1dppx * 1))", true)]
    [InlineData("(resolution: calc(48dpi + 48dpi))", true)]
    [InlineData("(aspect-ratio: calc(8 / 2) / 3)", true)]
    [InlineData("(min-resolution: calc(2x / 2))", true)]
    [InlineData("(width: calc(800px / 1px * 1px))", true)]
    // Typed arithmetic: products and quotients of dimensions (https://drafts.csswg.org/css-values-4/#calc-type-checking)
    [InlineData("(width: calc(1600px * 1em / 32px))", true)]
    [InlineData("(width: calc((50vw * 4em) / 32px))", true)]
    [InlineData("(width: calc(100vw * 1px * 1em / 1px / 16px))", true)]
    [InlineData("(resolution: calc(1dppx * 1px / 1px))", true)]
    [InlineData("(width: calc(800px * 1px / 1px / 1px))", false)]
    [InlineData("(width: calc(800px * 1dppx))", false)]
    [InlineData("(color: calc(16px / 1em * 8))", true)]
    // A negative resolution is invalid: the feature is unknown
    [InlineData("(resolution: -1dpi)", false)]
    [InlineData("not all and (resolution: -1dpi)", false)]
    [InlineData("(max-resolution: -1dpi)", false)]
    // Environment variables
    [InlineData("(width > env(safe-area-inset-top))", true)]
    [InlineData("(width = env(titlebar-area-width))", true)]
    [InlineData("(width: calc(env(safe-area-inset-left) + 800px))", true)]
    [InlineData("(width < env(unknown, 900px))", true)]
    [InlineData("(width < env(unknown, env(unknown-2, 700px)))", false)]
    [InlineData("(width < env(unknown))", false)]
    [InlineData("not all and (width < env(unknown))", false)]
    public void Matches_DefaultEnvironment(string query, bool expected)
    {
        Assert.Equal(expected, MediaQueryEvaluator.Matches(query, MediaEnvironment.Default));
    }

    [Theory]
    [InlineData("print", true)]
    [InlineData("screen", false)]
    [InlineData("only print and (color)", true)]
    [InlineData("not screen", true)]
    [InlineData("(update: none)", true)]
    [InlineData("(update)", false)]
    [InlineData("(overflow-block: paged)", true)]
    [InlineData("(overflow-inline: none)", true)]
    [InlineData("(scan)", false)]
    [InlineData("(scan: progressive)", false)]
    public void Matches_Print(string query, bool expected)
    {
        var environment = MediaEnvironment.Default with { MediaType = "print" };
        Assert.Equal(expected, MediaQueryEvaluator.Matches(query, environment));
    }

    [Theory]
    [InlineData("(orientation: portrait)", true)]
    [InlineData("(orientation: landscape)", false)]
    [InlineData("(max-width: 480px)", true)]
    [InlineData("(max-aspect-ratio: 1)", true)]
    [InlineData("(aspect-ratio: 400/800)", true)]
    [InlineData("(width: 100vmin)", true)]
    [InlineData("(height: 100vmax)", true)]
    [InlineData("(width: 20em)", true)]
    [InlineData("(min-width: 43ex)", true)]
    [InlineData("(min-width: 44ex)", false)]
    [InlineData("(resolution: 2dppx)", true)]
    [InlineData("(resolution: 192dpi)", true)]
    [InlineData("(min-resolution: 2x)", true)]
    [InlineData("(-webkit-min-device-pixel-ratio: 2)", true)]
    [InlineData("(prefers-color-scheme: dark)", true)]
    [InlineData("(prefers-color-scheme: light)", false)]
    [InlineData("(prefers-reduced-motion: reduce)", true)]
    [InlineData("(prefers-reduced-motion)", true)]
    [InlineData("(prefers-contrast: more)", true)]
    [InlineData("(prefers-contrast)", true)]
    [InlineData("(forced-colors: active)", true)]
    [InlineData("(inverted-colors: inverted)", true)]
    [InlineData("(scripting: enabled)", true)]
    [InlineData("(scripting)", true)]
    [InlineData("(pointer: coarse)", true)]
    [InlineData("(any-pointer: coarse)", true)]
    [InlineData("(hover: none)", true)]
    [InlineData("(hover)", false)]
    [InlineData("(color-gamut: srgb)", true)]
    [InlineData("(color-gamut: p3)", true)]
    [InlineData("(color-gamut: rec2020)", false)]
    [InlineData("(dynamic-range: high)", true)]
    [InlineData("(dynamic-range: standard)", true)]
    [InlineData("(display-mode: standalone)", true)]
    public void Matches_CustomEnvironment(string query, bool expected)
    {
        var environment = new MediaEnvironment
        {
            Width = 400,
            Height = 800,
            DevicePixelRatio = 2,
            FontSize = 20,
            PrefersColorScheme = "dark",
            PrefersReducedMotion = true,
            PrefersContrast = "more",
            ForcedColors = true,
            InvertedColors = true,
            ScriptingEnabled = true,
            Pointer = "coarse",
            Hover = "none",
            ColorGamut = "p3",
            DynamicRange = "high",
            DisplayMode = "standalone",
        };

        Assert.Equal(expected, MediaQueryEvaluator.Matches(query, environment));
    }

    [Fact]
    public void Matches_SquareViewportIsPortrait()
    {
        var environment = MediaEnvironment.Default with { Width = 500, Height = 500 };
        Assert.True(MediaQueryEvaluator.Matches("(orientation: portrait)", environment));
        Assert.True(MediaQueryEvaluator.Matches("(aspect-ratio: 1)", environment));
    }

    [Fact]
    public void Matches_MonochromeDevice()
    {
        var environment = MediaEnvironment.Default with { ColorBits = 0, MonochromeBits = 2 };
        Assert.False(MediaQueryEvaluator.Matches("(color)", environment));
        Assert.True(MediaQueryEvaluator.Matches("(monochrome)", environment));
        Assert.True(MediaQueryEvaluator.Matches("(min-monochrome: 2)", environment));
        Assert.False(MediaQueryEvaluator.Matches("(color-gamut: srgb)", environment));
    }

    [Fact]
    public void Matches_ZeroSizedViewport()
    {
        var environment = MediaEnvironment.Default with { Width = 0, Height = 0 };
        Assert.False(MediaQueryEvaluator.Matches("(width)", environment));
        Assert.True(MediaQueryEvaluator.Matches("(width: 0)", environment));
        Assert.False(MediaQueryEvaluator.Matches("(aspect-ratio: 1)", environment));
        Assert.False(MediaQueryEvaluator.Matches("(aspect-ratio)", environment));
    }

    [Fact]
    public void Matches_ListOfSerializedQueries()
    {
        var environment = MediaEnvironment.Default;
        Assert.True(MediaQueryEvaluator.Matches(Array.Empty<string>(), environment));
        Assert.True(MediaQueryEvaluator.Matches(["print", "screen"], environment));
        Assert.False(MediaQueryEvaluator.Matches(["print", "not all"], environment));
        Assert.False(MediaQueryEvaluator.Matches(["not all"], environment));
        Assert.True(MediaQueryEvaluator.Matches(["(min-width: 400px)"], environment));
        Assert.False(MediaQueryEvaluator.Matches(["(min-width: 900px)"], environment));
    }

    [Fact]
    public void Matches_MediaListItems()
    {
        var list = new MediaList("print, screen and (min-width: 500px), bad$");
        Assert.True(MediaQueryEvaluator.Matches(list, MediaEnvironment.Default));
        Assert.False(MediaQueryEvaluator.Matches(list, MediaEnvironment.Default with { Width = 400 }));
        Assert.True(MediaQueryEvaluator.Matches(list, MediaEnvironment.Default with { Width = 400, MediaType = "print" }));
    }

    [Fact]
    public void Matches_ComponentValues()
    {
        var values = CssParser.ParseListOfComponentValues("print, (width >= 600px) and (orientation: landscape)");
        Assert.True(MediaQueryEvaluator.Matches(values, MediaEnvironment.Default));
        Assert.False(MediaQueryEvaluator.Matches(values, MediaEnvironment.Default with { Width = 500 }));

        // The results depend on the environment even though the parsed queries are cached
        Assert.True(MediaQueryEvaluator.Matches(values, MediaEnvironment.Default));
        Assert.True(MediaQueryEvaluator.Matches(CssParser.ParseListOfComponentValues(" "), MediaEnvironment.Default));
        Assert.False(MediaQueryEvaluator.Matches(CssParser.ParseListOfComponentValues("(unknown)"), MediaEnvironment.Default));
    }

    [Fact]
    public void Matches_EnvironmentDependentUnitsAreNotCached()
    {
        const string Query = "(width: 50em)";
        Assert.True(MediaQueryEvaluator.Matches(Query, MediaEnvironment.Default));
        Assert.False(MediaQueryEvaluator.Matches(Query, MediaEnvironment.Default with { FontSize = 20 }));
        Assert.True(MediaQueryEvaluator.Matches(Query, MediaEnvironment.Default with { FontSize = 20, Width = 1000 }));
    }

    [Fact]
    public void Matches_IsThreadSafe()
    {
        var results = new bool[1000];
        Parallel.For(0, results.Length, i => results[i] = MediaQueryEvaluator.Matches("screen and (min-width: " + (i % 20).ToString(System.Globalization.CultureInfo.InvariantCulture) + "px)", MediaEnvironment.Default));
        Assert.All(results, Assert.True);
    }
}
