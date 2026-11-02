using System.Collections;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Collections;

public sealed class BTree<T> : IReadOnlyCollection<T>
{
    private const int MaxKeysPerNode = 15;
    private const int MinKeysPerNode = MaxKeysPerNode / 2;
    private const int MaxChildrenPerNode = MaxKeysPerNode + 1;

    private readonly IComparer<T> _comparer;
    private Node? _root;
    private int _version;

    public BTree(IComparer<T>? comparer = null)
    {
        _comparer = comparer ?? Comparer<T>.Default;
    }

    public int Count { get; private set; }

    public bool Add(T item)
    {
        if (_root is null)
        {
            _root = new Node(isLeaf: true);
            _root.SetKey(0, item);
            _root.KeyCount = 1;
            Count = 1;
            _version++;
            return true;
        }

        if (_root.KeyCount == MaxKeysPerNode)
        {
            var newRoot = new Node(isLeaf: false);
            newRoot.SetChild(0, _root);
            newRoot.ChildCount = 1;
            SplitChild(newRoot, childIndex: 0);
            _root = newRoot;
            _version++; // The tree changed even if the item already exists
        }

        if (!InsertNonFull(_root, item))
            return false;

        Count++;
        _version++;
        return true;
    }

    public bool Remove(T item)
    {
        if (_root is null)
            return false;

        // Removing rebalances the nodes on the path to the item, even when the item does not exist
        var removed = RemoveCore(_root, item);
        if (_root.KeyCount is 0)
        {
            _root = _root.IsLeaf ? null : _root.GetChild(0);
            _version++;
        }

        if (removed)
        {
            Count--;
            _version++;
        }

        return removed;
    }

    public void Clear()
    {
        _root = null;
        Count = 0;
        _version++;
    }

