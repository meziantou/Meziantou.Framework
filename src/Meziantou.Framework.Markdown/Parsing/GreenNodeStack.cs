using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>A list of green nodes, whose top elements are the children of the node being built.</summary>
/// <remarks>
/// The nodes are in a struct, so that storing one does not check the type of the array: <see cref="GreenNode"/> is not sealed,
/// so an array of it can be an array of a derived type, which every store into it checks.
/// </remarks>
internal sealed class GreenNodeStack
{
    private Slot[] _slots = new Slot[64];

    public int Count { get; private set; }

    public void Push(GreenNode? node)
    {
        if (Count == _slots.Length)
        {
            Array.Resize(ref _slots, _slots.Length * 2);
        }

        _slots[Count++].Node = node;
    }

    /// <summary>Gets the nodes from <paramref name="start"/>.</summary>
    /// <remarks>A slot is a struct whose only field is the node, so the slots are read as nodes, as ImmutableArray reads its array.</remarks>
    public ReadOnlySpan<GreenNode?> From(int start) => start == Count ? [] : unsafe(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Slot, GreenNode?>(ref _slots[start]), Count - start));

    /// <summary>Removes the nodes from <paramref name="count"/>.</summary>
    /// <remarks>The slots keep the nodes until they are reused: the stack lives as long as the tree being built.</remarks>
    public void Truncate(int count) => Count = count;

    private struct Slot
    {
        public GreenNode? Node;
    }
}
