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
    private readonly CsvDeserializationErrorHandler? _errorHandler;

    // The columns whose member is set after the object is created, in the order of the columns: the loop over the values of a record does not
    // skip the columns without a member, and does not call CanSet for each value
    private readonly int[] _setColumns;
    private readonly CsvPropertyInfo<T>[] _setProperties;

    private CsvRecordBinder(CsvTypeInfo<T> typeInfo, CsvPropertyInfo<T>?[] columns, int[] requiredColumns, bool throwOnUnmappedColumn)
    {
        _typeInfo = typeInfo;
        _errorHandler = typeInfo.Options.DeserializationErrorHandler;
        _columns = columns;
        _requiredColumns = requiredColumns;
        _throwOnUnmappedColumn = throwOnUnmappedColumn;
        if (typeInfo.ParameterizedCreator is not null)
        {
            _arguments = typeInfo.CreateArguments();
        }

        var setColumns = new List<int>();
        var setProperties = new List<CsvPropertyInfo<T>>();
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i] is { CanSet: true } property)
            {
                setColumns.Add(i);
                setProperties.Add(property);
            }
        }

        _setColumns = [.. setColumns];
        _setProperties = [.. setProperties];
    }

    private CsvRecordBinder(CsvRecordBinder<T> binder, CsvDeserializationErrorHandler? errorHandler)
    {
        _typeInfo = binder._typeInfo;
        _errorHandler = errorHandler;
        _columns = binder._columns;
        _requiredColumns = binder._requiredColumns;
        _throwOnUnmappedColumn = binder._throwOnUnmappedColumn;
        _setColumns = binder._setColumns;
        _setProperties = binder._setProperties;
        if (_typeInfo.ParameterizedCreator is not null)
        {
            _arguments = _typeInfo.CreateArguments();
        }
    }

    public CsvDeserializationErrorHandler? ErrorHandler => _errorHandler;

    /// <summary>Creates a binder of the same columns that can read records at the same time as this one, on another thread.</summary>
    /// <param name="errorHandler">The handler called for the errors of the records, instead of the handler of the options.</param>
    public CsvRecordBinder<T> Clone(CsvDeserializationErrorHandler? errorHandler) => new(this, errorHandler);

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
                // The column is ignored unless the handler throws: the header row is not a record to skip
                ReportError(options.DeserializationErrorHandler, CsvDeserializationErrorKind.UnmappedColumn, parser.CreateException($"The column '{name}' has no member in '{typeof(T)}'.", column), memberName: null, rawValue: null);
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
                // The member keeps its default value unless the handler throws: the header row is not a record to skip
                if (property.IsRequired)
                {
                    ReportError(options.DeserializationErrorHandler, CsvDeserializationErrorKind.MissingColumn, parser.CreateException($"The required column '{property.Name}' of '{typeof(T)}' is missing."), property.Name, rawValue: null);
                }
                else if (options.MissingColumnHandling is CsvMissingColumnHandling.Throw)
                {
                    ReportError(options.DeserializationErrorHandler, CsvDeserializationErrorKind.MissingColumn, parser.CreateException($"The column '{property.Name}' of '{typeof(T)}' is missing."), property.Name, rawValue: null);
                }
            }
            else if (property.IsRequired)
            {
                requiredColumns.Add(Array.IndexOf(columns, property));
            }
        }

        // Only the columns of the header are checked
        return new CsvRecordBinder<T>(typeInfo, columns, [.. requiredColumns], throwOnUnmappedColumn: false);
    }

    /// <summary>Creates an instance from the current record.</summary>
    /// <returns><see langword="true"/> when <paramref name="result"/> is the instance; <see langword="false"/> when the error handler skips the record.</returns>
    public bool TryRead<TChar>(CsvParser<TChar> parser, [MaybeNullWhen(false)] out T result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (_errorHandler is null)
            return TryRead<TChar, WithoutErrorHandler>(parser, out result);

        return TryRead<TChar, WithErrorHandler>(parser, out result);
    }

    // Without a handler, every error throws and the code that reports the errors and skips the record is removed: the method that handles both cases
    // measured 3 to 4% more instructions per record
    private bool TryRead<TChar, TMode>(CsvParser<TChar> parser, [MaybeNullWhen(false)] out T result)
        where TChar : unmanaged, IBinaryInteger<TChar>
        where TMode : struct
    {
        var columns = _columns;
        var columnCount = parser.ColumnCount;
        if (_throwOnUnmappedColumn)
        {
            // Each extra column is reported, so that a handler can accept some of them and not the others
            for (var column = columns.Length; column < columnCount; column++)
            {
                var exception = parser.CreateException($"The column {column.ToString(CultureInfo.InvariantCulture)} has no member in '{typeof(T)}'.", column);
                if (typeof(TMode) == typeof(WithoutErrorHandler))
                    throw exception;

                if (!ReportError(_errorHandler, CsvDeserializationErrorKind.UnmappedColumn, exception, memberName: null, rawValue: null))
                    goto Skip;
            }
        }

        foreach (var required in _requiredColumns)
        {
            if (required >= columnCount)
            {
                var name = columns[required]!.Name;
                var exception = parser.CreateException($"The record has no value for the required column '{name}'.", required);
                if (typeof(TMode) == typeof(WithoutErrorHandler))
                    throw exception;

                if (!ReportError(_errorHandler, CsvDeserializationErrorKind.MissingRequiredValue, exception, name, rawValue: null))
                    goto Skip;
            }
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
                    if (typeof(TMode) == typeof(WithoutErrorHandler))
                        throw CreateConversionException(parser, column, property, parser.GetString(value), ex);

                    // The parameter keeps its default value when the error is ignored
                    if (!ReportConversionError(parser, column, property, value, ex))
                        goto Skip;
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

        // The ends of the columns are read once for the record, instead of checking the index of each value
        var columnEnds = parser.RowColumnEnds;
        var setColumns = _setColumns;
        var setProperties = _setProperties;
        for (var i = 0; i < setColumns.Length; i++)
        {
            // The columns are in ascending order: the next ones are not in the record either
            var column = setColumns[i];
            if (column >= columnCount)
                break;

            var property = setProperties[i];
            var start = columnEnds[column] + 1;
            parser.GetValue(column, row[start..columnEnds[column + 1]], out var value, out _);
            var converted = false;
            try
            {
                if (typeof(TChar) == typeof(char))
                {
                    property.ReadAndSet(ref obj, unsafe(MemoryMarshal.Cast<TChar, char>(value)), ref converted);
                }
                else
                {
                    property.ReadAndSet(ref obj, unsafe(MemoryMarshal.Cast<TChar, byte>(value)), ref converted);
                }
            }
            catch (Exception ex) when (IsConversionException(ex))
            {
                // An exception of the setter is not reported to the handler, but it is wrapped as without a handler, with the position of the value
                if (typeof(TMode) == typeof(WithoutErrorHandler) || converted)
                    throw CreateConversionException(parser, column, property, parser.GetString(value), ex);

                // The member keeps its value when the error is ignored
                if (!ReportConversionError(parser, column, property, value, ex))
                    goto Skip;
            }
        }

        if (_typeInfo.HasOnDeserialized)
        {
            ((ICsvOnDeserialized)obj!).OnDeserialized();
        }

        result = obj;
        return true;

    Skip:
        result = default;
        return false;
    }

    /// <summary>Calls the error handler, or throws when there is none.</summary>
    /// <returns><see langword="true"/> to ignore the error; <see langword="false"/> to skip the record.</returns>
    private static bool ReportError(CsvDeserializationErrorHandler? handler, CsvDeserializationErrorKind kind, CsvException exception, string? memberName, string? rawValue)
    {
        if (handler is null)
            throw exception;

        var action = handler(new CsvDeserializationErrorContext(kind, typeof(T), memberName, rawValue, exception));
        return action switch
        {
            CsvDeserializationErrorAction.Ignore => true,
            CsvDeserializationErrorAction.SkipRecord => false,
            CsvDeserializationErrorAction.Throw => throw exception,
            _ => throw new InvalidOperationException($"The deserialization error handler returned an invalid action ({action})."),
        };
    }

    private bool ReportConversionError<TChar>(CsvParser<TChar> parser, int column, CsvPropertyInfo property, ReadOnlySpan<TChar> value, Exception exception)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        var text = parser.GetString(value);
        return ReportError(_errorHandler, CsvDeserializationErrorKind.ConversionFailed, CreateConversionException(parser, column, property, text, exception), property.Name, text);
    }

    private static bool IsConversionException(Exception exception) => exception is FormatException or OverflowException or ArgumentException or InvalidCastException or CsvException;

    private static CsvException CreateConversionException<TChar>(CsvParser<TChar> parser, int column, CsvPropertyInfo property, string text, Exception exception)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (text.Length > 100)
        {
            text = string.Concat(text.AsSpan(0, 100), "…");
        }

        return parser.CreateException($"The value '{text}' of the column '{property.Name}' cannot be converted to '{property.PropertyType}': {exception.Message}", column, exception);
    }
}

// The modes of CsvRecordBinder<T>.TryRead: the JIT removes the code under a typeof comparison of the type arguments. They are not nested in the
// generic type: nested, they would be shared generic types when T is a reference type, and the comparison would be done at run time.
[SuppressMessage("Design", "MA0048:File name must match type name", Justification = "Implementation detail of CsvRecordBinder")]
internal readonly struct WithoutErrorHandler;

[SuppressMessage("Design", "MA0048:File name must match type name", Justification = "Implementation detail of CsvRecordBinder")]
internal readonly struct WithErrorHandler;
