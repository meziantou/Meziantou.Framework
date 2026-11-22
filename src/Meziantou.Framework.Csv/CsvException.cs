namespace Meziantou.Framework.Csv;

/// <summary>The exception thrown when CSV data is invalid or cannot be converted.</summary>
public sealed class CsvException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CsvException"/> class.</summary>
    public CsvException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CsvException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public CsvException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CsvException"/> class with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public CsvException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal CsvException(string message, long? rowIndex, long? lineNumber, long? position, int? columnIndex, string? columnName, Exception? innerException = null)
        : base(FormatMessage(message, rowIndex, lineNumber, columnIndex, columnName), innerException)
    {
        RowIndex = rowIndex;
        LineNumber = lineNumber;
        Position = position;
        ColumnIndex = columnIndex;
        ColumnName = columnName;
    }

    /// <summary>Gets the 0-based index of the record where the error occurred, not counting the header row, if known.</summary>
    public long? RowIndex { get; }

    /// <summary>Gets the 1-based line number where the record that caused the error starts, if known.</summary>
    public long? LineNumber { get; }

    /// <summary>Gets the 0-based position, in characters or in bytes from the start of the data, of the record that caused the error, if known.</summary>
    public long? Position { get; }

    /// <summary>Gets the 0-based index of the column that caused the error, if known.</summary>
    public int? ColumnIndex { get; }

    /// <summary>Gets the name of the column that caused the error, if known.</summary>
    public string? ColumnName { get; }

    private static string FormatMessage(string message, long? rowIndex, long? lineNumber, int? columnIndex, string? columnName)
    {
        var sb = new StringBuilder(message);
        var hasDetails = false;
        void AppendSeparator()
        {
            sb.Append(hasDetails ? ", " : " (");
            hasDetails = true;
        }

        if (lineNumber is not null)
        {
            AppendSeparator();
            sb.Append("line ").Append(lineNumber.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (rowIndex is not null)
        {
            AppendSeparator();
            sb.Append("row ").Append(rowIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (columnIndex is not null)
        {
            AppendSeparator();
            sb.Append("column ").Append(columnIndex.Value.ToString(CultureInfo.InvariantCulture));
            if (columnName is not null)
            {
                sb.Append(" '").Append(columnName).Append('\'');
            }
        }
        else if (columnName is not null)
        {
            AppendSeparator();
            sb.Append("column '").Append(columnName).Append('\'');
        }

        if (hasDetails)
        {
            sb.Append(')');
        }

        return sb.ToString();
    }
}
