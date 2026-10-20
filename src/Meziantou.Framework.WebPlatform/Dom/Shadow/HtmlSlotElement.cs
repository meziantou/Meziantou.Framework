namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Represents the options of <c>assignedNodes()</c> and <c>assignedElements()</c>.</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#assignednodesoptions"/>
[IdlInterface("AssignedNodesOptions", "https://html.spec.whatwg.org/multipage/scripting.html#assignednodesoptions")]
public sealed class AssignedNodesOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the nodes assigned to nested slots are returned instead of the slots.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-assignednodesoptions-flatten"/>
    [IdlMember("flatten", "https://html.spec.whatwg.org/multipage/scripting.html#dom-assignednodesoptions-flatten")]
    public bool Flatten { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/scripting.html#the-slot-element</summary>
public partial class HtmlSlotElement
{
    partial void OnCreated()
    {
        NodeDocument.HasSlots = true;
    }

    /// <summary>The slot's name (https://dom.spec.whatwg.org/#slot-name).</summary>
    internal string SlotName { get; private set; } = "";

    /// <summary>https://dom.spec.whatwg.org/#slot-assigned-nodes</summary>
    internal List<Node> AssignedNodesList { get; set; } = [];

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#manually-assigned-nodes</summary>
    internal List<Node> ManuallyAssignedNodes { get; private set; } = [];

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assignednodes</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assignednodes"/>
    [IdlMember("assignedNodes", "https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assignednodes")]
    public IReadOnlyList<Node> AssignedNodes(AssignedNodesOptions? options = null)
    {
        if (options?.Flatten != true)
            return [.. AssignedNodesList];

        return Slots.FindFlattenedSlottables(this);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assignedelements</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assignedelements"/>
    [IdlMember("assignedElements", "https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assignedelements")]
    public IReadOnlyList<Element> AssignedElements(AssignedNodesOptions? options = null)
    {
        var nodes = options?.Flatten == true ? Slots.FindFlattenedSlottables(this) : AssignedNodesList;
        return nodes.OfType<Element>().ToArray();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assign</summary>
    /// <remarks>
    /// As in the browsers, the slottables are also assigned for the trees of the slots of the other trees that lose a node:
    /// their assigned nodes change, and they are signaled a slot change (the specification only runs "assign slottables for a
    /// tree" for the root of this slot, which leaves the assigned nodes of the previous slot of a node out of date).
    /// </remarks>
    /// <seealso href="https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assign"/>
    [IdlMember("assign", "https://html.spec.whatwg.org/multipage/scripting.html#dom-slot-assign")]
    public void Assign(params IReadOnlyList<Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        foreach (var node in ManuallyAssignedNodes)
        {
            node.GetOrCreateSlottable().ManualSlotAssignment = null;
        }

        var root = GetRoot();
        List<Node>? previousRoots = null;
        var nodesSet = new List<Node>();
        foreach (var node in nodes)
        {
            if (!Slots.IsSlottable(node))
                throw new TypeErrorException("Only elements and text nodes can be assigned to a slot");

            var data = node.GetOrCreateSlottable();
            if (data.ManualSlotAssignment is { } previousSlot)
            {
                previousSlot.ManuallyAssignedNodes.Remove(node);
                var previousRoot = previousSlot.GetRoot();
                if (!ReferenceEquals(previousRoot, root) && (previousRoots is null || !previousRoots.Contains(previousRoot)))
                {
                    (previousRoots ??= []).Add(previousRoot);
                }
            }

            data.ManualSlotAssignment = this;
            if (!nodesSet.Contains(node))
            {
                nodesSet.Add(node);
            }
        }

        ManuallyAssignedNodes = nodesSet;
        Slots.AssignSlottablesForTree(root);
        if (previousRoots is not null)
        {
            foreach (var previousRoot in previousRoots)
            {
                Slots.AssignSlottablesForTree(previousRoot);
            }
        }

        // The flat tree changed without a mutation of the tree (::slotted(), :has-slotted, inheritance)
        NodeDocument.IncrementElementStateVersion();
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (localName == "name" && @namespace is null && Slots.ShouldUpdateName(oldValue, value))
        {
            SlotName = value ?? "";
            Slots.AssignSlottablesForTree(GetRoot());
        }
    }

    internal override void AdoptingSteps(Document oldDocument)
    {
        base.AdoptingSteps(oldDocument);
        NodeDocument.HasSlots = true;
    }
}
