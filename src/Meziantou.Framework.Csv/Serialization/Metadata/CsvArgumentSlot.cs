namespace Meziantou.Framework.Csv.Serialization.Metadata;

internal abstract class CsvArgumentSlot
{
    public abstract object? BoxedValue { get; }

    public abstract void Reset();
}
