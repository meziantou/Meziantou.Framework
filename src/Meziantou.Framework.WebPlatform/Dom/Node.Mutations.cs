using System.Diagnostics;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Mutation algorithms (https://dom.spec.whatwg.org/#mutation-algorithms).</summary>
public abstract partial class Node
{

    internal List<RegisteredObserver>? RegisteredObserverList => RareDataOrNull?.RegisteredObservers;

    internal IReadOnlyList<RegisteredObserver> RegisteredObservers => RareDataOrNull?.RegisteredObservers ?? (IReadOnlyList<RegisteredObserver>)[];

    internal void AddRegisteredObserver(RegisteredObserver observer)
    {
        (RareData.RegisteredObservers ??= []).Add(observer);
        NodeDocument.HasMutationObservers = true;
    }

    /// <summary>Called after registered observers were removed from the list.</summary>
    internal void UpdateObserverCount()
    {
        // The count is only an optimization to skip queuing mutation records when nothing ever observed mutations
    }


    /// <summary>The slottable state of an Element or Text node, if any.</summary>
    internal SlottableData? Slottable => RareDataOrNull?.Slottable;

    internal SlottableData GetOrCreateSlottable() => RareData.Slottable ??= new SlottableData();

    /// <summary>https://dom.spec.whatwg.org/#queue-a-tree-mutation-record</summary>
    private static void QueueTreeMutationRecord(Node target, IReadOnlyList<Node> addedNodes, IReadOnlyList<Node> removedNodes, Node? previousSibling, Node? nextSibling)
    {
        var document = target.NodeDocument;
        document.EditingRecorder?.RecordChildList(target, addedNodes, removedNodes, nextSibling);
        if (!document.HasMutationObservers)
            return;

        Debug.Assert(addedNodes.Count > 0 || removedNodes.Count > 0);
        MutationObserver.QueueMutationRecord("childList", target, null, null, null, addedNodes, removedNodes, previousSibling, nextSibling);
    }

    /// <summary>Queues a tree mutation record for one added or removed node; the lists are only created when there are observers.</summary>
    private static void QueueSingleNodeTreeMutationRecord(Node target, Node? addedNode, Node? removedNode, Node? previousSibling, Node? nextSibling)
    {
        var document = target.NodeDocument;
        if (!document.HasMutationObservers && document.EditingRecorder is null)
            return;

        QueueTreeMutationRecord(target, addedNode is null ? [] : [addedNode], removedNode is null ? [] : [removedNode], previousSibling, nextSibling);
    }

    /// <summary>Queues a mutation record for attributes or character data, when there are observers.</summary>
    internal static void QueueMutationRecordIfObserved(string type, Node target, string? name, string? @namespace, string? oldValue)
    {
        var document = target.NodeDocument;
        if (document.EditingRecorder is { } recorder)
        {
            if (target is CharacterData characterData)
            {
                recorder.RecordCharacterData(characterData, oldValue ?? "");
            }
            else if (target is Element element && name is not null)
            {
                recorder.RecordAttribute(element, name, @namespace, oldValue);
            }
        }

        if (!document.HasMutationObservers)
            return;

        MutationObserver.QueueMutationRecord(type, target, name, @namespace, oldValue, [], [], null, null);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-node-ensure-pre-insert-validity</summary>
    // childToExclude: the child of the "childrenToExclude" list (used by replace).
    // excludeAllChildren: whether "childrenToExclude" is the list of all the children (used by replaceChildren()).
    internal void EnsurePreInsertValidity(Node node, Node? child, Node? childToExclude = null, bool excludeAllChildren = false)
    {
        if (this is not (Document or DocumentFragment or Element))
            throw DomException.HierarchyRequest("The parent must be a Document, DocumentFragment or Element node");

        if (node.IsHostIncludingInclusiveAncestorOf(this))
            throw DomException.HierarchyRequest("The node is an ancestor of the parent");

        if (child is not null && !ReferenceEquals(child._parent, this))
            throw DomException.NotFound("The reference child is not a child of the parent");

        if (node is not (DocumentFragment or DocumentType or Element or CharacterData))
            throw DomException.HierarchyRequest("The node cannot be inserted");

        if (this is not Document)
        {
            if (node is DocumentType)
                throw DomException.HierarchyRequest("A doctype can only be inserted in a document");

            return;
        }

        if (node is Text)
            throw DomException.HierarchyRequest("A document cannot contain Text nodes");

        if (node is CharacterData)
            return;

        if (node is DocumentFragment fragment)
        {
            var elementCount = 0;
            for (var c = fragment._firstChild; c is not null; c = c._nextSibling)
            {
                if (c is Element)
                {
                    elementCount++;
                }
                else if (c is Text)
                {
                    throw DomException.HierarchyRequest("A document cannot contain Text nodes");
                }
            }

            if (elementCount > 1)
                throw DomException.HierarchyRequest("A document can have only one element child");

            if (elementCount == 0)
                return;
        }

        if (node is DocumentFragment or Element)
        {
            if ((!excludeAllChildren && HasChildOfTypeExcluding<Element>(childToExclude)) ||
                (child is not null && child.IsDocumentTypeFollowing()) ||
                (child is DocumentType && !excludeAllChildren && !ReferenceEquals(child, childToExclude)))
            {
                throw DomException.HierarchyRequest("A document can have only one element child, after the doctype");
            }

            return;
        }

        Debug.Assert(node is DocumentType);
        if ((!excludeAllChildren && HasChildOfTypeExcluding<DocumentType>(childToExclude)) ||
            (child is not null && child.IsElementPreceding()) ||
            (child is null && !excludeAllChildren && HasChildOfTypeExcluding<Element>(childToExclude)))
        {
            throw DomException.HierarchyRequest("A document can have only one doctype, before the document element");
        }
    }

    private bool HasChildOfTypeExcluding<T>(Node? excluded)
        where T : Node
    {
        for (var c = _firstChild; c is not null; c = c._nextSibling)
        {
            if (c is T && !ReferenceEquals(c, excluded))
                return true;
        }

        return false;
    }

    private bool IsDocumentTypeFollowing()
    {
        for (var sibling = _nextSibling; sibling is not null; sibling = sibling._nextSibling)
        {
            if (sibling is DocumentType)
                return true;
        }

        return false;
    }

    private bool IsElementPreceding()
    {
        for (var sibling = _previousSibling; sibling is not null; sibling = sibling._previousSibling)
        {
            if (sibling is Element)
                return true;
        }

        return false;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-node-pre-insert</summary>
    internal Node PreInsert(Node node, Node? child)
    {
        EnsurePreInsertValidity(node, child);
        var referenceChild = child;
        if (ReferenceEquals(referenceChild, node))
        {
            referenceChild = node._nextSibling;
        }

        Insert(node, referenceChild);
        return node;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-node-append</summary>
    internal Node AppendNode(Node node) => PreInsert(node, child: null);

    /// <summary>https://dom.spec.whatwg.org/#concept-node-insert</summary>
    internal void Insert(Node node, Node? child, bool suppressObservers = false)
    {
        using var scope = CEReactionsScope.Enter();

        // The nodes of a fragment, or null when a single node is inserted (the parser inserts nodes one by one: no list is allocated)
        List<Node>? nodes = null;
        if (node is DocumentFragment fragment)
        {
            if (fragment._firstChild is null)
                return;

            nodes = new List<Node>(fragment._childCount);
            for (var c = fragment._firstChild; c is not null; c = c._nextSibling)
            {
                nodes.Add(c);
            }

            foreach (var c in nodes)
            {
                fragment.RemoveCore(c, suppressObservers: true);
            }

            QueueTreeMutationRecord(fragment, [], nodes, null, null);
        }

        var count = nodes?.Count ?? 1;
        var document = NodeDocument;
        if (child is not null && document.HasLiveRanges)
        {
            document.UpdateLiveRangesForInsertion(this, child.GetIndex(), count);
        }

        var previousSibling = child is not null ? child._previousSibling : _lastChild;
        for (var i = 0; i < count; i++)
        {
            var n = nodes is null ? node : nodes[i];
            var hadParent = n._parent is not null;
            document.Adopt(n);

            // The unload event handlers of the child navigables of the node, which are unloaded before the node is removed
            // from its parent (see RemoveCore), may have inserted the node, or this node into it
            if (hadParent && (n._parent is not null || n.IsHostIncludingInclusiveAncestorOf(this) || (child is not null && !ReferenceEquals(child._parent, this))))
                continue;

            LinkChild(n, child);
            IdMap.UpdateSubtree(this, n, add: true);
            DocumentNamedItemMap.UpdateSubtree(this, n, add: true);
            if (IsConnected)
            {
                n.SetConnected(true);
            }

            if (document.HasSlots || n.NodeDocument.HasSlots)
            {
                Slots.OnInserted(this, n);
            }

            n.RunInsertionStepsForSubtree();
        }

        if (!suppressObservers)
        {
            if (nodes is null)
            {
                QueueSingleNodeTreeMutationRecord(this, node, removedNode: null, previousSibling, child);
            }
            else
            {
                QueueTreeMutationRecord(this, nodes, [], previousSibling, child);
            }
        }

        ChildrenChangedSteps(childrenInserted: true);

        // https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism: an inserted element may be the one that a
        // link rel=expect element waits for, and an inserted body element lets a render-blocked document be rendered
        if (document.ObservesInsertionsForRenderBlocking && IsConnected)
        {
            for (var i = 0; i < count; i++)
            {
                document.OnNodeInsertedForRenderBlocking(nodes is null ? node : nodes[i]);
            }
        }

        // Post-connection steps
        if (IsConnected)
        {
            if (nodes is null && node._firstChild is null && node is not Element { AttachedShadowRoot: not null })
            {
                node.PostConnectionSteps();
            }
            else
            {
                var staticNodeList = new List<Node>();
                for (var i = 0; i < count; i++)
                {
                    foreach (var descendant in (nodes is null ? node : nodes[i]).WalkShadowIncludingInclusiveDescendants())
                    {
                        staticNodeList.Add(descendant);
                    }
                }

                foreach (var n in staticNodeList)
                {
                    if (n.IsConnected)
                    {
                        n.PostConnectionSteps();
                    }
                }
            }
        }

        NodeDocument.EventLoop?.OnOperationCompleted();
    }

    private void RunInsertionStepsForSubtree()
    {
        // Fast path: the parser mostly inserts leaf nodes
        if (_firstChild is null && this is not Element { AttachedShadowRoot: not null })
        {
            InsertionSteps();
            if (_isConnected)
            {
                ConnectedCustomElementSteps();
            }

            return;
        }

        var previousRoot = s_insertionRoot;
        s_insertionRoot = this;
        try
        {
            foreach (var node in WalkShadowIncludingInclusiveDescendants())
            {
                node.InsertionSteps();
                if (node._isConnected)
                {
                    node.ConnectedCustomElementSteps();
                }
            }
        }
        finally
        {
            s_insertionRoot = previousRoot;
        }
    }

    /// <summary>The root of the subtree whose insertion steps are running (null while a leaf node is inserted).</summary>
    [ThreadStatic]
    private static Node? s_insertionRoot;

    /// <summary>
    /// Gets whether the insertion steps that are running were run because this node was inserted, rather than one of its
    /// ancestors (the root of the subtree inserted by the insert algorithm).
    /// </summary>
    internal bool IsInsertionRoot => s_insertionRoot is not { } root || ReferenceEquals(root, this) || !root.IsShadowIncludingInclusiveAncestorOf(this);

    /// <summary>The steps of the insert algorithm for the connected inclusive descendants (https://dom.spec.whatwg.org/#concept-node-insert, steps 7.7.3 and 7.7.4).</summary>
    private void ConnectedCustomElementSteps()
    {
        if (this is Element element)
        {
            if (element.IdValue is not null && NodeDocument.HasFormAttributeCustomElements)
            {
                NodeDocument.OnIdChangedForFormAttributeCustomElements();
            }

            // A custom element adopted into a document without registry has no registry, but its connectedCallback is still
            // invoked, as in browsers and the web-platform-tests (the insert algorithm only enqueues it when the element has
            // a registry, but the remove algorithm always enqueues the disconnectedCallback)
            if (element.IsCustom)
            {
                CustomElementReactions.EnqueueCallbackReaction(element, CustomElementCallbackKind.Connected, []);
            }

            if (element.CustomElementRegistry is not { } registry)
                return;

            if (registry.IsScoped)
            {
                registry.AddScopedDocument(NodeDocument);
            }

            if (!element.IsCustom && registry.HasAnyDefinition)
            {
                CustomElementReactions.TryUpgrade(element);
            }
        }
        else if (this is ShadowRoot { CustomElementRegistryStorage: { IsScoped: true } shadowRootRegistry })
        {
            shadowRootRegistry.AddScopedDocument(NodeDocument);
        }
    }

    /// <summary>Enumerates the shadow-including inclusive descendants in shadow-including tree order (https://dom.spec.whatwg.org/#concept-shadow-including-tree-order).</summary>
    internal IEnumerable<Node> ShadowIncludingInclusiveDescendants()
    {
        var stack = new Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            // Children are pushed in reverse order so they are visited in tree order; the shadow root is visited before the children
            for (var child = node._lastChild; child is not null; child = child._previousSibling)
            {
                stack.Push(child);
            }

            if (node is Element { AttachedShadowRoot: { } shadowRoot })
            {
                stack.Push(shadowRoot);
            }
        }
    }

    /// <summary>
    /// Enumerates the shadow-including inclusive descendants in shadow-including tree order, like
    /// <see cref="ShadowIncludingInclusiveDescendants"/>, without allocation: the next node is found from the links of the
    /// current one, so the tree must not change during the enumeration.
    /// </summary>
    internal ShadowIncludingDescendantWalker WalkShadowIncludingInclusiveDescendants() => new(this);

    /// <summary>Enumerates the shadow-including inclusive descendants of a node (see <see cref="WalkShadowIncludingInclusiveDescendants"/>).</summary>
    internal struct ShadowIncludingDescendantWalker(Node root)
    {
        private Node? _current;
        private bool _started;

        public readonly Node Current => _current!;

        public readonly ShadowIncludingDescendantWalker GetEnumerator() => this;

        public bool MoveNext()
        {
            if (!_started)
            {
                _started = true;
                _current = root;
                return true;
            }

            var node = _current;
            if (node is null)
                return false;

            // The shadow root is visited before the children
            if (node is Element { AttachedShadowRoot: { } shadowRoot })
            {
                _current = shadowRoot;
                return true;
            }

            if (node._firstChild is { } firstChild)
            {
                _current = firstChild;
                return true;
            }

            while (!ReferenceEquals(node, root))
            {
                if (node._nextSibling is { } nextSibling)
                {
                    _current = nextSibling;
                    return true;
                }

                if (node is ShadowRoot { Host: var host })
                {
                    // The children of the host follow its shadow tree
                    if (host._firstChild is { } hostChild)
                    {
                        _current = hostChild;
                        return true;
                    }

                    node = host;
                    continue;
                }

                if (node._parent is not { } parent)
                    break;

                node = parent;
            }

            _current = null;
            return false;
        }
    }

    /// <summary>Appends a node without the pre-insertion validity checks (used by the parsers and the cloning algorithm).</summary>
    internal void AppendChildCore(Node node) => Insert(node, child: null);

    /// <summary>Inserts a node before <paramref name="child"/> without the pre-insertion validity checks (used by the parsers).</summary>
    internal void InsertBeforeCore(Node node, Node? child) => Insert(node, child);

    private void LinkChild(Node node, Node? child)
    {
        Debug.Assert(node._parent is null);
        node._parent = this;
        if (child is null)
        {
            node._previousSibling = _lastChild;
            node._nextSibling = null;
            if (_lastChild is not null)
            {
                _lastChild._nextSibling = node;
            }
            else
            {
                _firstChild = node;
            }

            _lastChild = node;
        }
        else
        {
            node._nextSibling = child;
            node._previousSibling = child._previousSibling;
            if (child._previousSibling is not null)
            {
                child._previousSibling._nextSibling = node;
            }
            else
            {
                _firstChild = node;
            }

            child._previousSibling = node;
        }

        _childCount++;
        if (RareDataOrNull is { } rareData)
        {
            rareData.ChildrenVersion++;
        }

        NodeDocument.IncrementTreeVersion(this, node, removed: false);
    }

    private void UnlinkChild(Node node)
    {
        var oldPreviousSibling = node._previousSibling;
        var oldNextSibling = node._nextSibling;
        if (oldPreviousSibling is not null)
        {
            oldPreviousSibling._nextSibling = oldNextSibling;
        }
        else
        {
            _firstChild = oldNextSibling;
        }

        if (oldNextSibling is not null)
        {
            oldNextSibling._previousSibling = oldPreviousSibling;
        }
        else
        {
            _lastChild = oldPreviousSibling;
        }

        node._parent = null;
        node._previousSibling = null;
        node._nextSibling = null;
        if (node is Element element)
        {
            // (the cached index is stamped with the version of the children of the parent: it must not be valid in another parent)
            element.ElementIndexStamp = 0;
        }

        _childCount--;
        if (RareDataOrNull is { } rareData)
        {
            rareData.ChildrenVersion++;
        }

        NodeDocument.IncrementTreeVersion(this, node, removed: true);
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-node-replace</summary>
    internal Node Replace(Node child, Node node)
    {
        using var ceReactions = CEReactionsScope.Enter();

        // The child navigables are unloaded before the checks, as their event handlers may change the tree (see RemoveCore)
        BrowsingContext.UnloadRemovedChildNavigables(child);
        EnsurePreInsertValidity(node, child, childToExclude: child);
        var referenceChild = child._nextSibling;
        if (ReferenceEquals(referenceChild, node))
        {
            referenceChild = node._nextSibling;
        }

        var previousSibling = child._previousSibling;
        IReadOnlyList<Node> removedNodes = [];
        NodeDocument.Adopt(node);
        if (child._parent is not null)
        {
            removedNodes = [child];
            RemoveCore(child, suppressObservers: true);
        }

        IReadOnlyList<Node> nodes = node is DocumentFragment ? [.. node.ChildNodesSnapshot()] : [node];
        Insert(node, referenceChild, suppressObservers: true);
        QueueTreeMutationRecord(this, nodes, removedNodes, previousSibling, referenceChild);
        return child;
    }

    private List<Node> ChildNodesSnapshot()
    {
        var result = new List<Node>(_childCount);
        for (var c = _firstChild; c is not null; c = c._nextSibling)
        {
            result.Add(c);
        }

        return result;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-node-replace-all</summary>
    internal void ReplaceAll(Node? node)
    {
        using var ceReactions = CEReactionsScope.Enter();

        // The child navigables are unloaded before the children are listed, as their event handlers may change the tree (see RemoveCore)
        BrowsingContext.UnloadRemovedChildNavigables(this, descendantsOnly: true);
        var removedNodes = ChildNodesSnapshot();
        List<Node> addedNodes = node switch
        {
            DocumentFragment fragment => fragment.ChildNodesSnapshot(),
            null => [],
            _ => [node],
        };

        var document = NodeDocument;
        document.BeginReplaceAllRemoval();
        try
        {
            foreach (var child in removedNodes)
            {
                if (ReferenceEquals(child._parent, this))
                {
                    RemoveCore(child, suppressObservers: true);
                }
            }
        }
        finally
        {
            document.EndReplaceAllRemoval();
        }

        if (node is not null)
        {
            Insert(node, child: null, suppressObservers: true);
        }

        if (addedNodes.Count > 0 || removedNodes.Count > 0)
        {
            QueueTreeMutationRecord(this, addedNodes, removedNodes, null, null);
        }
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-node-pre-remove</summary>
    internal Node PreRemove(Node child)
    {
        if (!ReferenceEquals(child._parent, this))
            throw DomException.NotFound("The node is not a child of this node");

        RemoveCore(child, suppressObservers: false);
        return child;
    }

    /// <summary>Removes this node from its parent, if any (https://dom.spec.whatwg.org/#concept-node-remove).</summary>
    internal void RemoveFromParent()
    {
        _parent?.RemoveCore(this, suppressObservers: false);
    }

    /// <summary>Kept for the callers that remove a known child.</summary>
    internal void RemoveChildCore(Node node, bool suppressObservers) => RemoveCore(node, suppressObservers);

    /// <summary>https://dom.spec.whatwg.org/#concept-node-remove (this is the parent of <paramref name="node"/>).</summary>
    private void RemoveCore(Node node, bool suppressObservers)
    {
        Debug.Assert(ReferenceEquals(node._parent, this));
        using var scope = CEReactionsScope.Enter();

        // The documents of the removed child navigables are unloaded before the node is removed, as in the browsers: the
        // node is not removed when their event handlers removed or moved it
        BrowsingContext.UnloadRemovedChildNavigables(node);
        if (!ReferenceEquals(node._parent, this))
            return;

        var document = NodeDocument;
        if (document.HasLiveRanges)
        {
            document.LiveRangePreRemoveSteps(node);
        }

        if (document.HasNodeIterators)
        {
            document.NodeIteratorPreRemoveSteps(node);
        }

        if (document.FocusedArea is not null || document.SequentialFocusNavigationStartingPoint is not null)
        {
            SequentialFocusNavigation.OnRemoving(document, node, isMove: false);
        }

        // The targets of the pointers are updated to their nearest ancestor that stays connected
        if (document.HasPointerTargets)
        {
            document.BrowsingContext?.Top.InputOrNull?.OnRemoving(node);
        }

        var oldPreviousSibling = node._previousSibling;
        var oldNextSibling = node._nextSibling;
        IdMap.UpdateSubtree(this, node, add: false);
        DocumentNamedItemMap.UpdateSubtree(this, node, add: false);
        UnlinkChild(node);
        if (document.HasSlots)
        {
            Slots.OnRemoved(this, node);
        }

        if (node._isConnected)
        {
            node.SetConnected(false);
        }

        node.RemovingSteps(isSubtreeRoot: true, this);

        // https://html.spec.whatwg.org/multipage/infrastructure.html#node-remove-focus-fixup: when the focused area of the
        // document is removed, the viewport becomes the focused area (no event is fired). The sequential focus navigation
        // starting point is the position of the removed node (see SequentialFocusNavigation.OnRemoving).
        if (document.FocusedArea is { } focusedArea && node.IsShadowIncludingInclusiveAncestorOf(focusedArea))
        {
            document.FocusedArea = null;
        }

        var isParentConnected = IsConnected;
        if (isParentConnected && node is Element { IsCustom: true } customElement)
        {
            CustomElementReactions.EnqueueCallbackReaction(customElement, CustomElementCallbackKind.Disconnected, []);
        }

        if (node._firstChild is not null || node is Element { AttachedShadowRoot: not null })
        {
            foreach (var descendant in node.ShadowIncludingInclusiveDescendants())
            {
                if (!ReferenceEquals(descendant, node))
                {
                    descendant.RemovingSteps(isSubtreeRoot: false, this);
                    if (isParentConnected && descendant is Element { IsCustom: true } customDescendant)
                    {
                        CustomElementReactions.EnqueueCallbackReaction(customDescendant, CustomElementCallbackKind.Disconnected, []);
                    }
                }
            }
        }

        // https://fullscreen.spec.whatwg.org/#removing-steps
        if (document.FullscreenFlagCount > 0)
        {
            FullscreenModel.OnRemoved(document, node);
        }

        // https://w3c.github.io/pointerlock/#requirements: the pointer lock is exited when its target is disconnected
        if (isParentConnected && document.BrowsingContext?.Top is { PointerLockTarget: not null } pointerLockContext)
        {
            PointerLockModel.OnRemoved(pointerLockContext, node);
        }

        // https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#reset-the-form-owner: an element with an ID is removed from the document
        if (isParentConnected && document.HasFormAttributeCustomElements && ContainsElementWithId(node))
        {
            document.OnIdChangedForFormAttributeCustomElements();
        }

        if (document.HasMutationObservers)
        {
            // Transient registered observers
            for (Node? inclusiveAncestor = this; inclusiveAncestor is not null; inclusiveAncestor = inclusiveAncestor._parent)
            {
                if (inclusiveAncestor.RareDataOrNull?.RegisteredObservers is not { } registeredObservers)
                    continue;

                foreach (var registered in registeredObservers.ToArray())
                {
                    if (registered.Options.Subtree)
                    {
                        node.AddRegisteredObserver(new TransientRegisteredObserver(registered.Observer, registered.Options, registered));
                        registered.Observer.AddTransientNode(node);
                    }
                }
            }

            if (!suppressObservers)
            {
                QueueSingleNodeTreeMutationRecord(this, addedNode: null, node, oldPreviousSibling, oldNextSibling);
            }
        }
        else if (!suppressObservers && document.EditingRecorder is { } recorder)
        {
            recorder.RecordChildList(this, [], [node], oldNextSibling);
        }

        ChildrenChangedSteps(childrenInserted: false);
    }

    private static bool ContainsElementWithId(Node node)
    {
        for (Node? current = node; current is not null; current = current.GetNextInTreeOrder(node))
        {
            if (current is Element { IdValue: not null })
                return true;
        }

        return false;
    }

    /// <summary>https://dom.spec.whatwg.org/#move</summary>
    internal void Move(Node node, Node? child)
    {
        var newParent = this;
        if (!ReferenceEquals(newParent.GetShadowIncludingRoot(), node.GetShadowIncludingRoot()))
            throw DomException.HierarchyRequest("The node and the new parent must be in the same tree");

        if (node.IsHostIncludingInclusiveAncestorOf(newParent))
            throw DomException.HierarchyRequest("The node is an ancestor of the new parent");

        if (child is not null && !ReferenceEquals(child._parent, newParent))
            throw DomException.NotFound("The reference child is not a child of the new parent");

        if (node is not (Element or CharacterData))
            throw DomException.HierarchyRequest("Only elements and character data nodes can be moved");

        if (node is Text && newParent is Document)
            throw DomException.HierarchyRequest("A document cannot contain Text nodes");

        if (newParent is Document && node is Element &&
            (newParent.HasChildOfTypeExcluding<Element>(excluded: null) || child is DocumentType || (child is not null && child.IsDocumentTypeFollowing())))
        {
            throw DomException.HierarchyRequest("A document can have only one element child, after the doctype");
        }

        using var scope = CEReactionsScope.Enter();
        var oldParent = node._parent!;
        var document = NodeDocument;
        if (document.HasLiveRanges)
        {
            document.LiveRangePreRemoveSteps(node);
        }

        if (document.HasNodeIterators)
        {
            document.NodeIteratorPreRemoveSteps(node);
        }

        if (document.SequentialFocusNavigationStartingPoint is not null)
        {
            SequentialFocusNavigation.OnRemoving(document, node, isMove: true);
        }

        var oldPreviousSibling = node._previousSibling;
        var oldNextSibling = node._nextSibling;
        IdMap.UpdateSubtree(oldParent, node, add: false);
        DocumentNamedItemMap.UpdateSubtree(oldParent, node, add: false);
        oldParent.UnlinkChild(node);
        if (document.HasSlots)
        {
            Slots.OnMovedOut(oldParent, node);
        }

        // The pointer of the resource selection algorithm of a media element is updated as if the node was removed, then inserted
        (oldParent as HtmlMediaElement)?.UpdateSourcePointer();

        if (child is not null && document.HasLiveRanges)
        {
            document.UpdateLiveRangesForInsertion(newParent, child.GetIndex(), 1);
        }

        var newPreviousSibling = child is not null ? child._previousSibling : newParent._lastChild;
        newParent.LinkChild(node, child);

        // The node stays connected, but it may move between the document tree and a shadow tree (see GetRoot)
        if (node._isConnected && node._isInShadowTree != (newParent is ShadowRoot || newParent._isInShadowTree))
        {
            node.SetConnected(true);
        }

        IdMap.UpdateSubtree(newParent, node, add: true);
        DocumentNamedItemMap.UpdateSubtree(newParent, node, add: true);
        if (document.HasSlots)
        {
            Slots.OnInserted(newParent, node);
        }

        foreach (var inclusiveDescendant in node.ShadowIncludingInclusiveDescendants().ToList())
        {
            inclusiveDescendant.MovingSteps(ReferenceEquals(inclusiveDescendant, node), oldParent);
            if (inclusiveDescendant is Element { IsCustom: true } customElement && newParent.IsConnected)
            {
                CustomElementReactions.EnqueueCallbackReaction(customElement, CustomElementCallbackKind.ConnectedMove, []);
            }
        }

        QueueSingleNodeTreeMutationRecord(oldParent, addedNode: null, node, oldPreviousSibling, oldNextSibling);
        QueueSingleNodeTreeMutationRecord(newParent, node, removedNode: null, newPreviousSibling, child);

        // The move algorithm does not run the children changed steps, but the children of both parents changed (e.g. the
        // style block of a style element must be updated, as browsers do). No node is inserted: a moved node does not run
        // the steps of an insertion (e.g. a script element does not prepare its script).
        oldParent.ChildrenChangedSteps(childrenInserted: false);
        if (!ReferenceEquals(oldParent, newParent))
        {
            newParent.ChildrenChangedSteps(childrenInserted: false);
        }

        NodeDocument.EventLoop?.OnOperationCompleted();
    }

    /// <summary>The insertion steps (https://dom.spec.whatwg.org/#concept-node-insert-ext).</summary>
    internal virtual void InsertionSteps()
    {
    }

    /// <summary>The removing steps (https://dom.spec.whatwg.org/#concept-node-remove-ext).</summary>
    internal virtual void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
    }

    /// <summary>The moving steps (https://dom.spec.whatwg.org/#concept-node-move-ext).</summary>
    internal virtual void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
    }

    /// <summary>The post-connection steps (https://dom.spec.whatwg.org/#concept-node-post-connection-ext).</summary>
    internal virtual void PostConnectionSteps()
    {
    }

    /// <summary>The children changed steps (https://dom.spec.whatwg.org/#concept-node-children-changed-ext).</summary>
    /// <param name="childrenInserted">Whether the steps run because nodes were inserted, rather than removed.</param>
    internal virtual void ChildrenChangedSteps(bool childrenInserted)
    {
    }

    /// <summary>The child text content change steps (e.g. for style and title elements).</summary>
    internal virtual void ChildTextContentChanged()
    {
    }

    /// <summary>The adopting steps (https://dom.spec.whatwg.org/#concept-node-adopt-ext).</summary>
    internal virtual void AdoptingSteps(Document oldDocument)
    {
    }
}
