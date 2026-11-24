using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

internal abstract class CsvPropertyInfo<TDeclaring> : CsvPropertyInfo
{
    private protected CsvPropertyInfo(string name, string memberName, Type propertyType, int order, int? columnIndex, bool isRequired, string? format, CsvConverter converter, int parameterIndex)
        : base(name, memberName, propertyType, order, columnIndex, isRequired, format, converter)
    {
        ParameterIndex = parameterIndex;
    }

    // The index of the constructor parameter of the column, or -1
    public int ParameterIndex { get; }

    public abstract bool CanSet { get; }

    public abstract CsvArgumentSlot CreateArgumentSlot(bool hasDefaultValue, object? defaultValue);

    // converted is set once the value is converted, before the setter is called: an exception of the setter is not a conversion error
    public abstract void ReadAndSet(ref TDeclaring obj, ReadOnlySpan<char> value, ref bool converted);

    public abstract void ReadAndSet(ref TDeclaring obj, ReadOnlySpan<byte> utf8Value, ref bool converted);

    public abstract void ReadInto(CsvArgumentSlot slot, ReadOnlySpan<char> value);

    public abstract void ReadInto(CsvArgumentSlot slot, ReadOnlySpan<byte> utf8Value);

    public abstract void Write(CsvWriterCore<char> writer, TDeclaring obj);

    public abstract void Write(CsvWriterCore<byte> writer, TDeclaring obj);
}
