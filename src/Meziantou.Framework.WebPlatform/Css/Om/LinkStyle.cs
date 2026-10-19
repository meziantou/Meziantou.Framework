using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

// The LinkStyle mixin (https://drafts.csswg.org/cssom/#the-linkstyle-interface) and the style blocks
// (https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block)
public partial class Element
{
    /// <summary>Returns the associated CSS style sheet of a style element (null for the other elements).</summary>
    internal CssStyleSheet? GetLinkStyleSheet()
    {
        if (this is HtmlLinkElement link)
            return link.AssociatedStyleSheet;

        if (this is not (HtmlStyleElement or SvgStyleElement))
            return null;

        var rareData = ElementRareData;
        if (!rareData.StyleSheetUpToDate)
        {
            // The load event was already queued when the style block changed, unless the document has no browsing context. The
            // style block was not checked by the content security policies, which the document did not have when it changed.
            UpdateStyleBlock(rareData, queueLoadEvent: !HasEagerStyleBlockLifecycle, checkContentSecurityPolicy: false);
        }

        return rareData.StyleSheet;
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom/#add-a-css-style-sheet: the title of the first style sheet with a title that is added to
    /// the document is the preferred CSS style sheet set name (the style sheets are created lazily, but the name depends
    /// on the order in which they are added).
    /// </summary>
    private protected void SetPreferredStyleSheetSetName()
    {
        if (IsConnected && GetRoot() is Document document && document.PreferredStyleSheetSetName.Length == 0 && GetAttributeValue("title") is { Length: > 0 } title)
        {
            document.PreferredStyleSheetSetName = title;
        }
    }

    /// <summary>
    /// Whether the observable steps of "update a style block" (the load event) run when the style block changes, although
    /// the style sheet is parsed lazily: the document has a browsing context, or was created by a window (e.g. by
    /// <c>createHTMLDocument()</c>), whose event loop runs the tasks. Otherwise, the tasks would never run: the load event is
    /// only queued when the style sheet is computed.
    /// </summary>
    private bool HasEagerStyleBlockLifecycle => NodeDocument.EnvironmentDocument is not null;

    /// <summary>
    /// Runs "update a style block" when the style sheet was already computed. Otherwise, the style sheet is computed lazily,
    /// but the load event of the style sheet that would be created is queued.
    /// </summary>
    private protected void OnStyleBlockChanged()
    {
        // When the document has a content security policy, the style block of an element of the parser is updated when it is
        // popped off the stack of open elements, with its whole text (as in the browsers), and the style sheet is computed
        // eagerly, so that the inline check runs (and reports its violations) when the style block changes
        var hasContentSecurityPolicy = ContentSecurityPolicy.GetInlineCheckDocument(this) is not null;
        if (hasContentSecurityPolicy && RareDataOrNull is ElementRareData { IsOnStackOfOpenElements: true })
            return;

        if (RareDataOrNull is ElementRareData { StyleSheetUpToDate: true } rareData)
        {
            UpdateStyleBlock(rareData, queueLoadEvent: true);
        }
        else if (HasEagerStyleBlockLifecycle && IsConnected && IsCssStyleBlock())
        {
            // The style sheet is parsed now when it may import style sheets, so that they are fetched (and delay the load
            // events) as soon as possible
            if (hasContentSecurityPolicy || (NodeDocument.EnvironmentDocument!.BrowsingContext!.Options.DocumentLoader is not null && GetChildTextContent().Contains("@import", StringComparison.OrdinalIgnoreCase)))
            {
                UpdateStyleBlock(ElementRareData, queueLoadEvent: true);
            }
            else
            {
                QueueStyleSheetLoadEvent(ElementRareData);
            }
        }
    }

    /// <summary>
    /// Updates the media of the style sheet of a style element when its <c>media</c> attribute changes (as the browsers do: the
    /// style sheet is not parsed again), and the style block when its <c>type</c> attribute changes.
    /// </summary>
    private protected void OnStyleBlockAttributeChanged(string localName, string? @namespace)
    {
        if (@namespace is null && localName == "media" && RareDataOrNull is ElementRareData { StyleSheetUpToDate: true, StyleSheet: { } sheet })
        {
            sheet.Media.MediaText = GetAttributeValue("media") ?? "";
        }

        // As in the browsers, changing the type updates the style block (the specification does not list it)
        if (@namespace is null && localName == "type")
        {
            OnStyleBlockChanged();
        }
    }

    /// <summary>
    /// Called when a style element is pushed onto (<paramref name="onStack"/> true) or popped off the stack of open elements of
    /// a parser (https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block): the style block of a document
    /// that has a content security policy is updated when it is popped.
    /// </summary>
    internal void OnStyleElementStackOfOpenElementsChanged(bool onStack)
    {
        if (onStack)
        {
            ElementRareData.IsOnStackOfOpenElements = true;
            return;
        }

        if (RareDataOrNull is not ElementRareData { IsOnStackOfOpenElements: true } rareData)
            return;

        rareData.IsOnStackOfOpenElements = false;
        if (ContentSecurityPolicy.GetInlineCheckDocument(this) is not null)
        {
            OnStyleBlockChanged();
        }
    }

    /// <summary>Called when the parser appends text to the last text node of the element, which does not run the children changed steps.</summary>
    internal void OnParserAppendedText()
    {
        if (RareDataOrNull is ElementRareData { StyleSheetUpToDate: true })
        {
            OnStyleBlockChanged();
        }
    }

    /// <summary>Whether the type of the style element denotes a CSS style sheet: it is missing, empty or <c>text/css</c>.</summary>
    internal bool IsCssStyleBlock()
    {
        var type = GetAttributeValue("type");
        return string.IsNullOrEmpty(type) || InfraAscii.EqualsIgnoreAsciiCase(type, "text/css");
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block: the load event is fired by the next task,
    /// or once the critical subresources of the style sheet (the style sheets of its @import rules) are fetched. The event
    /// is an error event when one of them could not be fetched. The updates of the style block before this task runs fire a
    /// single event, if the element still has a style sheet.
    /// </summary>
    private void QueueStyleSheetLoadEvent(ElementRareData rareData)
    {
        if (rareData.StyleSheetLoadEventPending)
            return;

        rareData.StyleSheetLoadEventPending = true;
        NodeDocument.QueueElementTask(() =>
        {
            rareData.StyleSheetLoadEventPending = false;
            if (!IsConnected || !IsCssStyleBlock())
                return;

            // As in the browsers, a style sheet blocked by a content security policy fires an error event
            if (rareData is { StyleSheetUpToDate: true, StyleSheetBlockedByContentSecurityPolicy: true })
            {
                FireEvent(new Event("error"));
                return;
            }

            // The style sheet is not parsed yet when it has no @import rule to fetch
            var sheet = rareData.StyleSheetUpToDate ? rareData.StyleSheet : null;
            if (sheet?.ImportLoadState is { Pending: > 0 } state)
            {
                state.Completed += () =>
                {
                    if (rareData.StyleSheet == sheet && IsConnected)
                    {
                        FireStyleSheetLoadEvent(state);
                    }
                };
            }
            else
            {
                FireStyleSheetLoadEvent(sheet?.ImportLoadState);
            }
        });
    }

    private void FireStyleSheetLoadEvent(CssImportLoadState? state) => FireEvent(new Event(state is { Failed: true } ? "error" : "load"));

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block</summary>
    private void UpdateStyleBlock(ElementRareData rareData, bool queueLoadEvent, bool checkContentSecurityPolicy = true)
    {
        rareData.StyleSheetUpToDate = true;
        rareData.StyleSheetBlockedByContentSecurityPolicy = false;
        if (rareData.StyleSheet is { } oldSheet)
        {
            // https://drafts.csswg.org/cssom/#remove-a-css-style-sheet
            oldSheet.OwnerNode = null;
            oldSheet.ImportLoadState?.Cancellation.Cancel();
            rareData.StyleSheet = null;
            StopBlockingScripts(rareData);
            StopBlockingRendering(rareData);
        }

        if (!IsConnected)
            return;

        if (!IsCssStyleBlock())
            return;

        // https://w3c.github.io/webappsec-csp/#should-block-inline
        var text = GetChildTextContent();
        if (checkContentSecurityPolicy && ContentSecurityPolicy.ShouldBlockInline(this, ContentSecurityPolicyInlineType.Style, text))
        {
            rareData.StyleSheetBlockedByContentSecurityPolicy = true;
            if (queueLoadEvent)
            {
                QueueStyleSheetLoadEvent(rareData);
            }

            return;
        }

        // The title is only used for the style sheets of the document tree
        var title = GetRoot() is Document ? GetAttributeValue("title") : null;
        var sheet = new CssStyleSheet(this, text, GetAttributeValue("media"), title);
        rareData.StyleSheet = sheet;
        CssImportFetching.FetchImports(sheet);

        // https://html.spec.whatwg.org/multipage/semantics.html#contributes-a-script-blocking-style-sheet: the style sheet of a
        // style element created by the parser blocks the scripts until its imported style sheets are fetched (as in the
        // browsers, the @import rules whose media do not match do not block)
        if (this is HtmlStyleElement { CreatedByParser: true } && sheet.ImportLoadState is { Pending: > 0 } state && NodeDocument is var document
            && Css.Cascade.StyleEngine.CreateEnvironment(document) is var environment
            && (GetAttributeValue("media") is not { } media || MediaQueryEvaluator.Matches(media, environment))
            && sheet.RuleList.Exists(rule => rule is CssImportRule import && (import.Media.Length == 0 || MediaQueryEvaluator.Matches(import.Media.MediaText, environment))))
        {
            rareData.DocumentWhoseScriptsAreBlocked = document;
            document.SetStyleSheetBlockingScripts(true);
            state.Completed += () =>
            {
                if (rareData.StyleSheet == sheet)
                {
                    StopBlockingScripts(rareData);
                }
            };
        }

        // https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block: "If element's media attribute's value
        // matches the environment and element is potentially render-blocking, then block rendering on element", until the
        // critical subresources of the style sheet (its imported style sheets) are fetched
        if (this is HtmlStyleElement { IsPotentiallyRenderBlocking: true } && sheet.ImportLoadState is { Pending: > 0 } importState && NodeDocument is { AllowsAddingRenderBlockingElements: true } blockedDocument
            && (GetAttributeValue("media") is not { } styleMedia || MediaQueryEvaluator.Matches(styleMedia, Css.Cascade.StyleEngine.CreateEnvironment(blockedDocument))))
        {
            rareData.DocumentWhoseRenderingIsBlocked = blockedDocument;
            blockedDocument.BlockRendering(this);
            importState.Completed += () =>
            {
                if (rareData.StyleSheet == sheet)
                {
                    StopBlockingRendering(rareData);
                }
            };
        }

        if (queueLoadEvent)
        {
            QueueStyleSheetLoadEvent(rareData);
        }
    }

    private protected void StopBlockingRendering(ElementRareData rareData)
    {
        if (rareData.DocumentWhoseRenderingIsBlocked is not { } document)
            return;

        rareData.DocumentWhoseRenderingIsBlocked = null;
        document.UnblockRendering(this);
    }

    private static void StopBlockingScripts(ElementRareData rareData)
    {
        if (rareData.DocumentWhoseScriptsAreBlocked is not { } document)
            return;

        rareData.DocumentWhoseScriptsAreBlocked = null;
        document.SetStyleSheetBlockingScripts(false);
    }
}

public partial class HtmlStyleElement
{
    /// <summary>Whether the element was created by the parser of its document (https://html.spec.whatwg.org/multipage/semantics.html#contributes-a-script-blocking-style-sheet).</summary>
    internal bool CreatedByParser { get; set; }

    /// <summary>Gets the style sheet created by the <c>style</c> element, or null when there is none.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-linkstyle-sheet"/>
    [IdlMember("sheet", "https://drafts.csswg.org/cssom/#dom-linkstyle-sheet")]
    public CssStyleSheet? Sheet => GetLinkStyleSheet();

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#potentially-render-blocking: the blocking attribute of the
    /// element contains "render", or the element is implicitly potentially render-blocking (it was created by the parser of
    /// its document, https://html.spec.whatwg.org/multipage/semantics.html#the-style-element).
    /// </summary>
    internal bool IsPotentiallyRenderBlocking => CreatedByParser || Document.HasRenderBlockingToken(this);

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        OnStyleBlockAttributeChanged(localName, @namespace);

        // https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism: the blocking attribute "is changed so that
        // el is no longer potentially render-blocking"
        if (@namespace is null && localName == "blocking" && !IsPotentiallyRenderBlocking && RareDataOrNull is ElementRareData rareData)
        {
            StopBlockingRendering(rareData);
        }
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        SetPreferredStyleSheetSetName();
        OnStyleBlockChanged();
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        OnStyleBlockChanged();
    }

    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);
        OnStyleBlockChanged();
    }

    internal override void ChildTextContentChanged()
    {
        base.ChildTextContentChanged();
        OnStyleBlockChanged();
    }
}

