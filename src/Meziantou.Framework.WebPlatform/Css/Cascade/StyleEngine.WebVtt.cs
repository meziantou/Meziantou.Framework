using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebVtt;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The cascade of the WebVTT objects of the cues rendered over a video element (https://w3c.github.io/webvtt/#applying-css-properties,
// https://w3c.github.io/webvtt/#css-extensions): the properties the specification sets at the user agent level, the
// ::cue, ::cue() and ::cue-region rules of the style sheets that apply to the video element (the originating element),
// and the rules of the STYLE blocks of the WebVTT file of the cue, which come after them in the order of appearance.
internal sealed partial class StyleEngine
{
    /// <summary>The offsets of the layers and of the order of the rules of the STYLE blocks of a WebVTT file: after the ones of the document.</summary>
    private const int WebVttLayerOffset = 1 << 24;
    private const int WebVttOrderOffset = 1 << 30;

    /// <summary>The properties a rule sets on a WebVTT object: the properties that do not apply to it are ignored.</summary>
    private enum WebVttPropertyFilter
    {
        /// <summary>
        /// The properties of <c>::cue</c> on the root list of WebVTT node objects. The background is computed on the root (the
        /// <c>inherit</c> keyword is the background of the video element), but it is applied to the WebVTT cue background box,
        /// which inherits it: the root does not paint it.
        /// </summary>
        Cue,

        /// <summary>The properties of the background shorthand, on the WebVTT cue background box.</summary>
        Background,

        /// <summary>
        /// The properties of <c>::cue()</c> on a WebVTT internal node object (or on the root of the cue, whose background is
        /// applied to the WebVTT cue background box too), and of <c>::cue-region</c> on a region.
        /// </summary>
        Node,

        /// <summary>The properties of <c>::cue()</c> whose selector contains the :past or :future pseudo-class.</summary>
        TimedNode,
    }

    private static readonly ConcurrentDictionary<string, List<CssDeclarationEntry>> WebVttUserAgentDeclarations = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<List<CssDeclarationEntry>, List<CssDeclarationEntry>?[]> WebVttFilteredDeclarations = [];
    private readonly ConditionalWeakTable<TextTrack, WebVttTrackRules> _webVttTrackRules = [];

    /// <summary>The rules of the STYLE blocks of the WebVTT file of a text track, for a generation of the styles.</summary>
    private sealed class WebVttTrackRules
    {
        public required CascadeRuleSet Rules { get; init; }

        public required int StyleVersion { get; init; }

        public required MediaEnvironment Environment { get; init; }

        public required int SheetCount { get; init; }

        /// <summary>
        /// The element of the hypothetical document the style sheets apply to (https://w3c.github.io/webvtt/#obtaining-css-boxes):
        /// "a single empty element with no explicit name, no namespace, no attributes, no classes, no IDs, and unknown primary
        /// language", the originating element of the pseudo-elements of the rules.
        /// </summary>
        public required Element HypotheticalElement { get; init; }
    }

    private Dictionary<string, CascadedValue> CascadeWebVtt(WebVttElement element, string? pseudoElement, out bool shared)
    {
        var isBackgroundBox = pseudoElement == WebVttElement.BackgroundBoxPseudoElement;
        var blocks = new List<MatchedBlock>();
        if (pseudoElement is null || isBackgroundBox)
        {
            // The properties of https://w3c.github.io/webvtt/#applying-css-properties
            var userAgentStyle = isBackgroundBox ? WebVttElement.BackgroundBoxUserAgentStyle : element.UserAgentStyle;
            if (userAgentStyle.Length > 0)
            {
                blocks.Add(new MatchedBlock(WebVttUserAgentDeclarations.GetOrAdd(userAgentStyle, static text => ParseDeclarations(text)), Important: false, CascadeOrigin.UserAgent, 0, Attached: false, CascadeRule.Unlayered, default, NoProximity, 0, Source: null));
            }

            // The style sheets that apply to the video element: the pseudo-elements of the video element
            var host = element.Host;
            var root = host.GetRoot();
            var pseudoElementName = element.Kind == WebVttElementKind.Region ? "cue-region" : "cue";
            if (root is Dom.Document or ShadowRoot)
            {
                var tree = GetTreeStyles(root);
                if (tree.Rules.MayHaveRulesFor(pseudoElementName))
                {
                    var context = new SelectorContext(root, (root as ShadowRoot)?.Host);
                    var depth = GetContext(tree.Depth);
                    foreach (var rules in tree.Rules.GetCandidates(host))
                    {
                        foreach (var rule in rules)
                        {
                            if (rule.PseudoElement != pseudoElementName || (rule.StartingStyle && !_includeStartingStyle) || GetWebVttFilter(rule, element, isBackgroundBox) is not { } filter)
                                continue;

                            var proximity = NoProximity;
                            if (rule.Scope is { } scope)
                            {
                                if (MatchScoped(rule, scope, host, context) is not { } scopeProximity)
                                    continue;

                                proximity = -scopeProximity;
                            }
                            else if (!SelectorMatcher.MatchesOriginatingElement(rule.Selector, host, context))
                            {
                                continue;
                            }

                            if (MatchesContainerQueries(rule, host, pseudoElementName))
                            {
                                AddWebVttBlocks(blocks, rule, filter, depth, proximity, layerOffset: 0, orderOffset: 0);
                            }
                        }
                    }
                }
            }

            // The rules of the STYLE blocks of the WebVTT file: they apply to a hypothetical document that contains a single
            // empty element without name, which is the originating element of the pseudo-elements and matches no selector
            if (element.Track is { StyleSheets.Count: > 0 } track)
            {
                var trackRules = GetWebVttTrackRules(track, out var hypotheticalElement);
                if (trackRules.MayHaveRulesFor(pseudoElementName))
                {
                    var context = new SelectorContext(hypotheticalElement);
                    foreach (var rules in trackRules.GetCandidates(hypotheticalElement))
                    {
                        foreach (var rule in rules)
                        {
                            if (rule.PseudoElement != pseudoElementName || rule.Scope is not null || rule.Containers is not null
                                || !SelectorMatcher.MatchesOriginatingElement(rule.Selector, hypotheticalElement, context)
                                || GetWebVttFilter(rule, element, isBackgroundBox) is not { } filter)
                            {
                                continue;
                            }

                            AddWebVttBlocks(blocks, rule, filter, depth: 0, NoProximity, WebVttLayerOffset, WebVttOrderOffset);
                        }
                    }
                }
            }
        }

        blocks.Sort(MatchedBlock.Compare);
        var cascaded = ComputeCascadedValues(blocks);
        shared = ReferenceEquals(cascaded, EmptyCascadedValues);
        return cascaded;
    }

    /// <summary>
    /// Returns the properties a <c>::cue</c>, <c>::cue()</c> or <c>::cue-region</c> rule sets on a WebVTT object, or null when
    /// the pseudo-element of the rule does not match the object (https://w3c.github.io/webvtt/#the-cue-pseudo-element).
    /// </summary>
    private static WebVttPropertyFilter? GetWebVttFilter(CascadeRule rule, WebVttElement element, bool isBackgroundBox)
    {
        PseudoElementSelector? pseudoElement = null;
        foreach (var simple in rule.Selector.Compounds[^1].Selectors)
        {
            if (simple is PseudoElementSelector selector)
            {
                pseudoElement = selector;
            }
        }

        if (pseudoElement is null)
            return null;

        if (pseudoElement.Argument is not { } argument)
        {
            // ::cue matches the lists of WebVTT node objects, ::cue-region the regions
            return element.Kind switch
            {
                WebVttElementKind.CueRoot => isBackgroundBox ? WebVttPropertyFilter.Background : WebVttPropertyFilter.Cue,
                WebVttElementKind.Region => WebVttPropertyFilter.Node,
                _ => null,
            };
        }

        // ::cue(selector) matches the WebVTT internal node objects that match the selector in the tree of WebVTT node objects
        // of the cue; ::cue-region(selector) the regions whose identifier matches the selector
        if (!SelectorMatcher.Matches(argument, element, new SelectorContext(element.GetRoot())))
            return null;

        var isTimed = ContainsTimePseudoClass(argument);
        if (isBackgroundBox)
            return WebVttPropertyFilter.Background;

        return isTimed ? WebVttPropertyFilter.TimedNode : WebVttPropertyFilter.Node;
    }

    /// <summary>Whether a selector contains the :past or :future pseudo-class.</summary>
    private static bool ContainsTimePseudoClass(SelectorList list)
    {
        foreach (var selector in list.Selectors)
        {
            foreach (var compound in selector.Compounds)
            {
                foreach (var simple in compound.Selectors)
                {
                    if (simple is PseudoClassSelector pseudoClass && (pseudoClass.Kind is PseudoClass.Past or PseudoClass.Future || (pseudoClass.Argument is { } argument && ContainsTimePseudoClass(argument))))
                        return true;
                }
            }
        }

        return false;
    }

    private static void AddWebVttBlocks(List<MatchedBlock> blocks, CascadeRule rule, WebVttPropertyFilter filter, int depth, int proximity, int layerOffset, int orderOffset)
    {
        var declarations = GetWebVttDeclarations(rule.Declarations, filter);
        if (declarations.Count == 0)
            return;

        var layer = rule.Layer is CascadeRule.Unlayered or CascadeRule.PresentationalHints ? rule.Layer : rule.Layer + layerOffset;
        if (rule.HasNormal)
        {
            blocks.Add(new MatchedBlock(declarations, Important: false, rule.Origin, depth, Attached: false, layer, rule.Specificity, proximity, rule.Order + orderOffset, rule.Source));
        }

        if (rule.HasImportant)
        {
            blocks.Add(new MatchedBlock(declarations, Important: true, rule.Origin, depth, Attached: false, layer, rule.Specificity, proximity, rule.Order + orderOffset, rule.Source));
        }
    }

    /// <summary>
    /// Returns the declarations of a rule that apply to a WebVTT object (the other properties are ignored). The declarations
    /// of the background of the WebVTT cue background box inherit the values computed on the root of the cue.
    /// </summary>
    private static List<CssDeclarationEntry> GetWebVttDeclarations(List<CssDeclarationEntry> declarations, WebVttPropertyFilter filter)
    {
        var filtered = WebVttFilteredDeclarations.GetValue(declarations, static _ => new List<CssDeclarationEntry>?[4]);
        lock (filtered)
        {
            return filtered[(int)filter] ??= filter == WebVttPropertyFilter.Background
                ? declarations.FindAll(declaration => IsWebVttProperty(declaration.Name, filter)).ConvertAll(static declaration => new CssDeclarationEntry(new CssLonghandValue(declaration.Name, "inherit"), declaration.Important))
                : declarations.FindAll(declaration => IsWebVttProperty(declaration.Name, filter));
        }
    }

    private static bool IsWebVttProperty(string name, WebVttPropertyFilter filter)
    {
        // The properties of the background shorthand
        var isBackground = name.StartsWith("background-", StringComparison.Ordinal) && name != "background-blend-mode";
        if (filter == WebVttPropertyFilter.Background)
            return isBackground;

        // The custom properties are kept: the values of the properties that apply may reference them
        if (name is "color" or "opacity" or "visibility" or "text-shadow" || name.StartsWith("--", StringComparison.Ordinal)
            || name.StartsWith("text-decoration-", StringComparison.Ordinal)
            || (name.StartsWith("outline-", StringComparison.Ordinal) && name != "outline-offset"))
        {
            return true;
        }

        if (isBackground)
            return true;

        // The properties relating to the transition and animation features apply to ::cue()
        if (name.StartsWith("transition-", StringComparison.Ordinal) || name.StartsWith("animation-", StringComparison.Ordinal))
            return filter != WebVttPropertyFilter.Cue;

        // The properties of the font shorthand (including line-height), white-space, text-combine-upright and ruby-position
        // do not apply to ::cue() when its selector contains :past or :future
        if (name.StartsWith("font-", StringComparison.Ordinal) || name is "line-height" or "white-space" or "white-space-collapse" or "text-wrap-mode" or "white-space-trim" or "text-combine-upright" or "ruby-position")
            return filter != WebVttPropertyFilter.TimedNode;

        return false;
    }

    /// <summary>
    /// Returns the rules of the STYLE blocks of the WebVTT file of a text track (https://w3c.github.io/webvtt/#obtaining-css-boxes):
    /// the relative order of appearance of the style sheets is the order in which they were added to the collection.
    /// </summary>
    private CascadeRuleSet GetWebVttTrackRules(TextTrack track, out Element hypotheticalElement)
    {
        if (_webVttTrackRules.TryGetValue(track, out var cached) && cached.StyleVersion == _styleVersion && cached.Environment == _environment && cached.SheetCount == track.StyleSheets.Count)
        {
            hypotheticalElement = cached.HypotheticalElement;
            return cached.Rules;
        }

        // The URLs of the STYLE blocks whose scheme is not "data" fail to resolve: the base URL is not a valid base
        var collector = new CascadeRuleCollector(_environment!, CascadeOrigin.Author, treeRoot: null);
        foreach (var sheet in track.StyleSheets)
        {
            collector.AddStyleSheet(sheet, WebVttStyleSheetBaseUrl);
        }

        var rules = collector.Complete();
        hypotheticalElement = cached?.HypotheticalElement ?? new Element(_document, localName: "", @namespace: null, prefix: null);
        _webVttTrackRules.AddOrUpdate(track, new WebVttTrackRules { Rules = rules, StyleVersion = _styleVersion, Environment = _environment!, SheetCount = track.StyleSheets.Count, HypotheticalElement = hypotheticalElement });
        return rules;
    }

    /// <summary>The base URL of the style sheets of the STYLE blocks of the WebVTT files.</summary>
    internal const string WebVttStyleSheetBaseUrl = "about:invalid";
}
