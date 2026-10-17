namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>State of a slottable (https://dom.spec.whatwg.org/#light-tree-slotables).</summary>
internal sealed class SlottableData
{
    /// <summary>https://dom.spec.whatwg.org/#slotable-name</summary>
    public string Name { get; set; } = "";

    /// <summary>https://dom.spec.whatwg.org/#slotable-assigned-slot</summary>
    public HtmlSlotElement? AssignedSlot { get; set; }

    /// <summary>https://dom.spec.whatwg.org/#slottable-manual-slot-assignment</summary>
    public HtmlSlotElement? ManualSlotAssignment { get; set; }
}

/// <summary>Slot algorithms (https://dom.spec.whatwg.org/#shadow-tree-slots).</summary>
internal static class Slots
{
    public static bool IsSlottable(Node node) => node is Element or Text;

    /// <summary>https://dom.spec.whatwg.org/#find-a-slot</summary>
    public static HtmlSlotElement? FindASlot(Node slottable, bool open = false)
    {
        if (slottable.ParentNode is not Element { AttachedShadowRoot: { } shadow })
            return null;

        if (open && shadow.Mode != ShadowRootMode.Open)
            return null;

        if (shadow.SlotAssignment == SlotAssignmentMode.Manual)
        {
            for (var node = shadow.FirstChild; node is not null; node = node.GetNextInTreeOrder(shadow))
            {
                if (node is HtmlSlotElement slot && slot.ManuallyAssignedNodes.Contains(slottable))
                    return slot;
            }

            return null;
        }

        var name = slottable.Slottable?.Name ?? "";
        for (var node = shadow.FirstChild; node is not null; node = node.GetNextInTreeOrder(shadow))
        {
            if (node is HtmlSlotElement slot && slot.SlotName == name)
                return slot;
        }

        return null;
    }

    /// <summary>https://dom.spec.whatwg.org/#find-slotables</summary>
    public static List<Node> FindSlottables(HtmlSlotElement slot)
    {
        var result = new List<Node>();
        if (slot.GetRoot() is not ShadowRoot root)
            return result;

        var host = root.Host;
        if (root.SlotAssignment == SlotAssignmentMode.Manual)
        {
            foreach (var slottable in slot.ManuallyAssignedNodes)
            {
                if (ReferenceEquals(slottable.ParentNode, host))
                {
                    result.Add(slottable);
                }
            }

            return result;
        }

        for (var child = host.FirstChild; child is not null; child = child.NextSibling)
        {
            if (IsSlottable(child) && ReferenceEquals(FindASlot(child), slot))
            {
                result.Add(child);
            }
        }

        return result;
    }

    /// <summary>https://dom.spec.whatwg.org/#find-flattened-slotables</summary>
    public static List<Node> FindFlattenedSlottables(HtmlSlotElement slot)
    {
        var result = new List<Node>();
        if (slot.GetRoot() is not ShadowRoot)
            return result;

        var slottables = FindSlottables(slot);
        if (slottables.Count == 0)
        {
            for (var child = slot.FirstChild; child is not null; child = child.NextSibling)
            {
                if (IsSlottable(child))
                {
                    slottables.Add(child);
                }
            }
        }

        foreach (var node in slottables)
        {
            if (node is HtmlSlotElement nestedSlot && nestedSlot.GetRoot() is ShadowRoot)
            {
                result.AddRange(FindFlattenedSlottables(nestedSlot));
            }
            else
            {
                result.Add(node);
            }
        }

        return result;
    }

    /// <summary>https://dom.spec.whatwg.org/#assign-slotables</summary>
    public static void AssignSlottables(HtmlSlotElement slot)
    {
        var slottables = FindSlottables(slot);
        if (!slottables.SequenceEqual(slot.AssignedNodesList))
        {
            SignalSlotChange(slot);
        }

        foreach (var previous in slot.AssignedNodesList)
        {
            // Nodes that are no longer assigned to the slot
            if (previous.Slottable is { } data && ReferenceEquals(data.AssignedSlot, slot) && !slottables.Contains(previous))
            {
                data.AssignedSlot = null;
            }
        }

        slot.AssignedNodesList = slottables;
        foreach (var slottable in slottables)
        {
            slottable.GetOrCreateSlottable().AssignedSlot = slot;
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#assign-slotables-for-a-tree</summary>
    public static void AssignSlottablesForTree(Node root)
    {
        if (!root.NodeDocument.HasSlots)
            return;

        var slots = new List<HtmlSlotElement>();
        for (Node? node = root; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlSlotElement slot)
            {
                slots.Add(slot);
            }
        }

        foreach (var slot in slots)
        {
            AssignSlottables(slot);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#assign-a-slot</summary>
    public static void AssignASlot(Node slottable)
    {
        var slot = FindASlot(slottable);
        if (slot is not null)
        {
            AssignSlottables(slot);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#signal-a-slot-change</summary>
    public static void SignalSlotChange(HtmlSlotElement slot)
    {
        // The slotchange events of the documents without a browsing context are not fired
        if (slot.NodeDocument.EventLoop is not { } eventLoop)
            return;

        if (!eventLoop.SignalSlots.Contains(slot))
        {
            eventLoop.SignalSlots.Add(slot);
        }

        MutationObserver.QueueMutationObserverMicrotask(eventLoop);
    }

    /// <summary>The slot-related steps of the insert and move algorithms, for a node inserted into <paramref name="parent"/>.</summary>
    public static void OnInserted(Node parent, Node node)
    {
        // "If parent is a shadow host whose shadow root's slot assignment is "named" and node is a slottable, then assign a slot
        // for node." As in the browsers, a node that was manually assigned to a slot before its insertion into the host is
        // assigned too (https://dom.spec.whatwg.org/#find-a-slot handles the manual assignment; WPT
        // shadow-dom/slot-fallback-content-006.html)
        if (parent is Element { AttachedShadowRoot: not null } && IsSlottable(node))
        {
            AssignASlot(node);
        }

        if (parent.GetRoot() is ShadowRoot && parent is HtmlSlotElement slot && slot.AssignedNodesList.Count == 0)
        {
            SignalSlotChange(slot);
        }

        AssignSlottablesForTree(node.GetRoot());
    }

    /// <summary>The slot-related steps of the remove algorithm.</summary>
    public static void OnRemoved(Node parent, Node node)
    {
        if (node.Slottable?.AssignedSlot is { } assignedSlot)
        {
            AssignSlottables(assignedSlot);
        }

        if (parent.GetRoot() is ShadowRoot && parent is HtmlSlotElement slot && slot.AssignedNodesList.Count == 0)
        {
            SignalSlotChange(slot);
        }

        if (HasInclusiveDescendantSlot(node))
        {
            AssignSlottablesForTree(parent.GetRoot());
            AssignSlottablesForTree(node);
        }
    }

    /// <summary>The slot-related steps of the move algorithm when the node is removed from its old parent.</summary>
    public static void OnMovedOut(Node oldParent, Node node) => OnRemoved(oldParent, node);

    private static bool HasInclusiveDescendantSlot(Node node)
    {
        for (Node? current = node; current is not null; current = current.GetNextInTreeOrder(node))
        {
            if (current is HtmlSlotElement)
                return true;
        }

        return false;
    }

    /// <summary>The attribute change steps updating the name of a slot or a slottable (https://dom.spec.whatwg.org/#slot-name).</summary>
    public static bool ShouldUpdateName(string? oldValue, string? value)
    {
        if (value == oldValue)
            return false;

        if (value is null && oldValue is { Length: 0 })
            return false;

        if (value is { Length: 0 } && oldValue is null)
            return false;

        return true;
    }
}
