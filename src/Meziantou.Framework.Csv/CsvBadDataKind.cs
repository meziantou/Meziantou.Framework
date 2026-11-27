namespace Meziantou.Framework.Csv;

/// <summary>Specifies the kind of malformed data reported to <see cref="CsvReaderOptions.BadDataHandler"/>.</summary>
public enum CsvBadDataKind
{
    /// <summary>A value that is not quoted contains a quote character, such as <c>a"b</c>.</summary>
    /// <remarks>
    /// RFC 4180 requires a value that contains a quote to be quoted, with the quote doubled: <c>"a""b"</c>. In non-strict mode, or when the handler returns
    /// <see cref="CsvBadDataAction.Accept"/>, the quote is part of the value. The error often means that the value was meant to be quoted: in
    /// <c>a "b,c"</c>, the quote does not start the value, so the separator splits it into <c>a "b</c> and <c>c"</c>.
    /// </remarks>
    QuoteInUnquotedValue,

    /// <summary>The closing quote of a value is followed by characters that are not a separator or a line terminator, such as <c>"a"b</c>.</summary>
    /// <remarks>
    /// In non-strict mode, or when the handler returns <see cref="CsvBadDataAction.Accept"/>, the characters after the closing quote are part of the
    /// value: <c>"a"b</c> is <c>ab</c>. The error often means that a quote inside the value was not doubled.
    /// </remarks>
    CharactersAfterClosingQuote,

    /// <summary>A quoted value is not closed before the end of the data.</summary>
    /// <remarks>
    /// In non-strict mode, or when the handler returns <see cref="CsvBadDataAction.Accept"/>, the value ends at the end of the data, with the line
    /// terminators and the separators it contains. Skipping the record skips the rest of the data.
    /// </remarks>
    UnterminatedQuotedValue,

    /// <summary>The record does not have the expected number of columns. Only reported when <see cref="CsvReaderOptions.ValidateColumnCount"/> is <see langword="true"/>.</summary>
    ColumnCountMismatch,
}
