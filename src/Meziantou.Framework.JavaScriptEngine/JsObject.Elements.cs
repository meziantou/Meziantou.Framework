using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

public partial class JsObject
{
    // The kind of the array that stores the dense elements. The storage starts as the smallest kind that holds the first
    // element and moves to Mixed when a value of another kind is stored (it never moves back).
    private enum ElementKind : byte
    {
        None,

        // double[]: numbers only, 8 bytes per element. A hole is a NaN with a payload that no stored number has.
        Number,

        // CellSlot[]: the values that are not numbers, 8 bytes per element. A hole is HoleCell.Instance.
        Reference,

        // JsValue[]: any value, 16 bytes per element.
        Mixed,
    }

    // A reference without the covariance checks of JsCell?[] (reads and writes of the elements stay cheap)
    private readonly record struct CellSlot(JsCell? Cell);

    private const long NumberHoleBits = 0x7FF8_DEAD_BEEF_0001;

    private static readonly double NumberHole = BitConverter.Int64BitsToDouble(NumberHoleBits);

    // The storage of a large array is in the large object heap, where the allocations count toward a gen2 collection. The
    // storages of this size come from the shared array pools, and the storage that a growth replaces goes back to them
    // (nothing else references it: the storage never leaves the object). The growth doubles the length, so the sizes are
    // the powers of two of the pools.
    private const int PooledStorageMinBytes = 85_000;
    private const int PooledStorageMaxLength = 1 << 24;

    // Elements in which a hole is an absent property: the dense storage of the array indices (attributes: W/E/C). The
    // element i is at _elementStart + i in the storage: removing the first elements (shift) moves the start instead of the
    // other elements, and the free slots before the start take the elements inserted at the front (unshift). The slots
    // before the start are holes.
    private object? _elements;
    private ElementKind _elementKind;
    private int _elementStart;

    // The number of elements that are not holes
    private int _denseCount;

    private int StorageLength => _elementKind switch
    {
        ElementKind.Number => ((double[])_elements!).Length,
        ElementKind.Reference => ((CellSlot[])_elements!).Length,
        ElementKind.Mixed => ((JsValue[])_elements!).Length,
        _ => 0,
    };

    /// <summary>Gets the number of slots of the dense elements (the holes included).</summary>
    internal int DenseCapacity => StorageLength - _elementStart;

    /// <summary>Reads a dense element: false when the index is outside the storage or the element is a hole.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetDenseElement(uint index, out JsValue value)
    {
        switch (_elementKind)
        {
            case ElementKind.Number:
                {
                    var numbers = ((double[])_elements!);
                    var position = (ulong)index + (uint)_elementStart;
                    if (position < (ulong)numbers.Length)
                    {
                        var number = numbers[(int)position];
                        if (BitConverter.DoubleToInt64Bits(number) != NumberHoleBits)
                        {
                            value = new JsValue(number);
                            return true;
                        }
                    }

                    break;
                }

            case ElementKind.Reference:
                {
                    var cells = ((CellSlot[])_elements!);
                    var position = (ulong)index + (uint)_elementStart;
                    if (position < (ulong)cells.Length)
                    {
                        var cell = cells[(int)position].Cell;
                        if (!ReferenceEquals(cell, HoleCell.Instance))
                        {
                            value = new JsValue(cell);
                            return true;
                        }
                    }

                    break;
                }

            case ElementKind.Mixed:
                {
                    var values = ((JsValue[])_elements!);
                    var position = (ulong)index + (uint)_elementStart;
                    if (position < (ulong)values.Length)
                    {
                        value = values[(int)position];
                        if (!value.IsHole)
                            return true;
                    }

                    break;
                }
        }

        value = default;
        return false;
    }

    /// <summary>Gets whether the index is in the dense elements and is not a hole.</summary>
    internal bool HasDenseElement(uint index) => TryGetDenseElement(index, out _);

    private bool IsDenseHole(int index)
    {
        index += _elementStart;
        return _elementKind switch
        {
            ElementKind.Number => BitConverter.DoubleToInt64Bits(((double[])_elements!)[index]) == NumberHoleBits,
            ElementKind.Reference => ReferenceEquals(((CellSlot[])_elements!)[index].Cell, HoleCell.Instance),
            ElementKind.Mixed => ((JsValue[])_elements!)[index].IsHole,
            _ => true,
        };
    }

