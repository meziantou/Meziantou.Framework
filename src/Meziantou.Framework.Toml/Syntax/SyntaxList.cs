using System;
using System.Collections;
using System.Collections.Generic;
using Tomlyn.Helpers;

namespace Tomlyn.Syntax
{
    /// <summary>
    /// Abstract list of <see cref="SyntaxNode"/>
    /// </summary>
    public abstract class SyntaxList : SyntaxNode
    {
        /// <summary>
        /// Gets the backing list of child nodes.
        /// </summary>
#pragma warning disable CA1002, CA1051 // List<T> is part of the Tomlyn public API
        protected readonly List<SyntaxNode> Children;
#pragma warning restore CA1002, CA1051

        protected SyntaxList() : base(SyntaxKind.List)
        {
            Children = new List<SyntaxNode>();
        }

        /// <inheritdoc />
        public sealed override int ChildrenCount => Children.Count;

        /// <inheritdoc />
        protected override SyntaxNode GetChildImpl(int index)
        {
            return Children[index];
        }

        /// <inheritdoc />
        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.Visit(this);
        }
    }

    /// <summary>
    /// Abstract list of <see cref="SyntaxNode"/>
    /// </summary>
    /// <typeparam name="TSyntaxNode">Type of the node</typeparam>
    public sealed class SyntaxList<TSyntaxNode> : SyntaxList, IEnumerable<TSyntaxNode> where TSyntaxNode : SyntaxNode
    {
        /// <summary>
        /// Creates an instance of <see cref="SyntaxList{TSyntaxNode}"/>
        /// </summary>
        public SyntaxList()
        {
        }

        /// <summary>
        /// Adds the specified node to this list.
        /// </summary>
        /// <param name="node">Node to add to this list</param>
        public void Add(TSyntaxNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.Parent != null) throw ThrowHelper.GetExpectingNoParentException();
            Children.Add(node);
            node.Parent = this;
        }

        /// <summary>
        /// Gets a typed child at the specified index.
        /// </summary>
        /// <param name="index">Index of the child.</param>
        /// <returns>The child node at the specified index.</returns>
        public new TSyntaxNode? GetChild(int index)
        {
            return (TSyntaxNode?)base.GetChild(index);
        }

        /// <inheritdoc />
        protected override SyntaxNode GetChildImpl(int index)
        {
            return Children[index];
        }

        /// <summary>
        /// Removes a node at the specified index.
        /// </summary>
        /// <param name="index">Index of the node to remove</param>
        public void RemoveChildAt(int index)
        {
            var node = Children[index];
            Children.RemoveAt(index);
            node.Parent = null;
        }

        /// <summary>
        /// Removes the specified node instance.
        /// </summary>
        /// <param name="node">Node instance to remove</param>
        public void RemoveChild(TSyntaxNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.Parent != this) throw new InvalidOperationException("The node is not part of this list");
            Children.Remove(node);
            node.Parent = null;
        }

        /// <summary>
        /// Gets the default enumerator.
        /// </summary>
        /// <returns>The enumerator of this list</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(Children);
        }

        IEnumerator<TSyntaxNode> IEnumerable<TSyntaxNode>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Enumerator of a <see cref="SyntaxList{TSyntaxNode}"/>
        /// </summary>
        public struct Enumerator : IEnumerator<TSyntaxNode>
        {
            private readonly List<SyntaxNode> _nodes;
            private int _index;

            /// <summary>
            /// Initialize an enumerator with a list of <see cref="SyntaxNode"/>
            /// </summary>
            /// <param name="nodes"></param>
#pragma warning disable CA1002 // List<T> is part of the Tomlyn public API
            public Enumerator(List<SyntaxNode> nodes)
#pragma warning restore CA1002
            {
                _nodes = nodes;
                _index = -1;
            }

            /// <inheritdoc />
            public bool MoveNext()
            {
                if (_index + 1 == _nodes.Count) return false;
                _index++;
                return true;
            }

            /// <inheritdoc />
            public void Reset()
            {
                _index = -1;
            }

            /// <inheritdoc />
            public TSyntaxNode Current
            {
                get
                {
                    if (_index < 0) throw new InvalidOperationException("MoveNext must be called before accessing Current");
                    return (TSyntaxNode)_nodes[_index];
                }
            }

            /// <inheritdoc />
            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }
}