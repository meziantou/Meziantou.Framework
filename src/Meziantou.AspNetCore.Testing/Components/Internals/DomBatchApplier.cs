using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components.RenderTree;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// Applies the render batches of the renderer to the DOM of a WebPlatform document, as <c>BrowserRenderer.ts</c> of
/// Blazor does in a browser.
/// </summary>
internal sealed class DomBatchApplier(Document document, EventDelegator eventDelegator)
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string MathMLNamespace = "http://www.w3.org/1998/Math/MathML";

    private readonly Dictionary<int, LogicalContainer> _componentContainers = [];

    // The value of a <select> element, applied once its options are inserted
    private readonly Dictionary<Element, string?> _pendingSelectValues = [];

    public Document Document { get; } = document;

    public void AttachRootComponent(int componentId, Element host)
    {
        while (host.FirstChild is { } child)
        {
            host.RemoveChild(child);
        }

        _componentContainers[componentId] = new LogicalElement(host);
    }

    public void DetachRootComponent(int componentId)
    {
        if (_componentContainers.Remove(componentId, out var container))
        {
            for (var i = container.Children.Count - 1; i >= 0; i--)
            {
                RemoveLogicalChild(container, i);
            }
        }
    }

    public bool TryGetComponentDomNodes(int componentId, List<Node> nodes)
    {
        if (!_componentContainers.TryGetValue(componentId, out var container))
            return false;

        foreach (var child in container.Children)
        {
            child.CollectDomNodes(nodes);
        }

        return true;
    }

    public void ApplyBatch(in RenderBatch batch)
    {
        var updatedComponents = batch.UpdatedComponents;
        for (var i = 0; i < updatedComponents.Count; i++)
        {
            var diff = updatedComponents.Array[i];
            if (!_componentContainers.TryGetValue(diff.ComponentId, out var container))
                throw new InvalidOperationException($"No element is currently associated with component {diff.ComponentId.ToString(CultureInfo.InvariantCulture)}");

            ApplyEdits(batch, diff.ComponentId, container, diff.Edits);
        }

        var disposedComponents = batch.DisposedComponentIDs;
        for (var i = 0; i < disposedComponents.Count; i++)
        {
            _componentContainers.Remove(disposedComponents.Array[i]);
        }

        var disposedEventHandlers = batch.DisposedEventHandlerIDs;
        for (var i = 0; i < disposedEventHandlers.Count; i++)
        {
            eventDelegator.RemoveListener(disposedEventHandlers.Array[i]);
        }
    }

    private void ApplyEdits(in RenderBatch batch, int componentId, LogicalContainer container, ArrayBuilderSegment<RenderTreeEdit> edits)
    {
        var referenceFrames = batch.ReferenceFrames.Array;
        var parent = container;
        List<(int From, int To)>? permutations = null;
        for (var i = 0; i < edits.Count; i++)
        {
            ref var edit = ref edits.Array[edits.Offset + i];
            switch (edit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    InsertFrame(componentId, parent, edit.SiblingIndex, referenceFrames, edit.ReferenceFrameIndex);
                    break;

                case RenderTreeEditType.RemoveFrame:
                    RemoveLogicalChild(parent, edit.SiblingIndex);
                    break;

                case RenderTreeEditType.SetAttribute:
                    if (parent.Children[edit.SiblingIndex] is LogicalElement { Element: var element })
                    {
                        ApplyAttribute(componentId, element, ref referenceFrames[edit.ReferenceFrameIndex]);
                    }
                    else
                    {
                        throw new InvalidOperationException("Cannot set attribute on non-element child");
                    }

                    break;

                case RenderTreeEditType.RemoveAttribute:
                    if (parent.Children[edit.SiblingIndex] is LogicalElement { Element: var elementToUpdate })
                    {
                        SetOrRemoveAttribute(elementToUpdate, edit.RemovedAttributeName!, value: null);
                    }

                    break;

                case RenderTreeEditType.UpdateText:
                    if (parent.Children[edit.SiblingIndex] is LogicalDomNode { Node: Text text })
                    {
                        text.Data = referenceFrames[edit.ReferenceFrameIndex].TextContent;
                    }
                    else
                    {
                        throw new InvalidOperationException("Cannot set text content on non-text child");
                    }

                    break;

                case RenderTreeEditType.UpdateMarkup:
                    RemoveLogicalChild(parent, edit.SiblingIndex);
                    InsertMarkup(parent, edit.SiblingIndex, referenceFrames[edit.ReferenceFrameIndex].MarkupContent);
                    break;

                case RenderTreeEditType.StepIn:
                    parent = (LogicalContainer)parent.Children[edit.SiblingIndex];
                    break;

                case RenderTreeEditType.StepOut:
                    if (parent is LogicalElement { Element: var steppedOutElement })
                    {
                        ApplyPendingSelectValue(steppedOutElement);
                    }

                    parent = parent.Parent!;
                    break;

                case RenderTreeEditType.PermutationListEntry:
                    (permutations ??= []).Add((edit.SiblingIndex, edit.MoveToSiblingIndex));
                    break;

                case RenderTreeEditType.PermutationListEnd:
                    if (permutations is not null)
                    {
                        Permute(parent, permutations);
                        permutations = null;
                    }

                    break;

                default:
                    throw new InvalidOperationException($"Unknown edit type: {edit.Type}");
            }
        }
    }

    private int InsertFrame(int componentId, LogicalContainer parent, int childIndex, RenderTreeFrame[] frames, int frameIndex)
    {
        ref var frame = ref frames[frameIndex];
        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
                InsertElement(componentId, parent, childIndex, frames, frameIndex);
                return 1;

            case RenderTreeFrameType.Text:
                InsertLogicalChild(new LogicalDomNode(Document.CreateTextNode(frame.TextContent)), parent, childIndex);
                return 1;

            case RenderTreeFrameType.Component:
                var componentContainer = new LogicalFragment();
                InsertLogicalChild(componentContainer, parent, childIndex);
                _componentContainers[frame.ComponentId] = componentContainer;
                return 1;

            case RenderTreeFrameType.Region:
                return InsertFrameRange(componentId, parent, childIndex, frames, frameIndex + 1, frameIndex + frame.RegionSubtreeLength);

            case RenderTreeFrameType.ElementReferenceCapture:
                if (parent is LogicalElement { Element: var element })
                {
                    element.SetAttribute(ElementReferences.GetAttributeName(frame.ElementReferenceCaptureId), "");
                }

                return 0;

            case RenderTreeFrameType.Markup:
                InsertMarkup(parent, childIndex, frame.MarkupContent);
                return 1;

            case RenderTreeFrameType.Attribute:
                throw new InvalidOperationException("Attribute frames should only be present as leading children of element frames.");

            default:
                // ComponentReferenceCapture, NamedEvent, ComponentRenderMode: nothing to render
                return 0;
        }
    }

    private int InsertFrameRange(int componentId, LogicalContainer parent, int childIndex, RenderTreeFrame[] frames, int startIndex, int endIndexExclusive)
    {
        var originalChildIndex = childIndex;
        for (var index = startIndex; index < endIndexExclusive; index++)
        {
            ref var frame = ref frames[index];
            var numChildrenInserted = InsertFrame(componentId, parent, childIndex, frames, index);
            childIndex += numChildrenInserted;

            // Skip over any descendants, since they are already dealt with recursively
            index += GetSubtreeLength(ref frame) - 1;
        }

        return childIndex - originalChildIndex;
    }

    private static int GetSubtreeLength(ref RenderTreeFrame frame) => frame.FrameType switch
    {
        RenderTreeFrameType.Element => frame.ElementSubtreeLength,
        RenderTreeFrameType.Component => frame.ComponentSubtreeLength,
        RenderTreeFrameType.Region => frame.RegionSubtreeLength,
        _ => 1,
    };

    private void InsertElement(int componentId, LogicalContainer parent, int childIndex, RenderTreeFrame[] frames, int frameIndex)
    {
        ref var frame = ref frames[frameIndex];
        var tagName = frame.ElementName;
        var parentElement = GetClosestDomElement(parent);
        var element = tagName switch
        {
            "svg" => Document.CreateElementNS(SvgNamespace, tagName),
            "math" => Document.CreateElementNS(MathMLNamespace, tagName),
            _ when parentElement is { NamespaceUri: SvgNamespace } && parentElement.LocalName != "foreignObject" => Document.CreateElementNS(SvgNamespace, tagName),
            _ when parentElement is { NamespaceUri: MathMLNamespace } => Document.CreateElementNS(MathMLNamespace, tagName),
            _ => Document.CreateElement(tagName),
        };

        var logicalElement = new LogicalElement(element);
        InsertLogicalChild(logicalElement, parent, childIndex);

        // Apply the attributes, then insert the child frames
        var descendantsEndIndexExclusive = frameIndex + frame.ElementSubtreeLength;
        for (var descendantIndex = frameIndex + 1; descendantIndex < descendantsEndIndexExclusive; descendantIndex++)
        {
            ref var descendantFrame = ref frames[descendantIndex];
            if (descendantFrame.FrameType == RenderTreeFrameType.Attribute)
            {
                ApplyAttribute(componentId, element, ref descendantFrame);
            }
            else
            {
                InsertFrameRange(componentId, logicalElement, 0, frames, descendantIndex, descendantsEndIndexExclusive);
                break;
            }
        }

        ApplyPendingSelectValue(element);
        if (element is HtmlOptionElement option)
        {
            TrySelectOptionFromParentValue(option);
        }
    }

    private void InsertMarkup(LogicalContainer parent, int childIndex, string markup)
    {
        var container = new LogicalFragment();
        InsertLogicalChild(container, parent, childIndex);

        var parentElement = GetClosestDomElement(parent);
        Node[] nodes;
        if (parentElement is { NamespaceUri: SvgNamespace })
        {
            var svg = Document.CreateElementNS(SvgNamespace, "svg");
            svg.InnerHtml = markup;
            nodes = [.. svg.ChildNodes];
        }
        else
        {
            var template = (HtmlTemplateElement)Document.CreateElement("template");
            template.InnerHtml = markup;
            nodes = [.. template.Content.ChildNodes];
        }

        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i].ParentNode?.RemoveChild(nodes[i]);
            InsertLogicalChild(new LogicalDomNode(Document.AdoptNode(nodes[i])), container, i);
        }
    }

    private void ApplyAttribute(int componentId, Element element, ref RenderTreeFrame frame)
    {
        var name = frame.AttributeName;
        if (frame.AttributeEventHandlerId != 0)
        {
            var eventName = StripOnPrefix(name);
            eventDelegator.SetListener(element, eventName, frame.AttributeEventHandlerId, componentId);
            return;
        }

        SetOrRemoveAttribute(element, name, GetAttributeValue(frame.AttributeValue));
    }

    private static string? GetAttributeValue(object? value) => value switch
    {
        null => null,
        string str => str,
        true => "",
        false => null,
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private void SetOrRemoveAttribute(Element element, string name, string? value)
    {
        // @onclick:preventDefault and @onclick:stopPropagation
        const string PreventDefaultPrefix = "__internal_preventDefault_";
        const string StopPropagationPrefix = "__internal_stopPropagation_";
        if (name.StartsWith(PreventDefaultPrefix, StringComparison.Ordinal))
        {
            eventDelegator.SetPreventDefault(element, StripOnPrefix(name[PreventDefaultPrefix.Length..]), value is not null);
            return;
        }

        if (name.StartsWith(StopPropagationPrefix, StringComparison.Ordinal))
        {
            eventDelegator.SetStopPropagation(element, StripOnPrefix(name[StopPropagationPrefix.Length..]), value is not null);
            return;
        }

        if (TryApplySpecialProperty(element, name, value))
            return;

        if (value is not null)
        {
            element.SetAttribute(name, value);
        }
        else
        {
            element.RemoveAttribute(name);
        }
    }

    // The attributes name the event as the directive does ("__internal_preventDefault_onclick" for "click")
    private static string StripOnPrefix(string name) => name.StartsWith("on", StringComparison.Ordinal) ? name[2..] : name;

    private bool TryApplySpecialProperty(Element element, string name, string? value)
    {
        switch (name)
        {
            case "value":
                switch (element)
                {
                    case HtmlInputElement input:
                        input.Value = value ?? "";
                        return true;

                    case HtmlTextAreaElement textArea:
                        textArea.Value = value ?? "";
                        return true;

                    case HtmlSelectElement select:
                        // The options may not be inserted yet: the value is applied again once they are
                        _pendingSelectValues[select] = value;
                        SetSelectValue(select, value);
                        return true;

                    case HtmlOptionElement option:
                        if (value is not null)
                        {
                            option.SetAttribute("value", value);
                        }
                        else
                        {
                            option.RemoveAttribute("value");
                        }

                        TrySelectOptionFromParentValue(option);
                        return true;
                }

                return false;

            case "checked" when element is HtmlInputElement input:
                input.Checked = value is not null;
                return true;

            default:
                return false;
        }
    }

    private void ApplyPendingSelectValue(Element element)
    {
        if (element is HtmlSelectElement select && _pendingSelectValues.TryGetValue(select, out var value))
        {
            SetSelectValue(select, value);
        }
    }

    private void TrySelectOptionFromParentValue(HtmlOptionElement option)
    {
        var select = option.ParentElement as HtmlSelectElement ?? (option.ParentElement?.ParentElement as HtmlSelectElement);
        if (select is not null && _pendingSelectValues.TryGetValue(select, out var value))
        {
            SetSelectValue(select, value);
        }
    }

    private static void SetSelectValue(HtmlSelectElement select, string? value)
    {
        if (select.Multiple && value is not null && value.StartsWith('[', StringComparison.Ordinal))
        {
            var values = System.Text.Json.JsonSerializer.Deserialize<string[]>(value) ?? [];
            foreach (var option in select.QuerySelectorAll<HtmlOptionElement>("option"))
            {
                option.Selected = values.Contains(option.Value, StringComparer.Ordinal);
            }

            return;
        }

        select.Value = value ?? "";
    }

    private static Element? GetClosestDomElement(LogicalContainer container)
    {
        LogicalContainer? current = container;
        while (current is not null)
        {
            if (current is LogicalElement element)
                return element.Element;

            current = current.Parent;
        }

        return null;
    }

    private static Node? FindNextDomSibling(LogicalContainer parent, int index)
    {
        while (true)
        {
            for (var i = index; i < parent.Children.Count; i++)
            {
                if (parent.Children[i].GetFirstDomNode() is { } node)
                    return node;
            }

            if (parent is LogicalElement || parent.Parent is not { } grandParent)
                return null;

            index = grandParent.Children.IndexOf(parent) + 1;
            parent = grandParent;
        }
    }

    private static void InsertLogicalChild(LogicalNode child, LogicalContainer parent, int index)
    {
        parent.Children.Insert(index, child);
        child.Parent = parent;
        InsertDomNodes(child, parent, index);
    }

    private static void InsertDomNodes(LogicalNode child, LogicalContainer parent, int index)
    {
        var domParent = GetClosestDomElement(parent) ?? throw new InvalidOperationException("The logical tree has no DOM parent");
        var before = FindNextDomSibling(parent, index + 1);
        var nodes = new List<Node>();
        child.CollectDomNodes(nodes);
        foreach (var node in nodes)
        {
            domParent.InsertBefore(node, before);
        }
    }

    private static void RemoveDomNodes(LogicalNode child)
    {
        var nodes = new List<Node>();
        child.CollectDomNodes(nodes);
        foreach (var node in nodes)
        {
            node.ParentNode?.RemoveChild(node);
        }
    }

    private static void RemoveLogicalChild(LogicalContainer parent, int index)
    {
        var child = parent.Children[index];
        RemoveDomNodes(child);
        parent.Children.RemoveAt(index);
        child.Parent = null;
    }

    private static void Permute(LogicalContainer parent, List<(int From, int To)> permutations)
    {
        // The permutation is a bijection between the source and the destination indexes: detach the nodes that move,
        // place them at their new logical indexes, then reinsert their DOM nodes from the last one (so the next sibling
        // of each node is already in the DOM)
        var moved = new (int To, LogicalNode Node)[permutations.Count];
        for (var i = 0; i < permutations.Count; i++)
        {
            moved[i] = (permutations[i].To, parent.Children[permutations[i].From]);
            RemoveDomNodes(moved[i].Node);
        }

        foreach (var (to, node) in moved)
        {
            parent.Children[to] = node;
        }

        Array.Sort(moved, (a, b) => b.To.CompareTo(a.To));
        foreach (var (to, node) in moved)
        {
            InsertDomNodes(node, parent, to);
        }
    }
}
