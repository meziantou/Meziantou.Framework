using System.Text;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Html.Parsing;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A value of the <c>(TrustedHTML or DOMString)</c> union type, the arguments of <see cref="Document.Write"/>.</summary>
public readonly struct TrustedHtmlOrString
{
    private TrustedHtmlOrString(TrustedHtml? trustedHtml, string? value)
    {
        TrustedHtml = trustedHtml;
        StringValue = value;
    }

    /// <summary>Gets the trusted HTML value, or null when the union holds a string.</summary>
    [NotInIdl]
    public TrustedHtml? TrustedHtml { get; }

    /// <summary>Gets the string value, or null when the union holds a trusted HTML value.</summary>
    [NotInIdl]
    public string? StringValue { get; }

    /// <summary>Converts a trusted HTML value to the union type.</summary>
    [NotInIdl]
    public static implicit operator TrustedHtmlOrString(TrustedHtml value) => new(value, null);

    /// <summary>Converts a string to the union type.</summary>
    [NotInIdl]
    public static implicit operator TrustedHtmlOrString(string value) => new(null, value);

    /// <summary>Creates the union from a trusted HTML value.</summary>
    [NotInIdl]
    public static TrustedHtmlOrString FromTrustedHtml(TrustedHtml value) => new(value, null);

    /// <summary>Creates the union from a string.</summary>
    [NotInIdl]
    public static TrustedHtmlOrString FromString(string value) => new(null, value);
}

