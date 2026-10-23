using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

internal enum InsertionMode : byte
{
    Initial,
    BeforeHtml,
    BeforeHead,
    InHead,
    InHeadNoscript,
    AfterHead,
    InBody,
    Text,
    InTable,
    InTableText,
    InCaption,
    InColumnGroup,
    InTableBody,
    InRow,
    InCell,
    InTemplate,
    AfterBody,
    InFrameset,
    AfterFrameset,
    AfterAfterBody,
    AfterAfterFrameset,
}

/// <summary>https://html.spec.whatwg.org/multipage/parsing.html#parser-scripting-mode</summary>
internal enum ParserScriptingMode : byte
{
    Normal,
    Disabled,
    Inert,
    Fragment,
}

internal enum TokenKind : byte
{
    Characters,
    Comment,
    ProcessingInstruction,
    Doctype,
    StartTag,
    EndTag,
    EndOfFile,
}

/// <summary>A token as processed by the tree builder. Character tokens can be partially consumed by the insertion modes.</summary>
internal ref struct Token
{
    public TokenKind Kind;
    public ReadOnlySpan<char> Characters;
    public HtmlTagToken Tag;
    public string Data;
    public string? PiTarget;
    public string? DoctypeName;
    public string? DoctypePublicId;
    public string? DoctypeSystemId;
    public bool DoctypeForceQuirks;

    public readonly bool IsStartTag(TagId id) => Kind == TokenKind.StartTag && Tag.TagId == id;

    public readonly bool IsEndTag(TagId id) => Kind == TokenKind.EndTag && Tag.TagId == id;
}

internal enum ProcessResult : byte
{
    Done,
    Reprocess,
}

/// <summary>
/// The tree construction stage of the HTML parser (https://html.spec.whatwg.org/multipage/parsing.html#tree-construction).
/// </summary>
internal sealed partial class HtmlTreeBuilder
{
    private readonly Document _document;
    private readonly HtmlTokenizer _tokenizer;
    private readonly Action<HtmlParseErrorInfo>? _errorHandler;

    private InsertionMode _mode = InsertionMode.Initial;
    private InsertionMode _originalMode;
    private readonly List<Element> _openElements = new(32);

    // The number of HTML elements of each tag in the stack of open elements, so "has an element in scope" returns early
    // when there is no such element instead of walking the whole stack (quadratic on deeply nested documents).
    private readonly int[] _openHtmlElementCounts = new int[Enum.GetValues<TagId>().Length];
    private readonly List<InsertionMode> _templateModes = [];

    // List of active formatting elements. A null element is a marker.
    private readonly List<FormattingEntry> _activeFormatting = [];

    private Element? _headElement;
    private Element? _formElement;
    private bool _framesetOk = true;
    private bool _fosterParenting;
    private bool _ignoreNextLineFeed;
    private bool _stopped;
    private bool _pausedForScript;

    // Pending table character tokens (in table text insertion mode)
    private readonly System.Text.StringBuilder _pendingTableCharacters = new();
    private bool _pendingTableCharactersContainNonWhitespace;

    public HtmlTreeBuilder(Document document, HtmlTokenizer tokenizer, Action<HtmlParseErrorInfo>? errorHandler, ParserScriptingMode scriptingMode)
    {
        _document = document;
        _tokenizer = tokenizer;
        _errorHandler = errorHandler;
        ScriptingMode = scriptingMode;
        _tokenizer.IsAdjustedCurrentNodeInForeignContent = () => AdjustedCurrentNode is { IsHtmlNamespace: false };
    }

