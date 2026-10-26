using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>https://drafts.csswg.org/cssom/#the-stylesheet-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#stylesheet"/>
[IdlInterface("StyleSheet", "https://drafts.csswg.org/cssom/#stylesheet")]
public abstract class StyleSheet
{
    private protected StyleSheet(MediaList media)
    {
        Media = media;
        media.SetOwner(this);
    }

    /// <summary>Gets the media type of the style sheet, always <c>text/css</c>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheet-type"/>
    [IdlMember("type", "https://drafts.csswg.org/cssom/#dom-stylesheet-type")]
    public string Type => "text/css";

    /// <summary>https://drafts.csswg.org/cssom/#dom-stylesheet-href: the location of the style sheet (null for embedded and constructed style sheets).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheet-href"/>
    [IdlMember("href", "https://drafts.csswg.org/cssom/#dom-stylesheet-href")]
    public string? Href { get; private protected init; }

    /// <summary>https://drafts.csswg.org/cssom/#dom-stylesheet-ownernode: the element or processing instruction that created the style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheet-ownernode"/>
    [IdlMember("ownerNode", "https://drafts.csswg.org/cssom/#dom-stylesheet-ownernode")]
    public Node? OwnerNode { get; internal set; }

    /// <summary>https://drafts.csswg.org/cssom/#dom-stylesheet-parentstylesheet: the style sheet that imports this style sheet with an @import rule.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheet-parentstylesheet"/>
    [IdlMember("parentStyleSheet", "https://drafts.csswg.org/cssom/#dom-stylesheet-parentstylesheet")]
    public CssStyleSheet? ParentStyleSheet { get; internal set; }

    /// <summary>
    /// The document the style sheet applies to: the document of the owner node of the root style sheet, or the constructor
    /// document of a constructed style sheet (null for a style sheet that applies to no document). Its changes through the CSSOM
    /// change the version of the style sheets of this document (see <see cref="Cascade.CssStyleVersion"/>).
    /// </summary>
    internal Document? StyleDocument
    {
        get
        {
            var sheet = this;
            while (sheet.ParentStyleSheet is { } parent)
            {
                sheet = parent;
            }

            return sheet.OwnerNode?.NodeDocument ?? (sheet as CssStyleSheet)?.ConstructorDocument;
        }
    }

    /// <summary>https://drafts.csswg.org/cssom/#dom-stylesheet-title: the title of a link element is a reference to its attribute.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheet-title"/>
    [IdlMember("title", "https://drafts.csswg.org/cssom/#dom-stylesheet-title")]
    public string? Title { get; internal set; }

    /// <summary>Gets the media queries associated with the style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheet-media"/>
    [IdlMember("media", "https://drafts.csswg.org/cssom/#dom-stylesheet-media")]
    public MediaList Media { get; }

    /// <summary>https://drafts.csswg.org/cssom/#dom-stylesheet-disabled</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheet-disabled"/>
    [IdlMember("disabled", "https://drafts.csswg.org/cssom/#dom-stylesheet-disabled")]
    public bool Disabled
    {
        get;
        set
        {
            field = value;
            Cascade.CssStyleVersion.Increment(StyleDocument);
        }
    }
}

/// <summary>https://drafts.csswg.org/cssom/#dictdef-cssstylesheetinit</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#dictdef-cssstylesheetinit"/>
[IdlInterface("CSSStyleSheetInit", "https://drafts.csswg.org/cssom/#dictdef-cssstylesheetinit")]
public sealed class CssStyleSheetInit
{
    /// <summary>Gets the base URL used to resolve the relative URLs of the style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheetinit-baseurl"/>
    [IdlMember("baseURL", "https://drafts.csswg.org/cssom/#dom-cssstylesheetinit-baseurl")]
    public string? BaseUrl { get; init; }

    /// <summary>Gets the media queries associated with the style sheet, as a <c>MediaList</c> or a string.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheetinit-media"/>
    [IdlMember("media", "https://drafts.csswg.org/cssom/#dom-cssstylesheetinit-media")]
    public MediaListOrString Media { get; init; } = "";