    /// <summary>Replaces an element that exists in the dense elements: false when the index is a hole or outside the storage.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReplaceDenseElement(uint index, JsValue value)
    {
        if (index >= (uint)DenseCapacity || IsDenseHole((int)index))
            return false;

        StoreDenseElement((int)index, value);
        return true;
    }

    /// <summary>Stores a value in a slot of the storage (the index is in range), moving to a wider kind when needed; true when the slot was a hole.</summary>
    private bool StoreDenseElement(int index, JsValue value)
    {
        var position = _elementStart + index;
        switch (_elementKind)
        {
            case ElementKind.Number when value.IsNumber:
                {
                    var number = value.NumberValue;
                    if (double.IsNaN(number) && BitConverter.DoubleToInt64Bits(number) == NumberHoleBits)
                    {
                        number = double.NaN;
                    }

                    ref var slot = ref ((double[])_elements!)[position];
                    var wasHole = BitConverter.DoubleToInt64Bits(slot) == NumberHoleBits;
                    slot = number;
                    return wasHole;
                }

            case ElementKind.Reference when !value.IsNumber:
                {
                    ref var slot = ref ((CellSlot[])_elements!)[position];
                    var wasHole = ReferenceEquals(slot.Cell, HoleCell.Instance);
                    slot = new CellSlot(value.Cell);
                    return wasHole;
                }

            case ElementKind.Mixed:
                {
                    ref var slot = ref ((JsValue[])_elements!)[position];
                    var wasHole = slot.IsHole;
                    slot = value;
                    return wasHole;
                }
        }

        ConvertToMixed(DenseCapacity);
        ref var mixedSlot = ref ((JsValue[])_elements!)[index];
        var mixedWasHole = mixedSlot.IsHole;
        mixedSlot = value;
        return mixedWasHole;
    }

    private static bool IsPooledStorageLength(int length, int elementSize)
    {
        return length <= PooledStorageMaxLength && (long)length * elementSize >= PooledStorageMinBytes && BitOperations.IsPow2(length);
    }

    private static T[] AllocateStorage<T>(int length, int elementSize)
    {
        JsEngine.EnsureCurrentCanAllocate((long)length * elementSize);

        // A rented array is not cleared: the caller writes every element
        return IsPooledStorageLength(length, elementSize) ? ArrayPool<T>.Shared.Rent(length) : new T[length];
    }

    private static void ReleaseStorage<T>(T[] storage, int elementSize, bool hasReferences)
    {
        if (IsPooledStorageLength(storage.Length, elementSize))
        {
            ArrayPool<T>.Shared.Return(storage, clearArray: hasReferences);
        }
    }

    /// <summary>
    /// Copies the elements (from the start) to a new storage, after <paramref name="front"/> free slots, with room for
    /// <paramref name="capacity"/> elements (at least the current capacity): the other slots are holes.
    /// </summary>
    private static T[] CopyStorage<T>(T[] storage, int start, int front, int capacity, int elementSize, bool hasReferences, T hole)
    {
        var copy = AllocateStorage<T>(front + capacity, elementSize);
        var elements = storage.AsSpan(start);
        copy.AsSpan(0, front).Fill(hole);
        elements.CopyTo(copy.AsSpan(front));
        copy.AsSpan(front + elements.Length).Fill(hole);
        ReleaseStorage(storage, elementSize, hasReferences);
        return copy;
    }

    /// <summary>Moves the elements to a new storage of the same kind (see <see cref="CopyStorage"/>).</summary>
    private void ReallocateStorage(int front, int capacity)
    {
        _elements = _elementKind switch
        {
            ElementKind.Number => CopyStorage((double[])_elements!, _elementStart, front, capacity, elementSize: 8, hasReferences: false, NumberHole),
            ElementKind.Reference => CopyStorage((CellSlot[])_elements!, _elementStart, front, capacity, elementSize: 8, hasReferences: true, new CellSlot(HoleCell.Instance)),
            _ => CopyStorage((JsValue[])_elements!, _elementStart, front, capacity, elementSize: 16, hasReferences: true, JsValue.Hole),
        };

        _elementStart = front;
    }

