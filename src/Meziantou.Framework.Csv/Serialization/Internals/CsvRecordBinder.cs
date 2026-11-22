using System.Numerics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Csv.Internals;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Serialization.Internals;

/// <summary>Maps the columns of a file to the members of a type, then creates an instance from each record.</summary>
internal sealed class CsvRecordBinder<T>
{
    private readonly CsvTypeInfo<T> _typeInfo;
    private readonly CsvPropertyInfo<T>?[] _columns;
    private readonly int[] _requiredColumns;
    private readonly CsvConstructorArguments? _arguments;
    private readonly bool _throwOnUnmappedColumn;

    private CsvRecordBinder(CsvTypeInfo<T> typeInfo, CsvPropertyInfo<T>?[] columns, int[] requiredColumns, bool throwOnUnmappedColumn)
    {
        _typeInfo = typeInfo;
        _columns = columns;
        _requiredColumns = requiredColumns;
        _throwOnUnmappedColumn = throwOnUnmappedColumn;
        if (typeInfo.ParameterizedCreator is not null)
        {
            _arguments = typeInfo.CreateArguments();
        }
    }

    public static CsvRecordBinder<T> Create<TChar>(CsvTypeInfo<T> typeInfo, CsvParser<TChar> parser)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        var options = typeInfo.Options;
        var properties = typeInfo.TypedProperties;
        var header = parser.Header;
        var throwOnUnmapped = options.UnmappedColumnHandling is CsvUnmappedColumnHandling.Throw;
        if (header is null)
        {
            var layout = typeInfo.Layout;
            var required = new List<int>();
            for (var i = 0; i < layout.Length; i++)
            {
                if (layout[i] is { IsRequired: true })
                {
                    required.Add(i);
                }
            }

            return new CsvRecordBinder<T>(typeInfo, layout, [.. required], throwOnUnmapped);
        }

        var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var columns = new CsvPropertyInfo<T>?[header.ColumnCount];
        var bound = new bool[properties.Length];
        for (var column = 0; column < columns.Length; column++)
        {
            var name = header.ColumnNames[column];
            var index = Array.FindIndex(properties, p => comparer.Equals(p.Name, name));
            if (index >= 0 && !bound[index])
            {
                bound[index] = true;
                columns[column] = properties[index];
            }
            else if (throwOnUnmapped)
            {
                throw parser.CreateException($"The column '{name}' has no member in '{typeof(T)}'.", column);
            }
        }

        var requiredColumns = new List<int>();
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (!property.CanDeserialize)
                continue;

            if (!bound[i])
            {
                if (property.IsRequired)
                    throw parser.CreateException($"The required column '{property.Name}' of '{typeof(T)}' is missing.");

                if (options.MissingColumnHandling is CsvMissingColumnHandling.Throw)
                    throw parser.CreateException($"The column '{property.Name}' of '{typeof(T)}' is missing.");
            }
            else if (property.IsRequired)
            {
                requiredColumns.Add(Array.IndexOf(columns, property));
            }
        }

        // Only the columns of the header are checked
        return new CsvRecordBinder<T>(typeInfo, columns, [.. requiredColumns], throwOnUnmappedColumn: false);
    }

    public T Read<TChar>(CsvParser<TChar> parser)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        var columns = _columns;
        var columnCount = parser.ColumnCount;
        if (_throwOnUnmappedColumn && columnCount > columns.Length)
            throw parser.CreateException($"The column {columns.Length.ToString(CultureInfo.InvariantCulture)} has no member in '{typeof(T)}'.", columns.Length);

        foreach (var required in _requiredColumns)
        {
            if (required >= columnCount)
                throw parser.CreateException($"The record has no value for the required column '{columns[required]!.Name}'.", required);
        }

        var count = Math.Min(columnCount, columns.Length);
        var row = parser.RowSpan;
        T obj;
        if (_typeInfo.ParameterizedCreator is { } creator)
        {
            var arguments = _arguments!;
            arguments.Reset();
            for (var column = 0; column < count; column++)
            {
                var property = columns[column];
                if (property is null || property.ParameterIndex < 0)
                    continue;

                parser.GetField(row, column, out var value, out _, out _);
                try
                {
                    var slot = arguments.GetSlot(property.ParameterIndex);
                    if (typeof(TChar) == typeof(char))
                    {
                        property.ReadInto(slot, unsafe(MemoryMarshal.Cast<TChar, char>(value)));
                    }
                    else
                    {
                        property.ReadInto(slot, unsafe(MemoryMarshal.Cast<TChar, byte>(value)));
                    }
                }
                catch (Exception ex) when (IsConversionException(ex))
                {
                    throw CreateConversionException(parser, column, property, value, ex);
                }
            }

            obj = creator(arguments);
        }
        else
        {
            obj = _typeInfo.CreateObject();
        }

        if (_typeInfo.HasOnDeserializing)
        {
            ((ICsvOnDeserializing)obj!).OnDeserializing();
        }

        for (var column = 0; column < count; column++)
        {
            var property = columns[column];
            if (property is null || !property.CanSet)
                continue;

            parser.GetField(row, column, out var value, out _, out _);
            try
            {
                if (typeof(TChar) == typeof(char))
                {
                    property.ReadAndSet(ref obj, unsafe(MemoryMarshal.Cast<TChar, char>(value)));
                }
                else
                {
                    property.ReadAndSet(ref obj, unsafe(MemoryMarshal.Cast<TChar, byte>(value)));
                }
            }
            catch (Exception ex) when (IsConversionException(ex))
            {
                throw CreateConversionException(parser, column, property, value, ex);
            }
        }

        if (_typeInfo.HasOnDeserialized)
        {
            ((ICsvOnDeserialized)obj!).OnDeserialized();
        }

        return obj;
    }

    private static bool IsConversionException(Exception exception) => exception is FormatException or OverflowException or ArgumentException or InvalidCastException or CsvException;

    private static CsvException CreateConversionException<TChar>(CsvParser<TChar> parser, int column, CsvPropertyInfo property, ReadOnlySpan<TChar> value, Exception exception)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        var text = parser.GetString(value);
        if (text.Length > 100)
        {
            text = string.Concat(text.AsSpan(0, 100), "…");
        }

        return parser.CreateException($"The value '{text}' of the column '{property.Name}' cannot be converted to '{property.PropertyType}': {exception.Message}", column, exception);
    }
}
