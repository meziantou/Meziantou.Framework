using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>https://drafts.csswg.org/selectors-4/#combinators</summary>
public enum Combinator : byte
{
    /// <summary>The descendant combinator (whitespace).</summary>
    Descendant,

    /// <summary>The child combinator (<c>&gt;</c>).</summary>
    Child,

    /// <summary>The next-sibling combinator (<c>+</c>).</summary>
    NextSibling,

    /// <summary>The subsequent-sibling combinator (<c>~</c>).</summary>
    SubsequentSibling,
}

/// <summary>The namespace constraint of a type or attribute selector (https://drafts.csswg.org/selectors-4/#type-nmsp).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct NamespaceConstraint
{
    private NamespaceConstraint(NamespaceConstraintKind kind, string? ns)
    {
        Kind = kind;
        Namespace = ns;
    }

    /// <summary>Gets the kind of the constraint.</summary>
    public NamespaceConstraintKind Kind { get; }

    /// <summary>The namespace when <see cref="Kind"/> is <see cref="NamespaceConstraintKind.Specific"/>, otherwise null.</summary>
    public string? Namespace { get; }

    /// <summary>Any namespace, including no namespace (<c>*|E</c>, or <c>E</c> without a default namespace).</summary>
    public static NamespaceConstraint Any => new(NamespaceConstraintKind.Any, null);

    /// <summary>No namespace (<c>|E</c>).</summary>
    public static NamespaceConstraint None => new(NamespaceConstraintKind.None, null);

    /// <summary>The namespace <paramref name="ns"/>.</summary>
    public static NamespaceConstraint Specific(string ns) => new(NamespaceConstraintKind.Specific, ns);

    /// <summary>Returns whether the namespace (null for no namespace) satisfies the constraint.</summary>
    public bool Matches(string? ns) => Kind switch
    {
        NamespaceConstraintKind.Any => true,
        NamespaceConstraintKind.None => ns is null,
        _ => string.Equals(ns, Namespace, StringComparison.Ordinal),
    };
}

/// <summary>The kinds of namespace constraint of a type or attribute selector.</summary>
public enum NamespaceConstraintKind : byte
{
    /// <summary>Any namespace, including no namespace.</summary>
    Any,

    /// <summary>No namespace.</summary>
    None,

    /// <summary>The namespace <see cref="NamespaceConstraint.Namespace"/>.</summary>
    Specific,
}

/// <summary>The specificity of a selector (https://drafts.csswg.org/selectors-4/#specificity-rules).</summary>
/// <param name="A">The number of ID selectors.</param>
/// <param name="B">The number of class selectors, attribute selectors and pseudo-classes.</param>
/// <param name="C">The number of type selectors and pseudo-elements.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Specificity(int A, int B, int C) : IComparable<Specificity>
{
    /// <summary>Returns the component-wise sum of the two specificities.</summary>
    public static Specificity operator +(Specificity left, Specificity right) => new(left.A + right.A, left.B + right.B, left.C + right.C);

    /// <summary>Returns the greater of the two specificities.</summary>
    public static Specificity Max(Specificity left, Specificity right) => left.CompareTo(right) >= 0 ? left : right;

    /// <inheritdoc/>
    public int CompareTo(Specificity other)
    {
        if (A != other.A)
            return A.CompareTo(other.A);

        if (B != other.B)
            return B.CompareTo(other.B);

        return C.CompareTo(other.C);
    }

    /// <summary>Returns whether the left specificity is lower than the right specificity.</summary>
    public static bool operator <(Specificity left, Specificity right) => left.CompareTo(right) < 0;

    /// <summary>Returns whether the left specificity is greater than the right specificity.</summary>
    public static bool operator >(Specificity left, Specificity right) => left.CompareTo(right) > 0;

    /// <summary>Returns whether the left specificity is lower than or equal to the right specificity.</summary>
    public static bool operator <=(Specificity left, Specificity right) => left.CompareTo(right) <= 0;

    /// <summary>Returns whether the left specificity is greater than or equal to the right specificity.</summary>
    public static bool operator >=(Specificity left, Specificity right) => left.CompareTo(right) >= 0;

    /// <summary>Returns the specificity as "(A, B, C)".</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({A}, {B}, {C})");
}

