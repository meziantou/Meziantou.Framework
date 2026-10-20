using System.Collections.Frozen;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The visited links (https://drafts.csswg.org/selectors-4/#link). Selectors lets the user agents choose how they preserve
// the privacy of the user; as the browsers do, the scripts never observe which links are visited: the style of an element
// (its computed values, which getComputedStyle(), the layout, the transitions and the other APIs use) is computed as if every
// link was unvisited (:visited never matches), and the elements in a visited link have a second style, the visited style,
// used by the painting only, for a few color properties only (the ones of VisitedProperties), whose alpha is the one of the
// unvisited color (so that a visited link cannot be made to cover or uncover something that a script could measure).
// The visited style is computed on demand by the painting (GetUsedValue): its cascade is the cascade of the element where
// its link matches :visited instead of :link, it inherits from the visited style of the parent, and the properties that are
// not color properties, and the custom properties, have the values of the unvisited style.
internal sealed partial class ComputedStyle
{
    /// <summary>
    /// The properties whose painted color is the one of the visited style in a visited link: <c>color</c>,
    /// <c>background-color</c>, the colors of the borders, of the outline, of the rules of the columns and of the gaps, of the
    /// text decorations, of the emphasis marks, of the caret, of the fill and of the stroke of the text, and the colors of the
    /// <c>fill</c> and <c>stroke</c> paints (the properties that the browsers allow, see
    /// https://developer.mozilla.org/docs/Web/CSS/Guides/Selectors/Privacy_and_:visited).
    /// </summary>
    private static readonly FrozenSet<string> VisitedProperties = new[]
    {
        "color", "background-color", "border-top-color", "border-right-color", "border-bottom-color", "border-left-color", "outline-color",
        "column-rule-color", "row-rule-color", "text-decoration-color", "text-emphasis-color", "caret-color", "fill", "stroke",
        "-webkit-text-fill-color", "-webkit-text-stroke-color",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The unvisited style of a visited style (the computed style of the element), or null for the other styles.</summary>
    private ComputedStyle? UnvisitedStyle => _rareData?.UnvisitedStyle;

    /// <summary>
    /// The visited link that the element is in, or null: the element itself when it is a link that was visited (see
    /// <see cref="Document.IsVisitedLink"/>), else the link of the style that it inherits from, unless the element is an
    /// unvisited link. It is always null when the browsing context has no history of visited links.
    /// </summary>
    internal Element? VisitedLink => _rareData?.VisitedLink;

    /// <summary>Whether the style is the visited style of an element (see <see cref="VisitedOrSelf"/>).</summary>
    internal bool IsVisitedStyle => UnvisitedStyle is not null;

    /// <summary>
    /// Whether the cascaded values of the style are the result of the cascade of the rules for its element and its
    /// pseudo-element (a style computed by <see cref="StyleEngine"/>): the cascaded values of its visited style are the result of
    /// the same cascade where the link matches <c>:visited</c>. The other styles (e.g. of the anonymous boxes) have the same
    /// cascaded values in their visited style.
    /// </summary>
    internal bool IsCascadeOfRules { get; init; }

    /// <summary>The root of the use-element shadow tree that the style is the style of an element instance of (see <see cref="StyleEngine.GetUseInstanceStyle"/>), or null.</summary>
    internal Element? UseInstanceRoot
    {
        get => _rareData?.UseInstanceRoot;
        init
        {
            if (value is not null)
            {
                Rare.UseInstanceRoot = value;
            }
        }
    }

    private static Element? GetVisitedLink(StyleEngine engine, Element element, string? pseudoElement, ComputedStyle? parent)
    {
        if (!engine.HasVisitedLinks)
            return null;

        if (pseudoElement is null && HtmlElementStates.IsLink(element))
            return engine.Document.IsVisitedLink(element) ? element : null;

        return parent?.VisitedLink;
    }

    /// <summary>
    /// The style that the painting reads the declarations of the colors from: the visited style of an element in a visited
    /// link, else the style itself. The colors themselves are returned by <see cref="GetUsedValue"/>.
    /// </summary>
    internal ComputedStyle VisitedOrSelf => VisitedLink is null || UnvisitedStyle is not null ? this : Rare.VisitedStyle ??= CreateVisitedStyle();

    private ComputedStyle CreateVisitedStyle()
    {
        return new ComputedStyle(Engine, Element, PseudoElement, Parent?.VisitedOrSelf, GetVisitedCascadedValues(), cascadedShared: true, unvisitedStyle: this)
        {
            HighlightParent = HighlightParent?.VisitedOrSelf,
            IncludeTransitions = IncludeTransitions,
            UseInstanceRoot = UseInstanceRoot,
        };
    }

    /// <summary>Returns the cascaded values of the visited style of the style.</summary>
    private Dictionary<string, CascadedValue> GetVisitedCascadedValues()
    {
        if (_rareData?.VisitedCascaded is { } cascaded)
            return cascaded;

        if (_rareData?.VisitedCascadeSource is { } source)
            return Rare.VisitedCascaded = source.GetVisitedCascadedValues();

        if (IsCascadeOfRules)
            return Rare.VisitedCascaded = Engine.CascadeVisitedStyle(this);

        _cascadedShared = true;
        return Rare.VisitedCascaded = _cascaded;
    }

    /// <summary>
    /// Sets the cascaded values of the visited style of a style derived from this one, whose cascaded values are the ones of
    /// this style where some properties are replaced: they are replaced in the visited style too.
    /// </summary>
    private ComputedStyle WithVisitedCascadeOf(ComputedStyle derived, bool sameCascadedValues = false)
    {
        if (derived.VisitedLink is null || UnvisitedStyle is not null)
            return derived;

        if (sameCascadedValues)
        {
            derived.Rare.VisitedCascadeSource = this;
            return derived;
        }

        var visited = GetVisitedCascadedValues();
        Dictionary<string, CascadedValue>? copy = null;
        foreach (var (name, value) in derived._cascaded)
        {
            // (the logical properties are mapped to the physical ones by the visited style)
            if ((!name.EndsWith("color", StringComparison.Ordinal) && name is not ("fill" or "stroke")) || (_cascaded.TryGetValue(name, out var own) && own.Rank == value.Rank && own.Value == value.Value))
                continue;

            (copy ??= new Dictionary<string, CascadedValue>(visited, StringComparer.Ordinal))[name] = value;
        }

        derived.Rare.VisitedCascaded = copy ?? visited;
        return derived;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Used values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the painted value of a color property of an element in a visited link: the color of the visited style, with the
    /// alpha of the color of the unvisited style ("the alpha component of the visited color is ignored and the alpha of the
    /// unvisited color is used instead", as the browsers do). A fully transparent visited background is not a background that
    /// was set: the unvisited color is painted (as in Blink and Gecko). The paints of <c>fill</c> and <c>stroke</c> that are not
    /// colors (<c>none</c>, a paint server, a context paint) are the ones of the unvisited style. A visited style has a single
    /// color for the rules of the columns and of the gaps, which replaces each color of the unvisited style: as in Chromium, a
    /// list of colors is not a value of the visited style, whose color is then the initial value, <c>currentcolor</c>.
    /// </summary>
    private string GetVisitedDependentValue(string physical, string unvisited)
    {
        if (!VisitedProperties.Contains(physical))
            return unvisited;

        var visitedStyle = VisitedOrSelf;
        var visited = visitedStyle.GetResolvedValue(physical, forceColors: Environment.ForcedColors);
        if (string.Equals(visited, unvisited, StringComparison.Ordinal))
            return unvisited;

        if (physical is not ("fill" or "stroke" or "column-rule-color" or "row-rule-color" or "caret-color"))
            return CombineVisitedColor(physical, unvisited, visited) ?? unvisited;

        // The values that are not a single color: the colors of the unvisited value are replaced one by one
        var unvisitedComponents = CssParser.ParseListOfComponentValues(unvisited);
        var visitedColors = new List<string>();
        CollectColors(CssParser.ParseListOfComponentValues(visited), visitedColors, builder: null, replacements: null);
        var unvisitedColors = new List<string>();
        CollectColors(unvisitedComponents, unvisitedColors, builder: null, replacements: null);
        if (unvisitedColors.Count == 0)
            return unvisited;

        if (visitedColors.Count != 1)
        {
            if (physical is not ("column-rule-color" or "row-rule-color"))
                return unvisited;

            visitedColors = [visitedStyle.GetUsedColor()];
        }

        var replacements = unvisitedColors.ConvertAll(color => CombineVisitedColor(physical, color, visitedColors[0]) ?? color);
        var combined = new StringBuilder(unvisited.Length);
        CollectColors(unvisitedComponents, colors: null, combined, replacements);
        return combined.ToString();
    }

    /// <summary>Returns the visited color with the alpha of the unvisited color, or null when one of them is not a color (e.g. <c>auto</c>).</summary>
    private static string? CombineVisitedColor(string property, string unvisited, string visited)
    {
        if (CssValueComputer.ParseCssColorValue(unvisited) is not { } unvisitedColor || CssValueComputer.ParseCssColorValue(visited) is not { } visitedColor)
            return null;

        // "Technically someone could explicitly specify the color transparent, but [...] assume that if the background color is
        // transparent that it wasn't set" (Blink)
        if (property == "background-color" && visitedColor.Alpha is null or <= 0)
            return unvisited;

        return visitedColor.WithAlpha(unvisitedColor.Alpha).Serialize();
    }

    /// <summary>
    /// Collects the colors of a resolved value (the components that are not keywords of the property, URLs or separators), or
    /// serializes the value with its colors replaced, in order.
    /// </summary>
    private static void CollectColors(List<CssComponentValue> values, List<string>? colors, StringBuilder? builder, List<string>? replacements)
    {
        var index = 0;
        Visit(values);

        void Visit(List<CssComponentValue> components)
        {
            foreach (var component in components)
            {
                switch (component)
                {
                    case CssPreservedToken { Type: CssTokenType.Hash }:
                    case CssPreservedToken { Type: CssTokenType.Ident } ident when ident.Token.Value is not ("none" or "context-fill" or "context-stroke" or "repeat" or "auto"):
                    case CssFunction function when !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "url") && !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"):
                        colors?.Add(CssSerializer.SerializeValue([component], property: null));
                        builder?.Append(replacements![index]);
                        index++;
                        break;

                    case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"):
                    {
                        // repeat(<count>, <colors>): the count (an integer or auto) is kept
                        var comma = function.Value.FindIndex(static argument => argument is CssPreservedToken { Type: CssTokenType.Comma });
                        builder?.Append("repeat(").Append(CssSerializer.SerializeValue(function.Value.GetRange(0, comma + 1), property: null)).Append(comma >= 0 ? " " : "");
                        Visit(function.Value.GetRange(comma + 1, function.Value.Count - comma - 1));
                        builder?.Append(')');
                        break;
                    }

                    default:
                        builder?.Append(CssSerializer.SerializeValue([component], property: null));
                        break;
                }
            }
        }
    }
}
