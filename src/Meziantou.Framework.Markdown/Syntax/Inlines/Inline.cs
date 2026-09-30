// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.IO;
using System.Runtime.CompilerServices;

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;

namespace Meziantou.Framework.Markdown.Syntax.Inlines;

/// <summary>
/// Base class for all syntax tree inlines.
/// </summary>
/// <seealso cref="MarkdownObject" />
public abstract class Inline : MarkdownObject, IInline
{
    /// <summary>
    /// Initializes a new instance of the Inline class.
    /// </summary>
    protected Inline()
    {
        SetTypeKind(isInline: true, isContainer: false);
    }

    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The parameter only selects this overload")]
    private protected Inline(bool dummySkipTypeKind) { }

    private ContainerInline? _parent;

    /// <summary>
    /// Gets the parent container of this inline.
    /// </summary>
    public ContainerInline? Parent
    {
        get
        {
            var parent = _parent;
            if (parent?.ChildrenMovedTo is not null)
            {
                parent = FindMovedParent();
            }

            return parent;
        }
        internal set => _parent = value;
    }

    /// <summary>
    /// Gets the previous inline.
    /// </summary>
    public Inline? PreviousSibling { get; internal set; }

    /// <summary>
    /// Gets the next sibling inline.
    /// </summary>
    public Inline? NextSibling { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is closed.
    /// </summary>
    public bool IsClosed
    {
        get => IsClosedInternal;
        set
        {
            // The spare bit of a container tells whether it is in an engaged chain of open containers
            if (InternalSpareBit && IsContainerInline && IsClosedInternal != value)
            {
                InlineContainerChain.OnClosedChanged((ContainerInline)this);
            }

            IsClosedInternal = value;
        }
    }

    /// <summary>
    /// Inserts the specified inline after this instance.
    /// </summary>
    /// <param name="next">The inline to insert after this instance.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException">Inline has already a parent</exception>
    public void InsertAfter(Inline next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.Parent != null)
        {
            ThrowHelper.ArgumentException("Inline has already a parent", nameof(next));
        }

        var previousNext = NextSibling;
        if (previousNext != null)
        {
            previousNext.PreviousSibling = next;
        }

        next.PreviousSibling = this;
        next.NextSibling = previousNext;
        NextSibling = next;

        if (Parent != null)
        {
            Parent.OnChildInsert(next);
            next.Parent = Parent;
        }
    }

    /// <summary>
    /// Inserts the specified inline before this instance.
    /// </summary>
    /// <param name="previous">The inline previous to insert before this instance.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException">Inline has already a parent</exception>
    public void InsertBefore(Inline previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (previous.Parent != null)
        {
            ThrowHelper.ArgumentException("Inline has already a parent", nameof(previous));
        }

        var previousSibling = PreviousSibling;
        if (previousSibling != null)
        {
            previousSibling.NextSibling = previous;
        }

        PreviousSibling = previous;
        previous.PreviousSibling = previousSibling;
        previous.NextSibling = this;

        if (Parent != null)
        {
            Parent.OnChildInsert(previous);
            previous.Parent = Parent;
        }
    }

    /// <summary>
    /// Removes this instance from the current list and its parent
    /// </summary>
    public void Remove()
    {
        if (PreviousSibling != null)
        {
            PreviousSibling.NextSibling = NextSibling;
        }

        if (NextSibling != null)
        {
            NextSibling.PreviousSibling = PreviousSibling;
        }

        if (Parent != null)
        {
            Parent.OnChildRemove(this);

            PreviousSibling = null;
            NextSibling = null;
            Parent = null;
        }
    }

    /// <summary>
    /// Replaces this inline by the specified inline.
    /// </summary>
    /// <param name="inline">The inline.</param>
    /// <param name="copyChildren">if set to <c>true</c> the children of this instance are copied to the specified inline.</param>
    /// <returns>The last children</returns>
    /// <exception cref="ArgumentNullException">If inline is null</exception>
    public Inline ReplaceBy(Inline inline, bool copyChildren = true)
    {
        ArgumentNullException.ThrowIfNull(inline);

        // Save sibling
        var parent = Parent;
        var previousSibling = PreviousSibling;
        var nextSibling = NextSibling;
        Remove();

        if (previousSibling != null)
        {
            previousSibling.InsertAfter(inline);
        }
        else if (nextSibling != null)
        {
            nextSibling.InsertBefore(inline);
        }
        else if (parent != null)
        {
            parent.AppendChild(inline);
        }

        if (copyChildren && IsContainerInline)
        {
            var container = unsafe(Unsafe.As<ContainerInline>(this));
            ContainerInline? newContainer = inline.IsContainerInline && !inline.IsClosed
                ? unsafe(Unsafe.As<ContainerInline>(inline))
                : null;

            var child = container.FirstChild;
            var lastChild = inline;

            // A chain of open containers is notified once for all the children moved to the parent, instead of once for each
            var chainParent = newContainer is null && child is not null && inline.Parent is { IsInOpenChain: true } flaggedParent ? flaggedParent : null;
            var chainParentLastChild = chainParent?.LastChild;

            // The chain knows the anchors among the children of this container only while it is one of its containers
            var movedChildrenHaveAnchor = chainParent is not null ? InlineContainerChain.HasAnchorBeforeLastChild(container) : null;

            // The container left its parent, so a chain of open containers does not need to know about its children anymore
            if (parent is not null && container.IsInOpenChain)
            {
                InlineContainerChain.OnContainerDetached(container);
            }

            var inlineParent = inline.Parent;
            var destination = newContainer ?? inlineParent;
            if (child is not null && destination is not null && !ReferenceEquals(destination, container) && !ReferenceEquals(inlineParent, container) &&
                !container.IsInOpenChain && (newContainer is null || !newContainer.IsInOpenChain))
            {
                // Children accumulate in the unresolved delimiters that are replaced one after the other, so moving them one by
                // one would be quadratic. Their parent is updated when it is read.
                lastChild = container.LastChild!;
                destination.MoveChildrenFrom(container, newContainer is null ? inline : newContainer.LastChild);
            }
            else
            {
                chainParent?.IsInOpenChain = false;

                while (child != null)
                {
                    var nextChild = child.NextSibling;
                    child.Remove();
                    if (newContainer != null)
                    {
                        newContainer.AppendChild(child);
                    }
                    else
                    {
                        lastChild.InsertAfter(child);
                    }
                    lastChild = child;
                    child = nextChild;
                }

                chainParent?.IsInOpenChain = true;
            }

            if (chainParent is not null)
            {
                InlineContainerChain.OnChildrenMoved(chainParent, chainParentLastChild, inline.NextSibling!, lastChild, movedChildrenHaveAnchor);
            }

            return lastChild;
        }

        return inline;
    }

