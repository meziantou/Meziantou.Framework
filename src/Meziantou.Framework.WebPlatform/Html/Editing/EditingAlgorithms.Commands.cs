using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;
using DomRange = Meziantou.Framework.WebPlatform.Dom.Range;

namespace Meziantou.Framework.WebPlatform.Html;

// The commands: their actions, indeterminate flags, states and values (https://w3c.github.io/editing/docs/execCommand/#commands)
internal sealed partial class EditingAlgorithms
{
    /// <summary>The commands whose action preserves the state and value overrides when the selection stays collapsed.</summary>
    private static readonly FrozenSet<string> CommandsPreservingOverrides = Names(
        "delete", "formatblock", "forwarddelete", "indent", "inserthorizontalrule", "inserthtml", "insertimage", "insertlinebreak",
        "insertorderedlist", "insertparagraph", "insertunorderedlist", "justifycenter", "justifyfull", "justifyleft", "justifyright", "outdent");

    private static readonly FrozenSet<string> RemoveFormatCandidateNames = Names(
        "abbr", "acronym", "b", "bdi", "bdo", "big", "blink", "cite", "code", "dfn", "em", "font", "i", "ins", "kbd", "mark", "nobr", "q", "s",
        "samp", "small", "span", "strike", "strong", "sub", "sup", "tt", "u", "var");

    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    private static readonly FrozenSet<string> PreformattedElementNames = Names("pre", "listing", "xmp", "plaintext");

    private static readonly FrozenSet<string> PreformattedContainerNames = Names("address", "listing", "pre");

    /// <summary>The void elements (https://html.spec.whatwg.org/multipage/syntax.html#void-elements).</summary>
    private static readonly FrozenSet<string> VoidElementNames = Names("area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr");

    /// <summary>Whether the command has a value (https://w3c.github.io/editing/docs/execCommand/#value).</summary>
    private static bool HasValue(string command)
        => command is "backcolor" or "fontname" or "forecolor" or "hilitecolor" or "fontsize" or "formatblock" or "justifycenter" or "justifyfull" or "justifyleft" or "justifyright" or "defaultparagraphseparator";

    private static bool IsStandardInlineValueCommand(string command) => command is "backcolor" or "fontname" or "forecolor" or "hilitecolor";

    /// <summary>Takes the action of a command (https://w3c.github.io/editing/docs/execCommand/#action). Returns false when the value is not valid for the command.</summary>
    public bool RunAction(string command, string value)
    {
        // The contents of a select element are not editable text: as in the browsers, the commands do nothing when a
        // boundary point of the selection is in a select element (e.g. in an option)
        if (_activeRange is { } activeRange && (IsInSelectElement(activeRange.StartContainer) || IsInSelectElement(activeRange.EndContainer)) && command is "delete" or "forwarddelete" or "inserttext")
            return false;

        if (!CommandsPreservingOverrides.Contains(command))
            return RunActionCore(command, value);

        var overrides = RecordCurrentOverrides();
        var result = RunActionCore(command, value);
        if (ActiveRange.Collapsed)
        {
            RestoreStatesAndValues(overrides);
            if (command is "delete" or "forwarddelete")
            {
                _state.SetStateOverride(ActiveRange, CaretAfterDeletionOverride, state: true);
            }
        }

        return result;
    }

    private static bool IsInSelectElement(Node node)
    {
        for (var current = node; current is not null; current = current.ParentNode)
        {
            if (IsHtmlElement(current, "select"))
                return true;
        }

        return false;
    }

