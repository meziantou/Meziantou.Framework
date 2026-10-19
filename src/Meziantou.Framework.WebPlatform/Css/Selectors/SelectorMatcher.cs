using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>The context of the matching of a selector.</summary>
/// <param name="ScopingRoot">The scoping root (https://drafts.csswg.org/selectors-4/#scoping-root): the element :scope and &amp; represent, or a document, fragment or shadow root.</param>
/// <param name="ShadowHost">
/// The shadow host of the shadow tree of the style sheet, when the selector comes from the style sheet of a shadow tree
/// (https://drafts.csswg.org/css-scoping/#host-element-in-tree): the host is then the parent of the shadow root for the
/// combinators, and it is featureless: it only matches :host, :host() and :host-context().
/// </param>
internal readonly record struct SelectorContext(Node? ScopingRoot, Element? ShadowHost = null)
{
    public static implicit operator SelectorContext(Node? scopingRoot) => new(scopingRoot);

    /// <summary>
    /// The element referenced by a use element when the instance of one of its inclusive descendants in the use-element shadow
    /// tree is matched (https://svgwg.org/svg2-draft/struct.html#UseShadowTree), or null: it is the root of the shadow tree, without
    /// parent nor sibling.
    /// </summary>
    public Element? UseInstanceRoot { get; init; }

    /// <summary>Whether an element is the root of the use-element shadow tree being matched.</summary>
    public bool IsUseInstanceRoot(Node node) => UseInstanceRoot is { } root && ReferenceEquals(node, root);

    /// <summary>
    /// Whether the argument of a :has() is being matched: a :has() nested in it (through the nesting selector, the parser
    /// rejects the others) never matches (https://drafts.csswg.org/selectors-4/#relational).
    /// </summary>
    public bool InRelationalPseudoClass { get; init; }

    /// <summary>
    /// The scroll navigation pseudo-element (see <see cref="Layout.ScrollControls"/>) whose style is being computed, or null: the
    /// pseudo-classes written after the pseudo-element in a selector are matched against its state, not against the state of
    /// its originating element.
    /// </summary>
    public string? PseudoElement { get; init; }

    /// <summary>Whether the simple selectors being matched follow a scroll navigation pseudo-element: they match the state of <see cref="PseudoElement"/>.</summary>
    public bool MatchesPseudoElementState { get; init; }

    /// <summary>
    /// The link that matches <c>:visited</c> instead of <c>:link</c> (https://drafts.csswg.org/selectors-4/#link), or null: every
    /// link matches <c>:link</c>, which is what the scripts observe (<c>matches()</c>, <c>querySelector()</c>,
    /// <c>getComputedStyle()</c>...). It is only set by the cascade of the colors of the elements in a visited link (see
    /// <see cref="Cascade.ComputedStyle.VisitedLink"/>): the nearest link among the inclusive ancestors of the subject. As in the
    /// browsers, the links that are not an inclusive ancestor of the subject (the ones matched after a sibling combinator or in
    /// <c>:has()</c>) are unvisited, so that the style of an element does not depend on the history of another link.
    /// </summary>
    public Element? VisitedLink { get; init; }

    /// <summary>
    /// Returns the context of the DOM APIs (matches(), querySelector()...): in a shadow tree, the shadow host is in the tree
    /// (https://drafts.csswg.org/css-shadow-1/#host-element-in-tree), so that :host matches it.
    /// </summary>
    public static SelectorContext ForDomApi(Node scopingRoot) => new(scopingRoot, (scopingRoot.GetRoot() as ShadowRoot)?.Host);
}

