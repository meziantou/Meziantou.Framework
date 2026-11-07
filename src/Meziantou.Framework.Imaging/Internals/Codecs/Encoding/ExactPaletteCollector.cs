using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The exact set of the colors of the images of one output, for the encoders that store a palette without ever reducing
/// colors (indexed PNG): at most <see cref="MaxColors"/> distinct 32-bit keys, each mapped to its palette index.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// A key is any 32-bit value chosen by the encoder (for example <c>(A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c>);
/// two pixels share a palette entry when their keys are equal.
/// </description></item>
/// <item><description>
/// <b>Collection.</b> <see cref="TryAdd"/> adds a key unless the set is full. Keys can be collected from several images
/// before the palette is needed, and a group of additions can be undone (<see cref="Count"/> before the group, then
/// <see cref="Rollback"/>), so that an image that does not fit leaves the set as it was.
/// </description></item>
/// <item><description>
/// <b>Palette.</b> <see cref="Freeze"/> ends the collection and sorts the keys in ascending order: the index of a key is
/// then its rank, a pure function of the set of colors. <see cref="TryGetIndex"/> maps keys afterward.
/// </description></item>
/// </list>
/// The keys are found by open addressing over <see cref="SlotCount"/> slots (a load of at most a quarter). Working memory
/// (3 KiB, whatever the number and the size of the images) is rented once from the writer scope as
/// <see cref="AllocationKind.Temporary"/>.
/// </remarks>
internal sealed class ExactPaletteCollector : IDisposable
{
    /// <summary>The largest number of keys.</summary>
    public const int MaxColors = 256;

    private const int SlotCount = 1024;

    // The keys in palette order (insertion order until the set is frozen), then for each slot the index of its key plus one
    // (0 for an empty slot)
    private const int KeysOffset = 0;
    private const int SlotsOffset = KeysOffset + (MaxColors * sizeof(uint));
    private const int StateLength = SlotsOffset + (SlotCount * sizeof(ushort));

    private PooledBuffer? _state;
    private int _count;
    private bool _isFrozen;

    /// <summary>Allocates the working memory.</summary>
    /// <param name="scope">The scope charged for it.</param>
    /// <exception cref="ImageResourceLimitException">The memory exceeds the live-allocation limit.</exception>
    public ExactPaletteCollector(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _state = scope.Rent(StateLength, AllocationKind.Temporary);
    }

    /// <summary>Gets the number of keys.</summary>
    public int Count => _count;

    /// <summary>Gets a value indicating whether the collection is over (the keys are sorted and no key can be added).</summary>
    public bool IsFrozen => _isFrozen;

    /// <summary>Gets the keys in palette order: the order of their first addition, then ascending once the set is frozen.</summary>
    public ReadOnlySpan<uint> Colors => Keys[.._count];

    private Span<uint> Keys => unsafe(MemoryMarshal.Cast<byte, uint>(GetState().Slice(KeysOffset, MaxColors * sizeof(uint))));

    private Span<ushort> Slots => unsafe(MemoryMarshal.Cast<byte, ushort>(GetState().Slice(SlotsOffset, SlotCount * sizeof(ushort))));

    /// <summary>Adds a key to the set.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="false"/> when the key is new and the set already holds <see cref="MaxColors"/> keys (nothing is added).</returns>
    /// <exception cref="InvalidOperationException">The set is frozen.</exception>
    public bool TryAdd(uint key)
    {
        if (_isFrozen)
            throw new InvalidOperationException("The palette is final: no color can be added.");

        var slots = Slots;
        var keys = Keys;
        var slot = GetSlot(key);
        while (slots[slot] != 0)
        {
            if (keys[slots[slot] - 1] == key)
                return true;

            slot = (slot + 1) & (SlotCount - 1);
        }

        if (_count == MaxColors)
            return false;

        keys[_count++] = key;
        slots[slot] = (ushort)_count;
        return true;
    }

    /// <summary>Gets the palette index of a key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="index">The index of the key in <see cref="Colors"/>.</param>
    /// <returns><see langword="false"/> when the key is not in the set.</returns>
    public bool TryGetIndex(uint key, out byte index)
    {
        var slots = Slots;
        var keys = Keys;
        var slot = GetSlot(key);
        while (slots[slot] != 0)
        {
            var candidate = slots[slot] - 1;
            if (keys[candidate] == key)
            {
                index = (byte)candidate;
                return true;
            }

            slot = (slot + 1) & (SlotCount - 1);
        }

        index = 0;
        return false;
    }

    /// <summary>Removes the keys added since the set held <paramref name="count"/> keys.</summary>
    /// <param name="count">A previous value of <see cref="Count"/>.</param>
    /// <exception cref="InvalidOperationException">Keys must be removed and the set is frozen.</exception>
    public void Rollback(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _count);
        if (count == _count)
            return;

        if (_isFrozen)
            throw new InvalidOperationException("The palette is final: no color can be removed.");

        _count = count;
        RebuildSlots();
    }

    /// <summary>Ends the collection: sorts the keys in ascending order, which gives every key its final index.</summary>
    public void Freeze()
    {
        if (_isFrozen)
            return;

        Keys[.._count].Sort();
        RebuildSlots();
        _isFrozen = true;
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }

    // Fibonacci hashing: the ten high bits of the product spread keys that differ in any byte
    private static int GetSlot(uint key) => (int)((key * 2654435769u) >> 22);

    private void RebuildSlots()
    {
        var slots = Slots;
        var keys = Keys;
        slots.Clear();
        for (var i = 0; i < _count; i++)
        {
            var slot = GetSlot(keys[i]);
            while (slots[slot] != 0)
            {
                slot = (slot + 1) & (SlotCount - 1);
            }

            slots[slot] = (ushort)(i + 1);
        }
    }

    private Span<byte> GetState()
    {
        ObjectDisposedException.ThrowIf(_state is null, this);
        return _state.Span;
    }
}