public partial class SvgStyleElement
{
    /// <summary>Gets the style sheet created by the <c>style</c> element, or null when there is none.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-linkstyle-sheet"/>
    [IdlMember("sheet", "https://drafts.csswg.org/cssom/#dom-linkstyle-sheet")]
    public CssStyleSheet? Sheet => GetLinkStyleSheet();

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        OnStyleBlockAttributeChanged(localName, @namespace);
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        SetPreferredStyleSheetSetName();
        OnStyleBlockChanged();
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        OnStyleBlockChanged();
    }

    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);
        OnStyleBlockChanged();
    }

    internal override void ChildTextContentChanged()
    {
        base.ChildTextContentChanged();
        OnStyleBlockChanged();
    }
}

public partial class HtmlLinkElement
{
    /// <summary>
    /// https://drafts.csswg.org/cssom/#dom-linkstyle-sheet: the style sheet fetched by the <see cref="BrowsingContextOptions.DocumentLoader"/>
    /// when the element links to a style sheet (null until it is fetched, or when the document has no browsing context).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-linkstyle-sheet"/>
    [IdlMember("sheet", "https://drafts.csswg.org/cssom/#dom-linkstyle-sheet")]
    public CssStyleSheet? Sheet => GetLinkStyleSheet();
}

public partial class Document
{
    private StyleSheetList? _styleSheets;