    /// <summary>Moves the elements to the beginning of the storage (the free slots before them go after them).</summary>
    private void CompactStorage()
    {
        var start = _elementStart;
        var capacity = DenseCapacity;
        _elementStart = 0;
        MoveDenseElements(start, 0, capacity);
        FillDenseHoles(capacity, start);
    }

    /// <summary>Moves the elements to a JsValue[] of the given length (at least the current capacity).</summary>
    private void ConvertToMixed(int length)
    {
        var mixed = AllocateStorage<JsValue>(length, elementSize: 16);
        var start = _elementStart;
        var capacity = DenseCapacity;
        switch (_elementKind)
        {
            case ElementKind.Number:
                {
                    var numbers = ((double[])_elements!);
                    for (var i = 0; i < capacity; i++)
                    {
                        var number = numbers[start + i];
                        mixed[i] = BitConverter.DoubleToInt64Bits(number) == NumberHoleBits ? JsValue.Hole : new JsValue(number);
                    }

                    ReleaseStorage(numbers, elementSize: 8, hasReferences: false);
                    break;
                }

            case ElementKind.Reference:
                {
                    var cells = ((CellSlot[])_elements!);
                    for (var i = 0; i < capacity; i++)
                    {
                        mixed[i] = new JsValue(cells[start + i].Cell);
                    }

                    ReleaseStorage(cells, elementSize: 8, hasReferences: true);
                    break;
                }

            case ElementKind.Mixed:
                {
                    var values = ((JsValue[])_elements!);
                    Array.Copy(values, start, mixed, 0, capacity);
                    ReleaseStorage(values, elementSize: 16, hasReferences: true);
                    break;
                }
        }

        mixed.AsSpan(capacity).Fill(JsValue.Hole);
        _elements = mixed;
        _elementKind = ElementKind.Mixed;
        _elementStart = 0;
    }

    /// <summary>Makes a hole of an element.</summary>
    private void RemoveDenseElement(int index)
    {
        if (IsDenseHole(index))
            return;

        _denseCount--;
        var position = _elementStart + index;
        switch (_elementKind)
        {
            case ElementKind.Number:
                ((double[])_elements!)[position] = NumberHole;
                break;
            case ElementKind.Reference:
                ((CellSlot[])_elements!)[position] = new CellSlot(HoleCell.Instance);
                break;
            case ElementKind.Mixed:
                ((JsValue[])_elements!)[position] = JsValue.Hole;
                break;
        }
    }

    /// <summary>Sets slots to holes (the count of the elements is not updated).</summary>
    private void FillDenseHoles(int start, int count)
    {
        start += _elementStart;
        switch (_elementKind)
        {
            case ElementKind.Number:
                ((double[])_elements!).AsSpan(start, count).Fill(NumberHole);
                break;
            case ElementKind.Reference:
                ((CellSlot[])_elements!).AsSpan(start, count).Fill(new CellSlot(HoleCell.Instance));
                break;
            case ElementKind.Mixed:
                ((JsValue[])_elements!).AsSpan(start, count).Fill(JsValue.Hole);
                break;
        }
    }

    /// <summary>Truncates the dense elements (the length of an array decreased: the elements are below the previous length).</summary>
    internal void TruncateElements(uint length, uint previousLength)
    {
        var end = (int)Math.Min(previousLength, (uint)DenseCapacity);
        if (length < (uint)end)
        {
            for (var i = (int)length; i < end; i++)
            {
                if (!IsDenseHole(i))
                {
                    _denseCount--;
                }
            }

            FillDenseHoles((int)length, end - (int)length);
        }
    }

    /// <summary>Gets whether at least one dense element is not a hole.</summary>
    internal bool HasAnyDenseElement() => _denseCount > 0;

    /// <summary>The first index in [start, end) of a dense element that is not a hole, -1 when there is none.</summary>
    internal long NextDenseElement(long start, long end)
    {
        if (_denseCount == 0)
            return -1;

        end = Math.Min(end, DenseCapacity);
        for (var i = start; i < end; i++)
        {
            if (!IsDenseHole((int)i))
                return i;
        }

        return -1;
    }

    /// <summary>The last index in [0, start] of a dense element that is not a hole, -1 when there is none.</summary>
    internal long PreviousDenseElement(long start)
    {
        if (_denseCount == 0)
            return -1;

        for (var i = Math.Min(start, DenseCapacity - 1); i >= 0; i--)
        {
            if (!IsDenseHole((int)i))
                return i;
        }

        return -1;
    }

