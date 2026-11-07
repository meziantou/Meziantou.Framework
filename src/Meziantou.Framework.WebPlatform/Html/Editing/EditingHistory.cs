using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The undo history of the editable content of a document (the <c>undo</c> and <c>redo</c> commands, which the editing
/// specification leaves to the user agent): each command and each edit of the user is a transaction that records the
/// mutations of the DOM it makes (as the mutation records of the DOM: the child lists, the character data and the
/// attributes) and the selection before and after it. Undoing a transaction reverts its mutations in the reverse order
/// and restores the selection; redoing it applies them again. A new transaction clears the transactions that can be redone.
/// </summary>
/// <remarks>
/// The mutations made by the scripts are not recorded and do not clear the history, as in the browsers: a transaction is
/// reverted on the nodes it recorded, wherever they are now (a node the transaction inserted is removed from its current
/// parent; a node it removed is inserted before its recorded next sibling when this sibling is still a child of the
/// recorded parent, else appended to the parent).
/// </remarks>
internal sealed class EditingHistory
{
    /// <summary>The maximum number of transactions that are kept (the oldest ones are dropped).</summary>
    private const int MaximumDepth = 1000;

    private readonly List<EditingTransaction> _undo = [];
    private readonly List<EditingTransaction> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Starts the recording of the mutations of the document in a new transaction. Returns null when a transaction is
    /// already recording (a command run from a command: its mutations are part of the transaction that is recording).
    /// </summary>
    public static EditingTransaction? Begin(Document document)
    {
        if (document.EditingRecorder is not null)
            return null;

        var transaction = new EditingTransaction(GetSelection(document));
        document.EditingRecorder = transaction;
        return transaction;
    }

    /// <summary>Ends the recording of a transaction, and adds it to the history when it changed the document.</summary>
    public void Commit(Document document, EditingTransaction? transaction, Element? editingHost)
    {
        if (transaction is null)
            return;

        document.EditingRecorder = null;
        if (transaction.IsEmpty)
            return;

        transaction.EditingHost = editingHost;
        transaction.SelectionAfter = GetSelection(document);
        _redo.Clear();
        _undo.Add(transaction);
        if (_undo.Count > MaximumDepth)
        {
            _undo.RemoveAt(0);
        }
    }

    /// <summary>The transaction that the next undo reverts, or null.</summary>
    public EditingTransaction? PeekUndo() => _undo.Count > 0 ? _undo[^1] : null;

    /// <summary>The transaction that the next redo applies again, or null.</summary>
    public EditingTransaction? PeekRedo() => _redo.Count > 0 ? _redo[^1] : null;

    /// <summary>Reverts the last transaction. Returns the transaction, or null when there is none.</summary>
    public EditingTransaction? Undo(Document document)
    {
        if (_undo.Count == 0 || document.EditingRecorder is not null)
            return null;

        var transaction = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        transaction.Revert();
        _redo.Add(transaction);
        SetSelection(document, transaction.SelectionBefore);
        return transaction;
    }

    /// <summary>Applies again the last transaction that was reverted. Returns the transaction, or null when there is none.</summary>
    public EditingTransaction? Redo(Document document)
    {
        if (_redo.Count == 0 || document.EditingRecorder is not null)
            return null;

        var transaction = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        transaction.Apply();
        _undo.Add(transaction);
        SetSelection(document, transaction.SelectionAfter);
        return transaction;
    }

    private static (Node AnchorNode, uint AnchorOffset, Node FocusNode, uint FocusOffset)? GetSelection(Document document)
    {
        if (document.SelectionOrNull is not { RangeOrNull: not null, AnchorNode: { } anchorNode, FocusNode: { } focusNode } selection)
            return null;

        return (anchorNode, selection.AnchorOffset, focusNode, selection.FocusOffset);
    }

    /// <summary>Restores a recorded selection, when its boundary points are still in the document (the offsets are clamped to the lengths of the nodes).</summary>
    private static void SetSelection(Document document, (Node AnchorNode, uint AnchorOffset, Node FocusNode, uint FocusOffset)? recorded)
    {
        if (recorded is not { } points || document.GetSelection() is not { } selection)
            return;

        if (!document.IsInclusiveAncestorOf(points.AnchorNode) || !document.IsInclusiveAncestorOf(points.FocusNode))
            return;

        selection.SetBaseAndExtent(points.AnchorNode, Math.Min(points.AnchorOffset, Dom.Range.GetNodeLength(points.AnchorNode)), points.FocusNode, Math.Min(points.FocusOffset, Dom.Range.GetNodeLength(points.FocusNode)));
    }
}

/// <summary>A transaction of the <see cref="EditingHistory"/>: the mutations of a command or of an edit of the user.</summary>
internal sealed class EditingTransaction((Node AnchorNode, uint AnchorOffset, Node FocusNode, uint FocusOffset)? selectionBefore)
{
    private readonly List<Step> _steps = [];

    public bool IsEmpty => _steps.Count == 0;

    /// <summary>The editing host the transaction edited (the target of the input events of its undo and redo).</summary>
    public Element? EditingHost { get; set; }

    public (Node AnchorNode, uint AnchorOffset, Node FocusNode, uint FocusOffset)? SelectionBefore { get; } = selectionBefore;

    public (Node AnchorNode, uint AnchorOffset, Node FocusNode, uint FocusOffset)? SelectionAfter { get; set; }

    /// <summary>Records the nodes added to and removed from the children of a node (the nodes are already added and removed).</summary>
    public void RecordChildList(Node parent, IReadOnlyList<Node> addedNodes, IReadOnlyList<Node> removedNodes, Node? nextSibling)
        => _steps.Add(new Step(parent, [.. addedNodes], [.. removedNodes], nextSibling, Name: null, Namespace: null, IsAttribute: false) { Value = null });

    /// <summary>Records the data of a CharacterData node, before it changes.</summary>
    public void RecordCharacterData(CharacterData node, string oldValue)
        => _steps.Add(new Step(node, Added: null, Removed: null, NextSibling: null, Name: null, Namespace: null, IsAttribute: false) { Value = oldValue });

    /// <summary>Records the value of an attribute of an element (null when it has no such attribute), before it changes.</summary>
    public void RecordAttribute(Element element, string localName, string? @namespace, string? oldValue)
        => _steps.Add(new Step(element, Added: null, Removed: null, NextSibling: null, localName, @namespace, IsAttribute: true) { Value = oldValue });

    /// <summary>Reverts the mutations, from the last one to the first one.</summary>
    public void Revert()
    {
        for (var i = _steps.Count - 1; i >= 0; i--)
        {
            var step = _steps[i];
            if (step.Added is not null)
            {
                Replace(step.Target, toRemove: step.Added, toInsert: step.Removed!, step.NextSibling);
            }
            else
            {
                SwapValue(step);
            }
        }
    }

    /// <summary>Applies the mutations again, from the first one to the last one.</summary>
    public void Apply()
    {
        foreach (var step in _steps)
        {
            if (step.Added is not null)
            {
                Replace(step.Target, toRemove: step.Removed!, toInsert: step.Added, step.NextSibling);
            }
            else
            {
                SwapValue(step);
            }
        }
    }

    private static void Replace(Node parent, Node[] toRemove, Node[] toInsert, Node? nextSibling)
    {
        foreach (var node in toRemove)
        {
            node.ParentNode?.RemoveChild(node);
        }

        if (toInsert.Length == 0)
            return;

        // The scripts may have moved or removed the reference child since the transaction
        var reference = nextSibling is not null && ReferenceEquals(nextSibling.ParentNode, parent) ? nextSibling : null;
        foreach (var node in toInsert)
        {
            try
            {
                parent.InsertBefore(node, reference);
            }
            catch (DomException)
            {
                // The node cannot be a child of the parent anymore (e.g. the scripts moved the parent into the node)
            }
        }
    }

    /// <summary>Exchanges the recorded value with the current value of the character data or of the attribute.</summary>
    private static void SwapValue(Step step)
    {
        if (step.IsAttribute)
        {
            var element = (Element)step.Target;
            var current = element.GetAttributeNS(step.Namespace, step.Name!);
            if (step.Value is null)
            {
                element.RemoveAttributeNS(step.Namespace, step.Name!);
            }
            else
            {
                element.SetAttributeNS(step.Namespace, step.Name!, step.Value);
            }

            step.Value = current;
        }
        else
        {
            var characterData = (CharacterData)step.Target;
            var current = characterData.Data;
            characterData.Data = step.Value ?? "";
            step.Value = current;
        }
    }

    private sealed record Step(Node Target, Node[]? Added, Node[]? Removed, Node? NextSibling, string? Name, string? Namespace, bool IsAttribute)
    {
        public string? Value { get; set; }
    }
}