    /// <summary>Gets a value indicating whether the style sheet is created disabled.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheetinit-disabled"/>
    [IdlMember("disabled", "https://drafts.csswg.org/cssom/#dom-cssstylesheetinit-disabled")]
    public bool Disabled { get; init; }
}

/// <summary>The (MediaList or DOMString) union.</summary>
public readonly struct MediaListOrString : IUnionValue, IEquatable<MediaListOrString>
{
    private MediaListOrString(object value) => Value = value;

    /// <summary>Gets the underlying value, either a <see cref="MediaList"/> or a string.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a <see cref="MediaList"/>, or null when the value is a string.</summary>
    [NotInIdl]
    public MediaList? MediaList => Value as MediaList;

    /// <summary>Gets the value as a string, or null when the value is a <see cref="MediaList"/>.</summary>
    [NotInIdl]
    public string? StringValue => Value as string;

    /// <summary>Creates a union value from a <see cref="MediaList"/>.</summary>
    [NotInIdl]
    public static implicit operator MediaListOrString(MediaList value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a union value from a string.</summary>
    [NotInIdl]
    public static implicit operator MediaListOrString(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a union value from a <see cref="MediaList"/>.</summary>
    [NotInIdl]
    public static MediaListOrString FromMediaList(MediaList value) => value;

    /// <summary>Creates a union value from a string.</summary>
    [NotInIdl]
    public static MediaListOrString FromString(string value) => value;

    /// <summary>Returns whether the other union value wraps the same value.</summary>
    [NotInIdl]
    public bool Equals(MediaListOrString other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is MediaListOrString other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Returns whether the two union values wrap the same value.</summary>
    [NotInIdl]
    public static bool operator ==(MediaListOrString left, MediaListOrString right) => left.Equals(right);

    /// <summary>Returns whether the two union values wrap different values.</summary>
    [NotInIdl]
    public static bool operator !=(MediaListOrString left, MediaListOrString right) => !left.Equals(right);
}

/// <summary>https://drafts.csswg.org/cssom/#the-cssstylesheet-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#cssstylesheet"/>
[IdlInterface("CSSStyleSheet", "https://drafts.csswg.org/cssom/#cssstylesheet")]
public sealed partial class CssStyleSheet : StyleSheet
{
    private readonly List<CssRule> _rules = [];
    private CssRuleList? _ruleList;

    /// <summary>Creates a constructed style sheet (https://drafts.csswg.org/cssom/#dom-cssstylesheet-cssstylesheet).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-cssstylesheet"/>
    [IdlMember("constructor", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-cssstylesheet")]
    public CssStyleSheet(CssStyleSheetInit? options = null)
        : this(Document.CurrentGlobalDocument, options)
    {
    }

    /// <summary>
    /// Creates a constructed style sheet whose constructor document is <paramref name="constructorDocument"/>: the sheet can
    /// only be adopted by this document and the shadow roots of its tree. The IDL constructor uses the associated document of
    /// the current global object, which is the document of the script's window when a script runs.
    /// </summary>
    [NotInIdl]
    public CssStyleSheet(Document constructorDocument, CssStyleSheetInit? options = null)
        : base(CreateMedia(options))
    {
        ArgumentNullException.ThrowIfNull(constructorDocument);
        IsConstructed = true;
        ConstructorDocument = constructorDocument;
        IsQuirksMode = constructorDocument.Mode == DocumentMode.Quirks;
        Disabled = options?.Disabled ?? false;
        if (options?.BaseUrl is { } baseUrl)
        {
            // The base URL is parsed relative to the base URL of the constructor document
            var url = Url.Parse(baseUrl, ConstructorDocument.DocumentBaseUrl.Serialize()) ?? throw new DomException("The base URL is invalid", "NotAllowedError");
            BaseUrl = url.Href;
        }
    }

    /// <summary>Creates the style sheet of a style element (https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block).</summary>
    internal CssStyleSheet(Element ownerNode, string text, string? media, string? title)
        : base(new MediaList(media))
    {
        OwnerNode = ownerNode;
        Title = string.IsNullOrEmpty(title) ? null : title;
        IsQuirksMode = ownerNode.NodeDocument.Mode == DocumentMode.Quirks;
        SetRules(new CssRuleParser(sheet: null) { Quirks = IsQuirksMode }.ParseStylesheet(text));
    }

    /// <summary>Creates the style sheet of a link element (https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet).</summary>
    /// <param name="location">The URL of the request (<see cref="StyleSheet.Href"/>).</param>
    /// <param name="baseUrl">The URL of the response, which the relative URLs of the style sheet are resolved against.</param>
    internal CssStyleSheet(Element ownerNode, string text, string? media, string? title, string location, string baseUrl, bool alternate, bool originClean)
        : base(new MediaList(media))
    {
        OwnerNode = ownerNode;
        Title = string.IsNullOrEmpty(title) ? null : title;
        Href = location;
        BaseUrl = baseUrl;
        Alternate = alternate;
        OriginClean = originClean;
        IsQuirksMode = ownerNode.NodeDocument.Mode == DocumentMode.Quirks;
        SetRules(new CssRuleParser(sheet: null) { Quirks = IsQuirksMode }.ParseStylesheet(text));
    }

    /// <summary>
    /// Creates the style sheet of an xml-stylesheet processing instruction
    /// (https://drafts.csswg.org/cssom/#requirements-on-user-agents-implementing-the-xml-stylesheet-processing-instruction).
    /// </summary>
    /// <param name="location">The URL of the response (<see cref="StyleSheet.Href"/>), which the relative URLs of the style sheet are resolved against.</param>
    internal CssStyleSheet(ProcessingInstruction ownerNode, string text, string location, string? media, string? title, bool alternate, bool originClean)
        : base(new MediaList(media))
    {
        OwnerNode = ownerNode;
        Href = location;
        BaseUrl = location;
        Title = string.IsNullOrEmpty(title) ? null : title;
        Alternate = alternate;
        OriginClean = originClean;
        SetRules(new CssRuleParser(sheet: null).ParseStylesheet(text));
    }

    /// <summary>Creates a style sheet that is not associated with a document or an element, to format the text of a style element.</summary>
    internal CssStyleSheet(string text)
        : base(new MediaList())
    {
        SetRules(new CssRuleParser(sheet: null).ParseStylesheet(text));
    }

    /// <summary>
    /// Creates the style sheet fetched for an @import rule (https://drafts.csswg.org/css-cascade-5/#fetch-an-import). Its
    /// media list is the one of the rule.
    /// </summary>
    /// <param name="location">The URL of the request (<see cref="StyleSheet.Href"/>).</param>
    /// <param name="baseUrl">The URL of the response, which the relative URLs of the style sheet are resolved against.</param>
    internal CssStyleSheet(CssImportRule ownerRule, CssStyleSheet parentStyleSheet, string text, string location, string baseUrl, bool originClean)
        : base(ownerRule.Media)
    {
        OwnerRule = ownerRule;
        ParentStyleSheet = parentStyleSheet;
        Href = location;
        BaseUrl = baseUrl;
        OriginClean = originClean;
        IsQuirksMode = parentStyleSheet.IsQuirksMode;
        SetRules(new CssRuleParser(sheet: null) { Quirks = IsQuirksMode }.ParseStylesheet(text));
    }

    private static MediaList CreateMedia(CssStyleSheetInit? options)
    {
        if (options is null)
            return new MediaList();

        return options.Media.Value switch
        {
            MediaList list => new MediaList(list.MediaText),
            string text => new MediaList(text),
            _ => new MediaList(),
        };
    }

    /// <summary>https://drafts.csswg.org/cssom/#concept-css-style-sheet-constructed-flag</summary>
    internal bool IsConstructed { get; }

    /// <summary>
    /// Whether the style sheet is parsed in quirks mode, as browsers do for the style sheets of a document in quirks mode (and
    /// the style sheets they import): the quirky lengths and colors are valid in its declarations and its supports conditions
    /// (https://quirks.spec.whatwg.org/#the-hashless-hex-color-quirk, https://quirks.spec.whatwg.org/#the-unitless-length-quirk).
    /// </summary>
    internal bool IsQuirksMode { get; }

    /// <summary>The document that constructed the style sheet (https://drafts.csswg.org/cssom/#concept-css-style-sheet-constructor-document), set when it is first adopted.</summary>
    internal Document? ConstructorDocument { get; set; }

    /// <summary>https://drafts.csswg.org/cssom/#concept-css-style-sheet-stylesheet-base-url</summary>
    internal string? BaseUrl { get; }

    /// <summary>
    /// The referrer and the referrer policy of the requests of the resources of a fetched style sheet, or null for the other
    /// style sheets, whose resources are requested with the referrer and the referrer policy of the document.
    /// </summary>
    internal CssResourceReferrer? ResourceReferrer { get; init; }

    /// <summary>https://drafts.csswg.org/cssom/#concept-css-style-sheet-disallow-modification-flag</summary>
    private bool DisallowModification { get; set; }

    /// <summary>https://drafts.csswg.org/cssom/#concept-css-style-sheet-alternate-flag: the style sheet only applies when its CSS style sheet set is selected.</summary>
    internal bool Alternate { get; }

    /// <summary>
    /// https://drafts.csswg.org/cssom/#concept-css-style-sheet-origin-clean-flag: false for a style sheet imported from another
    /// origin, whose rules cannot be read or modified through the CSSOM.
    /// </summary>
    internal bool OriginClean { get; } = true;

    /// <summary>Schedules the fetch of the resources used by the rendering of the document of the owner node of the root style sheet, whose styles may have changed.</summary>
    internal void RequestCssResourcesUpdate() => RootStyleSheet.OwnerNode?.NodeDocument.RequestCssResourcesUpdate();

    /// <summary>The style sheet that is not imported by another style sheet (the style sheet of the element, or a constructed style sheet).</summary>
    internal CssStyleSheet RootStyleSheet
    {
        get
        {
            var sheet = this;
            while (sheet.ParentStyleSheet is { } parent)
            {
                sheet = parent;
            }

            return sheet;
        }
    }

    /// <summary>The state of the fetches of the @import rules of the style sheet and of its imported style sheets (set on the root style sheet).</summary>
    internal CssImportLoadState? ImportLoadState { get; set; }

    internal List<CssRule> RuleList => _rules;

    private void SetRules(List<CssRule> rules)
    {
        foreach (var rule in _rules)
        {
            rule.SetParentStyleSheet(null);
        }

        // The CSSRuleList object is [SameObject]: the list is updated in place
        _rules.Clear();
        _rules.AddRange(rules);
        foreach (var rule in rules)
        {
            rule.ParentRule = null;
            rule.SetParentStyleSheet(this);
        }
    }

    /// <summary>https://drafts.csswg.org/cssom/#dom-cssstylesheet-ownerrule: the @import rule that imports this style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-ownerrule"/>
    [IdlMember("ownerRule", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-ownerrule")]
    public CssRule? OwnerRule { get; }

    /// <summary>Gets the list of the CSS rules of the style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-cssrules"/>
    [IdlMember("cssRules", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-cssrules")]
    public CssRuleList CssRules
    {
        get
        {
            ThrowIfNotOriginClean();
            return _ruleList ??= new CssRuleList(_rules);
        }
    }

    /// <summary>Gets the list of the CSS rules of the style sheet (legacy alias of <c>cssRules</c>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-rules"/>
    [IdlMember("rules", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-rules")]
    public CssRuleList Rules => CssRules;

    /// <summary>
    /// Inserts a rule, parsed from the given text, at the given index of the style sheet and returns its index.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-insertrule"/>
    [IdlMember("insertRule", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-insertrule")]
    public uint InsertRule(string rule, uint index = 0)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ThrowIfNotOriginClean();
        if (DisallowModification)
            throw new DomException("The style sheet cannot be modified", "NotAllowedError");

        var result = new CssRuleParser(this).InsertRule(_rules, rule, index, CssRuleContext.TopLevel, parentRule: null, this, IsConstructed);
        if (_rules[(int)result] is CssImportRule importRule)
        {
            CssImportFetching.FetchImport(importRule);
        }

        return result;
    }

    /// <summary>Removes the rule at the given index from the style sheet.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-deleterule"/>
    [IdlMember("deleteRule", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-deleterule")]
    public void DeleteRule(uint index)
    {
        ThrowIfNotOriginClean();
        if (DisallowModification)
            throw new DomException("The style sheet cannot be modified", "NotAllowedError");

        CssRuleParser.RemoveRule(_rules, index, this);
    }

    /// <summary>https://drafts.csswg.org/cssom/#dom-cssstylesheet-replace: the style sheet is parsed synchronously.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-replace"/>
    [IdlMember("replace", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-replace")]
    public Task<CssStyleSheet> Replace(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsConstructed || DisallowModification)
            return Task.FromException<CssStyleSheet>(new DomException("The style sheet cannot be modified", "NotAllowedError"));

        SetRules(new CssRuleParser(sheet: null) { Quirks = IsQuirksMode }.ParseStylesheet(text, allowImport: false));
        Cascade.CssStyleVersion.Increment(this);
        return Task.FromResult(this);
    }

    /// <summary>
    /// Synchronously replaces the rules of a constructed style sheet with the rules parsed from the given text.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-replacesync"/>
    [IdlMember("replaceSync", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-replacesync")]
    public void ReplaceSync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsConstructed || DisallowModification)
            throw new DomException("The style sheet cannot be modified", "NotAllowedError");

        SetRules(new CssRuleParser(sheet: null) { Quirks = IsQuirksMode }.ParseStylesheet(text, allowImport: false));
        Cascade.CssStyleVersion.Increment(this);
    }

    /// <summary>https://drafts.csswg.org/cssom/#dom-cssstylesheet-addrule</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-addrule"/>
    [IdlMember("addRule", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-addrule")]
    public int AddRule(string selector = "undefined", string style = "undefined", uint? index = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(style);
        var rule = selector + " { " + style + " }";
        if (style.Length == 0)
        {
            rule = selector + " { }";
        }

        InsertRule(rule, index ?? (uint)_rules.Count);
        return -1;
    }

    /// <summary>
    /// Removes the rule at the given index from the style sheet (legacy alias of <c>deleteRule()</c>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-cssstylesheet-removerule"/>
    [IdlMember("removeRule", "https://drafts.csswg.org/cssom/#dom-cssstylesheet-removerule")]
    public void RemoveRule(uint index = 0) => DeleteRule(index);

    private void ThrowIfNotOriginClean()
    {
        if (!OriginClean)
            throw new DomException("The rules of a style sheet imported from another origin cannot be accessed", "SecurityError");
    }
}

/// <summary>https://drafts.csswg.org/cssom/#the-stylesheetlist-interface</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#stylesheetlist"/>
[IdlInterface("StyleSheetList", "https://drafts.csswg.org/cssom/#stylesheetlist")]
public sealed class StyleSheetList : IReadOnlyList<CssStyleSheet>
{
    private readonly Node _root;
    private List<CssStyleSheet> _sheets = [];
    private long _version = -1;

    internal StyleSheetList(Node root)
    {
        _root = root;
    }

    /// <summary>The style sheets of the elements of the tree, in tree order (https://drafts.csswg.org/cssom/#documentorshadowroot-document-or-shadow-root-css-style-sheets).</summary>
    private List<CssStyleSheet> Sheets
    {
        get
        {
            // The style sheet of a processing instruction or of a link element is set when it is fetched, which does not mutate the tree
            var document = _root.NodeDocument;
            var version = ((long)document.StyleSheetsVersion << 32) | (uint)document.DomVersion;
            if (version != _version)
            {
                var sheets = new List<CssStyleSheet>();
                var node = _root.FirstChild;
                while (node is not null)
                {
                    if (node is Element element && element.GetLinkStyleSheet() is { } sheet)
                    {
                        sheets.Add(sheet);
                    }
                    else if (node is ProcessingInstruction { Sheet: { } processingInstructionSheet })
                    {
                        sheets.Add(processingInstructionSheet);
                    }

                    // Pre-order traversal of the tree (the shadow trees have their own style sheets)
                    if (node.FirstChild is { } child)
                    {
                        node = child;
                        continue;
                    }

                    while (node is not null && node != _root && node.NextSibling is null)
                    {
                        node = node.ParentNode;
                    }

                    node = node is null || node == _root ? null : node.NextSibling;
                }

                _sheets = sheets;
                _version = version;
            }

            return _sheets;
        }
    }

    /// <summary>Returns the style sheet at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheetlist-item"/>
    [IdlMember("item", "https://drafts.csswg.org/cssom/#dom-stylesheetlist-item")]
    public CssStyleSheet? Item(uint index)
    {
        var sheets = Sheets;
        return index < (uint)sheets.Count ? sheets[(int)index] : null;
    }

    /// <summary>Gets the style sheet at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheetlist-item"/>
    [IdlMember("getter", "https://drafts.csswg.org/cssom/#dom-stylesheetlist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssStyleSheet? this[uint index] => Item(index);

    /// <summary>Gets the style sheet at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public CssStyleSheet this[int index] => Sheets[index];

    /// <summary>Gets the number of style sheets in the list.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-stylesheetlist-length"/>
    [IdlMember("length", "https://drafts.csswg.org/cssom/#dom-stylesheetlist-length")]
    public uint Length => (uint)Sheets.Count;

    /// <summary>Gets the number of style sheets in the list.</summary>
    [NotInIdl]
    public int Count => Sheets.Count;

    /// <summary>Returns an enumerator that iterates through the style sheets of the list.</summary>
    [NotInIdl]
    public IEnumerator<CssStyleSheet> GetEnumerator() => Sheets.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>https://drafts.csswg.org/cssom/#namespacedef-css</summary>
/// <seealso href="https://drafts.csswg.org/cssom/#namespacedef-css"/>
[IdlInterface("CSS", "https://drafts.csswg.org/cssom/#namespacedef-css")]
public static partial class Css
{
    /// <summary>
    /// The highlight registry of the associated document of the current global object
    /// (https://drafts.csswg.org/css-highlight-api-1/#dom-css-highlights): see <see cref="Document.Highlights"/>.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-css-highlights"/>
    [IdlMember("highlights", "https://drafts.csswg.org/css-highlight-api-1/#dom-css-highlights")]
    public static HighlightRegistry Highlights => Document.CurrentGlobalDocument.Highlights;

    /// <summary>
    /// The paint worklet of the associated document of the current global object
    /// (https://drafts.css-houdini.org/css-paint-api-1/#dom-css-paintworklet): see <see cref="Document.PaintWorklet"/>.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#dom-css-paintworklet"/>
    [IdlMember("paintWorklet", "https://drafts.css-houdini.org/css-paint-api-1/#dom-css-paintworklet")]
    public static Worklet PaintWorklet => Document.CurrentGlobalDocument.PaintWorklet;

    /// <summary>
    /// The layout worklet of the associated document of the current global object
    /// (https://drafts.css-houdini.org/css-layout-api-1/#dom-css-layoutworklet): see <see cref="Document.LayoutWorklet"/>.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-layout-api-1/#dom-css-layoutworklet"/>
    [IdlMember("layoutWorklet", "https://drafts.css-houdini.org/css-layout-api-1/#dom-css-layoutworklet")]
    public static Worklet LayoutWorklet => Document.CurrentGlobalDocument.LayoutWorklet;

    /// <summary>
    /// The animation worklet of the associated document of the current global object
    /// (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-css-animationworklet): see <see cref="Document.AnimationWorklet"/>.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-animation-worklet-1/#dom-css-animationworklet"/>
    [IdlMember("animationWorklet", "https://drafts.css-houdini.org/css-animation-worklet-1/#dom-css-animationworklet")]
    public static Worklet AnimationWorklet => Document.CurrentGlobalDocument.AnimationWorklet;

    /// <summary>https://drafts.csswg.org/cssom/#dom-css-escape</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-css-escape"/>
    [IdlMember("escape", "https://drafts.csswg.org/cssom/#dom-css-escape")]
    public static string Escape(string ident)
    {
        ArgumentNullException.ThrowIfNull(ident);
        return CssSerializer.SerializeIdentifier(ident);
    }

    /// <summary>https://drafts.csswg.org/css-conditional-3/#dom-css-supports</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-3/#dom-css-supports"/>
    [IdlMember("supports", "https://drafts.csswg.org/css-conditional-3/#dom-css-supports")]
    public static bool Supports(string property, string value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        // The property name is not ASCII case-insensitive for custom properties, and must not have whitespace around it
        if (!CssPropertyRegistry.IsCustomProperty(property) && CssPropertyRegistry.Get(Infra.InfraAscii.ToAsciiLowercase(property)) is null)
            return false;

        var values = CssParser.ParseListOfComponentValues(value);
        return CssRuleParser.IsSupportedDeclaration(property, values);
    }

    /// <summary>https://drafts.csswg.org/css-conditional-3/#dom-css-supports-conditiontext</summary>
    /// <seealso href="https://drafts.csswg.org/css-conditional-3/#dom-css-supports-conditiontext"/>
    [IdlMember("supports", "https://drafts.csswg.org/css-conditional-3/#dom-css-supports-conditiontext")]
    public static bool Supports(string conditionText)
    {
        ArgumentNullException.ThrowIfNull(conditionText);
        return Supports(conditionText, quirks: false);
    }

    /// <summary>
    /// Evaluates a supports condition, or a declaration without parentheses; in quirks mode, the quirky lengths and colors are
    /// valid in the declarations (they are not in <c>CSS.supports()</c>, https://drafts.csswg.org/css-values-4/#deprecated-quirky-length).
    /// </summary>
    internal static bool Supports(string conditionText, bool quirks)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(conditionText));
        if (CssRuleParser.EvaluateSupportsCondition(values, 0, out var end, quirks) is { } result && end == values.Count)
            return result;

        // The condition text wrapped in parentheses (e.g. "color: red")
        values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues("(" + conditionText + ")"));
        return CssRuleParser.EvaluateSupportsCondition(values, 0, out end, quirks) is true && end == values.Count;
    }
}

/// <summary>
/// The referrer and the referrer policy of the requests of the resources of a fetched style sheet (its @import rules, the
/// sources of its @font-face rules and the URLs of its declarations): the location of the style sheet is the referrer
/// (https://drafts.csswg.org/css-values-4/#fetch-a-style-resource), and the referrer policy is the one of the
/// <c>Referrer-Policy</c> header of its response.
/// </summary>
/// <param name="Url">The location of the style sheet.</param>
/// <param name="ReferrerPolicy">The referrer policy of the response of the style sheet, or the empty string.</param>
internal sealed record CssResourceReferrer(string Url, string ReferrerPolicy)
{
    /// <summary>Returns the referrer of the resources of the style sheet fetched from <paramref name="location"/>.</summary>
    public static CssResourceReferrer Create(string location, DocumentResource? response)
        => new(location, response is null ? "" : ReferrerPolicies.ParseHeader(response.Headers));

    /// <summary>
    /// Sets the referrer of a request of a resource of the style sheet, and its referrer policy unless the request has its
    /// own (the <c>referrer-policy()</c> request URL modifier, https://drafts.csswg.org/css-values-5/#request-url-modifiers).
    /// </summary>
    public void Apply(FetchRequest request, string requestPolicy)
    {
        request.Referrer = Url;
        if (requestPolicy.Length == 0 && ReferrerPolicy.Length > 0)
        {
            request.ReferrerPolicy = ReferrerPolicy;
        }
    }
}
