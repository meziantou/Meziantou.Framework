using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Fuzz;

public static partial class WebPlatformFuzzTargets
{
    /// <summary>The maximum length of the documents of the style target (the cost of the styles grows with the elements and the rules).</summary>
    private const int StyleMaxLength = 4096;

    /// <summary>The maximum number of elements whose styles the style target checks.</summary>
    private const int StyleMaxElements = 32;

    /// <summary>The maximum number of elements whose computed values the style target reads in two orders (all the properties).</summary>
    private const int StyleMaxComputedElements = 8;

    // (a property: the targets of the other part of the class read it when they are initialized, before the fields of this part)
    private static string[] StyleTokens =>
    [
        "<style>", "</style>", "<div>", "<p>", "<span>", "<ul>", "<li>", "<ol>", "<svg>", "<a ", " class=", " id=", " title=", " data-x=",
        " viewBox=", " xlink:href=", " style=", "{", "}", ":", ";", ",", "*", ">", "+", "~", "[", "]", "=", "~=", "^=", "|=", " i]",
        "::before", "::after", "::marker", ":is(", ":not(", ":has(", ":hover", "content:", "'x'", "all:", "inherit", "none", "display:",
        "list-item", "contents", "counter(", "counter-increment:", "attr(", "var(--", "--x:", "transition:", "animation:", "@keyframes",
        "@media", "@layer", "@scope", "@namespace", "box-sizing:", "color:", "width:", "float:", "position:", "!important", "stretch(", "fit-content(", "margin:", "height:",
        "@mixin --m", "@mixin --m(--x", "@apply --m", "@apply --m(", "@contents", "@private", "&", ".a", "{ }",
    ];

    /// <summary>The names of the supported properties that are not aliases, in the order of the registry.</summary>
    private static readonly Lazy<string[]> StylePropertyNames = new(() => [.. CssPropertyRegistry.Properties.Where(property => property.AliasOf is null).Select(property => property.Name)]);

