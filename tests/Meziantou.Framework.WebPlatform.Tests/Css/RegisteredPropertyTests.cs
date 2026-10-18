using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using CssNamespace = Meziantou.Framework.WebPlatform.Css.Om.Css;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>The custom properties registered with CSS.registerProperty() and @property (CSS Properties and Values API).</summary>
public sealed class RegisteredPropertyTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html) => _contexts.Create().Navigate(_ => html);

    private static string Get(Document document, string property, string selector = "#target") =>
        document.DefaultView!.GetComputedStyle(document.QuerySelector(selector)!).GetPropertyValue(property);

    private static CssStyleSheet GetSheet(Document document) => ((HtmlStyleElement)document.QuerySelector("style")!).Sheet!;

    private static void Register(Document document, string name, string syntax, string? initialValue, bool inherits = false) =>
        CssNamespace.RegisterProperty(document, new PropertyDefinition { Name = name, Syntax = syntax, InitialValue = initialValue, Inherits = inherits });

    [Fact]
    public void RegisterProperty_ComputesTheValues()
    {
        var document = Parse("<div id=target style='font-size: 10px; --a: 2em; --b: red'></div>");
        Assert.Equal("2em", Get(document, "--a"));

        Register(document, "--a", "<length>", "0px");
        Register(document, "--b", "<length>", "3px");
        Register(document, "--c", "<color>", "green");
        Assert.Equal("20px", Get(document, "--a"));
        Assert.Equal("3px", Get(document, "--b")); // Invalid at computed-value time: the initial value
        Assert.Equal("rgb(0, 128, 0)", Get(document, "--c"));
    }

    [Fact]
    public void RegisterProperty_Inherits()
    {
        var document = Parse("<div style='--a: 1px; --b: 1px'><div id=target></div></div>");
        Register(document, "--a", "<length>", "0px", inherits: true);
        Register(document, "--b", "<length>", "0px", inherits: false);
        Assert.Equal("1px", Get(document, "--a"));
        Assert.Equal("0px", Get(document, "--b"));
    }

    [Fact]
    public void RegisterProperty_WinsOverPropertyRules()
    {
        var document = Parse("<style>@property --a { syntax: '<color>'; inherits: false; initial-value: red }</style><div id=target></div>");
        Assert.Equal("rgb(255, 0, 0)", Get(document, "--a"));

        Register(document, "--a", "<length>", "1px");
        Assert.Equal("1px", Get(document, "--a"));
    }

    [Theory]
    [InlineData("no-dashes", "*", null, "SyntaxError")]
    [InlineData("--a", "<length>", null, "SyntaxError")]
    [InlineData("--a", "<length>", "1em", "SyntaxError")]
    [InlineData("--a", "<length>", "var(--b)", "SyntaxError")]
    [InlineData("--a", "*", "var(--b)", "SyntaxError")]
    [InlineData("--a", "*", "initial", "SyntaxError")]
    [InlineData("--a", "<length> <number>", "1px 2", "SyntaxError")]
    [InlineData("--a", "<Length>", "1px", "SyntaxError")]
    [InlineData("--a", "<transform-list>+", "scale(2)", "SyntaxError")]
    [InlineData("--a", "default", "default", "SyntaxError")]
    [InlineData("--a", "<length> | inherit", "1px", "SyntaxError")]
    [InlineData("--a", "foo", "Foo", "SyntaxError")]
    [InlineData("--a", "<resolution>", "-1dppx", "SyntaxError")]
    public void RegisterProperty_InvalidDefinition(string name, string syntax, string? initialValue, string expectedError)
    {
        var document = Parse("<div id=target></div>");
        var exception = Assert.Throws<DomException>(() => Register(document, name, syntax, initialValue));
        Assert.Equal(expectedError, exception.Name);
    }

    [Theory]
    [InlineData("*", null)]
    [InlineData(" * ", "")]
    [InlineData("<length>", "10vw")]
    [InlineData("<length>+ | <color>#", "red, blue")]
    [InlineData("big | bigger", "bigger")]
    [InlineData("banan\\61", "banana")]
    [InlineData("--foo | <color>", "--foo")]
    [InlineData("<transform-function>+", "translateX(2px) rotate(42deg)")]
    [InlineData("<integer>", "calc(3.1415)")]
    [InlineData("<string>#", "'a', 'b'")]
    [InlineData("<image>", "linear-gradient(red, blue)")]
    public void RegisterProperty_ValidDefinition(string syntax, string? initialValue)
    {
        var document = Parse("<div id=target></div>");
        Register(document, "--a", syntax, initialValue);
    }

    [Fact]
    public void RegisterProperty_AlreadyRegistered()
    {
        var document = Parse("<div id=target></div>");
        Register(document, "--a", "<length>", "0px");
        Assert.Equal("InvalidModificationError", Assert.Throws<DomException>(() => Register(document, "--a", "<length>", "0px")).Name);
    }

    [Theory]
    [InlineData("<length>#", "10px, 3em", "10px, 30px")]
    [InlineData("<length>+", "4em 9px", "40px 9px")]
    [InlineData("<length>", "0", "0px")]
    [InlineData("<length-percentage>", "calc(19em - 2%)", "calc(-2% + 190px)")]
    [InlineData("<integer>", "calc(2.6 + 3.1)", "6")]
    [InlineData("<number>", "calc(24 / 10)", "2.4")]
    [InlineData("<angle>", "400grad", "360deg")]
    [InlineData("<time>", "1000ms", "1s")]
    [InlineData("<resolution>", "96dpi", "1dppx")]
    [InlineData("<color>", "currentcolor", "rgb(0, 0, 255)")]
    [InlineData("<color>", "color-mix(in srgb, currentcolor, red)", "color(srgb 0.5 0 0.5)")]
    [InlineData("tomato | <color>", "tomato", "tomato")]
    [InlineData("<custom-ident>+", "a b", "a b")]
    [InlineData("<string>", "'a'", "\"a\"")]
    [InlineData("<transform-function>", "translateX(10em)", "translateX(100px)")]
    [InlineData("<transform-list>", "rotateX(0deg) translateX(1em)", "rotateX(0deg) translateX(10px)")]
    [InlineData("<url>", "url(a.png)", "url(\"https://example.com/a.png\")")]
    public void ComputedValues(string syntax, string value, string expected)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<div id=target style='font-size: 10px; color: blue'></div>");
        Register(document, "--b", syntax, InitialValueFor(syntax));
        ((HtmlElement)document.GetElementById("target")!).Style.SetProperty("--b", value);
        Assert.Equal(expected, Get(document, "--b"));
    }

    private static string InitialValueFor(string syntax) => syntax switch
    {
        "<length>#" or "<length>+" or "<length>" or "<length-percentage>" => "0px",
        "<integer>" or "<number>" => "0",
        "<angle>" => "0deg",
        "<time>" => "0s",
        "<resolution>" => "1dppx",
        "<color>" => "red",
        "tomato | <color>" => "tomato",
        "<custom-ident>+" => "a",
        "<string>" => "''",
        "<transform-function>" or "<transform-list>" => "scale(1)",
        "<url>" => "url(b.png)",
        _ => throw new ArgumentOutOfRangeException(nameof(syntax)),
    };

    [Theory]
    [InlineData("@property --a { syntax: 'rgb(255, 0, 0)'; inherits: false; initial-value: red }")]
    [InlineData("@property --a { syntax: 'default'; inherits: false; initial-value: default }")]
    [InlineData("@property --a { syntax: '<length>'; inherits: false; initial-value: 3em }")]
    [InlineData("@property --a { syntax: '*'; inherits: false; initial-value: var(--x) }")]
    public void PropertyRules_InvalidRulesAreDropped(string rule)
    {
        var document = Parse($"<style>{rule}</style><div id=target style='--a: calc(1px + 1px)'></div>");
        Assert.Empty(GetSheet(document).CssRules);
        Assert.Equal("calc(1px + 1px)", Get(document, "--a"));
    }

    [Fact]
    public void PropertyRules_InitialValueAcceptsAnyValue()
    {
        var document = Parse("<style>@property --a { syntax: '*'; inherits: false; initial-value: foo(){} }</style><div id=target></div>");
        Assert.Equal("foo(){}", ((CssPropertyRule)GetSheet(document).CssRules[0]).InitialValue);
        Assert.Equal("foo(){}", Get(document, "--a"));
    }

    [Fact]
    public void PropertyRules_ShadowTreesRegisterGlobally()
    {
        var document = Parse("<div id=host></div><div id=target style='--a: calc(1px + 1px)'></div>");
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<style>@property --a { syntax: '<length>'; inherits: false; initial-value: 0px }</style>";
        Assert.Equal("2px", Get(document, "--a"));
    }

    [Fact]
    public void Cycles_RegisteredPropertiesAreUnset()
    {
        var document = Parse("<div id=target style='--a: var(--b); --b: var(--a); --c: var(--a, 5px); --d: var(--a)'></div>");
        Register(document, "--a", "<length>", "1px");
        Register(document, "--b", "<length>", "2px");
        Assert.Equal("1px", Get(document, "--a"));
        Assert.Equal("2px", Get(document, "--b"));
        Assert.Equal("1px", Get(document, "--c"));
        Assert.Equal("1px", Get(document, "--d"));
    }

    [Fact]
    public void Cycles_FontRelativeUnits()
    {
        // font-size -> --u -> --a -> font-size: the properties of the cycle are invalid at computed-value time
        var document = Parse("<div style='font-size: 20px'><div id=target style='--a: 2em; --u: var(--a); font-size: var(--u); --z: var(--u, 5px); --r: 2rem'></div></div>");
        Register(document, "--a", "<length>", "3px");
        Register(document, "--r", "<length>", "0px");
        Assert.Equal("20px", Get(document, "font-size"));
        Assert.Equal("3px", Get(document, "--a"));
        Assert.Equal("", Get(document, "--u"));
        Assert.Equal("5px", Get(document, "--z"));

        // rem only depends on the font size of the root element
        document.GetElementById("target")!.SetAttribute("style", "--r: 2rem; font-size: var(--r)");
        Assert.Equal("32px", Get(document, "--r"));
        Assert.Equal("32px", Get(document, "font-size"));
    }
}