/// <summary>A selector list (https://drafts.csswg.org/selectors-4/#selector-list). Parsed selectors are immutable.</summary>
public sealed class SelectorList
{
    internal SelectorList(ComplexSelector[] selectors, UnparsedSelector[]? unparsedSelectors = null)
    {
        Selectors = ImmutableCollectionsMarshal.AsImmutableArray(selectors);
        UnparsedSelectors = unparsedSelectors is null ? [] : ImmutableCollectionsMarshal.AsImmutableArray(unparsedSelectors);
    }

    /// <summary>Gets the complex selectors of the list, in source order.</summary>
    public ImmutableArray<ComplexSelector> Selectors { get; }

    /// <summary>
    /// The invalid selectors of a forgiving selector list (:is(), :where()), which never match but are serialized as written
    /// (https://drafts.csswg.org/selectors-4/#typedef-forgiving-selector-list, https://github.com/w3c/csswg-drafts/issues/8356).
    /// </summary>
    internal ImmutableArray<UnparsedSelector> UnparsedSelectors { get; }

    /// <summary>The maximum specificity of the selectors of the list (used by :is(), :not(), :has() and :nth-child(of S)).</summary>
    public Specificity MaxSpecificity => Selectors.Length == 0 ? default : Selectors.Max(s => s.Specificity);

    /// <summary>
    /// Parses a selector list, as <c>querySelector</c> does (https://dom.spec.whatwg.org/#scope-match-a-selectors-string):
    /// namespace prefixes other than <c>*</c> and the empty prefix are invalid.
    /// </summary>
    /// <exception cref="DomException">The selector is invalid ("SyntaxError").</exception>
    public static SelectorList Parse(string selectors) => SelectorCache.Parse(selectors);

    /// <summary>Parses a selector list, as <see cref="Parse(string)"/> does, without throwing when the selector is invalid.</summary>
    public static bool TryParse(string selectors, [NotNullWhen(true)] out SelectorList? result)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        result = SelectorCache.TryParse(selectors);
        return result is not null;
    }

    /// <summary>Returns whether the element matches the selector, as <see cref="Element.Matches(string)"/> does (https://dom.spec.whatwg.org/#dom-element-matches).</summary>
    public bool Matches(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return SelectorMatcher.Matches(this, element, SelectorContext.ForDomApi(element));
    }

    /// <summary>Serializes the selector list (https://drafts.csswg.org/cssom/#serialize-a-group-of-selectors).</summary>
    public override string ToString() => SelectorSerializer.Serialize(this);
}

/// <summary>An invalid selector of a forgiving selector list, kept for the serialization.</summary>
/// <param name="Index">The position of the selector in the list, counting the valid selectors.</param>
/// <param name="Text">The text of the selector as written, without the leading and trailing whitespace.</param>
/// <param name="ContainsNestingSelector">Whether the text contains the nesting selector (&amp;): a nested selector containing it is not relative to the parent rule.</param>
internal sealed record UnparsedSelector(int Index, string Text, bool ContainsNestingSelector);

/// <summary>A complex (or relative) selector (https://drafts.csswg.org/selectors-4/#complex).</summary>
public sealed class ComplexSelector
{
    internal ComplexSelector(CompoundSelector[] compounds, Combinator[] combinators, Combinator? relativeCombinator)
    {
        Compounds = ImmutableCollectionsMarshal.AsImmutableArray(compounds);
        Combinators = ImmutableCollectionsMarshal.AsImmutableArray(combinators);
        RelativeCombinator = relativeCombinator;
    }

    /// <summary>The compound selectors, left to right.</summary>
    public ImmutableArray<CompoundSelector> Compounds { get; }

    /// <summary>The combinators; <c>Combinators[i]</c> is between <c>Compounds[i]</c> and <c>Compounds[i + 1]</c>.</summary>
    public ImmutableArray<Combinator> Combinators { get; }

    /// <summary>The leading combinator of a relative selector (https://drafts.csswg.org/selectors-4/#relative-selector), or null.</summary>
    public Combinator? RelativeCombinator { get; }

    /// <summary>The specificity (computed on demand: the specificity of the nesting selector depends on its parent rule).</summary>
    public Specificity Specificity
    {
        get
        {
            var result = default(Specificity);
            foreach (var compound in Compounds)
            {
                result += compound.Specificity;
            }

            return result;
        }
    }

    /// <summary>Whether the selector contains a pseudo-element (it then never matches an element).</summary>
    public bool HasPseudoElement => Compounds[^1].HasPseudoElement;

