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

    // Elements in which a hole is an absent property: the dense storage of the array indices (attributes: W/E/C)
    private object? _elements;
    private ElementKind _elementKind;

    /// <summary>Gets the number of slots of the dense elements (the holes included).</summary>
    internal int DenseCapacity
    {
        get
        {
            switch (_elementKind)
            {
                case ElementKind.Number:
                    return ((double[])_elements!).Length;
                case ElementKind.Reference:
                    return ((CellSlot[])_elements!).Length;
                case ElementKind.Mixed:
                    return ((JsValue[])_elements!).Length;
                default:
                    return 0;
            }
        }
    }

    /// <summary>Reads a dense element: false when the index is outside the storage or the element is a hole.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetDenseElement(uint index, out JsValue value)
    {
        switch (_elementKind)
        {
            case ElementKind.Number:
                {
                    var numbers = ((double[])_elements!);
                    if (index < (uint)numbers.Length)
                    {
                        var number = numbers[index];
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
                    if (index < (uint)cells.Length)
                    {
                        var cell = cells[index].Cell;
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
                    if (index < (uint)values.Length)
                    {
                        value = values[index];
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

    /// <summary>Stores a value in a slot of the storage (the index is in range), moving to a wider kind when needed.</summary>
    private void StoreDenseElement(int index, JsValue value)
    {
        switch (_elementKind)
        {
            case ElementKind.Number when value.IsNumber:
                {
                    var number = value.NumberValue;
                    if (double.IsNaN(number) && BitConverter.DoubleToInt64Bits(number) == NumberHoleBits)
                    {
                        number = double.NaN;
                    }

                    ((double[])_elements!)[index] = number;
                    return;
                }

            case ElementKind.Reference when !value.IsNumber:
                ((CellSlot[])_elements!)[index] = new CellSlot(value.Cell);
                return;

            case ElementKind.Mixed:
                ((JsValue[])_elements!)[index] = value;
                return;
        }

        ConvertToMixed(DenseCapacity);
        ((JsValue[])_elements!)[index] = value;
    }

    /// <summary>Moves the elements to a JsValue[] of the given length (at least the current capacity).</summary>
    private void ConvertToMixed(int length)
    {
        var mixed = new JsValue[length];
        var capacity = DenseCapacity;
        switch (_elementKind)
        {
            case ElementKind.Number:
                {
                    var numbers = ((double[])_elements!);
                    for (var i = 0; i < numbers.Length; i++)
                    {
                        mixed[i] = BitConverter.DoubleToInt64Bits(numbers[i]) == NumberHoleBits ? JsValue.Hole : new JsValue(numbers[i]);
                    }

                    break;
                }

            case ElementKind.Reference:
                {
                    var cells = ((CellSlot[])_elements!);
                    for (var i = 0; i < cells.Length; i++)
                    {
                        mixed[i] = new JsValue(cells[i].Cell);
                    }

                    break;
                }

            case ElementKind.Mixed:
                Array.Copy(((JsValue[])_elements!), mixed, capacity);
                break;
        }

        mixed.AsSpan(capacity).Fill(JsValue.Hole);
        _elements = mixed;
        _elementKind = ElementKind.Mixed;
    }

    /// <summary>Makes a hole of an element.</summary>
    private void RemoveDenseElement(int index)
    {
        switch (_elementKind)
        {
            case ElementKind.Number:
                ((double[])_elements!)[index] = NumberHole;
                break;
            case ElementKind.Reference:
                ((CellSlot[])_elements!)[index] = new CellSlot(HoleCell.Instance);
                break;
            case ElementKind.Mixed:
                ((JsValue[])_elements!)[index] = JsValue.Hole;
                break;
        }
    }

    private void FillDenseHoles(int start, int count)
    {
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

    /// <summary>Truncates the dense elements (the length of an array decreased).</summary>
    internal void TruncateElements(uint length)
    {
        var capacity = DenseCapacity;
        if (length < (uint)capacity)
        {
            FillDenseHoles((int)length, capacity - (int)length);
        }
    }

    /// <summary>Gets whether at least one dense element is not a hole.</summary>
    internal bool HasAnyDenseElement() => NextDenseElement(0, int.MaxValue) >= 0;

    /// <summary>The first index in [start, end) of a dense element that is not a hole, -1 when there is none.</summary>
    internal long NextDenseElement(long start, long end)
    {
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
                ((double[])_elements!).AsSpan(0, length).Reverse();
                break;
            case ElementKind.Reference:
                ((CellSlot[])_elements!).AsSpan(0, length).Reverse();
                break;
            case ElementKind.Mixed:
                ((JsValue[])_elements!).AsSpan(0, length).Reverse();
                break;
        }
    }

    /// <summary>Moves a range of the dense storage (the ranges may overlap).</summary>
    internal void MoveDenseElements(int source, int destination, int count)
    {
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
    internal void FillDensePlaceholder(int start, int count)
    {
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
            StoreDenseElement((int)index, value);
            return true;
        }

        if (index >= (uint)Array.MaxLength / 2)
            return false;

        // Close to the end: grow. Further away (up to twice the current length): grow only when the current storage is
        // at least half full, so that a sequence of writes at exponentially increasing indices stays sparse.
        if (index > (uint)currentLength + 1024)
        {
            if (index > (uint)currentLength * 2 || !IsMostlyFilled())
                return false;
        }

        var newLength = (int)Math.Min(Math.Max(Math.Max((long)currentLength * 2, (long)index + 1), 4), Array.MaxLength);
        if (_elementKind == ElementKind.None)
        {
            if (value.IsNumber)
            {
                _elements = new double[newLength];
                _elementKind = ElementKind.Number;
            }
            else
            {
                _elements = new CellSlot[newLength];
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
            switch (_elementKind)
            {
                case ElementKind.Number:
                    {
                        var numbers = ((double[])_elements!);
                        Array.Resize(ref numbers, newLength);
                        _elements = numbers;
                        break;
                    }

                case ElementKind.Reference:
                    {
                        var cells = ((CellSlot[])_elements!);
                        Array.Resize(ref cells, newLength);
                        _elements = cells;
                        break;
                    }

                default:
                    {
                        var values = ((JsValue[])_elements!);
                        Array.Resize(ref values, newLength);
                        _elements = values;
                        break;
                    }
            }

            FillDenseHoles(currentLength, newLength - currentLength);
        }

        StoreDenseElement((int)index, value);
        return true;
    }

    private bool IsMostlyFilled()
    {
        var capacity = DenseCapacity;
        var holes = 0;
        var maxHoles = capacity / 2;
        for (var i = 0; i < capacity; i++)
        {
            if (IsDenseHole(i) && ++holes > maxHoles)
                return false;
        }

        return capacity > 0;
    }
}