    private bool RunActionCore(string command, string value)
    {
        switch (command)
        {
            case "backcolor" or "forecolor" or "hilitecolor":
                // A hexadecimal color without its leading # is accepted
                if (value.Length is 3 or 6 && value.AsSpan().IndexOfAnyExceptInRange('0', '9') is var other && (other < 0 || !value.AsSpan().ContainsAnyExcept(HexDigits)))
                {
                    value = "#" + value;
                }

                if (NormalizeColor(value) is null)
                    return false;

                SetSelectionValue(command, value);
                return true;

            case "bold":
                SetSelectionValue("bold", QueryState("bold") ? "normal" : "bold");
                return true;

            case "italic":
                SetSelectionValue("italic", QueryState("italic") ? "normal" : "italic");
                return true;

            case "strikethrough":
            {
                var lineThroughElementName = _lineThroughElementName;
                _lineThroughElementName = "strike";
                SetSelectionValue("strikethrough", QueryState("strikethrough") ? null : "line-through");
                _lineThroughElementName = lineThroughElementName;
                return true;
            }

            case "underline":
                SetSelectionValue("underline", QueryState("underline") ? null : "underline");
                return true;

            case "subscript" or "superscript":
            {
                var state = QueryState(command);
                SetSelectionValue(command, null);
                if (!state)
                {
                    SetSelectionValue(command, command);
                }

                return true;
            }

            case "createlink":
                if (value.Length == 0)
                    return false;

                foreach (var node in GetAllEffectivelyContainedNodes(ActiveRange))
                {
                    for (var ancestor = node.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
                    {
                        if (IsEditable(ancestor) && IsHtmlElement(ancestor, "a") && ((Element)ancestor).HasAttributeWithLocalName("href"))
                        {
                            ((Element)ancestor).SetAttribute("href", value);
                        }
                    }
                }

                SetSelectionValue("createlink", value);
                return true;

            case "fontname":
                SetSelectionValue("fontname", value);
                return true;

            case "fontsize":
                if (NormalizeFontSize(value) is not { } size)
                    return false;

                SetSelectionValue("fontsize", size);
                return true;

            case "removeformat":
                RemoveFormat();
                return true;

            case "unlink":
                Unlink();
                return true;

            case "delete":
            {
                var deletesLineFeed = IsCaretAtLineFeed(forward: false);
                var result = DeleteAction();
                if (!deletesLineFeed)
                {
                    PadLastLineFeed();
                }

                return result;
            }

            case "forwarddelete":
            {
                var deletesLineFeed = IsCaretAtLineFeed(forward: true);
                var result = ForwardDeleteAction();
                if (!deletesLineFeed)
                {
                    PadLastLineFeed();
                }

                return result;
            }

            case "formatblock":
                return FormatBlock(value);

            case "indent":
                Indent();
                return true;

            case "outdent":
                Outdent();
                return true;

            case "inserthorizontalrule":
                return InsertHorizontalRule();

            case "inserthtml":
                return InsertHtml(value);

            case "insertimage":
                return InsertImage(value);

            case "insertlinebreak":
                return InsertLineBreak();

            case "insertparagraph":
                return InsertParagraph();

            case "inserttext":
                return InsertText(value);

            case "insertorderedlist":
                ToggleLists("ol");
                return true;

            case "insertunorderedlist":
                ToggleLists("ul");
                return true;

            case "justifycenter":
                JustifySelection("center");
                return true;

            case "justifyfull":
                JustifySelection("justify");
                return true;

            case "justifyleft":
                JustifySelection("left");
                return true;

            case "justifyright":
                JustifySelection("right");
                return true;

            case "defaultparagraphseparator":
                value = Infra.InfraAscii.ToAsciiLowercase(value);
                if (value is not ("p" or "div"))
                    return false;

                _state.DefaultSingleLineContainerName = value;
                return true;

            case "stylewithcss":
                _state.CssStyling = !Infra.InfraAscii.EqualsIgnoreAsciiCase(value, "false");
                return true;

            case "usecss":
                _state.CssStyling = Infra.InfraAscii.EqualsIgnoreAsciiCase(value, "false");
                return true;

            default:
                return false;
        }
    }

    /// <summary>Whether the command is indeterminate (https://w3c.github.io/editing/docs/execCommand/#indeterminate).</summary>
    public bool QueryIndeterm(string command)
    {
        if (_activeRange is null)
            return false;

        var range = _activeRange;
        switch (command)
        {
            case "fontsize":
            {
                var values = new HashSet<string?>();
                foreach (var node in GetAllEffectivelyContainedNodes(range, IsFormattableNode))
                {
                    values.Add(GetEffectiveCommandValue(node, "fontsize"));
                }

                return values.Count >= 2;
            }

            case "subscript" or "superscript":
            {
                var nodes = GetAllEffectivelyContainedNodes(range, IsFormattableNode);
                var values = nodes.ConvertAll(node => GetEffectiveCommandValue(node, command));
                return (values.Contains(command) && values.Exists(value => value != command)) || values.Contains("mixed");
            }

            case "formatblock":
            {
                var nodeList = GetAllContainedNodes(BlockExtend(range), node => IsVisible(node) && IsEditable(node) && !node.HasChildNodes());
                if (IsDefinitionList(nodeList))
                    return false;

                string? type = null;
                foreach (var node in nodeList)
                {
                    var currentType = GetFormatBlockType(node);
                    if (type is null)
                    {
                        type = currentType;
                    }
                    else if (type != currentType)
                    {
                        return true;
                    }
                }

                return false;
            }

            case "insertorderedlist":
                return GetSelectionListState() is "mixed" or "mixed ol";

            case "insertunorderedlist":
                return GetSelectionListState() is "mixed" or "mixed ul";

            case "justifycenter" or "justifyfull" or "justifyleft" or "justifyright":
            {
                var alignment = GetJustifyAlignment(command);
                var nodes = GetAllContainedNodes(BlockExtend(range), node => IsEditable(node) && IsVisible(node) && !node.HasChildNodes());
                return nodes.Exists(node => GetAlignmentValue(node) == alignment) && nodes.Exists(node => GetAlignmentValue(node) != alignment);
            }
        }

        if (IsStandardInlineValueCommand(command))
        {
            string? previous = null;
            var first = true;
            foreach (var node in GetAllEffectivelyContainedNodes(range, IsFormattableNode))
            {
                var value = GetEffectiveCommandValue(node, command);
                if (!first && value != previous)
                    return true;

                previous = value;
                first = false;
            }

            return false;
        }

        if (GetInlineCommandActivatedValues(command) is not null)
        {
            var values = GetAllEffectivelyContainedNodes(range, IsFormattableNode).ConvertAll(node => GetEffectiveCommandValue(node, command));
            var matching = values.FindAll(value => IsActivatedValue(command, value)).Count;
            return matching >= 1 && values.Count - matching >= 1;
        }

        return false;
    }

    /// <summary>The state of the command (https://w3c.github.io/editing/docs/execCommand/#state), or its state override.</summary>
    public bool QueryState(string command)
    {
        if (command == "stylewithcss")
            return _state.CssStyling;

        if (_activeRange is null)
            return false;

        var range = _activeRange;
        if (_state.GetStateOverride(range, command) is { } stateOverride)
            return stateOverride;

        switch (command)
        {
            case "insertorderedlist":
                return GetSelectionListState() == "ol";

            case "insertunorderedlist":
                return GetSelectionListState() == "ul";

            case "justifycenter" or "justifyfull" or "justifyleft" or "justifyright":
            {
                var alignment = GetJustifyAlignment(command);
                var nodes = GetAllContainedNodes(BlockExtend(range), node => IsEditable(node) && IsVisible(node) && !node.HasChildNodes());
                return nodes.Count > 0 && nodes.TrueForAll(node => GetAlignmentValue(node) == alignment);
            }
        }

        if (GetInlineCommandActivatedValues(command) is not null)
        {
            var nodes = GetAllEffectivelyContainedNodes(range, IsFormattableNode);
            if (nodes.Count == 0)
                return IsActivatedValue(command, GetEffectiveCommandValue(GetCaretNode(range), command));

            return nodes.TrueForAll(node => IsActivatedValue(command, GetEffectiveCommandValue(node, command)));
        }

        return false;
    }

    /// <summary>The value of the command (https://w3c.github.io/editing/docs/execCommand/#value), or its value override.</summary>
    public string QueryValue(string command)
    {
        if (!HasValue(command))
            return "";

        if (command == "defaultparagraphseparator")
            return _state.DefaultSingleLineContainerName;

        if (_activeRange is null)
            return "";

        if (_state.GetValueOverride(_activeRange, command) is { } valueOverride)
            return command == "fontsize" ? GetLegacyFontSize(valueOverride) ?? "" : valueOverride;

        return GetCommandValue(command);
    }

    /// <summary>The value of a command, without its value override.</summary>
    private string GetCommandValue(string command)
    {
        var range = ActiveRange;
        switch (command)
        {
            case "fontsize":
            {
                var node = GetAllEffectivelyContainedNodes(range, IsFormattableNode) is [var first, ..] ? first : GetCaretNode(range);
                return GetEffectiveCommandValue(node, "fontsize") is { } pixelSize ? GetLegacyFontSize(pixelSize) ?? "" : "";
            }

            case "formatblock":
            {
                var nodes = GetAllContainedNodes(BlockExtend(range), node => IsVisible(node) && IsEditable(node) && !node.HasChildNodes());
                return nodes.Count == 0 ? "" : IsDefinitionList(nodes) ? "dl" : GetFormatBlockType(nodes[0]);
            }

            case "justifycenter" or "justifyfull" or "justifyleft" or "justifyright":
            {
                var nodes = GetAllContainedNodes(BlockExtend(range), node => IsEditable(node) && IsVisible(node) && !node.HasChildNodes());
                return nodes.Count > 0 ? GetAlignmentValue(nodes[0]) : "left";
            }
        }

        if (IsStandardInlineValueCommand(command))
        {
            var node = GetAllEffectivelyContainedNodes(range, IsFormattableNode) is [var first, ..] ? first : GetCaretNode(range);
            return GetEffectiveCommandValue(node, command) ?? "";
        }

        return "";
    }

    /// <summary>
    /// The node whose style gives the states and the values of the commands when the active range contains no formattable
    /// node: its start node. As in the browsers, a range that starts in an element after an inline node (e.g. after the
    /// blocks of a deletion are merged) has the style of the end of this node.
    /// </summary>
    private Node GetCaretNode(DomRange range)
    {
        var node = range.StartContainer;
        var offset = (int)range.StartOffset;
        while (node is Element && ChildAt(node, offset - 1) is { } previous && IsInlineNode(previous) && (previous is Text || (previous is Element element && element.HasChildNodes() && !IsAtomicElement(element))))
        {
            node = previous;
            offset = NodeLength(node);
        }

        return node;
    }

    private static string GetJustifyAlignment(string command) => command switch
    {
        "justifycenter" => "center",
        "justifyfull" => "justify",
        "justifyright" => "right",
        _ => "left",
    };

    /// <summary>
    /// Whether the formattable blocks of the nodes are the dt and dd elements of one dl: as in the browsers, the value of
    /// formatBlock is then <c>dl</c>, and the command is not indeterminate.
    /// </summary>
    private bool IsDefinitionList(List<Node> nodes)
    {
        Node? list = null;
        var hasTerm = false;
        var hasDescription = false;
        foreach (var node in nodes)
        {
            var block = node;
            while (IsEditable(block.ParentNode) && InSameEditingHost(block, block.ParentNode) && !IsHtmlElement(block, FormattableBlockNames))
            {
                block = block.ParentNode;
            }

            if (!IsHtmlElement(block, DefinitionItemNames) || !IsHtmlElement(block.ParentNode, "dl") || (list is not null && !ReferenceEquals(list, block.ParentNode)))
                return false;

            list = block.ParentNode;
            hasTerm |= IsHtmlElement(block, "dt");
            hasDescription |= IsHtmlElement(block, "dd");
        }

        return hasTerm && hasDescription;
    }

    /// <summary>The local name of the formattable block of a node (the value of formatBlock), or the empty string.</summary>
    private string GetFormatBlockType(Node node)
    {
        while (IsEditable(node.ParentNode) && InSameEditingHost(node, node.ParentNode) && !IsHtmlElement(node, FormattableBlockNames))
        {
            node = node.ParentNode;
        }

        if (IsEditable(node) && IsHtmlElement(node, FormattableBlockNames) && !AnyDescendant(node, IsProhibitedParagraphChild))
            return ((Element)node).LocalName;

        return "";
    }

    /// <summary>
    /// The keyword of the font size given to the fontSize command (https://w3c.github.io/editing/docs/execCommand/#the-fontsize-command):
    /// a number from 1 to 7, which may be relative to 3 (<c>+1</c>, <c>-2</c>). Returns null when the value is not a number.
    /// </summary>
    private static string? NormalizeFontSize(string value)
    {
        value = value.Trim(' ', '\t', '\n', '\f', '\r');
        if (!FontSizeRegex().IsMatch(value))
            return null;

        var relativePlus = value[0] == '+';
        var relativeMinus = value[0] == '-';
        var digits = value.AsSpan(relativePlus || relativeMinus ? 1 : 0);
        var end = 0;
        while (end < digits.Length && char.IsAsciiDigit(digits[end]))
        {
            end++;
        }

        if (!int.TryParse(digits[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            number = int.MaxValue / 2;
        }

        if (relativePlus)
        {
            number += 3;
        }
        else if (relativeMinus)
        {
            number = 3 - number;
        }

        return FontSizeKeywords[Math.Clamp(number, 1, 7) - 1];
    }

    [GeneratedRegex(@"^[-+]?[0-9]+(\.[0-9]+)?([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FontSizeRegex();

    [GeneratedRegex(@"^(?<number>[0-9]+(\.[0-9]+)?)(?<unit>cm|mm|in|pt|pc|px)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AbsoluteLengthRegex();

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#legacy-font-size-for: the size of the font element (1 to 7) whose
    /// font size is the closest to a size, given as a keyword, an absolute length or a number of the fontSize command.
    /// </summary>
    private string? GetLegacyFontSize(string size)
    {
        if (NormalizeFontSize(size) is { } normalized)
            return (Array.IndexOf(FontSizeKeywords, normalized) + 1).ToString(CultureInfo.InvariantCulture);

        double pixelSize;
        if (Array.IndexOf(FontSizeKeywords, size) is >= 0 and var keywordIndex)
        {
            pixelSize = GetFontSizePixels(keywordIndex);
        }
        else if (AbsoluteLengthRegex().Match(size) is { Success: true } match)
        {
            var number = double.Parse(match.Groups["number"].ValueSpan, CultureInfo.InvariantCulture);
            pixelSize = Math.Truncate(number * match.Groups["unit"].Value switch
            {
                "cm" => 96 / 2.54,
                "mm" => 96 / 25.4,
                "in" => 96,
                "pt" => 96 / 72d,
                "pc" => 16,
                _ => 1,
            });
        }
        else
        {
            return null;
        }

        for (var returnedSize = 1; returnedSize < 7; returnedSize++)
        {
            var average = (GetFontSizePixels(returnedSize - 1) + GetFontSizePixels(returnedSize)) / 2;
            if (pixelSize < average)
                return returnedSize.ToString(CultureInfo.InvariantCulture);
        }

        return "7";

        // The font size of the font element of a size, in pixels without their fraction
        double GetFontSizePixels(int index)
        {
            var value = GetFontSizeKeywordPixels(FontSizeKeywords[index]);
            return value is not null && value.EndsWith("px", StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels) ? Math.Truncate(pixels) : 16;
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-removeformat-command</summary>
    private void RemoveFormat()
    {
        var range = ActiveRange;
        foreach (var element in GetAllEffectivelyContainedNodes(range, IsRemoveFormatCandidate))
        {
            while (element.FirstChild is { } child)
            {
                MovePreservingRanges(child, element.ParentNode!, NodeIndex(element));
            }

            RemoveNode(element);
        }

        SplitTextAtBoundaryPoints();
        foreach (var node in GetAllEffectivelyContainedNodes(range, static node => IsEditable(node)))
        {
            while (IsRemoveFormatCandidate(node.ParentNode) && InSameEditingHost(node.ParentNode, node))
            {
                var parent = node.ParentNode;
                SplitParent([node]);
                if (ReferenceEquals(node.ParentNode, parent))
                    break;
            }
        }

        foreach (var command in RecordedCommands)
        {
            SetSelectionValue(command, null);
        }

        static bool IsRemoveFormatCandidate(Node? node) => IsEditable(node) && IsHtmlElement(node, RemoveFormatCandidateNames);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-unlink-command</summary>
    private void Unlink()
    {
        var range = ActiveRange;
        var hyperlinks = new List<Element>();
        for (Node? node = range.StartContainer; node is not null; node = node.ParentNode)
        {
            if (IsHyperlink(node))
            {
                hyperlinks.Insert(0, (Element)node);
            }
        }

        var stop = NextNodeDescendants(range.EndContainer);
        for (Node? node = range.StartContainer; node is not null && !ReferenceEquals(node, stop); node = NextNode(node))
        {
            if (IsHyperlink(node) && (IsContained(node, range) || IsAncestor(node, range.EndContainer) || ReferenceEquals(node, range.EndContainer)))
            {
                hyperlinks.Add((Element)node);
            }
        }

        foreach (var hyperlink in hyperlinks)
        {
            ClearValue(hyperlink, "unlink");
        }

        static bool IsHyperlink(Node node) => IsHtmlElement(node, "a") && ((Element)node).HasAttributeWithLocalName("href");
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-formatblock-command</summary>
    private bool FormatBlock(string value)
    {
        if (value.Length >= 2 && value[0] == '<' && value[^1] == '>')
        {
            value = value[1..^1];
        }

        value = Infra.InfraAscii.ToAsciiLowercase(value);
        if (!FormattableBlockNames.Contains(value))
            return false;

        var newRange = BlockExtend(ActiveRange);
        var nodeList = GetContainedNodes(newRange, static node => IsEditable(node)
            && (IsReplacedByFormatBlock(node) || IsAllowedChild(node, "p") || IsHtmlElement(node, DefinitionItemNames))
            && !AnyDescendant(node, IsProhibitedParagraphChild));
        var values = RecordValues(nodeList);
        foreach (var node in nodeList)
        {
            while (HasFormattableBlockAncestor(node))
            {
                var parent = node.ParentNode;
                SplitParent([node]);
                if (ReferenceEquals(node.ParentNode, parent))
                    break;
            }
        }

        RestoreValues(values);
        var index = 0;
        while (index < nodeList.Count)
        {
            List<Node> sublist;
            if (IsReplacedByFormatBlock(nodeList[index]))
            {
                sublist = GetChildren(nodeList[index]);
                var childValues = RecordValues(sublist);
                RemovePreservingDescendants(nodeList[index]);
                RestoreValues(childValues);
                index++;
            }
            else
            {
                sublist = [nodeList[index++]];
                while (index < nodeList.Count && ReferenceEquals(nodeList[index], sublist[^1].NextSibling) && !IsReplacedByFormatBlock(nodeList[index]) && !IsHtmlElement(sublist[^1], "br"))
                {
                    sublist.Add(nodeList[index++]);
                }
            }

            FixDisallowedAncestors(Wrap(
                sublist,
                value is "div" or "p" ? null : node => IsHtmlElement(node, value) && ((Element)node).AttributeCount == 0,
                () => CreateElement(value)));
        }

        return true;

        // The single-line containers are replaced by the new block, except, as in the browsers, the listing and xmp elements,
        // which are wrapped in it
        static bool IsReplacedByFormatBlock(Node node) => IsSingleLineContainer(node) && !IsHtmlElement(node, "listing") && !IsHtmlElement(node, "xmp");

        static bool HasFormattableBlockAncestor(Node node)
        {
            for (var ancestor = node.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
            {
                if (IsEditable(ancestor) && InSameEditingHost(ancestor, node) && IsHtmlElement(ancestor, FormattableBlockNames) && !AnyDescendant(ancestor, IsProhibitedParagraphChild))
                    return true;
            }

            return false;
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-indent-command</summary>
    private void Indent()
    {
        var range = ActiveRange;
        var common = range.CommonAncestorContainer;
        var items = new List<Node>();
        for (Node? node = range.EndContainer; node is not null && !ReferenceEquals(node, common); node = node.ParentNode)
        {
            if (IsHtmlElement(node, "li"))
            {
                items.Insert(0, node);
            }
        }

        for (Node? node = range.StartContainer; node is not null && !ReferenceEquals(node, common); node = node.ParentNode)
        {
            if (IsHtmlElement(node, "li"))
            {
                items.Insert(0, node);
            }
        }

        for (Node? node = common; node is not null; node = node.ParentNode)
        {
            if (IsHtmlElement(node, "li"))
            {
                items.Insert(0, node);
            }
        }

        foreach (var item in items)
        {
            NormalizeSublists(item);
        }

        var newRange = BlockExtend(range);
        var nodeList = GetContainedNodes(newRange, static node => IsEditable(node) && (IsAllowedChild(node, "div") || IsAllowedChild(node, "ol")));
        if (nodeList.Find(IsVisible) is { } firstVisible && IsHtmlElement(firstVisible, "li") && IsHtmlElement(firstVisible.ParentNode, ListNames))
        {
            var sibling = firstVisible.PreviousSibling;
            while (IsInvisible(sibling))
            {
                sibling = sibling.PreviousSibling;
            }

            if (IsHtmlElement(sibling, "li"))
            {
                NormalizeSublists(sibling);
            }
        }

        var index = 0;
        while (index < nodeList.Count)
        {
            var sublist = new List<Node> { nodeList[index++] };
            while (index < nodeList.Count && ReferenceEquals(nodeList[index], sublist[^1].NextSibling))
            {
                sublist.Add(nodeList[index++]);
            }

            IndentNodes(sublist);
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-outdent-command</summary>
    private void Outdent()
    {
        var range = ActiveRange;
        var common = range.CommonAncestorContainer;
        var items = new List<Node>();
        for (Node? node = range.StartContainer; node is not null; node = node.ParentNode)
        {
            if (IsHtmlElement(node, "li"))
            {
                items.Insert(0, node);
            }
        }

        var endItems = new List<Node>();
        for (Node? node = range.EndContainer; node is not null && !ReferenceEquals(node, common); node = node.ParentNode)
        {
            if (IsHtmlElement(node, "li"))
            {
                endItems.Insert(0, node);
            }
        }

        items.AddRange(endItems);
        foreach (var item in items)
        {
            NormalizeSublists(item);
        }

        var newRange = BlockExtend(range);
        var nodeList = GetContainedNodes(newRange, static node => IsEditable(node)
            && (!HasEditableDescendants(node) || IsHtmlElement(node, ListNames) || (IsHtmlElement(node, "li") && IsHtmlElement(node.ParentNode, ListNames))));
        var index = 0;
        while (index < nodeList.Count)
        {
            while (index < nodeList.Count && (IsHtmlElement(nodeList[index], ListNames) || !IsHtmlElement(nodeList[index].ParentNode, ListNames)))
            {
                OutdentNode(nodeList[index++]);
            }

            if (index >= nodeList.Count)
                break;

            var sublist = new List<Node> { nodeList[index++] };
            while (index < nodeList.Count && ReferenceEquals(nodeList[index], sublist[^1].NextSibling) && !IsHtmlElement(nodeList[index], ListNames))
            {
                sublist.Add(nodeList[index++]);
            }

            var values = RecordValues(sublist);
            SplitParent(sublist);
            foreach (var node in sublist)
            {
                FixDisallowedAncestors(node);
            }

            RestoreValues(values);
        }
    }

    /// <summary>Moves a boundary point of the active range that is at the start or at the end of a Text node out of it.</summary>
    private void MoveCollapsedRangeOutOfText()
    {
        var range = ActiveRange;
        if (range.StartContainer is Text text && text.ParentNode is { } parent)
        {
            if (range.StartOffset == 0)
            {
                CollapseTo(parent, NodeIndex(text));
            }
            else if (range.StartOffset == text.Length)
            {
                CollapseTo(parent, 1 + NodeIndex(text));
            }
        }
    }

    /// <summary>
    /// Deletes the selection before an insertion. The specification also runs the algorithm for a caret, which only
    /// canonicalizes the white space around it; as in the browsers, an insertion at a caret leaves the white space that
    /// it does not move to a boundary of a line as it is (e.g. a sequence of no-break spaces).
    /// </summary>
    private void DeleteSelectionUnlessCollapsed()
    {
        if (!ActiveRange.Collapsed)
        {
            DeleteSelection();
        }
    }

    /// <summary>
    /// Removes the collapsible spaces around a caret in a Text node that are not rendered: the spaces after the caret that
    /// end the line (with the spaces before the caret), and the spaces before the caret that begin the line (with the
    /// spaces after the caret). The other white space is left as it is.
    /// </summary>
    private void RemoveCollapsedSpacesAtCaret()
    {
        var range = ActiveRange;
        if (range.StartContainer is not Text text || !range.Collapsed || PreservesWhiteSpace(text.ParentNode))
            return;

        var data = text.Data;
        var start = (int)range.StartOffset;
        var end = start;
        while (start > 0 && data[start - 1] == ' ')
        {
            start--;
        }

        while (end < data.Length && data[end] == ' ')
        {
            end++;
        }

        if (end > start && ((end == data.Length && FindLineItem(text, forward: true) != LineItem.Content) || (start == 0 && FindLineItem(text, forward: false) != LineItem.Content)))
        {
            text.DeleteData((uint)start, (uint)(end - start));
        }
    }

    /// <summary>
    /// Replaces the collapsible space that ends (or begins) the text of the inline contents of a node with a no-break
    /// space: the space is now at a boundary of a line, where it would collapse.
    /// </summary>
    private void ProtectBoundarySpace(Node? node, bool atEnd)
    {
        while (node is Element element && IsDisplayInline(element) && !IsAtomicElement(element))
        {
            node = atEnd ? element.LastChild : element.FirstChild;
        }

        if (node is not Text { Length: > 0 } text || !IsEditable(text) || PreservesWhiteSpace(text.ParentNode))
            return;

        var index = (int)(atEnd ? text.Length - 1 : 0);
        var data = text.Data;
        if (data[index] != ' ')
            return;

        // The character is replaced without moving the boundary points that are after it
        text.InsertData((uint)index, NoBreakSpace);
        text.DeleteData((uint)index + 1, 1);

        // The no-break space that followed the space of the start of a line is not needed any more before other text
        if (!atEnd && data.Length > 1 && data[1] == NoBreakSpaceCharacter && (data.Length == 2 ? FindLineItem(text, forward: true) == LineItem.Content : data[2] is not (' ' or NoBreakSpaceCharacter)))
        {
            text.InsertData(1, " ");
            text.DeleteData(2, 1);
        }
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-inserthorizontalrule-command</summary>
    private bool InsertHorizontalRule()
    {
        var range = ActiveRange;
        var startNode = range.StartContainer;
        var startOffset = (int)range.StartOffset;
        var endNode = range.EndContainer;
        var endOffset = (int)range.EndOffset;
        while (startOffset == 0 && startNode.ParentNode is { } parent)
        {
            startOffset = NodeIndex(startNode);
            startNode = parent;
        }

        while (endOffset == NodeLength(endNode) && endNode.ParentNode is { } parent)
        {
            endOffset = 1 + NodeIndex(endNode);
            endNode = parent;
        }

        SetRange(startNode, startOffset, endNode, endOffset);
        DeleteSelection(blockMerging: false);
        if (!IsEditableOrEditingHost(range.StartContainer))
            return true;

        MoveCollapsedRangeOutOfText();
        var hr = CreateElement("hr");
        range.InsertNode(hr);
        FixDisallowedAncestors(hr);
        CollapseTo(hr.ParentNode!, 1 + NodeIndex(hr));
        return true;
    }

    /// <summary>
    /// Moves the caret out of its editable inline ancestors, except the links: before or after the ones it begins or ends,
    /// and between the two halves of the ones it is in the middle of, which are split (the second half is a clone without
    /// id).
    /// </summary>
    private void SplitInlineAncestorsAtCaret()
    {
        var range = ActiveRange;
        if (!range.Collapsed)
            return;

        var node = range.StartContainer;
        var offset = (int)range.StartOffset;
        if (node is Text text && text.ParentNode is { } textParent)
        {
            if (offset != 0 && offset != text.Length)
            {
                text.SplitText((uint)offset);
            }

            offset = offset == 0 ? NodeIndex(text) : 1 + NodeIndex(text);
            node = textParent;
        }

        while (node is Element element && node.ParentNode is { } parent && IsEditable(node) && IsDisplayInline(element) && !IsHtmlElement(node, "a") && !IsAtomicElement(node))
        {
            if (offset == 0)
            {
                offset = NodeIndex(node);
            }
            else
            {
                if (offset != node.ChildCount)
                {
                    var clone = (Element)element.CloneNode();
                    clone.RemoveAttribute("id");
                    InsertAfter(clone, node);
                    while (ChildAt(node, offset) is { } child)
                    {
                        clone.AppendChild(child);
                    }
                }

                offset = 1 + NodeIndex(node);
            }

            node = parent;
        }

        CollapseTo(node, offset);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-inserthtml-command</summary>
    private bool InsertHtml(string value, DocumentFragment? pastedFragment = null)
    {
        // Plain text content (contenteditable=plaintext-only): the text of the HTML is inserted
        if (pastedFragment is null && IsInPlaintextOnlyContent())
            return InsertHtmlAsPlainText(value);

        DeleteSelectionUnlessCollapsed();
        var range = ActiveRange;
        if (!IsEditableOrEditingHost(range.StartContainer))
            return true;

        // The scripts of the fragment are not run when they are inserted (see CreateContextualFragmentForEditing)
        var fragment = pastedFragment ?? range.CreateContextualFragmentForEditing(value);
        var lastChild = fragment.LastChild;
        if (lastChild is null)
            return true;

        // The specification inserts the fragment where the selection is: its contents get the formatting of the inline
        // elements of the insertion point. As in the browsers, these elements are split (or the fragment is inserted
        // before or after them, at their start or at their end) when the fragment has elements
        // and only inline contents (the formatting of the blocks is preserved when their disallowed ancestors are fixed)
        if (fragment.FirstElementChild is not null && GetChildren(fragment).TrueForAll(static child => IsAllowedChild(child, "span")))
        {
            SplitInlineAncestorsAtCaret();
        }

        var descendants = GetDescendants(fragment);
        if (IsBlockNode(range.StartContainer))
        {
            foreach (var child in GetChildren(range.StartContainer))
            {
                if (IsEditable(child) && IsCollapsedBlockProp(child) && NodeIndex(child) >= range.StartOffset)
                {
                    RemoveNode(child);
                }
            }
        }

        range.InsertNode(fragment);
        if (IsBlockNode(range.StartContainer) && !HasVisibleChild(range.StartContainer))
        {
            range.StartContainer.AppendChild(CreateElement("br"));
        }

        if (lastChild.ParentNode is { } lastChildParent)
        {
            CollapseTo(lastChildParent, 1 + NodeIndex(lastChild));
        }

        foreach (var descendant in descendants)
        {
            FixDisallowedAncestors(descendant);
        }

        return true;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-insertimage-command</summary>
    private bool InsertImage(string value)
    {
        if (value.Length == 0)
            return false;

        DeleteSelectionUnlessCollapsed();
        var range = ActiveRange;
        if (!IsEditableOrEditingHost(range.StartContainer))
            return true;

        if (IsBlockNode(range.StartContainer) && range.StartContainer.ChildCount == 1 && IsHtmlElement(range.StartContainer.FirstChild, "br") && range.StartOffset == 0)
        {
            range.StartContainer.RemoveChild(range.StartContainer.FirstChild);
        }

        // At the start or at the end of a Text node, the image is inserted before or after the node: inserting it in the node
        // would split it and leave an empty Text node, which the split of the inline ancestors below would keep in an empty
        // element (<b>foo</b><img><b></b>)
        MoveCollapsedRangeOutOfText();
        var img = CreateElement("img");
        img.SetAttribute("src", value);
        range.InsertNode(img);

        // The specification inserts the image where the selection was. As in the browsers (and as the tests expect), the
        // image is not a child of the inline elements of the insertion point: they are split
        while (img.ParentNode is Element parent && IsEditable(parent) && IsInlineNode(parent) && !IsHtmlElement(parent, "a") && InSameEditingHost(parent, img))
        {
            SplitParent([img]);
            if (ReferenceEquals(img.ParentNode, parent))
                break;
        }

        CollapseTo(img.ParentNode!, 1 + NodeIndex(img));
        return true;
    }

    /// <summary>
    /// Whether nothing follows the node on its line in its editing host, when the editing host is inline: as in the
    /// browsers, the line break that ends an inline editing host needs another one to be rendered in it, whatever follows
    /// the host on the line.
    /// </summary>
    private bool EndsInlineEditingHost(Node node)
        => GetEditingHostOf(node) is { } host && IsDisplayInline(host) && FindLineItem(node, forward: true, host) == LineItem.Boundary;

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-insertlinebreak-command</summary>
    private bool InsertLineBreak()
    {
        DeleteSelection(stripWrappers: false);
        var range = ActiveRange;
        if (!IsEditableOrEditingHost(range.StartContainer))
            return true;

        var container = range.StartContainer as Element ?? range.StartContainer.ParentNode;
        if (container is null || !IsAllowedChild("br", container))
            return true;

        // The specification always inserts a br element. As in the browsers, a line feed is inserted where white-space
        // preserves it (pre, pre-wrap, pre-line), except in the preformatted elements (pre, listing, xmp)
        if (PreservesLineFeeds(GetWhiteSpace(container)) && !IsHtmlElement(GetBlockNodeOf(container), PreformattedElementNames))
        {
            Text text;
            int offset;
            if (range.StartContainer is Text startText)
            {
                (text, offset) = (startText, (int)range.StartOffset);
                text.InsertData((uint)offset, "\n");
            }
            else
            {
                text = _document.CreateTextNode("\n");
                range.InsertNode(text);
                offset = 0;
            }

            // A line feed at the end of an inline editing host needs another one to be rendered; at the end of a block, it
            // needs a br element (see PadLastLineFeed: a second line feed would be in the text content of the block)
            if (offset + 1 == text.Length && FindLineItem(text, forward: true) != LineItem.Boundary && EndsInlineEditingHost(text))
            {
                text.InsertData((uint)offset + 1, "\n");
            }

            CollapseTo(text, offset + 1);
            PadLastLineFeed();
            return true;
        }

        MoveCollapsedRangeOutOfText();
        var br = CreateElement("br");
        range.InsertNode(br);
        CollapseTo(br.ParentNode!, 1 + NodeIndex(br));
        if (IsCollapsedLineBreak(br) || EndsInlineEditingHost(br))
        {
            range.InsertNode(CreateElement("br"));
            CollapseTo(br.ParentNode!, 1 + NodeIndex(br));
        }

        return true;
    }

    /// <summary>
    /// Whether the node is a single-line container that insertParagraph splits. The specification splits every single-line
    /// container; as in the browsers, the ones that are inline-level (e.g. a div whose display is inline or inline-block)
    /// are inline contents of their line.
    /// </summary>
    private bool IsParagraphContainer(Node? node) => node is Element element && IsSingleLineContainer(node) && !GetDisplay(element).IsInlineLevel;

    private bool IsInlineLevelContainer(Node? node) => node is Element element && IsSingleLineContainer(node) && GetDisplay(element).IsInlineLevel;

    /// <summary>
    /// The insertion point of a paragraph for a caret that is in an element that has no line: the point before the table for
    /// the parts of a table that are not cells or captions (table, colgroup, col, tbody, thead, tfoot, tr), the point before
    /// the element for a void element (e.g. hr). The specification splits these elements (or inserts a br in them); the
    /// browsers handle the caret outside of them.
    /// </summary>
    private static (Node Node, int Offset) MoveOutOfNonSplittableElements(Node node, int offset)
    {
        while (node.ParentNode is { } parent && IsEditable(node) && (IsHtmlElement(node, TableStructureNames) || IsHtmlElement(node, VoidElementNames)))
        {
            offset = NodeIndex(node);
            node = parent;
        }

        return (node, offset);
    }

    /// <summary>The states and the values of the inline formatting commands at the caret (with their overrides), except the link.</summary>
    private List<CommandOverride> RecordCaretStatesAndValues()
    {
        var result = new List<CommandOverride>();
        foreach (var command in (ReadOnlySpan<string>)["bold", "italic", "strikethrough", "subscript", "superscript", "underline"])
        {
            result.Add(new(command, QueryState(command), null));
        }

        foreach (var command in (ReadOnlySpan<string>)["fontname", "forecolor", "hilitecolor"])
        {
            result.Add(new(command, null, QueryValue(command)));
        }

        result.Add(new("fontsize", null, GetCaretFontSize()));
        return result;
    }

    private string? GetCaretFontSize()
        => _activeRange is { } range ? _state.GetValueOverride(range, "fontsize") ?? GetEffectiveCommandValue(GetCaretNode(range), "fontsize") : null;

    /// <summary>
    /// Sets the state and value overrides of the caret for the recorded states and values that differ from the ones of its
    /// container: the text typed there gets the recorded formatting. The states and the values that are the ones of
    /// <paramref name="block"/> are not formatting of its inline contents (e.g. the bold of a heading, the color of a
    /// link): they are skipped.
    /// </summary>
    private void SetCaretOverrides(List<CommandOverride> recorded, Element block)
    {
        var range = ActiveRange;
        var container = range.StartContainer;
        foreach (var (command, state, value) in recorded)
        {
            if (state is { } stateOverride)
            {
                if (IsActivatedValue(command, GetEffectiveCommandValue(block, command)) != stateOverride && IsActivatedValue(command, GetEffectiveCommandValue(container, command)) != stateOverride)
                {
                    _state.SetStateOverride(range, command, stateOverride);
                }
            }
            else if (value is not null
                && !string.Equals(GetEffectiveCommandValue(block, command) ?? "", value, StringComparison.Ordinal)
                && !string.Equals(GetEffectiveCommandValue(container, command) ?? "", value, StringComparison.Ordinal))
            {
                _state.SetValueOverride(range, command, value);
            }
        }
    }

    /// <summary>
    /// The states and the values of the active range (https://w3c.github.io/editing/docs/execCommand/#record-current-states-and-values),
    /// without the ones that the block of its start has too (e.g. the bold of a heading): the formatting of the text that
    /// begins its inline contents.
    /// </summary>
    private List<CommandOverride> RecordInlineStatesAndValues()
    {
        // The formatting of an image that begins the selection is not the formatting of text (Chromium and Gecko)
        if (GetAllEffectivelyContainedNodes(ActiveRange).Find(IsFormattableNode) is not Text)
            return [];

        var recorded = RecordCurrentStatesAndValues();
        if (GetBlockNodeOf(ActiveRange.StartContainer) is not { } block)
            return recorded;

        var sameFontSize = recorded.Count > 0 && string.Equals(recorded[^1].Value, GetEffectiveCommandValue(block, "fontsize"), StringComparison.Ordinal);
        recorded.RemoveAll(item => item.State is { } state
            ? IsActivatedValue(item.Command, GetEffectiveCommandValue(block, item.Command)) == state
            : item.Command == "fontsize" ? sameFontSize : string.Equals(item.Value ?? "", GetEffectiveCommandValue(block, item.Command) ?? "", StringComparison.Ordinal));
        return recorded;
    }

    /// <summary>
    /// Clones the inline elements of the caret of insertParagraph (<paramref name="ancestors"/>, from the outermost) in a
    /// line that the split has left empty or with a sole br: the clones (without their id) are nested at the start of
    /// <paramref name="block"/>, after the clones that the split has already created, around the br of the line (which is
    /// created when there is none). Returns the container of the br, or the block when the line is not empty.
    /// </summary>
    private Node CloneInlineFormatting(Node block, List<Element> ancestors)
    {
        var parent = block;
        var depth = 0;
        while (depth < ancestors.Count && parent.FirstChild is Element child && child.NextSibling is null && child.LocalName == ancestors[depth].LocalName && !IsHtmlElement(child, "br"))
        {
            parent = child;
            depth++;
        }

        var lineBreak = parent.FirstChild;
        if (depth == ancestors.Count || (lineBreak is not null && (!IsHtmlElement(lineBreak, "br") || lineBreak.NextSibling is not null)))
            return depth == ancestors.Count && lineBreak is not null && IsHtmlElement(lineBreak, "br") && lineBreak.NextSibling is null ? parent : block;

        var reference = lineBreak;
        for (; depth < ancestors.Count; depth++)
        {
            var clone = (Element)ancestors[depth].CloneNode();
            clone.RemoveAttribute("id");
            parent.InsertBefore(clone, reference);
            parent = clone;
            reference = null;
        }

        parent.AppendChild(lineBreak ?? CreateElement("br"));
        return parent;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-insertparagraph-command</summary>
    private bool InsertParagraph()
    {
        // As the delete command does, the states and the values of the selection that is deleted are restored at the new
        // caret (the text typed in the new paragraph has the formatting of the start of the selection, as in the browsers)
        var isCaret = ActiveRange.Collapsed;
        var selectionFormatting = isCaret ? null : RecordInlineStatesAndValues();
        DeleteSelectionUnlessCollapsed();
        RemoveCollapsedSpacesAtCaret();
        var range = ActiveRange;
        if (!IsEditableOrEditingHost(range.StartContainer))
            return true;

        var caretFormatting = RecordCaretStatesAndValues();
        var node = range.StartContainer;
        var offset = (int)range.StartOffset;
        if (node is Text text && offset != 0 && offset != NodeLength(node))
        {
            text.SplitText((uint)offset);
        }

        if (node is Text && offset == NodeLength(node))
        {
            offset = 1 + NodeIndex(node);
            node = node.ParentNode!;
        }

        if (node is Text or Comment)
        {
            offset = NodeIndex(node);
            node = node.ParentNode!;
        }

        if (isCaret)
        {
            (node, offset) = MoveOutOfNonSplittableElements(node, offset);
        }

        CollapseTo(node, offset);
        var caretNode = node;
        var container = node;
        while (!IsParagraphContainer(container) && IsEditable(container.ParentNode) && InSameEditingHost(node, container.ParentNode))
        {
            container = container.ParentNode;
        }

        if (IsEditable(container) && IsParagraphContainer(container) && InSameEditingHost(node, container.ParentNode) && (IsHtmlElement(container, "p") || IsHtmlElement(container, "div")))
        {
            var outerContainer = container;
            while (!IsHtmlElement(outerContainer, ListItemNames) && IsEditable(outerContainer.ParentNode))
            {
                outerContainer = outerContainer.ParentNode;
            }

            if (IsHtmlElement(outerContainer, ListItemNames))
            {
                container = outerContainer;
            }
        }

        if (!IsEditable(container) || !InSameEditingHost(container, node) || !IsParagraphContainer(container))
        {
            var tag = _state.DefaultSingleLineContainerName;

            // The specification wraps the line in a new container, even when the editing host is inline or cannot contain
            // it (e.g. a span or a p element). As in the browsers, a line break is then inserted
            var host = GetEditingHostOf(node);
            if (host is null || IsInlineNode(host) || !IsAllowedChild(tag, host))
                return InsertLineBreak();

            var newRange = BlockExtend(range);
            // The single-line containers that are inline-level are contents of the line, as the inline elements
            var nodeList = GetContainedNodes(newRange, contained => IsEditable(contained) && InSameEditingHost(contained, node) && (IsAllowedChild(contained, "p") || IsInlineLevelContainer(contained)));
            if (nodeList.Count == 0)
            {
                if (!IsAllowedChild(tag, range.StartContainer))
                    return true;

                var newBlock = CreateElement(tag);
                range.InsertNode(newBlock);
                newBlock.AppendChild(CreateElement("br"));
                CollapseTo(newBlock, 0);
                return true;
            }

            nodeList.RemoveRange(1, nodeList.Count - 1);
            while (nodeList[^1].NextSibling is { } next && (IsAllowedChild(next, "p") || IsInlineLevelContainer(next)))
            {
                nodeList.Add(next);
            }

            // A p element cannot contain these containers: the line is wrapped in a div
            if (tag == "p" && nodeList.Exists(IsInlineLevelContainer))
            {
                tag = "div";
            }

            // The specification removes the br that precedes the new container (it is extraneous before a block). As in
            // Chromium and WebKit, and as the tests expect, the br that ends the previous line is kept
            var previousLineBreak = IsHtmlElement(nodeList[0].PreviousSibling, "br") && IsEditable(nodeList[0].PreviousSibling) ? nodeList[0].PreviousSibling : null;
            if (Wrap(nodeList, siblingCriteria: null, () => CreateElement(tag)) is not { } wrapper)
                return true;

            if (previousLineBreak is { ParentNode: null } && wrapper.ParentNode is { } wrapperParent)
            {
                wrapperParent.InsertBefore(previousLineBreak, wrapper);
            }

            container = wrapper;
        }

        var containerElement = (Element)container;
        if (IsHtmlElement(containerElement, PreformattedContainerNames))
        {
            var br = CreateElement("br");
            range.InsertNode(br);
            CollapseTo(node, offset + 1);
            if (!IsDescendant(NextNode(br), containerElement))
            {
                range.InsertNode(CreateElement("br"));
                CollapseTo(node, offset + 1);
            }

            return true;
        }

        if (IsHtmlElement(containerElement, ListItemNames) && (!containerElement.HasChildNodes() || (containerElement.ChildCount == 1 && IsHtmlElement(containerElement.FirstChild, "br"))))
        {
            SplitParent([containerElement]);
            if (!containerElement.HasChildNodes())
            {
                containerElement.AppendChild(CreateElement("br"));
            }

            if (IsHtmlElement(containerElement, DefinitionItemNames) && HasNoAllowedAncestor(containerElement))
            {
                containerElement = SetTagName(containerElement, _state.DefaultSingleLineContainerName);
            }

            FixDisallowedAncestors(containerElement);
            return true;
        }

        // The inline ancestors of the caret in the block of its line, from the outermost. The formatting of a caret that is
        // in a link is not kept (as in Chromium and WebKit)
        var formattingAncestors = new List<Element>();
        for (var ancestor = caretNode; ancestor is Element element && !ReferenceEquals(ancestor, containerElement) && IsEditable(ancestor) && IsDisplayInline(element) && !IsProhibitedParagraphChild(ancestor) && !IsAtomicElement(ancestor); ancestor = ancestor.ParentNode)
        {
            formattingAncestors.Insert(0, element);
        }

        // The elements that are cloned in the line that the caret began, when the split leaves it empty: the ones that contain the link
        var linkIndex = formattingAncestors.FindIndex(static element => IsHtmlElement(element, "a"));
        var startFormattingAncestors = linkIndex < 0 ? formattingAncestors : formattingAncestors.GetRange(0, linkIndex);
        if (linkIndex >= 0)
        {
            formattingAncestors = [];
        }

        var newLineRange = _document.CreateRange();
        newLineRange.SetStart(range.StartContainer, range.StartOffset);
        newLineRange.SetEnd(containerElement, (uint)NodeLength(containerElement));
        while (newLineRange.StartOffset == 0 && !IsProhibitedParagraphChild(newLineRange.StartContainer) && newLineRange.StartContainer.ParentNode is { } parent)
        {
            newLineRange.SetStart(parent, (uint)NodeIndex(newLineRange.StartContainer));
        }

        while (newLineRange.StartOffset == NodeLength(newLineRange.StartContainer) && !IsProhibitedParagraphChild(newLineRange.StartContainer) && newLineRange.StartContainer.ParentNode is { } parent)
        {
            newLineRange.SetStart(parent, 1 + (uint)NodeIndex(newLineRange.StartContainer));
        }

        var containedInNewLineRange = GetContainedNodes(newLineRange);
        var endOfLine = containedInNewLineRange.Count == 0 || (containedInNewLineRange.Count == 1 && IsHtmlElement(containedInNewLineRange[0], "br"));
        var containerName = containerElement.LocalName;
        string newContainerName;
        if (HeadingNames.Contains(containerName) && endOfLine)
        {
            newContainerName = _state.DefaultSingleLineContainerName;
        }
        else if (containerName == "dt" && endOfLine)
        {
            newContainerName = "dd";
        }
        else if (containerName == "dd" && endOfLine)
        {
            newContainerName = "dt";
        }
        else
        {
            newContainerName = containerName;
        }

        var newContainer = CreateElement(newContainerName);
        foreach (var attribute in containerElement.Attributes.ToArray())
        {
            newContainer.SetAttributeNS(attribute.NamespaceUri, attribute.Name, attribute.Value);
        }

        newContainer.RemoveAttribute("id");
        InsertAfter(newContainer, containerElement);
        var containedNodes = new HashSet<Node>(GetAllContainedNodes(newLineRange), ReferenceEqualityComparer.Instance);
        var fragment = newLineRange.ExtractContents();
        foreach (var descendant in GetDescendants(fragment))
        {
            if (descendant is Element element && !containedNodes.Contains(descendant))
            {
                element.RemoveAttribute("id");
            }
        }

        newContainer.AppendChild(fragment);
        Node lastContainer = containerElement;
        while (IsProhibitedParagraphChild(lastContainer.LastChild))
        {
            lastContainer = lastContainer.LastChild!;
        }

        Node lastNewContainer = newContainer;
        while (IsProhibitedParagraphChild(lastNewContainer.LastChild))
        {
            lastNewContainer = lastNewContainer.LastChild!;
        }

        // The specification leaves the white space of the two sides of the split as it is: a space that ends the first
        // container or begins the new one would collapse. As in the browsers, it becomes a no-break space
        ProtectBoundarySpace(lastContainer.LastChild, atEnd: true);
        ProtectBoundarySpace(lastNewContainer.FirstChild, atEnd: false);

        // The specification moves the start of the new line out of the inline elements that the caret begins or ends
        // (<p><b>foo[]</b></p> gives <p><b>foo</b></p><p><br></p>): the line that becomes empty has no inline formatting.
        // As in the browsers, the inline elements of the caret are cloned in the empty line, around its br
        // (<p><b>foo</b></p><p><b><br></b></p>)
        if (!HasVisibleChild(lastContainer))
        {
            if (!lastContainer.HasChildNodes())
            {
                CloneInlineFormatting(lastContainer, startFormattingAncestors);
            }

            if (!HasVisibleChild(lastContainer))
            {
                lastContainer.AppendChild(CreateElement("br"));
            }
        }

        var caretContainer = endOfLine ? CloneInlineFormatting(lastNewContainer, formattingAncestors) : lastNewContainer;
        if (!HasVisibleChild(lastNewContainer))
        {
            lastNewContainer.AppendChild(CreateElement("br"));
        }

        // The specification collapses the selection at the start of the new container. As in the browsers, the caret is in
        // the inline elements that start it, so that the text typed next has their formatting
        if (!endOfLine)
        {
            while (caretContainer.FirstChild is Element child && !IsHtmlElement(child, "br") && IsEditable(child) && IsInlineNode(child) && !IsAtomicElement(child) && IsAllowedChild("br", child))
            {
                caretContainer = child;
            }
        }

        CollapseTo(caretContainer, 0);

        // The specification leaves the inline elements of the end of a line in the first container (<b>abc[]</b> gives
        // <p><b>abc</b></p><p><br></p>): the text typed in the new paragraph loses their formatting. The browsers keep
        // it (they clone the inline elements): the formatting of the caret is kept as overrides of the new caret
        // (not the formatting of a caret that is in a link: it would be the color and the underline of the link)
        if (endOfLine && linkIndex < 0)
        {
            SetCaretOverrides(caretFormatting, containerElement);
        }

        if (selectionFormatting is not null && range.Collapsed)
        {
            RestoreStatesAndValues(selectionFormatting);
        }

        return true;
    }

    /// <summary>The closest editable inclusive ancestor of the node that is a link (an a element with an href attribute).</summary>
    private static Element? GetLinkOf(Node node)
    {
        for (var current = node; current is not null && IsEditable(current); current = current.ParentNode)
        {
            if (current is Element element && IsHtmlElement(element, "a") && element.HasAttribute("href"))
                return element;
        }

        return null;
    }

    /// <summary>
    /// The name of the state override (it is not a command) of a caret that follows text inserted in a link in place of a
    /// selection: the text typed at this caret stays in the link, even at its boundaries. As the other overrides, it is
    /// unset when the selection changes.
    /// </summary>
    private const string TypingInLinkOverride = "\0typing-in-link";

    /// <summary>The name of the state override (it is not a command) of the caret that a deletion has just set.</summary>
    private const string CaretAfterDeletionOverride = "\0after-deletion";

    /// <summary>
    /// Moves a caret that is at the start or at the end of a link (an a element with an href attribute that has contents)
    /// before or after the link: the specification inserts the text in the link; as in the browsers, the text typed at the
    /// boundaries of a link is not a part of it. The inline formatting of the caret that does not come from the link or
    /// from its ancestors (e.g. a b element in the link) is kept as overrides.
    /// </summary>
    private void MoveCaretOutOfLinkBoundary()
    {
        var range = ActiveRange;
        var node = range.StartContainer;
        var offset = (int)range.StartOffset;

        // A caret whose container is the link itself is in the link (WPT editing/other/link-boundaries-insertion.html, as
        // in Firefox), except the caret that a deletion left there
        if (!range.Collapsed || (IsHtmlElement(node, "a") && _state.GetStateOverride(range, CaretAfterDeletionOverride) != true))
            return;

        var atStart = offset == 0;
        var atEnd = offset == NodeLength(node);
        Element? link = null;
        for (var current = node; current is not null && IsEditable(current) && (atStart || atEnd); current = current.ParentNode)
        {
            if (current is Element element && IsHtmlElement(element, "a") && element.HasAttribute("href"))
            {
                link = element;
                break;
            }

            atStart &= current.PreviousSibling is null;
            atEnd &= current.NextSibling is null;
        }

        if (link is null || atStart == atEnd || link.ParentNode is not { } parent || !InSameEditingHost(link, parent))
            return;

        var formatting = RecordCaretStatesAndValues();
        CollapseTo(parent, atStart ? NodeIndex(link) : 1 + NodeIndex(link));
        SetCaretOverrides(formatting, link);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#the-inserttext-command</summary>
    private bool InsertText(string value, bool replacesSelection = false)
    {
        // The specification canonicalizes the white space around a caret even when there is nothing to insert. As in the
        // browsers, the insertion of no text at a caret does nothing
        var deletesSelection = !ActiveRange.Collapsed;
        if (value.Length == 0 && !deletesSelection)
            return true;

        // The text that replaces a selection is in a link when the whole selection is in this link
        replacesSelection |= deletesSelection && GetLinkOf(ActiveRange.StartContainer) is { } link && ReferenceEquals(link, GetLinkOf(ActiveRange.EndContainer));
        DeleteSelection(stripWrappers: false);
        var range = ActiveRange;
        if (!IsEditableOrEditingHost(range.StartContainer))
            return true;

        // The text cannot be in a void element (e.g. a caret set in a br or in an hr): as in the browsers, it is inserted
        // before the element
        while (IsHtmlElement(range.StartContainer, VoidElementNames) && IsEditable(range.StartContainer) && range.StartContainer.ParentNode is { } voidParent)
        {
            CollapseTo(voidParent, NodeIndex(range.StartContainer));
        }

        // A link whose contents were deleted with a selection that goes out of it is removed (the text is not a part of it)
        if (deletesSelection && !replacesSelection && range.Collapsed && GetLinkOf(range.StartContainer) is { ParentNode: { } linkParent } emptyLink && !HasVisibleChild(emptyLink) && InSameEditingHost(emptyLink, linkParent))
        {
            CollapseTo(linkParent, NodeIndex(emptyLink));
            RemoveNode(emptyLink);
        }

        // The characters are inserted one by one
        if (value.Length > 1 && !(value.Length == 2 && char.IsSurrogatePair(value[0], value[1])))
        {
            for (var i = 0; i < value.Length;)
            {
                var length = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
                InsertText(value.Substring(i, length), replacesSelection && i == 0);
                i += length;
            }

            return true;
        }

        if (value.Length == 0)
            return true;

        if (value == "\n")
        {
            RunAction("insertparagraph", "");
            return true;
        }

        // The text that replaces a selection in a link, and the text typed after it, are parts of the link
        var keepsCaretInLink = replacesSelection || _state.GetStateOverride(range, TypingInLinkOverride) == true;
        if (!keepsCaretInLink)
        {
            // A caret that follows inline elements that end with a link (e.g. after a deletion has joined two blocks) is
            // the caret of the end of the link, as in the browsers
            if (range.StartContainer is Element && GetCaretNode(range) is Text caretText && GetLinkOf(caretText) is { } caretLink && !caretLink.IsInclusiveAncestorOf(range.StartContainer))
            {
                CollapseTo(caretText, NodeLength(caretText));
            }

            MoveCaretOutOfLinkBoundary();
        }

        var node = range.StartContainer;
        var offset = (int)range.StartOffset;
        if (ChildAt(node, offset - 1) is Text previousText)
        {
            node = previousText;
            offset = NodeLength(node);
        }

        if (ChildAt(node, offset) is Text nextText)
        {
            node = nextText;
            offset = 0;
        }

        var overrides = RecordCurrentOverrides();
        CollapseTo(node, offset);
        CanonicalizeWhitespace(node, offset);
        node = range.StartContainer;
        offset = (int)range.StartOffset;
        if (node is Text text)
        {
            // As in the browsers, the line feed that only keeps an empty last line of a block rendered (white-space: pre,
            // pre-wrap or pre-line) is replaced with the text, as the br of an empty line is
            if (offset + 1 == text.Length && text.Data[offset] == '\n' && (offset == 0 ? FindLineItem(text, forward: false) != LineItem.Content : text.Data[offset - 1] == '\n')
                && PreservesLineFeeds(GetWhiteSpace(text.ParentNode)) && FindLineItem(text, forward: true) == LineItem.Boundary)
            {
                text.DeleteData((uint)offset, 1);
            }

            text.InsertData((uint)offset, value);
            SetRange(node, offset, node, offset + value.Length);
        }
        else
        {
            // The specification only removes the br that is the sole child of the node. As in the browsers, the br that
            // keeps the line of the caret rendered (the last br of a block, next to the caret) is replaced with the text
            if (ChildAt(node, offset) is { } nextChild && IsCollapsedLineBreak(nextChild) && IsEditable(nextChild))
            {
                node.RemoveChild(nextChild);
            }
            else if (ChildAt(node, offset - 1) is { } previousChild && IsCollapsedLineBreak(previousChild) && IsEditable(previousChild) && offset == NodeLength(node))
            {
                node.RemoveChild(previousChild);
            }

            var newText = _document.CreateTextNode(value);
            range.InsertNode(newText);
            SetRange(newText, 0, newText, value.Length);
        }

        RemovePaddingLineBreakAfterInsertedText();
        RestoreStatesAndValues(overrides);
        CanonicalizeWhitespace(range.StartContainer, (int)range.StartOffset, fixCollapsedSpace: false);
        CanonicalizeWhitespace(range.EndContainer, (int)range.EndOffset, fixCollapsedSpace: false);

        // The specification creates a link for the URL or the email address that ends before a white space that is typed
        // (https://w3c.github.io/editing/docs/execCommand/#autolink). No browser does it, and the tests expect no link
        range.Collapse(toStart: false);
        if (keepsCaretInLink)
        {
            _state.SetStateOverride(range, TypingInLinkOverride, state: true);
        }

        return true;
    }
}
