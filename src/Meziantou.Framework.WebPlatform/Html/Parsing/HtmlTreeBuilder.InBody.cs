using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

internal sealed partial class HtmlTreeBuilder
{
    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.4.7 The "in body" insertion mode
    // ----------------------------------------------------------------------------------------------------------------

    private ProcessResult ProcessInBody(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
                ProcessCharactersInBody(ref token);
                return token.Characters.IsEmpty ? ProcessResult.Done : ProcessResult.Reprocess;

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
                return ProcessStartTagInBody(ref token);

            case TokenKind.EndTag:
                return ProcessEndTagInBody(ref token);

            case TokenKind.EndOfFile:
                if (_templateModes.Count > 0)
                    return ProcessInTemplate(ref token);

                if (HasUnexpectedOpenElement())
                {
                    ParseError("eof-in-body");
                }

                StopParsing();
                return ProcessResult.Done;
        }

        return ProcessResult.Done;
    }

    /// <summary>Processes a prefix of the character token: either a NULL character or a run without NULL characters.</summary>
    private void ProcessCharactersInBody(ref Token token)
    {
        var characters = token.Characters;
        if (characters[0] == '\0')
        {
            ParseError("unexpected-null-character");
            token.Characters = characters[1..];
            return;
        }

        var end = characters.IndexOf('\0');
        if (end < 0)
        {
            end = characters.Length;
        }

        var run = characters[..end];
        ReconstructActiveFormattingElements();
        InsertCharacters(run);
        if (ContainsNonWhitespace(run))
        {
            _framesetOk = false;
        }

        token.Characters = characters[end..];
    }

    private bool HasUnexpectedOpenElement()
    {
        foreach (var node in _openElements)
        {
            if (!(node.IsHtmlNamespace && node.TagId is TagId.Dd or TagId.Dt or TagId.Li or TagId.Optgroup or TagId.Option or TagId.P or TagId.Rb or TagId.Rp or
                TagId.Rt or TagId.Rtc or TagId.Tbody or TagId.Td or TagId.Tfoot or TagId.Th or TagId.Thead or TagId.Tr or TagId.Body or TagId.Html))
                return true;
        }

        return false;
    }

    private ProcessResult ProcessStartTagInBody(ref Token token)
    {
        var tag = token.Tag;
        switch (tag.TagId)
        {
            case TagId.Html:
            {
                ParseError("unexpected-start-tag");
                if (HasTemplateInOpenElements())
                    return ProcessResult.Done;

                AddMissingAttributes(_openElements[0], tag);
                return ProcessResult.Done;
            }

            case TagId.Base or TagId.Basefont or TagId.Bgsound or TagId.Link or TagId.Meta or TagId.Noframes or TagId.Script or TagId.Style or TagId.Template or TagId.Title:
                return ProcessInHead(ref token);

            case TagId.Body:
            {
                ParseError("unexpected-start-tag");
                if (_openElements.Count == 1 || !_openElements[1].IsHtml(TagId.Body) || HasTemplateInOpenElements())
                    return ProcessResult.Done;

                _framesetOk = false;
                AddMissingAttributes(_openElements[1], tag);
                return ProcessResult.Done;
            }

            case TagId.Frameset:
            {
                ParseError("unexpected-start-tag");
                if (_openElements.Count == 1 || !_openElements[1].IsHtml(TagId.Body))
                    return ProcessResult.Done;

                if (!_framesetOk)
                    return ProcessResult.Done;

                _openElements[1].RemoveFromParent();
                while (_openElements.Count > 1)
                {
                    PopCurrentNode();
                }

                InsertHtmlElement(tag);
                _mode = InsertionMode.InFrameset;
                return ProcessResult.Done;
            }

            case TagId.Address or TagId.Article or TagId.Aside or TagId.Blockquote or TagId.Center or TagId.Details or TagId.Dialog or TagId.Dir or
                TagId.Div or TagId.Dl or TagId.Fieldset or TagId.Figcaption or TagId.Figure or TagId.Footer or TagId.Header or TagId.Hgroup or
                TagId.Main or TagId.Menu or TagId.Nav or TagId.Ol or TagId.P or TagId.Search or TagId.Section or TagId.Summary or TagId.Ul:
                CloseParagraphIfInButtonScope();
                InsertHtmlElement(tag);
                return ProcessResult.Done;

            case TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6:
                CloseParagraphIfInButtonScope();
                if (CurrentNode.IsHtmlNamespace && CurrentNode.TagId is TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6)
                {
                    ParseError("unexpected-start-tag");
                    PopCurrentNode();
                }

                InsertHtmlElement(tag);
                return ProcessResult.Done;

            case TagId.Pre or TagId.Listing:
                CloseParagraphIfInButtonScope();
                InsertHtmlElement(tag);
                _ignoreNextLineFeed = true;
                _framesetOk = false;
                return ProcessResult.Done;

            case TagId.Form:
            {
                var inTemplate = IsParsingTemplateContents;
                if (_formElement is not null && !inTemplate)
                {
                    ParseError("unexpected-start-tag");
                    return ProcessResult.Done;
                }

                CloseParagraphIfInButtonScope();
                var form = InsertHtmlElement(tag);
                if (!inTemplate)
                {
                    _formElement = form;
                }

                return ProcessResult.Done;
            }

            case TagId.Li:
            {
                _framesetOk = false;
                for (var i = _openElements.Count - 1; i >= 0; i--)
                {
                    var node = _openElements[i];
                    if (node.IsHtml(TagId.Li))
                    {
                        GenerateImpliedEndTags(except: TagId.Li);
                        if (!CurrentNode.IsHtml(TagId.Li))
                        {
                            ParseError("unexpected-start-tag");
                        }

                        PopUntil(TagId.Li);
                        break;
                    }

                    if (IsSpecial(node) && !(node.IsHtmlNamespace && node.TagId is TagId.Address or TagId.Div or TagId.P))
                        break;
                }

                CloseParagraphIfInButtonScope();
                InsertHtmlElement(tag);
                return ProcessResult.Done;
            }

            case TagId.Dd or TagId.Dt:
            {
                _framesetOk = false;
                for (var i = _openElements.Count - 1; i >= 0; i--)
                {
                    var node = _openElements[i];
                    if (node.IsHtml(TagId.Dd) || node.IsHtml(TagId.Dt))
                    {
                        var nodeTag = node.TagId;
                        GenerateImpliedEndTags(except: nodeTag);
                        if (!CurrentNode.IsHtml(nodeTag))
                        {
                            ParseError("unexpected-start-tag");
                        }

                        PopUntil(nodeTag);
                        break;
                    }

                    if (IsSpecial(node) && !(node.IsHtmlNamespace && node.TagId is TagId.Address or TagId.Div or TagId.P))
                        break;
                }

                CloseParagraphIfInButtonScope();
                InsertHtmlElement(tag);
                return ProcessResult.Done;
            }

            case TagId.Plaintext:
                CloseParagraphIfInButtonScope();
                InsertHtmlElement(tag);
                _tokenizer.State = TokenizerState.Plaintext;
                return ProcessResult.Done;

            case TagId.Button:
                if (HasInScope(TagId.Button))
                {
                    ParseError("unexpected-start-tag");
                    GenerateImpliedEndTags();
                    PopUntil(TagId.Button);
                }

                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                _framesetOk = false;
                return ProcessResult.Done;

            case TagId.A:
            {
                for (var i = _activeFormatting.Count - 1; i >= 0; i--)
                {
                    var entry = _activeFormatting[i];
                    if (entry.IsMarker)
                        break;

                    if (entry.Element!.IsHtml(TagId.A))
                    {
                        ParseError("unexpected-start-tag-implies-end-tag");
                        var element = entry.Element;
                        RunAdoptionAgencyAlgorithm(tag);
                        var index = IndexOfActiveFormattingElement(element);
                        if (index >= 0)
                        {
                            _activeFormatting.RemoveAt(index);
                        }

                        RemoveFromOpenElements(element);
                        break;
                    }
                }

                ReconstructActiveFormattingElements();
                var a = InsertHtmlElement(tag);
                PushActiveFormattingElement(a, tag);
                return ProcessResult.Done;
            }

            case TagId.B or TagId.Big or TagId.Code or TagId.Em or TagId.Font or TagId.I or TagId.S or TagId.Small or TagId.Strike or TagId.Strong or TagId.Tt or TagId.U:
            {
                ReconstructActiveFormattingElements();
                var element = InsertHtmlElement(tag);
                PushActiveFormattingElement(element, tag);
                return ProcessResult.Done;
            }

            case TagId.Nobr:
            {
                ReconstructActiveFormattingElements();
                if (HasInScope(TagId.Nobr))
                {
                    ParseError("unexpected-start-tag-implies-end-tag");
                    RunAdoptionAgencyAlgorithm(tag);
                    ReconstructActiveFormattingElements();
                }

                var element = InsertHtmlElement(tag);
                PushActiveFormattingElement(element, tag);
                return ProcessResult.Done;
            }

            case TagId.Applet or TagId.Marquee or TagId.Object:
                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                InsertMarker();
                _framesetOk = false;
                return ProcessResult.Done;

            case TagId.Table:
                if (_document.Mode != DocumentMode.Quirks)
                {
                    CloseParagraphIfInButtonScope();
                }

                InsertHtmlElement(tag);
                _framesetOk = false;
                _mode = InsertionMode.InTable;
                return ProcessResult.Done;

            case TagId.Area or TagId.Br or TagId.Embed or TagId.Img or TagId.Keygen or TagId.Wbr:
                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                PopCurrentNode();
                tag.SelfClosingAcknowledged = true;
                _framesetOk = false;
                return ProcessResult.Done;

            case TagId.Input:
            {
                if (ContextElement is not null && ContextElement.IsHtml(TagId.Select))
                {
                    ParseError("unexpected-input-in-select");
                    return ProcessResult.Done;
                }

                if (HasInScope(TagId.Select))
                {
                    ParseError("unexpected-input-in-select");
                    PopUntil(TagId.Select);
                }

                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                PopCurrentNode();
                tag.SelfClosingAcknowledged = true;
                var type = tag.GetAttribute("type");
                if (type is null || !InfraAscii.EqualsIgnoreAsciiCase(type, "hidden"))
                {
                    _framesetOk = false;
                }

                return ProcessResult.Done;
            }

            case TagId.Param or TagId.Source or TagId.Track:
                InsertHtmlElement(tag);
                PopCurrentNode();
                tag.SelfClosingAcknowledged = true;
                return ProcessResult.Done;

            case TagId.Hr:
                CloseParagraphIfInButtonScope();
                if (HasInScope(TagId.Select))
                {
                    GenerateImpliedEndTags();
                    if (HasInScope(TagId.Option) || HasInScope(TagId.Optgroup))
                    {
                        ParseError("unexpected-hr-in-select");
                    }
                }

                InsertHtmlElement(tag);
                PopCurrentNode();
                tag.SelfClosingAcknowledged = true;
                _framesetOk = false;
                return ProcessResult.Done;

            case TagId.Image:
                ParseError("unexpected-image-start-tag");
                tag.Name = "img";
                tag.TagId = TagId.Img;
                return ProcessResult.Reprocess;

            case TagId.Textarea:
                InsertHtmlElement(tag);
                _ignoreNextLineFeed = true;
                _tokenizer.State = TokenizerState.Rcdata;
                _originalMode = _mode;
                _framesetOk = false;
                _mode = InsertionMode.Text;
                return ProcessResult.Done;

            case TagId.Xmp:
                CloseParagraphIfInButtonScope();
                ReconstructActiveFormattingElements();
                _framesetOk = false;
                ParseGenericTextElement(tag, TokenizerState.Rawtext);
                return ProcessResult.Done;

            case TagId.Iframe:
                _framesetOk = false;
                ParseGenericTextElement(tag, TokenizerState.Rawtext);
                return ProcessResult.Done;

            case TagId.Noembed:
            case TagId.Noscript when ScriptingMode != ParserScriptingMode.Disabled:
                ParseGenericTextElement(tag, TokenizerState.Rawtext);
                return ProcessResult.Done;

            case TagId.Select:
                if (ContextElement is not null && ContextElement.IsHtml(TagId.Select))
                {
                    ParseError("unexpected-select-in-select");
                    return ProcessResult.Done;
                }

                if (HasInScope(TagId.Select))
                {
                    ParseError("unexpected-select-in-select");
                    PopUntil(TagId.Select);
                    return ProcessResult.Done;
                }

                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                _framesetOk = false;
                return ProcessResult.Done;

            case TagId.Option:
                if (HasInScope(TagId.Select))
                {
                    GenerateImpliedEndTags(except: TagId.Optgroup);
                    if (HasInScope(TagId.Option))
                    {
                        ParseError("unexpected-start-tag");
                    }
                }
                else if (CurrentNode.IsHtml(TagId.Option))
                {
                    PopCurrentNode();
                }

                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                return ProcessResult.Done;

            case TagId.Optgroup:
                if (HasInScope(TagId.Select))
                {
                    GenerateImpliedEndTags();
                    if (HasInScope(TagId.Option) || HasInScope(TagId.Optgroup))
                    {
                        ParseError("unexpected-start-tag");
                    }
                }
                else if (CurrentNode.IsHtml(TagId.Option))
                {
                    PopCurrentNode();
                }

                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                return ProcessResult.Done;

            case TagId.Rb or TagId.Rtc:
                if (HasInScope(TagId.Ruby))
                {
                    GenerateImpliedEndTags();
                    if (!CurrentNode.IsHtml(TagId.Ruby))
                    {
                        ParseError("unexpected-start-tag");
                    }
                }

                InsertHtmlElement(tag);
                return ProcessResult.Done;

            case TagId.Rp or TagId.Rt:
                if (HasInScope(TagId.Ruby))
                {
                    GenerateImpliedEndTags(except: TagId.Rtc);
                    if (!(CurrentNode.IsHtml(TagId.Rtc) || CurrentNode.IsHtml(TagId.Ruby)))
                    {
                        ParseError("unexpected-start-tag");
                    }
                }

                InsertHtmlElement(tag);
                return ProcessResult.Done;

            case TagId.Math:
                ReconstructActiveFormattingElements();
                ForeignAttributes.AdjustMathMLAttributes(tag);
                ForeignAttributes.AdjustForeignAttributes(tag);
                InsertForeignElement(tag, Namespaces.MathML, onlyAddToElementStack: false);
                if (tag.SelfClosing)
                {
                    PopCurrentNode();
                    tag.SelfClosingAcknowledged = true;
                }

                return ProcessResult.Done;

            case TagId.Svg:
                ReconstructActiveFormattingElements();
                ForeignAttributes.AdjustSvgAttributes(tag);
                ForeignAttributes.AdjustForeignAttributes(tag);
                InsertForeignElement(tag, Namespaces.Svg, onlyAddToElementStack: false);
                if (tag.SelfClosing)
                {
                    PopCurrentNode();
                    tag.SelfClosingAcknowledged = true;
                }

                return ProcessResult.Done;

            case TagId.Caption or TagId.Col or TagId.Colgroup or TagId.Frame or TagId.Head or TagId.Tbody or TagId.Td or TagId.Tfoot or TagId.Th or TagId.Thead or TagId.Tr:
                ParseError("unexpected-start-tag");
                return ProcessResult.Done;

            default:
                ReconstructActiveFormattingElements();
                InsertHtmlElement(tag);
                return ProcessResult.Done;
        }
    }

    private void AddMissingAttributes(Element element, HtmlTagToken tag)
    {
        if (GetSanitizerAction(element) != SanitizerElementAction.Keep)
            return;

        tag = GetSanitizedToken(tag, element.NamespaceUri!);
        foreach (ref var attribute in tag.Attributes)
        {
            if (element.FindAttribute(attribute.Namespace, attribute.LocalName) < 0)
            {
                element.AppendAttribute(attribute.LocalName, attribute.Namespace, attribute.Prefix, attribute.Value);
                if (attribute.Namespace is null && attribute.LocalName == "nonce" && element.IsConnected)
                {
                    element.HideNonce();
                }
            }
        }
    }

    private void CloseParagraphIfInButtonScope()
    {
        if (HasInScope(TagId.P, Scope.Button))
        {
            CloseParagraph();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#close-a-p-element</summary>
    private void CloseParagraph()
    {
        GenerateImpliedEndTags(except: TagId.P);
        if (!CurrentNode.IsHtml(TagId.P))
        {
            ParseError("unexpected-end-tag");
        }

        PopUntil(TagId.P);
    }

    private ProcessResult ProcessEndTagInBody(ref Token token)
    {
        var tag = token.Tag;
        switch (tag.TagId)
        {
            case TagId.Template:
                return ProcessInHead(ref token);

            case TagId.Body:
                if (!HasInScope(TagId.Body))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                if (HasUnexpectedOpenElement())
                {
                    ParseError("end-tag-too-early");
                }

                _mode = InsertionMode.AfterBody;
                return ProcessResult.Done;

            case TagId.Html:
                if (!HasInScope(TagId.Body))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                if (HasUnexpectedOpenElement())
                {
                    ParseError("end-tag-too-early");
                }

                _mode = InsertionMode.AfterBody;
                return ProcessResult.Reprocess;

            case TagId.Address or TagId.Article or TagId.Aside or TagId.Blockquote or TagId.Button or TagId.Center or TagId.Details or TagId.Dialog or
                TagId.Dir or TagId.Div or TagId.Dl or TagId.Fieldset or TagId.Figcaption or TagId.Figure or TagId.Footer or TagId.Header or TagId.Hgroup or
                TagId.Listing or TagId.Main or TagId.Menu or TagId.Nav or TagId.Ol or TagId.Pre or TagId.Search or TagId.Section or TagId.Select or
                TagId.Summary or TagId.Ul:
                if (!HasInScope(tag.TagId))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                GenerateImpliedEndTags();
                if (!CurrentNode.IsHtml(tag.TagId))
                {
                    ParseError("end-tag-too-early");
                }

                PopUntil(tag.TagId);
                return ProcessResult.Done;

            case TagId.Form:
                if (!IsParsingTemplateContents)
                {
                    var node = _formElement;
                    _formElement = null;
                    if (node is null || !HasInScope(node))
                    {
                        ParseError("unexpected-end-tag");
                        return ProcessResult.Done;
                    }

                    GenerateImpliedEndTags();
                    if (!ReferenceEquals(CurrentNode, node))
                    {
                        ParseError("end-tag-too-early");
                    }

                    RemoveFromOpenElements(node);
                }
                else
                {
                    if (!HasInScope(TagId.Form))
                    {
                        ParseError("unexpected-end-tag");
                        return ProcessResult.Done;
                    }

                    GenerateImpliedEndTags();
                    if (!CurrentNode.IsHtml(TagId.Form))
                    {
                        ParseError("end-tag-too-early");
                    }

                    PopUntil(TagId.Form);
                }

                return ProcessResult.Done;

            case TagId.P:
                if (!HasInScope(TagId.P, Scope.Button))
                {
                    ParseError("unexpected-end-tag");
                    InsertHtmlElement(TagId.P);
                }

                CloseParagraph();
                return ProcessResult.Done;

            case TagId.Li:
                if (!HasInScope(TagId.Li, Scope.ListItem))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                GenerateImpliedEndTags(except: TagId.Li);
                if (!CurrentNode.IsHtml(TagId.Li))
                {
                    ParseError("end-tag-too-early");
                }

                PopUntil(TagId.Li);
                return ProcessResult.Done;

            case TagId.Dd or TagId.Dt:
                if (!HasInScope(tag.TagId))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                GenerateImpliedEndTags(except: tag.TagId);
                if (!CurrentNode.IsHtml(tag.TagId))
                {
                    ParseError("end-tag-too-early");
                }

                PopUntil(tag.TagId);
                return ProcessResult.Done;

            case TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6:
            {
                ReadOnlySpan<TagId> headings = [TagId.H1, TagId.H2, TagId.H3, TagId.H4, TagId.H5, TagId.H6];
                if (!HasAnyInScope(headings, Scope.Default))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                GenerateImpliedEndTags();
                if (!CurrentNode.IsHtml(tag.TagId))
                {
                    ParseError("end-tag-too-early");
                }

                while (_openElements.Count > 0)
                {
                    var element = CurrentNode;
                    PopCurrentNode();
                    if (element.IsHtmlNamespace && headings.Contains(element.TagId))
                        break;
                }

                return ProcessResult.Done;
            }

            case TagId.A or TagId.B or TagId.Big or TagId.Code or TagId.Em or TagId.Font or TagId.I or TagId.Nobr or TagId.S or TagId.Small or TagId.Strike or TagId.Strong or TagId.Tt or TagId.U:
                RunAdoptionAgencyAlgorithm(tag);
                return ProcessResult.Done;

            case TagId.Applet or TagId.Marquee or TagId.Object:
                if (!HasInScope(tag.TagId))
                {
                    ParseError("unexpected-end-tag");
                    return ProcessResult.Done;
                }

                GenerateImpliedEndTags();
                if (!CurrentNode.IsHtml(tag.TagId))
                {
                    ParseError("end-tag-too-early");
                }

                PopUntil(tag.TagId);
                ClearActiveFormattingElementsUpToLastMarker();
                return ProcessResult.Done;

            case TagId.Br:
                ParseError("unexpected-end-tag");
                ReconstructActiveFormattingElements();
                InsertHtmlElement(TagId.Br);
                PopCurrentNode();
                _framesetOk = false;
                return ProcessResult.Done;

            default:
                AnyOtherEndTagInBody(tag);
                return ProcessResult.Done;
        }
    }

    /// <summary>The "any other end tag" steps of the "in body" insertion mode.</summary>
    private void AnyOtherEndTagInBody(HtmlTagToken tag)
    {
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (node.IsHtmlNamespace && node.LocalName == tag.Name)
            {
                GenerateImpliedEndTagsExceptName(tag.Name);
                if (!ReferenceEquals(node, CurrentNode))
                {
                    ParseError("end-tag-too-early");
                }

                PopUntil(node);
                return;
            }

            if (IsSpecial(node))
            {
                ParseError("unexpected-end-tag");
                return;
            }
        }
    }

    /// <summary>Generates implied end tags, except for HTML elements with the given name.</summary>
    private void GenerateImpliedEndTagsExceptName(string name)
    {
        while (_openElements.Count > 0)
        {
            var node = CurrentNode;
            if (!node.IsHtmlNamespace || node.LocalName == name)
                return;

            if (node.TagId is not (TagId.Dd or TagId.Dt or TagId.Li or TagId.Optgroup or TagId.Option or TagId.P or TagId.Rb or TagId.Rp or TagId.Rt or TagId.Rtc))
                return;

            PopCurrentNode();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#adoption-agency-algorithm</summary>
    private void RunAdoptionAgencyAlgorithm(HtmlTagToken token)
    {
        var subject = token.Name;
        if (CurrentNode.IsHtmlNamespace && CurrentNode.LocalName == subject && IndexOfActiveFormattingElement(CurrentNode) < 0)
        {
            PopCurrentNode();
            return;
        }

        for (var outerLoopCounter = 0; outerLoopCounter < 8; outerLoopCounter++)
        {
            // Find the formatting element
            var formattingElementIndex = -1;
            for (var i = _activeFormatting.Count - 1; i >= 0; i--)
            {
                var entry = _activeFormatting[i];
                if (entry.IsMarker)
                    break;

                if (entry.Element!.LocalName == subject)
                {
                    formattingElementIndex = i;
                    break;
                }
            }

            if (formattingElementIndex < 0)
            {
                AnyOtherEndTagInBody(token);
                return;
            }

            var formattingEntry = _activeFormatting[formattingElementIndex];
            var formattingElement = formattingEntry.Element!;
            var formattingElementStackIndex = _openElements.LastIndexOf(formattingElement);
            if (formattingElementStackIndex < 0)
            {
                ParseError("adoption-agency-1.2");
                _activeFormatting.RemoveAt(formattingElementIndex);
                return;
            }

            if (!HasInScope(formattingElement))
            {
                ParseError("adoption-agency-4.4");
                return;
            }

            if (!ReferenceEquals(formattingElement, CurrentNode))
            {
                ParseError("adoption-agency-1.3");
            }

            // Furthest block: the topmost special node lower in the stack than the formatting element
            Element? furthestBlock = null;
            var furthestBlockIndex = -1;
            for (var i = formattingElementStackIndex + 1; i < _openElements.Count; i++)
            {
                if (IsSpecial(_openElements[i]))
                {
                    furthestBlock = _openElements[i];
                    furthestBlockIndex = i;
                    break;
                }
            }

            if (furthestBlock is null)
            {
                while (_openElements.Count > formattingElementStackIndex)
                {
                    PopCurrentNode();
                }

                _activeFormatting.Remove(formattingEntry);
                return;
            }

            var commonAncestor = _openElements[formattingElementStackIndex - 1];
            var bookmark = formattingElementIndex;
            Element node;

            // The elements removed or replaced by their children by the sanitizer are not inserted, as in Blink: the last node
            // is null while the nodes are removed (see HtmlTreeBuilder.Sanitizer.cs)
            Element? lastNode = GetSanitizerAction(furthestBlock) == SanitizerElementAction.Keep ? furthestBlock : null;
            var nodeIndex = furthestBlockIndex;
            var innerLoopCounter = 0;
            while (true)
            {
                innerLoopCounter++;

                // The element immediately above node in the stack (or above its former position)
                nodeIndex--;
                node = _openElements[nodeIndex];
                if (ReferenceEquals(node, formattingElement))
                    break;

                var nodeFormattingIndex = IndexOfActiveFormattingElement(node);
                if (innerLoopCounter > 3 && nodeFormattingIndex >= 0)
                {
                    _activeFormatting.RemoveAt(nodeFormattingIndex);
                    if (nodeFormattingIndex < bookmark)
                    {
                        bookmark--;
                    }

                    nodeFormattingIndex = -1;
                }

                if (nodeFormattingIndex < 0)
                {
                    RemoveFromOpenElementsAt(nodeIndex);
                    continue;
                }

                var nodeToken = _activeFormatting[nodeFormattingIndex].Token!;
                var newElement = CreateElementForToken(nodeToken, Namespaces.Html, commonAncestor);
                _activeFormatting[nodeFormattingIndex] = new FormattingEntry(newElement, nodeToken);
                // The new element has the same tag as the replaced one, so the open element counts do not change
                _openElements[nodeIndex] = newElement;
                node = newElement;

                if (ReferenceEquals(lastNode, furthestBlock))
                {
                    bookmark = nodeFormattingIndex + 1;
                }

                switch (GetSanitizerAction(node))
                {
                    case SanitizerElementAction.Keep:
                        if (lastNode is not null)
                        {
                            node.AppendChildCore(DetachIfNeeded(lastNode));
                        }

                        lastNode = node;
                        break;

                    case SanitizerElementAction.Remove:
                        lastNode?.RemoveFromParent();
                        lastNode = null;
                        break;
                }
            }

            if (lastNode is not null)
            {
                var (target, refNode) = GetAdjustedInsertionLocation(commonAncestor);
                lastNode.RemoveFromParent();
                if (lastNode.ParentNode is null &&
                    !lastNode.IsHostIncludingInclusiveAncestorOf(target) &&
                    !(target is Document targetDocument && targetDocument.DocumentElement is not null) &&
                    (refNode is null || ReferenceEquals(refNode.ParentNode, target)))
                {
                    target.InsertBeforeCore(lastNode, refNode);
                }
            }

            var newFormattingElement = CreateElementForToken(formattingEntry.Token!, Namespaces.Html, furthestBlock);

            // Replace the formatting element in the list at the bookmark position
            var oldIndex = _activeFormatting.IndexOf(formattingEntry);
            if (oldIndex < bookmark)
            {
                bookmark--;
            }

            _activeFormatting.RemoveAt(oldIndex);
            _activeFormatting.Insert(Math.Min(bookmark, _activeFormatting.Count), new FormattingEntry(newFormattingElement, formattingEntry.Token));

            RemoveFromOpenElements(formattingElement);
            var furthestBlockStackIndex = _openElements.IndexOf(furthestBlock);
            _openElements.Insert(furthestBlockStackIndex + 1, newFormattingElement);
            OnElementPushed(newFormattingElement);

            // The children of furthestBlock stay in it when the new element is replaced by its children, and are removed with
            // it when it is removed. The stack of open elements is updated first, so the element is appended to the nearest kept
            // ancestor of furthestBlock when furthestBlock is replaced by its children.
            var newFormattingElementAction = GetSanitizerAction(newFormattingElement);
            if (newFormattingElementAction == SanitizerElementAction.ReplaceWithChildren)
                continue;

            // A custom element reaction may have inserted the new element in the tree (e.g. in furthestBlock): the nodes are
            // not appended to one of their descendants, which would create a cycle
            for (var child = furthestBlock.FirstChild; child is not null;)
            {
                var next = child.NextSibling;
                if (!child.IsHostIncludingInclusiveAncestorOf(newFormattingElement))
                {
                    furthestBlock.RemoveChildCore(child, suppressObservers: false);
                    newFormattingElement.AppendChildCore(child);
                }

                child = next is not null && ReferenceEquals(next.ParentNode, furthestBlock) ? next : null;
            }

            if (newFormattingElementAction == SanitizerElementAction.Remove)
                continue;

            var newParent = SkipElementsReplacedWithChildren(furthestBlock);
            Node? newReferenceChild = null;
            if (RootInsertionTarget is not null && ReferenceEquals(newParent, _openElements[0]))
            {
                newParent = RootInsertionTarget;
                newReferenceChild = RootInsertionReferenceChild;
            }

            if (!newFormattingElement.IsHostIncludingInclusiveAncestorOf(newParent) && !IsDisplaced(newParent, newReferenceChild))
            {
                newFormattingElement.RemoveFromParent();
                newParent.InsertBeforeCore(newFormattingElement, newReferenceChild);
            }
        }
    }

    private static Node DetachIfNeeded(Node node)
    {
        node.RemoveFromParent();
        return node;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 13.2.6.5 The rules for parsing tokens in foreign content
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign (an SVG script end tag): the element is
    /// popped, and the SVG script element is processed (it runs when the browsing context has a script engine). As for the
    /// HTML script elements, the scripts parsed by the fragment parsing algorithm never run. Unlike the specification, the
    /// parser pause flag is not set while the script is processed, as in the browsers: the characters written by
    /// <c>document.write()</c> are parsed (and their scripts run) during the write, as for an HTML script element.
    /// </summary>
    private void ProcessSvgScriptEndTag()
    {
        var script = CurrentNode;
        PopCurrentNode();

        // https://w3c.github.io/trusted-types/dist/spec/#setting-slot-values-from-parser
        if (script is SvgScriptElement parsedScript)
        {
            parsedScript.ScriptText = parsedScript.GetChildTextContent();
        }

        if (ContextElement is null && script is SvgScriptElement svgScript)
        {
            PerformMicrotaskCheckpointBeforeScript();
            var oldInsertionPoint = InsertionPoint;
            InsertionPoint = _tokenizer.PositionFromEnd;
            ScriptNestingLevel++;

            // An external script written by a running script waits until that script returns, and pauses the parsing of the
            // characters written after it
            var pendingScripts = ScriptNestingLevel > 1 ? PendingSvgScripts ??= [] : null;
            var pendingCount = pendingScripts?.Count ?? 0;
            svgScript.Process(pendingScripts);
            ScriptNestingLevel--;
            if (ScriptNestingLevel == 0)
            {
                ParserPauseFlag = false;
            }
            else if (pendingScripts is not null && pendingScripts.Count > pendingCount)
            {
                ParserPauseFlag = true;
            }

            InsertionPoint = oldInsertionPoint;
            RunPendingSvgScripts();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/parsing.html#scriptEndTag (step 1): a microtask checkpoint is performed before
    /// the script is prepared, when no script is running (only when the browsing context of the document has a script engine).
    /// </summary>
    private void PerformMicrotaskCheckpointBeforeScript()
    {
        if (_document.BrowsingContext is { Options.ScriptEngine: not null } browsingContext && !browsingContext.EventLoop.IsScriptRunning)
        {
            browsingContext.EventLoop.PerformMicrotaskCheckpoint();
        }
    }

    private ProcessResult ProcessInForeignContent(ref Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Characters:
            {
                var characters = token.Characters;
                if (characters[0] == '\0')
                {
                    ParseError("unexpected-null-character");
                    InsertCharacters("\U0000FFFD");
                    token.Characters = characters[1..];
                    return token.Characters.IsEmpty ? ProcessResult.Done : ProcessResult.Reprocess;
                }

                var end = characters.IndexOf('\0');
                if (end < 0)
                {
                    end = characters.Length;
                }

                var run = characters[..end];
                InsertCharacters(run);
                if (ContainsNonWhitespace(run))
                {
                    _framesetOk = false;
                }

                token.Characters = characters[end..];
                return token.Characters.IsEmpty ? ProcessResult.Done : ProcessResult.Reprocess;
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
            {
                var tag = token.Tag;
                var breaksOut = tag.TagId is TagId.B or TagId.Big or TagId.Blockquote or TagId.Body or TagId.Br or TagId.Center or TagId.Code or TagId.Dd or
                    TagId.Div or TagId.Dl or TagId.Dt or TagId.Em or TagId.Embed or TagId.H1 or TagId.H2 or TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6 or
                    TagId.Head or TagId.Hr or TagId.I or TagId.Img or TagId.Li or TagId.Listing or TagId.Menu or TagId.Meta or TagId.Nobr or TagId.Ol or
                    TagId.P or TagId.Pre or TagId.Ruby or TagId.S or TagId.Small or TagId.Span or TagId.Strong or TagId.Strike or TagId.Sub or TagId.Sup or
                    TagId.Table or TagId.Tt or TagId.U or TagId.Ul or TagId.Var
                    || (tag.TagId == TagId.Font && (tag.HasAttribute("color") || tag.HasAttribute("face") || tag.HasAttribute("size")));
                if (breaksOut)
                {
                    ParseError("unexpected-html-element-in-foreign-content");
                    PopUntilHtmlOrIntegrationPoint();
                    return ProcessUsingRulesFor(_mode, ref token);
                }

                var adjustedCurrentNode = AdjustedCurrentNode!;
                var @namespace = adjustedCurrentNode.NamespaceUri!;
                if (ReferenceEquals(@namespace, Namespaces.MathML))
                {
                    ForeignAttributes.AdjustMathMLAttributes(tag);
                }

                if (ReferenceEquals(@namespace, Namespaces.Svg))
                {
                    ForeignAttributes.AdjustSvgTagName(tag);
                    ForeignAttributes.AdjustSvgAttributes(tag);
                }

                ForeignAttributes.AdjustForeignAttributes(tag);
                InsertForeignElement(tag, @namespace, onlyAddToElementStack: false);
                if (tag.SelfClosing)
                {
                    tag.SelfClosingAcknowledged = true;
                    if (CurrentNode is SvgScriptElement)
                    {
                        ProcessSvgScriptEndTag();
                    }
                    else
                    {
                        PopCurrentNode();
                    }
                }

                return ProcessResult.Done;
            }

            case TokenKind.EndTag:
            {
                var tag = token.Tag;
                if (tag.TagId is TagId.Br or TagId.P)
                {
                    ParseError("unexpected-html-element-in-foreign-content");
                    PopUntilHtmlOrIntegrationPoint();
                    return ProcessUsingRulesFor(_mode, ref token);
                }

                if (tag.Name == "script" && CurrentNode.LocalName == "script" && ReferenceEquals(CurrentNode.NamespaceUri, Namespaces.Svg))
                {
                    ProcessSvgScriptEndTag();
                    return ProcessResult.Done;
                }

                var index = _openElements.Count - 1;
                var node = _openElements[index];
                if (InfraAscii.ToAsciiLowercase(node.LocalName) != tag.Name)
                {
                    ParseError("unexpected-end-tag");
                }

                while (true)
                {
                    if (index == 0)
                        return ProcessResult.Done;

                    if (InfraAscii.ToAsciiLowercase(node.LocalName) == tag.Name)
                    {
                        PopUntil(node);
                        return ProcessResult.Done;
                    }

                    index--;
                    node = _openElements[index];
                    if (node.IsHtmlNamespace)
                        return ProcessUsingRulesFor(_mode, ref token);
                }
            }
        }

        return ProcessResult.Done;
    }

    private void PopUntilHtmlOrIntegrationPoint()
    {
        while (_openElements.Count > 0)
        {
            var node = CurrentNode;
            if (IsMathMLTextIntegrationPoint(node) || IsHtmlIntegrationPoint(node) || node.IsHtmlNamespace)
                return;

            PopCurrentNode();
        }
    }
}