    /// <summary>Serializes the selector (https://drafts.csswg.org/cssom/#serialize-a-selector).</summary>
    public override string ToString() => SelectorSerializer.Serialize(this);
}

/// <summary>A compound selector, with the pseudo-elements and the pseudo-classes that follow them (https://drafts.csswg.org/selectors-4/#compound).</summary>
public sealed class CompoundSelector
{
    internal CompoundSelector(SimpleSelector[] selectors)
    {
        Selectors = ImmutableCollectionsMarshal.AsImmutableArray(selectors);
        HasPseudoElement = selectors.Any(s => s is PseudoElementSelector);
    }

    /// <summary>The simple selectors, in source order.</summary>
    public ImmutableArray<SimpleSelector> Selectors { get; }

    /// <summary>Gets a value indicating whether the compound selector contains a pseudo-element.</summary>
    public bool HasPseudoElement { get; }

    /// <summary>
    /// Gets the specificity of the compound selector, the sum of the specificities of its simple selectors.
    /// </summary>
    public Specificity Specificity
    {
        get
        {
            var result = default(Specificity);
            foreach (var selector in Selectors)
            {
                result += selector.Specificity;
            }

            return result;
        }
    }

    /// <summary>Serializes the compound selector (https://drafts.csswg.org/cssom/#serialize-a-selector).</summary>
    public override string ToString() => SelectorSerializer.Serialize(this);
}

/// <summary>A simple selector (https://drafts.csswg.org/selectors-4/#simple).</summary>
public abstract class SimpleSelector
{
    private protected SimpleSelector()
    {
    }

    /// <summary>Gets the specificity of the simple selector.</summary>
    public abstract Specificity Specificity { get; }

    /// <summary>Serializes the simple selector (https://drafts.csswg.org/cssom/#serialize-a-simple-selector).</summary>
    public override string ToString() => SelectorSerializer.Serialize(this);
}

/// <summary>A type selector or the universal selector (https://drafts.csswg.org/selectors-4/#type-selectors).</summary>
public sealed class TypeSelector : SimpleSelector
{
    internal TypeSelector(NamespaceConstraint ns, string? localName)
    {
        Namespace = ns;
        LocalName = localName;
        LowercaseLocalName = localName is null ? null : Infra.InfraAscii.ToAsciiLowercase(localName);
    }

    /// <summary>Gets the namespace constraint of the selector.</summary>
    public NamespaceConstraint Namespace { get; }

    /// <summary>The local name, or null for the universal selector.</summary>
    public string? LocalName { get; }

    /// <summary>Whether this is the universal selector (<c>*</c>, https://drafts.csswg.org/selectors-4/#the-universal-selector).</summary>
    public bool IsUniversal => LocalName is null;

    /// <summary>The ASCII lowercase local name, used to match HTML elements in HTML documents.</summary>
    internal string? LowercaseLocalName { get; }

    /// <summary>The namespace prefix as written ("*" for any namespace, "" for no namespace), or null when there is no prefix.</summary>
    public string? Prefix { get; internal init; }

    /// <summary>Whether the namespace is the default namespace (the prefix is then not serialized).</summary>
    public bool IsDefaultNamespace { get; internal init; }

    /// <summary>
    /// Whether the universal selector is implied by a compound selector without type selector when a default namespace is
    /// declared (https://drafts.csswg.org/css-namespaces/#prefixes): it restricts the compound selector to the elements of
    /// the default namespace, and it is not serialized.
    /// </summary>
    public bool Implicit { get; internal init; }

    /// <summary>
    /// Gets the specificity of the selector: (0, 0, 1) for a type selector, (0, 0, 0) for the universal selector.
    /// </summary>
    public override Specificity Specificity => LocalName is null ? default : new(0, 0, 1);
}

/// <summary>An ID selector (https://drafts.csswg.org/selectors-4/#id-selectors).</summary>
public sealed class IdSelector : SimpleSelector
{
    internal IdSelector(string id)
    {
        Id = id;
    }

    /// <summary>Gets the identifier to match.</summary>
    public string Id { get; }

    /// <summary>Gets the specificity of the selector, always (1, 0, 0).</summary>
    public override Specificity Specificity => new(1, 0, 0);
}

/// <summary>A class selector (https://drafts.csswg.org/selectors-4/#class-html).</summary>
public sealed class ClassSelector : SimpleSelector
{
    internal ClassSelector(string className)
    {
        ClassName = className;
    }

