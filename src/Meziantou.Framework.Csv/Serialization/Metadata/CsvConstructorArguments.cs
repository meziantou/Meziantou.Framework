namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>The values of the constructor parameters of a record, passed to <see cref="CsvObjectInfoValues{T}.ObjectWithParameterizedConstructorCreator"/>.</summary>
/// <remarks>The values are kept in typed slots, reused for every record, so that the values of value types are not boxed.</remarks>
public sealed class CsvConstructorArguments
{
    private readonly CsvArgumentSlot[] _slots;

    internal CsvConstructorArguments(CsvArgumentSlot[] slots)
    {
        _slots = slots;
    }

    /// <summary>Gets the number of parameters.</summary>
    public int Count => _slots.Length;

    /// <summary>Gets the value of a parameter: the value read from the record, or the default value of the parameter when the record has no value for it.</summary>
    /// <typeparam name="TValue">The type of the parameter.</typeparam>
    /// <param name="index">The position of the parameter.</param>
    /// <returns>The value of the parameter.</returns>
    /// <exception cref="InvalidCastException"><typeparamref name="TValue"/> is not the type of the parameter.</exception>
    public TValue GetValue<TValue>(int index)
    {
        var slot = _slots[index];
        if (slot is CsvArgumentSlot<TValue> typedSlot)
            return typedSlot.Value;

        // A parameter without a member has its default value, boxed
        return slot.BoxedValue is null ? default! : (TValue)slot.BoxedValue;
    }

    internal CsvArgumentSlot GetSlot(int index) => _slots[index];

    internal object? GetBoxedValue(int index) => _slots[index].BoxedValue;

    internal void Reset()
    {
        foreach (var slot in _slots)
        {
            slot.Reset();
        }
    }
}
