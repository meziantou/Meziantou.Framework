using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The xml-stylesheet processing instruction (https://drafts.csswg.org/cssom/#requirements-on-user-agents-implementing-the-xml-stylesheet-processing-instruction)
public sealed partial class ProcessingInstruction
{
    private CssStyleSheet? _sheet;
    private CancellationTokenSource? _styleSheetFetch;
    private bool _isInProlog;
    private bool _isBlockingScripts;

    /// <summary>https://drafts.csswg.org/cssom/#dom-linkstyle-sheet</summary>
    [IdlMember("sheet", "https://drafts.csswg.org/cssom/#dom-linkstyle-sheet")]
    public CssStyleSheet? Sheet => _sheet;

    /// <summary>Whether the node was created by the XML parser of its document: its style sheet blocks the scripts of the parser.</summary>
    internal bool CreatedByParser { get; init; }

    /// <summary>https://drafts.csswg.org/cssom/#prolog: the node is a child of the document that does not follow the document element.</summary>
    private bool IsInProlog()
    {
        if (ParentNode is not Document)
            return false;

        for (var sibling = PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
        {
            if (sibling is Element)
                return false;
        }

        return true;
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        UpdateXmlStyleSheetIfPrologChanged();
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        UpdateXmlStyleSheetIfPrologChanged();
    }

    internal override void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.MovingSteps(isSubtreeRoot, oldAncestor);
        UpdateXmlStyleSheetIfPrologChanged();
    }

    /// <summary>Runs the steps when the node becomes part of the prolog, or is no longer part of the prolog.</summary>
    internal void UpdateXmlStyleSheetIfPrologChanged()
    {
        if (IsInProlog() != _isInProlog)
        {
            UpdateXmlStyleSheet();
        }
    }

    /// <summary>
    /// The steps run when the node becomes part of the prolog, is no longer part of the prolog, or has its data changed. The
    /// style sheet is fetched with the <see cref="DocumentLoader"/> of the browsing context of the document.
    /// </summary>
    private void UpdateXmlStyleSheet()
    {
        _isInProlog = IsInProlog();
        if (Target != "xml-stylesheet")
            return;

        // Abort the running instance of the algorithm, and remove the associated style sheet
        if (_styleSheetFetch is { } fetch)
        {
            _styleSheetFetch = null;
            fetch.Cancel();
            SetBlockingScripts(false);
        }

        if (_sheet is { } oldSheet)
        {
            // https://drafts.csswg.org/cssom/#remove-a-css-style-sheet
            oldSheet.OwnerNode = null;
            oldSheet.ImportLoadState?.Cancellation.Cancel();
            _sheet = null;
            Css.Cascade.CssStyleVersion.Increment();
        }

        if (!_isInProlog)
            return;

        // The pseudo-attributes of the data (null when the data does not match the PseudoAtts production)
        if (_attributes is null)
            return;

        var alternate = GetAttribute("alternate") == "yes";
        var title = GetAttribute("title");
        if (alternate && title is null)
            return;

        // CSS is the only supported styling language
        if (GetAttribute("type") is { } type && (MimeType.Parse(type) is not { } mimeType || mimeType.Essence != "text/css"))
            return;

        if (GetAttribute("href") is not { } href)
            return;

        var document = NodeDocument;
        if (document.BrowsingContext is not { } browsingContext || UrlParser.Parse(href, document.DocumentBaseUrl) is not { } parsedUrl)
            return;

        var cancellation = new CancellationTokenSource();
        if (!CssImportFetching.TryStartFetch(document, browsingContext, parsedUrl, initiator: null, "other", cancellation.Token, out var styleSheetFetch))
        {
            cancellation.Dispose();
            return;
        }

        _styleSheetFetch = cancellation;
        document.StyleSheetImportsDelayingTheLoadEvent++;

        // A style sheet that is blocking scripts: the node was created by the parser of its document
        SetBlockingScripts(CreatedByParser && !alternate);

        var media = GetAttribute("media");
        var environmentEncoding = GetAttribute("charset") is { } charset && WebEncodingRegistry.TryGetEncoding(charset, out var charsetEncoding) ? charsetEncoding : document.Encoding;
        var url = parsedUrl.Serialize();
        CssImportFetching.WhenCompleted(styleSheetFetch, resource =>
        {
            // The result is ignored when the algorithm was aborted (the load event is delayed until the loader completes)
            if (ReferenceEquals(_styleSheetFetch, cancellation))
            {
                _styleSheetFetch = null;
                if (resource is not null && CssImportFetching.TryGetStyleSheetText(document, url, resource, environmentEncoding, out var text, out var responseUrl, out var sameOrigin))
                {
                    _sheet = new CssStyleSheet(this, text, responseUrl, media, title, alternate, sameOrigin);
                    Css.Cascade.CssStyleVersion.Increment();
                    CssImportFetching.FetchImports(_sheet);
                }

                SetBlockingScripts(false);
            }

            cancellation.Dispose();
            document.StyleSheetImportsDelayingTheLoadEvent--;
            document.TryQueueLoadEvent();
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#a-style-sheet-that-is-blocking-scripts</summary>
    private void SetBlockingScripts(bool value)
    {
        if (_isBlockingScripts == value)
            return;

        _isBlockingScripts = value;
        NodeDocument.SetStyleSheetBlockingScripts(value);
    }
}

public partial class Document
{
    private int _styleSheetsBlockingScripts;

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#has-a-style-sheet-that-is-blocking-scripts</summary>
    internal bool HasStyleSheetThatIsBlockingScripts => _styleSheetsBlockingScripts > 0;

    /// <summary>Called once when the document no longer has a style sheet that is blocking scripts (the parser waiting for it resumes).</summary>
    internal Action? NoStyleSheetBlockingScriptsCallback { get; set; }

    internal void SetStyleSheetBlockingScripts(bool value)
    {
        _styleSheetsBlockingScripts += value ? 1 : -1;
        if (_styleSheetsBlockingScripts != 0)
            return;

        // The autofocus candidates of the document are no longer paused (https://html.spec.whatwg.org/multipage/interaction.html#flush-autofocus-candidates)
        if (BrowsingContext?.Top.Document is { AutofocusProcessed: false, AutofocusCandidates.Count: > 0 } topDocument)
        {
            topDocument.EventLoop?.RequestRenderingOpportunity(topDocument);
        }

        if (NoStyleSheetBlockingScriptsCallback is { } callback)
        {
            NoStyleSheetBlockingScriptsCallback = null;
            callback();
        }
    }

    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);

        // The processing instructions that precede or follow the document element are part of the prolog or not
        for (var child = FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is ProcessingInstruction processingInstruction)
            {
                processingInstruction.UpdateXmlStyleSheetIfPrologChanged();
            }
        }
    }
}