    public bool Contains(T item)
    {
        var node = _root;
        while (node is not null)
        {
            var index = FindKeyIndex(node, item, _comparer, out var found);
            if (found)
                return true;

            if (node.IsLeaf)
                return false;

            node = node.GetChild(index);
        }

        return false;
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    private bool InsertNonFull(Node node, T item)
    {
        var keyIndex = FindKeyIndex(node, item, _comparer, out var found);
        if (found)
            return false;

        if (node.IsLeaf)
        {
            for (var i = node.KeyCount; i > keyIndex; i--)
            {
                node.SetKey(i, node.GetKey(i - 1));
            }

            node.SetKey(keyIndex, item);
            node.KeyCount++;
            return true;
        }

        var child = node.GetChild(keyIndex);
        if (child is null)
            throw new InvalidOperationException("Invalid B-tree state");

        if (child.KeyCount == MaxKeysPerNode)
        {
            SplitChild(node, keyIndex);
            _version++; // The tree changed even if the item already exists
            var compareResult = _comparer.Compare(item, node.GetKey(keyIndex));
            if (compareResult is 0)
                return false;

            if (compareResult > 0)
            {
                keyIndex++;
            }
        }

        var nextChild = node.GetChild(keyIndex);
        if (nextChild is null)
            throw new InvalidOperationException("Invalid B-tree state");

        return InsertNonFull(nextChild, item);
    }

    private bool RemoveCore(Node node, T item)
    {
        while (true)
        {
            var keyIndex = FindKeyIndex(node, item, _comparer, out var found);
            if (node.IsLeaf)
            {
                if (!found)
                    return false;

                RemoveKeyAt(node, keyIndex);
                return true;
            }

            if (found)
            {
                var leftChild = GetRequiredChild(node, keyIndex);
                var rightChild = GetRequiredChild(node, keyIndex + 1);
                if (leftChild.KeyCount > MinKeysPerNode)
                {
                    // Replace the item with its predecessor, then remove the predecessor from the left subtree
                    var predecessor = GetMaxKey(leftChild);
                    node.SetKey(keyIndex, predecessor);
                    item = predecessor;
                    node = leftChild;
                }
                else if (rightChild.KeyCount > MinKeysPerNode)
                {
                    // Replace the item with its successor, then remove the successor from the right subtree
                    var successor = GetMinKey(rightChild);
                    node.SetKey(keyIndex, successor);
                    item = successor;
                    node = rightChild;
                }
                else
                {
                    // Both children have the minimum number of keys: merge them around the item, and remove it from the merged node
                    MergeChildren(node, keyIndex);
                    node = leftChild;
                }

                continue;
            }

            // Make sure the child has more than the minimum number of keys before going down, so removing a key from it is always possible
            var child = GetRequiredChild(node, keyIndex);
            if (child.KeyCount <= MinKeysPerNode)
            {
                if (keyIndex > 0 && GetRequiredChild(node, keyIndex - 1).KeyCount > MinKeysPerNode)
                {
                    BorrowFromLeftSibling(node, keyIndex);
                }
                else if (keyIndex < node.KeyCount && GetRequiredChild(node, keyIndex + 1).KeyCount > MinKeysPerNode)
                {
                    BorrowFromRightSibling(node, keyIndex);
                }
                else
                {
                    if (keyIndex == node.KeyCount)
                    {
                        keyIndex--;
                    }

                    MergeChildren(node, keyIndex);
                }

                child = GetRequiredChild(node, keyIndex);
            }

            node = child;
        }
    }

    private static T GetMaxKey(Node node)
    {
        while (!node.IsLeaf)
        {
            node = GetRequiredChild(node, node.ChildCount - 1);
        }

        return node.GetKey(node.KeyCount - 1);
    }

    private static T GetMinKey(Node node)
    {
        while (!node.IsLeaf)
        {
            node = GetRequiredChild(node, 0);
        }

        return node.GetKey(0);
    }

    private void MergeChildren(Node parent, int keyIndex)
    {
        // Move the key of the parent and all the content of the right child into the left child
        var leftChild = GetRequiredChild(parent, keyIndex);
        var rightChild = GetRequiredChild(parent, keyIndex + 1);

        var leftKeyCount = leftChild.KeyCount;
        leftChild.SetKey(leftKeyCount, parent.GetKey(keyIndex));
        for (var i = 0; i < rightChild.KeyCount; i++)
        {
            leftChild.SetKey(leftKeyCount + 1 + i, rightChild.GetKey(i));
        }

        if (!leftChild.IsLeaf)
        {
            var leftChildCount = leftChild.ChildCount;
            for (var i = 0; i < rightChild.ChildCount; i++)
            {
                leftChild.SetChild(leftChildCount + i, rightChild.GetChild(i));
            }

            leftChild.ChildCount += rightChild.ChildCount;
        }

        leftChild.KeyCount += rightChild.KeyCount + 1;

        RemoveKeyAt(parent, keyIndex);
        RemoveChildAt(parent, keyIndex + 1);
        _version++;
    }

    private void BorrowFromLeftSibling(Node parent, int childIndex)
    {
        // Rotate right: the last key of the left sibling goes to the parent, and the key of the parent goes to the child
        var child = GetRequiredChild(parent, childIndex);
        var sibling = GetRequiredChild(parent, childIndex - 1);

        InsertKeyAt(child, 0, parent.GetKey(childIndex - 1));
        parent.SetKey(childIndex - 1, sibling.GetKey(sibling.KeyCount - 1));
        RemoveKeyAt(sibling, sibling.KeyCount - 1);

        if (!child.IsLeaf)
        {
            InsertChildAt(child, 0, GetRequiredChild(sibling, sibling.ChildCount - 1));
            RemoveChildAt(sibling, sibling.ChildCount - 1);
        }

        _version++;
    }

    private void BorrowFromRightSibling(Node parent, int childIndex)
    {
        // Rotate left: the first key of the right sibling goes to the parent, and the key of the parent goes to the child
        var child = GetRequiredChild(parent, childIndex);
        var sibling = GetRequiredChild(parent, childIndex + 1);

        InsertKeyAt(child, child.KeyCount, parent.GetKey(childIndex));
        parent.SetKey(childIndex, sibling.GetKey(0));
        RemoveKeyAt(sibling, 0);

        if (!child.IsLeaf)
        {
            InsertChildAt(child, child.ChildCount, GetRequiredChild(sibling, 0));
            RemoveChildAt(sibling, 0);
        }

        _version++;
    }

    private static void InsertKeyAt(Node node, int index, T key)
    {
        for (var i = node.KeyCount; i > index; i--)
        {
            node.SetKey(i, node.GetKey(i - 1));
        }

        node.SetKey(index, key);
        node.KeyCount++;
    }

    private static void RemoveKeyAt(Node node, int index)
    {
        for (var i = index; i < node.KeyCount - 1; i++)
        {
            node.SetKey(i, node.GetKey(i + 1));
        }

        node.KeyCount--;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            node.SetKey(node.KeyCount, default!);
        }
    }

    private static void InsertChildAt(Node node, int index, Node child)
    {
        for (var i = node.ChildCount; i > index; i--)
        {
            node.SetChild(i, node.GetChild(i - 1));
        }

        node.SetChild(index, child);
        node.ChildCount++;
    }

    private static void RemoveChildAt(Node node, int index)
    {
        for (var i = index; i < node.ChildCount - 1; i++)
        {
            node.SetChild(i, node.GetChild(i + 1));
        }

        node.ChildCount--;
        node.SetChild(node.ChildCount, null);
    }

    private static Node GetRequiredChild(Node node, int index)
    {
        var child = node.GetChild(index);
        if (child is null)
            throw new InvalidOperationException("Invalid B-tree state");

        return child;
    }

    private static void SplitChild(Node parent, int childIndex)
    {
        var leftNode = parent.GetChild(childIndex);
        if (leftNode is null)
            throw new InvalidOperationException("Invalid B-tree state");

        var rightNode = new Node(leftNode.IsLeaf);
        var median = leftNode.GetKey(MinKeysPerNode);
        var rightNodeKeyCount = MaxKeysPerNode - MinKeysPerNode - 1;

        for (var i = 0; i < rightNodeKeyCount; i++)
        {
            var sourceIndex = i + MinKeysPerNode + 1;
            rightNode.SetKey(i, leftNode.GetKey(sourceIndex));
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                leftNode.SetKey(sourceIndex, default!);
            }
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            leftNode.SetKey(MinKeysPerNode, default!);
        }