    /// <summary>https://drafts.csswg.org/cssom/#preferred-css-style-sheet-set-name</summary>
    internal string PreferredStyleSheetSetName { get; set; } = "";
    private AdoptedStyleSheetList? _adoptedStyleSheets;

    /// <summary>Gets the list of the style sheets of the document.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-documentorshadowroot-stylesheets"/>
    [IdlMember("styleSheets", "https://drafts.csswg.org/cssom/#dom-documentorshadowroot-stylesheets")]
    public StyleSheetList StyleSheets => _styleSheets ??= new StyleSheetList(this);

    /// <summary>Gets the list of the constructed style sheets adopted by the document.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-documentorshadowroot-adoptedstylesheets"/>
    [IdlMember("adoptedStyleSheets", "https://drafts.csswg.org/cssom/#dom-documentorshadowroot-adoptedstylesheets")]
    public IList<CssStyleSheet> AdoptedStyleSheets
    {
        get => _adoptedStyleSheets ??= new AdoptedStyleSheetList(this);
        set => (_adoptedStyleSheets ??= new AdoptedStyleSheetList(this)).ReplaceAll(value);
    }
}

public partial class ShadowRoot
{
    private StyleSheetList? _styleSheets;
    private AdoptedStyleSheetList? _adoptedStyleSheets;

