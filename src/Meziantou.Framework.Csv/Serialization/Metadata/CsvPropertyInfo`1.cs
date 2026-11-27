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

    // converted is set once the value is converted, before the setter is called: an exception of the setter is not reported to the error handler
    public abstract void ReadAndSet(ref TDeclaring obj, ReadOnlySpan<char> value, ref bool converted);

    // isAscii: the value is from a record whose bytes are all ASCII (see CsvParser<T>.RowIsAscii), which a string does not need to validate
    public abstract void ReadAndSet(ref TDeclaring obj, ReadOnlySpan<byte> utf8Value, bool isAscii, ref bool converted);

    public abstract void ReadInto(CsvArgumentSlot slot, ReadOnlySpan<char> value);

    public abstract void ReadInto(CsvArgumentSlot slot, ReadOnlySpan<byte> utf8Value, bool isAscii);

    public abstract void Write(CsvWriterCore<char> writer, TDeclaring obj);

    public abstract void Write(CsvWriterCore<byte> writer, TDeclaring obj);
}