    private readonly record struct FormattingEntry(Element? Element, HtmlTagToken? Token)
    {
        public bool IsMarker => Element is null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#fragment-context-element</summary>
    public Element? ContextElement { get; private set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#root-insertion-target: the node into which the children of the root
    /// html element are inserted, the DocumentFragment of the fragment parsing algorithm, or the element or shadow root into
    /// which HTML is streamed (https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html).
    /// </summary>
    public Node? RootInsertionTarget { get; private set; }

    /// <summary>The child of <see cref="RootInsertionTarget"/> before which the nodes are inserted (the end when it is null).</summary>
    public Node? RootInsertionReferenceChild { get; private set; }

    /// <summary>
    /// Gets whether the parser streams HTML into <see cref="RootInsertionTarget"/> (https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html):
    /// the elements of the root are created in its node document, and the custom element constructors run synchronously.
    /// </summary>
    public bool IsStreaming { get; private set; }

    public bool AllowDeclarativeShadowRoots { get; set; }

    public ParserScriptingMode ScriptingMode { get; set; }

    /// <summary>
    /// Called when the parser inserts a <c>meta</c> element in the head (in the "in head" insertion mode, outside of a
    /// template), which may declare the encoding (https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inhead).
    /// The other <c>meta</c> elements do not change the encoding, as in Blink and WebKit and as proposed by
    /// https://github.com/whatwg/html/issues/6962 (see <see cref="EncodingSniffer.Sniff"/>).
    /// </summary>
    public Action<Element>? MetaElementInsertedInHead { get; set; }

    // ----------------------------------------------------------------------------------------------------------------
    // Setup
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Sets up the parser for the HTML fragment parsing algorithm (https://html.spec.whatwg.org/multipage/parsing.html#html-fragment-parsing-algorithm).</summary>
    /// <param name="context">The context element.</param>
    /// <param name="rootInsertionTarget">The node into which the children of the root are inserted (see <see cref="RootInsertionTarget"/>).</param>
    /// <param name="referenceChild">The child of <paramref name="rootInsertionTarget"/> before which the nodes are inserted, or null.</param>
    /// <param name="registry">The custom element registry of the root html element.</param>
    /// <param name="streaming">Whether the parser streams HTML into <paramref name="rootInsertionTarget"/> (see <see cref="IsStreaming"/>).</param>
    public void SetupFragmentCase(Element context, Node rootInsertionTarget, Node? referenceChild, CustomElementRegistry? registry, bool streaming = false)
    {
        ContextElement = context;
        var root = ElementFactory.CreateElement(_document, "html", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false, registry);
        _document.AppendChildCore(root);
        PushOpenElement(root);
        RootInsertionTarget = rootInsertionTarget;
        RootInsertionReferenceChild = referenceChild;
        IsStreaming = streaming;
        if (context.IsHtml(TagId.Template))
        {
            _templateModes.Add(InsertionMode.InTemplate);
        }

        ResetInsertionModeAppropriately();
        for (Node? node = context; node is not null; node = node.ParentNode)
        {
            if (node is Element element && element.IsHtml(TagId.Form))
            {
                _formElement = element;
                break;
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Main loop
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#script-nesting-level</summary>
    public int ScriptNestingLevel { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#parser-pause-flag</summary>
    public bool ParserPauseFlag { get; set; }

    /// <summary>
    /// The external SVG script elements parsed from the characters written by <c>document.write()</c> while a script runs,
    /// whose fetch completed synchronously: as in the browsers, they block the parser like the parsing-blocking scripts and
    /// run once the script that wrote them returns (see <see cref="RunPendingSvgScripts"/>).
    /// </summary>
    internal List<Action>? PendingSvgScripts { get; set; }

    /// <summary>Runs the external SVG scripts written by a script that returned (the script nesting level is zero).</summary>
    internal void RunPendingSvgScripts()
    {
        while (ScriptNestingLevel == 0 && PendingSvgScripts is { Count: > 0 } scripts)
        {
            var run = scripts[0];
            scripts.RemoveAt(0);
            var oldInsertionPoint = InsertionPoint;
            InsertionPoint = _tokenizer.PositionFromEnd;
            ScriptNestingLevel++;
            run();
            ScriptNestingLevel--;
            ParserPauseFlag = false;
            InsertionPoint = oldInsertionPoint;
        }
    }

    /// <summary>
    /// Gets or sets the insertion point (https://html.spec.whatwg.org/multipage/parsing.html#insertion-point), as a position
    /// from the end of the input, or null when it is undefined.
    /// </summary>
    public int? InsertionPoint { get; set; }

    /// <summary>Gets whether the last run stopped because the tokenizer reached its limit (the insertion point, or the end of the input of a script-created parser).</summary>
    public bool IsWaitingForInput => _tokenizer.IsWaitingForInput;

    /// <summary>Gets the tokenizer (its input stream receives the characters of <c>document.write()</c>).</summary>
    internal HtmlTokenizer Tokenizer => _tokenizer;

    /// <summary>Gets whether the parser has stopped (https://html.spec.whatwg.org/multipage/parsing.html#stop-parsing).</summary>
    public bool IsStopped => _stopped;

    /// <summary>
    /// Processes the characters inserted before the insertion point, stopping when the tokenizer reaches the insertion
    /// point or when the tree construction aborts the processing (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#document-write-steps, step 11).
    /// </summary>
    public void ProcessInsertedCharacters()
    {
        if (_stopped || InsertionPoint is not { } insertionPoint)
            return;

        var previousStop = _tokenizer.StopFromEnd;
        _tokenizer.StopFromEnd = insertionPoint;
        try
        {
            Run();
        }
        finally
        {
            _tokenizer.StopFromEnd = previousStop;
        }
    }

    /// <summary>
    /// Runs the tree construction until the end of the input, or until a parser-blocking script is pending (see
    /// <see cref="ScriptingParser"/>). Returns true when the parser stopped.
    /// </summary>
    public bool Run()
    {
        _runDepth++;
        try
        {
            return RunCore();
        }
        finally
        {
            _runDepth--;
            if (_abortRequested && _runDepth == 0)
            {
                _abortRequested = false;
                Abort();
            }
        }
    }

    // The number of nested Run() calls (document.write() in a script run by the tree construction), and whether Abort() was
    // called while the tree construction was running
    private int _runDepth;
    private bool _abortRequested;

    private bool RunCore()
    {
        while (!_stopped && !_abortRequested)
        {
            // https://html.spec.whatwg.org/multipage/parsing.html#tokenization: the tokenizer does not proceed while the
            // parser is paused (e.g. document.write() while a parsing-blocking script is pending)
            if (ParserPauseFlag && ScriptNestingLevel > 0)
                return false;

            var type = _tokenizer.NextToken();
            var token = default(Token);
            switch (type)
            {
                case HtmlTokenType.None:
                    // The tokenizer reached the insertion point, or the end of the input of a script-created parser
                    return false;
                case HtmlTokenType.Characters:
                    token.Kind = TokenKind.Characters;
                    token.Characters = _tokenizer.Characters;
                    if (_ignoreNextLineFeed)
                    {
                        _ignoreNextLineFeed = false;
                        if (token.Characters.Length > 0 && token.Characters[0] == '\n')
                        {
                            token.Characters = token.Characters[1..];
                            if (token.Characters.IsEmpty)
                                continue;
                        }
                    }

                    break;
                case HtmlTokenType.StartTag:
                    token.Kind = TokenKind.StartTag;
                    token.Tag = _tokenizer.Tag!;
                    break;
                case HtmlTokenType.EndTag:
                    token.Kind = TokenKind.EndTag;
                    token.Tag = _tokenizer.Tag!;
                    break;
                case HtmlTokenType.Comment:
                    token.Kind = TokenKind.Comment;
                    token.Data = _tokenizer.CommentData;
                    break;
                case HtmlTokenType.ProcessingInstruction:
                    token.Kind = TokenKind.ProcessingInstruction;
                    token.PiTarget = _tokenizer.ProcessingInstructionTarget;
                    token.Data = _tokenizer.ProcessingInstructionData;
                    break;
                case HtmlTokenType.Doctype:
                    token.Kind = TokenKind.Doctype;
                    token.DoctypeName = _tokenizer.DoctypeName;
                    token.DoctypePublicId = _tokenizer.DoctypePublicId;
                    token.DoctypeSystemId = _tokenizer.DoctypeSystemId;
                    token.DoctypeForceQuirks = _tokenizer.DoctypeForceQuirks;
                    break;
                case HtmlTokenType.EndOfFile:
                    token.Kind = TokenKind.EndOfFile;
                    break;
                default:
                    throw new InvalidOperationException("Unexpected token");
            }

            if (type != HtmlTokenType.Characters)
            {
                _ignoreNextLineFeed = false;
            }

            Dispatch(ref token);

            if (token.Kind == TokenKind.StartTag && token.Tag.SelfClosing && !token.Tag.SelfClosingAcknowledged)
            {
                ParseError("non-void-html-element-start-tag-with-trailing-solidus");
            }

            if (type is HtmlTokenType.StartTag or HtmlTokenType.EndTag && token.Kind is TokenKind.StartTag or TokenKind.EndTag && !token.Tag.Retained)
            {
                _tokenizer.ReleaseTag(token.Tag);
            }

            if (type == HtmlTokenType.EndOfFile)
                break;

            if (_pausedForScript)
            {
                _pausedForScript = false;
                return false;
            }
        }

        // The parser is aborted when the outermost Run() returns
        if (_abortRequested)
            return true;

        StopParsing();
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#tree-construction-dispatcher</summary>
    private void Dispatch(ref Token token)
    {
        while (true)
        {
            ProcessResult result;
            if (ShouldUseHtmlContentRules(in token))
            {
                result = ProcessUsingRulesFor(_mode, ref token);
            }
            else
            {
                result = ProcessInForeignContent(ref token);
            }

            if (result == ProcessResult.Done)
                return;

            if (token.Kind == TokenKind.Characters && token.Characters.IsEmpty)
                return;
        }
    }

    private bool ShouldUseHtmlContentRules(in Token token)
    {
        if (_openElements.Count == 0)
            return true;

        var node = AdjustedCurrentNode!;
        if (node.IsHtmlNamespace)
            return true;

        if (IsMathMLTextIntegrationPoint(node))
        {
            if (token.Kind == TokenKind.StartTag && token.Tag.Name is not ("mglyph" or "malignmark"))
                return true;

            if (token.Kind == TokenKind.Characters)
                return true;
        }

        if (token.Kind == TokenKind.StartTag && token.Tag.Name == "svg" && IsMathML(node, TagId.AnnotationXml))
            return true;

        if (IsHtmlIntegrationPoint(node) && token.Kind is TokenKind.StartTag or TokenKind.Characters)
            return true;

        return token.Kind == TokenKind.EndOfFile;
    }

    private ProcessResult ProcessUsingRulesFor(InsertionMode mode, ref Token token)
    {
        return mode switch
        {
            InsertionMode.Initial => ProcessInitial(ref token),
            InsertionMode.BeforeHtml => ProcessBeforeHtml(ref token),
            InsertionMode.BeforeHead => ProcessBeforeHead(ref token),
            InsertionMode.InHead => ProcessInHead(ref token),
            InsertionMode.InHeadNoscript => ProcessInHeadNoscript(ref token),
            InsertionMode.AfterHead => ProcessAfterHead(ref token),
            InsertionMode.InBody => ProcessInBody(ref token),
            InsertionMode.Text => ProcessText(ref token),
            InsertionMode.InTable => ProcessInTable(ref token),
            InsertionMode.InTableText => ProcessInTableText(ref token),
            InsertionMode.InCaption => ProcessInCaption(ref token),
            InsertionMode.InColumnGroup => ProcessInColumnGroup(ref token),
            InsertionMode.InTableBody => ProcessInTableBody(ref token),
            InsertionMode.InRow => ProcessInRow(ref token),
            InsertionMode.InCell => ProcessInCell(ref token),
            InsertionMode.InTemplate => ProcessInTemplate(ref token),
            InsertionMode.AfterBody => ProcessAfterBody(ref token),
            InsertionMode.InFrameset => ProcessInFrameset(ref token),
            InsertionMode.AfterFrameset => ProcessAfterFrameset(ref token),
            InsertionMode.AfterAfterBody => ProcessAfterAfterBody(ref token),
            InsertionMode.AfterAfterFrameset => ProcessAfterAfterFrameset(ref token),
            _ => throw new InvalidOperationException("Unknown insertion mode"),
        };
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#abort-a-parser of a fragment parser (https://whatpr.org/html/12758/parsing.html#abort-a-parser):
    /// the script elements that are still open never run, and the open elements are popped.
    /// </summary>
    public void Abort()
    {
        if (_stopped)
            return;

        // A script run by the tree construction (e.g. window.stop()) aborts the parser once the current token is processed
        if (_runDepth > 0)
        {
            _abortRequested = true;
            return;
        }

        foreach (var element in _openElements)
        {
            if (element is HtmlScriptElement script)
            {
                script.AlreadyStarted = true;
            }
        }

        StopParsing();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#stop-parsing</summary>
    private void StopParsing()
    {
        _stopped = true;
        while (_openElements.Count > 0)
        {
            PopCurrentNode();
        }
    }

    private void ParseError(string code)
    {
        _errorHandler?.Invoke(new HtmlParseErrorInfo(code, _tokenizer.Input, _tokenizer.CurrentTokenEndOffset));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Character helpers
    // ----------------------------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    private static int CountLeadingWhitespace(ReadOnlySpan<char> value)
    {
        var i = 0;
        while (i < value.Length && IsWhitespace(value[i]))
        {
            i++;
        }

        return i;
    }

    private static bool ContainsNonWhitespace(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!IsWhitespace(c))
                return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Stack of open elements
    // ----------------------------------------------------------------------------------------------------------------

    private Element CurrentNode => _openElements[^1];

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#adjusted-current-node</summary>
    private Element? AdjustedCurrentNode
    {
        get
        {
            if (_openElements.Count == 0)
                return null;

            if (ContextElement is not null && _openElements.Count == 1)
                return ContextElement;

            return _openElements[^1];
        }
    }

    private void PushOpenElement(Element element)
    {
        _openElements.Add(element);
        OnElementPushed(element);
    }

    private void PopCurrentNode()
    {
        var element = _openElements[^1];
        _openElements.RemoveAt(_openElements.Count - 1);
        OnElementPopped(element);
    }

    /// <summary>Removes an element from the stack of open elements (not necessarily the current node).</summary>
    private void RemoveFromOpenElements(Element element)
    {
        if (_openElements.Remove(element))
        {
            OnElementPopped(element);
        }
    }

    private void RemoveFromOpenElementsAt(int index)
    {
        var element = _openElements[index];
        _openElements.RemoveAt(index);
        OnElementPopped(element);
    }

    /// <summary>Called when an element is pushed onto (or inserted into) the stack of open elements.</summary>
    private void OnElementPushed(Element element)
    {
        if (element.IsHtmlNamespace)
        {
            _openHtmlElementCounts[(int)element.TagId]++;
        }

        if (element is HtmlObjectElement obj)
        {
            obj.IsOnStackOfOpenElements = true;
        }
    }

    /// <summary>Called when an element is popped off the stack of open elements.</summary>
    private void OnElementPopped(Element element)
    {
        if (element.IsHtmlNamespace)
        {
            _openHtmlElementCounts[(int)element.TagId]--;
        }

        // https://html.spec.whatwg.org/multipage/form-elements.html#the-option-element: when an option element is popped off
        // the stack of open elements, update descendant selectedcontent elements for the option.
        if (element is HtmlOptionElement option)
        {
            option.UpdateDescendantSelectedContentElements();
        }

        // A template that patches content and is popped without its end tag (e.g. at the end of the input) removes its
        // markers too, as in Chromium (the specification only removes them at the end tag)
        if (element is HtmlTemplateElement template)
        {
            RemovePatchMarkers(template);
        }

        // https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element: what an object element represents
        // is determined when it is popped off the stack of open elements
        if (element is HtmlObjectElement obj)
        {
            obj.OnPoppedOffStackOfOpenElements();
        }
        else if (element is HtmlMediaElement media)
        {
            // https://html.spec.whatwg.org/multipage/media.html#blocked-on-parser: the automatic text track selection is
            // performed when a media element is popped off the stack of open elements
            media.OnPoppedOffStackOfOpenElements();
        }
        else if (element is HtmlStyleElement or SvgStyleElement)
        {
            element.OnStyleElementStackOfOpenElementsChanged(onStack: false);
        }

        // https://html.spec.whatwg.org/multipage/parsing.html#the-stack-of-open-elements: "When the current node is removed from
        // the stack of open elements, process internal resource links given the current node's node document": the link
        // rel=expect elements that wait for this element no longer block the rendering
        if (element.NodeDocument.HasRenderBlockingElements)
        {
            element.NodeDocument.ProcessInternalResourceLinks(element);
        }
    }

    private void PopUntil(TagId tagId)
    {
        while (_openElements.Count > 0)
        {
            var element = CurrentNode;
            PopCurrentNode();
            if (element.IsHtml(tagId))
                return;
        }
    }

    private void PopUntil(Element element)
    {
        while (_openElements.Count > 0)
        {
            var current = CurrentNode;
            PopCurrentNode();
            if (ReferenceEquals(current, element))
                return;
        }
    }

    private void PopUntilOneOf(TagId a, TagId b)
    {
        while (_openElements.Count > 0)
        {
            var element = CurrentNode;
            PopCurrentNode();
            if (element.IsHtml(a) || element.IsHtml(b))
                return;
        }
    }

    private bool IsInOpenElements(Element element)
    {
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_openElements[i], element))
                return true;
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#parsing-template-contents</summary>
    private bool IsParsingTemplateContents => HasTemplateInOpenElements() || (ContextElement is not null && ContextElement.IsHtml(TagId.Template));

    private bool HasTemplateInOpenElements()
    {
        foreach (var element in _openElements)
        {
            if (element.IsHtml(TagId.Template))
                return true;
        }

        return false;
    }

    private enum Scope : byte
    {
        Default,
        ListItem,
        Button,
        Table,
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#has-an-element-in-the-specific-scope</summary>
    private bool HasInScope(TagId tagId, Scope scope = Scope.Default)
    {
        if (_openHtmlElementCounts[(int)tagId] == 0)
            return false;

        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (node.IsHtml(tagId))
                return true;

            if (IsScopeBoundary(node, scope))
                return false;
        }

        return false;
    }

    private bool HasInScope(Element target, Scope scope = Scope.Default)
    {
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (ReferenceEquals(node, target))
                return true;

            if (IsScopeBoundary(node, scope))
                return false;
        }

        return false;
    }

    private bool HasAnyInScope(ReadOnlySpan<TagId> tagIds, Scope scope)
    {
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (node.IsHtmlNamespace && tagIds.Contains(node.TagId))
                return true;

            if (IsScopeBoundary(node, scope))
                return false;
        }

        return false;
    }

    private static bool IsScopeBoundary(Element node, Scope scope)
    {
        if (scope == Scope.Table)
            return node.IsHtmlNamespace && node.TagId is TagId.Html or TagId.Table or TagId.Template;

        if (node.IsHtmlNamespace)
        {
            switch (node.TagId)
            {
                case TagId.Applet or TagId.Caption or TagId.Html or TagId.Table or TagId.Td or TagId.Th or TagId.Marquee or TagId.Object or TagId.Select or TagId.Template:
                    return true;
                case TagId.Ol or TagId.Ul when scope == Scope.ListItem:
                    return true;
                case TagId.Button when scope == Scope.Button:
                    return true;
            }

            return false;
        }

        if (ReferenceEquals(node.NamespaceUri, Namespaces.MathML))
            return node.TagId is TagId.Mi or TagId.Mo or TagId.Mn or TagId.Ms or TagId.Mtext or TagId.AnnotationXml;

        if (ReferenceEquals(node.NamespaceUri, Namespaces.Svg))
            return node.LocalName is "foreignObject" or "desc" or "title";

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#special</summary>
    private static bool IsSpecial(Element node)
    {
        if (node.IsHtmlNamespace)
        {
            return node.TagId switch
            {
                TagId.Address or TagId.Applet or TagId.Area or TagId.Article or TagId.Aside or TagId.Base or TagId.Basefont or TagId.Bgsound or
                TagId.Blockquote or TagId.Body or TagId.Br or TagId.Button or TagId.Caption or TagId.Center or TagId.Col or TagId.Colgroup or
                TagId.Dd or TagId.Details or TagId.Dir or TagId.Div or TagId.Dl or TagId.Dt or TagId.Embed or TagId.Fieldset or TagId.Figcaption or
                TagId.Figure or TagId.Footer or TagId.Form or TagId.Frame or TagId.Frameset or TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or
                TagId.H5 or TagId.H6 or TagId.Head or TagId.Header or TagId.Hgroup or TagId.Hr or TagId.Html or TagId.Iframe or TagId.Img or
                TagId.Input or TagId.Keygen or TagId.Li or TagId.Link or TagId.Listing or TagId.Main or TagId.Marquee or TagId.Menu or TagId.Meta or
                TagId.Nav or TagId.Noembed or TagId.Noframes or TagId.Noscript or TagId.Object or TagId.Ol or TagId.P or TagId.Param or
                TagId.Plaintext or TagId.Pre or TagId.Script or TagId.Search or TagId.Section or TagId.Select or TagId.Source or TagId.Style or
                TagId.Summary or TagId.Table or TagId.Tbody or TagId.Td or TagId.Template or TagId.Textarea or TagId.Tfoot or TagId.Th or
                TagId.Thead or TagId.Title or TagId.Tr or TagId.Track or TagId.Ul or TagId.Wbr or TagId.Xmp => true,
                _ => false,
            };
        }

        return IsScopeBoundary(node, Scope.Default);
    }

    private static bool IsMathML(Element node, TagId tagId) => node.TagId == tagId && ReferenceEquals(node.NamespaceUri, Namespaces.MathML);

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#mathml-text-integration-point</summary>
    private static bool IsMathMLTextIntegrationPoint(Element node) =>
        ReferenceEquals(node.NamespaceUri, Namespaces.MathML) && node.TagId is TagId.Mi or TagId.Mo or TagId.Mn or TagId.Ms or TagId.Mtext;

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#html-integration-point</summary>
    private static bool IsHtmlIntegrationPoint(Element node)
    {
        if (ReferenceEquals(node.NamespaceUri, Namespaces.MathML))
            return node.TagId == TagId.AnnotationXml && node is MathMLElement { IsHtmlIntegrationPoint: true };

        if (ReferenceEquals(node.NamespaceUri, Namespaces.Svg))
            return node.LocalName is "foreignObject" or "desc" or "title";

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Implied end tags
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#generate-implied-end-tags</summary>
    private void GenerateImpliedEndTags(TagId except = TagId.Unknown)
    {
        while (_openElements.Count > 0)
        {
            var node = CurrentNode;
            if (!node.IsHtmlNamespace || node.TagId == except)
                return;

            if (node.TagId is not (TagId.Dd or TagId.Dt or TagId.Li or TagId.Optgroup or TagId.Option or TagId.P or TagId.Rb or TagId.Rp or TagId.Rt or TagId.Rtc))
                return;

            PopCurrentNode();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#generate-all-implied-end-tags-thoroughly</summary>
    private void GenerateAllImpliedEndTagsThoroughly()
    {
        while (_openElements.Count > 0)
        {
            var node = CurrentNode;
            if (!node.IsHtmlNamespace)
                return;

            if (node.TagId is not (TagId.Caption or TagId.Colgroup or TagId.Dd or TagId.Dt or TagId.Li or TagId.Optgroup or TagId.Option or
                TagId.P or TagId.Rb or TagId.Rp or TagId.Rt or TagId.Rtc or TagId.Tbody or TagId.Td or TagId.Tfoot or TagId.Th or TagId.Thead or TagId.Tr))
                return;

            PopCurrentNode();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Insertion
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#appropriate-place-for-inserting-a-node</summary>
    private (Node Parent, Node? Before) GetAppropriatePlaceForInsertingANode(Node? overrideTarget = null)
    {
        var targetParent = overrideTarget ?? CurrentNode;
        Node? referenceChild = null;
        if (_fosterParenting && targetParent is Element fosterTarget && fosterTarget.IsHtmlNamespace && fosterTarget.TagId is TagId.Table or TagId.Tbody or TagId.Tfoot or TagId.Thead or TagId.Tr)
        {
            var lastTemplateOrTableIndex = -1;
            for (var i = _openElements.Count - 1; i >= 0; i--)
            {
                if (_openElements[i].IsHtml(TagId.Template) || _openElements[i].IsHtml(TagId.Table))
                {
                    lastTemplateOrTableIndex = i;
                    break;
                }
            }

            if (lastTemplateOrTableIndex < 0)
                return (_openElements[0], null);

            var lastTemplateOrTable = _openElements[lastTemplateOrTableIndex];
            if (lastTemplateOrTable.IsHtml(TagId.Template))
            {
                targetParent = lastTemplateOrTable;
            }
            else if (lastTemplateOrTable.ParentNode is not null)
            {
                targetParent = lastTemplateOrTable.ParentNode;
                referenceChild = lastTemplateOrTable;
            }
            else
            {
                targetParent = _openElements[lastTemplateOrTableIndex - 1];
            }
        }

        if (_sanitizerActions is not null)
        {
            var parent = SkipElementsReplacedWithChildren(targetParent);
            if (!ReferenceEquals(parent, targetParent))
            {
                targetParent = parent;
                referenceChild = null;
            }
        }

        if (targetParent is not HtmlTemplateElement template)
            return (targetParent, referenceChild);

        var patchInsertionTarget = template.InsertionTarget;
        if (patchInsertionTarget is null)
            return (template.TemplateContents, null);

        if (template.InsertionEndMarker is { } endMarker && ReferenceEquals(endMarker.ParentNode, patchInsertionTarget))
            return (patchInsertionTarget, endMarker);

        return (patchInsertionTarget, null);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#adjusted-insertion-location</summary>
    private (Node Parent, Node? Before) GetAdjustedInsertionLocation(Node? overrideTarget = null)
    {
        var location = GetAppropriatePlaceForInsertingANode(overrideTarget);
        if (RootInsertionTarget is not null && ReferenceEquals(location.Parent, _openElements[0]))
            return (RootInsertionTarget, RootInsertionReferenceChild);

        return location;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#create-an-element-for-the-token</summary>
    private Element CreateElementForToken(HtmlTagToken token, string @namespace, Node intendedParent)
    {
        // When streaming, the elements of the root are created in the node document of the node into which they are inserted,
        // as their descendants (https://whatpr.org/html/12758/parsing.html#create-an-element-for-the-token)
        if (IsStreaming && ReferenceEquals(intendedParent, _openElements[0]))
        {
            intendedParent = RootInsertionTarget!;
        }

        var document = intendedParent.NodeDocument;
        var sanitizerElementName = new SanitizerName(token.Name, @namespace);
        var sanitizerAction = Sanitizer?.GetElementAction(sanitizerElementName) ?? SanitizerElementAction.Keep;
        var sanitizerElement = Sanitizer?.FindElement(sanitizerElementName);

        // The elements removed or replaced by the sanitizer are created in an inert document, so their descendants (created in
        // their node document) have no side effect, e.g. an image fetch (https://whatpr.org/html/12758/parsing.html#create-an-element-for-the-token)
        if (sanitizerAction != SanitizerElementAction.Keep)
        {
            document = document.GetAppropriateTemplateContentsOwnerDocument();
        }

        string? isValue = null;
        CustomElementRegistry? registry = null;

        // The elements removed or replaced by their children by the sanitizer are never inserted: they are not custom elements
        // and have no attributes. So are their descendants created in a removed element.
        if (sanitizerAction == SanitizerElementAction.Keep)
        {
            isValue = token.GetAttribute("is");
            if (isValue is not null && Sanitizer is not null && Sanitizer.RemovesAttribute(sanitizerElementName, sanitizerElement, "is", namespaceUri: null, isValue))
            {
                isValue = null;
            }

            registry = CustomElementRegistry.LookUp(intendedParent);
        }


        // Custom element constructors run synchronously, except for the fragment parsing algorithm (but when streaming)
        var willExecuteScript = (ContextElement is null || IsStreaming) && registry is { HasAnyDefinition: true } && CustomElementRegistry.LookUpDefinition(registry, @namespace, token.Name, isValue) is not null;
        var scope = default(CEReactionsScope);
        if (willExecuteScript)
        {
            // document.open()/write()/close() throw while the custom element constructor runs
            document.ThrowOnDynamicMarkupInsertionCounter++;
            document.EventLoop?.PerformMicrotaskCheckpointBeforeCustomElementConstruction();
            scope = CEReactionsScope.EnterAndPush();
        }

        Element element;
        try
        {
            element = ElementFactory.CreateElement(document, token.Name, @namespace, prefix: null, isValue, willExecuteScript, registry);
            if (sanitizerAction == SanitizerElementAction.Keep)
            {
                element.EnsureAttributeCapacity(token.Attributes.Length);
                foreach (ref var attribute in token.Attributes)
                {
                    if (Sanitizer is null || !Sanitizer.RemovesAttribute(sanitizerElementName, sanitizerElement, attribute.LocalName, attribute.Namespace, attribute.Value))
                    {
                        element.AppendAttribute(attribute.LocalName, attribute.Namespace, attribute.Prefix, attribute.Value);
                    }
                }
            }
            else
            {
                _sanitizerActions![element] = sanitizerAction;
            }
        }
        finally
        {
            scope.Dispose();
            if (willExecuteScript)
            {
                document.ThrowOnDynamicMarkupInsertionCounter--;
            }
        }

        // https://w3c.github.io/webappsec-csp/#is-element-nonceable
        if (token.HadDuplicateAttribute)
        {
            element.ElementRareData.HadDuplicateAttributeParseError = true;
        }

        // A style element is pushed onto the stack of open elements after it is inserted: its style block is updated when it is
        // popped (see Element.OnStyleElementStackOfOpenElementsChanged)
        if (element is HtmlStyleElement or SvgStyleElement)
        {
            element.OnStyleElementStackOfOpenElementsChanged(onStack: true);
        }

        // The position of the inline style sheet or script in the document, for the source location of the violations of the
        // content security policies (the line, and the column after the start tag)
        if (element is HtmlStyleElement or SvgStyleElement or SvgScriptElement && _document.ContentSecurityPoliciesOrNull is not null && ScriptingMode != ParserScriptingMode.Fragment)
        {
            element.ElementRareData.InlineSourcePosition = _tokenizer.GetCurrentLineAndColumn();
        }

        if (element is MathMLElement mathElement && mathElement.TagId == TagId.AnnotationXml)
        {
            var encoding = token.GetAttribute("encoding");
            mathElement.IsHtmlIntegrationPoint = encoding is not null && (InfraAscii.EqualsIgnoreAsciiCase(encoding, "text/html") || InfraAscii.EqualsIgnoreAsciiCase(encoding, "application/xhtml+xml"));
        }

        // https://html.spec.whatwg.org/multipage/semantics.html#contributes-a-script-blocking-style-sheet: the style sheets of
        // the style and link elements created by the parser of a document block its scripts
        if (ScriptingMode == ParserScriptingMode.Normal)
        {
            if (element is HtmlLinkElement link)
            {
                link.CreatedByParser = true;
            }
            else if (element is HtmlStyleElement style)
            {
                style.CreatedByParser = true;
            }
        }

        // https://html.spec.whatwg.org/multipage/media.html#blocked-on-parser: the flag of a media element created by a parser
        // is set until the element is popped off the stack of open elements
        (element as HtmlMediaElement)?.OnCreatedByParser();

        if (element is HtmlElement htmlElement && !htmlElement.IsFormAssociatedCustomElement)
        {
            // https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-form-reset-control
            htmlElement.ResetFromParser();

            if (_formElement is not null && !IsParsingTemplateContents && ContextElement is null && htmlElement.IsFormAssociated &&
                !(htmlElement.IsListed && htmlElement.HasAttribute("form")) && ReferenceEquals(intendedParent.GetRoot(), _formElement.GetRoot()))
            {
                htmlElement.AssociateWithFormFromParser(_formElement);
            }
        }

        return element;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#insert-an-element-at-the-adjusted-insertion-location</summary>
    private void InsertElementAtAdjustedInsertionLocation(Element element, Node? insertionLocationParent = null)
    {
        if (GetSanitizerAction(element) != SanitizerElementAction.Keep)
            return;

        var (targetParent, referenceChild) = GetAdjustedInsertionLocation(insertionLocationParent);
        if (element.ParentNode is not null || element.IsHostIncludingInclusiveAncestorOf(targetParent) || (targetParent is Document document && document.DocumentElement is not null) || IsDisplaced(targetParent, referenceChild))
            return;

        targetParent.InsertBeforeCore(element, referenceChild);
    }

    /// <summary>
    /// Whether the reference child of an insertion location is no longer a child of its parent (e.g. a script or a custom
    /// element reaction moved it while HTML is streamed): the node is not inserted (https://whatpr.org/html/12758/parsing.html#insert-a-character).
    /// </summary>
    private static bool IsDisplaced(Node targetParent, Node? referenceChild) => referenceChild is not null && !ReferenceEquals(referenceChild.ParentNode, targetParent);

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#insert-a-foreign-element</summary>
    private Element InsertForeignElement(HtmlTagToken token, string @namespace, bool onlyAddToElementStack)
    {
        var adjustedInsertionLocation = GetAppropriatePlaceForInsertingANode();
        var element = CreateElementForToken(token, @namespace, adjustedInsertionLocation.Parent);

        // The SVG script elements are processed when the parser reaches their end tag. As for the HTML script elements, the
        // scripts of the Fragment scripting mode are not parser-inserted, and the scripts of the Inert scripting mode never run.
        if (element is SvgScriptElement svgScript)
        {
            svgScript.IsParserInserted = ScriptingMode != ParserScriptingMode.Fragment;
            if (ScriptingMode == ParserScriptingMode.Inert)
            {
                svgScript.MarkAsAlreadyProcessed();
            }
        }

        if (!onlyAddToElementStack)
        {
            // The element is pushed onto the stack of open elements once it is inserted: its insertion steps consider that it
            // is on the stack already (see IsOnStackOfOpenElements)
            _elementBeingInserted = element;
            InsertElementAtAdjustedInsertionLocation(element);
            _elementBeingInserted = null;
            _speculativeElementInserted?.Invoke(element);
        }

        PushOpenElement(element);
        return element;
    }

    private Element? _elementBeingInserted;

    /// <summary>
    /// Whether the element is on the stack of open elements, or is being inserted by the parser, which then pushes it onto the
    /// stack: a <c>link rel=expect</c> element waits until the element it expects is popped off the stack
    /// (https://html.spec.whatwg.org/multipage/links.html#process-internal-resource-link), so the start tag of the element
    /// must not unblock the rendering (the insertion steps of the specification run before the element is pushed).
    /// </summary>
    internal bool IsOnStackOfOpenElements(Element element) => ReferenceEquals(_elementBeingInserted, element) || IsInOpenElements(element);

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#insert-an-html-element</summary>
    private Element InsertHtmlElement(HtmlTagToken token) => InsertForeignElement(token, Namespaces.Html, onlyAddToElementStack: false);

    private Element InsertHtmlElement(TagId tagId) => InsertHtmlElement(new HtmlTagToken(TagNames.GetName(tagId), tagId, [], selfClosing: false));

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#insert-a-character</summary>
    private void InsertCharacters(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
            return;

        var (targetParent, referenceChild) = GetAdjustedInsertionLocation();
        if (targetParent is Document || IsDisplaced(targetParent, referenceChild))
            return;

        var previousSibling = referenceChild is not null ? referenceChild.PreviousSibling : targetParent.LastChild;
        if (previousSibling is Text text)
        {
            text.SetDataCore(string.Concat(text.Data, data));

            // The style sheet parsed from the previous text (e.g. a style element written by several document.write() calls) is outdated
            if (targetParent is HtmlStyleElement style)
            {
                style.OnParserAppendedText();
            }

            return;
        }

        targetParent.InsertBeforeCore(new Text(targetParent.NodeDocument, _tokenizer.NameTable.GetText(data)), referenceChild);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#insert-a-comment</summary>
    private void InsertComment(string data, Node? insertionLocationParent = null)
    {
        if (Sanitizer is { KeepsComments: false })
            return;

        var (targetParent, referenceChild) = GetAdjustedInsertionLocation(insertionLocationParent);
        if (IsDisplaced(targetParent, referenceChild))
            return;

        targetParent.InsertBeforeCore(new Comment(targetParent.NodeDocument, data), referenceChild);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#insert-a-processing-instruction</summary>
    private void InsertProcessingInstruction(string target, string data, Node? insertionLocationParent = null)
    {
        if (Sanitizer is not null && !Sanitizer.KeepsProcessingInstruction(target))
            return;

        var (targetParent, referenceChild) = GetAdjustedInsertionLocation(insertionLocationParent);
        if (IsDisplaced(targetParent, referenceChild))
            return;

        targetParent.InsertBeforeCore(new ProcessingInstruction(targetParent.NodeDocument, target, data, validate: false), referenceChild);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // List of active formatting elements
    // ----------------------------------------------------------------------------------------------------------------

    private void InsertMarker() => _activeFormatting.Add(new FormattingEntry(null, null));

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#push-onto-the-list-of-active-formatting-elements</summary>
    private void PushActiveFormattingElement(Element element, HtmlTagToken token)
    {
        var count = 0;
        var earliest = -1;
        for (var i = _activeFormatting.Count - 1; i >= 0; i--)
        {
            var entry = _activeFormatting[i];
            if (entry.IsMarker)
                break;

            if (entry.Element!.LocalName == element.LocalName && ReferenceEquals(entry.Element.NamespaceUri, element.NamespaceUri) && HaveSameAttributes(entry.Token!, token))
            {
                count++;
                earliest = i;
            }
        }

        if (count >= 3)
        {
            _activeFormatting.RemoveAt(earliest);
        }

        token.Retained = true;
        _activeFormatting.Add(new FormattingEntry(element, token));
    }

    private static bool HaveSameAttributes(HtmlTagToken a, HtmlTagToken b)
    {
        if (a.Attributes.Length != b.Attributes.Length)
            return false;

        foreach (ref var attribute in a.Attributes)
        {
            var found = false;
            foreach (ref var other in b.Attributes)
            {
                if (attribute.LocalName == other.LocalName && attribute.Namespace == other.Namespace && attribute.Value == other.Value)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#reconstruct-the-active-formatting-elements</summary>
    private void ReconstructActiveFormattingElements()
    {
        if (_activeFormatting.Count == 0)
            return;

        var last = _activeFormatting[^1];
        if (last.IsMarker || IsInOpenElements(last.Element!))
            return;

        var entryIndex = _activeFormatting.Count - 1;

        // Rewind
        while (entryIndex > 0)
        {
            var previous = _activeFormatting[entryIndex - 1];
            if (previous.IsMarker || IsInOpenElements(previous.Element!))
                break;

            entryIndex--;
        }

        // Advance and create
        for (; entryIndex < _activeFormatting.Count; entryIndex++)
        {
            var token = _activeFormatting[entryIndex].Token!;
            var newElement = InsertHtmlElement(token);
            _activeFormatting[entryIndex] = new FormattingEntry(newElement, token);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#clear-the-list-of-active-formatting-elements-up-to-the-last-marker</summary>
    private void ClearActiveFormattingElementsUpToLastMarker()
    {
        while (_activeFormatting.Count > 0)
        {
            var entry = _activeFormatting[^1];
            _activeFormatting.RemoveAt(_activeFormatting.Count - 1);
            if (entry.IsMarker)
                return;
        }
    }

    private int IndexOfActiveFormattingElement(Element element)
    {
        for (var i = _activeFormatting.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_activeFormatting[i].Element, element))
                return i;
        }

        return -1;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#reset-the-insertion-mode-appropriately</summary>
    private void ResetInsertionModeAppropriately()
    {
        var last = false;
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (i == 0)
            {
                last = true;
                if (ContextElement is not null)
                {
                    node = ContextElement;
                }
            }

            if (node.IsHtmlNamespace)
            {
                switch (node.TagId)
                {
                    case TagId.Td or TagId.Th when !last:
                        _mode = InsertionMode.InCell;
                        return;
                    case TagId.Tr:
                        _mode = InsertionMode.InRow;
                        return;
                    case TagId.Tbody or TagId.Thead or TagId.Tfoot:
                        _mode = InsertionMode.InTableBody;
                        return;
                    case TagId.Caption:
                        _mode = InsertionMode.InCaption;
                        return;
                    case TagId.Colgroup:
                        _mode = InsertionMode.InColumnGroup;
                        return;
                    case TagId.Table:
                        _mode = InsertionMode.InTable;
                        return;
                    case TagId.Template:
                        _mode = _templateModes[^1];
                        return;
                    case TagId.Head when !last:
                        _mode = InsertionMode.InHead;
                        return;
                    case TagId.Body:
                        _mode = InsertionMode.InBody;
                        return;
                    case TagId.Frameset:
                        _mode = InsertionMode.InFrameset;
                        return;
                    case TagId.Html:
                        _mode = _headElement is null ? InsertionMode.BeforeHead : InsertionMode.AfterHead;
                        return;
                }
            }

            if (last)
            {
                _mode = InsertionMode.InBody;
                return;
            }
        }
    }
}
