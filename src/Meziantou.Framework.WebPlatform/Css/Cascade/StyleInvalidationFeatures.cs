using Meziantou.Framework.WebPlatform.Css.Selectors;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The elements whose styles may change when a feature of an element (a class, an id, an attribute, a pseudo-class) or the
/// children of an element change, relative to that element (the invalidation sets of the browsers, see
/// <see cref="StyleInvalidationFeatures"/>).
/// </summary>
[Flags]
internal enum InvalidationScope : ushort
{
    None = 0,

    /// <summary>The element.</summary>
    Self = 1 << 0,

    /// <summary>The element and its descendants.</summary>
    Subtree = 1 << 1,

    /// <summary>The following siblings of the element.</summary>
    Siblings = 1 << 2,

    /// <summary>The following siblings of the element and their descendants.</summary>
    SiblingSubtrees = 1 << 3,

    /// <summary>The siblings of the element (preceding and following).</summary>
    AllSiblings = 1 << 4,

    /// <summary>The siblings of the element (preceding and following) and their descendants.</summary>
    AllSiblingSubtrees = 1 << 5,

    /// <summary>The element and its ancestors (the anchors of the relative selectors of <c>:has()</c>).</summary>
    Ancestors = 1 << 6,

    /// <summary>The siblings of the element and of its ancestors (the anchors of the relative selectors of <c>:has()</c> that start with a sibling combinator).</summary>
    AncestorSiblings = 1 << 7,

    /// <summary>All the elements of the document.</summary>
    Global = 1 << 8,
}

/// <summary>
/// The features of the selectors of a rule set that the styles depend on, with the elements whose styles may change when they
/// change (https://chromium.googlesource.com/chromium/src/+/main/third_party/blink/renderer/core/css/invalidation/README.md):
/// the classes, the ids, the attribute names and the pseudo-classes of the compound selectors, and the structural
/// dependencies (the positions of the elements among their siblings, the sibling combinators, <c>:empty</c> and
/// <c>:has()</c>). A feature in the subject compound selector only affects the element; a feature in a compound selector
/// followed by a descendant or child combinator affects its subtree, followed by a sibling combinator its following
/// siblings, and in the argument of <c>:has()</c> the anchors (the ancestors, and their siblings for the sibling relative
/// selectors). The scopes are supersets: the styles of the elements in a scope are checked, and computed again only when
/// their cascaded values changed.
/// </summary>
internal sealed class StyleInvalidationFeatures
{
    private readonly Dictionary<string, InvalidationScope> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InvalidationScope> _ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InvalidationScope> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly InvalidationScope[] _pseudoClasses = new InvalidationScope[(int)PseudoClass.TargetAfter + 1];
    private List<(string Prefix, InvalidationScope Scope)>? _classPrefixes;

    /// <summary>The scope of a change of the position of an element among its siblings (<c>:first-child</c>, <c>:nth-child()</c>...), relative to each sibling.</summary>
    public InvalidationScope Positional { get; private set; }

    /// <summary>The scope of a change of the siblings of an element through a sibling combinator, relative to each sibling.</summary>
    public InvalidationScope SiblingCombinators { get; private set; }

    /// <summary>The scope of a change of the children of an element for <c>:empty</c>, relative to the element.</summary>
    public InvalidationScope Empty { get; private set; }

    /// <summary>The scope of a change of the children of an element for <c>:has()</c>, relative to the element.</summary>
    public InvalidationScope Has { get; private set; }

    /// <summary>Whether a selector uses <c>:target</c> or <c>:target-within</c> (the target element depends on the ids of the elements).</summary>
    public bool UsesTarget { get; private set; }

    /// <summary>Whether a declaration uses a tree-counting function (<c>sibling-index()</c>, <c>sibling-count()</c>), whose value depends on the position of the element.</summary>
    public bool UsesTreeCountingFunctions { get; private set; }

    /// <summary>Returns the scope of a change of a class of an element.</summary>
    public InvalidationScope GetClassScope(string className)
    {
        var scope = _classes.GetValueOrDefault(className);
        if (_classPrefixes is not null)
        {
            foreach (var (prefix, prefixScope) in _classPrefixes)
            {
                if (className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    scope |= prefixScope;
                }
            }
        }

        return scope;
    }

    /// <summary>Returns the scope of a change of the id of an element.</summary>
    public InvalidationScope GetIdScope(string id) => _ids.GetValueOrDefault(id);

    /// <summary>Returns the scope of a change of an attribute of an element (by local name).</summary>
    public InvalidationScope GetAttributeScope(string localName) => _attributes.GetValueOrDefault(localName);

    /// <summary>Returns the scope of a change of the state of an element that a pseudo-class matches.</summary>
    public InvalidationScope GetPseudoClassScope(PseudoClass pseudoClass) => _pseudoClasses[(int)pseudoClass];

