namespace Meziantou.Framework.Csv;

/// <summary>Describes malformed data found by <see cref="CsvReader"/> or <see cref="Utf8CsvReader"/>, passed to <see cref="CsvReaderOptions.BadDataHandler"/>.</summary>
/// <remarks>The context is only valid during the call to the handler: the handler must not call the methods of the reader.</remarks>
public sealed class CsvBadDataContext
{
    private CsvException? _exception;

    internal CsvBadDataContext(CsvBadDataKind kind, string message, long? rowIndex, long lineNumber, long position, int? columnIndex, string? columnName)
    {
        Kind = kind;
        Message = message;
        RowIndex = rowIndex;
        LineNumber = lineNumber;
        Position = position;
        ColumnIndex = columnIndex;
        ColumnName = columnName;
    }

    /// <summary>Gets the kind of malformed data.</summary>
    public CsvBadDataKind Kind { get; }

    /// <summary>Gets the description of the error, without its location.</summary>
    public string Message { get; }

    /// <summary>Gets the 0-based index of the record, not counting the header row, or <see langword="null"/> when the record is the header row.</summary>
    public long? RowIndex { get; }

    /// <summary>Gets the 1-based line number where the record starts.</summary>
    public long LineNumber { get; }

    /// <summary>Gets the 0-based position, in characters or in bytes from the start of the data, where the record starts.</summary>
    public long Position { get; }

    /// <summary>Gets the 0-based index of the column that contains the malformed data, or <see langword="null"/> when the error is about the whole record.</summary>
    public int? ColumnIndex { get; }

    /// <summary>Gets the name of the column from the header row, or <see langword="null"/> when the column has no name or the header row is not read yet.</summary>
    public string? ColumnName { get; }

    /// <summary>Gets the exception that the reader throws for this error in strict mode, or when the handler returns <see cref="CsvBadDataAction.Throw"/>.</summary>
    public CsvException Exception => _exception ??= new CsvException(Message, RowIndex, LineNumber, Position, ColumnIndex, ColumnName);
}
