using System.Diagnostics;
using Meziantou.Framework.WebPlatform.Html.Parsing;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-gethtmloptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-gethtmloptions"/>
[IdlInterface("GetHTMLOptions", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-gethtmloptions")]
public sealed class GetHtmlOptions
{
    /// <summary>
    /// Gets a value indicating whether the serializable shadow roots of the descendants are included in the result.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-gethtmloptions-serializableshadowroots"/>
    [IdlMember("serializableShadowRoots", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-gethtmloptions-serializableshadowroots")]
    public bool SerializableShadowRoots { get; init; }

    /// <summary>Gets the shadow roots to include in the result, in addition to the serializable ones.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-gethtmloptions-shadowroots"/>
    [IdlMember("shadowRoots", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-gethtmloptions-shadowroots")]
    public IReadOnlyList<ShadowRoot> ShadowRoots { get; init; } = [];
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sethtmlunsafeoptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sethtmlunsafeoptions"/>
[IdlInterface("SetHTMLUnsafeOptions", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sethtmlunsafeoptions")]
public sealed class SetHtmlUnsafeOptions
{
    /// <summary>The sanitizer, or null (the member does not exist) to sanitize nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sethtmlunsafeoptions-sanitizer"/>
    [IdlMember("sanitizer", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sethtmlunsafeoptions-sanitizer")]
    public SanitizerOrSanitizerConfigOrSanitizerPresets? Sanitizer { get; init; }

    /// <summary>Scripts are never executed by this library; the option only changes the parser scripting mode.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sethtmlunsafeoptions-runscripts"/>
    [IdlMember("runScripts", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sethtmlunsafeoptions-runscripts")]
    public bool RunScripts { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-parsehtmlunsafeoptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-parsehtmlunsafeoptions"/>
[IdlInterface("ParseHTMLUnsafeOptions", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-parsehtmlunsafeoptions")]
public sealed class ParseHtmlUnsafeOptions
{
    /// <summary>The sanitizer, or null (the member does not exist) to sanitize nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafeoptions-sanitizer"/>
    [IdlMember("sanitizer", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafeoptions-sanitizer")]
    public SanitizerOrSanitizerConfigOrSanitizerPresets? Sanitizer { get; init; }
}

/// <summary>
/// The (SetHTMLUnsafeOptions or TrustedHTMLParserOptions) union: the options of the unsafe HTML insertion methods, a
/// dictionary or the options vetted by a Trusted Types policy (<see cref="TrustedTypePolicy.CreateParserOptions"/>).
/// </summary>
public readonly struct SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions : IUnionValue, IEquatable<SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions>
{
    private SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions(object value) => Value = value;

    /// <summary>Gets the value of the union: a <see cref="Dom.SetHtmlUnsafeOptions"/> or a <see cref="Dom.TrustedHtmlParserOptions"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as options, or null when the union holds another type.</summary>
    [NotInIdl]
    public SetHtmlUnsafeOptions? SetHtmlUnsafeOptions => Value as SetHtmlUnsafeOptions;

    /// <summary>Gets the value as trusted parser options, or null when the union holds another type.</summary>
    [NotInIdl]
    public TrustedHtmlParserOptions? TrustedHtmlParserOptions => Value as TrustedHtmlParserOptions;

    /// <summary>Converts options to the union.</summary>
    [NotInIdl]
    public static implicit operator SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions(SetHtmlUnsafeOptions value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts trusted parser options to the union.</summary>
    [NotInIdl]
    public static implicit operator SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions(TrustedHtmlParserOptions value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from options.</summary>
    [NotInIdl]
    public static SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions FromSetHtmlUnsafeOptions(SetHtmlUnsafeOptions value) => value;

    /// <summary>Creates the union from trusted parser options.</summary>
    [NotInIdl]
    public static SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions FromTrustedHtmlParserOptions(TrustedHtmlParserOptions value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions left, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions left, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions right) => !left.Equals(right);
}

/// <summary>
/// The (ParseHTMLUnsafeOptions or TrustedHTMLParserOptions) union: the options of <see cref="Document.ParseHtmlUnsafe(string, ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions?)"/>,
/// a dictionary or the options vetted by a Trusted Types policy (<see cref="TrustedTypePolicy.CreateParserOptions"/>).
/// </summary>
public readonly struct ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions : IUnionValue, IEquatable<ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions>
{
    private ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions(object value) => Value = value;

    /// <summary>Gets the value of the union: a <see cref="Dom.ParseHtmlUnsafeOptions"/> or a <see cref="Dom.TrustedHtmlParserOptions"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as options, or null when the union holds another type.</summary>
    [NotInIdl]
    public ParseHtmlUnsafeOptions? ParseHtmlUnsafeOptions => Value as ParseHtmlUnsafeOptions;

    /// <summary>Gets the value as trusted parser options, or null when the union holds another type.</summary>
    [NotInIdl]
    public TrustedHtmlParserOptions? TrustedHtmlParserOptions => Value as TrustedHtmlParserOptions;

    /// <summary>Converts options to the union.</summary>
    [NotInIdl]
    public static implicit operator ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions(ParseHtmlUnsafeOptions value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts trusted parser options to the union.</summary>
    [NotInIdl]
    public static implicit operator ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions(TrustedHtmlParserOptions value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from options.</summary>
    [NotInIdl]
    public static ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions FromParseHtmlUnsafeOptions(ParseHtmlUnsafeOptions value) => value;

    /// <summary>Creates the union from trusted parser options.</summary>
    [NotInIdl]
    public static ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions FromTrustedHtmlParserOptions(TrustedHtmlParserOptions value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions left, ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions left, ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions right) => !left.Equals(right);
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#fragment-parser-mode</summary>
internal enum FragmentParserMode
{
    /// <summary>The unsafe elements and attributes are removed, and declarative shadow roots are allowed.</summary>
    Safe,

    /// <summary>The unsafe elements and attributes are kept unless a sanitizer is supplied, and declarative shadow roots are allowed.</summary>
    Unsafe,

    /// <summary>The unsafe elements and attributes are kept unless a sanitizer is supplied, declarative shadow roots are not allowed, and the XML parser is used in XML documents.</summary>
    Legacy,
}

/// <summary>
/// The options of the HTML parsing algorithms: the members of a <c>SetHTMLOptions</c>, <c>SetHTMLUnsafeOptions</c> or
/// <c>ParseHTMLUnsafeOptions</c> dictionary (a null <see cref="Sanitizer"/> does not exist), or a
/// <see cref="TrustedHtmlParserOptions"/> object (<see cref="Trusted"/>).
/// </summary>
internal readonly record struct FragmentParserOptions(SanitizerOrSanitizerConfigOrSanitizerPresets? Sanitizer, bool RunScripts, TrustedHtmlParserOptions? Trusted = null)
{
    public static FragmentParserOptions FromSafe(SetHtmlOptions? options) => new(options?.Sanitizer ?? SanitizerPresets.Default, RunScripts: false);

    public static FragmentParserOptions FromUnsafe(SetHtmlUnsafeOptions? options) => new(options?.Sanitizer, options?.RunScripts ?? false);

    public static FragmentParserOptions FromUnsafe(SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options) => options?.Value switch
    {
        TrustedHtmlParserOptions trusted => new(null, RunScripts: false, trusted),
        SetHtmlUnsafeOptions dictionary => FromUnsafe(dictionary),
        _ => default,
    };

    public static FragmentParserOptions FromUnsafe(ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options) => options?.Value switch
    {
        TrustedHtmlParserOptions trusted => new(null, RunScripts: false, trusted),
        ParseHtmlUnsafeOptions dictionary => new(dictionary.Sanitizer, RunScripts: false),
        _ => default,
    };

    /// <summary>
    /// The runScripts of the options: the <see cref="TrustedHtmlParserOptions"/> runScripts
    /// (https://w3c.github.io/trusted-types/dist/spec/#trustedhtmlparseroptions-runscripts), else the member of the dictionary.
    /// </summary>
    public bool ShouldRunScripts => Trusted?.RunScripts ?? RunScripts;

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#parser-options-need-vetting: the dictionary enables the scripts or has
    /// a sanitizer that is not an empty configuration.
    /// </summary>
    public bool NeedsVetting => Trusted is null && (RunScripts || (Sanitizer is { } sanitizer && !(sanitizer.Value is SanitizerConfig config && IsEmpty(config))));

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#get-a-sanitizer-config-from-options: the
    /// configuration of the sanitizer of the options, or null when the options do not sanitize.
    /// </summary>
    public SanitizerConfiguration? GetSanitizerConfiguration(bool safe)
    {
        if (Trusted is { } trusted)
        {
            Debug.Assert(trusted.SanitizerConfiguration is not null || !safe);

            // The configuration was configured with the permissive defaults, as for an unsafe method
            return trusted.SanitizerConfiguration;
        }

        if (Sanitizer is { } sanitizer)
            return Dom.Sanitizer.FromOptions(sanitizer, safe).Configuration;

        return safe ? Dom.Sanitizer.FromOptions(SanitizerPresets.Default, safe).Configuration : null;
    }

    private static bool IsEmpty(SanitizerConfig config) => config is
    {
        Elements: null,
        RemoveElements: null,
        ReplaceWithChildrenElements: null,
        ProcessingInstructions: null,
        RemoveProcessingInstructions: null,
        Attributes: null,
        RemoveAttributes: null,
        Comments: null,
        DataAttributes: null,
        JavaScriptUrls: null,
    };
}

/// <summary>The algorithms shared by the dynamic markup insertion APIs (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html).</summary>
internal static class DynamicMarkup
{
    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#fragment-serializing-algorithm-steps</summary>
    public static string FragmentSerialize(Node node, bool requireWellFormed)
    {
        if (node.NodeDocument.IsHtmlDocument)
            return HtmlSerializer.SerializeChildren(node);

        return Xml.XmlSerialization.SerializeChildren(node, requireWellFormed);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#fragment-parsing-algorithm-steps</summary>
    public static DocumentFragment FragmentParse(Node target, string markup, FragmentParserOptions options, FragmentParserMode mode)
    {
        // The sanitization is not supported for XML documents
        if (mode == FragmentParserMode.Legacy && !target.NodeDocument.IsHtmlDocument)
            return XmlDocumentParser.ParseFragment(target, markup);

        var safe = mode == FragmentParserMode.Safe;
        var sanitizer = options.GetSanitizerConfiguration(safe);
        var runScripts = options.ShouldRunScripts;
        Debug.Assert(!runScripts || !safe);
        var scriptingMode = runScripts ? ParserScriptingMode.Fragment : ParserScriptingMode.Inert;
        var allowDeclarativeShadowRoots = mode != FragmentParserMode.Legacy;
        if (sanitizer is null)
            return HtmlDocumentParser.ParseFragment(target, markup, allowDeclarativeShadowRoots, scriptingMode, errorHandler: null);

        // The nodes are sanitized while they are parsed, as in Blink, so the elements removed by the sanitizer have no side
        // effect (custom element constructors, declarative shadow roots), then the result is sanitized (it does not change
        // the nodes sanitized by the parser)
        var configuration = SanitizerConfiguration.GetEffectiveConfiguration(sanitizer, safe);
        var fragment = HtmlDocumentParser.ParseFragment(target, markup, allowDeclarativeShadowRoots, scriptingMode, errorHandler: null, configuration);
        SanitizerConfiguration.Sanitize(fragment, configuration, safe: false);
        return fragment;
    }

    /// <summary>
    /// Parses the HTML that the user pastes in editable content, in the context of the element of the insertion point. The
    /// fragment is sanitized as the browsers sanitize the content of the clipboard: the elements, the attributes and the
    /// URLs that run scripts are removed (the "remove unsafe" step of the sanitizer of <c>setHTML()</c>), with the comments
    /// (e.g. the fragment markers of the clipboard of Windows) and the elements of the head of the source document.
    /// </summary>
    public static DocumentFragment ParsePastedFragment(Element context, string html)
    {
        if (IsHtmlElementOfHtmlDocument(context))
        {
            context = CreateBodyElement(context.NodeDocument);
        }

        using var ceReactions = CEReactionsScope.Enter();
        var configuration = new SanitizerConfiguration { Comments = false };
        configuration.ApplyDefaults(permissiveDefaults: true);
        foreach (var name in PastedFragmentRemovedElements)
        {
            configuration.RemoveElement(new SanitizerName(name, Namespaces.Html));
        }

        configuration = SanitizerConfiguration.GetEffectiveConfiguration(configuration, safe: true);
        var fragment = HtmlDocumentParser.ParseFragment(context, html, allowDeclarativeShadowRoots: false, ParserScriptingMode.Inert, errorHandler: null, configuration);
        SanitizerConfiguration.Sanitize(fragment, configuration, safe: false);
        return fragment;
    }

    private static readonly string[] PastedFragmentRemovedElements = ["link", "meta", "noscript", "style", "title"];

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#set-and-filter-html</summary>
    public static void SetAndFilterHtml(Node target, string html, FragmentParserOptions options, FragmentParserMode mode)
    {
        using var ceReactions = CEReactionsScope.Enter();
        if (ParseHtmlForInsertion(target, html, options, mode) is { } fragment)
        {
            target.ReplaceAll(fragment);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#filter-and-pre-insert-html</summary>
    public static void FilterAndPreInsertHtml(Node parent, Node? child, string html, FragmentParserOptions options, FragmentParserMode mode)
    {
        using var ceReactions = CEReactionsScope.Enter();
        if (ParseHtmlForInsertion(parent, html, options, mode) is { } fragment)
        {
            parent.PreInsert(fragment, child);
        }
    }

    /// <summary>The <c>prependHTML()</c> methods: <see cref="FilterAndPreInsertHtml"/> before the first child of <paramref name="target"/> (before the parsing).</summary>
    public static void FilterAndPrependHtml(Node target, string html, FragmentParserOptions options, FragmentParserMode mode)
        => FilterAndPreInsertHtml(target, target.FirstChild, html, options, mode);

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#filter-and-replace-with-html</summary>
    public static void FilterAndReplaceWithHtml(Node parent, Node node, string html, FragmentParserOptions options, FragmentParserMode mode)
    {
        using var ceReactions = CEReactionsScope.Enter();
        if (ParseHtmlForInsertion(parent, html, options, mode) is { } fragment)
        {
            parent.Replace(node, fragment);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#parse-html-for-insertion: null when the safe
    /// methods do not modify the children of a script element.
    /// </summary>
    private static DocumentFragment? ParseHtmlForInsertion(Node target, string html, FragmentParserOptions options, FragmentParserMode mode)
    {
        if (mode == FragmentParserMode.Safe && IsScriptElement(GetParserContextElement(target)))
            return null;

        return FragmentParse(target, html, options, mode);
    }

    /// <summary>https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html</summary>
    /// <exception cref="DomException">The context element is a script element.</exception>
    public static HtmlStreamWriter StreamHtml(Node target, Node? referenceChild, FragmentParserOptions options, bool safe)
    {
        // Streaming into a script element would prepare it with only a part of its source text
        if (IsScriptElement(GetParserContextElement(target)))
            throw new DomException("HTML cannot be streamed into a script element", "NotSupportedError");

        // The configuration is copied: a change of the Sanitizer object while streaming does not affect the parser
        var sanitizer = options.GetSanitizerConfiguration(safe);
        var configuration = sanitizer is null ? null : SanitizerConfiguration.GetEffectiveConfiguration(sanitizer.Clone(), safe);
        var scriptingMode = !safe && options.ShouldRunScripts ? ParserScriptingMode.Fragment : ParserScriptingMode.Inert;
        return HtmlDocumentParser.CreateStreamingParser(target, referenceChild, scriptingMode, configuration);
    }

    /// <summary>
    /// The Trusted Types check of the unsafe streaming methods (https://whatpr.org/html/12758/dynamic-markup-insertion.html#dom-element-streamhtmlunsafe):
    /// the HTML is not known when the stream is created, so the options must be <see cref="TrustedHtmlParserOptions"/> or
    /// the default policy must create them.
    /// </summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not create <c>TrustedHTMLParserOptions</c>.</exception>
    public static FragmentParserOptions GetCompliantStreamingOptions(Node node, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options, string sink)
        => TrustedTypesAlgorithms.GetCompliantParserOptions(node.NodeDocument, FragmentParserOptions.FromUnsafe(options), throwIfMissing: true, sink);

    /// <summary>https://whatpr.org/html/12758/dynamic-markup-insertion.html#parent-for-html-streaming</summary>
    /// <exception cref="DomException">The node has no parent, or its parent is not a valid parent (see <see cref="GetParentForHtmlInsertion"/>).</exception>
    public static Node GetParentForHtmlStreaming(Node node)
        => GetParentForHtmlInsertion(node) ?? throw new DomException("The node has no parent", "HierarchyRequestError");

    /// <summary>https://whatpr.org/html/12758/dynamic-markup-insertion.html#determine-the-parser-context-element</summary>
    public static Element? GetParserContextElement(Node target) => target as Element ?? (target as DocumentFragment)?.Host;

    /// <summary>Whether <paramref name="element"/> is an HTML or SVG script element.</summary>
    public static bool IsScriptElement(Element? element) => element is not null && element.LocalName == "script" && element.NamespaceUri is Namespaces.Html or Namespaces.Svg;

    /// <summary>https://whatpr.org/html/12753/dynamic-markup-insertion.html#target-for-html-insertion</summary>
    public static Node GetTargetForHtmlInsertion(Node target) => target is HtmlTemplateElement template ? template.Content : target;

    /// <summary>https://whatpr.org/html/12753/dynamic-markup-insertion.html#parent-for-html-insertion</summary>
    /// <exception cref="DomException">The parent is a template element, a document or a document fragment without a host.</exception>
    public static Node? GetParentForHtmlInsertion(Node node)
    {
        var parent = node.ParentNode;
        if (parent is HtmlTemplateElement)
            throw new DomException("The HTML cannot be inserted next to a child of a template element", "HierarchyRequestError");

        if (parent is null or Element or DocumentFragment { Host: not null })
            return parent;

        throw new DomException("The parent of the node is not an element", "HierarchyRequestError");
    }

    /// <summary>Returns true when the context element must be replaced by a new body element (insertAdjacentHTML, createContextualFragment).</summary>
    public static bool IsHtmlElementOfHtmlDocument(Element element) => element.NodeDocument.IsHtmlDocument && element.IsHtml(TagId.Html);

    public static Element CreateBodyElement(Document document) => ElementFactory.CreateElement(document, "body", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
}

public partial class Element
{
    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-innerhtml</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-innerhtml"/>
    [IdlMember("innerHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-innerhtml")]
    public string InnerHtml
    {
        get => DynamicMarkup.FragmentSerialize(this, requireWellFormed: true);
        set => SetInnerHtmlCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, value ?? "", default, "Element innerHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    /// <summary>The innerHTML setter with a <see cref="TrustedHtml"/> value (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-innerhtml).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-innerhtml"/>
    [IdlMember("innerHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-innerhtml")]
    public void SetInnerHtml(TrustedHtml html)
    {
        ArgumentNullException.ThrowIfNull(html);
        SetInnerHtmlCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, default, "Element innerHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    private void SetInnerHtmlCore((string Html, FragmentParserOptions Options) compliantInput)
        => DynamicMarkup.SetAndFilterHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), compliantInput.Html, compliantInput.Options, FragmentParserMode.Legacy);

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-outerhtml</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-outerhtml"/>
    [IdlMember("outerHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-outerhtml")]
    public string OuterHtml
    {
        get
        {
            if (NodeDocument.IsHtmlDocument)
                return HtmlSerializer.SerializeNode(this);

            // The XML serialization of a fictional node whose only child is this element
            return Xml.XmlSerialization.Serialize(this, requireWellFormed: true);
        }
        set => SetOuterHtmlCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, value ?? "", default, "Element outerHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    /// <summary>The outerHTML setter with a <see cref="TrustedHtml"/> value (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-outerhtml).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-outerhtml"/>
    [IdlMember("outerHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-outerhtml")]
    public void SetOuterHtml(TrustedHtml html)
    {
        ArgumentNullException.ThrowIfNull(html);
        SetOuterHtmlCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, default, "Element outerHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    private void SetOuterHtmlCore((string Html, FragmentParserOptions Options) compliantInput)
    {
        var parent = ParentNode;
        if (parent is null)
            return;

        if (parent is Document)
            throw new DomException("The element's parent is a document", "NoModificationAllowedError");

        if (parent is DocumentFragment)
        {
            parent = DynamicMarkup.CreateBodyElement(NodeDocument);
        }

        using var ceReactions = CEReactionsScope.Enter();
        var fragment = DynamicMarkup.FragmentParse(parent, compliantInput.Html, compliantInput.Options, FragmentParserMode.Legacy);
        ParentNode!.Replace(this, fragment);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-insertadjacenthtml</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-insertadjacenthtml"/>
    [IdlMember("insertAdjacentHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-insertadjacenthtml")]
    public void InsertAdjacentHtml(string position, string text)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(text);
        InsertAdjacentHtmlCore(position, TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, text, default, "Element insertAdjacentHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-insertadjacenthtml</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-insertadjacenthtml"/>
    [IdlMember("insertAdjacentHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-insertadjacenthtml")]
    public void InsertAdjacentHtml(string position, TrustedHtml text)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(text);
        InsertAdjacentHtmlCore(position, TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, text, default, "Element insertAdjacentHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    private void InsertAdjacentHtmlCore(string position, (string Html, FragmentParserOptions Options) compliantInput)
    {
        Node? context;
        var isBefore = InfraAscii.EqualsIgnoreAsciiCase(position, "beforebegin");
        var isAfter = InfraAscii.EqualsIgnoreAsciiCase(position, "afterend");
        var isAfterBegin = InfraAscii.EqualsIgnoreAsciiCase(position, "afterbegin");
        var isBeforeEnd = InfraAscii.EqualsIgnoreAsciiCase(position, "beforeend");
        if (isBefore || isAfter)
        {
            context = ParentNode;
            if (context is null or Document)
                throw new DomException("The element has no parent element", "NoModificationAllowedError");
        }
        else if (isAfterBegin || isBeforeEnd)
        {
            context = this;
        }
        else
        {
            throw DomException.Syntax($"'{position}' is not a valid position");
        }

        if (context is not Element contextElement || DynamicMarkup.IsHtmlElementOfHtmlDocument(contextElement))
        {
            context = DynamicMarkup.CreateBodyElement(NodeDocument);
        }

        using var ceReactions = CEReactionsScope.Enter();
        var fragment = DynamicMarkup.FragmentParse(context, compliantInput.Html, compliantInput.Options, FragmentParserMode.Legacy);
        if (isBefore)
        {
            ParentNode!.Insert(fragment, this);
        }
        else if (isAfterBegin)
        {
            Insert(fragment, FirstChild);
        }
        else if (isBeforeEnd)
        {
            AppendNode(fragment);
        }
        else
        {
            ParentNode!.Insert(fragment, NextSibling);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-gethtml</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-gethtml"/>
    [IdlMember("getHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-gethtml")]
    public string GetHtml(GetHtmlOptions? options = null) => HtmlSerializer.SerializeChildren(this, options?.SerializableShadowRoots ?? false, options?.ShadowRoots);

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtml</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtml"/>
    [IdlMember("setHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtml")]
    public void SetHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.SetAndFilterHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtmlunsafe</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtmlunsafe"/>
    [IdlMember("setHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtmlunsafe")]
    public void SetHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        SetHtmlUnsafeCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element setHTMLUnsafe"));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtmlunsafe</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtmlunsafe"/>
    [IdlMember("setHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-element-sethtmlunsafe")]
    public void SetHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        SetHtmlUnsafeCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "Element setHTMLUnsafe"));
    }

    private void SetHtmlUnsafeCore((string Html, FragmentParserOptions Options) compliantInput)
        => DynamicMarkup.SetAndFilterHtml(DynamicMarkup.GetTargetForHtmlInsertion(this), compliantInput.Html, compliantInput.Options, FragmentParserMode.Unsafe);
}

public sealed partial class ShadowRoot
{

    /// <summary>Gets or sets the event handler of the <c>slotchange</c> event.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowroot-onslotchange"/>
    [IdlMember("onslotchange", "https://dom.spec.whatwg.org/#dom-shadowroot-onslotchange")]
    public Action<Event>? OnSlotChange
    {
        get => GetEventHandler("slotchange");
        set => SetEventHandler("slotchange", value);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-innerhtml</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-innerhtml"/>
    [IdlMember("innerHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-innerhtml")]
    public string InnerHtml
    {
        get => DynamicMarkup.FragmentSerialize(this, requireWellFormed: true);
        set => SetInnerHtmlCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, value ?? "", default, "ShadowRoot innerHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    /// <summary>The innerHTML setter with a <see cref="TrustedHtml"/> value (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-innerhtml).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-innerhtml"/>
    [IdlMember("innerHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-innerhtml")]
    public void SetInnerHtml(TrustedHtml html)
    {
        ArgumentNullException.ThrowIfNull(html);
        SetInnerHtmlCore(TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, default, "ShadowRoot innerHTML", NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    private void SetInnerHtmlCore((string Html, FragmentParserOptions Options) compliantInput)
        => DynamicMarkup.SetAndFilterHtml(this, compliantInput.Html, compliantInput.Options, FragmentParserMode.Legacy);

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-gethtml</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-gethtml"/>
    [IdlMember("getHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-gethtml")]
    public string GetHtml(GetHtmlOptions? options = null) => HtmlSerializer.SerializeChildren(this, options?.SerializableShadowRoots ?? false, options?.ShadowRoots);

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtml</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtml"/>
    [IdlMember("setHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtml")]
    public void SetHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        DynamicMarkup.SetAndFilterHtml(this, html, FragmentParserOptions.FromSafe(options), FragmentParserMode.Safe);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtmlunsafe</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtmlunsafe"/>
    [IdlMember("setHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtmlunsafe")]
    public void SetHtmlUnsafe(string html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot setHTMLUnsafe");
        DynamicMarkup.SetAndFilterHtml(this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtmlunsafe</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtmlunsafe"/>
    [IdlMember("setHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-shadowroot-sethtmlunsafe")]
    public void SetHtmlUnsafe(TrustedHtml html, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(NodeDocument, html, FragmentParserOptions.FromUnsafe(options), "ShadowRoot setHTMLUnsafe");
        DynamicMarkup.SetAndFilterHtml(this, compliantHtml, compliantOptions, FragmentParserMode.Unsafe);
    }
}

public partial class Document
{
    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafe</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options or the value are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafe"/>
    [IdlMember("parseHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafe")]
    public static Document ParseHtmlUnsafe(string html, ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(CurrentGlobalDocument, html, FragmentParserOptions.FromUnsafe(options), "Document parseHTMLUnsafe");
        return ParseAndSanitize(compliantHtml, compliantOptions.GetSanitizerConfiguration(safe: false), safe: false);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafe</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the options are not trusted.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafe"/>
    [IdlMember("parseHTMLUnsafe", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtmlunsafe")]
    public static Document ParseHtmlUnsafe(TrustedHtml html, ParseHtmlUnsafeOptionsOrTrustedHtmlParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var (compliantHtml, compliantOptions) = TrustedTypesAlgorithms.GetCompliantInput(CurrentGlobalDocument, html, FragmentParserOptions.FromUnsafe(options), "Document parseHTMLUnsafe");
        return ParseAndSanitize(compliantHtml, compliantOptions.GetSanitizerConfiguration(safe: false), safe: false);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtml</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtml"/>
    [IdlMember("parseHTML", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-parsehtml")]
    public static Document ParseHtml(string html, SetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        return ParseAndSanitize(html, FragmentParserOptions.FromSafe(options).GetSanitizerConfiguration(safe: true), safe: true);
    }

    /// <summary>
    /// Serializes the children of the document (the doctype, the comments, the processing instructions and the document
    /// element) with the HTML fragment serialization algorithm
    /// (https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments), as <see cref="Element.GetHtml"/>
    /// does for an element. The HTML serialization is used even for XML documents. As defined by the algorithm, the doctype
    /// is serialized as <c>&lt;!DOCTYPE name&gt;</c>: its public and system identifiers are not serialized.
    /// </summary>
    [NotInIdl]
    public string GetHtml(GetHtmlOptions? options = null) => HtmlSerializer.SerializeChildren(this, options?.SerializableShadowRoots ?? false, options?.ShadowRoots);

    /// <summary>Same as <see cref="GetHtml"/>, but writes the serialization to <paramref name="writer"/> instead of building a string.</summary>
    [NotInIdl]
    public void WriteHtml(TextWriter writer, GetHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        HtmlSerializer.SerializeChildren(this, writer, options?.SerializableShadowRoots ?? false, options?.ShadowRoots);
    }

    private static Document ParseAndSanitize(string html, SanitizerConfiguration? sanitizer, bool safe)
    {
        var document = new HtmlDocumentParser(new HtmlParserOptions { ScriptingEnabled = false, AllowDeclarativeShadowRoots = true }).Parse(html);
        document.OriginDocument = GetOriginDocument(CurrentGlobalDocument);
        if (sanitizer is not null)
        {
            SanitizerConfiguration.Sanitize(document, sanitizer, safe);
        }

        return document;
    }
}

public sealed partial class Range
{
    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-range-createcontextualfragment</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-range-createcontextualfragment"/>
    [IdlMember("createContextualFragment", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-range-createcontextualfragment")]
    public DocumentFragment CreateContextualFragment(string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return CreateContextualFragmentCore(TrustedTypesAlgorithms.GetCompliantInput(_registeredDocument, fragment, CreateContextualFragmentOptions, "Range createContextualFragment", StartContainer.NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-range-createcontextualfragment</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-range-createcontextualfragment"/>
    [IdlMember("createContextualFragment", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-range-createcontextualfragment")]
    public DocumentFragment CreateContextualFragment(TrustedHtml fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return CreateContextualFragmentCore(TrustedTypesAlgorithms.GetCompliantInput(_registeredDocument, fragment, CreateContextualFragmentOptions, "Range createContextualFragment", StartContainer.NodeDocument.IsHtmlDocument, optionsFromAuthor: false));
    }

    /// <summary>
    /// Creates the fragment of the <c>insertHTML</c> editing command
    /// (https://w3c.github.io/editing/docs/execCommand/#the-inserthtml-command), whose sink is "Document execCommand". The
    /// specification calls <c>createContextualFragment()</c>, whose scripts run when they are inserted; as in the browsers, the
    /// scripts of this fragment are already started, as the ones of <c>innerHTML</c>: they never run.
    /// </summary>
    internal DocumentFragment CreateContextualFragmentForEditing(string fragment)
    {
        return CreateContextualFragmentCore((TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, _registeredDocument, fragment, "Document execCommand"), default));
    }

    /// <summary>The options of <c>createContextualFragment()</c>: the scripts run when the fragment is inserted, unless a default policy disables them.</summary>
    private static FragmentParserOptions CreateContextualFragmentOptions => new(Sanitizer: null, RunScripts: true);

    private DocumentFragment CreateContextualFragmentCore((string Html, FragmentParserOptions Options) compliantInput)
    {
        var node = StartContainer;
        var element = node switch
        {
            Element e => e,
            Text or Comment => node.ParentElement,
            _ => null,
        };

        if (element is null || DynamicMarkup.IsHtmlElementOfHtmlDocument(element))
        {
            element = DynamicMarkup.CreateBodyElement(node.NodeDocument);
        }

        using var ceReactions = CEReactionsScope.Enter();
        var result = DynamicMarkup.FragmentParse(element, compliantInput.Html, compliantInput.Options, FragmentParserMode.Legacy);

        // The html, head and body elements of the HTML namespace are replaced by their children, as in Blink and WebKit (WPT
        // domparsing/createContextualFragment-xhtml.xhtml): only the XML fragment parser returns them
        Node? next;
        for (var child = result.FirstChild; child is not null; child = next)
        {
            next = child.NextSibling;
            if (child is Element childElement && (childElement.IsHtml(TagId.Html) || childElement.IsHtml(TagId.Head) || childElement.IsHtml(TagId.Body)))
            {
                next = childElement.FirstChild ?? next;
                while (childElement.FirstChild is { } grandchild)
                {
                    result.InsertBefore(grandchild, childElement);
                }

                result.RemoveChild(childElement);
            }
        }

        return result;
    }
}
