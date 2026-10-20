using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The invalidation of the computed styles element by element: when only the DOM, the states of the elements and the animated
// values changed since the last update, and the changes are known (Document.StyleInvalidations, DocumentAnimations), the styles
// of the elements that the changes may affect are checked (see StyleInvalidationFeatures), and only the ones whose cascaded
// values changed are computed again, with their descendants. The other changes (the style sheets, the environment, the fonts,
// the changes that are not recorded) invalidate all the styles (see EnsureUpToDate).
internal sealed partial class StyleEngine
{
    /// <summary>
    /// Whether the invalidations by element are verified (the <c>WEBPLATFORM_VERIFY_STYLE_INVALIDATION</c> environment variable is
    /// <c>1</c>): after each update, the cascade of each kept style is computed again and compared with the cascade of the style,
    /// and an <see cref="InvalidOperationException"/> is thrown when they differ (a missing invalidation). For the tests.
    /// </summary>
    internal static bool VerifyInvalidations { get; set; } = System.Environment.GetEnvironmentVariable("WEBPLATFORM_VERIFY_STYLE_INVALIDATION") == "1";

    /// <summary>
    /// The names of the pseudo-elements whose styles are computed, by element (to discard them with the style of the element). The
    /// arrays are never modified (a name is added by replacing the array), so the elements share the common ones (see
    /// <see cref="CommonPseudoElementStyleNames"/>).
    /// </summary>
    private readonly Dictionary<Element, string[]> _pseudoElementStyleNames = new(ReferenceEqualityComparer.Instance);

    /// <summary>The names of the pseudo-elements whose styles <see cref="RecalculateAllStyles"/> computes for every element, in its order.</summary>
    private static readonly string[][] CommonPseudoElementStyleNames =
    [
        ["marker"], ["marker", "before"], ["marker", "before", "after"], ["marker", "after"], ["before"], ["before", "after"], ["after"],
    ];

    /// <summary>Whether styles of WebVTT objects were computed (they inherit from their media element, out of the tree).</summary>
    private bool _hasWebVttStyles;

    /// <summary>Whether a style attribute uses a tree-counting function (see <see cref="StyleInvalidationFeatures.UsesTreeCountingFunctions"/>).</summary>
    private bool _inlineStylesUseTreeCountingFunctions;

    /// <summary>The number of updates whose styles were invalidated element by element (for the tests).</summary>
    internal int ElementInvalidationCount { get; private set; }

    /// <summary>The number of updates that invalidated all the styles (for the tests).</summary>
    internal int FullInvalidationCount { get; private set; }

    private void AddPseudoElementStyleName(Element element, string pseudoElement)
    {
        ref var names = ref CollectionsMarshal.GetValueRefOrAddDefault(_pseudoElementStyleNames, element, out _);
        if (names is not null && Array.IndexOf(names, pseudoElement) >= 0)
            return;

        string[] result = names is null ? [pseudoElement] : [.. names, pseudoElement];
        foreach (var common in CommonPseudoElementStyleNames)
        {
            if (common.AsSpan().SequenceEqual(result))
            {
                result = common;
                break;
            }
        }

        names = result;
    }

    /// <summary>The changes of the document since the last update of the styles (see <see cref="ConsumeElementInvalidations"/>).</summary>
    private sealed class ElementInvalidations
    {
        public List<StyleInvalidation>? Changes { get; init; }

        public List<(Element Element, string? PseudoElement)>? AnimationTargets { get; init; }
    }