    /// <summary>
    /// The adopted style sheets can only be adopted by their constructor document: they are removed when the shadow root
    /// is adopted by another document, except when it moves between a document and its template contents owner document
    /// (e.g. into the content of a template element), as in browsers.
    /// </summary>
    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);
        var document = NodeDocument;
        if (_adoptedStyleSheets is { Count: > 0 } adoptedStyleSheets && !document.IsTemplateContentsOwnerDocumentOf(oldDocument) && !oldDocument.IsTemplateContentsOwnerDocumentOf(document))
        {
            adoptedStyleSheets.Clear();
        }
    }

    /// <summary>Gets the list of the style sheets of the shadow root.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-documentorshadowroot-stylesheets"/>
    [IdlMember("styleSheets", "https://drafts.csswg.org/cssom/#dom-documentorshadowroot-stylesheets")]
    public StyleSheetList StyleSheets => _styleSheets ??= new StyleSheetList(this);

    /// <summary>Gets the list of the constructed style sheets adopted by the shadow root.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom/#dom-documentorshadowroot-adoptedstylesheets"/>
    [IdlMember("adoptedStyleSheets", "https://drafts.csswg.org/cssom/#dom-documentorshadowroot-adoptedstylesheets")]
    public IList<CssStyleSheet> AdoptedStyleSheets
    {
        get => _adoptedStyleSheets ??= new AdoptedStyleSheetList(this);
        set => (_adoptedStyleSheets ??= new AdoptedStyleSheetList(this)).ReplaceAll(value);
    }
}

/// <summary>
/// The adopted style sheets (https://drafts.csswg.org/cssom/#dom-documentorshadowroot-adoptedstylesheets): an observable
/// array that only accepts the constructed style sheets of the document.
/// </summary>
internal sealed class AdoptedStyleSheetList(Node owner) : System.Collections.ObjectModel.Collection<CssStyleSheet>
{
    private void Validate(CssStyleSheet item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsConstructed)
            throw new DomException("Only constructed style sheets can be adopted", "NotAllowedError");

        var document = owner.NodeDocument;
        item.ConstructorDocument ??= document;
        if (item.ConstructorDocument != document)
            throw new DomException("The style sheet was constructed by another document", "NotAllowedError");
    }

    protected override void InsertItem(int index, CssStyleSheet item)
    {
        Validate(item);
        base.InsertItem(index, item);
        Css.Cascade.CssStyleVersion.Increment(owner.NodeDocument);
    }

    protected override void SetItem(int index, CssStyleSheet item)
    {
        Validate(item);
        base.SetItem(index, item);
        Css.Cascade.CssStyleVersion.Increment(owner.NodeDocument);
    }

    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        Css.Cascade.CssStyleVersion.Increment(owner.NodeDocument);
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        Css.Cascade.CssStyleVersion.Increment(owner.NodeDocument);
    }

    public void ReplaceAll(IEnumerable<CssStyleSheet> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToList();
        foreach (var item in list)
        {
            Validate(item);
        }

        Clear();
        foreach (var item in list)
        {
            base.InsertItem(Count, item);
        }
    }
}
