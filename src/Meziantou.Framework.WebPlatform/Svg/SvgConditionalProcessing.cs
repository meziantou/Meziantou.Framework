using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The conditional processing attributes (https://svgwg.org/svg2-draft/struct.html#ConditionalProcessing): an element whose
/// <c>requiredExtensions</c> or <c>systemLanguage</c> attribute evaluates to false is not rendered.
/// </summary>
internal static class SvgConditionalProcessing
{
    /// <summary>The extensions supported in <c>requiredExtensions</c>: the HTML and MathML namespaces (as in the browsers).</summary>
    private static readonly string[] SupportedExtensions = [Namespaces.Html, Namespaces.MathML];

    /// <summary>The primary language subtag of the language of the user (https://svgwg.org/svg2-draft/struct.html#SystemLanguageAttribute), the one of <c>navigator.language</c>.</summary>
    private const string UserLanguage = "en";

    /// <summary>
    /// Returns whether the conditional processing attributes do not prevent the rendering of an element: the ones of the element
    /// and of its SVG ancestors evaluate to true, and each of them that is a child of a switch element is the child that the
    /// switch element renders (https://svgwg.org/svg2-draft/struct.html#SwitchElement: its first child whose attributes evaluate to true).
    /// </summary>
    public static bool IsRendered(Element element)
    {
        for (var current = element; current is SvgElement; current = current.ParentNode as Element)
        {
            if (!Evaluate(current))
                return false;

            if (current.ParentNode is SvgElement { LocalName: "switch" } parent)
            {
                for (var sibling = parent.FirstElementChild; sibling is not null && !ReferenceEquals(sibling, current); sibling = sibling.NextElementSibling)
                {
                    // As in the painting: the first child that is rendered and whose attributes evaluate to true
                    if (sibling is SvgElement && !Css.Layout.BoxGeneration.IsNeverRenderedSvgElement(sibling) && sibling.LocalName is not ("animate" or "animateMotion" or "animateTransform" or "set" or "mpath")
                        && SvgUserSpace.GetComputedValue(sibling, "display") != "none" && Evaluate(sibling))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>Returns whether the conditional processing attributes of an element evaluate to true (or are absent).</summary>
    public static bool Evaluate(Element element)
    {
        // "If the attribute is not present, then it evaluates to true. If a null string or empty string value is given ... it evaluates to false"
        if (element.GetAttributeValue("requiredExtensions") is { } extensions)
        {
            var tokens = extensions.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return false;

            foreach (var token in tokens)
            {
                if (Array.IndexOf(SupportedExtensions, token) < 0)
                    return false;
            }
        }

        if (element.GetAttributeValue("systemLanguage") is { } languages)
        {
            var matches = false;
            foreach (var language in languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // As in the browsers, the primary language subtags are compared (en matches en-US and en-GB)
                var dash = language.IndexOf('-', StringComparison.Ordinal);
                if (InfraAscii.EqualsIgnoreAsciiCase(dash < 0 ? language : language[..dash], UserLanguage))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches)
                return false;
        }

        return true;
    }
}
