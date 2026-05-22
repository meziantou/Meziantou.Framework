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
        }

        if (!InsertNonFull(_root, item))
            return false;

        Count++;
        _version++;
        return true;
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