// Dynamic markup insertion (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dynamic-markup-insertion)
public partial class Document
{
    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#throw-on-dynamic-markup-insertion-counter</summary>
    internal int ThrowOnDynamicMarkupInsertionCounter { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#ignore-destructive-writes-counter</summary>
    internal int IgnoreDestructiveWritesCounter { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#active-parser-was-aborted</summary>
    internal bool ActiveParserWasAborted { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-lifecycle.html#unload-counter</summary>
    internal int UnloadCounter { get; set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-open: replaces the document in place
    /// (its nodes, event listeners and URL) with an empty document, whose content is given by <see cref="Write"/>. It has no
    /// effect in a script run by the parser.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-open"/>
    [IdlMember("open", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-open")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The arguments are ignored (https://github.com/whatwg/webidl/issues/581)")]
    public Document Open(string? unused1 = null, string? unused2 = null) => OpenSteps();

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-open-window: runs the window open
    /// steps, as <see cref="Window.Open(string, string, string)"/>. As in the browsers, the target (e.g. <c>_self</c>) is
    /// chosen from the navigable of this document, not from the one of the entry document.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-open-window"/>
    [IdlMember("open", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-open-window")]
    public Window? Open(string url, string name, string features)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(features);
        if (!IsFullyActive || RelevantWindow is not { } window)
            throw new DomException("The document is not fully active", "InvalidAccessError");

        return window.Open(url, name, features, BrowsingContext);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-close: closes the input stream opened by <see cref="Open(string?, string?)"/>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-close"/>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-close")]
    public void Close()
    {
        ThrowIfDynamicMarkupInsertionIsNotAllowed();
        if (ActiveParser is not HtmlScriptingParser { IsScriptCreated: true } parser)
            return;

        // The explicit "EOF" character
        parser.TreeBuilder.Tokenizer.EofInserted = true;
        if (PendingParsingBlockingScript is not null)
            return;

        parser.Resume();
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-write: inserts markup in the input
    /// stream of the parser (after the script being executed by the parser), or replaces the document (as
    /// <see cref="Open(string?, string?)"/>) when the document is not being parsed.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-write"/>
    [IdlMember("write", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-write")]
    public void Write(params ReadOnlySpan<TrustedHtmlOrString> text) => WriteSteps(text, lineFeed: false, "Document write");

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-writeln: <see cref="Write"/> followed by a line feed.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-writeln"/>
    [IdlMember("writeln", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-document-writeln")]
    public void Writeln(params ReadOnlySpan<TrustedHtmlOrString> text) => WriteSteps(text, lineFeed: true, "Document writeln");

    private void ThrowIfDynamicMarkupInsertionIsNotAllowed()
    {
        if (!IsHtmlDocument)
            throw DomException.InvalidState("Dynamic markup insertion is not supported in XML documents");

        if (ThrowOnDynamicMarkupInsertionCounter > 0)
            throw DomException.InvalidState("Dynamic markup insertion is not allowed while a custom element is constructed by the parser");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#document-open-steps</summary>
    private Document OpenSteps()
    {
        ThrowIfDynamicMarkupInsertionIsNotAllowed();

        // The entry document is not tracked in the promise jobs (e.g. after an await): the document of the incumbent realm
        // is used. Without a running script (a call from .NET), the document is its own entry document.
        var entryDocument = EntryGlobalDocument ?? (BrowsingContext?.Options.ScriptEngine?.GetCurrentGlobal() as Window)?.Document ?? this;
        if (!IsSameOrigin(this, entryDocument))
            throw new DomException("The document is not same origin with the entry document", "SecurityError");

        // document.open() is ignored in a script run by the parser, while the document is unloaded, and after the parser was aborted
        if (ActiveParser is HtmlScriptingParser { TreeBuilder.ScriptNestingLevel: > 0 })
            return this;

        if (UnloadCounter > 0 || ActiveParserWasAborted)
            return this;

        // document.open() in a load event handler of the iframe mutes the following load events of the iframe
        if (IframeLoadInProgress)
        {
            MuteIframeLoad = true;
        }

        // A navigation of the navigable of the document is stopped, and the fetches of the document are aborted
        if (BrowsingContext is { OngoingNavigationId: not 0 } navigable && ReferenceEquals(navigable.Document, this))
        {
            navigable.StopLoading();
        }

        // The parser of the document is replaced by the script-created parser
        ActiveParser?.Abort();

        using (CEReactionsScope.Enter())
        {
            foreach (var node in ShadowIncludingInclusiveDescendants())
            {
                node.EraseAllEventListenersAndHandlers();
            }

            if (RelevantWindow is { } window && ReferenceEquals(window.Document, this))
            {
                window.EraseAllEventListenersAndHandlers();
            }

            ReplaceAll(null);
        }

        if (IsFullyActive && BrowsingContext is { } browsingContext)
        {
            var newUrl = entryDocument.UrlRecord.Clone();
            if (!ReferenceEquals(entryDocument, this))
            {
                newUrl.Fragment = null;
            }

            browsingContext.UpdateUrlAndHistory(newUrl, serializedData: null, hasSerializedData: false, NavigationHistoryBehavior.Replace);
        }

        IsInitialAboutBlank = false;
        Mode = DocumentMode.NoQuirks;

        // A script-created parser: its input ends when document.close() is called. The insertion point is the end of the input.
        var tokenizer = new HtmlTokenizer("", errorHandler: null, new NameTable()) { EofInserted = false };
        var builder = new HtmlTreeBuilder(this, tokenizer, errorHandler: null, ScriptingEnabled ? ParserScriptingMode.Normal : ParserScriptingMode.Disabled)
        {
            AllowDeclarativeShadowRoots = AllowDeclarativeShadowRoots,
            InsertionPoint = 0,
        };

        ResetParsingState();
        var parser = new HtmlScriptingParser(this, builder, runTheEnd: BrowsingContext is not null) { IsScriptCreated = true };
        parser.Start();
        UpdateReadiness(DocumentReadyState.Loading);
        return this;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#document-write-steps</summary>
    private void WriteSteps(ReadOnlySpan<TrustedHtmlOrString> text, bool lineFeed, string sink)
    {
        var builder = new StringBuilder();
        var isTrusted = true;
        foreach (var value in text)
        {
            if (value.TrustedHtml is { } trustedHtml)
            {
                builder.Append(trustedHtml.Data);
            }
            else
            {
                isTrusted = false;
                builder.Append(value.StringValue);
            }
        }

        var markup = builder.ToString();
        if (!isTrusted)
        {
            markup = TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, this, markup, sink);
        }

        if (lineFeed)
        {
            markup += "\n";
        }

        ThrowIfDynamicMarkupInsertionIsNotAllowed();
        if (ActiveParserWasAborted)
            return;

        if (ActiveParser is not HtmlScriptingParser { TreeBuilder.InsertionPoint: not null })
        {
            if (UnloadCounter > 0 || IgnoreDestructiveWritesCounter > 0)
                return;

            OpenSteps();
        }

        if (ActiveParser is not HtmlScriptingParser { TreeBuilder: { InsertionPoint: { } insertionPoint } treeBuilder } parser)
            return;

        treeBuilder.Tokenizer.Insert(insertionPoint, markup);
        if (PendingParsingBlockingScript is not null)
            return;

        treeBuilder.ProcessInsertedCharacters();

        // The characters written to a script-created parser from a script that is not run by the parser: the parser waits
        // for the parsing-blocking script they contain
        if (treeBuilder.ScriptNestingLevel == 0 && PendingParsingBlockingScript is not null)
        {
            parser.Resume();
        }
    }
}
