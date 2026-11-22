using System.Numerics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Csv.Internals;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

internal sealed class CsvPropertyInfo<TDeclaring, TValue> : CsvPropertyInfo<TDeclaring>
{
    private readonly Func<TDeclaring, TValue>? _getter;
    private readonly CsvPropertySetter<TDeclaring, TValue>? _setter;
    private readonly CsvConverter<TValue> _converter;
    private readonly CsvConverterContext _context;
    private readonly bool _isText;

    public CsvPropertyInfo(string name, CsvPropertyInfoValues<TDeclaring, TValue> values, CsvConverter<TValue> converter, CsvConverterContext context)
        : base(name, values.MemberName, typeof(TValue), values.Order, values.ColumnIndex, values.IsRequired, values.Format, converter, values.ParameterIndex)
    {
        _getter = values.Getter;
        _setter = values.Setter;
        _converter = converter;
        _context = context;
        _isText = typeof(TValue) == typeof(string);
    }

    public override bool CanSerialize => _getter is not null;

    public override bool CanDeserialize => _setter is not null || ParameterIndex >= 0;

    public override bool CanSet => _setter is not null;

    public override CsvArgumentSlot CreateArgumentSlot(bool hasDefaultValue, object? defaultValue)
    {
        var value = default(TValue)!;
        if (hasDefaultValue)
        {
            if (defaultValue is TValue typedValue)
            {
                value = typedValue;
            }
            else if (defaultValue is not null && typeof(TValue).IsEnum)
            {
                // The default value of an enumeration parameter can be its underlying value
                value = (TValue)Enum.ToObject(typeof(TValue), defaultValue);
            }
        }

        return new CsvArgumentSlot<TValue>(value);
    }

    public override void ReadAndSet(ref TDeclaring obj, ReadOnlySpan<char> value) => _setter!(ref obj, Read(value));

    public override void ReadAndSet(ref TDeclaring obj, ReadOnlySpan<byte> utf8Value) => _setter!(ref obj, Read(utf8Value));

    public override void ReadInto(CsvArgumentSlot slot, ReadOnlySpan<char> value) => ((CsvArgumentSlot<TValue>)slot).Value = Read(value);

    public override void ReadInto(CsvArgumentSlot slot, ReadOnlySpan<byte> utf8Value) => ((CsvArgumentSlot<TValue>)slot).Value = Read(utf8Value);

    private TValue Read(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty && default(TValue) is null && !_converter.HandleNull)
            return default!;

        return _converter.Read(value, _context);
    }

    private TValue Read(ReadOnlySpan<byte> utf8Value)
    {
        if (utf8Value.IsEmpty && default(TValue) is null && !_converter.HandleNull)
            return default!;

        return _converter.Read(utf8Value, _context);
    }

    public override void Write(CsvWriterCore<char> writer, TDeclaring obj) => WriteCore(writer, obj);

    public override void Write(CsvWriterCore<byte> writer, TDeclaring obj) => WriteCore(writer, obj);

    private void WriteCore<TChar>(CsvWriterCore<TChar> writer, TDeclaring obj)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (_getter is null)
        {
            writer.WriteField([], isText: false);
            return;
        }

        var value = _getter(obj);
        if (value is null && !_converter.HandleNull)
        {
            writer.WriteField([], isText: false);
            return;
        }

        var sizeHint = 64;
        var destination = writer.BeginField(sizeHint, out var start);
        while (true)
        {
            bool success;
            int written;
            if (typeof(TChar) == typeof(char))
            {
                success = _converter.TryFormat(value, unsafe(MemoryMarshal.Cast<TChar, char>(destination)), out written, _context);
            }
            else
            {
                success = _converter.TryFormat(value, unsafe(MemoryMarshal.Cast<TChar, byte>(destination)), out written, _context);
            }

            if (success)
            {
                writer.CommitField(start, written, _isText);
                return;
            }

            sizeHint = CsvWriterCore<TChar>.GrowFormatSize(Math.Max(sizeHint, destination.Length));
            destination = writer.GetSpan(sizeHint);
        }
    }
}
