using System.Text;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>The namespace context of a selector (https://drafts.csswg.org/css-namespaces/). Without @namespace rules, only the "*" and empty prefixes are valid.</summary>
internal sealed class SelectorNamespaceContext
{
    public static SelectorNamespaceContext Empty { get; } = new();

    public string? DefaultNamespace { get; init; }

    public Func<string, string?>? ResolvePrefix { get; init; }
}

/// <summary>Parses selectors (https://drafts.csswg.org/selectors-4/#grammar).</summary>
internal sealed class SelectorParser
{
    private static readonly FrozenDictionary<string, PseudoClass> PseudoClasses = new Dictionary<string, PseudoClass>(StringComparer.Ordinal)
    {
        ["root"] = PseudoClass.Root,
        ["empty"] = PseudoClass.Empty,
        ["first-child"] = PseudoClass.FirstChild,
        ["last-child"] = PseudoClass.LastChild,
        ["only-child"] = PseudoClass.OnlyChild,
        ["first-of-type"] = PseudoClass.FirstOfType,
        ["last-of-type"] = PseudoClass.LastOfType,
        ["only-of-type"] = PseudoClass.OnlyOfType,
        ["scope"] = PseudoClass.Scope,
        ["host"] = PseudoClass.Host,
        ["has-slotted"] = PseudoClass.HasSlotted,
        ["any-link"] = PseudoClass.AnyLink,
        ["link"] = PseudoClass.Link,
        ["visited"] = PseudoClass.Visited,
        ["target"] = PseudoClass.Target,
        ["target-within"] = PseudoClass.TargetWithin,
        ["target-current"] = PseudoClass.TargetCurrent,
        ["target-before"] = PseudoClass.TargetBefore,
        ["target-after"] = PseudoClass.TargetAfter,
        ["hover"] = PseudoClass.Hover,
        ["active"] = PseudoClass.Active,
        ["focus"] = PseudoClass.Focus,
        ["focus-visible"] = PseudoClass.FocusVisible,
        ["focus-within"] = PseudoClass.FocusWithin,
        ["defined"] = PseudoClass.Defined,
        ["enabled"] = PseudoClass.Enabled,
        ["disabled"] = PseudoClass.Disabled,
        ["read-only"] = PseudoClass.ReadOnly,
        ["read-write"] = PseudoClass.ReadWrite,
        ["placeholder-shown"] = PseudoClass.PlaceholderShown,
        ["default"] = PseudoClass.Default,
        ["checked"] = PseudoClass.Checked,
        ["indeterminate"] = PseudoClass.Indeterminate,
        ["valid"] = PseudoClass.Valid,
        ["invalid"] = PseudoClass.Invalid,
        ["in-range"] = PseudoClass.InRange,
        ["out-of-range"] = PseudoClass.OutOfRange,
        ["required"] = PseudoClass.Required,
        ["optional"] = PseudoClass.Optional,
        ["user-valid"] = PseudoClass.UserValid,
        ["user-invalid"] = PseudoClass.UserInvalid,
        ["autofill"] = PseudoClass.Autofill,
        ["-webkit-autofill"] = PseudoClass.Autofill, // legacy selector alias
        ["open"] = PseudoClass.Open,
        ["modal"] = PseudoClass.Modal,
        ["popover-open"] = PseudoClass.PopoverOpen,
        ["fullscreen"] = PseudoClass.Fullscreen,
        ["picture-in-picture"] = PseudoClass.PictureInPicture,
        ["heading"] = PseudoClass.Heading,
        ["playing"] = PseudoClass.Playing,
        ["paused"] = PseudoClass.Paused,
        ["seeking"] = PseudoClass.Seeking,
        ["buffering"] = PseudoClass.Buffering,
        ["stalled"] = PseudoClass.Stalled,
        ["muted"] = PseudoClass.Muted,
        ["volume-locked"] = PseudoClass.VolumeLocked,
        ["active-view-transition"] = PseudoClass.ActiveViewTransition,
        ["current"] = PseudoClass.Current,
        ["future"] = PseudoClass.Future,
        ["past"] = PseudoClass.Past,
        ["xr-overlay"] = PseudoClass.XrOverlay,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> PseudoElements = new[]
    {
        "after", "before", "first-line", "first-letter", "marker", "placeholder", "selection", "backdrop",
        "file-selector-button", "details-content", "target-text", "spelling-error", "grammar-error", "cue", "cue-region",
        "checkmark", "picker-icon", "slider-track", "slider-fill", "slider-thumb", "color-swatch", "column", "scroll-marker", "scroll-marker-group", "search-text", "view-transition",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FunctionalPseudoElements = new[]
    {
        "slotted", "part", "highlight", "picker", "cue", "cue-region", "scroll-button",
        "view-transition-group", "view-transition-image-pair", "view-transition-old", "view-transition-new", "view-transition-group-children",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Pseudo-elements that can follow ::slotted() (tree-abiding pseudo-elements, https://drafts.csswg.org/css-shadow-1/#slotted-pseudo).</summary>
    private static readonly FrozenSet<string> SubPseudoElements = new[] { "before", "after", "marker", "placeholder", "file-selector-button", "details-content", "picker", "picker-icon", "checkmark", "slider-track", "slider-fill", "slider-thumb", "color-swatch" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly SelectorNamespaceContext _namespaces;

    /// <summary>The preprocessed source of the component values (the invalid selectors of the forgiving selector lists are serialized as written), or null.</summary>
    private readonly string? _source;
    private int _hasDepth;

    /// <summary>The depth of the selector list being parsed: 1 for the top-level list, more for the arguments of the pseudo-classes.</summary>
    private int _listDepth;

    /// <summary>Whether the nesting selector implied by a relative nested selector is not serialized.</summary>
    private bool _implicitNesting;

    /// <summary>Whether the forgiving selector lists (:is(), :where()) are parsed as selector lists (in the selector() function of @supports).</summary>
    private bool _unforgiving;

    /// <summary>
    /// Whether the selectors of the logical pseudo-classes being parsed must be compound selectors: they are in the argument
    /// of :host(), :host-context() or ::slotted(), which is a compound selector (https://github.com/w3c/csswg-drafts/issues/5093).
    /// </summary>
    private bool _compoundOnly;

    /// <summary>
    /// The pseudo-element followed by the logical pseudo-class whose arguments are being parsed (e.g. ::part() in
    /// "::part(label):is(:hover)"): the arguments can only contain the pseudo-classes allowed after it.
    /// </summary>
    private PseudoElementSelector? _afterPseudoElement;

    // The lists the selectors are built in, shared by the nested selectors (e.g. the arguments of :is()): each one uses the end
    // of the lists, and removes its items before returning
    private readonly List<ComplexSelector> _complexBuffer = [];
    private readonly List<CompoundSelector> _compoundBuffer = [];
    private readonly List<Combinator> _combinatorBuffer = [];
    private readonly List<SimpleSelector> _simpleBuffer = [];

    private SelectorParser(SelectorNamespaceContext namespaces, string? source = null)
    {
        _namespaces = namespaces;
        _source = source;
    }

    /// <summary>https://drafts.csswg.org/selectors-4/#parse-a-selector: returns null on failure.</summary>
    public static SelectorList? ParseSelector(string source, SelectorNamespaceContext? namespaces = null)
    {
        var values = CssParser.ParseListOfComponentValues(source, out var preprocessed);
        return new SelectorParser(namespaces ?? SelectorNamespaceContext.Empty, preprocessed).ParseList(values, ListKind.Normal);
    }

    /// <summary>
    /// Parses the selector of the selector() function of @supports (https://drafts.csswg.org/css-conditional-4/#support-definition-ext):
    /// the forgiving selector lists are parsed as regular selector lists, so that an invalid selector in :is() or :where() is
    /// not supported.
    /// </summary>
    public static SelectorList? ParseSupportsSelector(List<CssComponentValue> values, SelectorNamespaceContext? namespaces = null) =>
        new SelectorParser(namespaces ?? SelectorNamespaceContext.Empty) { _unforgiving = true }.ParseList(values, ListKind.Normal);

    /// <summary>
    /// Parses a selector list from component values (e.g. the prelude of a style rule): returns null on failure. The source is
    /// the preprocessed text of the component values, when it is available.
    /// </summary>
    public static SelectorList? ParseSelector(List<CssComponentValue> values, SelectorNamespaceContext? namespaces = null, string? source = null) =>
        new SelectorParser(namespaces ?? SelectorNamespaceContext.Empty, source).ParseList(values, ListKind.Normal);

    /// <summary>
    /// Parses the selector of a nested style rule (https://drafts.csswg.org/css-nesting/#syntax): a relative selector list
    /// whose leading combinator is optional.
    /// </summary>
    public static SelectorList? ParseNestedSelector(List<CssComponentValue> values, SelectorNamespaceContext? namespaces = null, string? source = null) =>
        new SelectorParser(namespaces ?? SelectorNamespaceContext.Empty, source).ParseList(values, ListKind.Nested);

    /// <summary>
    /// Parses the selector of a scoped style rule (https://drafts.csswg.org/css-cascade-6/#scoped-rules): like a nested
    /// selector, but a selector containing :scope is not relative to the scoping root.
    /// </summary>
    public static SelectorList? ParseScopedSelector(List<CssComponentValue> values, SelectorNamespaceContext? namespaces = null, string? source = null) =>
        new SelectorParser(namespaces ?? SelectorNamespaceContext.Empty, source).ParseList(values, ListKind.Scoped);

    /// <summary>
    /// Parses the &lt;scope-start&gt; selector of a @scope rule nested in a style rule or in another @scope rule
    /// (https://drafts.csswg.org/css-cascade-6/#scope-syntax): it is relative to the nesting selector (the parent style
    /// rule) or to :scope (the scoping root of the parent @scope rule); the implied selector is not serialized.
    /// </summary>
    public static SelectorList? ParseNestedScopeStart(List<CssComponentValue> values, bool scoped, SelectorNamespaceContext? namespaces = null, string? source = null) =>
        new SelectorParser(namespaces ?? SelectorNamespaceContext.Empty, source) { _implicitNesting = true }.ParseList(values, scoped ? ListKind.Scoped : ListKind.Nested);

    /// <summary>https://drafts.csswg.org/selectors-4/#parse-a-relative-selector: returns null on failure.</summary>
    public static SelectorList? ParseRelativeSelector(string source, SelectorNamespaceContext? namespaces = null)
    {
        var values = CssParser.ParseListOfComponentValues(source, out var preprocessed);
        return new SelectorParser(namespaces ?? SelectorNamespaceContext.Empty, preprocessed).ParseList(values, ListKind.Relative);
    }

    private enum ListKind
    {
        /// <summary>&lt;selector-list&gt;: complex selectors, pseudo-elements allowed.</summary>
        Normal,

        /// <summary>&lt;complex-real-selector-list&gt;: no pseudo-elements.</summary>
        Real,

        /// <summary>&lt;forgiving-selector-list&gt;: invalid selectors are dropped.</summary>
        Forgiving,

        /// <summary>&lt;relative-selector-list&gt;.</summary>
        Relative,

        /// <summary>The selector of a nested style rule: relative selectors without implied descendant combinator.</summary>
        Nested,

        /// <summary>The selector of a scoped style rule: like <see cref="Nested"/>, :scope is like the nesting selector.</summary>
        Scoped,
    }

    private static bool IsWhitespace(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Whitespace };

    private static bool IsDelim(CssComponentValue? value, char c) => value is CssPreservedToken token && token.Token.IsDelim(c);

    private static bool IsToken(CssComponentValue? value, CssTokenType type) => value is CssPreservedToken token && token.Type == type;

    private static string? GetIdent(CssComponentValue? value) => value is CssPreservedToken { Type: CssTokenType.Ident } token ? token.Token.Value : null;

    private static CssComponentValue? At(List<CssComponentValue> values, int index) => index < values.Count ? values[index] : null;

    private static CssComponentValue? At(ReadOnlySpan<CssComponentValue> values, int index) => index < values.Length ? values[index] : null;

    private static void SkipWhitespace(ReadOnlySpan<CssComponentValue> values, ref int index)
    {
        while (index < values.Length && IsWhitespace(values[index]))
        {
            index++;
        }
    }

    private static void SkipWhitespace(List<CssComponentValue> values, ref int index)
    {
        while (index < values.Count && IsWhitespace(values[index]))
        {
            index++;
        }
    }

    private static List<List<CssComponentValue>> SplitByComma(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (IsToken(value, CssTokenType.Comma))
            {
                result.Add([]);
            }
            else
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    private SelectorList? ParseList(List<CssComponentValue> values, ListKind kind)
    {
        _listDepth++;
        try
        {
            return ParseListCore(values, kind);
        }
        finally
        {
            _listDepth--;
        }
    }

    private SelectorList? ParseListCore(List<CssComponentValue> values, ListKind kind)
    {
        var resultStart = _complexBuffer.Count;
        try
        {
            List<UnparsedSelector>? unparsed = null;
            var span = CollectionsMarshal.AsSpan(values);
            var partStart = 0;
            var partIndex = 0;
            for (var position = 0; position <= span.Length; position++)
            {
                // The selectors are the component values between the commas
                if (position < span.Length && !IsToken(span[position], CssTokenType.Comma))
                    continue;

                var part = span[partStart..position];
                var i = partIndex++;
                partStart = position + 1;
                var nested = kind is ListKind.Nested or ListKind.Scoped;
                var selector = ParseComplex(part, relative: kind is ListKind.Relative || nested, allowPseudoElements: kind is ListKind.Normal || nested, impliedCombinator: !nested);
                if (selector is not null && nested)
                {
                    selector = Absolutize(selector, scoped: kind == ListKind.Scoped, implicitNesting: _implicitNesting || kind == ListKind.Scoped);
                }

                if (selector is not null && kind is ListKind.Forgiving or ListKind.Real && !IsAllowedInContext(selector))
                {
                    selector = null;
                }

                if (selector is null)
                {
                    // The invalid selector never matches, but it is serialized as written. It must be an <any-value>: the
                    // bad tokens and the unmatched blocks make the list invalid (https://drafts.csswg.org/css-syntax-3/#typedef-any-value).
                    if (kind == ListKind.Forgiving && !ContainsBadToken(part))
                    {
                        (unparsed ??= []).Add(new UnparsedSelector(i, GetText(part), ContainsNestingDelim(part)));
                        continue;
                    }

                    return null;
                }

                _complexBuffer.Add(selector);
            }

            return new SelectorList(CollectionsMarshal.AsSpan(_complexBuffer)[resultStart..].ToArray(), unparsed?.ToArray());
        }
        finally
        {
            _complexBuffer.RemoveRange(resultStart, _complexBuffer.Count - resultStart);
        }
    }

    /// <summary>
    /// Returns whether a selector of the argument of a logical pseudo-class is allowed where the pseudo-class is: a compound
    /// selector in the argument of :host(), :host-context() and ::slotted(), and only the pseudo-classes allowed after the
    /// pseudo-element when the pseudo-class follows a pseudo-element.
    /// </summary>
    private bool IsAllowedInContext(ComplexSelector selector)
    {
        if (_compoundOnly && selector.Compounds.Length > 1)
            return false;

        if (_afterPseudoElement is { } pseudoElement)
        {
            if (selector.Compounds.Length > 1)
                return false;

            foreach (var simple in selector.Compounds[0].Selectors)
            {
                if (simple is not PseudoClassSelector pseudoClass || !IsPseudoClassAllowedAfter(pseudoElement, pseudoClass))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Returns the text of component values as written, without the leading and trailing whitespace.</summary>
    private string GetText(ReadOnlySpan<CssComponentValue> values)
    {
        var start = 0;
        SkipWhitespace(values, ref start);
        if (start >= values.Length)
            return "";

        var end = values.Length - 1;
        while (IsWhitespace(values[end]))
        {
            end--;
        }

        var from = values[start].Start;
        var to = values[end].End;
        if (_source is null || from < 0 || to > _source.Length || from > to)
            return Values.CssSerializer.SerializeValue([.. values[start..(end + 1)]], property: null);

        return _source[from..to];
    }

    private static bool ContainsBadToken(ReadOnlySpan<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.BadString or CssTokenType.BadUrl }:
                case CssFunction { Unterminated: true }:
                case CssSimpleBlock { Unterminated: true }:
                case CssFunction function when ContainsBadToken(CollectionsMarshal.AsSpan(function.Value)):
                case CssSimpleBlock block when ContainsBadToken(CollectionsMarshal.AsSpan(block.Value)):
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsNestingDelim(ReadOnlySpan<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken token when token.Token.IsDelim('&'):
                case CssFunction function when ContainsNestingDelim(CollectionsMarshal.AsSpan(function.Value)):
                case CssSimpleBlock block when ContainsNestingDelim(CollectionsMarshal.AsSpan(block.Value)):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A nested selector that does not contain the nesting selector is relative to it
    /// (https://drafts.csswg.org/css-nesting/#syntax): ".a" is "&amp; .a" and "&gt; .a" is "&amp; &gt; .a". A nested
    /// selector starting with a combinator is always relative: "&gt; &amp; .a" is "&amp; &gt; &amp; .a".
    /// </summary>
    private static ComplexSelector Absolutize(ComplexSelector selector, bool scoped, bool implicitNesting)
    {
        if ((scoped || selector.RelativeCombinator is null) && ContainsNestingSelector(selector, scoped))
            return selector;

        var compounds = new CompoundSelector[selector.Compounds.Length + 1];
        compounds[0] = new CompoundSelector([new NestingSelector { Implicit = implicitNesting }]);
        selector.Compounds.CopyTo(compounds, 1);
        var combinators = new Combinator[selector.Combinators.Length + 1];
        combinators[0] = selector.RelativeCombinator ?? Combinator.Descendant;
        selector.Combinators.CopyTo(combinators, 1);
        return new ComplexSelector(compounds, combinators, relativeCombinator: null);
    }

    private static bool ContainsNestingSelector(ComplexSelector selector, bool orScope = false)
    {
        foreach (var compound in selector.Compounds)
        {
            foreach (var simple in compound.Selectors)
            {
                if (simple is NestingSelector || (orScope && simple is PseudoClassSelector { Kind: PseudoClass.Scope }))
                    return true;

                if (simple is PseudoClassSelector { Argument: { } argument } && (argument.Selectors.Any(s => ContainsNestingSelector(s, orScope)) || argument.UnparsedSelectors.Any(s => s.ContainsNestingSelector)))
                    return true;
            }
        }

        return false;
    }

    private ComplexSelector? ParseComplex(ReadOnlySpan<CssComponentValue> values, bool relative, bool allowPseudoElements, bool impliedCombinator = true)
    {
        var index = 0;
        SkipWhitespace(values, ref index);
        if (index >= values.Length)
            return null; // An empty selector is invalid

        Combinator? relativeCombinator = null;
        if (relative)
        {
            relativeCombinator = TryParseCombinator(values, ref index) ?? (impliedCombinator ? Combinator.Descendant : null);
            SkipWhitespace(values, ref index);
        }

        var compoundStart = _compoundBuffer.Count;
        var combinatorStart = _combinatorBuffer.Count;
        try
        {
            while (true)
            {
                var compound = ParseCompound(values, ref index, allowPseudoElements);
                if (compound is null)
                    return null;

                _compoundBuffer.Add(compound);
                var hadWhitespace = index < values.Length && IsWhitespace(values[index]);
                SkipWhitespace(values, ref index);
                if (index >= values.Length)
                    break;

                if (compound.HasPseudoElement)
                    return null; // Pseudo-elements must be in the last compound selector

                var combinator = TryParseCombinator(values, ref index);
                if (combinator is null)
                {
                    if (!hadWhitespace)
                        return null;

                    combinator = Combinator.Descendant;
                }

                SkipWhitespace(values, ref index);
                if (index >= values.Length)
                    return null;

                // In the argument of a pseudo-class, the default namespace only spares the compound selector of the subject
                // (https://drafts.csswg.org/selectors-4/#matches): e.g. .container in :is(.container .a)
                if (_listDepth > 1 && _namespaces.DefaultNamespace is { } defaultNamespace && compound.Selectors[0] is not TypeSelector && !ContainsNestingSelector(compound))
                {
                    _compoundBuffer[^1] = new CompoundSelector([new TypeSelector(GetNamespaceConstraint(defaultNamespace), localName: null) { Implicit = true }, .. compound.Selectors]);
                }

                _combinatorBuffer.Add(combinator.Value);
            }

            return new ComplexSelector(CollectionsMarshal.AsSpan(_compoundBuffer)[compoundStart..].ToArray(), CollectionsMarshal.AsSpan(_combinatorBuffer)[combinatorStart..].ToArray(), relativeCombinator);
        }
        finally
        {
            _compoundBuffer.RemoveRange(compoundStart, _compoundBuffer.Count - compoundStart);
            _combinatorBuffer.RemoveRange(combinatorStart, _combinatorBuffer.Count - combinatorStart);
        }
    }

    private static bool ContainsNestingSelector(CompoundSelector compound)
    {
        foreach (var selector in compound.Selectors)
        {
            if (selector is NestingSelector)
                return true;
        }

        return false;
    }

    private static Combinator? TryParseCombinator(ReadOnlySpan<CssComponentValue> values, ref int index)
    {
        var value = At(values, index);
        Combinator? result = value switch
        {
            _ when IsDelim(value, '>') => Combinator.Child,
            _ when IsDelim(value, '+') => Combinator.NextSibling,
            _ when IsDelim(value, '~') => Combinator.SubsequentSibling,
            _ => null,
        };

        if (result is not null)
        {
            index++;
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Compound selectors
    // ----------------------------------------------------------------------------------------------------------------

    private CompoundSelector? ParseCompound(ReadOnlySpan<CssComponentValue> values, ref int index, bool allowPseudoElements)
    {
        var start = _simpleBuffer.Count;
        try
        {
            return ParseCompoundCore(values, ref index, allowPseudoElements, start);
        }
        finally
        {
            _simpleBuffer.RemoveRange(start, _simpleBuffer.Count - start);
        }
    }

    private CompoundSelector? ParseCompoundCore(ReadOnlySpan<CssComponentValue> values, ref int index, bool allowPseudoElements, int start)
    {
        var selectors = _simpleBuffer;
        if (TryParseTypeSelector(values, ref index, out var typeSelector, out var failed))
        {
            selectors.Add(typeSelector);
        }
        else if (failed)
        {
            return null;
        }

        PseudoElementSelector? pseudoElement = null;
        while (index < values.Length)
        {
            var value = values[index];
            if (value is CssPreservedToken { Type: CssTokenType.Hash } hash)
            {
                if (!hash.Token.IsIdHash || pseudoElement is not null)
                    return null;

                selectors.Add(new IdSelector(hash.Token.Value));
                index++;
            }
            else if (IsDelim(value, '.'))
            {
                var name = GetIdent(At(values, index + 1));
                if (name is null || pseudoElement is not null)
                    return null;

                // A class prefix selector is an identifier ending with a hyphen, immediately followed by an asterisk
                if (name.EndsWith('-', StringComparison.Ordinal) && IsDelim(At(values, index + 2), '*'))
                {
                    selectors.Add(new ClassPrefixSelector(name));
                    index += 3;
                    continue;
                }

                selectors.Add(new ClassSelector(name));
                index += 2;
            }
            else if (IsDelim(value, '&'))
            {
                if (pseudoElement is not null)
                    return null;

                selectors.Add(new NestingSelector());
                index++;
            }
            else if (value is CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket } block)
            {
                if (pseudoElement is not null)
                    return null;

                var attribute = ParseAttributeSelector(block.Value);
                if (attribute is null)
                    return null;

                selectors.Add(attribute);
                index++;
            }
            else if (IsToken(value, CssTokenType.Colon))
            {
                var next = At(values, index + 1);
                if (IsToken(next, CssTokenType.Colon))
                {
                    // Pseudo-element
                    if (!allowPseudoElements)
                        return null;

                    var element = ParsePseudoElement(At(values, index + 2));
                    if (element is null || !IsPseudoElementAllowedAfter(pseudoElement, element))
                        return null;

                    selectors.Add(element);
                    pseudoElement = element;
                    index += 3;
                    continue;
                }

                // Legacy pseudo-elements (:before, :after, :first-line, :first-letter)
                if (GetIdent(next) is { } legacyName && InfraAscii.ToAsciiLowercase(legacyName) is "before" or "after" or "first-line" or "first-letter")
                {
                    if (!allowPseudoElements || pseudoElement is not null)
                        return null;

                    pseudoElement = new PseudoElementSelector(InfraAscii.ToAsciiLowercase(legacyName));
                    selectors.Add(pseudoElement);
                    index += 2;
                    continue;
                }

                // The arguments of a logical pseudo-class that follows a pseudo-element are restricted like the pseudo-class
                var outerPseudoElement = _afterPseudoElement;
                _afterPseudoElement = pseudoElement ?? outerPseudoElement;
                PseudoClassSelector? pseudoClass;
                try
                {
                    pseudoClass = ParsePseudoClass(next);
                }
                finally
                {
                    _afterPseudoElement = outerPseudoElement;
                }

                if (pseudoClass is null || !IsPseudoClassAllowedAfter(pseudoElement, pseudoClass))
                    return null;

                selectors.Add(pseudoClass);
                index += 2;
            }
            else
            {
                break;
            }
        }

        var parsed = CollectionsMarshal.AsSpan(selectors)[start..];
        if (parsed.IsEmpty)
            return null;

        // https://drafts.csswg.org/css-namespaces/#prefixes: a compound selector without type selector only matches the
        // elements of the default namespace, except in the arguments of the pseudo-classes (https://drafts.csswg.org/selectors-4/#matches)
        if (_listDepth == 1 && _namespaces.DefaultNamespace is { } defaultNamespace && parsed[0] is not TypeSelector && !ContainsNestingSelector(parsed))
        {
            var withType = new SimpleSelector[parsed.Length + 1];
            withType[0] = new TypeSelector(GetNamespaceConstraint(defaultNamespace), localName: null) { Implicit = true };
            parsed.CopyTo(withType.AsSpan(1));
            return new CompoundSelector(withType);
        }

        return new CompoundSelector(parsed.ToArray());
    }

    private static bool ContainsNestingSelector(ReadOnlySpan<SimpleSelector> selectors)
    {
        foreach (var selector in selectors)
        {
            if (selector is NestingSelector)
                return true;
        }

        return false;
    }

    private static bool IsPseudoElementAllowedAfter(PseudoElementSelector? previous, PseudoElementSelector element)
    {
        if (previous is null)
            return true;

        // https://drafts.csswg.org/css-multicol-2/#column-pseudo: ::before::column, ::column::scroll-marker
        if (previous.Name is "before" or "after")
            return element.Name is "marker" or "column";

        if (previous.Name == "column")
            return element.Name == "scroll-marker";

        // ::details-content is element-backed (https://drafts.csswg.org/css-pseudo-4/#details-content-pseudo): it can be
        // followed by the tree-abiding pseudo-elements and by the pseudo-elements of the formatted text
        if (previous.Name == "details-content")
            return element.Name is "first-line" or "first-letter" || (element.Name != "details-content" && SubPseudoElements.Contains(element.Name));

        // ::picker(select) is element-backed (https://drafts.csswg.org/css-forms-1/#picker-pseudo): it can be followed by the
        // tree-abiding pseudo-elements, by the pseudo-elements of the formatted text and by ::backdrop (it is a popover)
        if (previous.Name == "picker")
            return element.Name is "first-line" or "first-letter" or "backdrop" || SubPseudoElements.Contains(element.Name);

        if (previous.Name == "slotted")
            return SubPseudoElements.Contains(element.Name);

        // ::part() is a fully styleable pseudo-element (https://drafts.csswg.org/css-shadow-1/#part): it can be followed by
        // the pseudo-elements of the elements, except the ones that select elements of a shadow tree
        if (previous.Name == "part")
            return element.Name is not ("part" or "slotted");

        return false;
    }

    /// <summary>Whether a pseudo-class is <c>:not(:only-child)</c>.</summary>
    internal static bool IsNegatedOnlyChild(PseudoClassSelector pseudoClass)
        => pseudoClass is { Kind: PseudoClass.Not, Argument.Selectors: [{ Compounds: [{ Selectors: [PseudoClassSelector { Kind: PseudoClass.OnlyChild }] }] }] };

    private static bool IsPseudoClassAllowedAfter(PseudoElementSelector? pseudoElement, PseudoClassSelector pseudoClass)
    {
        if (pseudoElement is null)
            return true;

        // :is() and :where() can follow any pseudo-element: the selectors of their argument that are not allowed after the
        // pseudo-element are invalid (https://github.com/w3c/csswg-drafts/issues/8356)
        if (pseudoClass.Kind is PseudoClass.Is or PseudoClass.Where)
            return !pseudoElement.Name.StartsWith("view-transition", StringComparison.Ordinal) && pseudoElement.Name != "column";

        // ::slotted() cannot be followed by other pseudo-classes
        if (pseudoElement.Name == "slotted")
            return false;

        // https://drafts.csswg.org/css-view-transitions/#pseudo-root: the view transition pseudo-elements can only be followed by
        // :only-child, which can be negated (https://drafts.csswg.org/selectors-4/#pseudo-element-states: the logical combinations
        // of the pseudo-classes that are allowed after the pseudo-element)
        if (pseudoElement.Name.StartsWith("view-transition", StringComparison.Ordinal))
            return pseudoElement.IsFunction && (pseudoClass.Kind == PseudoClass.OnlyChild || IsNegatedOnlyChild(pseudoClass));

        // https://drafts.csswg.org/css-multicol-2/#column-pseudo: no pseudo-class
        if (pseudoElement.Name == "column")
            return false;

        // https://drafts.csswg.org/css-overflow-5/#scroll-buttons: :enabled and :disabled
        if (pseudoElement.Name == "scroll-button" && pseudoClass.Kind is PseudoClass.Enabled or PseudoClass.Disabled)
            return true;

        // https://drafts.csswg.org/css-overflow-5/#active-scroll-marker: the ::scroll-marker pseudo-elements are scroll markers
        if (pseudoElement.Name == "scroll-marker" && pseudoClass.Kind is PseudoClass.TargetCurrent or PseudoClass.TargetBefore or PseudoClass.TargetAfter)
            return true;

        // The pseudo-classes of the scroll navigation controls can be negated (their arguments are restricted like the pseudo-class)
        if (pseudoElement.Name is "scroll-marker" or "scroll-marker-group" or "scroll-button" && pseudoClass.Kind == PseudoClass.Not)
            return true;

        // https://drafts.csswg.org/css-pseudo-4/#highlight-selectors: no pseudo-class can follow the highlight pseudo-elements,
        // except :current after ::search-text (also in :not(), https://drafts.csswg.org/css-pseudo-4/#selectordef-search-text)
        if (pseudoElement.Name is "selection" or "target-text" or "spelling-error" or "grammar-error" or "highlight" or "search-text")
            return pseudoElement.Name == "search-text" && pseudoClass.Kind is PseudoClass.Current or PseudoClass.Not;

        // User action pseudo-classes can follow the other pseudo-elements
        if (pseudoClass.Kind is PseudoClass.Hover or PseudoClass.Active or PseudoClass.Focus or PseudoClass.FocusVisible or PseudoClass.FocusWithin)
            return true;

        // ::part() can be followed by the pseudo-classes that do not match based on tree information
        // (https://drafts.csswg.org/css-shadow-1/#part); the argument of :not() is restricted the same way
        if (pseudoElement.Name == "part")
        {
            return pseudoClass.Kind is not (PseudoClass.Root or PseudoClass.Empty or PseudoClass.FirstChild or PseudoClass.LastChild or PseudoClass.OnlyChild
                or PseudoClass.FirstOfType or PseudoClass.LastOfType or PseudoClass.OnlyOfType or PseudoClass.NthChild or PseudoClass.NthLastChild
                or PseudoClass.NthOfType or PseudoClass.NthLastOfType or PseudoClass.Has or PseudoClass.Scope or PseudoClass.Host
                or PseudoClass.HostFunction or PseudoClass.HostContext or PseudoClass.HasSlotted);
        }

        return false;
    }

    /// <summary>Parses a type selector or the universal selector with an optional namespace prefix (https://drafts.csswg.org/selectors-4/#typedef-type-selector).</summary>
    private bool TryParseTypeSelector(ReadOnlySpan<CssComponentValue> values, ref int index, out TypeSelector selector, out bool failed)
    {
        selector = null!;
        failed = false;
        var first = At(values, index);
        var second = At(values, index + 1);
        var third = At(values, index + 2);

        string? prefix = null;
        var hasPrefix = false;
        int nameIndex;
        if ((GetIdent(first) is not null || IsDelim(first, '*')) && IsDelim(second, '|') && (GetIdent(third) is not null || IsDelim(third, '*')))
        {
            prefix = GetIdent(first) ?? "*";
            hasPrefix = true;
            nameIndex = index + 2;
        }
        else if (IsDelim(first, '|'))
        {
            if (GetIdent(second) is null && !IsDelim(second, '*'))
            {
                failed = true;
                return false;
            }

            prefix = "";
            hasPrefix = true;
            nameIndex = index + 1;
        }
        else if (GetIdent(first) is not null || IsDelim(first, '*'))
        {
            nameIndex = index;
        }
        else
        {
            return false;
        }

        NamespaceConstraint ns;
        if (hasPrefix)
        {
            if (!TryResolvePrefix(prefix!, out ns))
            {
                failed = true;
                return false;
            }
        }
        else
        {
            ns = _namespaces.DefaultNamespace is { } defaultNamespace ? GetNamespaceConstraint(defaultNamespace) : NamespaceConstraint.Any;
        }

        var nameValue = At(values, nameIndex);
        selector = new TypeSelector(ns, GetIdent(nameValue))
        {
            Prefix = hasPrefix ? prefix : null,
            // *|e is serialized as e when there is no default namespace (https://drafts.csswg.org/cssom/#serializing-selectors)
            IsDefaultNamespace = hasPrefix && ((ns.Kind == NamespaceConstraintKind.Specific && ns.Namespace == _namespaces.DefaultNamespace) || (ns.Kind == NamespaceConstraintKind.None && _namespaces.DefaultNamespace is "") || (ns.Kind == NamespaceConstraintKind.Any && _namespaces.DefaultNamespace is null)),
        };
        index = nameIndex + 1;
        return true;
    }

    /// <summary>The empty namespace name stands for no namespace (https://drafts.csswg.org/css-namespaces/#declaration).</summary>
    private static NamespaceConstraint GetNamespaceConstraint(string ns) => ns.Length == 0 ? NamespaceConstraint.None : NamespaceConstraint.Specific(ns);

    private bool TryResolvePrefix(string prefix, out NamespaceConstraint result)
    {
        if (prefix == "*")
        {
            result = NamespaceConstraint.Any;
            return true;
        }

        if (prefix.Length == 0)
        {
            result = NamespaceConstraint.None;
            return true;
        }

        // Namespace prefixes are case-sensitive (https://drafts.csswg.org/css-namespaces/#prefixes)
        var ns = _namespaces.ResolvePrefix?.Invoke(prefix);
        if (ns is null)
        {
            result = default;
            return false; // An undeclared namespace prefix makes the selector invalid
        }

        result = ns.Length == 0 ? NamespaceConstraint.None : NamespaceConstraint.Specific(ns);
        return true;
    }

    /// <summary>https://drafts.csswg.org/selectors-4/#attribute-selectors</summary>
    private AttributeSelector? ParseAttributeSelector(List<CssComponentValue> values)
    {
        var index = 0;
        SkipWhitespace(values, ref index);
        var first = At(values, index);
        var second = At(values, index + 1);
        var third = At(values, index + 2);

        NamespaceConstraint ns;
        string? name;
        string? prefix = null;
        if ((GetIdent(first) is not null || IsDelim(first, '*')) && IsDelim(second, '|') && GetIdent(third) is not null)
        {
            prefix = GetIdent(first) ?? "*";
            if (!TryResolvePrefix(prefix, out ns))
                return null;

            name = GetIdent(third);
            index += 3;
        }
        else if (IsDelim(first, '|') && GetIdent(second) is not null)
        {
            ns = NamespaceConstraint.None;
            prefix = "";
            name = GetIdent(second);
            index += 2;
        }
        else if (GetIdent(first) is { } ident)
        {
            ns = NamespaceConstraint.None;
            name = ident;
            index++;
        }
        else
        {
            return null;
        }

        SkipWhitespace(values, ref index);
        if (index >= values.Count)
            return new AttributeSelector(ns, name!, AttributeOperator.Exists, "", AttributeCaseSensitivity.Default) { Prefix = prefix };

        AttributeOperator op;
        var matcher = values[index];
        if (IsDelim(matcher, '='))
        {
            op = AttributeOperator.Equals;
            index++;
        }
        else if (IsDelim(At(values, index + 1), '=') && matcher is CssPreservedToken { Type: CssTokenType.Delim } delim)
        {
            op = delim.Token.Value switch
            {
                "~" => AttributeOperator.Includes,
                "|" => AttributeOperator.DashMatch,
                "^" => AttributeOperator.Prefix,
                "$" => AttributeOperator.Suffix,
                "*" => AttributeOperator.Substring,
                _ => (AttributeOperator)255,
            };

            if ((byte)op == 255)
                return null;

            index += 2;
        }
        else
        {
            return null;
        }

        SkipWhitespace(values, ref index);
        string value;
        if (At(values, index) is CssPreservedToken { Type: CssTokenType.Ident or CssTokenType.String } valueToken)
        {
            value = valueToken.Token.Value;
            index++;
        }
        else
        {
            return null;
        }

        SkipWhitespace(values, ref index);
        var caseSensitivity = AttributeCaseSensitivity.Default;
        if (GetIdent(At(values, index)) is { } modifier)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(modifier, "i"))
            {
                caseSensitivity = AttributeCaseSensitivity.Insensitive;
            }
            else if (InfraAscii.EqualsIgnoreAsciiCase(modifier, "s"))
            {
                caseSensitivity = AttributeCaseSensitivity.Sensitive;
            }
            else
            {
                return null;
            }

            index++;
            SkipWhitespace(values, ref index);
        }

        if (index < values.Count)
            return null;

        return new AttributeSelector(ns, name!, op, value, caseSensitivity) { Prefix = prefix };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Pseudo-classes and pseudo-elements
    // ----------------------------------------------------------------------------------------------------------------

    private PseudoClassSelector? ParsePseudoClass(CssComponentValue? value)
    {
        var result = ParsePseudoClassCore(value);
        if (result is not null)
        {
            result.Name = InfraAscii.ToAsciiLowercase(GetIdent(value) ?? ((CssFunction)value!).Name);
        }

        return result;
    }

    private PseudoClassSelector? ParsePseudoClassCore(CssComponentValue? value)
    {
        if (GetIdent(value) is { } ident)
        {
            var name = InfraAscii.ToAsciiLowercase(ident);
            return PseudoClasses.TryGetValue(name, out var kind) ? new PseudoClassSelector(kind) : null;
        }

        if (value is not CssFunction function)
            return null;

        var functionName = InfraAscii.ToAsciiLowercase(function.Name);
        var arguments = function.Value;
        switch (functionName)
        {
            case "is" or "where":
            {
                var list = ParseList(arguments, _unforgiving ? ListKind.Real : ListKind.Forgiving);
                return list is null ? null : new PseudoClassSelector(functionName == "is" ? PseudoClass.Is : PseudoClass.Where) { Argument = list };
            }

            case "not":
            {
                var list = ParseList(arguments, ListKind.Real);
                return list is null ? null : new PseudoClassSelector(PseudoClass.Not) { Argument = list };
            }

            case "has":
            {
                // :has() cannot be nested (https://drafts.csswg.org/selectors-4/#relational)
                if (_hasDepth > 0)
                    return null;

                // The relative selectors of :has() can be complex in a compound selector (e.g. ::slotted(:has(a b)))
                var outerCompoundOnly = _compoundOnly;
                _compoundOnly = false;
                _hasDepth++;
                try
                {
                    var list = ParseList(arguments, ListKind.Relative);
                    if (list is null || list.Selectors.Length == 0)
                        return null;

                    return new PseudoClassSelector(PseudoClass.Has) { Argument = list };
                }
                finally
                {
                    _hasDepth--;
                    _compoundOnly = outerCompoundOnly;
                }
            }

            case "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type":
            {
                var kind = functionName switch
                {
                    "nth-child" => PseudoClass.NthChild,
                    "nth-last-child" => PseudoClass.NthLastChild,
                    "nth-of-type" => PseudoClass.NthOfType,
                    _ => PseudoClass.NthLastOfType,
                };

                // An+B [of S] (https://drafts.csswg.org/selectors-4/#the-nth-child-pseudo)
                var ofIndex = arguments.FindIndex(v => GetIdent(v) is { } word && InfraAscii.EqualsIgnoreAsciiCase(word, "of"));
                SelectorList? of = null;
                var anbValues = arguments;
                if (ofIndex >= 0 && kind is PseudoClass.NthChild or PseudoClass.NthLastChild)
                {
                    // "of" must be preceded by whitespace
                    if (ofIndex == 0 || !IsWhitespace(arguments[ofIndex - 1]))
                        return null;

                    of = ParseList(arguments[(ofIndex + 1)..], ListKind.Real);
                    if (of is null)
                        return null;

                    anbValues = arguments[..ofIndex];
                }

                var anb = ParseAnB(anbValues);
                if (anb is null)
                    return null;

                return new PseudoClassSelector(kind) { Nth = anb.Value, Argument = of };
            }

            case "lang":
            {
                // :lang() takes a list of language ranges: identifiers or strings
                var ranges = new List<string>();
                foreach (var part in SplitByComma(arguments))
                {
                    var trimmed = part.Where(v => !IsWhitespace(v)).ToList();
                    if (trimmed is not [CssPreservedToken { Type: CssTokenType.Ident or CssTokenType.String } range])
                        return null;

                    ranges.Add(range.Token.Value);
                }

                return new PseudoClassSelector(PseudoClass.Lang)
                {
                    Strings = [.. ranges],
                    ArgumentText = string.Join(", ", SplitByComma(arguments).Select(part => part.First(v => !IsWhitespace(v)) is CssPreservedToken { Type: CssTokenType.String } str ? Values.CssSerializer.SerializeString(str.Token.Value) : Values.CssSerializer.SerializeIdentifier(GetIdent(part.First(v => !IsWhitespace(v)))!))),
                };
            }

            case "dir":
            {
                var trimmed = arguments.Where(v => !IsWhitespace(v)).ToList();
                if (trimmed is not [CssPreservedToken { Type: CssTokenType.Ident } direction])
                    return null;

                return new PseudoClassSelector(PseudoClass.Dir) { Strings = [InfraAscii.ToAsciiLowercase(direction.Token.Value)] };
            }

            case "state":
            {
                var trimmed = arguments.Where(v => !IsWhitespace(v)).ToList();
                if (trimmed is not [CssPreservedToken { Type: CssTokenType.Ident } state])
                    return null;

                return new PseudoClassSelector(PseudoClass.State) { Strings = [state.Token.Value] };
            }

            case "active-view-transition-type":
            {
                // <custom-ident># (https://drafts.csswg.org/css-view-transitions-2/#the-active-view-transition-type-pseudo)
                var types = new List<string>();
                foreach (var part in SplitByComma(arguments))
                {
                    var trimmed = part.Where(v => !IsWhitespace(v)).ToList();
                    if (trimmed is not [CssPreservedToken { Type: CssTokenType.Ident } type] || Values.ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(type.Token.Value)) || InfraAscii.EqualsIgnoreAsciiCase(type.Token.Value, "default"))
                        return null;

                    // https://drafts.csswg.org/css-view-transitions-2/#typedef-view-transition-type: none and the names that start
                    // with -ua- are reserved
                    if (InfraAscii.EqualsIgnoreAsciiCase(type.Token.Value, "none") || type.Token.Value.StartsWith("-ua-", StringComparison.Ordinal))
                        return null;

                    types.Add(type.Token.Value);
                }

                return new PseudoClassSelector(PseudoClass.ActiveViewTransitionType) { Strings = [.. types] };
            }

            case "host" or "host-context":
            {
                var compound = ParseSingleCompound(arguments);
                if (compound is null)
                    return null;

                return new PseudoClassSelector(functionName == "host" ? PseudoClass.HostFunction : PseudoClass.HostContext) { Argument = compound };
            }

            case "heading":
            {
                var levels = new List<int>();
                foreach (var part in SplitByComma(arguments))
                {
                    var trimmed = part.Where(v => !IsWhitespace(v)).ToList();
                    if (trimmed is not [CssPreservedToken { Type: CssTokenType.Number, Token.IsInteger: true } level])
                        return null;

                    levels.Add((int)Math.Clamp(level.Token.NumericValue, int.MinValue, int.MaxValue));
                }

                return new PseudoClassSelector(PseudoClass.HeadingFunction) { Levels = [.. levels] };
            }
        }

        return null;
    }

    /// <summary>Parses the argument of :host(), :host-context() and ::slotted(): a single compound selector.</summary>
    private SelectorList? ParseSingleCompound(List<CssComponentValue> arguments)
    {
        var index = 0;
        SkipWhitespace(arguments, ref index);
        var outerCompoundOnly = _compoundOnly;
        var outerPseudoElement = _afterPseudoElement;
        _compoundOnly = true;
        _afterPseudoElement = null;
        CompoundSelector? compound;
        try
        {
            compound = ParseCompound(CollectionsMarshal.AsSpan(arguments), ref index, allowPseudoElements: false);
        }
        finally
        {
            _compoundOnly = outerCompoundOnly;
            _afterPseudoElement = outerPseudoElement;
        }

        SkipWhitespace(arguments, ref index);
        if (compound is null || index < arguments.Count)
            return null;

        return new SelectorList([new ComplexSelector([compound], [], null)]);
    }

    private PseudoElementSelector? ParsePseudoElement(CssComponentValue? value)
    {
        if (GetIdent(value) is { } ident)
        {
            var name = InfraAscii.ToAsciiLowercase(ident);
            if (PseudoElements.Contains(name) || name.StartsWith("-webkit-", StringComparison.Ordinal))
                return new PseudoElementSelector(name);

            return null;
        }

        if (value is not CssFunction function)
            return null;

        var functionName = InfraAscii.ToAsciiLowercase(function.Name);
        if (!FunctionalPseudoElements.Contains(functionName))
            return null;

        var arguments = function.Value.Where(v => !IsWhitespace(v)).ToList();
        switch (functionName)
        {
            case "slotted":
            {
                var compound = ParseSingleCompound(function.Value);
                return compound is null ? null : new PseudoElementSelector(functionName) { Argument = compound, IsFunction = true };
            }

            case "part":
                // One or more identifiers
                if (arguments.Count == 0 || arguments.Any(a => GetIdent(a) is null))
                    return null;

                return new PseudoElementSelector(functionName)
                {
                    IsFunction = true,
                    ArgumentText = string.Join(' ', arguments.Select(a => Values.CssSerializer.SerializeIdentifier(GetIdent(a)!))),
                    PartNames = [.. arguments.Select(a => GetIdent(a)!)],
                };

            case "scroll-button":
                // https://drafts.csswg.org/css-overflow-5/#scroll-buttons: '*' | <scroll-button-direction>
                if (arguments is [CssPreservedToken { Token: var star }] && star.IsDelim('*'))
                    return new PseudoElementSelector(functionName) { IsFunction = true, ArgumentText = "*" };

                if (arguments is not [CssPreservedToken { Type: CssTokenType.Ident } direction]
                    || InfraAscii.ToAsciiLowercase(direction.Token.Value) is not ("up" or "down" or "left" or "right" or "block-start" or "block-end" or "inline-start" or "inline-end" or "prev" or "next"))
                {
                    return null;
                }

                return new PseudoElementSelector(functionName) { IsFunction = true, ArgumentText = InfraAscii.ToAsciiLowercase(direction.Token.Value) };

            case "view-transition-group" or "view-transition-image-pair" or "view-transition-old" or "view-transition-new" or "view-transition-group-children":
                return ParseViewTransitionArgument(function.Value) is { } argument ? new PseudoElementSelector(functionName) { IsFunction = true, ArgumentText = argument } : null;

            case "picker":
                // https://drafts.csswg.org/css-forms-1/#picker-pseudo: ::picker(<form-control-identifier>), where the only form
                // control identifier is select
                if (arguments is not [CssPreservedToken { Type: CssTokenType.Ident } control] || !InfraAscii.EqualsIgnoreAsciiCase(control.Token.Value, "select"))
                    return null;

                return new PseudoElementSelector(functionName) { IsFunction = true, ArgumentText = "select" };

            case "cue" or "cue-region":
            {
                // https://w3c.github.io/webvtt/#the-cue-pseudo-element: the argument is a selector, which is matched against
                // the WebVTT node objects of the cues (the regions for ::cue-region())
                var outerCompoundOnly = _compoundOnly;
                var outerPseudoElement = _afterPseudoElement;
                _compoundOnly = false;
                _afterPseudoElement = null;
                SelectorList? list;
                try
                {
                    list = ParseList(function.Value, ListKind.Real);
                }
                finally
                {
                    _compoundOnly = outerCompoundOnly;
                    _afterPseudoElement = outerPseudoElement;
                }

                if (list is null || list.Selectors.Any(static selector => selector.HasPseudoElement))
                    return null;

                return new PseudoElementSelector(functionName) { IsFunction = true, Argument = list, ArgumentText = SelectorSerializer.Serialize(list) };
            }

            case "highlight":
                // https://drafts.csswg.org/css-pseudo-4/#selectordef-highlight: <highlight-name-selector> = '*' | <custom-ident>
                // (the escaped \* is the custom highlight name "*")
                if (arguments is [CssPreservedToken { Token: var universal }] && universal.IsDelim('*'))
                    return new PseudoElementSelector(functionName) { IsFunction = true, ArgumentText = "*" };

                if (arguments is not [CssPreservedToken { Type: CssTokenType.Ident }])
                    return null;

                return new PseudoElementSelector(functionName) { IsFunction = true, ArgumentText = Values.CssSerializer.SerializeIdentifier(GetIdent(arguments[0])!) };

            default:
                return new PseudoElementSelector(functionName) { IsFunction = true, ArgumentText = Values.CssSerializer.SerializeValue(function.Value, property: null) };
        }
    }

    /// <summary>
    /// Parses the argument of the view transition pseudo-elements (https://drafts.csswg.org/css-view-transitions-2/#typedef-pt-name-and-class-selector):
    /// &lt;pt-name-selector&gt; &lt;pt-class-selector&gt;? | &lt;pt-class-selector&gt;, where &lt;pt-name-selector&gt; is '*' or a
    /// &lt;custom-ident&gt; and &lt;pt-class-selector&gt; is ['.' &lt;custom-ident&gt;]+ without whitespace. Returns the serialization, or null.
    /// </summary>
    private static string? ParseViewTransitionArgument(List<CssComponentValue> values)
    {
        var start = 0;
        var end = values.Count;
        while (start < end && IsWhitespace(values[start]))
        {
            start++;
        }

        while (end > start && IsWhitespace(values[end - 1]))
        {
            end--;
        }

        var builder = new StringBuilder();
        var index = start;
        if (index < end && values[index] is CssPreservedToken { Token: var star } && star.IsDelim('*'))
        {
            builder.Append('*');
            index++;
        }
        else if (index < end && GetIdent(values[index]) is { } name)
        {
            if (Values.ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(name)) || InfraAscii.EqualsIgnoreAsciiCase(name, "default"))
                return null;

            builder.Append(Values.CssSerializer.SerializeIdentifier(name));
            index++;
        }

        while (index < end)
        {
            if (index + 1 >= end || values[index] is not CssPreservedToken { Token: var dot } || !dot.IsDelim('.') || GetIdent(values[index + 1]) is not { } className)
                return null;

            builder.Append('.').Append(Values.CssSerializer.SerializeIdentifier(className));
            index += 2;
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // An+B (https://drafts.csswg.org/css-syntax-3/#anb-microsyntax)
    // ----------------------------------------------------------------------------------------------------------------

    internal static AnB? ParseAnB(List<CssComponentValue> values)
    {
        // Keep the tokens, and whether each one is preceded by whitespace
        var tokens = new List<(CssToken Token, bool Whitespace)>();
        var whitespace = false;
        foreach (var value in values)
        {
            if (value is not CssPreservedToken preserved)
                return null;

            if (preserved.Type == CssTokenType.Whitespace)
            {
                whitespace = true;
                continue;
            }

            tokens.Add((preserved.Token, whitespace));
            whitespace = false;
        }

        if (tokens.Count == 0)
            return null;

        var index = 0;
        var first = tokens[0].Token;
        index++;
        int a;
        switch (first.Type)
        {
            case CssTokenType.Ident when first.IsIdent("odd") && tokens.Count == 1:
                return new AnB(2, 1);

            case CssTokenType.Ident when first.IsIdent("even") && tokens.Count == 1:
                return new AnB(2, 0);

            case CssTokenType.Number when first.IsInteger && tokens.Count == 1:
                return new AnB(0, Clamp(first.NumericValue));

            case CssTokenType.Dimension when first.IsInteger:
                a = Clamp(first.NumericValue);
                return ParseNPart(first.Value, a, tokens, index);

            case CssTokenType.Ident:
            {
                var ident = InfraAscii.ToAsciiLowercase(first.Value);
                if (ident.StartsWith('-', StringComparison.Ordinal))
                    return ParseNPart(ident[1..], -1, tokens, index);

                return ParseNPart(ident, 1, tokens, index);
            }

            case CssTokenType.Delim when first.IsDelim('+'):
            {
                // '+'? n...: no whitespace is allowed between '+' and n
                if (tokens.Count < 2 || tokens[1].Whitespace || tokens[1].Token.Type != CssTokenType.Ident || tokens[1].Token.Value.StartsWith('-', StringComparison.Ordinal))
                    return null;

                return ParseNPart(InfraAscii.ToAsciiLowercase(tokens[1].Token.Value), 1, tokens, 2);
            }

            default:
                return null;
        }
    }

    /// <summary>Parses the "n", "n-", "n-&lt;digits&gt;" part (the unit of a dimension or an identifier), followed by the optional B part.</summary>
    private static AnB? ParseNPart(string unit, int a, List<(CssToken Token, bool Whitespace)> tokens, int index)
    {
        unit = InfraAscii.ToAsciiLowercase(unit);
        if (unit == "n")
        {
            // n, n <signed-integer>, n ['+' | '-'] <signless-integer>
            if (index == tokens.Count)
                return new AnB(a, 0);

            var next = tokens[index].Token;
            if (next.Type == CssTokenType.Number && next.IsInteger && next.Sign != '\0' && index + 1 == tokens.Count)
                return new AnB(a, Clamp(next.NumericValue));

            if ((next.IsDelim('+') || next.IsDelim('-')) && index + 2 == tokens.Count)
            {
                var b = tokens[index + 1].Token;
                if (b.Type == CssTokenType.Number && b.IsInteger && b.Sign == '\0')
                    return new AnB(a, next.IsDelim('-') ? -Clamp(b.NumericValue) : Clamp(b.NumericValue));
            }

            return null;
        }

        if (unit == "n-")
        {
            // n- <signless-integer>
            if (index + 1 == tokens.Count)
            {
                var b = tokens[index].Token;
                if (b.Type == CssTokenType.Number && b.IsInteger && b.Sign == '\0')
                    return new AnB(a, -Clamp(b.NumericValue));
            }

            return null;
        }

        // n-<digits>
        if (unit.Length > 2 && unit.StartsWith("n-", StringComparison.Ordinal) && unit.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0 && index == tokens.Count)
            return new AnB(a, -Clamp(double.Parse(unit.AsSpan(2), CultureInfo.InvariantCulture)));

        return null;
    }

    private static int Clamp(double value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