    /// <summary>Gets whether the elements [0, length) are all in the dense storage and none is a hole.</summary>
    internal bool IsDensePacked(long length)
    {
        if (length > DenseCapacity)
            return false;

        // The elements of an array are below its length
        if (this is JsArray array && length == array.Length)
            return _denseCount == length;

        for (var i = 0; i < length; i++)
        {
            if (IsDenseHole(i))
                return false;
        }

        return true;
    }

    /// <summary>Reverses the first elements of the dense storage.</summary>
    internal void ReverseDenseElements(int length)
    {
        switch (_elementKind)
        {
            case ElementKind.Number:
                ((double[])_elements!).AsSpan(_elementStart, length).Reverse();
                break;
            case ElementKind.Reference:
                ((CellSlot[])_elements!).AsSpan(_elementStart, length).Reverse();
                break;
            case ElementKind.Mixed:
                ((JsValue[])_elements!).AsSpan(_elementStart, length).Reverse();
                break;
        }
    }

    /// <summary>
    /// Moves the elements of a packed array (its elements [0, length) are all in the dense storage) for splice: the
    /// <paramref name="deleteCount"/> elements at <paramref name="start"/> are replaced with <paramref name="itemCount"/>
    /// slots that the caller overwrites (they are not holes), and the elements after them are moved accordingly. The
    /// elements before or after the deleted ones move, whichever are fewer: removing or inserting the first elements
    /// moves the start of the storage.
    /// </summary>
    internal void SpliceDenseElements(int length, int start, int deleteCount, int itemCount)
    {
        var tail = length - start - deleteCount;
        if (tail == 0)
        {
            // Nothing to move: the caller writes the items and truncates the elements after them
            return;
        }

        var newLength = length - deleteCount + itemCount;
        if (itemCount < deleteCount)
        {
            var removed = deleteCount - itemCount;
            if (start < tail)
            {
                MoveDenseElements(0, removed, start);
                FillDenseHoles(0, removed);
                _elementStart += removed;
            }
            else
            {
                MoveDenseElements(start + deleteCount, start + itemCount, tail);
                FillDenseHoles(newLength, removed);
            }
        }
        else
        {
            var added = itemCount - deleteCount;
            if (start < tail)
            {
                if (_elementStart < added)
                {
                    // Free slots before the elements, for the next insertions at the front
                    var front = Math.Max(added, newLength / 2);
                    if ((long)front + Math.Max(DenseCapacity, newLength) > Array.MaxLength)
                    {
                        front = added;
                    }

                    ReallocateStorage(front, Math.Max(DenseCapacity, newLength));
                }

                _elementStart -= added;
                MoveDenseElements(added, 0, start);
                FillDensePlaceholder(start, added);
            }
            else
            {
                if (newLength > DenseCapacity)
                {
                    EnsureDenseCapacity(newLength);
                }

                MoveDenseElements(start + deleteCount, start + itemCount, tail);
                FillDensePlaceholder(start + deleteCount, added);
            }
        }

        _denseCount = newLength;
    }

    /// <summary>Makes room for <paramref name="capacity"/> elements (more than the current capacity).</summary>
    private void EnsureDenseCapacity(int capacity)
    {
        var storageLength = StorageLength;
        if (capacity <= storageLength && _elementStart >= storageLength / 4)
        {
            CompactStorage();
            return;
        }

        var newCapacity = (int)Math.Min(Math.Max(Math.Max((long)DenseCapacity * 2, capacity), 4), Array.MaxLength);
        ReallocateStorage(front: 0, newCapacity);
    }

    /// <summary>Moves a range of the dense storage (the ranges may overlap).</summary>
    private void MoveDenseElements(int source, int destination, int count)
    {
        source += _elementStart;
        destination += _elementStart;
        switch (_elementKind)
        {
            case ElementKind.Number:
                Array.Copy(((double[])_elements!), source, ((double[])_elements!), destination, count);
                break;
            case ElementKind.Reference:
                Array.Copy(((CellSlot[])_elements!), source, ((CellSlot[])_elements!), destination, count);
                break;
            case ElementKind.Mixed:
                Array.Copy(((JsValue[])_elements!), source, ((JsValue[])_elements!), destination, count);
                break;
        }
    }