    /// <summary>
    /// Returns the changes of the document and of the animations since the last update of the styles of the document, and
    /// clears them, or null when they are not known element by element (all the styles are then invalidated).
    /// </summary>
    private ElementInvalidations? ConsumeElementInvalidations(bool domOrStateChanged, bool animationsChanged)
    {
        // Only the style engine of the document records the changes (the other engines, e.g. the one of the printing, invalidate all
        // their styles)
        if (_environmentOverride is not null || PrintViewportSize is not null)
            return null;

        var log = _document.StyleInvalidations;
        List<StyleInvalidation>? changes = null;
        var known = true;
        if (domOrStateChanged)
        {
            if (log.IsFull)
            {
                known = false;
            }
            else if (log.Entries.Count > 0)
            {
                changes = [.. log.Entries];
            }
        }

        if (log.IsFull)
        {
            AddFullBoxTreeChange();
        }
        else
        {
            AddBoxTreeChanges(log.Entries);
        }

        log.Clear();

        List<(Element Element, string? PseudoElement)>? targets = null;
        if (_document.AnimationsOrNull is { } animations)
        {
            var invalidatedTargets = animations.ConsumeStyleInvalidations();
            if (animationsChanged)
            {
                if (invalidatedTargets is null)
                {
                    known = false;
                }
                else
                {
                    targets = invalidatedTargets;
                }
            }
        }

        return known ? new ElementInvalidations { Changes = changes, AnimationTargets = targets } : null;
    }

    /// <summary>
    /// Returns whether the styles of the document can be invalidated element by element. The documents in quirks mode (some
    /// rules of the user agent depend on other elements), with shadow trees, an active view transition, SMIL animations, WebVTT
    /// cues or declarations of scroll-driven timelines (the timelines are looked up by name in the styles of other elements)
    /// invalidate all their styles.
    /// </summary>
    private bool CanInvalidateElements() =>
        _environmentOverride is null && PrintViewportSize is null && _document.Mode != DocumentMode.Quirks && !_document.HasShadowRoots
        && !_document.HasViewTransitionPseudoTrees && _document.SmilOrNull is null && !_hasWebVttStyles
        && !_inlineStylesDeclareTimelines && !GetTreeStyles(_document).Rules.DeclaresTimelines;

    /// <summary>Whether a style attribute declares a named scroll-driven timeline or the timeline of an animation (see <see cref="CascadeRuleSet.DeclaresTimelines"/>).</summary>
    private bool _inlineStylesDeclareTimelines;

    /// <summary>
    /// Invalidates the styles of the elements affected by the changes, or returns false when all the styles must be invalidated.
    /// Called by <see cref="EnsureUpToDate"/> once the versions are updated.
    /// </summary>
    private bool TryInvalidateElements(ElementInvalidations invalidations, bool domChanged)
    {
        if (!CanInvalidateElements())
            return false;

        // Nothing to invalidate (the before-change styles of a previous update wait for the next computation of the styles)
        if (_styles.Count == 0)
        {
            if (domChanged)
            {
                PruneBeforeChangeStyles();
            }

            ElementInvalidationCount++;
            return true;
        }

        // The style sheets of the document changed (e.g. a style element was inserted)
        var previousTree = _trees.GetValueOrDefault(_document);
        if (previousTree is null || !ReferenceEquals(GetTreeStyles(_document), previousTree))
            return false;

        var work = new InvalidationWork(this);
        if (!work.Collect(invalidations))
            return false;

        // (the styles computed by the cascades of the checks are computed with the current versions: a change of a version is
        // seen by the next update, as in the layout, see SuspendUpdates)
        var previousRecalculating = _recalculating;
        _recalculating = true;
        _updatesSuspended++;
        try
        {
            var mayHaveTransitions = MayHaveTransitions();
            work.Apply(mayHaveTransitions);
            if (domChanged)
            {
                PruneBeforeChangeStyles();
            }

            // The styles before the change are kept for the transitions, and the invalidated styles are computed again at the
            // next style read or animation frame (see InvalidationWork.Apply and RecalculateStyles)
            if (mayHaveTransitions)
            {
                ScheduleRecalculation(all: false);
            }
            else
            {
                _beforeChangeStyles.Clear();
            }

            if (VerifyInvalidations)
            {
                VerifyKeptStyles();
            }
        }
        finally
        {
            _updatesSuspended--;
            _recalculating = previousRecalculating;
        }

        ElementInvalidationCount++;
        return true;
    }

    /// <summary>The features of the rules that apply to the elements of the document: the user agent, the presentational hints and the style sheets of the document.</summary>
    private StyleInvalidationFeatures[] GetInvalidationFeatures() =>
    [
        GetUserAgentRuleSet(_document, _environment!).InvalidationFeatures,
        PresentationalHintRuleSet.Value.InvalidationFeatures,
        GetTreeStyles(_document).Rules.InvalidationFeatures,
    ];

