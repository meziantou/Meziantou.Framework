namespace Meziantou.Framework.Csv;

/// <summary>Specifies when <see cref="CsvWriter"/> and <see cref="Utf8CsvWriter"/> quote the values they write.</summary>
public enum CsvQuoteMode
{
    /// <summary>A value is quoted when it contains the separator, the quote character, a line terminator or a character of the new line string.</summary>
    AsNeeded,

    /// <summary>Every value is quoted.</summary>
    Always,

    /// <summary>
    /// No value is quoted: values are written as they are. A value that contains the separator, the quote character or a line terminator does not read back as it was written.
    /// </summary>
    Never,
}