    /// <summary>Sets a range of the dense storage to a placeholder that does not change the kind (the caller overwrites it).</summary>
    private void FillDensePlaceholder(int start, int count)
    {
        start += _elementStart;
        switch (_elementKind)
        {
            case ElementKind.Number:
                ((double[])_elements!).AsSpan(start, count).Clear();
                break;
            case ElementKind.Reference:
                ((CellSlot[])_elements!).AsSpan(start, count).Clear();
                break;
            case ElementKind.Mixed:
                ((JsValue[])_elements!).AsSpan(start, count).Fill(JsValue.Undefined);
                break;
        }
    }

    /// <summary>Stores an element in the dense storage, growing it when the index is close to the end; false when it is too sparse.</summary>
    private bool TrySetElement(uint index, JsValue value)
    {
        var currentLength = DenseCapacity;
        if (index < (uint)currentLength)
        {
            if (StoreDenseElement((int)index, value))
            {
                _denseCount++;
            }

            return true;
        }

        // The free slots before the elements (after shifts) go after them, when there are enough of them
        if (_elementStart > 0 && index < (uint)StorageLength && _elementStart >= StorageLength / 4)
        {
            CompactStorage();
            _denseCount++;
            StoreDenseElement((int)index, value);
            return true;
        }

        if (index >= (uint)Array.MaxLength / 2)
            return false;

        // Close to the end: grow, unless the last elements are nearly all holes. Further away (up to twice the current
        // length): grow only when the current storage is at least half full. So the writes at exponentially increasing
        // indices stay sparse: the reverse mappings of a flags enum compiled by TypeScript (E[1], E[2], E[4]...) would
        // otherwise double the storage at each write, up to a billion elements for E[1 << 29].
        if (index > (uint)currentLength + 1024)
        {
            if (index > (uint)currentLength * 2 || !IsMostlyFilled())
                return false;
        }
        else if (currentLength >= 1024 && IsEndMostlyHoles())
        {
            return false;
        }

        var newLength = (int)Math.Min(Math.Max(Math.Max((long)currentLength * 2, (long)index + 1), 4), Array.MaxLength);
        if (this is JsArray { Length: var arrayLength } && index < arrayLength && currentLength >= 1024 && newLength < arrayLength)
        {
            // Filling an array whose length is known (new Array(n), the result of map...): it grows faster, up to its length
            newLength = (int)Math.Min(Math.Min(arrayLength, (long)newLength * 4), Array.MaxLength);
        }
        else if (newLength is >= PooledStorageMinBytes / 16 and < PooledStorageMaxLength)
        {
            // The large storages have the sizes of the array pools
            newLength = (int)BitOperations.RoundUpToPowerOf2((uint)newLength);
        }

        if (_elementKind == ElementKind.None)
        {
            if (value.IsNumber)
            {
                _elements = AllocateStorage<double>(newLength, elementSize: 8);
                _elementKind = ElementKind.Number;
            }
            else
            {
                _elements = AllocateStorage<CellSlot>(newLength, elementSize: 8);
                _elementKind = ElementKind.Reference;
            }

            FillDenseHoles(0, newLength);
        }
        else if ((_elementKind == ElementKind.Number && !value.IsNumber) || (_elementKind == ElementKind.Reference && value.IsNumber))
        {
            ConvertToMixed(newLength);
        }
        else
        {
            ReallocateStorage(front: 0, newLength);
        }

        _denseCount++;
        StoreDenseElement((int)index, value);
        return true;
    }

    private bool IsMostlyFilled()
    {
        var capacity = DenseCapacity;
        return capacity > 0 && capacity - _denseCount <= capacity / 2;
    }

    // Whether more than 7/8 of the last 1024 elements of the storage are holes: a bounded check, as each write that stays
    // sparse checks it again
    private bool IsEndMostlyHoles()
    {
        var capacity = DenseCapacity;
        var start = Math.Max(capacity - 1024, 0);
        var minElements = (capacity - start) / 8;
        var elements = 0;
        for (var i = start; i < capacity; i++)
        {
            if (!IsDenseHole(i) && ++elements >= minElements)
                return false;
        }

        return true;
    }
}