    /// <summary>
    /// Computes the styles of a document and checks the shortcuts of the style engine against the full computation: the
    /// candidate rules of an element (by id, class, tag and attribute) include every author rule whose selector matches it; a
    /// ::before or ::after pseudo-element that the engine says has no content (whose style is not computed) computes to no
    /// content; and the computed values of the properties do not depend on the order in which they are read.
    /// </summary>
    public static void Style(string input)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = FrozenTimeProvider.Instance });
        var document = context.Navigate("https://example.com/", _ => input);
        var elements = document.QuerySelectorAll("*").Cast<Element>().Take(StyleMaxElements).ToArray();
        var engine = document.GetStyleEngine();
        var window = document.DefaultView!;

        // The candidates of the elements include the rules that match them (the selectors are matched without the index)
        var collector = new CascadeRuleCollector(MediaEnvironment.Default, CascadeOrigin.Author, document);
        foreach (var sheet in document.StyleSheets)
        {
            collector.AddStyleSheet(sheet, baseUrl: null);
        }

        var ruleSet = collector.Complete();
        var rules = ruleSet.EnumerateRules().Where(rule => rule.Kind == CascadeRuleKind.Element && rule.Scope is null).ToArray();
        foreach (var element in elements)
        {
            // (a list can be enumerated twice: the classes are looked up ignoring the case, e.g. class="a A")
            var candidates = new HashSet<CascadeRule>(ReferenceEqualityComparer.Instance);
            foreach (var list in ruleSet.GetCandidates(element))
            {
                candidates.UnionWith(list);
            }

            foreach (var rule in rules)
            {
                var matches = rule.PseudoElement is null
                    ? SelectorMatcher.MatchesComplex(rule.Selector, element, default)
                    : SelectorMatcher.MatchesOriginatingElement(rule.Selector, element, default);
                if (matches)
                {
                    FuzzAssert.True(candidates.Contains(rule), $"A rule whose selector matches <{element.LocalName}> is not one of its candidates");
                }
            }
        }

        // (the engine is asked before the style of the pseudo-element is computed: a computed style is always a candidate)
        foreach (var element in elements)
        {
            foreach (var pseudoElement in (ReadOnlySpan<string>)["before", "after"])
            {
                if (engine.MayHaveGeneratedContent(element, pseudoElement))
                    continue;

                var content = window.GetComputedStyle(element, "::" + pseudoElement).GetPropertyValue("content");
                FuzzAssert.True(content is "none" or "normal", $"The ::{pseudoElement} of <{element.LocalName}> has the content {FuzzAssert.Format(content)}, but its style was not expected to give it one");
            }
        }

        // The values of a second document are read in the reverse order (the values are stored by property index), and the
        // values of a third one in the same order: the values that differ between two documents (e.g. random() draws a value
        // for each document) are not compared
        var reversed = ReadComputedValues(input, reverse: true);
        var repeated = ReadComputedValues(input, reverse: false);
        var names = StylePropertyNames.Value;
        for (var i = 0; i < Math.Min(Math.Min(reversed.Length, repeated.Length), elements.Length); i++)
        {
            var style = window.GetComputedStyle(elements[i]);
            for (var j = 0; j < names.Length; j++)
            {
                var value = style.GetPropertyValue(names[j]);
                if (value == repeated[i][j])
                {
                    FuzzAssert.Equal(value, reversed[i][j], $"The computed value of {names[j]} of <{elements[i].LocalName}> depends on the order in which the properties are read");
                }
            }
        }
    }

    /// <summary>Loads the document in a new browsing context and reads the computed values of all the properties of its first elements, in the order of <see cref="StylePropertyNames"/> or in the reverse order.</summary>
    private static string[][] ReadComputedValues(string input, bool reverse)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = FrozenTimeProvider.Instance });
        var document = context.Navigate("https://example.com/", _ => input);
        var names = StylePropertyNames.Value;
        var result = new List<string[]>();
        foreach (var element in document.QuerySelectorAll("*").Cast<Element>().Take(StyleMaxComputedElements))
        {
            var style = document.DefaultView!.GetComputedStyle(element);
            var values = new string[names.Length];
            for (var k = 0; k < names.Length; k++)
            {
                var j = reverse ? names.Length - 1 - k : k;
                values[j] = style.GetPropertyValue(names[j]);
            }

            result.Add(values);
        }

        return [.. result];
    }

    /// <summary>The maximum length of the input of the link parameters target.</summary>
    private const int LinkParametersMaxLength = 1024;

    /// <summary>The tokens of the link parameters target: the grammar of param(), of its types, and of the fragment directives.</summary>
    private static string[] LinkParametersTokens =>
    [
        "param(", "--a", "--b", "--", "color", "accent-color", ":", ": ", ",", ")", " ", "<color>", "<length>", "<custom-ident>+", "<number>#", "type(", " | ", "*",
        "var(--x)", "var(--missing, red)", "env(--a)", "ident(\"--a\")", "currentcolor", "none", "red", "10px", "#0a5c2e", ";", "!", "{", "}",
        "#:~:", "&", "%20", "%3A", "text=", "url(\"a.svg\" ", "cross-origin(anonymous)",
    ];

    /// <summary>
    /// Parses link parameters (https://drafts.csswg.org/css-link-params-1/): the input is a value of <c>link-parameters</c>, the
    /// modifiers of a <c>url()</c> and the fragment directive of a URL. The serialization of a valid value is parsed as the same
    /// value; the parameters of the fragment are serialized as parameters parsed as themselves; and the parameters resolved
    /// against an element are equal when they are resolved again, so that the rendering of an SVG image with them is cached.
    /// </summary>
    public static void LinkParameters(string input)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = FrozenTimeProvider.Instance });
        var document = context.Navigate("https://example.com/", _ => "<div id=d style='--x: blue; color: green'></div>");
        var element = (HtmlElement)document.GetElementById("d")!;

        // The value of the property, and the modifiers of a url()
        foreach (var (property, value) in (ReadOnlySpan<(string, string)>)[("link-parameters", input), ("background-image", "url(\"a.svg\" " + input + ")")])
        {
            element.Style.SetProperty(property, value);
            var serialized = element.Style.GetPropertyValue(property);
            if (serialized.Length == 0)
                continue;

            element.Style.RemoveProperty(property);
            element.Style.SetProperty(property, serialized);
            FuzzAssert.Equal(serialized, element.Style.GetPropertyValue(property), $"The serialization of {property} is not parsed as the same value: {FuzzAssert.Format(serialized)}");
        }

        // The parameters of the fragment directive: the URL of the document has no fragment directive
        var url = "https://example.com/a.svg#box:~:" + input;
        FuzzAssert.Equal("https://example.com/a.svg#box", CssLinkParameters.RemoveFragmentDirective(url), "The fragment directive is not removed");
        var directives = CssLinkParameters.ParseFragmentDirectives(url);
        foreach (var parameter in directives)
        {
            var text = parameter.ToString();
            var reparsed = CssParser.ParseListOfComponentValues(text) is [CssFunction function] ? CssLinkParameters.Parse(function) : null;
            FuzzAssert.Equal(text, reparsed?.ToString(), $"The serialization of a parameter is not parsed as the same parameter: {FuzzAssert.Format(text)}");
        }

        // The resolution against the element: the property, the fragment and the modifiers of the url()
        var style = document.GetStyleEngine().GetStyle(element);
        var modifiers = CssParser.ParseListOfComponentValues(element.Style.GetPropertyValue("background-image")) is [var image] ? CssUrlModifiers.GetLinkParameters(image) : [];
        var resolved = CssLinkParameters.Resolve(style, url, modifiers, document);
        var again = CssLinkParameters.Resolve(style, url, modifiers, document);
        FuzzAssert.True(Equals(resolved, again) && (resolved?.GetHashCode() ?? 0) == (again?.GetHashCode() ?? 0), "The parameters resolved twice are not equal");

        // The SVG image uses the parameters, and its rendering is cached by them
        var bytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='env(--a, 10px)' height='10' fill='env(--b, red)' stroke='env(color)'/></svg>");
        var svgImage = SvgImage.Get(new PaintImage(url, 10, 10, bytes, "image/svg+xml"), document)!;
        var list = svgImage.GetDisplayList(new PhysicalSize(10, 10), linkParameters: resolved);
        FuzzAssert.True(ReferenceEquals(list, svgImage.GetDisplayList(new PhysicalSize(10, 10), linkParameters: again)), "The rendering of the SVG image is not cached by its parameters");
    }

    /// <summary>A clock that does not move: the two documents of the style target compute the same animated values.</summary>
    private sealed class FrozenTimeProvider : TimeProvider
    {
        public static FrozenTimeProvider Instance { get; } = new();

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => 0;

        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