    /// <summary>Collects the features of the rules of a rule set.</summary>
    public static StyleInvalidationFeatures Create(CascadeRuleSet ruleSet)
    {
        var features = new StyleInvalidationFeatures();
        foreach (var rule in ruleSet.EnumerateRules())
        {
            features.AddSelector(rule.Selector, InvalidationScope.Self);
            if (rule.SlottedSelector is { } slotted)
            {
                features.AddSelectorList(slotted, InvalidationScope.Self);
            }

            for (var scope = rule.Scope; scope is not null; scope = scope.Parent)
            {
                // A change of the scoping roots or of the scoping limits changes the scope of the elements of their subtrees
                if (scope.Start is { } start)
                {
                    features.AddSelectorList(start, InvalidationScope.Subtree);
                }

                if (scope.End is { } end)
                {
                    features.AddSelectorList(end, InvalidationScope.Subtree);
                }
            }

            if (!features.UsesTreeCountingFunctions && (UsesTreeCountingFunction(rule.Declarations) || UsesTreeCountingFunction(rule.Containers)))
            {
                features.UsesTreeCountingFunctions = true;
            }
        }

        return features;
    }

    /// <summary>Returns whether a declaration uses a tree-counting function (https://drafts.csswg.org/css-values-5/#tree-counting).</summary>
    internal static bool UsesTreeCountingFunction(List<Om.CssDeclarationEntry> declarations)
    {
        foreach (var declaration in declarations)
        {
            var value = declaration.Value;
            if (value.Value.Contains("sibling-", StringComparison.OrdinalIgnoreCase) || (value.ShorthandValue?.Contains("sibling-", StringComparison.OrdinalIgnoreCase) ?? false))
                return true;
        }

        return false;
    }

