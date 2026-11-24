namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Describes an error of <see cref="CsvSerializer"/> while it converts records to objects, passed to <see cref="CsvSerializerOptions.DeserializationErrorHandler"/>.</summary>
public sealed class CsvDeserializationErrorContext
{
    internal CsvDeserializationErrorContext(CsvDeserializationErrorKind kind, Type type, string? memberName, string? rawValue, CsvException exception)
    {
        Kind = kind;
        Type = type;
        MemberName = memberName;
        RawValue = rawValue;
        Exception = exception;
    }

    /// <summary>Gets the kind of error.</summary>
    public CsvDeserializationErrorKind Kind { get; }

    /// <summary>Gets the type of the objects being read.</summary>
    public Type Type { get; }

    /// <summary>Gets the column name of the member, as the type defines it, or <see langword="null"/> when the column has no member.</summary>
    /// <remarks>
    /// When <see cref="CsvSerializerOptions.PropertyNameCaseInsensitive"/> is <see langword="true"/>, the header row can write it differently: <see cref="ColumnName"/>
    /// is the name as written in the header row.
    /// </remarks>
    public string? MemberName { get; }

    /// <summary>Gets the value that cannot be converted, for <see cref="CsvDeserializationErrorKind.ConversionFailed"/>; otherwise, <see langword="null"/>.</summary>
    public string? RawValue { get; }

    /// <summary>Gets the 0-based index of the record, not counting the header row.</summary>
    /// <remarks>The header row is checked when the first record is read: an error of the header row has the index of the first record.</remarks>
    public long? RowIndex => Exception.RowIndex;

    /// <summary>Gets the 1-based line number where the record starts.</summary>
    public long? LineNumber => Exception.LineNumber;

    /// <summary>Gets the 0-based position, in characters or in bytes from the start of the data, where the record starts.</summary>
    public long? Position => Exception.Position;

    /// <summary>Gets the 0-based index of the column, or <see langword="null"/> when the error is not about one column.</summary>
    public int? ColumnIndex => Exception.ColumnIndex;

    /// <summary>Gets the name of the column from the header row, or <see langword="null"/> when the data has no header row or the error is not about one column.</summary>
    public string? ColumnName => Exception.ColumnName;

    /// <summary>Gets the exception that the serializer throws for this error when the handler returns <see cref="CsvDeserializationErrorAction.Throw"/>, or when there is no handler.</summary>
    public CsvException Exception { get; }
}