    // A container whose children were moved to another container by ReplaceBy stays the parent that its former children
    // reference, and forwards to the container that received them. Point the inline and the containers on the way to the
    // container at the end, so that each forwarding is followed once.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private ContainerInline FindMovedParent()
    {
        var parent = _parent!;
        while (parent.ChildrenMovedTo is { } movedTo)
        {
            parent = movedTo;
        }

        var container = _parent!;
        while (!ReferenceEquals(container, parent))
        {
            var next = container.ChildrenMovedTo!;
            container.ChildrenMovedTo = parent;
            container = next;
        }

        _parent = parent;
        return parent;
    }

    /// <summary>
    /// Determines whether this instance contains a parent of the specified type.
    /// </summary>
    /// <typeparam name="T">Type of the parent to check</typeparam>
    /// <returns><c>true</c> if this instance contains a parent of the specified type; <c>false</c> otherwise</returns>
    public bool ContainsParentOfType<T>() where T : Inline
    {
        var inline = this;
        while (inline != null)
        {
            var delimiter = inline as T;
            if (delimiter != null)
            {
                return true;
            }
            inline = inline.Parent;
        }
        return false;
    }

    /// <summary>
    /// Determines whether there is a sibling of the specified type among root-level siblings.
    /// This walks up to find the root container, then checks all siblings.
    /// </summary>
    /// <typeparam name="T">Type of the sibling to check</typeparam>
    /// <returns><c>true</c> if a sibling of the specified type exists; <c>false</c> otherwise</returns>
    public bool ContainsParentOrSiblingOfType<T>() where T : Inline
    {
        // First check parents (handles nested case)
        if (ContainsParentOfType<T>())
        {
            return true;
        }

        // Then check siblings at root level (handles flat case)
        // Find the root container
        var root = Parent;
        while (root?.Parent != null)
        {
            root = root.Parent;
        }

        if (root is not ContainerInline container)
        {
            return false;
        }

        // Walk siblings looking for the type
        var sibling = container.FirstChild;
        while (sibling != null)
        {
            if (sibling is T)
            {
                return true;
            }
            sibling = sibling.NextSibling;
        }

        return false;
    }

    /// <summary>
    /// Iterates on parents of the specified type.
    /// </summary>
    /// <typeparam name="T">Type of the parent to iterate over</typeparam>
    /// <returns>An enumeration on the parents of the specified type</returns>
    public IEnumerable<T> FindParentOfType<T>() where T : Inline
    {
        var inline = this;
        while (inline != null)
        {
            if (inline is T inlineOfT)
            {
                yield return inlineOfT;
            }
            inline = inline.Parent;
        }
    }

    /// <summary>
    /// Performs the first parent of type t operation.
    /// </summary>
    public T? FirstParentOfType<T>() where T : notnull, Inline
    {
        var inline = this;
        while (inline != null)
        {
            if (inline is T inlineOfT)
            {
                return inlineOfT;
            }
            inline = inline.Parent;
        }
        return null;
    }

    /// <summary>
    /// Performs the find best parent operation.
    /// </summary>
    public Inline FindBestParent()
    {
        var current = this;

        while (current.Parent != null || current.PreviousSibling != null)
        {
            if (current.Parent != null)
            {
                current = current.Parent;
                continue;
            }

            current = current.PreviousSibling!;
        }

        return current;
    }

    /// <summary>
    /// Performs the on child remove operation.
    /// </summary>
    protected virtual void OnChildRemove(Inline child)
    {

    }

    /// <summary>
    /// Performs the on child insert operation.
    /// </summary>
    protected virtual void OnChildInsert(Inline child)
    {
    }

    /// <summary>
    /// Dumps this instance to <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public void DumpTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        DumpTo(writer, 0);
    }

    /// <summary>
    /// Dumps this instance to <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="level">The level of indent.</param>
    /// <exception cref="ArgumentNullException">if writer is null</exception>
    public void DumpTo(TextWriter writer, int level)
    {
        ArgumentNullException.ThrowIfNull(writer);
        for (int i = 0; i < level; i++)
        {
            writer.Write(' ');
        }

        writer.WriteLine("-> " + this.GetType().Name + " = " + this);

        DumpChildTo(writer, level + 1);

        if (NextSibling != null)
        {
            NextSibling.DumpTo(writer, level);
        }
    }

    /// <summary>
    /// Performs the dump child to operation.
    /// </summary>
    protected virtual void DumpChildTo(TextWriter writer, int level)
    {
    }
}