        rightNode.KeyCount = rightNodeKeyCount;
        leftNode.KeyCount = MinKeysPerNode;

        if (!leftNode.IsLeaf)
        {
            var rightNodeChildCount = rightNodeKeyCount + 1;
            for (var i = 0; i < rightNodeChildCount; i++)
            {
                var sourceIndex = i + MinKeysPerNode + 1;
                var child = leftNode.GetChild(sourceIndex);
                rightNode.SetChild(i, child);
                leftNode.SetChild(sourceIndex, null);
            }

            rightNode.ChildCount = rightNodeChildCount;
            leftNode.ChildCount = MinKeysPerNode + 1;
        }

        for (var i = parent.ChildCount; i > childIndex + 1; i--)
        {
            parent.SetChild(i, parent.GetChild(i - 1));
        }

        parent.SetChild(childIndex + 1, rightNode);
        parent.ChildCount++;

        for (var i = parent.KeyCount; i > childIndex; i--)
        {
            parent.SetKey(i, parent.GetKey(i - 1));
        }

        parent.SetKey(childIndex, median);
        parent.KeyCount++;
    }

    private static int FindKeyIndex(Node node, T item, IComparer<T> comparer, out bool found)
    {
        var low = 0;
        var high = node.KeyCount - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var compareResult = comparer.Compare(item, node.GetKey(middle));
            switch (compareResult)
            {
                case < 0:
                    high = middle - 1;
                    break;
                case > 0:
                    low = middle + 1;
                    break;
                default:
                    found = true;
                    return middle;
            }
        }

        found = false;
        return low;
    }

    private sealed class Node
    {
        private KeyStorage _keys;
        private ChildStorage _children;

        public Node(bool isLeaf)
        {
            IsLeaf = isLeaf;
        }

        public bool IsLeaf { get; }
        public int KeyCount { get; set; }
        public int ChildCount { get; set; }

        public T GetKey(int index) => _keys[index];
        public void SetKey(int index, T value) => _keys[index] = value;

        public Node? GetChild(int index) => _children[index];
        public void SetChild(int index, Node? value) => _children[index] = value;

        [InlineArray(MaxKeysPerNode)]
        private struct KeyStorage
        {
            private T _element0;
        }

        [InlineArray(MaxChildrenPerNode)]
        private struct ChildStorage
        {
            private Node? _element0;
        }
    }

    public struct Enumerator : IEnumerator<T>, IEnumerator
    {
        private readonly BTree<T> _tree;
        private readonly int _version;
        private Stack<Frame>? _stack;
        private int _state;

        internal Enumerator(BTree<T> tree)
        {
            _tree = tree;
            _version = tree._version;
            _stack = tree._root is null ? null : new Stack<Frame>();
            _state = 0;
            Current = default!;

            if (tree._root is not null)
            {
                PushLeftPath(tree._root);
            }
        }

        public readonly void Dispose()
        {
        }

        public bool MoveNext()
        {
            if (_version != _tree._version)
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

            if (_stack is null || _stack.Count is 0)
            {
                _state = 2;
                Current = default!;
                return false;
            }

            while (_stack.Count > 0)
            {
                var frame = _stack.Pop();
                var node = frame.Node;
                var keyIndex = frame.KeyIndex;
                if (keyIndex >= node.KeyCount)
                {
                    continue;
                }

                _stack.Push(new Frame(node, keyIndex + 1));
                if (!node.IsLeaf)
                {
                    var child = node.GetChild(keyIndex + 1);
                    if (child is not null)
                    {
                        PushLeftPath(child);
                    }
                }

                _state = 1;
                Current = node.GetKey(keyIndex);
                return true;
            }

            _state = 2;
            Current = default!;
            return false;
        }

        public T Current { readonly get => field; private set; }

        readonly object? IEnumerator.Current
        {
            get
            {
                if (_state is 0 or 2)
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

                return Current;
            }
        }

        void IEnumerator.Reset()
        {
            if (_version != _tree._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }

            _state = 0;
            Current = default!;
            if (_tree._root is null)
            {
                _stack = null;
                return;
            }

            _stack ??= new Stack<Frame>();
            _stack.Clear();
            PushLeftPath(_tree._root);
        }

        private void PushLeftPath(Node node)
        {
            while (true)
            {
                _stack!.Push(new Frame(node, keyIndex: 0));
                if (node.IsLeaf)
                {
                    return;
                }

                var child = node.GetChild(0);
                if (child is null)
                {
                    throw new InvalidOperationException("Invalid B-tree state");
                }

                node = child;
            }
        }

        private readonly struct Frame(Node node, int keyIndex)
        {
            public Node Node { get; } = node;
            public int KeyIndex { get; } = keyIndex;
        }
    }
}
