using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The HDR colors (https://drafts.csswg.org/css-color-hdr-1/): the HDR color spaces and color-hdr().</summary>
public sealed class HdrColorTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html) => _contexts.Create(new BrowsingContextOptions()).Navigate(_ => html);

    private string GetComputedColor(string html)
    {
        var document = Parse(html);
        return document.DefaultView!.GetComputedStyle(document.QuerySelector("#target")!, null).GetPropertyValue("color");
    }

    // The expected values are the ones of the examples of the specification, and of color.js for the others
    [Theory]
    [InlineData("ictcp(from white i ct cp)", "ictcp(0.580689 0 0)")]
    [InlineData("ictcp(from red i ct cp)", "ictcp(0.42788 -0.115704 0.278729)")]
    [InlineData("ictcp(from lime i ct cp)", "ictcp(0.53976 -0.281248 -0.049485)")]
    [InlineData("ictcp(from color(rec2100-pq 0.58 0 0) i ct cp)", "ictcp(0.446445 -0.129412 0.398965)")]
    [InlineData("ictcp(from color(rec2020 1 0 0) i ct cp)", "ictcp(0.447073 -0.12957 0.399201)")]
    [InlineData("jzazbz(from white j a b)", "jzazbz(0.222065 -0.000161 -0.000117)")]
    [InlineData("jzazbz(from lime j a b)", "jzazbz(0.176807 -0.109043 0.118989)")]
    [InlineData("jzazbz(from black j a b)", "jzazbz(0 0 0)")]
    [InlineData("jzczhz(from lime j c h)", "jzczhz(0.176807 0.161397 132.503)")]
    [InlineData("color(from white rec2100-pq r g b)", "color(rec2100-pq 0.580689 0.580689 0.580689)")]
    [InlineData("color(from white rec2100-hlg r g b)", "color(rec2100-hlg 0.749991 0.749991 0.749991)")]
    [InlineData("color(from white rec2100-linear r g b)", "color(rec2100-linear 1 1 1)")]
    [InlineData("color(from ictcp(0.58069 0 0) srgb r g b)", "color(srgb 1 1 1)")]
    [InlineData("color(from color(rec2100-pq 0.34 0.34 0.34) srgb r g b)", "color(srgb 0.314681 0.314681 0.314681)")]
    [InlineData("color(from color(rec2100-hlg 0.38 0.38 0.38) srgb r g b)", "color(srgb 0.463346 0.463346 0.463346)")]

    // The absolute XYZ of the examples of https://drafts.csswg.org/css-color-hdr-1/#headroom-interpolation
    [InlineData("color(from color(rec2100-linear 0.9 1.0 0.8) xyz-d65 calc(x * 203) calc(y * 203) calc(z * 203))", "color(xyz-d65 173.156 195.26 178.003)")]
    [InlineData("color(from jzczhz(70% 0.14 100) xyz-d65 calc(x * 203) calc(y * 203) calc(z * 203))", "color(xyz-d65 3776.14 4362.41 1577.91)")]
    [InlineData("color(from ictcp(50% -0.3 0) xyz-d65 calc(x * 203) calc(y * 203) calc(z * 203))", "color(xyz-d65 63.6033 96.5342 8.55834)")]

    // The percentage reference ranges, the negative chroma and the math functions
    [InlineData("ictcp(50% 100% -100%)", "ictcp(0.5 0.5 -0.5)")]
    [InlineData("jzazbz(50% 100% -100% / 50%)", "jzazbz(0.5 0.21 -0.21 / 0.5)")]
    [InlineData("jzczhz(50% 100% 1rad)", "jzczhz(0.5 0.26 57.2958)")]
    [InlineData("jzczhz(none -10% 370deg)", "jzczhz(none 0 10)")]
    [InlineData("color(rec2100-pq 50% 0.2 none)", "color(rec2100-pq 0.5 0.2 none)")]
    [InlineData("ictcp(calc(0.1 + 0.2) 0 0)", "ictcp(0.3 0 0)")]
    [InlineData("color-mix(in srgb, color(rec2100-linear 1 1 1), black)", "color(srgb 0.5 0.5 0.5)")]
    public void ComputedColor(string value, string expected)
    {
        Assert.Equal(expected, GetComputedColor("<div id=target style='color: " + value + "'></div>"));
    }

    [Theory]
    [InlineData("ictcp(50% 0.1 -0.1)", "ictcp(0.5 0.1 -0.1)")]
    [InlineData("jzczhz(0.2 0.1 1turn)", "jzczhz(0.2 0.1 0)")]
    [InlineData("color(rec2100-hlg 0.75 0.75 0.75)", "color(rec2100-hlg 0.75 0.75 0.75)")]
    [InlineData("ictcp(from red i ct cp / 0.5)", "ictcp(from red i ct cp / 0.5)")]
    [InlineData("ictcp(0.5, 0, 0)", "")]
    [InlineData("jzczhz(0.5 0.1 10%)", "")]
    [InlineData("ictcp(0.5 0 1deg)", "")]
    [InlineData("color(rec2100-pq 1 1)", "")]

    // https://drafts.csswg.org/css-color-hdr-1/#hdr-color-function: the headroom follows its color
    [InlineData("color-hdr(red 0, blue 2)", "color-hdr(red 0, blue 2)")]
    [InlineData("color-hdr(2 blue, red)", "color-hdr(blue 2, red)")]
    [InlineData("color-hdr(hsl(120 50% 50%) 0, ictcp(0.5 0 0) calc(1 + 1))", "color-hdr(rgb(64, 191, 64) 0, ictcp(0.5 0 0) calc(2))")]
    [InlineData("color-hdr(red 1, blue 1)", "")]
    [InlineData("color-hdr(red, blue)", "")]
    [InlineData("color-hdr(red -1, blue 2)", "")]
    [InlineData("color-hdr(red 0px, blue 2)", "")]
    [InlineData("color-hdr(red 0)", "")]
    [InlineData("color-hdr(red 0, blue 2, green 3)", "")]
    public void SpecifiedColor(string value, string expected)
    {
        var document = Parse("<div id=target style='color: " + value + "'></div>");
        Assert.Equal(expected, ((HtmlElement)document.QuerySelector("#target")!).Style.GetPropertyValue("color"));
    }

    // The color of color-hdr() depends on the HDR headroom, which is not exposed: the computed value keeps the function
    [Theory]
    [InlineData("<div id=target style='color: color-hdr(red 0, blue 2)'></div>", "color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2)")]
    [InlineData("<div id=target style='color: color-hdr(2 blue, hsl(120 50% 50%))'></div>", "color-hdr(rgb(0, 0, 255) 2, rgb(64, 191, 64))")]
    [InlineData("<div id=target style='color: color-hdr(red calc(-5), blue calc(1 + 1))'></div>", "color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2)")]
    [InlineData("<div style='color: green'><div id=target style='color: color-hdr(currentcolor 0, blue 2)'></div></div>", "color-hdr(rgb(0, 128, 0) 0, rgb(0, 0, 255) 2)")]
    [InlineData("<div style='color: color-hdr(red 0, blue 2)'><div id=target></div></div>", "color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2)")]
    [InlineData("<div style='color: color-hdr(red 0, blue 2)'><div id=target style='color: currentcolor'></div></div>", "color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2)")]
    [InlineData("<div style='color: color-hdr(red 0, blue 2)'><div id=target style='color: color-mix(in srgb, currentcolor, red)'></div></div>", "color-mix(in srgb, color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2), rgb(255, 0, 0))")]
    [InlineData("<div id=target style='color: color-mix(in srgb, color-hdr(red 0, blue 2), white)'></div>", "color-mix(in srgb, color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2), rgb(255, 255, 255))")]
    [InlineData("<div id=target style='color: rgb(from color-hdr(red 0, blue 2) r g b)'></div>", "rgb(from color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2) r g b)")]
    public void ComputedHdrColor(string html, string expected)
    {
        Assert.Equal(expected, GetComputedColor(html));
    }

    [Fact]
    public void HdrColorOfOtherProperties()
    {
        var document = Parse("<div id=target style='color: color-hdr(red 0, blue 2); border-color: currentcolor; background-color: color-hdr(currentcolor 1, white 2)'></div>");
        var style = document.DefaultView!.GetComputedStyle(document.QuerySelector("#target")!, null);
        Assert.Equal("color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2)", style.GetPropertyValue("border-top-color"));
        Assert.Equal("color-hdr(color-hdr(rgb(255, 0, 0) 0, rgb(0, 0, 255) 2) 1, rgb(255, 255, 255) 2)", style.GetPropertyValue("background-color"));
    }

    // https://html.spec.whatwg.org/multipage/input.html#color-state-(type=color): the colors are parsed without an element,
    // and color-hdr() is the color of an SDR display (a headroom of 0, https://drafts.csswg.org/css-color-hdr-1/#headroom-interpolation)
    [Theory]
    [InlineData("color-hdr(red 0, blue 2)", "#ff0000")]
    [InlineData("color-hdr(blue 2, red 1)", "#ff0000")]
    [InlineData("color-hdr(red 1, blue 1)", "#000000")]
    [InlineData("color-mix(in srgb, color-hdr(red 0, blue 2), white)", "#ff8080")]
    [InlineData("color(rec2100-pq 0.580689 0.580689 0.580689)", "#ffffff")]
    [InlineData("ictcp(0.580689 0 0)", "#ffffff")]
    public void ColorInput(string value, string expected)
    {
        var document = Parse("<input id=target type=color>");
        var input = (HtmlInputElement)document.QuerySelector("#target")!;
        input.SetAttribute("value", value);
        Assert.Equal(expected, input.Value);
    }
}
