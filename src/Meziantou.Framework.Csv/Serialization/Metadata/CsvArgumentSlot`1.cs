namespace Meziantou.Framework.Csv.Serialization.Metadata;

internal sealed class CsvArgumentSlot<TValue> : CsvArgumentSlot
{
    private readonly TValue _defaultValue;

    public CsvArgumentSlot(TValue defaultValue)
    {
        _defaultValue = defaultValue;
        Value = defaultValue;
    }

    public TValue Value { get; set; }

    public override object? BoxedValue => Value;

    public override void Reset() => Value = _defaultValue;
}