    /// <summary>Returns whether the cascaded values are the same.</summary>
    private static bool CascadesEqual(Dictionary<string, CascadedValue> x, Dictionary<string, CascadedValue> y, bool lenient = false)
    {
        if (ReferenceEquals(x, y))
            return true;

        if (x.Count != y.Count)
            return false;

        foreach (var (name, value) in x)
        {
            if (!y.TryGetValue(name, out var other))
                return false;

            // (the verification compares the declared values: their typed representations are not compared)
            if ((lenient ? !string.Equals(value.Value.Value, other.Value.Value, StringComparison.Ordinal) : value.Value != other.Value)
                || value.Rank != other.Rank || value.Position != other.Position || value.IsImportant != other.IsImportant
                || !Equals(value.Source, other.Source) || !ReferenceEquals(value.OriginatingElement, other.OriginatingElement))
            {
                return false;
            }

            // A value that rolls back the cascade references the matched declaration blocks, which are not compared
            if (!lenient && (value.RollBackSubstitution is not null || other.RollBackSubstitution is not null))
                return false;
        }

        return true;
    }

    /// <summary>Returns whether the cascaded values of a style reference the attributes of the element (attr()).</summary>
    private static bool ReferencesAttributes(ComputedStyle style)
    {
        foreach (var value in style.InitialCascade.Values)
        {
            if (Contains(value.Value.Value) || Contains(value.Value.ShorthandValue) || Contains(value.Value.ComputationValue))
                return true;
        }

        return false;

        static bool Contains(string? value) => value is not null && value.Contains("attr(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the style of an element is the parent of the styles of other elements through a pseudo-element: the children of a
    /// details element inherit from its <c>::details-content</c>, the contents of a select element from its <c>::picker(select)</c>,
    /// and the highlight pseudo-elements from the ones of the parent element.
    /// </summary>
    private bool HasInheritedPseudoElementStyles(Element element)
    {
        if (!_pseudoElementStyleNames.TryGetValue(element, out var names))
            return false;

        foreach (var name in names)
        {
            if (name.StartsWith(DetailsContent, StringComparison.Ordinal) || name.StartsWith(SelectPicker, StringComparison.Ordinal) || IsHighlightPseudoElement(name))
                return true;
        }

        return false;
    }

    /// <summary>Removes the styles of the pseudo-elements of an element; returns false when the element had none.</summary>
    private bool DiscardPseudoElementStyles(Element element, bool keepBeforeChange)
    {
        if (!_pseudoElementStyleNames.Remove(element, out var names))
            return false;

        AddBoxTreeChange(element);
        foreach (var name in names)
        {
            if (_styles.Remove((element, name), out var style) && keepBeforeChange)
            {
                _beforeChangeStyles[(element, name)] = style;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks that the cascaded values of each kept style are the ones a new cascade gives, and that its parent is the style of
    /// its parent element (see <see cref="VerifyInvalidations"/>).
    /// </summary>
    private void VerifyKeptStyles()
    {
        foreach (var ((element, pseudoElement), style) in _styles.ToArray())
        {
            if (pseudoElement is not null || !element.IsConnected || !ReferenceEquals(element.NodeDocument, _document) || element is WebVtt.WebVttElement)
                continue;

            if (!_styles.TryGetValue((element, null), out var current) || !ReferenceEquals(current, style))
                continue;

            var parentElement = GetInheritanceParent(element);
            var expectedParent = parentElement is null ? null : IsInDetailsContent(element) || IsInSelectPicker(element) ? null : _styles.GetValueOrDefault((parentElement, null));
            if (parentElement is not null && expectedParent is not null && !ReferenceEquals(style.Parent, expectedParent))
                throw new InvalidOperationException($"Style invalidation: the style of <{element.LocalName}> was kept, but not the style of its parent <{parentElement.LocalName}> (generations: style {style.Generation}, its parent {style.Parent?.Generation}, current parent {expectedParent.Generation}, engine {_version})");

            if (style.IsCascadeOfRules && element is not HtmlSelectElement && !HasControlBaseAppearance(style))
            {
                var cascade = Cascade(element, pseudoElement: null, style.Parent, out _);
                if (!CascadesEqual(style.InitialCascade, cascade, lenient: true))
                    throw new InvalidOperationException($"Style invalidation: the style of <{element.LocalName} id='{element.Id}' class='{element.GetAttributeValue("class")}'> was kept, but its cascaded values changed: {DescribeDifference(style.InitialCascade, cascade)}");
            }
        }
    }

    private static string DescribeDifference(Dictionary<string, CascadedValue> x, Dictionary<string, CascadedValue> y)
    {
        var result = new System.Text.StringBuilder();
        foreach (var name in x.Keys.Union(y.Keys, StringComparer.Ordinal))
        {
            var hasX = x.TryGetValue(name, out var valueX);
            var hasY = y.TryGetValue(name, out var valueY);
            if (hasX && hasY && valueX.Value == valueY.Value && valueX.Rank == valueY.Rank && valueX.Position == valueY.Position && Equals(valueX.Source, valueY.Source))
                continue;

            result.Append(name).Append(": ").Append(hasX ? valueX.Value.Value + "@" + valueX.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(none)").Append(" -> ").Append(hasY ? valueY.Value.Value + "@" + valueY.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(none)").Append("; ");
        }

        return result.ToString();
    }

    /// <summary>The elements whose styles are checked or discarded for a set of changes.</summary>
    private sealed class InvalidationWork(StyleEngine engine)
    {
        /// <summary>The elements whose own styles are checked.</summary>
        private readonly HashSet<Element> _checks = new(ReferenceEqualityComparer.Instance);

        /// <summary>The elements whose styles and the styles of their descendants are checked.</summary>
        private readonly List<Element> _subtrees = [];

        /// <summary>The elements whose styles are computed again, even when their cascaded values are the same (e.g. their animations changed).</summary>
        private readonly HashSet<Element> _forced = new(ReferenceEqualityComparer.Instance);

        /// <summary>The elements whose attributes changed: their styles are computed again when they reference their attributes.</summary>
        private readonly HashSet<Element> _attributesChanged = new(ReferenceEqualityComparer.Instance);

        /// <summary>The elements whose positions among their siblings changed: their animations may use tree-counting functions.</summary>
        private readonly HashSet<Element> _positionsChanged = new(ReferenceEqualityComparer.Instance);

        /// <summary>The nodes removed from the document or inserted in it: their styles are discarded.</summary>
        private readonly List<Node> _removed = [];

        /// <summary>The elements whose styles were checked.</summary>
        private readonly HashSet<Element> _checked = new(ReferenceEqualityComparer.Instance);

        private StyleInvalidationFeatures[] _features = [];
        private bool _usesTreeCountingFunctions;
        private bool _usesFunctions;
        private bool _rootChanged;

        /// <summary>Collects the elements affected by the changes; returns false when all the styles must be invalidated.</summary>
        public bool Collect(ElementInvalidations invalidations)
        {
            _features = engine.GetInvalidationFeatures();
            var treeStyles = engine.GetTreeStyles(engine._document);
            _usesFunctions = treeStyles.FunctionRules.Count > 0;
            _usesTreeCountingFunctions = _usesFunctions || engine._inlineStylesUseTreeCountingFunctions || _features.Any(static features => features.UsesTreeCountingFunctions);
            if (invalidations.Changes is { } changes)
            {
                foreach (var change in changes)
                {
                    var result = change.Kind switch
                    {
                        StyleInvalidationKind.Attribute => CollectAttribute(change),
                        StyleInvalidationKind.ChildInserted or StyleInvalidationKind.ChildRemoved => CollectChild(change),
                        StyleInvalidationKind.Text => CollectText(change),
                        StyleInvalidationKind.State => CollectState(change),
                        _ => false,
                    };

                    if (!result)
                        return false;
                }
            }

            if (invalidations.AnimationTargets is { } targets)
            {
                foreach (var (element, _) in targets)
                {
                    // (the effects may target the elements of another document, whose styles are invalidated by their engine)
                    if (!element.IsConnected || !ReferenceEquals(element.NodeDocument, engine._document))
                        continue;

                    _checks.Add(element);
                    _forced.Add(element);
                }
            }

            return true;
        }

        private InvalidationScope GetScope(Func<StyleInvalidationFeatures, InvalidationScope> selector)
        {
            var scope = InvalidationScope.None;
            foreach (var features in _features)
            {
                scope |= selector(features);
            }

            return scope;
        }

        private bool CollectAttribute(StyleInvalidation change)
        {
            if (change.Target is not Element element || !element.IsConnected)
                return true;

            if (change.Namespace is not null)
                return false;

            var name = change.Name!;
            var scope = GetScope(features => features.GetAttributeScope(name));
            switch (name)
            {
                case "class":
                    foreach (var className in GetChangedClasses(change.OldValue, change.NewValue))
                    {
                        scope |= GetScope(features => features.GetClassScope(className));
                    }

                    break;

                case "id":
                    // The target element (:target) is looked up by id, and the id of the form-associated elements, of the forms
                    // and of the labeled controls relate them to other elements
                    if (_features.Any(static features => features.UsesTarget) || IsSensitiveNode(element))
                        return false;

                    if (change.OldValue is { Length: > 0 } oldId)
                    {
                        scope |= GetScope(features => features.GetIdScope(oldId));
                    }

                    if (change.NewValue is { Length: > 0 } newId)
                    {
                        scope |= GetScope(features => features.GetIdScope(newId));
                    }

                    break;

                case "style":
                    // The timelines of the animations are looked up by name in the styles of other elements
                    if (ContainsTimeline(change.OldValue) || ContainsTimeline(change.NewValue))
                        return false;

                    break;

                default:
                    // The other attributes may change the states of other elements (e.g. disabled, href, lang, dir), the
                    // presentational hints of other elements (e.g. the attributes of a table, of a body element) or the style
                    // sheets: only the data and ARIA attributes are invalidated element by element
                    if (!name.StartsWith("data-", StringComparison.Ordinal) && !name.StartsWith("aria-", StringComparison.Ordinal))
                        return false;

                    break;
            }

            // The element itself: its style attribute, attr()...
            _checks.Add(element);
            _attributesChanged.Add(element);
            return Apply(scope, element);
        }

        private static bool ContainsTimeline(string? value) => value is not null && value.Contains("timeline", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> GetChangedClasses(string? oldValue, string? newValue)
        {
            var oldClasses = Split(oldValue);
            var newClasses = Split(newValue);
            foreach (var className in oldClasses)
            {
                if (!newClasses.Contains(className))
                    yield return className;
            }

            foreach (var className in newClasses)
            {
                if (!oldClasses.Contains(className))
                    yield return className;
            }

            static HashSet<string> Split(string? value) => value is null ? [] : [.. value.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries)];
        }

        private bool CollectChild(StyleInvalidation change)
        {
            var child = change.Child;
            if (change.Target is not Element parent)
            {
                // The children of the document: the root element changes all the styles (the comments and the doctype do not
                // change the styles; the style sheets of the xml-stylesheet processing instructions are compared)
                return change.Target is Dom.Document && child is not Element;
            }

            if (child is not (Element or Text))
                return true;

            if (IsSensitiveParent(parent) || (child is Element element && SubtreeIsSensitive(element)))
                return false;

            // The styles of a removed node are discarded, and so are the styles of an inserted node: it may have been moved from a
            // node removed before (its removal from the removed node is not recorded)
            _removed.Add(child);

            if (!parent.IsConnected)
                return true;

            return CollectChildren(parent, elementChanged: child is Element);
        }

        private bool CollectText(StyleInvalidation change)
        {
            if (change.Target is not Element parent || !parent.IsConnected)
                return true;

            if (IsSensitiveParent(parent))
                return false;

            return CollectChildren(parent, elementChanged: false);
        }

        /// <summary>Collects the elements affected by a change of the children of an element (an element or a text node inserted or removed, the data of a text changed).</summary>
        private bool CollectChildren(Element parent, bool elementChanged)
        {
            // The target element (:target) is looked up by id
            if (elementChanged && _features.Any(static features => features.UsesTarget))
                return false;

            if (!Apply(GetScope(static features => features.Empty), parent))
                return false;

            // The anchors of :has() are the ancestors of the children and their siblings
            if (!Apply(GetScope(static features => features.Has), parent.FirstElementChild ?? parent))
                return false;

            // The directionality of the elements with auto directionality depends on the text of their descendants
            if (FindAutoDirectionalityRoot(parent) is { } autoRoot)
            {
                _subtrees.Add(autoRoot);
            }

            if (elementChanged)
            {
                var positional = GetScope(static features => features.Positional | features.SiblingCombinators);
                for (var sibling = parent.FirstElementChild; sibling is not null; sibling = sibling.NextElementSibling)
                {
                    if (!Apply(positional, sibling))
                        return false;

                    // sibling-index() and sibling-count() depend on the position of the element (also in the keyframes of its animations)
                    if (_usesTreeCountingFunctions)
                    {
                        _checks.Add(sibling);
                        _forced.Add(sibling);
                    }
                    else
                    {
                        _checks.Add(sibling);
                        _positionsChanged.Add(sibling);
                    }
                }
            }

            return true;
        }

        private bool CollectState(StyleInvalidation change)
        {
            if (change.Target is not Element element || !element.IsConnected)
                return true;

            var states = change.States;
            var scope = InvalidationScope.None;
            if ((states & StyleInvalidationStates.Hover) != StyleInvalidationStates.None)
            {
                scope |= GetScope(static features => features.GetPseudoClassScope(PseudoClass.Hover));
            }

            if ((states & StyleInvalidationStates.Active) != StyleInvalidationStates.None)
            {
                scope |= GetScope(static features => features.GetPseudoClassScope(PseudoClass.Active));

                // The color of an active link may be the alink attribute of the body element (a presentational hint)
                if (HtmlElementStates.IsLink(element))
                {
                    scope |= InvalidationScope.Self;
                }
            }

            if ((states & StyleInvalidationStates.Focus) != StyleInvalidationStates.None)
            {
                scope |= GetScope(static features => features.GetPseudoClassScope(PseudoClass.Focus) | features.GetPseudoClassScope(PseudoClass.FocusVisible) | features.GetPseudoClassScope(PseudoClass.FocusWithin));
            }

            if ((states & StyleInvalidationStates.CustomState) != StyleInvalidationStates.None)
            {
                scope |= GetScope(static features => features.GetPseudoClassScope(PseudoClass.State));
            }

            return Apply(scope, element);
        }

        /// <summary>Adds the elements of a scope relative to an element; returns false for the global scope.</summary>
        private bool Apply(InvalidationScope scope, Element element)
        {
            if (scope == InvalidationScope.None)
                return true;

            if ((scope & InvalidationScope.Global) != InvalidationScope.None)
                return false;

            if ((scope & InvalidationScope.Self) != InvalidationScope.None)
            {
                _checks.Add(element);
            }

            if ((scope & InvalidationScope.Subtree) != InvalidationScope.None)
            {
                _subtrees.Add(element);
            }

            if ((scope & (InvalidationScope.Siblings | InvalidationScope.SiblingSubtrees)) != InvalidationScope.None)
            {
                for (var sibling = element.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
                {
                    AddSibling(sibling, subtree: (scope & InvalidationScope.SiblingSubtrees) != InvalidationScope.None);
                }
            }

            if ((scope & (InvalidationScope.AllSiblings | InvalidationScope.AllSiblingSubtrees)) != InvalidationScope.None && element.ParentNode is { } parent)
            {
                for (var sibling = parent.FirstChild; sibling is not null; sibling = sibling.NextSibling)
                {
                    if (sibling is Element siblingElement && !ReferenceEquals(siblingElement, element))
                    {
                        AddSibling(siblingElement, subtree: (scope & InvalidationScope.AllSiblingSubtrees) != InvalidationScope.None);
                    }
                }
            }

            if ((scope & (InvalidationScope.Ancestors | InvalidationScope.AncestorSiblings)) != InvalidationScope.None)
            {
                for (var ancestor = element; ancestor is not null; ancestor = ancestor.ParentElement)
                {
                    _checks.Add(ancestor);
                    if ((scope & InvalidationScope.AncestorSiblings) != InvalidationScope.None && ancestor.ParentNode is { } ancestorParent)
                    {
                        for (var sibling = ancestorParent.FirstChild; sibling is not null; sibling = sibling.NextSibling)
                        {
                            if (sibling is Element siblingElement)
                            {
                                _checks.Add(siblingElement);
                            }
                        }
                    }
                }
            }

            return true;

            void AddSibling(Element sibling, bool subtree)
            {
                if (subtree)
                {
                    _subtrees.Add(sibling);
                }
                else
                {
                    _checks.Add(sibling);
                }
            }
        }

        /// <summary>
        /// Returns whether the insertion or the removal of an element (or a change of its id) may change the styles of other
        /// elements than its siblings and ancestors, beyond the selectors: the elements of the head of the document (the style
        /// sheets, the base URL, the color scheme), the root and body elements, and the listed form-associated elements and the
        /// forms (the validity of the forms and of the fieldsets, the default buttons, the labeled controls). The elements of
        /// SVG (the style elements, the elements referenced by the use elements) are not invalidated element by element.
        /// </summary>
        private static bool IsSensitiveNode(Element element)
        {
            if (element.IsHtmlNamespace)
            {
                return element.TagId is TagId.Base or TagId.Meta or TagId.Link or TagId.Style or TagId.Html or TagId.Head or TagId.Body
                    or TagId.Form or TagId.Label or TagId.Option or TagId.Optgroup
                    || (element is HtmlElement { IsListed: true });
            }

            return element.NamespaceUri != Namespaces.MathML;
        }

        /// <summary>
        /// Returns whether a change of the children of an element may change the styles of other elements than its children,
        /// its siblings and its ancestors, beyond the selectors: the contents of the select, option, datalist and details
        /// elements (they are rendered in pseudo-elements, their first summary, their selected options), the first legend of a
        /// fieldset, the default value of a textarea, the text of a style element, the sources of the media and picture
        /// elements, the areas of an image map and the frames of a frameset.
        /// </summary>
        private static bool IsSensitiveParent(Element element)
        {
            if (element.IsHtmlNamespace)
            {
                return element.TagId is TagId.Select or TagId.Option or TagId.Optgroup or TagId.Datalist or TagId.Details or TagId.Fieldset
                    or TagId.Textarea or TagId.Style or TagId.Object or TagId.Picture or TagId.Video or TagId.Audio or TagId.Map or TagId.Frameset
                    or TagId.Label;
            }

            return element.NamespaceUri != Namespaces.MathML;
        }

        private static bool SubtreeIsSensitive(Element root)
        {
            for (Node? node = root; node is not null; node = node.GetNextInTreeOrder(root))
            {
                if (node is Element element && IsSensitiveNode(element))
                    return true;
            }

            return false;
        }

        /// <summary>Returns the outermost inclusive ancestor of the element with auto directionality (https://html.spec.whatwg.org/multipage/dom.html#auto-directionality), or null.</summary>
        private static Element? FindAutoDirectionalityRoot(Element element)
        {
            Element? result = null;
            for (var ancestor = element; ancestor is not null; ancestor = ancestor.ParentElement)
            {
                if (!ancestor.IsHtmlNamespace)
                    continue;

                var dir = ancestor.GetAttributeValue("dir");
                if ((dir is not null && dir.Equals("auto", StringComparison.OrdinalIgnoreCase)) || (ancestor.TagId == TagId.Bdi && dir is null))
                {
                    result = ancestor;
                }
            }

            return result;
        }

        /// <summary>
        /// Discards the styles of the removed nodes, then checks the styles of the affected elements. When the styles may have
        /// transitions, the discarded styles are kept as the before-change styles, and the elements whose styles were discarded
        /// and the inserted elements are added to the styles computed again by the next style read (see <see cref="RecalculateStyles"/>).
        /// </summary>
        public void Apply(bool mayHaveTransitions)
        {
            // (a node inserted again, e.g. by moveBefore(), keeps its styles as the before-change styles; an inserted element
            // without styles transitions from its starting style, and starts its animations: Discard adds it to the styles
            // computed again)
            foreach (var node in _removed)
            {
                if (node is Element element)
                {
                    Discard(element, keepBeforeChange: mayHaveTransitions && element.IsConnected);
                }
            }

            foreach (var subtree in _subtrees)
            {
                CheckSubtree(subtree, mayHaveTransitions);
            }

            foreach (var element in _checks)
            {
                if (element.IsConnected && engine._styles.TryGetValue((element, null), out var style) && _checked.Add(element))
                {
                    Check(element, style, mayHaveTransitions);
                }
            }

            // The viewport-percentage lengths depend on the scrollbar gutters of the root and body elements (see GetViewportUnitSize)
            if (_rootChanged && engine._document.DocumentElement is { } root)
            {
                engine._viewportUnitGutters = null;
                Discard(root, mayHaveTransitions);
            }
        }

        private void CheckSubtree(Element root, bool mayHaveTransitions)
        {
            if (!root.IsConnected)
                return;

            var node = (Node?)root;
            while (node is not null)
            {
                if (node is Element element && engine._styles.TryGetValue((element, null), out var style))
                {
                    // (an element whose style is discarded is skipped with its descendants)
                    if ((_checked.Contains(element) || (_checked.Add(element) && !Check(element, style, mayHaveTransitions))) && element.FirstChild is { } firstChild)
                    {
                        node = firstChild;
                        continue;
                    }
                }

                node = NextSkippingChildren(node, root);
            }
        }

        private static Node? NextSkippingChildren(Node node, Node root)
        {
            for (var current = node; current is not null && !ReferenceEquals(current, root); current = current.ParentNode)
            {
                if (current.NextSibling is { } next)
                    return next;
            }

            return null;
        }

        /// <summary>
        /// Checks the style of an element: when its cascaded values are the same, the style is kept (the styles of its
        /// pseudo-elements are discarded); otherwise the styles of the element and of its descendants are discarded. Returns
        /// whether the styles were discarded.
        /// </summary>
        private bool Check(Element element, ComputedStyle style, bool mayHaveTransitions)
        {
            if (IsChanged(element, style))
            {
                Discard(element, mayHaveTransitions);
                return true;
            }

            // The descendants of an element inherit from some of its pseudo-elements (e.g. ::details-content)
            if (engine.HasInheritedPseudoElementStyles(element))
            {
                Discard(element, mayHaveTransitions);
                return true;
            }

            if (engine.DiscardPseudoElementStyles(element, mayHaveTransitions) && mayHaveTransitions)
            {
                engine.AddRecalculationRoot(element, subtree: false);
            }

            return false;
        }

        private bool IsChanged(Element element, ComputedStyle style)
        {
            if (_forced.Contains(element))
                return true;

            // The visited style of a link, the animations (their keyframes may reference the attributes), attr() and the custom
            // functions (their bodies may reference the attributes) are not compared
            if (style.VisitedLink is not null || !style.IsCascadeOfRules)
                return true;

            if (_attributesChanged.Contains(element) && (style.HasAnimationEffects || _usesFunctions || ReferencesAttributes(style)))
                return true;

            if (style.HasAnimationEffects && _positionsChanged.Contains(element))
                return true;

            var cascade = engine.Cascade(element, pseudoElement: null, style.Parent, out _);
            return !CascadesEqual(style.InitialCascade, cascade);
        }

        /// <summary>Discards the styles of an element, of its pseudo-elements and of its descendants.</summary>
        private void Discard(Element root, bool keepBeforeChange)
        {
            // (the boxes of the descendants are built again with the boxes of the element)
            engine.AddBoxTreeChange(root);
            if (keepBeforeChange && root.IsConnected)
            {
                engine.AddRecalculationRoot(root, subtree: true);
            }

            var node = (Node?)root;
            while (node is not null)
            {
                if (node is Element element && engine._styles.Remove((element, null), out var style))
                {
                    if (keepBeforeChange && element.IsConnected)
                    {
                        engine._beforeChangeStyles[(element, null)] = style;
                    }

                    engine.DiscardPseudoElementStyles(element, keepBeforeChange && element.IsConnected);
                    if (ReferenceEquals(element, engine._document.DocumentElement) || ReferenceEquals(element, engine._document.Body))
                    {
                        _rootChanged |= element.IsConnected;
                    }

                    if (element.FirstChild is { } firstChild)
                    {
                        node = firstChild;
                        continue;
                    }
                }

                // (the descendants of an element without style have no style: the styles of the ancestors are computed first)
                node = NextSkippingChildren(node, root);
            }
        }
    }
}
