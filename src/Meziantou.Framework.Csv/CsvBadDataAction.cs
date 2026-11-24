namespace Meziantou.Framework.Csv;

/// <summary>Specifies what the reader does with malformed data, as returned by <see cref="CsvReaderOptions.BadDataHandler"/>.</summary>
public enum CsvBadDataAction
{
    /// <summary>The reader throws the <see cref="CsvException"/> of <see cref="CsvBadDataContext.Exception"/>, as in strict mode.</summary>
    Throw,

    /// <summary>The data is read as when <see cref="CsvReaderOptions.Strict"/> is <see langword="false"/>, and the record is returned.</summary>
    /// <remarks>
    /// A quote character inside a value that is not quoted is part of the value, the characters after a closing quote are part of the value, an unterminated
    /// quoted value ends at the end of the data, and a record with an unexpected number of columns is returned as is.
    /// </remarks>
    Accept,

    /// <summary>The record is not returned, and the reader continues with the next record.</summary>
    /// <remarks>
    /// The record still counts in the row indexes: the next record has the index it would have had. Once a record is skipped, the other errors of the same
    /// record are not reported. Skipping the header row makes the next record the header row.
    /// </remarks>
    Skip,
}