/// <summary>Matches selectors against elements (https://drafts.csswg.org/selectors-4/#match-against-element).</summary>
internal static class SelectorMatcher
{
    /// <summary>The attributes whose values are matched ASCII case-insensitively on HTML elements in HTML documents (https://html.spec.whatwg.org/multipage/semantics-other.html#case-sensitivity-of-selectors).</summary>
    internal static readonly FrozenSet<string> CaseInsensitiveAttributes = new[]
    {
        "accept", "accept-charset", "align", "alink", "axis", "bgcolor", "charset", "checked", "clear", "codetype", "color", "compact",
        "declare", "defer", "dir", "direction", "disabled", "enctype", "face", "frame", "hreflang", "http-equiv", "lang", "language",
        "link", "media", "method", "multiple", "nohref", "noresize", "noshade", "nowrap", "readonly", "rel", "rev", "rules", "scope",
        "scrolling", "selected", "shape", "target", "text", "type", "valign", "valuetype", "vlink",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>https://drafts.csswg.org/selectors-4/#match-a-selector-against-an-element</summary>
    public static bool Matches(SelectorList selectors, Element element, in SelectorContext context)
    {
        foreach (var selector in selectors.Selectors)
        {
            if (MatchesComplex(selector, element, context))
                return true;
        }

        return false;
    }

    public static bool MatchesComplex(ComplexSelector selector, Element element, in SelectorContext context)
    {
        if (selector.HasPseudoElement)
            return false;

        return MatchesFrom(selector, selector.Compounds.Length - 1, element, context, anchor: null);
    }

    /// <summary>
    /// Matches a selector ending with a pseudo-element against its originating element
    /// (https://drafts.csswg.org/selectors-4/#originating-element): the pseudo-elements of the last compound selector are ignored.
    /// </summary>
    public static bool MatchesOriginatingElement(ComplexSelector selector, Element element, in SelectorContext context)
    {
        var last = selector.Compounds.Length - 1;
        if (!MatchesCompound(selector.Compounds[last], element, context, ignorePseudoElements: true))
            return false;

        return last == 0 || MatchCombinator(selector, last, element, context, anchor: null) == MatchResult.Matches;
    }

    /// <summary>
    /// Matches the pseudo-classes that follow the ::part() pseudo-element of a selector (e.g. :hover in "::part(label):hover")
    /// against the part, an element of a shadow tree (https://drafts.csswg.org/css-shadow-1/#part). The originating element
    /// (the shadow host) is matched by <see cref="MatchesOriginatingElement"/>.
    /// </summary>
    public static bool MatchesPart(ComplexSelector selector, Element part)
    {
        var context = new SelectorContext(part.GetRoot());
        var afterPart = false;
        foreach (var simple in selector.Compounds[^1].Selectors)
        {
            // The pseudo-classes that follow a pseudo-element of the part (e.g. :hover in ::part(label)::before:hover) are
            // matched against the part, like the ones that follow a pseudo-element are matched against its originating element
            if (simple is PseudoElementSelector pseudoElement)
            {
                afterPart |= pseudoElement.Name == "part";
                continue;
            }

            if (afterPart && !MatchesSimple(simple, part, context))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Matches the compound selector at <paramref name="index"/> and the ones on its left against <paramref name="node"/>.
    /// For relative selectors (:has()), <paramref name="anchor"/> is the element the leading combinator is relative to.
    /// </summary>
    private static bool MatchesFrom(ComplexSelector selector, int index, Node node, in SelectorContext context, Element? anchor)
        => MatchFrom(selector, index, node, context, anchor) == MatchResult.Matches;

    /// <summary>
    /// The result of matching the compound selectors on the left of a combinator. A failure tells which other candidates
    /// cannot match either, so that the descendant and subsequent-sibling combinators do not try them (as in Blink): without
    /// it, a selector with several descendant combinators (e.g. "div div div span") backtracks exponentially.
    /// </summary>
    private enum MatchResult : byte
    {
        Matches,

        /// <summary>The selector does not match the element, but may match another candidate.</summary>
        FailsLocally,

        /// <summary>The selector does not match the element nor its previous siblings (the next candidates of a subsequent-sibling combinator).</summary>
        FailsAllSiblings,

        /// <summary>The selector does not match the element nor its ancestors (the next candidates of a descendant combinator), nor their siblings.</summary>
        FailsCompletely,
    }

    private static MatchResult MatchFrom(ComplexSelector selector, int index, Node node, in SelectorContext context, Element? anchor)
    {
        if (!MatchesCompound(selector.Compounds[index], node, context))
            return MatchResult.FailsLocally;

        if (index == 0)
        {
            if (anchor is null)
                return MatchResult.Matches;

            // The leading combinator of a relative selector is relative to the anchor element
            return IsRelated(selector.RelativeCombinator ?? Combinator.Descendant, node, anchor, context) ? MatchResult.Matches : MatchResult.FailsLocally;
        }

        return MatchCombinator(selector, index, node, context, anchor);
    }

    /// <summary>Returns the parent of a node for the combinators: the shadow host is the parent of its shadow root in the context of the shadow tree.</summary>
    private static Element? GetParentElement(Node node, in SelectorContext context)
    {
        // The shadow host is the root of the shadow tree: its ancestors are not in the tree
        if (context.ShadowHost is { } shadowHost && ReferenceEquals(node, shadowHost))
            return null;

        if (context.IsUseInstanceRoot(node))
            return null;

        var parent = node.ParentNode;
        if (parent is Element element)
            return element;

        return parent is ShadowRoot shadowRoot && context.ShadowHost is { } host && ReferenceEquals(shadowRoot.Host, host) ? host : null;
    }

    /// <summary>Matches the combinator on the left of the compound selector at <paramref name="index"/> and the compound selectors on its left.</summary>
    private static MatchResult MatchCombinator(ComplexSelector selector, int index, Node node, in SelectorContext context, Element? anchor)
    {
        var combinator = selector.Combinators[index - 1];
        switch (combinator)
        {
            case Combinator.Child:
            {
                var parent = GetParentElement(node, context);
                return parent is null ? MatchResult.FailsCompletely : MatchFrom(selector, index - 1, parent, context, anchor);
            }

            case Combinator.Descendant:
            {
                // When the selector fails completely for an ancestor, it fails for the next ones too: their ancestors were tried
                for (var ancestor = GetParentElement(node, context); ancestor is not null; ancestor = GetParentElement(ancestor, context))
                {
                    var result = MatchFrom(selector, index - 1, ancestor, context, anchor);
                    if (result is MatchResult.Matches or MatchResult.FailsCompletely)
                        return result;
                }

                return MatchResult.FailsCompletely;
            }

            // The shadow host has no siblings in its shadow tree
            case Combinator.NextSibling or Combinator.SubsequentSibling when context.ShadowHost is { } shadowHost && ReferenceEquals(node, shadowHost):
                return MatchResult.FailsAllSiblings;

            // The root of a use-element shadow tree is the only child of its shadow root
            case Combinator.NextSibling or Combinator.SubsequentSibling when context.IsUseInstanceRoot(node):
                return MatchResult.FailsAllSiblings;

            case Combinator.NextSibling:
            {
                // (the links on the left of a sibling combinator are unvisited: see SelectorContext.VisitedLink)
                var sibling = PreviousElementSibling(node);
                if (sibling is null)
                    return MatchResult.FailsAllSiblings;

                if (context.VisitedLink is null)
                    return MatchFrom(selector, index - 1, sibling, context, anchor);

                var unvisitedContext = context with { VisitedLink = null };
                return MatchFrom(selector, index - 1, sibling, unvisitedContext, anchor);
            }

            case Combinator.SubsequentSibling:
            {
                if (context.VisitedLink is null)
                    return MatchSubsequentSiblings(selector, index, node, context, anchor);

                var unvisitedContext = context with { VisitedLink = null };
                return MatchSubsequentSiblings(selector, index, node, unvisitedContext, anchor);
            }
        }

        return MatchResult.FailsLocally;
    }

    private static MatchResult MatchSubsequentSiblings(ComplexSelector selector, int index, Node node, in SelectorContext context, Element? anchor)
    {
        // When the selector fails for all the siblings of a sibling, it fails for the next ones too: their previous siblings were tried
        for (var sibling = PreviousElementSibling(node); sibling is not null; sibling = PreviousElementSibling(sibling))
        {
            var result = MatchFrom(selector, index - 1, sibling, context, anchor);
            if (result != MatchResult.FailsLocally)
                return result;
        }

        return MatchResult.FailsAllSiblings;
    }

    private static Element? PreviousElementSibling(Node node)
    {
        for (var sibling = node.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
        {
            if (sibling is Element element)
                return element;
        }

        return null;
    }

    private static bool IsRelated(Combinator combinator, Node node, Element anchor, in SelectorContext context) => combinator switch
    {
        Combinator.Child => ReferenceEquals(GetParentElement(node, context), anchor),
        Combinator.Descendant => IsDescendant(node, anchor, context),
        Combinator.NextSibling => ReferenceEquals(PreviousElementSibling(node), anchor),
        _ => IsFollowingSibling(node, anchor),
    };

    private static bool IsDescendant(Node node, Element anchor, in SelectorContext context)
    {
        for (var ancestor = GetParentElement(node, context); ancestor is not null; ancestor = GetParentElement(ancestor, context))
        {
            if (ReferenceEquals(ancestor, anchor))
                return true;
        }

        return false;
    }

    private static bool IsFollowingSibling(Node node, Element anchor)
    {
        for (var sibling = node.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
        {
            if (ReferenceEquals(sibling, anchor))
                return true;
        }

        return false;
    }

    private static bool MatchesCompound(CompoundSelector compound, Node node, in SelectorContext context, bool ignorePseudoElements = false)
    {
        if (node is not Element element)
            return false;

        // The shadow host is featureless in the context of its shadow tree (https://drafts.csswg.org/css-scoping/#host-element-in-tree)
        var featureless = context.ShadowHost is not null && ReferenceEquals(element, context.ShadowHost);
        var stateContext = default(SelectorContext);
        scoped ref readonly var currentContext = ref context;
        foreach (var selector in compound.Selectors)
        {
            if (ignorePseudoElements && selector is PseudoElementSelector pseudoElement)
            {
                // The pseudo-classes that follow ::part() apply to the part (see MatchesPart); the :only-child that follows a view
                // transition pseudo-element applies to the pseudo-element in its tree (see StyleEngine.MatchesPseudoElement)
                if (pseudoElement.Name == "part" || pseudoElement.Name.StartsWith("view-transition", StringComparison.Ordinal))
                    break;

                // The pseudo-classes that follow a scroll navigation pseudo-element match its own state
                if (pseudoElement.Name is Layout.ScrollControls.Marker or Layout.ScrollControls.MarkerGroup or Layout.ScrollControls.ButtonRuleName)
                {
                    stateContext = currentContext with { MatchesPseudoElementState = true };
                    currentContext = ref stateContext;
                }

                continue;
            }

            if (featureless ? !MatchesFeaturelessHost(selector, element, currentContext) : !MatchesSimple(selector, element, currentContext))
                return false;
        }

        // :has() alone does not make a compound selector match the featureless host (:host:has(.a) does)
        if (featureless && !HasFeaturelessHostFeature(compound))
            return false;

        return true;
    }

    private static bool HasFeaturelessHostFeature(CompoundSelector compound)
    {
        foreach (var selector in compound.Selectors)
        {
            if (selector is not (PseudoClassSelector { Kind: PseudoClass.Has } or PseudoElementSelector or TypeSelector { Implicit: true }))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Matches a simple selector against the featureless shadow host: only :host, :host() and :host-context() (and the
    /// logical pseudo-classes containing them), and :scope and &amp; when the host is the scoping root, match it.
    /// </summary>
    private static bool MatchesFeaturelessHost(SimpleSelector selector, Element host, in SelectorContext context)
    {
        // The host also matches :scope and & when it is the scoping root (https://drafts.csswg.org/css-cascade-6/#scoped-rules)
        if (selector is NestingSelector nesting)
            return nesting.Parent is { } parent ? Matches(parent, host, context) : ReferenceEquals(context.ScopingRoot, host);

        // The universal selector implied by the default namespace does not apply to the featureless host
        if (selector is TypeSelector { Implicit: true })
            return true;

        if (selector is not PseudoClassSelector pseudoClass)
            return false;

        switch (pseudoClass.Kind)
        {
            case PseudoClass.Host:
                return true;

            case PseudoClass.Scope:
                return ReferenceEquals(context.ScopingRoot, host);

            case PseudoClass.HostFunction:
                // The argument is matched against the host in the context of its own tree
                return Matches(pseudoClass.Argument!, host, new SelectorContext(context.ScopingRoot) { VisitedLink = context.VisitedLink });

            case PseudoClass.HostContext:
                for (Node? node = host; node is not null; node = node is ShadowRoot shadowRoot ? shadowRoot.Host : node.ParentNode)
                {
                    if (node is Element ancestor && Matches(pseudoClass.Argument!, ancestor, new SelectorContext(context.ScopingRoot)))
                        return true;
                }

                return false;

            case PseudoClass.Is or PseudoClass.Where:
                foreach (var complex in pseudoClass.Argument!.Selectors)
                {
                    if (complex.Compounds.Length == 1 && MatchesCompound(complex.Compounds[0], host, context))
                        return true;
                }

                return false;

            case PseudoClass.Not:
            {
                // A negation only matches the featureless host when one of its arguments is able to match it (:not(:not(:host))
                // matches; :not(div) and :host:not(:hover) do not: https://drafts.csswg.org/selectors-4/#featureless)
                var canMatch = false;
                foreach (var complex in pseudoClass.Argument!.Selectors)
                {
                    if (complex.Compounds.Length != 1 || !CanMatchFeaturelessHost(complex.Compounds[0]))
                        continue;

                    if (MatchesCompound(complex.Compounds[0], host, context))
                        return false;

                    canMatch = true;
                }

                return canMatch;
            }

            case PseudoClass.Has:
                // The descendants of the host are the elements of its shadow tree
                return MatchesHas(pseudoClass.Argument!, host, context);

            default:
                return false;
        }
    }

    /// <summary>Returns whether every simple selector of a compound selector is able to match a featureless shadow host.</summary>
    private static bool CanMatchFeaturelessHost(CompoundSelector compound)
    {
        foreach (var selector in compound.Selectors)
        {
            switch (selector)
            {
                case NestingSelector or PseudoElementSelector or TypeSelector { Implicit: true }:
                case PseudoClassSelector { Kind: PseudoClass.Host or PseudoClass.HostFunction or PseudoClass.HostContext or PseudoClass.Scope or PseudoClass.Has }:
                    break;

                case PseudoClassSelector { Kind: PseudoClass.Is or PseudoClass.Where or PseudoClass.Not, Argument: { } argument }:
                    if (!argument.Selectors.Any(static complex => complex.Compounds.Length == 1 && CanMatchFeaturelessHost(complex.Compounds[0])))
                        return false;

                    break;

                default:
                    return false;
            }
        }

        return true;
    }

    private static bool IsHtmlElementInHtmlDocument(Element element) => element.IsHtmlNamespace && element.NodeDocument.IsHtmlDocument;

    private static bool MatchesSimple(SimpleSelector selector, Element element, in SelectorContext context)
    {
        switch (selector)
        {
            case TypeSelector type:
                if (!type.Namespace.Matches(element.NamespaceUri))
                    return false;

                if (type.LocalName is null)
                    return true;

                return IsHtmlElementInHtmlDocument(element) ? type.LowercaseLocalName == element.LocalName : type.LocalName == element.LocalName;

            case NestingSelector nesting:
                // Outside of a style rule, & represents the scoping root (https://drafts.csswg.org/css-nesting/#nest-selector)
                if (nesting.Parent is { } parent)
                    return Matches(parent, element, context);

                return context.ScopingRoot is Element scopingElement ? ReferenceEquals(element, scopingElement) : element.ParentNode is Document;

            case IdSelector id:
                if (element.IdValue is not { } elementId)
                    return false;

                return element.NodeDocument.Mode == DocumentMode.Quirks ? InfraAscii.EqualsIgnoreAsciiCase(elementId, id.Id) : elementId == id.Id;

            case ClassSelector classSelector:
            {
                var quirks = element.NodeDocument.Mode == DocumentMode.Quirks;
                foreach (var className in element.GetStyleClassNames())
                {
                    if (quirks ? InfraAscii.EqualsIgnoreAsciiCase(className, classSelector.ClassName) : className == classSelector.ClassName)
                        return true;
                }

                return false;
            }

            case ClassPrefixSelector classPrefix:
            {
                // The prefix is matched case-sensitively; in quirks mode, the classes are ASCII lowercased first
                var quirks = element.NodeDocument.Mode == DocumentMode.Quirks;
                var prefix = classPrefix.Prefix;
                foreach (var className in element.GetStyleClassNames())
                {
                    if (className.Length > prefix.Length && className[prefix.Length] != '-' && (quirks ? InfraAscii.ToAsciiLowercase(className) : className).StartsWith(prefix, StringComparison.Ordinal))
                        return true;
                }

                return false;
            }

            case AttributeSelector attribute:
                return MatchesAttribute(attribute, element);

            case PseudoClassSelector pseudoClass:
                return MatchesPseudoClass(pseudoClass, element, context);

            default:
                // Pseudo-elements never match elements
                return false;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Attribute selectors (https://drafts.csswg.org/selectors-4/#attribute-selectors)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Matches a compound selector made only of type, ID, class and attribute selectors (see <see cref="SelectorHints.IsSimpleCompound"/>)
    /// against an element of a document that is not in quirks mode and whose classes are not animated: the IDs and the classes
    /// are compared case-sensitively.
    /// </summary>
    internal static bool MatchesSimpleCompound(CompoundSelector compound, Element element, bool isHtmlDocument)
    {
        var isHtml = isHtmlDocument && element.IsHtmlNamespace;
        foreach (var simple in compound.Selectors)
        {
            var matches = simple switch
            {
                TypeSelector type => type.Namespace.Matches(element.NamespaceUri) && (type.LocalName is null || (isHtml ? type.LowercaseLocalName : type.LocalName) == element.LocalName),
                IdSelector id => element.IdValue == id.Id,
                ClassSelector classSelector => element.GetClassNames().Contains(classSelector.ClassName),
                AttributeSelector attribute => MatchesAttribute(attribute, element, isHtml),
                _ => false,
            };

            if (!matches)
                return false;
        }

        return true;
    }

    private static bool MatchesAttribute(AttributeSelector selector, Element element) => MatchesAttribute(selector, element, IsHtmlElementInHtmlDocument(element));

    private static bool MatchesAttribute(AttributeSelector selector, Element element, bool isHtml)
    {
        var name = isHtml ? selector.LowercaseName : selector.Name;
        for (var i = 0; i < element.AttributeCount; i++)
        {
            // The name is matched ASCII case-insensitively on the HTML elements of an HTML document, whatever the namespace
            // of the attribute (https://html.spec.whatwg.org/multipage/semantics-other.html#case-sensitivity-of-selectors)
            if (name != element.GetAttributeLocalName(i))
                continue;

            var ns = element.GetAttributeNamespace(i);
            if (!selector.Namespace.Matches(ns))
                continue;

            if (selector.Operator == AttributeOperator.Exists)
                return true;

            var ignoreCase = selector.CaseSensitivity switch
            {
                AttributeCaseSensitivity.Insensitive => true,
                AttributeCaseSensitivity.Sensitive => false,
                _ => isHtml && ns is null && selector.IsDefaultCaseInsensitiveHtml,
            };

            if (MatchesAttributeValue(selector, element.GetAttributeValue(i), ignoreCase))
                return true;
        }

        return false;
    }

    private static bool MatchesAttributeValue(AttributeSelector selector, string actual, bool ignoreCase)
    {
        var expected = selector.Value;
        switch (selector.Operator)
        {
            case AttributeOperator.Equals:
                return Equals(actual, expected, ignoreCase);

            case AttributeOperator.Includes:
                if (expected.Length == 0 || selector.ValueHasWhitespace)
                    return false;

                foreach (var range in actual.AsSpan().SplitAny(" \t\n\r\f"))
                {
                    if (Equals(actual.AsSpan(range), expected, ignoreCase))
                        return true;
                }

                return false;

            case AttributeOperator.DashMatch:
                return Equals(actual, expected, ignoreCase) || (actual.Length > expected.Length && actual[expected.Length] == '-' && Equals(actual.AsSpan(0, expected.Length), expected, ignoreCase));

            case AttributeOperator.Prefix:
                return expected.Length > 0 && actual.Length >= expected.Length && Equals(actual.AsSpan(0, expected.Length), expected, ignoreCase);

            case AttributeOperator.Suffix:
                return expected.Length > 0 && actual.Length >= expected.Length && Equals(actual.AsSpan(actual.Length - expected.Length), expected, ignoreCase);

            case AttributeOperator.Substring:
                if (expected.Length == 0)
                    return false;

                if (!ignoreCase)
                    return actual.Contains(expected, StringComparison.Ordinal);

                for (var i = 0; i + expected.Length <= actual.Length; i++)
                {
                    if (InfraAscii.EqualsIgnoreAsciiCase(actual.AsSpan(i, expected.Length), expected))
                        return true;
                }

                return false;
        }

        return false;
    }

    /// <summary>Ordinal or ASCII case-insensitive comparison (non-ASCII characters are compared exactly).</summary>
    private static bool Equals(ReadOnlySpan<char> actual, string expected, bool ignoreCase) => ignoreCase ? InfraAscii.EqualsIgnoreAsciiCase(actual, expected) : actual.SequenceEqual(expected);

    // ----------------------------------------------------------------------------------------------------------------
    // Pseudo-classes
    // ----------------------------------------------------------------------------------------------------------------

    private static bool MatchesPseudoClass(PseudoClassSelector selector, Element element, in SelectorContext context)
    {
        // https://drafts.csswg.org/css-overflow-5/#scroll-focus-pseudos: the pseudo-classes of a scroll navigation pseudo-element
        // (also the ones in the arguments of the logical pseudo-classes that follow it)
        if (context.MatchesPseudoElementState && selector.Kind is not (PseudoClass.Is or PseudoClass.Where or PseudoClass.Not))
            return context.PseudoElement is { } pseudoElement && Layout.ScrollControls.MatchesState(selector.Kind, element, pseudoElement);

        switch (selector.Kind)
        {
            case PseudoClass.Is or PseudoClass.Where:
                return Matches(selector.Argument!, element, context);

            case PseudoClass.Not:
                return !Matches(selector.Argument!, element, context);

            case PseudoClass.Has:
                return MatchesHas(selector.Argument!, element, context);

            case PseudoClass.Root:
                // https://w3c.github.io/webvtt/#the-cue-pseudo-element: the root of the tree of ::cue() is the list of WebVTT
                // node objects of the cue
                if (element is WebVtt.WebVttElement vttElement)
                    return vttElement.Kind == WebVtt.WebVttElementKind.CueRoot;

                return element.ParentNode is Document && !context.IsUseInstanceRoot(element);

            case PseudoClass.Empty:
                for (var child = element.FirstChild; child is not null; child = child.NextSibling)
                {
                    if (child is Element || (child is Text text && text.Length > 0))
                        return false;
                }

                return true;

            case PseudoClass.FirstChild:
                return context.IsUseInstanceRoot(element) || PreviousElementSibling(element) is null;

            case PseudoClass.LastChild:
                return context.IsUseInstanceRoot(element) || NextElementSibling(element) is null;

            case PseudoClass.OnlyChild:
                return context.IsUseInstanceRoot(element) || (PreviousElementSibling(element) is null && NextElementSibling(element) is null);

            case PseudoClass.FirstOfType:
                return CountSiblings(element, forward: false, sameType: true, filter: null, context) == 0;

            case PseudoClass.LastOfType:
                return CountSiblings(element, forward: true, sameType: true, filter: null, context) == 0;

            case PseudoClass.OnlyOfType:
                return CountSiblings(element, forward: false, sameType: true, filter: null, context) == 0 && CountSiblings(element, forward: true, sameType: true, filter: null, context) == 0;

            case PseudoClass.NthChild or PseudoClass.NthLastChild:
                // With "of S", only the elements matching S are counted, and the element must match S
                if (selector.Argument is not null && !Matches(selector.Argument, element, context))
                    return false;

                return selector.Nth.Matches(1 + CountSiblings(element, forward: selector.Kind == PseudoClass.NthLastChild, sameType: false, selector.Argument, context));

            case PseudoClass.NthOfType or PseudoClass.NthLastOfType:
                return selector.Nth.Matches(1 + CountSiblings(element, forward: selector.Kind == PseudoClass.NthLastOfType, sameType: true, filter: null, context));

            case PseudoClass.Scope:
                // When the scoping root is not an element (a document, a fragment or a shadow root), there is no scoping
                // element: :scope then represents the root of the tree, like :root (web-compatible behavior checked by WPT)
                return context.ScopingRoot is Element scopingElement ? ReferenceEquals(element, scopingElement) : element.ParentNode is Document;

            case PseudoClass.Host or PseudoClass.HostFunction or PseudoClass.HostContext:
                // Only the featureless shadow host matches them, in the context of its shadow tree (querying never matches it)
                return false;

            case PseudoClass.HasSlotted:
                return element is HtmlSlotElement slot && HasFlattenedAssignedNodes(slot);

            case PseudoClass.AnyLink:
                return HtmlElementStates.IsLink(element);

            case PseudoClass.Link:
                return HtmlElementStates.IsLink(element) && !ReferenceEquals(element, context.VisitedLink);

            case PseudoClass.Visited:
                return context.VisitedLink is { } visitedLink && ReferenceEquals(element, visitedLink);

            case PseudoClass.Target:
                // The target element of a text directive can be in a shadow tree
                return element.IsConnected && ReferenceEquals(HtmlElementStates.GetTargetElement(element.NodeDocument), element);

            case PseudoClass.TargetWithin:
                return element.IsConnected && HtmlElementStates.GetTargetElement(element.NodeDocument) is { } target && element.IsShadowIncludingInclusiveAncestorOf(target);

            case PseudoClass.TargetCurrent or PseudoClass.TargetBefore or PseudoClass.TargetAfter:
                // https://drafts.csswg.org/css-overflow-5/#active-scroll-marker: the scroll marker elements (the links of a
                // scroll marker group container); the state is the one of the last snapshot of the layout
                return element.IsConnected && element.NodeDocument.LayoutEngineOrNull is { } layoutEngine && layoutEngine.MatchesScrollMarkerState(selector.Kind, element, pseudoElement: null);

            case PseudoClass.Focus:
                return FocusModel.HasFocusForSelector(element);

            case PseudoClass.FocusVisible:
                return FocusModel.HasFocusVisible(element);

            case PseudoClass.FocusWithin:
                return FocusModel.HasFocusWithin(element);

            case PseudoClass.Hover:
                return UserActionStates.IsHovered(element);

            case PseudoClass.Active:
                return UserActionStates.IsActive(element);

            case PseudoClass.Lang:
            {
                var language = HtmlElementStates.GetLanguage(element);
                if (language is null)
                    return false;

                foreach (var range in selector.Strings)
                {
                    if (HtmlElementStates.MatchesLanguageRange(language, range))
                        return true;
                }

                return false;
            }

            case PseudoClass.Dir:
                return selector.Strings[0] switch
                {
                    "ltr" => HtmlElementStates.IsLeftToRight(element),
                    "rtl" => !HtmlElementStates.IsLeftToRight(element),
                    _ => false,
                };

            case PseudoClass.Defined:
                return element.CustomElementState is CustomElementState.Uncustomized or CustomElementState.Custom;

            case PseudoClass.ActiveViewTransition:
                // https://drafts.csswg.org/css-view-transitions-2/#the-active-view-transition-pseudo: the root element of a
                // document that has an active view transition, and the root element of a scoped view transition (as in
                // Chromium: the pseudo-elements of a scoped view transition are selected from it)
                return element.NodeDocument.GetActiveViewTransition(element) is not null;

            case PseudoClass.ActiveViewTransitionType:
            {
                // https://drafts.csswg.org/css-view-transitions-2/#the-active-view-transition-type-pseudo: one of the types is
                // an active type of the transition
                if (element.NodeDocument.GetActiveViewTransition(element) is not { } transition)
                    return false;

                foreach (var type in selector.Strings!)
                {
                    if (transition.Types.Contains(type))
                        return true;
                }

                return false;
            }

            case PseudoClass.State:
                // https://html.spec.whatwg.org/multipage/custom-elements.html#selector-custom
                return element is HtmlElement htmlElement && htmlElement.HasCustomState(selector.Strings![0]);

            case PseudoClass.Enabled:
                return HtmlElementStates.IsEnabledCandidate(element) && !HtmlElementStates.IsActuallyDisabled(element);

            case PseudoClass.Disabled:
                return HtmlElementStates.IsActuallyDisabled(element);

            case PseudoClass.ReadWrite:
                return element.IsHtmlNamespace && HtmlElementStates.IsReadWrite(element);

            case PseudoClass.ReadOnly:
                return element.IsHtmlNamespace && !HtmlElementStates.IsReadWrite(element);

            case PseudoClass.PlaceholderShown:
                return HtmlElementStates.IsPlaceholderShown(element);

            case PseudoClass.Default:
                return HtmlElementStates.IsDefault(element);

            case PseudoClass.Checked:
                return HtmlElementStates.IsChecked(element);

            case PseudoClass.Indeterminate:
                return HtmlElementStates.IsIndeterminate(element);

            case PseudoClass.Required:
                return HtmlElementStates.IsRequired(element);

            case PseudoClass.Optional:
                return HtmlElementStates.IsOptional(element);

            case PseudoClass.Open:
                return HtmlElementStates.IsOpen(element);

            case PseudoClass.Valid:
                return HtmlElementStates.IsValid(element) == true;

            case PseudoClass.Invalid:
                return HtmlElementStates.IsValid(element) == false;

            case PseudoClass.UserValid:
                return HtmlElementStates.IsUserValid(element) == true;

            case PseudoClass.UserInvalid:
                return HtmlElementStates.IsUserValid(element) == false;

            case PseudoClass.InRange:
                return HtmlElementStates.IsInRange(element) == true;

            case PseudoClass.OutOfRange:
                return HtmlElementStates.IsInRange(element) == false;

            case PseudoClass.Heading:
                return HtmlElementStates.GetHeadingLevel(element) > 0;

            case PseudoClass.HeadingFunction:
                return selector.Levels.Contains(HtmlElementStates.GetHeadingLevel(element)) && HtmlElementStates.GetHeadingLevel(element) > 0;

            // The media pseudo-classes (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-playing)
            case PseudoClass.Playing:
                return element is HtmlMediaElement { Paused: false };

            case PseudoClass.Paused:
                return element is HtmlMediaElement { Paused: true };

            case PseudoClass.Seeking:
                return element is HtmlMediaElement { Seeking: true };

            case PseudoClass.Buffering:
                return element is HtmlMediaElement { IsBuffering: true };

            case PseudoClass.Stalled:
                return element is HtmlMediaElement { IsBuffering: true, IsCurrentlyStalled: true };

            case PseudoClass.Muted:
                return element is HtmlMediaElement { IsMuted: true };

            // https://w3c.github.io/webvtt/#the-past-and-future-pseudo-classes: the WebVTT node objects of the cues that are
            // rendered, relative to the current playback position of their media element
            case PseudoClass.Past:
                return element is WebVtt.WebVttElement { IsPast: true };

            case PseudoClass.Future:
                return element is WebVtt.WebVttElement { IsFuture: true };

            case PseudoClass.PopoverOpen:
                return element is HtmlElement { IsPopoverShowing: true } popover && popover.PopoverAttributeState != PopoverState.None;

            // https://html.spec.whatwg.org/multipage/semantics-other.html#selector-modal: a modal dialog, or an element whose
            // fullscreen flag is set
            case PseudoClass.Modal:
                return element is HtmlDialogElement { IsModal: true } || element.HasFullscreenFlag;

            // https://fullscreen.spec.whatwg.org/#fullscreen-pseudo-class
            case PseudoClass.Fullscreen:
                return FullscreenModel.MatchesFullscreen(element);

            // Autofill, picture-in-picture, the locked volume and view transitions are not modeled: these
            // pseudo-classes never match
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns whether nodes are assigned to the slot, through the slots assigned to it (https://drafts.csswg.org/css-shadow-1/#has-slotted-pseudo):
    /// an assigned slot counts for its flattened slottables, but the fallback content of the slot itself is not slotted.
    /// </summary>
    private static bool HasFlattenedAssignedNodes(HtmlSlotElement slot)
    {
        foreach (var node in slot.AssignedNodesList)
        {
            if (node is not HtmlSlotElement nestedSlot || nestedSlot.GetRoot() is not ShadowRoot || Slots.FindFlattenedSlottables(nestedSlot).Count > 0)
                return true;
        }

        return false;
    }

    private static Element? NextElementSibling(Node node)
    {
        for (var sibling = node.NextSibling; sibling is not null; sibling = sibling.NextSibling)
        {
            if (sibling is Element element)
                return element;
        }

        return null;
    }

    /// <summary>Counts the preceding (or following) element siblings, optionally of the same type or matching a selector list.</summary>
    private static int CountSiblings(Element element, bool forward, bool sameType, SelectorList? filter, in SelectorContext context)
    {
        if (context.IsUseInstanceRoot(element))
            return 0;

        // Counting the siblings of each child is quadratic: the indexes of the children are cached
        if (!sameType && filter is null && element.ParentNode is { } parent)
        {
            var index = GetElementIndex(element, parent);
            return forward ? GetElementIndex(LastElementChild(parent), parent) - index : index;
        }

        var count = 0;
        for (var sibling = forward ? NextElementSibling(element) : PreviousElementSibling(element); sibling is not null; sibling = forward ? NextElementSibling(sibling) : PreviousElementSibling(sibling))
        {
            if (sameType && !(sibling.LocalName == element.LocalName && string.Equals(sibling.NamespaceUri, element.NamespaceUri, StringComparison.Ordinal)))
                continue;

            if (filter is not null && !Matches(filter, sibling, context))
                continue;

            count++;
        }

        return count;
    }

    /// <summary>
    /// Returns the index of the element among the element children of its parent: the indexes of all the children are
    /// computed together, and cached in the elements until the children of the parent change.
    /// </summary>
    private static int GetElementIndex(Element element, Node parent)
    {
        var stamp = unchecked(parent.ChildrenVersion + 1);
        if (stamp == 0)
            return CountPreviousElementSiblings(element);

        if (element.ElementIndexStamp != stamp)
        {
            var index = 0;
            for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is Element childElement)
                {
                    childElement.ElementIndex = index++;
                    childElement.ElementIndexStamp = stamp;
                }
            }
        }

        return element.ElementIndex;
    }

    /// <summary>Counts the preceding element siblings (when the version of the children of the parent cannot stamp the cached indexes).</summary>
    private static int CountPreviousElementSiblings(Element element)
    {
        var count = 0;
        for (var sibling = PreviousElementSibling(element); sibling is not null; sibling = PreviousElementSibling(sibling))
        {
            count++;
        }

        return count;
    }

    private static Element LastElementChild(Node parent)
    {
        for (var child = parent.LastChild; child is not null; child = child.PreviousSibling)
        {
            if (child is Element element)
                return element;
        }

        throw new InvalidOperationException("The parent has no element child");
    }

    /// <summary>https://drafts.csswg.org/selectors-4/#relational</summary>
    private static bool MatchesHas(SelectorList relativeSelectors, Element anchor, in SelectorContext outerContext)
    {
        if (outerContext.InRelationalPseudoClass)
            return false;

        // (the links matched by the argument are unvisited: see SelectorContext.VisitedLink)
        var context = outerContext with { InRelationalPseudoClass = true, VisitedLink = null };

        // The descendants of the featureless shadow host are the elements of its shadow tree, and it has no siblings
        var isHost = context.ShadowHost is not null && ReferenceEquals(anchor, context.ShadowHost);
        Node descendantsRoot = isHost && anchor.AttachedShadowRoot is { } shadowRoot ? shadowRoot : anchor;
        foreach (var selector in relativeSelectors.Selectors)
        {
            var combinator = selector.RelativeCombinator ?? Combinator.Descendant;
            var last = selector.Compounds.Length - 1;
            if (combinator is Combinator.Descendant or Combinator.Child)
            {
                if (GetMaxRelativeDepth(selector) is { } maxDepth)
                {
                    // Without descendant combinator (e.g. :has(> img)), the subject is at most maxDepth levels below the anchor
                    if (MatchesHasDescendants(selector, last, descendantsRoot, maxDepth, context, anchor))
                        return true;
                }
                else
                {
                    for (var node = descendantsRoot.FirstChild; node is not null; node = node.GetNextInTreeOrder(descendantsRoot))
                    {
                        if (node is Element candidate && MatchesFrom(selector, last, candidate, context, anchor))
                            return true;
                    }
                }
            }
            else if (!isHost)
            {
                // The candidates are the following siblings of the anchor, and their descendants
                for (var sibling = anchor.NextSibling; sibling is not null; sibling = sibling.NextSibling)
                {
                    if (sibling is not Element siblingElement)
                        continue;

                    if (MatchesFrom(selector, last, siblingElement, context, anchor))
                        return true;

                    for (var node = siblingElement.FirstChild; node is not null; node = node.GetNextInTreeOrder(siblingElement))
                    {
                        if (node is Element candidate && MatchesFrom(selector, last, candidate, context, anchor))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the maximum number of levels between the anchor of a relative selector and its subject: the number of child
    /// combinators (the sibling combinators stay at the same level), or null when it contains a descendant combinator.
    /// </summary>
    private static int? GetMaxRelativeDepth(ComplexSelector selector)
    {
        if (selector.RelativeCombinator is not Combinator.Child)
            return null;

        var depth = 1;
        foreach (var combinator in selector.Combinators)
        {
            switch (combinator)
            {
                case Combinator.Child:
                    depth++;
                    break;
                case Combinator.NextSibling or Combinator.SubsequentSibling:
                    break;
                default:
                    return null;
            }
        }

        return depth;
    }

    /// <summary>Returns whether a descendant of the node, at most <paramref name="depth"/> levels below it, matches the relative selector.</summary>
    private static bool MatchesHasDescendants(ComplexSelector selector, int last, Node parent, int depth, in SelectorContext context, Element anchor)
    {
        for (var node = parent.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is not Element candidate)
                continue;

            if (MatchesFrom(selector, last, candidate, context, anchor))
                return true;

            if (depth > 1 && MatchesHasDescendants(selector, last, candidate, depth - 1, context, anchor))
                return true;
        }

        return false;
    }
}
