namespace Meziantou.Framework.Csv.Serialization.Metadata;

// The slot of a constructor parameter that has no member: no column sets it, so it always has the default value of the parameter
internal sealed class CsvBoxedArgumentSlot(object? defaultValue) : CsvArgumentSlot
{
    public override object? BoxedValue => defaultValue;

    public override void Reset()
    {
    }
}