    /// <summary>The class name, with the escapes resolved.</summary>
    public string ClassName { get; }

    /// <summary>Gets the specificity of the selector, always (0, 1, 0).</summary>
    public override Specificity Specificity => new(0, 1, 0);
}

/// <summary>
/// A class prefix selector (https://drafts.csswg.org/selectors-5/#class-prefix, tentative): <c>.foo-*</c> matches the
/// elements with a class that starts with the prefix (an identifier ending with a hyphen), followed by at least one
/// character that is not a hyphen.
/// </summary>
[Experimental(ExperimentalApis.ClassPrefixSelector, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class ClassPrefixSelector : SimpleSelector
{
    internal ClassPrefixSelector(string prefix)
    {
        Prefix = prefix;
    }

    /// <summary>The prefix, with the escapes resolved (e.g. "foo-" for <c>.foo-*</c>).</summary>
    public string Prefix { get; }

    /// <summary>Gets the specificity of the selector, always (0, 1, 0).</summary>
    public override Specificity Specificity => new(0, 1, 0);
}

/// <summary>https://drafts.csswg.org/selectors-4/#attribute-selectors</summary>
public enum AttributeOperator : byte
{
    /// <summary><c>[att]</c></summary>
    Exists,

    /// <summary><c>[att=val]</c></summary>
    Equals,

    /// <summary><c>[att~=val]</c></summary>
    Includes,

    /// <summary><c>[att|=val]</c></summary>
    DashMatch,

    /// <summary><c>[att^=val]</c></summary>
    Prefix,

    /// <summary><c>[att$=val]</c></summary>
    Suffix,

    /// <summary><c>[att*=val]</c></summary>
    Substring,
}

/// <summary>https://drafts.csswg.org/selectors-4/#attribute-case</summary>
public enum AttributeCaseSensitivity : byte
{
    /// <summary>Defined by the document language (the HTML rules).</summary>
    Default,

    /// <summary>The <c>i</c> modifier.</summary>
    Insensitive,

    /// <summary>The <c>s</c> modifier.</summary>
    Sensitive,
}

/// <summary>An attribute selector (https://drafts.csswg.org/selectors-4/#attribute-selectors).</summary>
public sealed class AttributeSelector : SimpleSelector
{
    internal AttributeSelector(NamespaceConstraint ns, string name, AttributeOperator op, string value, AttributeCaseSensitivity caseSensitivity)
    {
        Namespace = ns;
        Name = name;
        LowercaseName = Infra.InfraAscii.ToAsciiLowercase(name);
        Operator = op;
        Value = value;
        CaseSensitivity = caseSensitivity;
    }

    /// <summary>Gets the namespace constraint of the attribute.</summary>
    public NamespaceConstraint Namespace { get; }

    /// <summary>Gets the name of the attribute to match.</summary>
    public string Name { get; }

    internal string LowercaseName { get; }

    /// <summary>Gets the operator used to compare the attribute value.</summary>
    public AttributeOperator Operator { get; }

    /// <summary>The value, or the empty string for <see cref="AttributeOperator.Exists"/>.</summary>
    public string Value { get; }

    /// <summary>Gets how the attribute value is compared.</summary>
    public AttributeCaseSensitivity CaseSensitivity { get; }

    /// <summary>The namespace prefix as written ("*" for any namespace, "" for no namespace), or null when there is no prefix.</summary>
    public string? Prefix { get; internal init; }

    /// <summary>Gets the specificity of the selector, always (0, 1, 0).</summary>
    public override Specificity Specificity => new(0, 1, 0);
}

/// <summary>
/// The nesting selector (https://drafts.csswg.org/css-nesting/#nest-selector): the elements matched by the parent style
/// rule, or the scoping root (:scope) outside of a style rule.
/// </summary>
public sealed class NestingSelector : SimpleSelector
{
    internal NestingSelector()
    {
    }

    /// <summary>The selector of the parent style rule, or null outside of a style rule (bound when the style sheet is applied).</summary>
    public SelectorList? Parent { get; internal set; }

    /// <summary>
    /// Whether the nesting selector is implied by a relative selector of a nested or scoped style rule, or of a &lt;scope-end&gt;
    /// (https://drafts.csswg.org/css-cascade-6/#scoped-rules): it is not serialized.
    /// </summary>
    public bool Implicit { get; internal init; }

    /// <summary>
    /// Gets the specificity of the selector, the greatest specificity of the parent selector list. A parent selector with a
    /// pseudo-element is contextually invalid in the :is() the nesting selector stands for: it never matches and has no
    /// specificity (https://drafts.csswg.org/css-nesting/#nest-selector).
    /// </summary>
    public override Specificity Specificity
    {
        get
        {
            var result = default(Specificity);
            if (Parent is { } parent)
            {
                foreach (var selector in parent.Selectors)
                {
                    if (!selector.HasPseudoElement)
                    {
                        result = Specificity.Max(result, selector.Specificity);
                    }
                }
            }

            return result;
        }
    }
}

/// <summary>An An+B value (https://drafts.csswg.org/css-syntax-3/#anb-microsyntax).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AnB(int A, int B)
{
    /// <summary>Returns whether the 1-based index matches An+B for some non-negative integer n.</summary>
    public bool Matches(int index)
    {
        if (A == 0)
            return index == B;

        var diff = (long)index - B;
        return diff % A == 0 && diff / A >= 0;
    }

    /// <summary>Serializes the value (https://drafts.csswg.org/css-syntax-3/#serializing-anb).</summary>
    public override string ToString() => SelectorSerializer.SerializeAnB(this);
}

/// <summary>The pseudo-classes (https://drafts.csswg.org/selectors-4/#pseudo-classes).</summary>
public enum PseudoClass : byte
{
    // Logical
    /// <summary>The <c>:is()</c> pseudo-class.</summary>
    Is,
    /// <summary>The <c>:where()</c> pseudo-class.</summary>
    Where,
    /// <summary>The <c>:not()</c> pseudo-class.</summary>
    Not,
    /// <summary>The <c>:has()</c> pseudo-class.</summary>
    Has,

    // Structural
    /// <summary>The <c>:root</c> pseudo-class.</summary>
    Root,
    /// <summary>The <c>:empty</c> pseudo-class.</summary>
    Empty,
    /// <summary>The <c>:first-child</c> pseudo-class.</summary>
    FirstChild,
    /// <summary>The <c>:last-child</c> pseudo-class.</summary>
    LastChild,
    /// <summary>The <c>:only-child</c> pseudo-class.</summary>
    OnlyChild,
    /// <summary>The <c>:first-of-type</c> pseudo-class.</summary>
    FirstOfType,
    /// <summary>The <c>:last-of-type</c> pseudo-class.</summary>
    LastOfType,
    /// <summary>The <c>:only-of-type</c> pseudo-class.</summary>
    OnlyOfType,
    /// <summary>The <c>:nth-child()</c> pseudo-class.</summary>
    NthChild,
    /// <summary>The <c>:nth-last-child()</c> pseudo-class.</summary>
    NthLastChild,
    /// <summary>The <c>:nth-of-type()</c> pseudo-class.</summary>
    NthOfType,
    /// <summary>The <c>:nth-last-of-type()</c> pseudo-class.</summary>
    NthLastOfType,
    /// <summary>The <c>:scope</c> pseudo-class.</summary>
    Scope,

    // Shadow DOM

    /// <summary><c>:host</c></summary>
    Host,

    /// <summary><c>:host()</c></summary>
    HostFunction,
    /// <summary>The <c>:host-context()</c> pseudo-class.</summary>
    HostContext,
    /// <summary>The <c>:has-slotted</c> pseudo-class.</summary>
    HasSlotted,

    // Location
    /// <summary>The <c>:any-link</c> pseudo-class.</summary>
    AnyLink,
    /// <summary>The <c>:link</c> pseudo-class.</summary>
    Link,
    /// <summary>The <c>:visited</c> pseudo-class.</summary>
    Visited,
    /// <summary>The <c>:target</c> pseudo-class.</summary>
    Target,
    /// <summary>The <c>:target-within</c> pseudo-class.</summary>
    TargetWithin,

    // User action (the state of the pointing device is updated by the trusted pointer events that simulate the user input)
    /// <summary>The <c>:hover</c> pseudo-class.</summary>
    Hover,
    /// <summary>The <c>:active</c> pseudo-class.</summary>
    Active,
    /// <summary>The <c>:focus</c> pseudo-class.</summary>
    Focus,
    /// <summary>The <c>:focus-visible</c> pseudo-class.</summary>
    FocusVisible,
    /// <summary>The <c>:focus-within</c> pseudo-class.</summary>
    FocusWithin,

    // Linguistic
    /// <summary>The <c>:lang()</c> pseudo-class.</summary>
    Lang,
    /// <summary>The <c>:dir()</c> pseudo-class.</summary>
    Dir,

    // Input and element states
    /// <summary>The <c>:defined</c> pseudo-class.</summary>
    Defined,
    /// <summary>The <c>:enabled</c> pseudo-class.</summary>
    Enabled,
    /// <summary>The <c>:disabled</c> pseudo-class.</summary>
    Disabled,
    /// <summary>The <c>:read-only</c> pseudo-class.</summary>
    ReadOnly,
    /// <summary>The <c>:read-write</c> pseudo-class.</summary>
    ReadWrite,
    /// <summary>The <c>:placeholder-shown</c> pseudo-class.</summary>
    PlaceholderShown,
    /// <summary>The <c>:default</c> pseudo-class.</summary>
    Default,
    /// <summary>The <c>:checked</c> pseudo-class.</summary>
    Checked,
    /// <summary>The <c>:indeterminate</c> pseudo-class.</summary>
    Indeterminate,
    /// <summary>The <c>:valid</c> pseudo-class.</summary>
    Valid,
    /// <summary>The <c>:invalid</c> pseudo-class.</summary>
    Invalid,
    /// <summary>The <c>:in-range</c> pseudo-class.</summary>
    InRange,
    /// <summary>The <c>:out-of-range</c> pseudo-class.</summary>
    OutOfRange,
    /// <summary>The <c>:required</c> pseudo-class.</summary>
    Required,
    /// <summary>The <c>:optional</c> pseudo-class.</summary>
    Optional,
    /// <summary>The <c>:user-valid</c> pseudo-class.</summary>
    UserValid,
    /// <summary>The <c>:user-invalid</c> pseudo-class.</summary>
    UserInvalid,

    /// <summary><c>:autofill</c> or <c>:-webkit-autofill</c></summary>
    Autofill,
    /// <summary>The <c>:open</c> pseudo-class.</summary>
    Open,
    /// <summary>The <c>:modal</c> pseudo-class.</summary>
    Modal,
    /// <summary>The <c>:popover-open</c> pseudo-class.</summary>
    PopoverOpen,
    /// <summary>The <c>:fullscreen</c> pseudo-class.</summary>
    Fullscreen,
    /// <summary>The <c>:picture-in-picture</c> pseudo-class.</summary>
    PictureInPicture,
    /// <summary>The <c>:state()</c> pseudo-class.</summary>
    State,

    /// <summary><c>:heading</c></summary>
    Heading,

    /// <summary><c>:heading()</c></summary>
    HeadingFunction,

    // View transitions (there is no view transition: they never match)
    /// <summary>The <c>:active-view-transition</c> pseudo-class.</summary>
    ActiveViewTransition,
    /// <summary>The <c>:active-view-transition-type()</c> pseudo-class.</summary>
    ActiveViewTransitionType,

    // Time-dimensional pseudo-classes (there is no cue nor active search result: they never match)
    /// <summary>The <c>:current</c> pseudo-class.</summary>
    Current,
    /// <summary>The <c>:future</c> pseudo-class.</summary>
    Future,
    /// <summary>The <c>:past</c> pseudo-class.</summary>
    Past,

    /// <summary><c>:xr-overlay</c> (there is no immersive session: it never matches)</summary>
    XrOverlay,

    // Media resource states
    /// <summary>The <c>:playing</c> pseudo-class.</summary>
    Playing,
    /// <summary>The <c>:paused</c> pseudo-class.</summary>
    Paused,
    /// <summary>The <c>:seeking</c> pseudo-class.</summary>
    Seeking,
    /// <summary>The <c>:buffering</c> pseudo-class.</summary>
    Buffering,
    /// <summary>The <c>:stalled</c> pseudo-class.</summary>
    Stalled,
    /// <summary>The <c>:muted</c> pseudo-class.</summary>
    Muted,
    /// <summary>The <c>:volume-locked</c> pseudo-class.</summary>
    VolumeLocked,

    // Scroll markers (https://drafts.csswg.org/css-overflow-5/#active-scroll-marker)
    /// <summary>The <c>:target-current</c> pseudo-class: the active scroll marker of a scroll marker group.</summary>
    TargetCurrent,
    /// <summary>The <c>:target-before</c> pseudo-class: the scroll markers that precede the active one in their group.</summary>
    TargetBefore,
    /// <summary>The <c>:target-after</c> pseudo-class: the scroll markers that follow the active one in their group.</summary>
    TargetAfter,
}

/// <summary>A pseudo-class (https://drafts.csswg.org/selectors-4/#pseudo-classes).</summary>
public sealed class PseudoClassSelector : SimpleSelector
{
    internal PseudoClassSelector(PseudoClass kind)
    {
        Kind = kind;
    }

    /// <summary>Gets the kind of the pseudo-class.</summary>
    public PseudoClass Kind { get; }

    /// <summary>The name as written, in ASCII lowercase and without the colon (e.g. "nth-child" or "-webkit-autofill").</summary>
    public string Name { get; internal set; } = "";

    /// <summary>The serialized arguments of :lang().</summary>
    public string? ArgumentText { get; internal set; }

    /// <summary>The selector argument of :is(), :where(), :not(), :has(), :nth-child(An+B of S), :nth-last-child(An+B of S), :host() and :host-context().</summary>
    public SelectorList? Argument { get; internal init; }

    /// <summary>The An+B argument of :nth-child(), :nth-last-child(), :nth-of-type() and :nth-last-of-type().</summary>
    public AnB Nth { get; internal init; }

    /// <summary>The language ranges of :lang(), the direction of :dir(), the identifier of :state(), the types of :active-view-transition-type().</summary>
    public ImmutableArray<string> Strings { get; internal init; } = [];

    /// <summary>The levels of :heading().</summary>
    public ImmutableArray<int> Levels { get; internal init; } = [];

    /// <summary>
    /// Gets the specificity of the selector, which depends on the pseudo-class and its selector argument.
    /// </summary>
    public override Specificity Specificity => Kind switch
    {
        PseudoClass.Where => default,
        PseudoClass.Is or PseudoClass.Not or PseudoClass.Has => Argument!.MaxSpecificity,
        PseudoClass.NthChild or PseudoClass.NthLastChild => new Specificity(0, 1, 0) + (Argument?.MaxSpecificity ?? default),
        PseudoClass.HostFunction or PseudoClass.HostContext => new Specificity(0, 1, 0) + (Argument?.MaxSpecificity ?? default),
        _ => new(0, 1, 0),
    };
}

/// <summary>A pseudo-element (https://drafts.csswg.org/selectors-4/#pseudo-elements). Pseudo-elements are not elements, so a selector containing one never matches an element.</summary>
public sealed class PseudoElementSelector : SimpleSelector
{
    internal PseudoElementSelector(string name)
    {
        Name = name;
    }

    /// <summary>The name, in ASCII lowercase and without the colons (e.g. "before" or "part").</summary>
    public string Name { get; }

    /// <summary>Whether the pseudo-element is a function (e.g. ::part()).</summary>
    public bool IsFunction { get; internal init; }

    /// <summary>The serialized arguments of the functional pseudo-elements other than ::slotted() (e.g. the identifiers of ::part()).</summary>
    public string? ArgumentText { get; internal init; }

    /// <summary>The part names of ::part() (https://drafts.csswg.org/css-shadow-1/#part).</summary>
    public ImmutableArray<string> PartNames { get; internal init; } = [];

    /// <summary>The compound selector argument of ::slotted().</summary>
    public SelectorList? Argument { get; internal init; }

    /// <summary>Whether the pseudo-element is <c>::highlight(*)</c>, which selects every custom highlight.</summary>
    internal bool IsUniversalHighlight => Name == "highlight" && ArgumentText == "*";

    /// <summary>The specificity of a pseudo-element, except <c>::highlight(*)</c> whose specificity is zero (https://drafts.csswg.org/css-pseudo-4/#selectordef-highlight).</summary>
    public override Specificity Specificity => IsUniversalHighlight || IsUniversalViewTransition ? default : new Specificity(0, 0, 1) + (Argument?.MaxSpecificity ?? default);

    /// <summary>
    /// Whether the pseudo-element is a named view transition pseudo-element whose argument is <c>*</c> without class, whose
    /// specificity is zero (https://drafts.csswg.org/css-view-transitions-2/#named-view-transition-pseudo).
    /// </summary>
    internal bool IsUniversalViewTransition => ArgumentText == "*" && Name.StartsWith("view-transition-", StringComparison.Ordinal);
}
