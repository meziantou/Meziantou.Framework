using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The CSS counters of the boxes of a document (https://drafts.csswg.org/css-lists-3/#auto-numbering): the counters are
/// inherited, instantiated, incremented and set in the tree order of the flat tree, including the ::marker, ::before and
/// ::after pseudo-elements. The elements that do not generate a box (display: none) do not affect the counters.
/// </summary>
/// <remarks>
/// Style containment (https://drafts.csswg.org/css-contain-2/#containment-style) scopes counter-increment and counter-set to
/// the sub-tree of the element (its pseudo-elements and its descendants): they create a new counter instead of changing a
/// counter created outside (each counter records the containment scope it was created in), while counter() and counters()
/// still see the counters of the outside.
/// </remarks>
internal sealed class CssCounters
{
    private readonly StyleEngine _engine;
    private readonly Dictionary<(Element Element, string? PseudoElement), List<Counter>> _sets = [];

    /// <summary>The counters set of the last box of the tree order, the source of the values of the next box.</summary>
    private List<Counter> _previous = [];

    /// <summary>The style containment scope of the current box (0 outside of any element with style containment).</summary>
    private int _scope;
    private int _lastScope;

    private CssCounters(StyleEngine engine)
    {
        _engine = engine;
        CounterStyles = new CounterStyleResolver(engine);
    }

    /// <summary>The counter styles of the document (https://drafts.csswg.org/css-counter-styles-3/).</summary>
    public CounterStyleResolver CounterStyles { get; }

    /// <summary>Computes the counters of the boxes of the document of the element.</summary>
    public static CssCounters Compute(Element element)
    {
        var counters = new CssCounters(element.NodeDocument.GetStyleEngine());
        if (element.NodeDocument.DocumentElement is { } root)
        {
            counters.Process(root, parentSet: null, siblingSet: null);
        }

        return counters;
    }

    /// <summary>
    /// Whether a pseudo-element other than <c>::before</c> and <c>::after</c> (e.g. a <c>::marker</c>) sets, resets or increments a
    /// counter: these operations are not in the box tree (see <c>BoxTreeBuilder.TryPatch</c>, which compares the operations of the boxes).
    /// </summary>
    public bool HasPseudoElementOperations { get; private set; }

    /// <summary>Returns whether the counters of the element or of its pseudo-element were computed (it generates a box, or it is a ::marker).</summary>
    public bool HasCounters(Element element, string? pseudoElement) => _sets.ContainsKey((element, pseudoElement));

    /// <summary>The counters in scope of the element or of its pseudo-element, innermost last.</summary>
    public IReadOnlyList<Counter> GetCounters(Element element, string? pseudoElement) => _sets.GetValueOrDefault((element, pseudoElement)) ?? [];

    /// <summary>https://drafts.csswg.org/css-lists-3/#funcdef-counter: the value of the innermost counter, 0 when there is none.</summary>
    public int GetCounterValue(Element element, string? pseudoElement, string name)
    {
        var counters = GetCounters(element, pseudoElement);
        for (var i = counters.Count - 1; i >= 0; i--)
        {
            if (counters[i].Name == name)
                return counters[i].Value;
        }

        return 0;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-lists-3/#funcdef-counter: the representation of the value of the innermost counter in the
    /// counter style (the writing mode and the direction of the style of the box select the symbols of the disclosure styles).
    /// </summary>
    public string GetCounter(Element element, string? pseudoElement, string name, CounterStyle counterStyle, ComputedStyle style)
        => CounterStyles.Generate(counterStyle, GetCounterValue(element, pseudoElement, name), style.Layout.WritingMode, style.Layout.Direction);

    /// <summary>https://drafts.csswg.org/css-lists-3/#funcdef-counters: the representations of the values of all the counters of the name, outermost first.</summary>
    public string GetCounters(Element element, string? pseudoElement, string name, string separator, CounterStyle counterStyle, ComputedStyle style)
    {
        var (writingMode, direction) = (style.Layout.WritingMode, style.Layout.Direction);
        var values = GetCounters(element, pseudoElement).Where(c => c.Name == name).Select(c => CounterStyles.Generate(counterStyle, c.Value, writingMode, direction)).ToList();
        return values.Count == 0 ? CounterStyles.Generate(counterStyle, 0, writingMode, direction) : string.Join(separator, values);
    }

    /// <summary>
    /// The counter style of the third argument of counter() or of counters(), or of list-style-type: a name is looked up from
    /// the tree of the style sheet of the declaration of the property (an inherited value keeps its tree,
    /// https://drafts.csswg.org/css-scoping/#css-tree-scoped-reference). Returns null when the value is not a counter style.
    /// </summary>
    public CounterStyle? ResolveCounterStyle(List<CssComponentValue> value, ComputedStyle style, string property)
    {
        Node? treeScope = null;
        for (var current = style; current is not null; current = current.Parent)
        {
            if (current.TryGetCascadedValue(property, out var cascaded) && cascaded.Value.Value.Trim() is not ("inherit" or "unset"))
            {
                treeScope = cascaded.TreeScope;
                break;
            }
        }

        return CounterStyles.Resolve(value, treeScope);
    }

    /// <summary>
    /// The marker string of a list item (https://drafts.csswg.org/css-lists-3/#marker-string) from the list-style-type of the
    /// style of its ::marker: the string, or the representation of the list-item counter in the counter style with the prefix
    /// and the suffix of the style. Returns null when list-style-type is none.
    /// </summary>
    /// <param name="element">The list item, or the originating element of the pseudo-element that is a list item.</param>
    /// <param name="style">The style of the marker.</param>
    /// <param name="pseudoElement">The pseudo-element whose counters are used: <c>marker</c>, or the ::before or ::after pseudo-element that is a list item.</param>
    public string? GetMarkerString(Element element, ComputedStyle style, string pseudoElement = "marker")
    {
        var listStyleType = style.GetComputedValue("list-style-type");
        if (listStyleType is "none" or "")
            return null;

        var value = Values.ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(listStyleType));
        if (value is [CssPreservedToken { Type: CssTokenType.String } text])
            return text.Token.Value;

        if (ResolveCounterStyle(value, style, "list-style-type") is not { } counterStyle)
            return null;

        return CounterStyles.GenerateMarker(counterStyle, GetCounterValue(element, pseudoElement, "list-item"), style.Layout.WritingMode, style.Layout.Direction);
    }

    private List<Counter>? Process(Element element, List<Counter>? parentSet, List<Counter>? siblingSet)
    {
        var style = _engine.GetStyle(element);
        var display = style.GetComputedValue("display");
        if (display == "none")
            return null;

        // https://drafts.csswg.org/css-lists-3/#counters-without-boxes: an element that does not generate a box cannot set,
        // reset or increment a counter. The boxes of the contents of an element with display: contents are children of the box of
        // its parent: they inherit the counters from it and from each other, and their counters are in scope after the element
        if (display == "contents" && parentSet is not null)
        {
            if (HasGeneratedContent(element, "before"))
            {
                siblingSet = ProcessPseudoElement(element, "before", parentSet, siblingSet);
            }

            foreach (var child in GetFlatTreeChildren(element))
            {
                if (child is Element childElement && Process(childElement, parentSet, siblingSet) is { } childSet)
                {
                    siblingSet = childSet;
                }
            }

            if (HasGeneratedContent(element, "after"))
            {
                siblingSet = ProcessPseudoElement(element, "after", parentSet, siblingSet);
            }

            return ProcessScrollButtons(element, parentSet, siblingSet);
        }

        var set = Inherit(parentSet, siblingSet);
        ApplyProperties(element, pseudoElement: null, style, set);
        Store(element, pseudoElement: null, set);

        // https://drafts.csswg.org/css-contain-2/#containment-style: the sub-tree of an element with style containment (not the
        // element itself) is a new scope ("no effect if the element does not generate a principal box": display: contents)
        var outerScope = _scope;
        if (display != "contents" && style.Layout.HasStyleContainment)
        {
            _scope = ++_lastScope;
        }

        // The pseudo-elements and the children are the children of the element box
        List<Counter>? childSiblingSet = null;
        if (display.Contains("list-item", StringComparison.Ordinal))
        {
            childSiblingSet = ProcessPseudoElement(element, "marker", set, childSiblingSet);
        }

        // https://drafts.csswg.org/css-overflow-5/#scroll-marker-pseudo: the ::scroll-marker of the element, whose box is in a
        // ::scroll-marker-group, is in the tree of its originating element for the counters, after its ::marker (as in Chrome)
        if (_engine.MayHaveAuthorPseudoElementRules(element, Layout.ScrollControls.Marker) && HasGeneratedContent(element, Layout.ScrollControls.Marker))
        {
            childSiblingSet = ProcessPseudoElement(element, Layout.ScrollControls.Marker, set, childSiblingSet);
        }

        // The scroll markers of the columns of a multi-column container (the columns of its last layout)
        if (_engine.MayHaveAuthorPseudoElementRules(element, Layout.ScrollControls.ColumnMarkerRuleName) && element.NodeDocument.LayoutEngineOrNull is { } layoutEngine)
        {
            var columnCount = layoutEngine.GetColumnCount(element);
            for (var i = 0; i < columnCount; i++)
            {
                var columnMarker = Layout.ScrollControls.GetColumnMarker(i);
                if (!HasGeneratedContent(element, columnMarker))
                    break;

                childSiblingSet = ProcessPseudoElement(element, columnMarker, set, childSiblingSet);
            }
        }

        if (HasGeneratedContent(element, "before"))
        {
            childSiblingSet = ProcessPseudoElement(element, "before", set, childSiblingSet);
        }

        foreach (var child in GetFlatTreeChildren(element))
        {
            if (child is Element childElement && Process(childElement, set, childSiblingSet) is { } childSet)
            {
                childSiblingSet = childSet;
            }
        }

        if (HasGeneratedContent(element, "after"))
        {
            childSiblingSet = ProcessPseudoElement(element, "after", set, childSiblingSet);
        }

        ProcessScrollButtons(element, set, childSiblingSet);
        _scope = outerScope;
        return set;
    }

    /// <summary>
    /// The <c>::scroll-button()</c> pseudo-elements of an element (https://drafts.csswg.org/css-overflow-5/#scroll-buttons): their
    /// boxes follow the box of the element, but their counters are in the scope of their originating element (they see its
    /// counter-reset), after its <c>::after</c>, as in Chrome.
    /// </summary>
    private List<Counter>? ProcessScrollButtons(Element element, List<Counter> parentSet, List<Counter>? siblingSet)
    {
        if (!_engine.MayHaveAuthorPseudoElementRules(element, Layout.ScrollControls.ButtonRuleName))
            return siblingSet;

        foreach (var button in Layout.ScrollControls.Buttons)
        {
            if (HasGeneratedContent(element, button))
            {
                siblingSet = ProcessPseudoElement(element, button, parentSet, siblingSet);
            }
        }

        return siblingSet;
    }

    private List<Counter> ProcessPseudoElement(Element element, string pseudoElement, List<Counter> parentSet, List<Counter>? siblingSet)
    {
        var set = Inherit(parentSet, siblingSet);
        var style = _engine.GetStyle(element, pseudoElement);
        if (pseudoElement is not ("before" or "after") && !HasPseudoElementOperations)
        {
            HasPseudoElementOperations = style.GetComputedValue("counter-reset") != "none" || style.GetComputedValue("counter-increment") != "none" || style.GetComputedValue("counter-set") != "none";
        }

        ApplyProperties(element, pseudoElement, style, set);
        Store(element, pseudoElement, set);
        return set;
    }

    /// <summary>
    /// Returns whether a pseudo-element generates a box: its content is not <c>none</c> and its display is not <c>none</c>
    /// ("an element that is not displayed cannot increment or reset a counter", https://drafts.csswg.org/css-lists-3/#counters-without-boxes).
    /// </summary>
    private bool HasGeneratedContent(Element element, string pseudoElement)
    {
        var style = _engine.GetStyle(element, pseudoElement);
        return style.GetComputedValue("content") is not ("none" or "normal" or "") && style.GetComputedValue("display") != "none";
    }

    /// <summary>
    /// https://drafts.csswg.org/css-lists-3/#inherit-counters: the counters of the preceding sibling (which include the ones of
    /// the parent that are still in scope), or of the parent when there is none, with the values of the box that precedes in
    /// tree order.
    /// </summary>
    private List<Counter> Inherit(List<Counter>? parentSet, List<Counter>? siblingSet)
    {
        var source = siblingSet ?? parentSet;
        var set = source is null ? [] : new List<Counter>(source);

        for (var i = 0; i < set.Count; i++)
        {
            var index = _previous.FindLastIndex(c => c.Name == set[i].Name && ReferenceEquals(c.Originator, set[i].Originator));
            if (index >= 0)
            {
                set[i] = set[i] with { Value = _previous[index].Value };
            }
        }

        return set;
    }

    private void Store(Element element, string? pseudoElement, List<Counter> set)
    {
        _sets[(element, pseudoElement)] = set;
        _previous = set;
    }

    /// <summary>
    /// counter-reset, counter-increment (and the implicit increment of the list items), then counter-set. The start, reversed
    /// and value attributes of the ol and li elements are presentational hints for these properties
    /// (https://html.spec.whatwg.org/multipage/rendering.html#lists).
    /// </summary>
    private void ApplyProperties(Element element, string? pseudoElement, ComputedStyle style, List<Counter> set)
    {
        var originator = new CounterOriginator(element, pseudoElement);
        foreach (var counter in ParseCounters(style.GetComputedValue("counter-reset"), defaultValue: 0))
        {
            // https://drafts.csswg.org/css-lists-3/#instantiate-counter: a reversed counter without initial value
            var initialValue = counter.Reversed && !counter.HasValue ? ComputeReversedInitialValue(element, pseudoElement, counter.Name) : counter.Value;
            Instantiate(set, counter.Name, originator, initialValue, counter.Reversed, _scope);
        }

        var increments = ParseCounters(style.GetComputedValue("counter-increment"), defaultValue: 1);
        if (pseudoElement is null or "before" or "after" && !increments.Exists(c => c.Name == "list-item") && style.GetComputedValue("display").Contains("list-item", StringComparison.Ordinal))
        {
            // https://drafts.csswg.org/css-lists-3/#list-item-counter: -1 when the counter is reversed (the counter that the
            // increment changes: a counter created outside of the style containment scope of the box is not, a new one is
            // instantiated)
            var listItem = set.FindLastIndex(c => c.Name == "list-item");
            increments.Add(new CounterValue("list-item", listItem >= 0 && set[listItem].Reversed && set[listItem].Scope == _scope ? -1 : 1, Reversed: false, HasValue: true));
        }

        // https://drafts.csswg.org/css-lists-3/#auto-numbering says that a value outside of the limits of the user agent is
        // clamped; like the browsers, an increment that overflows the 32-bit integers is ignored
        foreach (var counter in increments)
        {
            var index = GetOrInstantiate(set, counter.Name, originator);
            var value = (long)set[index].Value + counter.Value;
            if (value is >= int.MinValue and <= int.MaxValue)
            {
                set[index] = set[index] with { Value = (int)value };
            }
        }

        foreach (var counter in ParseCounters(style.GetComputedValue("counter-set"), defaultValue: 0))
        {
            var index = GetOrInstantiate(set, counter.Name, originator);
            set[index] = set[index] with { Value = counter.Value };
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-lists-3/#instantiate-counter: the initial value of a reversed counter instantiated without
    /// initial value, computed from the increments of the boxes of its scope (the element, its descendants, and its following
    /// siblings and their descendants, except the scopes of the nested counters of the same name).
    /// </summary>
    private int ComputeReversedInitialValue(Element element, string? pseudoElement, string name)
    {
        var state = new ReversedCounterState(name);

        // The scope of a counter of a pseudo-element is not walked (only its following siblings would be)
        if (pseudoElement is not null || VisitReversedCounterScope(element, isOriginator: true, state) != ScopeWalk.Stop)
        {
            if (HtmlElement.GetFlatTreeParent(element) is Element parent)
            {
                var following = false;
                foreach (var sibling in GetFlatTreeChildren(parent))
                {
                    if (!following)
                    {
                        following = ReferenceEquals(sibling, element);
                        continue;
                    }

                    // A sibling that instantiates a counter of the same name replaces the counter
                    if (sibling is Element siblingElement && VisitReversedCounterScope(siblingElement, isOriginator: false, state) != ScopeWalk.Continue)
                        break;
                }
            }
        }

        return (int)Math.Clamp((long)state.Value + state.LastNonZeroIncrementNegated, int.MinValue, int.MaxValue);
    }

    private ScopeWalk VisitReversedCounterScope(Element element, bool isOriginator, ReversedCounterState state)
    {
        var style = _engine.GetStyle(element);
        var display = style.GetComputedValue("display");
        if (display == "none")
            return ScopeWalk.Continue;

        // A nested counter of the same name: its scope is the element, its descendants and its following siblings
        if (!isOriginator && ParseCounters(style.GetComputedValue("counter-reset"), defaultValue: 0).Exists(c => c.Name == state.Name))
            return ScopeWalk.EndSiblings;

        var increment = 0;
        var increments = ParseCounters(style.GetComputedValue("counter-increment"), defaultValue: 1);
        var explicitIncrement = increments.FindLastIndex(c => c.Name == state.Name);
        if (explicitIncrement >= 0)
        {
            increment = increments[explicitIncrement].Value;
        }
        else if (state.Name == "list-item" && display.Contains("list-item", StringComparison.Ordinal))
        {
            // The implicit increment of a list item for a reversed counter
            increment = -1;
        }

        var incrementNegated = -increment;
        if (incrementNegated != 0)
        {
            state.LastNonZeroIncrementNegated = incrementNegated;
        }

        var sets = ParseCounters(style.GetComputedValue("counter-set"), defaultValue: 0);
        var set = sets.FindLastIndex(c => c.Name == state.Name);
        if (set >= 0)
        {
            state.Value += sets[set].Value;
            return ScopeWalk.Stop;
        }

        state.Value += incrementNegated;

        // https://drafts.csswg.org/css-contain-2/#containment-style: the increments in the sub-tree of an element with style
        // containment are scoped to it: they do not change the counter
        if (display != "contents" && style.Layout.HasStyleContainment)
            return ScopeWalk.Continue;

        foreach (var child in GetFlatTreeChildren(element))
        {
            if (child is not Element childElement)
                continue;

            var result = VisitReversedCounterScope(childElement, isOriginator: false, state);
            if (result == ScopeWalk.Stop)
                return ScopeWalk.Stop;

            if (result == ScopeWalk.EndSiblings)
                break;
        }

        return ScopeWalk.Continue;
    }

    /// <summary>
    /// Returns the index of the innermost counter of the name, which counter-increment and counter-set change, or instantiates
    /// one when there is none, or when it was created outside of the style containment scope of the box.
    /// </summary>
    private int GetOrInstantiate(List<Counter> set, string name, CounterOriginator originator)
    {
        var index = set.FindLastIndex(c => c.Name == name);
        if (index >= 0 && set[index].Scope == _scope)
            return index;

        Instantiate(set, name, originator, 0, reversed: false, _scope);
        return set.Count - 1;
    }

    /// <summary>https://drafts.csswg.org/css-lists-3/#instantiate-counter: a counter of the same box or of a preceding sibling is replaced.</summary>
    private void Instantiate(List<Counter> set, string name, CounterOriginator originator, int value, bool reversed, int scope)
    {
        var index = set.FindLastIndex(c => c.Name == name);
        if (index >= 0 && IsSelfOrSibling(set[index].Originator, originator))
        {
            set.RemoveAt(index);
        }

        set.Add(new Counter(name, originator, value, reversed, scope));
    }

    private bool IsSelfOrSibling(CounterOriginator existing, CounterOriginator originator)
    {
        if (ReferenceEquals(existing.Element, originator.Element) && existing.PseudoElement == originator.PseudoElement)
            return true;

        return ReferenceEquals(GetBoxParent(existing), GetBoxParent(originator));
    }

    /// <summary>The element of the parent box of the box of an originator: the boxes of the pseudo-elements of an element are children of its box.</summary>
    private Element? GetBoxParent(CounterOriginator originator)
        => originator.PseudoElement is not null && _engine.GetStyle(originator.Element).GetComputedValue("display") != "contents" ? originator.Element : GetBoxParent(originator.Element);

    /// <summary>The element of the parent box of the box of an element: its nearest flat tree ancestor that generates a box (not display: contents).</summary>
    private Element? GetBoxParent(Element element)
    {
        var parent = HtmlElement.GetFlatTreeParent(element) as Element;
        while (parent is not null && _engine.GetStyle(parent).GetComputedValue("display") == "contents" && HtmlElement.GetFlatTreeParent(parent) is Element ancestor)
        {
            parent = ancestor;
        }

        return parent;
    }

    /// <summary>
    /// Parses the computed value of counter-reset, counter-increment or counter-set: none, or a list of names (or reversed()
    /// names) with optional integers.
    /// </summary>
    internal static List<CounterValue> ParseCounters(string value, int defaultValue)
    {
        var result = new List<CounterValue>();
        if (value is "none" or "")
            return result;

        foreach (var component in Values.ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            switch (component)
            {
                case CssPreservedToken { Type: CssTokenType.Ident } ident:
                    result.Add(new CounterValue(ident.Token.Value, defaultValue, Reversed: false, HasValue: false));
                    break;

                case CssFunction { Name: var functionName } function when InfraAscii.EqualsIgnoreAsciiCase(functionName, "reversed"):
                    if (function.Value.OfType<CssPreservedToken>().FirstOrDefault(t => t.Type == CssTokenType.Ident) is { } reversedName)
                    {
                        result.Add(new CounterValue(reversedName.Token.Value, defaultValue, Reversed: true, HasValue: false));
                    }

                    break;

                case CssPreservedToken { Type: CssTokenType.Number, Token.IsInteger: true } number when result.Count > 0:
                    result[^1] = result[^1] with { Value = (int)Math.Clamp(number.Token.NumericValue, int.MinValue, int.MaxValue), HasValue = true };
                    break;
            }
        }

        return result;
    }

    private static IEnumerable<Node> GetFlatTreeChildren(Element element)
    {
        if (element.AttachedShadowRoot is { } shadowRoot)
            return shadowRoot.ChildNodes;

        if (element is HtmlSlotElement slot && slot.AssignedNodesList.Count > 0)
            return slot.AssignedNodesList;

        return element.ChildNodes;
    }

    /// <summary>The box that created a counter: an element or one of its pseudo-elements.</summary>
    internal sealed record CounterOriginator(Element Element, string? PseudoElement);

    /// <summary>
    /// A counter (https://drafts.csswg.org/css-lists-3/#css-counter): its name, the box that created it, its value, whether it is
    /// reversed, and the style containment scope it was created in (only the boxes of this scope change it).
    /// </summary>
    internal readonly record struct Counter(string Name, CounterOriginator Originator, int Value, bool Reversed = false, int Scope = 0);

    /// <summary>An item of the value of counter-reset, counter-increment or counter-set: a name, whether it is reversed, and its integer (or the default integer when it has none).</summary>
    internal readonly record struct CounterValue(string Name, int Value, bool Reversed, bool HasValue);

    /// <summary>The state of the computation of the initial value of a reversed counter.</summary>
    private sealed class ReversedCounterState(string name)
    {
        public string Name { get; } = name;

        public int Value { get; set; }

        public int LastNonZeroIncrementNegated { get; set; }
    }

    private enum ScopeWalk
    {
        /// <summary>Continue with the next box.</summary>
        Continue,

        /// <summary>The box starts the scope of a nested counter, which includes its following siblings: continue after its parent.</summary>
        EndSiblings,

        /// <summary>The value is computed (a box sets the counter).</summary>
        Stop,
    }
}
