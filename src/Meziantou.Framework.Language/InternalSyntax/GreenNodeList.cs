namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>Reads and edits the sequence of children held in one slot.</summary>
/// <remarks>
/// A slot holds nothing for an empty sequence, the child itself for a sequence of one, and a
/// <see cref="SyntaxList"/> otherwise. Every list type in the public surface goes through here so that collapsing
/// is decided in exactly one place.
/// </remarks>
internal static class GreenNodeList
{
    public static int Count(GreenNode? node) => node is null ? 0 : node.IsList ? node.SlotCount : 1;

    public static GreenNode? ElementAt(GreenNode? node, int index)
    {
        if (node is null)
            return null;

        return node.IsList ? node.GetSlot(index) : index == 0 ? node : null;
    }

    public static int OffsetAt(GreenNode? node, int index)
    {
        if (node is null || !node.IsList)
            return 0;

        return node.GetSlotOffset(index);
    }

    public static GreenNode?[] ToArray(GreenNode? node)
    {
        var count = Count(node);
        if (count == 0)
            return [];

        var result = new GreenNode?[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ElementAt(node, i);
        }

        return result;
    }

    public static GreenNode? Insert(GreenNode? node, int index, ReadOnlySpan<GreenNode?> items)
    {
        var existing = ToArray(node);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, existing.Length, nameof(index));

        var result = new GreenNode?[existing.Length + items.Length];
        existing.AsSpan(0, index).CopyTo(result);
        items.CopyTo(result.AsSpan(index));
        existing.AsSpan(index).CopyTo(result.AsSpan(index + items.Length));

        return SyntaxList.List(result);
    }

    public static GreenNode? RemoveAt(GreenNode? node, int index) => ReplaceRange(node, index, count: 1, []);

    public static GreenNode? ReplaceRange(GreenNode? node, int index, int count, ReadOnlySpan<GreenNode?> items)
    {
        var existing = ToArray(node);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index + count, existing.Length, nameof(count));

        var result = new GreenNode?[existing.Length - count + items.Length];
        existing.AsSpan(0, index).CopyTo(result);
        items.CopyTo(result.AsSpan(index));
        existing.AsSpan(index + count).CopyTo(result.AsSpan(index + items.Length));

        return SyntaxList.List(result);
    }

    public static int IndexOf(GreenNode? node, GreenNode? item)
    {
        var count = Count(node);
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(ElementAt(node, i), item))
                return i;
        }

        return -1;
    }
}
