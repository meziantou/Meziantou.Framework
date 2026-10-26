using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

internal sealed partial class HtmlTreeBuilder
{
    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.1 The "initial" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInitial(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                token.Characters = token.Characters[whitespace..];
                if (token.Characters.IsEmpty)
                    return ProcessResult.Done;

                break;
            }

            case TokenKind.Comment:
                InsertComment(token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ProcessDoctypeInInitialMode(in token);
                _mode = InsertionMode.BeforeHtml;
                return ProcessResult.Done;
        }

        if (!_document.IsIframeSrcdocDocument)
        {
            ParseError("expected-doctype-but-got-other");
            if (!_document.ParserCannotChangeTheMode)
            {
                _document.Mode = DocumentMode.Quirks;
            }
        }

        _mode = InsertionMode.BeforeHtml;
        return ProcessResult.Reprocess;
    }

    private void ProcessDoctypeInInitialMode(in Token token)
    {
        var name = token.DoctypeName;
        var publicId = token.DoctypePublicId;
        var systemId = token.DoctypeSystemId;
        if (name != "html" || publicId is not null || (systemId is not null && systemId != "about:legacy-compat"))
        {
            ParseError("unexpected-doctype");
        }

        var doctype = new DocumentType(_document, name ?? "", publicId ?? "", systemId ?? "");
        if (_document.Doctype is null && _document.DocumentElement is null)
        {
            _document.AppendChildCore(doctype);
        }

        if (_document.IsIframeSrcdocDocument || _document.ParserCannotChangeTheMode)
            return;

        if (QuirksMode.IsQuirks(token.DoctypeForceQuirks, name, publicId, systemId))
        {
            _document.Mode = DocumentMode.Quirks;
        }
        else if (QuirksMode.IsLimitedQuirks(publicId, systemId))
        {
            _document.Mode = DocumentMode.LimitedQuirks;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.2 The "before html" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessBeforeHtml(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.Comment:
                InsertComment(token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                token.Characters = token.Characters[whitespace..];
                if (token.Characters.IsEmpty)
                    return ProcessResult.Done;

                break;
            }

            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
            {
                var element = CreateElementForToken(token.Tag, Namespaces.Html, _document);
                InsertElementAtAdjustedInsertionLocation(element, _document);
                PushOpenElement(element);
                _mode = InsertionMode.BeforeHead;
                return ProcessResult.Done;
            }

            case TokenKind.EndTag when token.Tag.TagId is TagId.Head or TagId.Body or TagId.Html or TagId.Br:
                break;

            case TokenKind.EndTag:
                ParseError("unexpected-end-tag");
                return ProcessResult.Done;
        }

        var html = ElementFactory.CreateElement(_document, "html", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        _document.AppendChildCore(html);
        PushOpenElement(html);
        _mode = InsertionMode.BeforeHead;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.3 The "before head" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessBeforeHead(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                token.Characters = token.Characters[whitespace..];
                if (token.Characters.IsEmpty)
                    return ProcessResult.Done;

                break;
            }

            case TokenKind.Comment:
                InsertComment(token.Data);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
                return ProcessInBody(ref token);

            case TokenKind.StartTag when token.Tag.TagId == TagId.Head:
                _headElement = InsertHtmlElement(token.Tag);
                _mode = InsertionMode.InHead;
                return ProcessResult.Done;

            case TokenKind.EndTag when token.Tag.TagId is TagId.Head or TagId.Body or TagId.Html or TagId.Br:
                break;

            case TokenKind.EndTag:
                ParseError("unexpected-end-tag");
                return ProcessResult.Done;
        }

        _headElement = InsertHtmlElement(TagId.Head);
        _mode = InsertionMode.InHead;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.4 The "in head" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInHead(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                InsertCharacters(token.Characters[..whitespace]);
                token.Characters = token.Characters[whitespace..];
                if (token.Characters.IsEmpty)
                    return ProcessResult.Done;

                break;
            }

            case TokenKind.Comment:
                InsertComment(token.Data);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Html:
                        return ProcessInBody(ref token);

                    case TagId.Base or TagId.Basefont or TagId.Bgsound or TagId.Link:
                        InsertHtmlElement(token.Tag);
                        PopCurrentNode();
                        token.Tag.SelfClosingAcknowledged = true;
                        return ProcessResult.Done;

                    case TagId.Meta:
                    {
                        var element = InsertHtmlElement(token.Tag);
                        PopCurrentNode();
                        token.Tag.SelfClosingAcknowledged = true;
                        if (_mode == InsertionMode.InHead && _templateModes.Count == 0)
                        {
                            MetaElementInsertedInHead?.Invoke(element);
                        }

                        return ProcessResult.Done;
                    }

                    case TagId.Title:
                        ParseGenericTextElement(token.Tag, TokenizerState.Rcdata);
                        return ProcessResult.Done;

                    case TagId.Noscript when ScriptingMode != ParserScriptingMode.Disabled:
                    case TagId.Noframes or TagId.Style:
                        ParseGenericTextElement(token.Tag, TokenizerState.Rawtext);
                        return ProcessResult.Done;

                    case TagId.Noscript:
                        InsertHtmlElement(token.Tag);
                        _mode = InsertionMode.InHeadNoscript;
                        return ProcessResult.Done;

                    case TagId.Script:
                    {
                        var location = GetAppropriatePlaceForInsertingANode();
                        var element = CreateElementForToken(token.Tag, Namespaces.Html, location.Parent);

                        // https://html.spec.whatwg.org/multipage/parsing.html#scriptTag: the Fragment scripting mode treats the scripts
                        // as if they were not parser-inserted (their force async stays true, so the external ones run asynchronously). The scripts parsed in the Inert scripting mode never run. When HTML
                        // is streamed, the scripts are parser-inserted until their end tag, so they are not prepared when they
                        // are inserted with a part of their text.
                        if (element is HtmlScriptElement script)
                        {
                            if (ScriptingMode != ParserScriptingMode.Fragment || IsStreaming)
                            {
                                script.ParserDocument = _document;
                                script.SourceTextPosition = _tokenizer.GetCurrentLineAndColumn();
                                script.ForceAsync = false;
                            }

                            if (ScriptingMode == ParserScriptingMode.Inert)
                            {
                                script.AlreadyStarted = true;
                            }
                        }

                        InsertElementAtAdjustedInsertionLocation(element, location.Parent);
                        _speculativeElementInserted?.Invoke(element);
                        PushOpenElement(element);
                        _tokenizer.State = TokenizerState.ScriptData;
                        _originalMode = _mode;
                        _mode = InsertionMode.Text;
                        return ProcessResult.Done;
                    }

                    case TagId.Template:
                        ProcessTemplateStartTag(token.Tag);
                        return ProcessResult.Done;

                    case TagId.Head:
                        ParseError("unexpected-start-tag");
                        return ProcessResult.Done;
                }

                break;

            case TokenKind.EndTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Head:
                        PopCurrentNode();
                        _mode = InsertionMode.AfterHead;
                        return ProcessResult.Done;

                    case TagId.Body or TagId.Html or TagId.Br:
                        break;

                    case TagId.Template:
                        ProcessTemplateEndTag();
                        return ProcessResult.Done;

                    default:
                        ParseError("unexpected-end-tag");
                        return ProcessResult.Done;
                }

                break;
        }

        PopCurrentNode();
        _mode = InsertionMode.AfterHead;
        return ProcessResult.Reprocess;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#generic-raw-text-element-parsing-algorithm</summary>
    private void ParseGenericTextElement(HtmlTagToken tag, TokenizerState state)
    {
        InsertHtmlElement(tag);
        _tokenizer.State = state;
        _originalMode = _mode;
        _mode = InsertionMode.Text;
    }

    private void ProcessTemplateStartTag(HtmlTagToken templateStartTag)
    {
        InsertMarker();
        _framesetOk = false;
        _mode = InsertionMode.InTemplate;
        _templateModes.Add(InsertionMode.InTemplate);
        var adjustedInsertionLocation = GetAppropriatePlaceForInsertingANode();

        // A template removed by the sanitizer does not attach a shadow root nor patch the content, and the shadow root uses
        // the attributes kept by the sanitizer
        if (Sanitizer is not null)
        {
            if (Sanitizer.GetElementAction(new SanitizerName(templateStartTag.Name, Namespaces.Html)) != SanitizerElementAction.Keep)
            {
                InsertHtmlElement(templateStartTag);
                return;
            }

            templateStartTag = GetSanitizedToken(templateStartTag, Namespaces.Html);
        }

        var shadowRootMode = templateStartTag.GetAttribute("shadowrootmode");
        ShadowRootMode? mode = shadowRootMode is null ? null
            : InfraAscii.EqualsIgnoreAsciiCase(shadowRootMode, "open") ? ShadowRootMode.Open
            : InfraAscii.EqualsIgnoreAsciiCase(shadowRootMode, "closed") ? ShadowRootMode.Closed
            : null;

        if (mode is not null)
        {
            // "the adjusted current node is the topmost element in the stack of open elements": as in the browsers, the
            // current node is checked, so that, in the fragment case, a template that becomes a child of the context element
            // (whose adjusted current node is the context element, which is not in the stack) does not attach a shadow root
            // to the context element
            if (!AllowDeclarativeShadowRoots || ReferenceEquals(CurrentNode, _openElements[0]))
            {
                InsertHtmlElement(templateStartTag);
                return;
            }

            var declarativeShadowHostElement = AdjustedCurrentNode!;
            var template = (HtmlTemplateElement)InsertForeignElement(templateStartTag, Namespaces.Html, onlyAddToElementStack: true);
            var delegatesFocus = templateStartTag.HasAttribute("shadowrootdelegatesfocus");
            var serializable = templateStartTag.HasAttribute("shadowrootserializable");
            var slotAssignmentValue = templateStartTag.GetAttribute("shadowrootslotassignment");
            var slotAssignment = slotAssignmentValue is not null && InfraAscii.EqualsIgnoreAsciiCase(slotAssignmentValue, "manual") ? SlotAssignmentMode.Manual : SlotAssignmentMode.Named;
            var clonable = templateStartTag.HasAttribute("shadowrootclonable");

            if (declarativeShadowHostElement.AttachedShadowRoot is not null)
            {
                InsertElementAtAdjustedInsertionLocation(template, adjustedInsertionLocation.Parent);
                return;
            }

            var keepRegistryNull = templateStartTag.HasAttribute("shadowrootcustomelementregistry");
            var registry = keepRegistryNull ? null : declarativeShadowHostElement.NodeDocument.CustomElementRegistryStorage;
            ShadowRoot shadow;
            try
            {
                shadow = declarativeShadowHostElement.AttachShadowRoot(mode.Value, clonable, serializable, delegatesFocus, slotAssignment, registry);
            }
            catch (DomException)
            {
                InsertElementAtAdjustedInsertionLocation(template, adjustedInsertionLocation.Parent);
                return;
            }

            shadow.AvailableToElementInternals = true;
            shadow.Declarative = true;
            template.TemplateContents = shadow;
            if (keepRegistryNull)
            {
                shadow.KeepCustomElementRegistryNull = true;
            }

            return;
        }

        if (templateStartTag.HasAttribute("for"))
        {
            // The node that contains the adjusted insertion location: in the fragment case, the DocumentFragment into which
            // the nodes are inserted (the parser's root insertion target)
            var scope = adjustedInsertionLocation.Parent;
            if (RootInsertionTarget is not null && ReferenceEquals(scope, _openElements[0]))
            {
                scope = RootInsertionTarget;
            }

            if (scope is HtmlTemplateElement scopeTemplate)
            {
                scope = scopeTemplate.TemplateContents;
            }
            else if (scope is Element scopeElement && ReferenceEquals(scopeElement, GetBodyElement(scopeElement.NodeDocument)))
            {
                scope = scopeElement.ParentNode!;
            }

            // The tentative extensions of the declarative partial updates (https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md):
            // the buffered content and the sanitized content of a template that patches content. They are read from the start
            // tag: a later change of the attributes has no effect. (The in-place patch of an empty for attribute is not
            // supported: it contradicts the prepare content patching steps, where an empty marker name patches nothing.)
            var extensions = ContentPatching.AreExtensionsEnabled(adjustedInsertionLocation.Parent.NodeDocument);
            var buffered = extensions && templateStartTag.HasAttribute("buffer");
            var sanitizes = extensions && ContentPatching.IsSanitizing(templateStartTag.GetAttribute("sanitize"));
            var src = extensions ? templateStartTag.GetAttribute("src") : null;
            var template = (HtmlTemplateElement)InsertForeignElement(templateStartTag, Namespaces.Html, onlyAddToElementStack: true);
            if (!ContentPatching.Prepare(scope, template, removeContent: !buffered && src is null))
            {
                PopCurrentNode();
                InsertElementAtAdjustedInsertionLocation(template);
                PushOpenElement(template);
                return;
            }

            if (src is not null)
            {
                // The content is fetched (see HtmlTemplateElement.StartSourcePatch): the template is inserted where it is, and its
                // children are parsed into its template contents (they are ignored)
                var (start, end) = (template.InsertionStartMarker!, template.InsertionEndMarker);
                template.InsertionTarget = null;
                template.InsertionStartMarker = null;
                template.InsertionEndMarker = null;
                PopCurrentNode();
                InsertElementAtAdjustedInsertionLocation(template);
                PushOpenElement(template);
                template.StartSourcePatch(src, start, end, buffered, sanitizes);
            }
            else if (buffered)
            {
                // A buffered template is inserted where it is, and its content is parsed into its template contents: the content
                // of its markers is replaced when it is closed
                var patch = new ContentPatch { Buffered = true, StartMarker = template.InsertionStartMarker, EndMarker = template.InsertionEndMarker, Sanitizes = sanitizes, PreviousSanitizer = Sanitizer };
                template.InsertionTarget = null;
                template.InsertionStartMarker = null;
                template.InsertionEndMarker = null;
                PopCurrentNode();
                InsertElementAtAdjustedInsertionLocation(template);
                PushOpenElement(template);
                template.Patch = patch;
                BeginPatchSanitization(patch);
            }
            else if (sanitizes)
            {
                template.Patch = new ContentPatch { Sanitizes = true, PreviousSanitizer = Sanitizer };
                BeginPatchSanitization(template.Patch);
            }

            return;
        }

        InsertHtmlElement(templateStartTag);
    }

    /// <summary>
    /// The steps of a template end tag (https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inhead) for a template
    /// that patches content: its markers are removed. The tentative patches end too (see <see cref="FinishContentPatch"/>).
    /// </summary>
    private void RemovePatchMarkers(HtmlTemplateElement template)
    {
        if (template.InsertionTarget is not null && template.InsertionStartMarker is { } start)
        {
            start.RemoveFromParent();
            template.InsertionEndMarker?.RemoveFromParent();
        }

        template.InsertionTarget = null;
        template.InsertionStartMarker = null;
        template.InsertionEndMarker = null;
        FinishContentPatch(template);
    }

    /// <summary>
    /// The safe sanitization of the content of a template with the tentative <c>sanitize</c> attribute: the elements and the
    /// attributes of the content are sanitized while they are parsed (see HtmlTreeBuilder.Sanitizer.cs) with the built-in safe
    /// default configuration, or, inside the content of another sanitizer (e.g. <c>setHTML()</c>), with that configuration
    /// without its unsafe elements and attributes (the content cannot escape the sanitization of an outer template).
    /// </summary>
    private void BeginPatchSanitization(ContentPatch patch)
    {
        if (!patch.Sanitizes)
            return;

        if (Sanitizer is { } outer)
        {
            Sanitizer = SanitizerConfiguration.GetEffectiveConfiguration(outer, safe: true);
            return;
        }

        var configuration = SanitizerConfiguration.CreateDefault();
        configuration.ApplyDefaults(permissiveDefaults: false);
        Sanitizer = SanitizerConfiguration.GetEffectiveConfiguration(configuration, safe: true);
    }

    /// <summary>
    /// Ends the tentative patch of a template when it is closed (its end tag, or popped off the stack of open elements): the
    /// sanitizer of the parser is restored; the buffered content is inserted at once in place of the content between the
    /// markers of the target, then the template is removed. A canceled patch (the template was removed meanwhile) inserts
    /// nothing. The scripts of the buffered content run when it is inserted.
    /// </summary>
    private void FinishContentPatch(HtmlTemplateElement template)
    {
        if (template.Patch is not { } patch)
            return;

        template.Patch = null;
        if (patch.Sanitizes)
        {
            Sanitizer = patch.PreviousSanitizer;
        }

        if (patch.Canceled)
            return;

        if (!patch.Buffered)
            return;

        if (patch.StartMarker is { ParentNode: { } target } start)
        {
            ContentPatching.RemoveContentBetweenMarkers(start, patch.EndMarker);
            var end = patch.EndMarker is { } endMarker && ReferenceEquals(endMarker.ParentNode, target) ? endMarker : null;
            target.InsertBefore(template.TemplateContents, end);
            start.RemoveFromParent();
            end?.RemoveFromParent();
        }

        template.RemoveFromParent();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#the-body-element-2</summary>
    private static Element? GetBodyElement(Document document)
    {
        if (document.DocumentElement is not { } html || !html.IsHtml(TagId.Html))
            return null;

        for (var child = html.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element element && (element.IsHtml(TagId.Body) || element.IsHtml(TagId.Frameset)))
                return element;
        }

        return null;
    }

    private void ProcessTemplateEndTag()
    {
        if (!HasTemplateInOpenElements())
        {
            ParseError("unexpected-end-tag");
            return;
        }

        GenerateAllImpliedEndTagsThoroughly();
        if (!CurrentNode.IsHtml(TagId.Template))
        {
            ParseError("end-tag-too-early");
        }

        HtmlTemplateElement? template = null;
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            if (_openElements[i] is HtmlTemplateElement candidate)
            {
                template = candidate;
                break;
            }
        }

        // The tentative patch ends once the template is closed: the scripts of its buffered content run when it is inserted
        var patch = template!.Patch;
        template.Patch = null;
        RemovePatchMarkers(template);
        PopUntil(TagId.Template);
        ClearActiveFormattingElementsUpToLastMarker();
        _templateModes.RemoveAt(_templateModes.Count - 1);
        ResetInsertionModeAppropriately();
        if (patch is not null)
        {
            template.Patch = patch;
            FinishContentPatch(template);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.5 The "in head noscript" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInHeadNoscript(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
                return ProcessInBody(ref token);

            case TokenKind.EndTag when token.Tag.TagId == TagId.Noscript:
                PopCurrentNode();
                _mode = InsertionMode.InHead;
                return ProcessResult.Done;

            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                if (whitespace > 0)
                {
                    var whitespaceToken = token;
                    whitespaceToken.Characters = token.Characters[..whitespace];
                    ProcessInHead(ref whitespaceToken);
                    token.Characters = token.Characters[whitespace..];
                    if (token.Characters.IsEmpty)
                        return ProcessResult.Done;
                }

                break;
            }

            case TokenKind.Comment:
            case TokenKind.ProcessingInstruction:
            case TokenKind.StartTag when token.Tag.TagId is TagId.Basefont or TagId.Bgsound or TagId.Link or TagId.Meta or TagId.Noframes or TagId.Style:
                return ProcessInHead(ref token);

            case TokenKind.EndTag when token.Tag.TagId == TagId.Br:
                break;

            case TokenKind.StartTag when token.Tag.TagId is TagId.Head or TagId.Noscript:
            case TokenKind.EndTag:
                ParseError("unexpected-tag");
                return ProcessResult.Done;
        }

        ParseError("unexpected-token-in-head-noscript");
        PopCurrentNode();
        _mode = InsertionMode.InHead;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.6 The "after head" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessAfterHead(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                InsertCharacters(token.Characters[..whitespace]);
                token.Characters = token.Characters[whitespace..];
                if (token.Characters.IsEmpty)
                    return ProcessResult.Done;

                break;
            }

            case TokenKind.Comment:
                InsertComment(token.Data);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Html:
                        return ProcessInBody(ref token);

                    case TagId.Body:
                        HasInsertedBodyElement = true;
                        InsertHtmlElement(token.Tag);
                        _framesetOk = false;
                        _mode = InsertionMode.InBody;
                        return ProcessResult.Done;

                    case TagId.Frameset:
                        HasInsertedBodyElement = true;
                        InsertHtmlElement(token.Tag);
                        _mode = InsertionMode.InFrameset;
                        return ProcessResult.Done;

                    case TagId.Base or TagId.Basefont or TagId.Bgsound or TagId.Link or TagId.Meta or TagId.Noframes or TagId.Script or TagId.Style or TagId.Template or TagId.Title:
                    {
                        ParseError("unexpected-start-tag-after-head");
                        var head = _headElement!;
                        PushOpenElement(head);
                        var result = ProcessInHead(ref token);
                        RemoveFromOpenElements(head);
                        return result;
                    }

                    case TagId.Head:
                        ParseError("unexpected-start-tag");
                        return ProcessResult.Done;
                }

                break;

            case TokenKind.EndTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Template:
                        return ProcessInHead(ref token);

                    case TagId.Body or TagId.Html or TagId.Br:
                        break;

                    default:
                        ParseError("unexpected-end-tag");
                        return ProcessResult.Done;
                }

                break;
        }

        HasInsertedBodyElement = true;
        InsertHtmlElement(TagId.Body);
        _framesetOk = true;
        _mode = InsertionMode.InBody;
        return ProcessResult.Reprocess;
    }

    /// <summary>
    /// Whether the parser inserted the body (or frameset) element of the document: as in the browsers, a document that is
    /// being parsed is not rendered before (see <see cref="Document.IsRenderBlocked"/>).
    /// </summary>
    internal bool HasInsertedBodyElement { get; private set; }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.8 The "text" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessText(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
                InsertCharacters(token.Characters);
                return ProcessResult.Done;

            case TokenKind.EndOfFile:
                ParseError("eof-in-text");
                PopCurrentNode();
                _mode = _originalMode;
                return ProcessResult.Reprocess;

            case TokenKind.EndTag:
                // https://html.spec.whatwg.org/multipage/parsing.html#scriptEndTag: the script is prepared, and run when the
                // browsing context of the document has a script engine.
                var element = CurrentNode;
                if (element is HtmlScriptElement)
                {
                    PerformMicrotaskCheckpointBeforeScript();
                }

                PopCurrentNode();
                _mode = _originalMode;
                if (element is HtmlScriptElement script)
                {
                    // https://w3c.github.io/trusted-types/dist/spec/#setting-slot-values-from-parser
                    script.ScriptText = script.GetChildTextContent();

                    // The scripts streamed with runScripts run when their end tag is parsed, as the scripts inserted by a script
                    // (approximating https://whatpr.org/html/12758/parsing.html#scriptEndTag, which pauses the parser)
                    if (IsStreaming && ScriptingMode == ParserScriptingMode.Fragment)
                    {
                        script.ParserDocument = null;
                    }

                    // The characters written by the script are inserted just before the next input character
                    var oldInsertionPoint = InsertionPoint;
                    InsertionPoint = _tokenizer.PositionFromEnd;
                    ScriptNestingLevel++;
                    script.Prepare();
                    ScriptNestingLevel--;
                    if (ScriptNestingLevel == 0)
                    {
                        ParserPauseFlag = false;
                    }

                    InsertionPoint = oldInsertionPoint;
                    RunPendingSvgScripts();

                    // The parser pauses until the pending parsing-blocking script is executed (see ScriptingParser)
                    if (_document.PendingParsingBlockingScript is not null)
                    {
                        if (ScriptNestingLevel != 0)
                        {
                            ParserPauseFlag = true;
                        }
                        else
                        {
                            _pausedForScript = true;
                        }
                    }
                }

                return ProcessResult.Done;

            default:
                return ProcessResult.Done;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.9 The "in table" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInTable(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters when CurrentNode.IsHtmlNamespace && CurrentNode.TagId is TagId.Table or TagId.Tbody or TagId.Template or TagId.Tfoot or TagId.Thead or TagId.Tr:
                _pendingTableCharacters.Clear();
                _pendingTableCharactersContainNonWhitespace = false;
                _originalMode = _mode;
                _mode = InsertionMode.InTableText;
                return ProcessResult.Reprocess;

            case TokenKind.Comment:
                InsertComment(token.Data);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Caption:
                        ClearStackBackToTableContext();
                        InsertMarker();
                        InsertHtmlElement(token.Tag);
                        _mode = InsertionMode.InCaption;
                        return ProcessResult.Done;

                    case TagId.Colgroup:
                        ClearStackBackToTableContext();
                        InsertHtmlElement(token.Tag);
                        _mode = InsertionMode.InColumnGroup;
                        return ProcessResult.Done;

                    case TagId.Col:
                        ClearStackBackToTableContext();
                        InsertHtmlElement(TagId.Colgroup);
                        _mode = InsertionMode.InColumnGroup;
                        return ProcessResult.Reprocess;

                    case TagId.Tbody or TagId.Tfoot or TagId.Thead:
                        ClearStackBackToTableContext();
                        InsertHtmlElement(token.Tag);
                        _mode = InsertionMode.InTableBody;
                        return ProcessResult.Done;

                    case TagId.Td or TagId.Th or TagId.Tr:
                        ClearStackBackToTableContext();
                        InsertHtmlElement(TagId.Tbody);
                        _mode = InsertionMode.InTableBody;
                        return ProcessResult.Reprocess;

                    case TagId.Table:
                        ParseError("unexpected-start-tag-implies-end-tag");
                        if (!HasInScope(TagId.Table, Scope.Table))
                            return ProcessResult.Done;

                        PopUntil(TagId.Table);
                        ResetInsertionModeAppropriately();
                        return ProcessResult.Reprocess;

                    case TagId.Style or TagId.Script or TagId.Template:
                        return ProcessInHead(ref token);

                    case TagId.Input:
                    {
                        var type = token.Tag.GetAttribute("type");
                        if (type is null || !InfraAscii.EqualsIgnoreAsciiCase(type, "hidden"))
                            break;

                        ParseError("unexpected-hidden-input-in-table");
                        InsertHtmlElement(token.Tag);
                        PopCurrentNode();
                        token.Tag.SelfClosingAcknowledged = true;
                        return ProcessResult.Done;
                    }

                    case TagId.Form:
                        ParseError("unexpected-form-in-table");
                        if (_formElement is not null && !IsParsingTemplateContents)
                            return ProcessResult.Done;

                        var form = InsertHtmlElement(token.Tag);
                        if (!IsParsingTemplateContents)
                        {
                            _formElement = form;
                        }

                        PopCurrentNode();
                        return ProcessResult.Done;
                }

                break;

            case TokenKind.EndTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Table:
                        if (!HasInScope(TagId.Table, Scope.Table))
                        {
                            ParseError("unexpected-end-tag");
                            return ProcessResult.Done;
                        }

                        PopUntil(TagId.Table);
                        ResetInsertionModeAppropriately();
                        return ProcessResult.Done;

                    case TagId.Body or TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Html or TagId.Tbody or TagId.Td or TagId.Tfoot or TagId.Th or TagId.Thead or TagId.Tr:
                        ParseError("unexpected-end-tag");
                        return ProcessResult.Done;

                    case TagId.Template:
                        return ProcessInHead(ref token);
                }

                break;

            case TokenKind.EndOfFile:
                return ProcessInBody(ref token);
        }

        // Anything else
        ParseError("unexpected-token-in-table");
        _fosterParenting = true;
        var inBodyResult = ProcessInBody(ref token);
        _fosterParenting = false;
        return inBodyResult;
    }

    private void ClearStackBackToTableContext()
    {
        while (!(CurrentNode.IsHtmlNamespace && CurrentNode.TagId is TagId.Table or TagId.Template or TagId.Html))
        {
            PopCurrentNode();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.10 The "in table text" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInTableText(ref Token token)
    {
        if (token.Kind == TokenKind.Characters)
        {
            foreach (var c in token.Characters)
            {
                if (c == '\0')
                {
                    ParseError("unexpected-null-character");
                    continue;
                }

                if (!IsWhitespace(c))
                {
                    _pendingTableCharactersContainNonWhitespace = true;
                }

                _pendingTableCharacters.Append(c);
            }

            return ProcessResult.Done;
        }

        if (_pendingTableCharacters.Length > 0)
        {
            var pending = _pendingTableCharacters.ToString();
            _pendingTableCharacters.Clear();
            if (_pendingTableCharactersContainNonWhitespace)
            {
                ParseError("unexpected-character-in-table");
                var charactersToken = new Token { Kind = TokenKind.Characters, Characters = pending };
                _fosterParenting = true;
                while (!charactersToken.Characters.IsEmpty)
                {
                    ProcessInBody(ref charactersToken);
                }

                _fosterParenting = false;
            }
            else
            {
                InsertCharacters(pending);
            }
        }

        _mode = _originalMode;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.11 The "in caption" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInCaption(ref Token token)
    {
        if (token.IsEndTag(TagId.Caption) ||
            (token.Kind == TokenKind.StartTag && token.Tag.TagId is TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Tbody or TagId.Td or TagId.Tfoot or TagId.Th or TagId.Thead or TagId.Tr) ||
            token.IsEndTag(TagId.Table))
        {
            if (!HasInScope(TagId.Caption, Scope.Table))
            {
                ParseError("unexpected-tag");
                return ProcessResult.Done;
            }

            GenerateImpliedEndTags();
            if (!CurrentNode.IsHtml(TagId.Caption))
            {
                ParseError("end-tag-too-early");
            }

            PopUntil(TagId.Caption);
            ClearActiveFormattingElementsUpToLastMarker();
            _mode = InsertionMode.InTable;
            return token.IsEndTag(TagId.Caption) ? ProcessResult.Done : ProcessResult.Reprocess;
        }

        if (token.Kind == TokenKind.EndTag && token.Tag.TagId is TagId.Body or TagId.Col or TagId.Colgroup or TagId.Html or TagId.Tbody or TagId.Td or TagId.Tfoot or TagId.Th or TagId.Thead or TagId.Tr)
        {
            ParseError("unexpected-end-tag");
            return ProcessResult.Done;
        }

        return ProcessInBody(ref token);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.12 The "in column group" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInColumnGroup(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                InsertCharacters(token.Characters[..whitespace]);
                token.Characters = token.Characters[whitespace..];
                if (token.Characters.IsEmpty)
                    return ProcessResult.Done;

                break;
            }

            case TokenKind.Comment:
                InsertComment(token.Data);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
                return ProcessInBody(ref token);

            case TokenKind.StartTag when token.Tag.TagId == TagId.Col:
                InsertHtmlElement(token.Tag);
                PopCurrentNode();
                token.Tag.SelfClosingAcknowledged = true;
                return ProcessResult.Done;

            case TokenKind.EndTag when token.Tag.TagId == TagId.Colgroup:
                if (!CurrentNode.IsHtml(TagId.Colgroup))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                PopCurrentNode();
                _mode = InsertionMode.InTable;
                return ProcessResult.Done;

            case TokenKind.EndTag when token.Tag.TagId == TagId.Col:
                ParseError("unexpected-end-tag");
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Template:
            case TokenKind.EndTag when token.Tag.TagId == TagId.Template:
                return ProcessInHead(ref token);

            case TokenKind.EndOfFile:
                return ProcessInBody(ref token);
        }

        if (!CurrentNode.IsHtml(TagId.Colgroup))
        {
            ParseError("unexpected-token-in-column-group");
            if (token.Kind == TokenKind.Characters)
            {
                // Ignore the characters token, one character at a time
                token.Characters = token.Characters[1..];
                return token.Characters.IsEmpty ? ProcessResult.Done : ProcessResult.Reprocess;
            }

            return ProcessResult.Done;
        }

        PopCurrentNode();
        _mode = InsertionMode.InTable;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.13 The "in table body" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInTableBody(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.StartTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Tr:
                        ClearStackBackToTableBodyContext();
                        InsertHtmlElement(token.Tag);
                        _mode = InsertionMode.InRow;
                        return ProcessResult.Done;

                    case TagId.Th or TagId.Td:
                        ParseError("unexpected-cell-in-table-body");
                        ClearStackBackToTableBodyContext();
                        InsertHtmlElement(TagId.Tr);
                        _mode = InsertionMode.InRow;
                        return ProcessResult.Reprocess;

                    case TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Tbody or TagId.Tfoot or TagId.Thead:
                        if (!HasAnyInScope([TagId.Tbody, TagId.Thead, TagId.Tfoot], Scope.Table))
                        {
                            ParseError("unexpected-start-tag");
                            return ProcessResult.Done;
                        }

                        ClearStackBackToTableBodyContext();
                        PopCurrentNode();
                        _mode = InsertionMode.InTable;
                        return ProcessResult.Reprocess;
                }

                break;

            case TokenKind.EndTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Tbody or TagId.Tfoot or TagId.Thead:
                        if (!HasInScope(token.Tag.TagId, Scope.Table))
                        {
                            ParseError("unexpected-end-tag");
                            return ProcessResult.Done;
                        }

                        ClearStackBackToTableBodyContext();
                        PopCurrentNode();
                        _mode = InsertionMode.InTable;
                        return ProcessResult.Done;

                    case TagId.Table:
                        if (!HasAnyInScope([TagId.Tbody, TagId.Thead, TagId.Tfoot], Scope.Table))
                        {
                            ParseError("unexpected-end-tag");
                            return ProcessResult.Done;
                        }

                        ClearStackBackToTableBodyContext();
                        PopCurrentNode();
                        _mode = InsertionMode.InTable;
                        return ProcessResult.Reprocess;

                    case TagId.Body or TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Html or TagId.Td or TagId.Th or TagId.Tr:
                        ParseError("unexpected-end-tag");
                        return ProcessResult.Done;
                }

                break;
        }

        return ProcessInTable(ref token);
    }

    private void ClearStackBackToTableBodyContext()
    {
        while (!(CurrentNode.IsHtmlNamespace && CurrentNode.TagId is TagId.Tbody or TagId.Tfoot or TagId.Thead or TagId.Template or TagId.Html))
        {
            PopCurrentNode();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.14 The "in row" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInRow(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.StartTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Th or TagId.Td:
                        ClearStackBackToTableRowContext();
                        InsertHtmlElement(token.Tag);
                        _mode = InsertionMode.InCell;
                        InsertMarker();
                        return ProcessResult.Done;

                    case TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Tbody or TagId.Tfoot or TagId.Thead or TagId.Tr:
                        if (!HasInScope(TagId.Tr, Scope.Table))
                        {
                            ParseError("unexpected-start-tag");
                            return ProcessResult.Done;
                        }

                        ClearStackBackToTableRowContext();
                        PopCurrentNode();
                        _mode = InsertionMode.InTableBody;
                        return ProcessResult.Reprocess;
                }

                break;

            case TokenKind.EndTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Tr:
                        if (!HasInScope(TagId.Tr, Scope.Table))
                        {
                            ParseError("unexpected-end-tag");
                            return ProcessResult.Done;
                        }

                        ClearStackBackToTableRowContext();
                        PopCurrentNode();
                        _mode = InsertionMode.InTableBody;
                        return ProcessResult.Done;

                    case TagId.Table:
                        if (!HasInScope(TagId.Tr, Scope.Table))
                        {
                            ParseError("unexpected-end-tag");
                            return ProcessResult.Done;
                        }

                        ClearStackBackToTableRowContext();
                        PopCurrentNode();
                        _mode = InsertionMode.InTableBody;
                        return ProcessResult.Reprocess;

                    case TagId.Tbody or TagId.Tfoot or TagId.Thead:
                        if (!HasInScope(token.Tag.TagId, Scope.Table))
                        {
                            ParseError("unexpected-end-tag");
                            return ProcessResult.Done;
                        }

                        if (!HasInScope(TagId.Tr, Scope.Table))
                            return ProcessResult.Done;

                        ClearStackBackToTableRowContext();
                        PopCurrentNode();
                        _mode = InsertionMode.InTableBody;
                        return ProcessResult.Reprocess;

                    case TagId.Body or TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Html or TagId.Td or TagId.Th:
                        ParseError("unexpected-end-tag");
                        return ProcessResult.Done;
                }

                break;
        }

        return ProcessInTable(ref token);
    }

    private void ClearStackBackToTableRowContext()
    {
        while (!(CurrentNode.IsHtmlNamespace && CurrentNode.TagId is TagId.Tr or TagId.Template or TagId.Html))
        {
            PopCurrentNode();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.15 The "in cell" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInCell(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.EndTag when token.Tag.TagId is TagId.Td or TagId.Th:
                if (!HasInScope(token.Tag.TagId, Scope.Table))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                GenerateImpliedEndTags();
                if (!CurrentNode.IsHtml(token.Tag.TagId))
                {
                    ParseError("end-tag-too-early");
                }

                PopUntil(token.Tag.TagId);
                ClearActiveFormattingElementsUpToLastMarker();
                _mode = InsertionMode.InRow;
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId is TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Tbody or TagId.Td or TagId.Tfoot or TagId.Th or TagId.Thead or TagId.Tr:
                CloseTheCell();
                return ProcessResult.Reprocess;

            case TokenKind.EndTag when token.Tag.TagId is TagId.Body or TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Html:
                ParseError("unexpected-end-tag");
                return ProcessResult.Done;

            case TokenKind.EndTag when token.Tag.TagId is TagId.Table or TagId.Tbody or TagId.Tfoot or TagId.Thead or TagId.Tr:
                if (!HasInScope(token.Tag.TagId, Scope.Table))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                CloseTheCell();
                return ProcessResult.Reprocess;
        }

        return ProcessInBody(ref token);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#close-the-cell</summary>
    private void CloseTheCell()
    {
        GenerateImpliedEndTags();
        if (!(CurrentNode.IsHtml(TagId.Td) || CurrentNode.IsHtml(TagId.Th)))
        {
            ParseError("end-tag-too-early");
        }

        PopUntilOneOf(TagId.Td, TagId.Th);
        ClearActiveFormattingElementsUpToLastMarker();
        _mode = InsertionMode.InRow;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.16 The "in template" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInTemplate(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            case TokenKind.Comment:
            case TokenKind.ProcessingInstruction:
            case TokenKind.Doctype:
                return ProcessInBody(ref token);

            case TokenKind.StartTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Base or TagId.Basefont or TagId.Bgsound or TagId.Link or TagId.Meta or TagId.Noframes or TagId.Script or TagId.Style or TagId.Template or TagId.Title:
                        return ProcessInHead(ref token);

                    case TagId.Caption or TagId.Colgroup or TagId.Tbody or TagId.Tfoot or TagId.Thead:
                        return SwitchTemplateMode(InsertionMode.InTable);

                    case TagId.Col:
                        return SwitchTemplateMode(InsertionMode.InColumnGroup);

                    case TagId.Tr:
                        return SwitchTemplateMode(InsertionMode.InTableBody);

                    case TagId.Td or TagId.Th:
                        return SwitchTemplateMode(InsertionMode.InRow);

                    default:
                        return SwitchTemplateMode(InsertionMode.InBody);
                }

            case TokenKind.EndTag:
                if (token.Tag.TagId == TagId.Template)
                    return ProcessInHead(ref token);

                ParseError("unexpected-end-tag");
                return ProcessResult.Done;

            case TokenKind.EndOfFile:
                if (!HasTemplateInOpenElements())
                {
                    StopParsing();
                    return ProcessResult.Done;
                }

                ParseError("eof-in-template");
                PopUntil(TagId.Template);
                ClearActiveFormattingElementsUpToLastMarker();
                _templateModes.RemoveAt(_templateModes.Count - 1);
                ResetInsertionModeAppropriately();
                return ProcessResult.Reprocess;
        }

        return ProcessResult.Done;
    }

    private ProcessResult SwitchTemplateMode(InsertionMode mode)
    {
        _templateModes[^1] = mode;
        _mode = mode;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.17 The "after body" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessAfterBody(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                if (whitespace > 0)
                {
                    var whitespaceToken = token;
                    whitespaceToken.Characters = token.Characters[..whitespace];
                    ProcessInBody(ref whitespaceToken);
                    token.Characters = token.Characters[whitespace..];
                    if (token.Characters.IsEmpty)
                        return ProcessResult.Done;
                }

                break;
            }

            case TokenKind.Comment:
                InsertComment(token.Data, _openElements[0]);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data, _openElements[0]);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
                return ProcessInBody(ref token);

            case TokenKind.EndTag when token.Tag.TagId == TagId.Html:
                if (ContextElement is not null)
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                _mode = InsertionMode.AfterAfterBody;
                return ProcessResult.Done;

            case TokenKind.EndOfFile:
                StopParsing();
                return ProcessResult.Done;
        }

        ParseError("unexpected-token-after-body");
        _mode = InsertionMode.InBody;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.18 The "in frameset" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInFrameset(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
                InsertWhitespaceIgnoringOthers(ref token);
                return ProcessResult.Done;

            case TokenKind.Comment:
                InsertComment(token.Data);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag:
                switch (token.Tag.TagId)
                {
                    case TagId.Html:
                        return ProcessInBody(ref token);

                    case TagId.Frameset:
                        InsertHtmlElement(token.Tag);
                        return ProcessResult.Done;

                    case TagId.Frame:
                        InsertHtmlElement(token.Tag);
                        PopCurrentNode();
                        token.Tag.SelfClosingAcknowledged = true;
                        return ProcessResult.Done;

                    case TagId.Noframes:
                        return ProcessInHead(ref token);
                }

                break;

            case TokenKind.EndTag when token.Tag.TagId == TagId.Frameset:
                if (ReferenceEquals(CurrentNode, _openElements[0]))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                PopCurrentNode();
                if (ContextElement is null && !CurrentNode.IsHtml(TagId.Frameset))
                {
                    _mode = InsertionMode.AfterFrameset;
                }

                return ProcessResult.Done;

            case TokenKind.EndOfFile:
                if (!ReferenceEquals(CurrentNode, _openElements[0]))
                {
                    ParseError("eof-in-frameset");
                }

                StopParsing();
                return ProcessResult.Done;
        }

        ParseError("unexpected-token-in-frameset");
        return ProcessResult.Done;
    }

    /// <summary>Inserts the whitespace characters of the token and ignores the others (with a parse error).</summary>
    private void InsertWhitespaceIgnoringOthers(ref Token token)
    {
        var characters = token.Characters;
        var start = 0;
        for (var i = 0; i < characters.Length; i++)
        {
            if (!IsWhitespace(characters[i]))
            {
                InsertCharacters(characters[start..i]);
                ParseError("unexpected-character");
                start = i + 1;
            }
        }

        InsertCharacters(characters[start..]);
        token.Characters = default;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.19 The "after frameset" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessAfterFrameset(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
                InsertWhitespaceIgnoringOthers(ref token);
                return ProcessResult.Done;

            case TokenKind.Comment:
                InsertComment(token.Data);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data);
                return ProcessResult.Done;

            case TokenKind.Doctype:
                ParseError("unexpected-doctype");
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
                return ProcessInBody(ref token);

            case TokenKind.EndTag when token.Tag.TagId == TagId.Html:
                _mode = InsertionMode.AfterAfterFrameset;
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Noframes:
                return ProcessInHead(ref token);

            case TokenKind.EndOfFile:
                StopParsing();
                return ProcessResult.Done;
        }

        ParseError("unexpected-token-after-frameset");
        return ProcessResult.Done;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.20 The "after after body" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessAfterAfterBody(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Comment:
                InsertComment(token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.Doctype:
            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
                return ProcessInBody(ref token);

            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                if (whitespace > 0)
                {
                    var whitespaceToken = token;
                    whitespaceToken.Characters = token.Characters[..whitespace];
                    ProcessInBody(ref whitespaceToken);
                    token.Characters = token.Characters[whitespace..];
                    if (token.Characters.IsEmpty)
                        return ProcessResult.Done;
                }

                break;
            }

            case TokenKind.EndOfFile:
                StopParsing();
                return ProcessResult.Done;
        }

        ParseError("unexpected-token-after-after-body");
        _mode = InsertionMode.InBody;
        return ProcessResult.Reprocess;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.21 The "after after frameset" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessAfterAfterFrameset(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Comment:
                InsertComment(token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.ProcessingInstruction:
                InsertProcessingInstruction(token.PiTarget!, token.Data, _document);
                return ProcessResult.Done;

            case TokenKind.Doctype:
            case TokenKind.StartTag when token.Tag.TagId == TagId.Html:
                return ProcessInBody(ref token);

            case TokenKind.Characters:
            {
                var whitespace = CountLeadingWhitespace(token.Characters);
                if (whitespace > 0)
                {
                    var whitespaceToken = token;
                    whitespaceToken.Characters = token.Characters[..whitespace];
                    ProcessInBody(ref whitespaceToken);
                }

                // Non-whitespace characters are ignored (parse error)
                if (whitespace < token.Characters.Length)
                {
                    ParseError("unexpected-character");
                    token.Characters = token.Characters[(whitespace + 1)..];
                    return token.Characters.IsEmpty ? ProcessResult.Done : ProcessResult.Reprocess;
                }

                return ProcessResult.Done;
            }

            case TokenKind.EndOfFile:
                StopParsing();
                return ProcessResult.Done;

            case TokenKind.StartTag when token.Tag.TagId == TagId.Noframes:
                return ProcessInHead(ref token);
        }

        ParseError("unexpected-token-after-after-frameset");
        return ProcessResult.Done;
    }
}