    /// <summary>Returns whether a condition of the @container rules of a rule uses a tree-counting function (the position of the container).</summary>
    private static bool UsesTreeCountingFunction(CascadeContainerQuery? containers)
    {
        for (var query = containers; query is not null; query = query.Parent)
        {
            foreach (var condition in query.Conditions)
            {
                if (condition.QueryText.Contains("sibling-", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private void AddSelectorList(SelectorList list, InvalidationScope scope)
    {
        foreach (var selector in list.Selectors)
        {
            AddSelector(selector, scope);
        }
    }

    /// <summary>Adds the features of a complex selector whose subject is affected with <paramref name="subjectScope"/> by a change of its match.</summary>
    private void AddSelector(ComplexSelector selector, InvalidationScope subjectScope)
    {
        var last = selector.Compounds.Length - 1;
        for (var i = 0; i <= last; i++)
        {
            var position = Compose(subjectScope, GetPositionScope(selector, i));
            if (i < last && selector.Combinators[i] is Combinator.NextSibling or Combinator.SubsequentSibling)
            {
                // A change of the siblings of the elements matched by the compound selector on its right
                SiblingCombinators |= Compose(subjectScope, GetPositionScope(selector, i + 1));
            }

            foreach (var simple in selector.Compounds[i].Selectors)
            {
                AddSimpleSelector(simple, position);
            }
        }
    }

    /// <summary>
    /// Returns the elements whose match of the subject of a complex selector may change when the match of one of its compound
    /// selectors changes for an element, relative to that element.
    /// </summary>
    private static InvalidationScope GetPositionScope(ComplexSelector selector, int index)
    {
        var last = selector.Compounds.Length - 1;
        if (index == last)
            return InvalidationScope.Self;

        if (selector.Combinators[index] is Combinator.Descendant or Combinator.Child)
            return InvalidationScope.Subtree;

        for (var i = index + 1; i < last; i++)
        {
            if (selector.Combinators[i] is Combinator.Descendant or Combinator.Child)
                return InvalidationScope.SiblingSubtrees;
        }

        return InvalidationScope.Siblings;
    }

    private void AddSimpleSelector(SimpleSelector simple, InvalidationScope scope)
    {
        switch (simple)
        {
            case ClassSelector classSelector:
                Add(_classes, classSelector.ClassName, scope);
                break;

            case ClassPrefixSelector prefixSelector:
                (_classPrefixes ??= []).Add((prefixSelector.Prefix, scope));
                break;

            case IdSelector idSelector:
                Add(_ids, idSelector.Id, scope);
                break;

            case AttributeSelector attributeSelector:
                Add(_attributes, attributeSelector.LowercaseName, scope);
                break;

            case NestingSelector { Parent: { } parent }:
                AddSelectorList(parent, scope);
                break;

            case PseudoElementSelector { Argument: { } argument }:
                AddSelectorList(argument, scope);
                break;

            case PseudoClassSelector pseudoClass:
                AddPseudoClass(pseudoClass, scope);
                break;
        }
    }

    private void AddPseudoClass(PseudoClassSelector selector, InvalidationScope scope)
    {
        _pseudoClasses[(int)selector.Kind] |= scope;
        switch (selector.Kind)
        {
            case PseudoClass.Has:
            {
                // The anchors of the relative selectors are the ancestors of their subjects (and the preceding siblings of the
                // subjects and of their ancestors for the sibling relative selectors): a change of the children of an
                // element changes the matches of its ancestors and of their siblings
                var anchors = Compose(scope, InvalidationScope.Ancestors | InvalidationScope.AncestorSiblings);
                Has |= anchors;
                foreach (var relative in selector.Argument!.Selectors)
                {
                    var anchorScope = relative.RelativeCombinator is null or Combinator.Descendant or Combinator.Child ? InvalidationScope.Ancestors : InvalidationScope.Ancestors | InvalidationScope.AncestorSiblings;
                    AddSelector(relative, Compose(scope, anchorScope));
                }

                break;
            }

            case PseudoClass.Is or PseudoClass.Where or PseudoClass.Not or PseudoClass.HostFunction or PseudoClass.HostContext:
                if (selector.Argument is { } argument)
                {
                    // (the features of :host-context() are in the ancestors of the shadow host: the styles of the documents
                    // with shadow trees are not invalidated by element, see StyleEngine.Invalidation.cs)
                    AddSelectorList(argument, scope);
                }

                break;

            case PseudoClass.NthChild or PseudoClass.NthLastChild:
                Positional |= scope;
                if (selector.Argument is { } filter)
                {
                    // The elements counted by "of S": a change of their match changes the positions of their siblings
                    AddSelectorList(filter, Compose(scope, InvalidationScope.Self | InvalidationScope.AllSiblings));
                }

                break;

            case PseudoClass.FirstChild or PseudoClass.LastChild or PseudoClass.OnlyChild or PseudoClass.FirstOfType or PseudoClass.LastOfType
                or PseudoClass.OnlyOfType or PseudoClass.NthOfType or PseudoClass.NthLastOfType:
                Positional |= scope;
                break;

            case PseudoClass.Empty:
                Empty |= scope;
                break;

            case PseudoClass.Target or PseudoClass.TargetWithin:
                UsesTarget = true;
                break;
        }
    }

    private static void Add(Dictionary<string, InvalidationScope> map, string key, InvalidationScope scope)
    {
        map.TryGetValue(key, out var existing);
        map[key] = existing | scope;
    }

    /// <summary>
    /// Returns the elements affected by a change of an element through two relations: <paramref name="inner"/> gives the elements
    /// whose match changes relative to the changed element, <paramref name="outer"/> the elements affected by a change of the
    /// match relative to each of them (e.g. a feature of the argument of <c>:is()</c> in a compound selector followed by a
    /// descendant combinator). The result is a superset.
    /// </summary>
    internal static InvalidationScope Compose(InvalidationScope outer, InvalidationScope inner)
    {
        var result = InvalidationScope.None;
        for (var o = 1; o <= (int)InvalidationScope.Global; o <<= 1)
        {
            if (((InvalidationScope)o & outer) == InvalidationScope.None)
                continue;

            for (var i = 1; i <= (int)InvalidationScope.Global; i <<= 1)
            {
                if (((InvalidationScope)i & inner) != InvalidationScope.None)
                {
                    result |= Compose((InvalidationScope)o, (InvalidationScope)i);
                }
            }
        }

        return result;

        static InvalidationScope Compose(InvalidationScope outer, InvalidationScope inner)
        {
            const InvalidationScope Side = InvalidationScope.Siblings | InvalidationScope.SiblingSubtrees | InvalidationScope.AllSiblings | InvalidationScope.AllSiblingSubtrees;
            const InvalidationScope Up = InvalidationScope.Ancestors | InvalidationScope.AncestorSiblings;
            const InvalidationScope WithSubtrees = InvalidationScope.Subtree | InvalidationScope.SiblingSubtrees | InvalidationScope.AllSiblingSubtrees;
            if (outer == InvalidationScope.Global || inner == InvalidationScope.Global)
                return InvalidationScope.Global;

            if (inner == InvalidationScope.Self)
                return outer;

            if (outer == InvalidationScope.Self)
                return inner;

            // The elements of the subtree: the outer relation of an element of the subtree stays in the subtree, or is the
            // outer relation of the element itself (its siblings, its ancestors)
            if (inner == InvalidationScope.Subtree)
                return InvalidationScope.Subtree | outer;

            if ((inner & Side) != InvalidationScope.None)
            {
                var all = (inner & (InvalidationScope.AllSiblings | InvalidationScope.AllSiblingSubtrees)) != InvalidationScope.None;
                var subtrees = (inner & (InvalidationScope.SiblingSubtrees | InvalidationScope.AllSiblingSubtrees)) != InvalidationScope.None;
                if (outer == InvalidationScope.Subtree)
                    return all ? InvalidationScope.AllSiblingSubtrees : InvalidationScope.SiblingSubtrees;

                if ((outer & Side) != InvalidationScope.None)
                    return subtrees || (outer & WithSubtrees) != InvalidationScope.None ? InvalidationScope.AllSiblingSubtrees : InvalidationScope.AllSiblings;

                // The ancestors of the siblings are the ancestors of the element
                return (subtrees ? InvalidationScope.AllSiblingSubtrees : InvalidationScope.AllSiblings) | Up;
            }

            // The ancestors of the element (and their siblings)
            if ((outer & WithSubtrees) != InvalidationScope.None)
                return InvalidationScope.Global;

            return Up;
        }
    }
}
